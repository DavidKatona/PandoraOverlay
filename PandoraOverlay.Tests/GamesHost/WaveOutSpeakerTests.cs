using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using PandoraOverlay.Games;
using Xunit;
using Xunit.Abstractions;

namespace PandoraOverlay.Tests;

/// <summary>
/// The speaker (<see cref="WaveOutSpeaker"/>; A80, A88) over a device that
/// records (<see cref="FakeWaveDevice"/>): lazy; the first play opens on the
/// speaker's own thread and never makes the caller wait; the written stream
/// is the mix of what was asked; the device closes and the thread ends after
/// the idle time, and the next play opens again; a stop fades what plays at
/// the call and nothing after it, and does nothing with no thread; a failed
/// open or write and an exception are contained, logged once and not retried
/// within the retry time; Dispose joins for a bounded time and is idempotent.
/// Every wait is on a condition with seconds of patience, never a fixed sleep
/// that a slow PC could outrun. The one test that reaches the real device is
/// opt-in (<see cref="ProbeVariable"/>) and plays only silence.
/// </summary>
public sealed class WaveOutSpeakerTests
{
    /// <summary>Set to 1, the probe test opens the PC's real device and plays half a second of zeros; unset (CI, every normal run), it returns at once.</summary>
    public const string ProbeVariable = "PANDORA_GAMES_SOUND_PROBE";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    /// <summary>An idle or retry time no test outlives: the stream stays open, a failure stays fresh.</summary>
    private static readonly TimeSpan Never = TimeSpan.FromMinutes(5);

    private readonly ITestOutputHelper _output;

    public WaveOutSpeakerTests(ITestOutputHelper output) => _output = output;

    private static void Until(Func<bool> condition, string what) =>
        Assert.True(SpinWait.SpinUntil(condition, Patience), $"waited {Patience.TotalSeconds} s for {what}");

    private static WaveOutSpeaker Speaker(FakeWaveDevice device, SpeakerLog log, TimeSpan? idle = null, TimeSpan? retry = null) =>
        new(() => device, idle ?? Never, retry ?? Never, log.Write);

    private static short[] Constant(short value, int length) => Enumerable.Repeat(value, length).ToArray();

    /// <summary>A sound whose every sample tells where it is: a saw from −14,000 up by 300.</summary>
    private static short[] Saw(int length) => Enumerable.Range(0, length).Select(i => (short)((i % 97) * 300 - 14_000)).ToArray();

    /// <summary>A constant voice of this value faded from its first sample: the k-th at (219 − k)/220 of it, within one for the rounding.</summary>
    private static void AssertFadedFromItsStart(short[] stream, int start, short value)
    {
        for (var k = 0; k < SpeakerMix.FadeSamples; k++)
        {
            var expected = value * (SpeakerMix.FadeSamples - 1 - k) / (double)SpeakerMix.FadeSamples;
            Assert.InRange(stream[start + k], expected - 1, expected + 1);
        }
        Assert.All(stream[(start + SpeakerMix.FadeSamples)..], s => Assert.Equal(0, s));
    }

    // ---- Lazy ---------------------------------------------------------------------------------

    [Fact]
    public void ConstructingItCallsNothing()
    {
        var made = 0;
        var device = new FakeWaveDevice();
        var speaker = new WaveOutSpeaker(() => { made++; return device; }, Never, Never, new SpeakerLog().Write);

        Assert.Equal(0, made);
        Assert.Empty(device.Calls);
        Assert.False(speaker.HasThread);
        Assert.Null(speaker.LastThread);

        speaker.Dispose(); // never used: nothing to close, nothing to join
        Assert.Equal(0, made);
        Assert.Empty(device.Calls);

        using var real = new WaveOutSpeaker(); // the real one, as MainWindow makes it: no device, no thread
        Assert.False(real.HasThread);
        Assert.Null(real.LastThread);
    }

    // ---- The stream ---------------------------------------------------------------------------

    [Fact]
    public void TheFirstPlayOpensOnceOnItsOwnThreadAndTheBuffersCarryTheSamplesTimesTheGain()
    {
        var log = new SpeakerLog();
        var device = new FakeWaveDevice();
        using var speaker = Speaker(device, log);
        var samples = Saw(1000);

        speaker.Play(samples, 0.5f);
        Until(() => device.Writes >= 8, "the sound and the silence after it written");

        var thread = speaker.LastThread!;
        Assert.True(thread.IsBackground);
        Assert.Equal(ThreadPriority.AboveNormal, thread.Priority);
        Assert.Equal("Games sound", thread.Name);
        Assert.Equal(new[] { WaveOutSpeaker.ThreadName }, device.ThreadNames); // the device is the speaker's thread's alone
        Assert.Equal(1, device.Opens);
        Assert.Equal((WaveOutSpeaker.Buffers, WaveOutSpeaker.BufferSamples), device.OpenedWith);
        Assert.Equal(new[] { "open", "next", "buffer", "write", "next", "buffer", "write" }, device.Calls.Take(7));

        var stream = device.WrittenFrom(0);
        Assert.Equal(samples.Select(s => SpeakerMix.ToSample(s * 0.5f)), stream[..1000]); // from the first sample of the first buffer
        Assert.All(stream[1000..], s => Assert.Equal(0, s));
        Assert.Empty(log.At(LogVerbosity.Warning));
    }

    [Fact]
    public void TwoPlaysOverlapAsTheirSum()
    {
        var device = new FakeWaveDevice { OpenDelay = Patience }; // held in its open until the test lets it go
        using var speaker = Speaker(device, new SpeakerLog());
        var a = Saw(1000);
        var b = Constant(3001, 600);

        speaker.Play(a, 1f);
        speaker.Play(b, 0.5f);
        device.OpenRelease.Set();
        Until(() => device.Writes >= 8, "both sounds written");

        var stream = device.WrittenFrom(0);
        var expected = Enumerable.Range(0, 1000).Select(i => SpeakerMix.ToSample(a[i] + (i < b.Length ? b[i] * 0.5f : 0)));
        Assert.Equal(expected, stream[..1000]);
        Assert.Equal(1, device.Opens);
    }

    [Fact]
    public void PlayReturnsAtOnceWhileTheDeviceTakesLongToOpen()
    {
        var device = new FakeWaveDevice { OpenDelay = Patience }; // an open that takes as long as the test likes
        using var speaker = Speaker(device, new SpeakerLog());

        var clock = Stopwatch.StartNew();
        speaker.Play(Constant(1000, 441), 1f);
        speaker.StopAll();
        speaker.Play(Constant(1000, 441), 1f);
        var took = clock.Elapsed;

        Assert.Equal(0, device.OpensEnded); // the open is still under way: the calls did not wait for it
        Assert.True(took < TimeSpan.FromSeconds(1), $"the calls took {took.TotalMilliseconds} ms");
        Until(() => device.Opens == 1, "the thread's open begun");
        Assert.True(speaker.HasThread);

        device.OpenRelease.Set();
        Until(() => device.Writes >= WaveOutSpeaker.Buffers, "the stream started once the open ended");
    }

    [Fact]
    public void AfterTheLastSoundTheDeviceClosesAndTheThreadEndsAndTheNextPlayOpensAgain()
    {
        var log = new SpeakerLog();
        var device = new FakeWaveDevice();
        var idle = TimeSpan.FromMilliseconds(150);
        using var speaker = Speaker(device, log, idle);

        var clock = Stopwatch.StartNew();
        speaker.Play(Constant(1000, 441), 1f);
        var first = speaker.LastThread!;
        Until(() => device.Closes == 1, "the idle device closed");
        Assert.True(clock.Elapsed >= idle, "closed before its idle time");
        Until(() => !first.IsAlive, "the thread ended");
        Assert.False(speaker.HasThread);
        Assert.Equal(1, device.Opens);
        Assert.Equal("close", device.Calls[^1]); // nothing touched the device after its close

        var mark = device.Written.Count;
        speaker.Play(Constant(2000, 441), 1f);
        Until(() => device.Opens == 2 && device.Written.Count >= mark + WaveOutSpeaker.Buffers, "the second stream");

        Assert.NotSame(first, speaker.LastThread);
        Assert.All(device.WrittenFrom(mark)[..441], s => Assert.Equal(2000, s));
        Assert.Single(log.At(LogVerbosity.Log), l => l.StartsWith("Games sound: the device opened in ", StringComparison.Ordinal)); // the launch's first open only
        Assert.Empty(log.At(LogVerbosity.Warning));
        Until(() => device.Closes == 2, "the second stream closed too");
    }

    // ---- A stop (A88) -------------------------------------------------------------------------

    [Fact]
    public void AStopFadesWhatPlaysWithNoJump()
    {
        var device = new FakeWaveDevice();
        using var speaker = Speaker(device, new SpeakerLog());

        speaker.Play(Constant(10_000, GameSound.MaxSamples), 1f);
        Until(() => device.Writes >= 8, "the sound under way");
        speaker.StopAll();
        var stoppedAt = device.Writes;
        Until(() => device.Writes >= stoppedAt + 6, "the fade written");

        var stream = device.WrittenFrom(0);
        var fade = Array.FindIndex(stream, s => s != 10_000);
        Assert.True(fade >= 8 * WaveOutSpeaker.BufferSamples, "the fade began before the stop");
        Assert.Equal(0, fade % WaveOutSpeaker.BufferSamples); // at the first buffer filled after the stop
        AssertFadedFromItsStart(stream, fade, 10_000);
        for (var i = 1; i < stream.Length; i++)
        {
            Assert.True(stream[i] <= stream[i - 1], $"a step up at {i}");
            Assert.True(stream[i - 1] - stream[i] <= 10_000 / SpeakerMix.FadeSamples + 1, $"a jump at {i}");
        }
    }

    [Fact]
    public void AStopWithNoThreadCallsNothingAndAPlayAfterItIsHeardWhole()
    {
        var made = 0;
        var device = new FakeWaveDevice();
        using var speaker = new WaveOutSpeaker(() => { made++; return device; }, Never, Never, new SpeakerLog().Write);

        speaker.StopAll();
        speaker.StopAll();

        Assert.Equal(0, made);
        Assert.Empty(device.Calls);
        Assert.False(speaker.HasThread);
        Assert.Null(speaker.LastThread);
        Assert.Equal(0, speaker.Queued); // nothing kept for later

        speaker.Play(Constant(10_000, 600), 1f);
        Until(() => device.Writes >= WaveOutSpeaker.Buffers, "the sound written");
        var stream = device.WrittenFrom(0);
        Assert.All(stream[..600], s => Assert.Equal(10_000, s));
        Assert.All(stream[600..], s => Assert.Equal(0, s));
    }

    [Fact]
    public void AStopAfterTheThreadEndedCallsNothingAndAPlayAfterItIsHeardWhole()
    {
        var device = new FakeWaveDevice();
        using var speaker = Speaker(device, new SpeakerLog(), idle: TimeSpan.FromMilliseconds(100));
        speaker.Play(Constant(1000, 441), 1f);
        var first = speaker.LastThread!;
        Until(() => device.Closes == 1 && !first.IsAlive, "the first stream ended");
        var calls = device.Calls.Count;

        speaker.StopAll();

        Assert.Equal(calls, device.Calls.Count);
        Assert.Same(first, speaker.LastThread); // no thread started
        Assert.False(speaker.HasThread);
        Assert.Equal(0, speaker.Queued);

        var mark = device.Written.Count;
        speaker.Play(Constant(10_000, 600), 1f);
        Until(() => device.Written.Count >= mark + WaveOutSpeaker.Buffers, "the second sound written");
        Assert.All(device.WrittenFrom(mark)[..600], s => Assert.Equal(10_000, s));
    }

    [Fact]
    public void APlayThenAStopInsideOneWaitFadesThatSound()
    {
        var device = new FakeWaveDevice();
        using var speaker = Speaker(device, new SpeakerLog());
        speaker.Play(new short[1], 1f); // a thread running: one silent sample
        Until(() => device.Writes >= WaveOutSpeaker.Buffers, "the stream under way");

        device.Hold();
        Until(() => device.Waiting, "the thread held inside a wait");
        var mark = device.Written.Count;
        speaker.Play(Constant(10_000, 1000), 1f);
        speaker.StopAll();
        device.Release();
        Until(() => device.Written.Count >= mark + WaveOutSpeaker.Buffers, "the next buffers written");

        AssertFadedFromItsStart(device.WrittenFrom(mark), 0, 10_000); // the play came before the stop
    }

    [Fact]
    public void AStopThenAPlayInsideOneWaitPlaysItWhole()
    {
        var device = new FakeWaveDevice();
        using var speaker = Speaker(device, new SpeakerLog());
        speaker.Play(new short[1], 1f);
        Until(() => device.Writes >= WaveOutSpeaker.Buffers, "the stream under way");

        device.Hold();
        Until(() => device.Waiting, "the thread held inside a wait");
        var mark = device.Written.Count;
        speaker.StopAll();
        speaker.Play(Constant(10_000, 1000), 1f);
        device.Release();
        Until(() => device.Written.Count >= mark + WaveOutSpeaker.Buffers, "the next buffers written");

        var stream = device.WrittenFrom(mark);
        Assert.All(stream[..1000], s => Assert.Equal(10_000, s)); // a stop never fades a play after it (A88)
        Assert.All(stream[1000..], s => Assert.Equal(0, s));
    }

    // ---- The queue ----------------------------------------------------------------------------

    [Fact]
    public void TheQueueHoldsAtMostItsCapAndTwoStopsInARowAreOne()
    {
        var device = new FakeWaveDevice { OpenDelay = Patience };
        using var speaker = Speaker(device, new SpeakerLog());

        for (var i = 0; i < WaveOutSpeaker.MaxQueued + 10; i++) speaker.Play(Constant(100, 441), 0.1f);
        Assert.Equal(WaveOutSpeaker.MaxQueued, speaker.Queued);
        speaker.StopAll();
        speaker.StopAll();
        speaker.StopAll();
        Assert.Equal(WaveOutSpeaker.MaxQueued + 1, speaker.Queued);

        device.OpenRelease.Set();
        Until(() => speaker.Queued == 0, "the queue moved into the mix");
    }

    // ---- Failures -----------------------------------------------------------------------------

    [Fact]
    public void AFailedOpenLogsOneWarningAndIsNotTriedAgainWithinTheRetryTime()
    {
        var log = new SpeakerLog();
        var device = new FakeWaveDevice { OpenCode = 2 }; // MMSYSERR_BADDEVICEID: no device
        var retry = TimeSpan.FromSeconds(1);
        using var speaker = Speaker(device, log, retry: retry);
        const string Refused = "Games sound: the device could not be opened (code 2)";

        speaker.Play(Constant(1000, 441), 1f); // never throws
        var first = speaker.LastThread!;
        Until(() => !speaker.HasThread && device.Closes == 1 && !first.IsAlive, "the failed stream ended");
        Assert.Equal(new[] { Refused }, log.At(LogVerbosity.Warning));

        speaker.Play(Constant(1000, 441), 1f); // within the retry time: dropped
        Assert.Same(first, speaker.LastThread);
        Assert.False(speaker.HasThread);
        Assert.Equal(1, device.Opens);

        Thread.Sleep(retry + TimeSpan.FromMilliseconds(400));
        speaker.Play(Constant(1000, 441), 1f); // after it: tried again
        Until(() => device.Opens == 2 && device.Closes == 2 && !speaker.LastThread!.IsAlive, "the second try ended");
        Assert.Equal(new[] { Refused }, log.At(LogVerbosity.Warning)); // still one Warning
        Assert.Equal(new[] { Refused }, log.At(LogVerbosity.Verbose)); // the same again, at Verbose

        device.OpenCode = 0;
        Thread.Sleep(retry + TimeSpan.FromMilliseconds(400));
        var mark = device.Written.Count;
        speaker.Play(Constant(10_000, 441), 1f);
        Until(() => log.At(LogVerbosity.Log).Contains("Games sound: the device works again"), "the device working again");
        Assert.Equal(3, device.Opens);
        Assert.Single(log.At(LogVerbosity.Log), l => l.StartsWith("Games sound: the device opened in ", StringComparison.Ordinal));
        Assert.Single(log.At(LogVerbosity.Warning));
        Assert.All(device.WrittenFrom(mark)[..441], s => Assert.Equal(10_000, s));
    }

    [Fact]
    public void AFailedWriteClosesTheDeviceAndLogs()
    {
        var log = new SpeakerLog();
        var device = new FakeWaveDevice { FailWriteNumber = 12, WriteCode = 6 }; // unplugged mid-sound, after the first buffers
        using var speaker = Speaker(device, log);

        speaker.Play(Constant(10_000, GameSound.MaxSamples), 1f);
        var thread = speaker.LastThread!;
        Until(() => device.Closes == 1 && !thread.IsAlive, "the failed stream ended");

        Assert.False(speaker.HasThread);
        Assert.Equal(new[] { "Games sound: the device could not be written to (code 6)" }, log.At(LogVerbosity.Warning));
        Assert.Equal(11, device.Written.Count);
        Assert.Equal(new[] { "write", "close" }, device.Calls.TakeLast(2)); // the failed write, then the close, and nothing more
        speaker.Play(Constant(10_000, 441), 1f); // within the retry time: dropped
        Assert.Equal(1, device.Opens);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("wait")]
    [InlineData("write")]
    public void AnExceptionFromTheDeviceIsContained(string member)
    {
        var unhandled = new ConcurrentQueue<object>();
        UnhandledExceptionEventHandler handler = (_, e) => unhandled.Enqueue(e.ExceptionObject);
        AppDomain.CurrentDomain.UnhandledException += handler;
        try
        {
            var log = new SpeakerLog();
            var device = new FakeWaveDevice { ThrowIn = member };
            using var speaker = Speaker(device, log);

            speaker.Play(Constant(10_000, 4410), 1f);
            var thread = speaker.LastThread!;
            Until(() => !thread.IsAlive, "the thread ended");

            Assert.Equal(1, device.Closes);
            Assert.False(speaker.HasThread);
            var warning = Assert.Single(log.At(LogVerbosity.Warning));
            Assert.StartsWith("Games sound failed: InvalidOperationException", warning, StringComparison.Ordinal);
            Assert.DoesNotContain(log.Lines, l => l.Line.Contains(FakeWaveDevice.SecretMessage, StringComparison.Ordinal)); // its type and stack, never its message
            Assert.Empty(unhandled);
        }
        finally
        {
            AppDomain.CurrentDomain.UnhandledException -= handler;
        }
    }

    [Fact]
    public void AnExceptionFromTheCloseIsContainedAndTheNextPlayOpensAgain()
    {
        var unhandled = new ConcurrentQueue<object>();
        UnhandledExceptionEventHandler handler = (_, e) => unhandled.Enqueue(e.ExceptionObject);
        AppDomain.CurrentDomain.UnhandledException += handler;
        try
        {
            var log = new SpeakerLog();
            var device = new FakeWaveDevice { ThrowIn = "close" };
            using var speaker = Speaker(device, log, idle: TimeSpan.FromMilliseconds(100));

            speaker.Play(Constant(10_000, 441), 1f);
            var first = speaker.LastThread!;
            Until(() => device.Closes == 1 && !first.IsAlive, "the first stream ended");
            Assert.False(speaker.HasThread);
            Assert.Equal(new[] { "Games sound: InvalidOperationException on the way out" }, log.At(LogVerbosity.Warning));

            speaker.Play(Constant(10_000, 441), 1f); // the stream had ended as it should: no wait for a retry
            Until(() => device.Opens == 2 && device.Closes == 2 && !speaker.LastThread!.IsAlive, "the second stream ended");
            // The device worked in between (said once), so the second throw is a first
            // failure again, not "the same again".
            Assert.Equal(new[] { "Games sound: the device works again" }, log.At(LogVerbosity.Log).Where(l => l.Contains("again", StringComparison.Ordinal)));
            Assert.Equal(2, log.At(LogVerbosity.Warning).Length);
            Assert.All(log.At(LogVerbosity.Warning), w => Assert.Equal("Games sound: InvalidOperationException on the way out", w));
            Assert.Empty(unhandled);
        }
        finally
        {
            AppDomain.CurrentDomain.UnhandledException -= handler;
        }
    }

    // ---- Dispose ------------------------------------------------------------------------------

    [Fact]
    public void DisposeClosesJoinsAndIsIdempotentAndAPlayAfterItDoesNothing()
    {
        var log = new SpeakerLog();
        var device = new FakeWaveDevice();
        var speaker = Speaker(device, log);
        speaker.Play(Constant(10_000, GameSound.MaxSamples), 1f);
        Until(() => device.Writes >= 6, "the sound under way");
        var thread = speaker.LastThread!;

        speaker.Dispose();
        Until(() => !thread.IsAlive, "the thread joined");
        Assert.Equal(1, device.Closes);
        Assert.Equal("close", device.Calls[^1]);

        speaker.Dispose(); // idempotent, and never throws
        speaker.Play(Constant(10_000, 441), 1f);
        speaker.StopAll();
        Assert.Equal(1, device.Opens);
        Assert.Same(thread, speaker.LastThread);
        Assert.False(speaker.HasThread);
        Assert.Equal(0, speaker.Queued);
        Assert.Empty(log.At(LogVerbosity.Warning));
    }

    [Fact]
    public void DisposeNeverWaitsLongerThanItsJoin()
    {
        var device = new FakeWaveDevice { CloseDelay = TimeSpan.FromSeconds(3) }; // a driver slow to close
        var speaker = Speaker(device, new SpeakerLog());
        speaker.Play(Constant(10_000, 441), 1f);
        Until(() => device.Writes >= WaveOutSpeaker.Buffers, "the stream under way");
        var thread = speaker.LastThread!;

        var clock = Stopwatch.StartNew();
        speaker.Dispose();
        var took = clock.Elapsed;

        Assert.True(took < TimeSpan.FromSeconds(2), $"Dispose took {took.TotalMilliseconds} ms; its join is {WaveOutSpeaker.JoinMilliseconds}");
        Until(() => !thread.IsAlive, "the slow close finished on its own thread");
    }

    // ---- The end of a thread, against a play --------------------------------------------------

    /// <summary>
    /// The thread's leaving and its "no thread" flag are one step under the
    /// lock, after its last look at the queue, so a play either reaches the
    /// running thread or starts a new one, never neither: with an idle time of
    /// a millisecond the thread leaves after every sound, and each play lands
    /// at a random moment of that (still running, deciding, closing, or gone).
    /// Every sound is a single sample of its own value, so none can sum with
    /// another, and every one must reach the device.
    /// </summary>
    [Fact]
    public void APlayAsTheThreadLeavesIsNeverLost()
    {
        var log = new SpeakerLog();
        var device = new FakeWaveDevice { WaitSleep = 1 };
        using var speaker = Speaker(device, log, idle: TimeSpan.FromMilliseconds(1));
        var random = new Random(26);

        for (var k = 0; k < 150; k++)
        {
            var marker = (short)(k + 1);
            var from = Math.Max(0, device.Written.Count - 1);
            speaker.Play(new[] { marker }, 1f);
            Until(() => device.Written.Skip(from).Any(b => Array.IndexOf(b, marker) >= 0), $"sound {marker} reaching the device");
            Thread.Sleep(random.Next(0, 4));
        }

        Assert.True(device.Opens > 1, "the thread never left: the race was not run");
        Assert.Equal(0, device.Overlaps); // two streams never on the device at once
        Assert.Empty(log.At(LogVerbosity.Warning));
        _output.WriteLine($"150 sounds, {device.Opens} streams");
    }

    // ---- The real device, opt-in --------------------------------------------------------------

    /// <summary>
    /// The real speaker on the PC's real default device, only when
    /// <see cref="ProbeVariable"/> is 1 (CI has no audio device; a normal run
    /// returns at once). It plays SILENCE only (every sample 0), so nothing
    /// is heard: half a second, then a tenth of a second while the stream
    /// runs. It asserts no Warning and the device closed after its idle time,
    /// and prints the times: from a play to the open, to the device, the
    /// buffers queued, and the thread's start and end.
    /// </summary>
    [Fact]
    public void TheRealDevicePlaysSilenceAndClosesAfterItsIdleTime()
    {
        if (Environment.GetEnvironmentVariable(ProbeVariable) != "1") return;

        var log = new SpeakerLog();
        var clock = Stopwatch.StartNew();
        TimingDevice? timing = null;
        var speaker = new WaveOutSpeaker(() => timing = new TimingDevice(clock), write: log.Write);
        try
        {
            var played = clock.Elapsed;
            speaker.Play(new short[GameSound.SampleRate / 2], 1f); // half a second of zeros
            var returned = clock.Elapsed;
            var thread = speaker.LastThread!;
            Until(() => Volatile.Read(ref timing) is { } t && t.Reached(WaveOutSpeaker.BufferSamples) is not null, "the device through its first buffer");

            Thread.Sleep(250);
            var device = Volatile.Read(ref timing)!;
            var writesAtSecond = device.Writes;
            var second = clock.Elapsed;
            speaker.Play(new short[GameSound.SampleRate / 10], 1f); // a tenth of a second of zeros, while the stream runs

            Until(() => device.Closed, "the device closed after its idle time");
            Until(() => !thread.IsAlive, "the thread ended");
            var ended = clock.Elapsed;

            Assert.DoesNotContain(log.Lines, l => l.Level <= LogVerbosity.Warning);
            Assert.True(device.CloseStarted - second >= WaveOutSpeaker.IdleClose, "closed before its idle time");

            string Ms(TimeSpan? t) => t is { } v ? v.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture) : "never";
            var firstEnd = device.Reached(WaveOutSpeaker.BufferSamples);
            var secondEarliest = device.Reached((long)writesAtSecond * WaveOutSpeaker.BufferSamples) - second;
            var secondLatest = device.Reached((long)(writesAtSecond + 1) * WaveOutSpeaker.BufferSamples) - second;
            _output.WriteLine("The real device, silence only (every sample 0):");
            _output.WriteLine($"  Play returned after {Ms(returned - played)} ms");
            _output.WriteLine($"  the thread made its device {Ms(device.Made - played)} ms after Play");
            _output.WriteLine($"  the open: from {Ms(device.OpenStarted - played)} to {Ms(device.OpenEnded - played)} ms after Play ({Ms(device.OpenEnded - device.OpenStarted)} ms)");
            _output.WriteLine($"  the first buffer written {Ms(device.FirstWrite - played)} ms after Play; the device through it (sample {WaveOutSpeaker.BufferSamples}) at {Ms(firstEnd - played)} ms, so the sound's first sample played at about {Ms(firstEnd - played - TimeSpan.FromMilliseconds(10))} ms");
            _output.WriteLine($"  a play while the stream ran reached the device after {Ms(secondEarliest)} ms (its buffer the next one filled) or {Ms(secondLatest)} ms (the one after)");
            _output.WriteLine($"  buffers written {device.Writes}; fewest queued at the device at a wake {device.FewestQueued.ToString("0.00", CultureInfo.InvariantCulture)} of {WaveOutSpeaker.Buffers}; empty {device.Empty} times");
            _output.WriteLine($"  the close: began {Ms(device.CloseStarted - second)} ms after the second play (idle {WaveOutSpeaker.IdleClose.TotalSeconds} s), took {Ms(device.CloseEnded - device.CloseStarted)} ms");
            _output.WriteLine($"  the thread ended by {Ms(ended - played)} ms after the first Play");
            foreach (var (level, line) in log.Lines) _output.WriteLine($"  log {level}: {line}");
        }
        finally
        {
            speaker.Dispose();
        }
    }

    /// <summary>
    /// The real device with a stopwatch on each call, for the probe: the
    /// open, the writes, the close, and at each wake the device's position
    /// (read on the speaker's thread, inside its wait), which crosses a
    /// buffer's end just as the device hands that buffer back.
    /// </summary>
    private sealed class TimingDevice : IWaveDevice
    {
        private readonly WinmmDevice _inner = new();
        private readonly Stopwatch _clock;
        private readonly List<(TimeSpan At, long Position)> _positions = new();
        private int _samples;
        private int _writes;
        private volatile bool _closed;

        public TimingDevice(Stopwatch clock)
        {
            _clock = clock;
            Made = clock.Elapsed;
        }

        public TimeSpan Made { get; }

        public TimeSpan OpenStarted { get; private set; }

        public TimeSpan OpenEnded { get; private set; }

        public TimeSpan? FirstWrite { get; private set; }

        public TimeSpan CloseStarted { get; private set; }

        public TimeSpan CloseEnded { get; private set; }

        public int Writes => Volatile.Read(ref _writes);

        public bool Closed => _closed;

        /// <summary>The fewest buffers still queued at the device, seen at a wake once every buffer had been written.</summary>
        public double FewestQueued { get; private set; } = double.MaxValue;

        /// <summary>Wakes at which the device had nothing queued: a gap in the sound.</summary>
        public int Empty { get; private set; }

        /// <summary>When the device's position was first seen at or past this sample.</summary>
        public TimeSpan? Reached(long sample)
        {
            lock (_positions)
            {
                foreach (var (at, position) in _positions)
                {
                    if (position >= sample) return at;
                }
            }
            return null;
        }

        public int Open(int buffers, int samples)
        {
            OpenStarted = _clock.Elapsed;
            var code = _inner.Open(buffers, samples);
            OpenEnded = _clock.Elapsed;
            _samples = samples;
            return code;
        }

        public void Wait(int milliseconds)
        {
            _inner.Wait(milliseconds);
            var position = _inner.Position();
            var at = _clock.Elapsed;
            if (position < 0) return;
            lock (_positions) _positions.Add((at, position));
            if (Writes >= WaveOutSpeaker.Buffers)
            {
                var queued = (Writes * (double)_samples - position) / _samples;
                FewestQueued = Math.Min(FewestQueued, queued);
                if (queued <= 0) Empty++;
            }
        }

        public int NextFree() => _inner.NextFree();

        public Span<short> Buffer(int index) => _inner.Buffer(index);

        public int Write(int index)
        {
            var code = _inner.Write(index);
            if (code == 0)
            {
                FirstWrite ??= _clock.Elapsed;
                Interlocked.Increment(ref _writes);
            }
            return code;
        }

        public void Close()
        {
            CloseStarted = _clock.Elapsed;
            _inner.Close();
            CloseEnded = _clock.Elapsed;
            _closed = true;
        }
    }

    /// <summary>The speaker's log lines, collected from any thread, every level kept.</summary>
    private sealed class SpeakerLog
    {
        private readonly ConcurrentQueue<(LogVerbosity Level, string Line)> _lines = new();

        public void Write(LogVerbosity level, string line) => _lines.Enqueue((level, line));

        public IReadOnlyList<(LogVerbosity Level, string Line)> Lines => _lines.ToArray();

        public string[] At(LogVerbosity level) => _lines.Where(l => l.Level == level).Select(l => l.Line).ToArray();
    }
}

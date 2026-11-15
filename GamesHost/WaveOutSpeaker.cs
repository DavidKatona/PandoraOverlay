using System.Diagnostics;
using System.Globalization;

namespace PandoraOverlay;

/// <summary>
/// The Games window's speaker (G26, A80): every sound the gate
/// (<see cref="GamesSound"/>) passes on, played on the PC's default audio
/// device through winmm's <c>waveOut</c>, ONE stream, mixed by our own code
/// (<see cref="SpeakerMix"/>) on a thread of the host's own that exists only
/// while something plays (<c>PLAN-SOUND.md</c> 2.2). Measured on this PC by
/// the planner's spike: 40 to 42 ms from a play to the device with five
/// buffers of 10 ms, with every core at full load too, and never a gap; a
/// handle per sound would cost 17 ms to open and 24 to close on the calling
/// thread, a hitch at every sound.
/// <para>
/// <b>The UI thread never waits for the device.</b> <see cref="Play"/> and
/// <see cref="StopAll"/> (the UI thread's, the gate's) only queue a request
/// under the lock, and the first <see cref="Play"/> with no thread starts one;
/// they return at once and never throw. The lock guards the queue, the
/// "a thread is running" flag and the failure's time, nothing else: the
/// device is touched by the speaker's thread alone, never under the lock.
/// </para>
/// <para>
/// <b>Lazy.</b> Constructing it opens nothing and starts nothing:
/// MainWindow makes one in its constructor, and the tests and the
/// golden-render harness build MainWindow unshown, so they must never open a
/// device. The first sound starts the thread, which opens the device itself.
/// </para>
/// <para>
/// <b>The thread</b> (background, AboveNormal, "Games sound"): it joins the
/// thread before it, if that one is still closing its device (so two streams
/// never overlap for long), opens the device (<see cref="Buffers"/> buffers of
/// <see cref="BufferSamples"/>), fills and writes every buffer, then loops:
/// it waits for the device to hand a buffer back (at most
/// <see cref="WaitMilliseconds"/>), and for each free buffer moves what was
/// queued into the mix, under the lock, fills it and writes it. Once the mix
/// has been empty for <see cref="IdleClose"/> and nothing new is queued, it
/// marks "no thread" under the lock, in the same step as that last look at
/// the queue, and leaves; its device is closed on the way out. So a
/// <see cref="Play"/> either reaches the running thread or starts a new one,
/// never neither, and the device is open only while sounds come: a stream
/// left open keeps an audio session busy and can hold off the PC's sleep. The
/// next sound opens Windows' default device again, so a headset plugged in
/// between two turns is used.
/// </para>
/// <para>
/// <b>A stop means what plays at the call</b> (A88). The gate calls
/// <see cref="StopAll"/> at every stage at which nobody, or another instance,
/// is heard (the grid, the warning, the window's close, a game shown), so
/// mostly with nothing playing. With no thread running it does NOTHING:
/// nothing queued, no thread, no device. With one running, the queue keeps
/// the calls' order: a stop fades the voices in the mix and those of the
/// plays queued before it, never one queued after it, so a play right after a
/// stop is heard whole. A stop kept for later would fade the next sound.
/// </para>
/// <para>
/// <b>Fail soft</b> (A80). A device that can't be opened (none, or one in
/// exclusive use) or written to (unplugged mid-sound), or any exception on the
/// thread: the voices dropped, the device closed, the failure's time kept,
/// and no new thread within <see cref="RetryAfter"/> (a sound asked for
/// meanwhile is dropped), so a missing device costs one try every few seconds
/// while a game plays, not one per sound. The first failure since the launch,
/// or since the device last worked, is a Warning in the games' log (the step
/// and winmm's code, or the exception's type and stack, never its message:
/// constraint 4); the same again at Verbose; a success after a failure one
/// Log line (A90). An exception never leaves the thread: unhandled on a thread of
/// our own it would end the process (A50 is about the UI thread's handlers;
/// this one is ours to contain, like a failed request).
/// </para>
/// <para>
/// <b>Never</b>: the call that sets a handle's volume (it is the overlay's
/// level in Windows' mixer, the player's: A80, a scan holds it), a timer, the
/// thread pool, a process, anything of the game. The device is the Windows
/// audio engine's default endpoint, which mixes The Isle's sound and ours as
/// it mixes any app's (constraint 1, A86). The thread's timing is a
/// <see cref="Stopwatch"/>: the host is not bound by the games' list.
/// </para>
/// </summary>
internal sealed class WaveOutSpeaker : ISpeaker
{
    /// <summary>A buffer: 441 samples, 10 ms. The spike's 4 x 441 reached the device in 30 to 34 ms, 3 x 882 in 40 to 53: smaller buffers, steadier latency.</summary>
    public const int BufferSamples = 441;

    /// <summary>Buffers queued at the device: 5, 40 ms ahead of what plays. On a thread of its own at AboveNormal the spike's 5 never fell under 4 queued with every core at full load (40 to 42 ms from a play to the device), and The Isle is not a spin loop.</summary>
    public const int Buffers = 5;

    /// <summary>The device closed and the thread gone this long after the last sound: a burst of a turn's cues keeps one stream, an idle window holds none.</summary>
    public static readonly TimeSpan IdleClose = TimeSpan.FromSeconds(3);

    /// <summary>No new try within this of a failure: a missing device is tried once every few seconds while a game plays, not at every sound.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(5);

    /// <summary>The longest the thread sleeps between two looks: the device's event wakes it at every buffer (10 ms); this bounds a stalled device's, and an exit's, wait.</summary>
    public const int WaitMilliseconds = 20;

    /// <summary>How long <see cref="Dispose"/> (the overlay exiting, on the UI thread), or a new thread, waits for a thread to end: a close took 23 to 60 ms in the spike.</summary>
    public const int JoinMilliseconds = 200;

    /// <summary>
    /// The most plays queued at once: far more than the mix could sound
    /// between two buffers (<see cref="SpeakerMix.MaxVoices"/>). Only a device
    /// that never answers its open lets the queue fill; a play past this is
    /// dropped, so it can't grow while a game keeps playing.
    /// </summary>
    public const int MaxQueued = 64;

    /// <summary>The thread's name, as a debugger and a dump show it.</summary>
    internal const string ThreadName = "Games sound";

    private readonly Func<IWaveDevice> _newDevice;
    private readonly TimeSpan _idleClose;
    private readonly TimeSpan _retryAfter;
    private readonly Action<LogVerbosity, string> _write;

    private readonly object _lock = new();

    // ---- Guarded by _lock ----

    /// <summary>The plays and stops asked for and not yet moved into the mix, in the order they were asked.</summary>
    private readonly List<Request> _queue = new();

    /// <summary>The thread that owns the stream now: "a thread is running". Null: no thread, and the next play starts one.</summary>
    private Thread? _running;

    /// <summary>A thread that has left and may still be closing its device: the next thread joins it first.</summary>
    private Thread? _closing;

    /// <summary>When the last failure was (a <see cref="Stopwatch"/> timestamp), or null.</summary>
    private long? _failedAt;

    /// <summary>A failure since the device last worked: the next one is logged at Verbose, a success says it works again.</summary>
    private bool _failing;

    /// <summary>The launch's first open was logged.</summary>
    private bool _openLogged;

    // Set under the lock, read by the thread at each look without it.
    private volatile bool _disposed;

    /// <summary>The real speaker: Windows' default device, the constants above, the games' log.</summary>
    public WaveOutSpeaker()
        : this(null)
    {
    }

    /// <param name="newDevice">A device for each stream, made on the speaker's thread: winmm's by default, the tests' fake.</param>
    /// <param name="idleClose">As <see cref="IdleClose"/>; the tests' is short.</param>
    /// <param name="retryAfter">As <see cref="RetryAfter"/>; the tests' is short.</param>
    /// <param name="write">Where the lines go: the games' log by default (<see cref="AppLog.Games"/>), a list in the tests.</param>
    internal WaveOutSpeaker(Func<IWaveDevice>? newDevice, TimeSpan? idleClose = null, TimeSpan? retryAfter = null, Action<LogVerbosity, string>? write = null)
    {
        _newDevice = newDevice ?? (() => new WinmmDevice());
        _idleClose = idleClose ?? IdleClose;
        _retryAfter = retryAfter ?? RetryAfter;
        _write = write ?? AppLog.Games.Write;
    }

    /// <summary>A thread is running (the tests' look at the flag).</summary>
    internal bool HasThread
    {
        get
        {
            lock (_lock) return _running is not null;
        }
    }

    /// <summary>The plays and stops queued and not yet in the mix (the tests' look at the cap).</summary>
    internal int Queued
    {
        get
        {
            lock (_lock) return _queue.Count;
        }
    }

    /// <summary>The last thread started, running or not (the tests' look at its end).</summary>
    internal Thread? LastThread { get; private set; }

    // ---- The UI thread's side ----------------------------------------------------------------

    /// <summary>
    /// One sound, from now (<see cref="ISpeaker.Play"/>): queued for the
    /// running thread, or a thread started for it, unless disposed or within
    /// <see cref="RetryAfter"/> of a failure, when it is dropped. Returns at
    /// once; never throws.
    /// </summary>
    public void Play(short[] samples, float gain)
    {
        if (samples is null || samples.Length == 0) return;
        (LogVerbosity Level, string Line)? failure = null;
        try
        {
            lock (_lock)
            {
                if (_disposed) return;
                if (_running is not null)
                {
                    if (_queue.Count < MaxQueued) _queue.Add(new Request(samples, gain));
                    return;
                }
                if (_failedAt is { } failed && Stopwatch.GetElapsedTime(failed) < _retryAfter) return;
                _queue.Add(new Request(samples, gain)); // before the thread exists: its first buffer carries the sound's first sample
                failure = StartThread();
            }
        }
        catch (Exception ex)
        {
            failure = (LogVerbosity.Warning, $"Games sound: a sound could not be queued: {AppLog.Short(ex)}");
        }
        if (failure is { } said) Say(said.Level, said.Line);
    }

    /// <summary>
    /// Every sound playing faded to silence within 5 ms, and dropped; what
    /// plays at the call only (A88): with no thread running, nothing at all,
    /// and a play after it is heard whole. Returns at once; never throws.
    /// </summary>
    public void StopAll()
    {
        try
        {
            lock (_lock)
            {
                if (_disposed || _running is null) return;
                // Two stops in a row are one: the second finds every voice fading already.
                if (_queue.Count > 0 && _queue[^1].IsStop) return;
                _queue.Add(Request.Stop);
            }
        }
        catch (Exception ex)
        {
            Say(LogVerbosity.Warning, $"Games sound: a stop could not be queued: {AppLog.Short(ex)}");
        }
    }

    /// <summary>
    /// The overlay exiting: no sound is taken any more, the thread is asked to
    /// leave (it closes its device on the way out) and joined for at most
    /// <see cref="JoinMilliseconds"/>, so a stuck driver never holds the exit.
    /// It does not fade (A90): a sound still playing is ended by the device's
    /// reset; the window's close just before it has only queued its stop.
    /// Idempotent; never throws.
    /// </summary>
    public void Dispose()
    {
        try
        {
            Thread? running, closing;
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                _queue.Clear();
                running = _running;
                closing = _closing;
            }
            var started = Stopwatch.GetTimestamp();
            foreach (var thread in new[] { running, closing })
            {
                var left = JoinMilliseconds - (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (thread is not null && left > 0) thread.Join(left);
            }
        }
        catch (Exception ex)
        {
            Say(LogVerbosity.Warning, $"Games sound: the speaker could not be closed: {AppLog.Short(ex)}");
        }
    }

    /// <summary>Under the lock: the thread made and started, marked as running first. Null, or the line saying why none could be (a failure like a device's: the voices dropped, no new try within <see cref="RetryAfter"/>).</summary>
    private (LogVerbosity Level, string Line)? StartThread()
    {
        try
        {
            var thread = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = ThreadName };
            _running = thread;
            LastThread = thread;
            thread.Start();
            return null;
        }
        catch (Exception ex)
        {
            _running = null;
            _queue.Clear();
            _failedAt = Stopwatch.GetTimestamp();
            var first = !_failing;
            _failing = true;
            return (first ? LogVerbosity.Warning : LogVerbosity.Verbose, $"Games sound: no thread could be started: {AppLog.Short(ex)}");
        }
    }

    // ---- The speaker's thread ---------------------------------------------------------------

    /// <summary>
    /// The thread's whole body. Nothing leaves it: an exception is logged and
    /// contained, and the way out (the flag, the device's close, the closing
    /// mark) runs whatever happened, each step contained on its own.
    /// </summary>
    private void Run()
    {
        var me = Thread.CurrentThread;
        IWaveDevice? device = null;
        var failed = false;
        try
        {
            JoinTheOneBefore(me);
            device = _newDevice();
            failed = !Stream(device);
        }
        catch (Exception ex)
        {
            failed = true;
            Contain(() => _write(FailureLevel(), $"Games sound failed: {AppLog.Describe(ex)}"));
        }
        finally
        {
            Contain(() => Leave(me, failed));
            Contain(() => device?.Close());
            Contain(() => Closed(me));
        }
    }

    /// <summary>A thread before this one that is still closing its device is waited for, here, never on the UI thread, so two streams never overlap for long.</summary>
    private void JoinTheOneBefore(Thread me)
    {
        Thread? before;
        lock (_lock) before = _closing;
        if (before is not null && !ReferenceEquals(before, me)) before.Join(JoinMilliseconds);
    }

    /// <summary>
    /// The stream from its open to its end: true when it ended as it should
    /// (idle, or the speaker disposed), false when the device failed (said
    /// already).
    /// </summary>
    private bool Stream(IWaveDevice device)
    {
        if (_disposed) return true; // the overlay exiting while this thread waited for the one before: no device opened for nothing
        var opening = Stopwatch.GetTimestamp();
        var code = device.Open(Buffers, BufferSamples);
        if (code != 0)
        {
            Failure("opened", code);
            return false;
        }
        var openMs = Stopwatch.GetElapsedTime(opening).TotalMilliseconds;

        var mix = new SpeakerMix();
        if (!Pass(device, mix)) return false; // every buffer is free after the open: all filled and written at once
        Worked(openMs);

        long? idleSince = null;
        while (!_disposed)
        {
            device.Wait(WaitMilliseconds);
            if (_disposed) break;
            if (!Pass(device, mix)) return false;
            if (!mix.Idle)
            {
                idleSince = null;
                continue;
            }
            idleSince ??= Stopwatch.GetTimestamp();
            if (Stopwatch.GetElapsedTime(idleSince.Value) >= _idleClose && LeaveIfNothingNew()) return true;
        }
        return true;
    }

    /// <summary>Every buffer the device has handed back: what was queued moved into the mix (under the lock, in order), the buffer filled and written. False when a write failed (said already).</summary>
    private bool Pass(IWaveDevice device, SpeakerMix mix)
    {
        for (var n = 0; n < Buffers; n++)
        {
            var index = device.NextFree();
            if (index < 0) break;
            lock (_lock) Drain(mix);
            mix.Fill(device.Buffer(index));
            var code = device.Write(index);
            if (code != 0)
            {
                Failure("written to", code);
                return false;
            }
        }
        return true;
    }

    /// <summary>Under the lock: the queue moved into the mix in the order it was asked, so a stop fades what came before it and nothing after.</summary>
    private void Drain(SpeakerMix mix)
    {
        foreach (var request in _queue)
        {
            if (request.IsStop) mix.StopAll();
            else mix.Add(request.Samples, request.Gain);
        }
        _queue.Clear();
    }

    /// <summary>
    /// The end of an idle stream, decided under the lock with the last look at
    /// the queue: nothing new, so "no thread" is marked in the same step and
    /// the next play starts a new thread (which joins this one while it closes).
    /// Something new: the stream goes on, and the next pass takes it.
    /// </summary>
    private bool LeaveIfNothingNew()
    {
        lock (_lock)
        {
            if (_queue.Count > 0) return false;
            _running = null;
            _closing = Thread.CurrentThread;
            return true;
        }
    }

    /// <summary>The way out of any other end (a failure, an exception, the speaker disposed): "no thread" marked if this one still owns the stream, what was queued dropped with the voices, and a failure's time kept, in one step under the lock.</summary>
    private void Leave(Thread me, bool failed)
    {
        lock (_lock)
        {
            if (ReferenceEquals(_running, me))
            {
                _running = null;
                _closing = me;
                _queue.Clear();
            }
            if (failed) _failedAt = Stopwatch.GetTimestamp();
        }
    }

    /// <summary>This thread's device is closed: a later thread has nothing of it to wait for.</summary>
    private void Closed(Thread me)
    {
        lock (_lock)
        {
            if (ReferenceEquals(_closing, me)) _closing = null;
        }
    }

    // ---- What the log says -------------------------------------------------------------------

    /// <summary>The device took the stream: the launch's first open with its time, and one line when it works again after a failure.</summary>
    private void Worked(double openMs)
    {
        bool first, again;
        lock (_lock)
        {
            first = !_openLogged;
            _openLogged = true;
            again = _failing;
            _failing = false;
        }
        if (first) _write(LogVerbosity.Log, $"Games sound: the device opened in {openMs.ToString("0", CultureInfo.InvariantCulture)} ms");
        if (again) _write(LogVerbosity.Log, "Games sound: the device works again");
    }

    /// <summary>A step the device refused, with winmm's code.</summary>
    private void Failure(string step, int code) =>
        _write(FailureLevel(), $"Games sound: the device could not be {step} (code {code.ToString(CultureInfo.InvariantCulture)})");

    /// <summary>The first failure since the launch or since the device last worked is a Warning, the same again Verbose (A90).</summary>
    private LogVerbosity FailureLevel()
    {
        lock (_lock)
        {
            var first = !_failing;
            _failing = true;
            return first ? LogVerbosity.Warning : LogVerbosity.Verbose;
        }
    }

    /// <summary>A line from the UI thread's side, written outside the lock.</summary>
    private void Say(LogVerbosity level, string line)
    {
        try
        {
            _write(level, line);
        }
        catch (Exception)
        {
            // The log is fail soft itself; a broken one must not turn a dropped sound into a throw.
        }
    }

    /// <summary>One step of the thread's way out, contained: an exception leaving this thread would end the process. Logged as any failure: the first a Warning, the same again Verbose.</summary>
    private void Contain(Action step)
    {
        try
        {
            step();
        }
        catch (Exception ex)
        {
            try
            {
                _write(FailureLevel(), $"Games sound: {AppLog.Short(ex)} on the way out");
            }
            catch (Exception)
            {
                // Nothing is left to tell; the thread ends quietly rather than take the overlay with it.
            }
        }
    }

    /// <summary>A play (its samples, the gate's own copy, and its gain) or a stop (no samples).</summary>
    private readonly record struct Request(short[]? Samples, float Gain)
    {
        public static Request Stop => new(null, 0);

        public bool IsStop => Samples is null;
    }
}

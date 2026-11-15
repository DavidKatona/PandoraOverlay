namespace PandoraOverlay.Tests;

/// <summary>
/// An audio device that plays nothing and records everything (A80's
/// <see cref="IWaveDevice"/>), for <see cref="WaveOutSpeakerTests"/>: its
/// buffers are managed arrays, it hands every written buffer back at the next
/// <see cref="Wait"/> (after a short sleep, so the speaker's thread runs at a
/// device's pace instead of spinning), and each buffer written is copied
/// into <see cref="Written"/> in order, so a test reads exactly the stream the
/// device would have played. It can be told to refuse its open or a write
/// with a code, to throw, to take long in its open or its close, or to hold
/// the thread inside a wait until the test lets it go. One fake can serve
/// every stream of a speaker: an open starts its buffers from nothing, and
/// what it recorded stays.
/// </summary>
internal sealed class FakeWaveDevice : IWaveDevice
{
    private readonly object _gate = new();
    private readonly List<string> _calls = new();
    private readonly List<short[]> _written = new();
    private readonly HashSet<string> _threadNames = new();
    private readonly ManualResetEventSlim _released = new(true);
    private short[][] _buffers = Array.Empty<short[]>();
    private bool[] _withDevice = Array.Empty<bool>();
    private int _inside;
    private int _opens;
    private int _opensEnded;
    private int _closes;
    private int _writes;
    private int _overlaps;
    private volatile bool _waiting;

    /// <summary>What every open answers: 0, or a winmm code (2, no driver; 4, allocated).</summary>
    public int OpenCode { get; set; }

    /// <summary>The write, counted from 1 over the fake's life, that answers <see cref="WriteCode"/>; 0, none.</summary>
    public int FailWriteNumber { get; set; }

    /// <summary>The code the failing write answers.</summary>
    public int WriteCode { get; set; } = 6; // MMSYSERR_NODRIVER: as an unplugged device answers

    /// <summary>The member that throws ("open", "wait", "write", "close"), or null.</summary>
    public string? ThrowIn { get; set; }

    /// <summary>The text of what the fake throws: never to reach a log line (constraint 4: the type, never the message).</summary>
    public const string SecretMessage = "made up: a message the log must never hold";

    /// <summary>How long an open takes; <see cref="OpenRelease"/> set ends it sooner.</summary>
    public TimeSpan OpenDelay { get; set; }

    /// <summary>Set, an open taking <see cref="OpenDelay"/> ends at once.</summary>
    public ManualResetEventSlim OpenRelease { get; } = new(false);

    /// <summary>How long a close takes.</summary>
    public TimeSpan CloseDelay { get; set; }

    /// <summary>A wait's own sleep before it hands the buffers back: a device's pace, coarser than the real 10 ms a buffer.</summary>
    public int WaitSleep { get; set; } = 2;

    public int Opens => Volatile.Read(ref _opens);

    /// <summary>Opens that have returned (whatever they answered).</summary>
    public int OpensEnded => Volatile.Read(ref _opensEnded);

    public int Closes => Volatile.Read(ref _closes);

    public int Writes => Volatile.Read(ref _writes);

    /// <summary>Times two threads were inside the fake at once: the device is the speaker's thread's alone, so always 0.</summary>
    public int Overlaps => Volatile.Read(ref _overlaps);

    /// <summary>The thread is held inside a wait (<see cref="Hold"/>).</summary>
    public bool Waiting => _waiting;

    /// <summary>What the last open was asked for: its buffers and their samples.</summary>
    public (int Buffers, int Samples) OpenedWith { get; private set; }

    /// <summary>The name of every thread that called the fake (the device is the speaker's thread's alone).</summary>
    public IReadOnlyCollection<string> ThreadNames
    {
        get
        {
            lock (_gate) return _threadNames.ToArray();
        }
    }

    /// <summary>Every call, in order: "open", "wait", "next", "buffer", "write", "close".</summary>
    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_gate) return _calls.ToArray();
        }
    }

    /// <summary>Every buffer written, copied at its write, in order.</summary>
    public IReadOnlyList<short[]> Written
    {
        get
        {
            lock (_gate) return _written.ToArray();
        }
    }

    /// <summary>The stream from the buffer numbered <paramref name="from"/> on, as one run of samples.</summary>
    public short[] WrittenFrom(int from) => Written.Skip(from).SelectMany(b => b).ToArray();

    /// <summary>From now, the next wait holds the thread until <see cref="Release"/>.</summary>
    public void Hold() => _released.Reset();

    /// <summary>The held wait goes on.</summary>
    public void Release() => _released.Set();

    public int Open(int buffers, int samples)
    {
        Enter("open");
        try
        {
            Interlocked.Increment(ref _opens);
            OpenedWith = (buffers, samples);
            if (OpenDelay > TimeSpan.Zero) OpenRelease.Wait(OpenDelay);
            if (ThrowIn == "open") throw new InvalidOperationException(SecretMessage);
            if (OpenCode != 0) return OpenCode;
            lock (_gate)
            {
                _buffers = Enumerable.Range(0, buffers).Select(_ => new short[samples]).ToArray();
                _withDevice = new bool[buffers];
            }
            return 0;
        }
        finally
        {
            Interlocked.Increment(ref _opensEnded);
            Leave();
        }
    }

    public void Wait(int milliseconds)
    {
        Enter("wait");
        try
        {
            if (ThrowIn == "wait") throw new InvalidOperationException(SecretMessage);
            Thread.Sleep(Math.Min(WaitSleep, milliseconds));
            if (!_released.IsSet)
            {
                _waiting = true;
                _released.Wait(TimeSpan.FromSeconds(30)); // a test that forgets to let go fails, it doesn't hang
                _waiting = false;
            }
            lock (_gate) Array.Clear(_withDevice);
        }
        finally
        {
            Leave();
        }
    }

    public int NextFree()
    {
        Enter("next");
        try
        {
            lock (_gate) return Array.IndexOf(_withDevice, false);
        }
        finally
        {
            Leave();
        }
    }

    public Span<short> Buffer(int index)
    {
        Enter("buffer");
        try
        {
            lock (_gate) return _buffers[index];
        }
        finally
        {
            Leave();
        }
    }

    public int Write(int index)
    {
        Enter("write");
        try
        {
            if (ThrowIn == "write") throw new InvalidOperationException(SecretMessage);
            var number = Interlocked.Increment(ref _writes);
            if (number == FailWriteNumber) return WriteCode;
            lock (_gate)
            {
                _written.Add(_buffers[index].ToArray());
                _withDevice[index] = true;
            }
            return 0;
        }
        finally
        {
            Leave();
        }
    }

    public void Close()
    {
        Enter("close");
        try
        {
            Interlocked.Increment(ref _closes);
            if (CloseDelay > TimeSpan.Zero) Thread.Sleep(CloseDelay);
            if (ThrowIn == "close") throw new InvalidOperationException(SecretMessage);
            lock (_gate)
            {
                _buffers = Array.Empty<short[]>();
                _withDevice = Array.Empty<bool>();
            }
        }
        finally
        {
            Leave();
        }
    }

    private void Enter(string call)
    {
        if (Interlocked.Increment(ref _inside) > 1) Interlocked.Increment(ref _overlaps);
        lock (_gate)
        {
            _calls.Add(call);
            _threadNames.Add(Thread.CurrentThread.Name ?? "(unnamed)");
        }
    }

    private void Leave() => Interlocked.Decrement(ref _inside);
}

using System.ComponentModel;
using System.Runtime.InteropServices;
using PandoraOverlay.Games;

namespace PandoraOverlay;

/// <summary>
/// The audio device as the speaker's thread sees it (A80): one wave-out
/// stream of a few buffers it fills and hands over, one by one, as the device
/// hands them back. <see cref="WinmmDevice"/> is the real one;
/// the tests' fake keeps its buffers in arrays and records every call, so
/// every rule of <see cref="WaveOutSpeaker"/>'s thread is tested with no
/// device. Every member is called on the speaker's thread only, never the
/// UI's (an open took 17 to 65 ms in the planner's spike, a close 23 to 60).
/// </summary>
internal interface IWaveDevice
{
    /// <summary>The default device, 44,100 Hz, 16-bit, mono, woken by an event, with this many buffers of this many samples. 0, or winmm's code.</summary>
    int Open(int buffers, int samples);

    /// <summary>Until the device hands a buffer back, or the time passes.</summary>
    void Wait(int milliseconds);

    /// <summary>A buffer the device has handed back (or never had since the open), or -1.</summary>
    int NextFree();

    /// <summary>A free buffer's samples, for the mix to fill before it is written.</summary>
    Span<short> Buffer(int index);

    /// <summary>The buffer handed to the device, to play after the ones before it. 0, or winmm's code.</summary>
    int Write(int index);

    /// <summary>Stops the device and lets go of everything: reset, unprepare each header, close, free the memory, in that order. Idempotent.</summary>
    void Close();
}

/// <summary>
/// The PC's default audio device through winmm's <c>waveOut</c> (A80), as
/// the planner's spike ran it on this PC (x64; <c>PLAN-SOUND.md</c>'s
/// appendix, <c>evidence\sound-planner\waveout-spike.cs</c>): one handle on
/// <see cref="Winmm.WaveMapper"/> (Windows' default output, so a headset
/// plugged in between two streams is the next one's), PCM 16-bit mono at
/// <see cref="GameSound.SampleRate"/>, and an event the device sets each time
/// it hands a buffer back (<c>CALLBACK_EVENT</c>), which <see cref="Wait"/>
/// sleeps on.
/// <para>
/// <b>The memory the device writes.</b> The headers and the samples live in
/// unmanaged memory (<see cref="Marshal.AllocHGlobal(int)"/>) from the open to
/// the close: the device sets a header's <c>dwFlags</c> on a thread of its own
/// while managed code runs, so neither may ever move. The mix fills a managed
/// array of the buffer's (<see cref="Buffer"/>), and <see cref="Write"/>
/// copies it over only once the device has handed that buffer back, so
/// managed code never writes memory the device is reading. If the device
/// refuses to close, nothing it could still touch is freed: a few kilobytes
/// left behind beat a heap the device writes into after it was given back.
/// </para>
/// <para>
/// Never the call that sets a handle's volume: measured, it is the overlay's
/// own level in Windows' volume mixer (every handle's at once), which is the
/// player's (A80; <c>GamesArchitectureTests</c> holds it). The level is the
/// mix's gain. No process, no window, nothing of the game: the device is
/// the audio engine's default endpoint, which mixes The Isle's sound and ours
/// as it mixes any app's (constraint 1, A86).
/// </para>
/// </summary>
internal sealed class WinmmDevice : IWaveDevice
{
    private static readonly int HeaderSize = Marshal.SizeOf<Winmm.WaveHeader>();
    private static readonly int FlagsOffset = (int)Marshal.OffsetOf<Winmm.WaveHeader>(nameof(Winmm.WaveHeader.dwFlags));

    private IntPtr _handle;
    private IntPtr _event;
    private IntPtr _headers;
    private IntPtr _data;
    private int _count;
    private int _samples;

    /// <summary>Per buffer: prepared with the device (and so to unprepare at the close).</summary>
    private bool[] _prepared = Array.Empty<bool>();

    /// <summary>Per buffer: handed to the device at least once since the open (before that, it is free though its done flag is not set).</summary>
    private bool[] _written = Array.Empty<bool>();

    /// <summary>Per buffer: the managed samples the mix fills, copied to the device's memory at the write.</summary>
    private short[][] _staging = Array.Empty<short[]>();

    public int Open(int buffers, int samples)
    {
        if (buffers < 1) throw new ArgumentOutOfRangeException(nameof(buffers));
        if (samples < 1) throw new ArgumentOutOfRangeException(nameof(samples));
        Close(); // a second open starts from nothing

        _event = Winmm.CreateEventW(IntPtr.Zero, false, false, IntPtr.Zero);
        if (_event == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());

        var format = Winmm.WaveFormat.Pcm16Mono(GameSound.SampleRate);
        var code = Winmm.waveOutOpen(out _handle, Winmm.WaveMapper, ref format, _event, IntPtr.Zero, Winmm.CallbackEvent);
        if (code != 0)
        {
            _handle = IntPtr.Zero; // not a handle when the open failed
            Close();
            return code;
        }

        _count = buffers;
        _samples = samples;
        _prepared = new bool[buffers];
        _written = new bool[buffers];
        _staging = new short[buffers][];
        _headers = Zeroed(HeaderSize * buffers);
        _data = Zeroed(samples * sizeof(short) * buffers);
        for (var i = 0; i < buffers; i++)
        {
            _staging[i] = new short[samples];
            var header = new Winmm.WaveHeader { lpData = Data(i), dwBufferLength = (uint)(samples * sizeof(short)) };
            Marshal.StructureToPtr(header, Header(i), false);
            code = Winmm.waveOutPrepareHeader(_handle, Header(i), (uint)HeaderSize);
            if (code != 0)
            {
                Close();
                return code;
            }
            _prepared[i] = true;
        }
        return 0;
    }

    public void Wait(int milliseconds)
    {
        if (_event != IntPtr.Zero) Winmm.WaitForSingleObject(_event, (uint)Math.Max(0, milliseconds));
        else Thread.Sleep(Math.Max(0, milliseconds)); // not open: never a loop that spins
    }

    public int NextFree()
    {
        for (var i = 0; i < _count; i++)
        {
            if (!_written[i] || (Marshal.ReadInt32(Header(i), FlagsOffset) & Winmm.HeaderDone) != 0)
            {
                // The device set the flag on its own thread: nothing of this buffer
                // is read or written before the flag was seen.
                Interlocked.MemoryBarrier();
                return i;
            }
        }
        return -1;
    }

    public Span<short> Buffer(int index) => _staging[index];

    public int Write(int index)
    {
        if (_handle == IntPtr.Zero) return Winmm.InvalidHandle;
        Marshal.Copy(_staging[index], 0, Data(index), _samples);
        var header = Header(index);
        Marshal.WriteInt32(header, FlagsOffset, Marshal.ReadInt32(header, FlagsOffset) & ~Winmm.HeaderDone);
        _written[index] = true;
        return Winmm.waveOutWrite(_handle, header, (uint)HeaderSize);
    }

    /// <summary>
    /// The samples the device has played since the open
    /// (<c>waveOutGetPosition</c> in samples), or -1 when it can't say. Not
    /// the speaker's: the opt-in probe reads it, on the speaker's thread,
    /// to measure how long a sound takes to reach the device.
    /// </summary>
    public long Position()
    {
        if (_handle == IntPtr.Zero) return -1;
        var time = new Winmm.MmTime { wType = Winmm.TimeSamples };
        var code = Winmm.waveOutGetPosition(_handle, ref time, (uint)Marshal.SizeOf<Winmm.MmTime>());
        return code == 0 && time.wType == Winmm.TimeSamples ? time.u : -1;
    }

    public void Close()
    {
        var freeable = true;
        if (_handle != IntPtr.Zero)
        {
            // The order that worked (the appendix): every buffer handed back at
            // once, each header unprepared, the handle closed, then the memory.
            Winmm.waveOutReset(_handle);
            for (var i = 0; i < _count; i++)
            {
                if (_prepared[i]) Winmm.waveOutUnprepareHeader(_handle, Header(i), (uint)HeaderSize);
            }
            freeable = Winmm.waveOutClose(_handle) == 0;
            _handle = IntPtr.Zero;
        }
        if (freeable)
        {
            if (_headers != IntPtr.Zero) Marshal.FreeHGlobal(_headers);
            if (_data != IntPtr.Zero) Marshal.FreeHGlobal(_data);
            if (_event != IntPtr.Zero) Winmm.CloseHandle(_event);
        }
        // Refused to close: the device may still write a header or set the
        // event, so neither is given back (see the class's summary).
        _headers = IntPtr.Zero;
        _data = IntPtr.Zero;
        _event = IntPtr.Zero;
        _count = 0;
        _samples = 0;
        _prepared = Array.Empty<bool>();
        _written = Array.Empty<bool>();
        _staging = Array.Empty<short[]>();
    }

    private IntPtr Header(int index) => _headers + index * HeaderSize;

    private IntPtr Data(int index) => _data + index * _samples * sizeof(short);

    /// <summary>Unmanaged memory of this many bytes, every byte 0 (a header's unused fields, and silence until the first fill).</summary>
    private static IntPtr Zeroed(int bytes)
    {
        var memory = Marshal.AllocHGlobal(bytes);
        Marshal.Copy(new byte[bytes], 0, memory, bytes);
        return memory;
    }
}

/// <summary>
/// winmm's wave-out calls and kernel32's event, declared as the planner's
/// spike declared them and ran them on this PC (x64; <c>PLAN-SOUND.md</c>'s
/// appendix). The host's only native audio calls (A80): a game can't make
/// one (the forbidden list refuses native calls), and nothing else in the app
/// declares them. The call that sets a handle's volume is not declared, on
/// purpose (A80).
/// </summary>
internal static class Winmm
{
    /// <summary>The device id of Windows' default output.</summary>
    public const uint WaveMapper = 0xFFFFFFFF;

    /// <summary>The device sets the event given at the open at each buffer it hands back.</summary>
    public const uint CallbackEvent = 0x50000;

    /// <summary>A header's flag: the device is done with the buffer.</summary>
    public const int HeaderDone = 1;

    /// <summary>A position in samples.</summary>
    public const uint TimeSamples = 2;

    /// <summary>winmm's MMSYSERR_INVALHANDLE: what a write on a device not open answers.</summary>
    public const int InvalidHandle = 5;

    /// <summary>WAVEFORMATEX, packed to 2 as the header declares it.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    public struct WaveFormat
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;

        /// <summary>PCM (format tag 1), one channel, 16 bits, at this rate.</summary>
        public static WaveFormat Pcm16Mono(int rate) => new()
        {
            wFormatTag = 1,
            nChannels = 1,
            nSamplesPerSec = (uint)rate,
            nAvgBytesPerSec = (uint)(rate * sizeof(short)),
            nBlockAlign = sizeof(short),
            wBitsPerSample = 16,
            cbSize = 0,
        };
    }

    /// <summary>WAVEHDR: a buffer the device plays, and the flags it sets.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct WaveHeader
    {
        public IntPtr lpData;
        public uint dwBufferLength;
        public uint dwBytesRecorded;
        public IntPtr dwUser;
        public uint dwFlags;
        public uint dwLoops;
        public IntPtr lpNext;
        public IntPtr reserved;
    }

    /// <summary>MMTIME, 12 bytes: the kind, then the union, whose first 4 bytes are the sample count for <see cref="TimeSamples"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MmTime
    {
        public uint wType;
        public uint u;
        public uint pad;
    }

    [DllImport("winmm.dll")]
    public static extern int waveOutOpen(out IntPtr handle, uint device, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);

    [DllImport("winmm.dll")]
    public static extern int waveOutPrepareHeader(IntPtr handle, IntPtr header, uint size);

    [DllImport("winmm.dll")]
    public static extern int waveOutUnprepareHeader(IntPtr handle, IntPtr header, uint size);

    [DllImport("winmm.dll")]
    public static extern int waveOutWrite(IntPtr handle, IntPtr header, uint size);

    [DllImport("winmm.dll")]
    public static extern int waveOutReset(IntPtr handle);

    [DllImport("winmm.dll")]
    public static extern int waveOutClose(IntPtr handle);

    [DllImport("winmm.dll")]
    public static extern int waveOutGetPosition(IntPtr handle, ref MmTime time, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr CreateEventW(IntPtr attributes, bool manualReset, bool initialState, IntPtr name);

    [DllImport("kernel32.dll")]
    public static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll")]
    public static extern bool CloseHandle(IntPtr handle);
}

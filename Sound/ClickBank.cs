using System.Buffers.Binary;
using System.IO;
using PandoraOverlay.Games;

namespace PandoraOverlay;

/// <summary>
/// The six click sounds' samples (A106): each WAV embedded in the app as
/// <c>Clicks/&lt;file&gt;.wav</c> (one csproj line over <c>Assets/Clicks/</c>,
/// whose files the sound tool writes, C01), read by the app's own reader
/// below, once per cue, at the first click that needs it: never at
/// construction, so MainWindow makes a bank in its constructor at no cost
/// and a launch with nobody clicking reads nothing.
/// <para>
/// <b>The app's own reader.</b> The games' WAVs are read by the Engine,
/// which the app may never name (A2), so the clicks, which are the app's,
/// get a reader of their own here. The format is the one every sound of the
/// overlay keeps: RIFF/WAVE, a <c>fmt </c> chunk of PCM, one channel,
/// <see cref="GameSound.SampleRate"/> samples a second, 16 bits, and a
/// <c>data</c> chunk of 1 to <see cref="GameSound.MaxSamples"/> samples;
/// other chunks are skipped by their sizes (with a pad byte after an odd
/// one). The rate and the length's bound are the Contract's two constants,
/// the format's one home (A78, A109), never numbers of this file's own: a
/// click at another rate than the speaker's would play at the wrong pitch.
/// Anything else is an <see cref="InvalidDataException"/> naming the cue
/// and the field, with the value found (<c>Click 'press': 2 channels, not 1</c>).
/// </para>
/// <para>
/// <b>Fail soft.</b> A cue whose file is missing from the build or refused
/// is ONE Warning in the sound's log (<see cref="AppLog.Sound"/>), naming
/// the cue and, for a refusal, the exception's type (never its message:
/// constraint 4), and then silence for that cue: never a throw, never a
/// second line. The other cues play. A build that broke a file is caught
/// before any player hears it: <c>ClickBankTests</c> read every cue, and
/// <c>SoundInStepTests</c> hold the files to the tool.
/// </para>
/// <para>
/// It reads its own assembly's <c>Clicks/</c> resources by name and no other
/// resource (the assembly also holds the site's access header). On the UI
/// thread only, like the gate that asks it (<see cref="ClickSound"/>).
/// </para>
/// </summary>
internal sealed class ClickBank
{
    /// <summary>Where the clicks sit among the app's manifest resources: <c>Clicks/&lt;file&gt;.wav</c> (the csproj's LogicalName).</summary>
    internal const string Folder = "Clicks/";

    /// <summary>A click's resource ends in this, in lower case as the tool writes it.</summary>
    internal const string Extension = ".wav";

    private readonly Func<string, Stream?> _open;
    private readonly Action<LogVerbosity, string> _write;

    /// <summary>Each cue read so far: its samples, or null for a file missing or refused (warned once).</summary>
    private readonly Dictionary<ClickCue, short[]?> _read = new();

    /// <summary>The real bank: the app's own embedded clicks, the sound's log.</summary>
    public ClickBank()
        : this(null, null)
    {
    }

    /// <param name="open">A resource's stream by its name, or null when it is missing: the app's own manifest resources by default, the tests' bytes.</param>
    /// <param name="write">Where the one Warning goes: <see cref="AppLog.Sound"/> by default, a list in the tests.</param>
    internal ClickBank(Func<string, Stream?>? open, Action<LogVerbosity, string>? write)
    {
        _open = open ?? (resource => typeof(ClickBank).Assembly.GetManifestResourceStream(resource));
        _write = write ?? AppLog.Sound.Write;
    }

    /// <summary>A cue's file name, as the tool writes it and the plan names it (section 3.3).</summary>
    internal static string FileOf(ClickCue cue) => cue switch
    {
        ClickCue.Press => "press",
        ClickCue.Select => "select",
        ClickCue.ToggleOn => "toggle-on",
        ClickCue.ToggleOff => "toggle-off",
        ClickCue.Arm => "arm",
        ClickCue.Back => "back",
        _ => throw new ArgumentOutOfRangeException(nameof(cue), cue, "not a click cue"),
    };

    /// <summary>A cue's embedded resource: <c>Clicks/&lt;file&gt;.wav</c>.</summary>
    internal static string ResourceOf(ClickCue cue) => Folder + FileOf(cue) + Extension;

    /// <summary>
    /// A cue's samples, read the first time it is asked for and kept: the
    /// same array every time after (the speaker only reads it). Null when its
    /// file is missing or refused: one Warning the first time, silence after.
    /// Never throws.
    /// </summary>
    public short[]? Samples(ClickCue cue)
    {
        if (_read.TryGetValue(cue, out var kept)) return kept;
        short[]? samples = null;
        string file = Enum.IsDefined(cue) ? FileOf(cue) : cue.ToString();
        try
        {
            using var wav = _open(ResourceOf(cue));
            if (wav is null)
                _write(LogVerbosity.Warning, $"Sound: the click '{file}' is not in the build; it stays silent");
            else
                samples = Read(wav, file);
        }
        catch (Exception ex)
        {
            _write(LogVerbosity.Warning, $"Sound: the click '{file}' could not be read ({ex.GetType().Name}); it stays silent");
        }
        _read[cue] = samples;
        return samples;
    }

    // ---- The reader ----------------------------------------------------------------------

    /// <summary>A PCM format's tag in the <c>fmt </c> chunk.</summary>
    private const ushort Pcm = 1;

    /// <summary>Mono: one channel.</summary>
    private const int Channels = 1;

    /// <summary>16-bit samples.</summary>
    private const int Bits = 16;

    /// <summary>Bytes in one sample of one channel.</summary>
    private const int SampleBytes = Bits / 8;

    /// <summary>The <c>fmt </c> fields read here: PCM's own 16 bytes (a longer chunk is fine, its rest skipped).</summary>
    private const int FormatBytes = 16;

    /// <summary>
    /// One click's WAV into its samples (the summary's format). The stream is
    /// read up to the end of its <c>data</c> chunk and left open: it is the
    /// caller's. Anything else: <see cref="InvalidDataException"/> naming the
    /// cue and the field. The count is checked before anything is allocated for it.
    /// </summary>
    internal static short[] Read(Stream wav, string cue)
    {
        ArgumentNullException.ThrowIfNull(wav);
        ArgumentNullException.ThrowIfNull(cue);
        Span<byte> header = stackalloc byte[12];
        int got = Fill(wav, header);
        if (got < 4 || !Is(header[..4], "RIFF")) throw Refused(cue, "not a RIFF file");
        if (got < header.Length) throw Refused(cue, "it ends inside its RIFF header");
        if (!Is(header[8..12], "WAVE")) throw Refused(cue, "a RIFF file, but not WAVE");

        var formatRead = false;
        Span<byte> chunk = stackalloc byte[8];
        while (true)
        {
            got = Fill(wav, chunk);
            if (got == 0) throw Refused(cue, formatRead ? "no data chunk" : "no fmt chunk");
            if (got < chunk.Length) throw Refused(cue, "it ends inside a chunk's header");
            string id = IdOf(chunk[..4]);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
            if (id == "fmt ")
            {
                if (formatRead) throw Refused(cue, "a second fmt chunk");
                ReadFormat(wav, cue, size);
                Skip(wav, cue, "fmt", (long)size - FormatBytes + (size & 1));
                formatRead = true;
            }
            else if (id == "data")
            {
                if (!formatRead) throw Refused(cue, "its data chunk comes before its fmt chunk");
                return ReadSamples(wav, cue, size);
            }
            else
            {
                Skip(wav, cue, $"'{id}'", (long)size + (size & 1));
            }
        }
    }

    /// <summary>The <c>fmt </c> chunk's six fields, each against the one format; what lies past them is the caller's to skip.</summary>
    private static void ReadFormat(Stream wav, string cue, uint size)
    {
        if (size < FormatBytes) throw Refused(cue, $"a fmt chunk of {size} bytes, shorter than {FormatBytes}");
        Span<byte> fmt = stackalloc byte[FormatBytes];
        if (Fill(wav, fmt) < FormatBytes) throw Refused(cue, "it ends inside its fmt chunk");
        ushort format = BinaryPrimitives.ReadUInt16LittleEndian(fmt);
        ushort channels = BinaryPrimitives.ReadUInt16LittleEndian(fmt[2..]);
        uint rate = BinaryPrimitives.ReadUInt32LittleEndian(fmt[4..]);
        uint byteRate = BinaryPrimitives.ReadUInt32LittleEndian(fmt[8..]);
        ushort block = BinaryPrimitives.ReadUInt16LittleEndian(fmt[12..]);
        ushort bits = BinaryPrimitives.ReadUInt16LittleEndian(fmt[14..]);

        if (format != Pcm) throw Refused(cue, $"format {format}, not PCM ({Pcm})");
        if (channels != Channels) throw Refused(cue, $"{channels} channels, not {Channels}");
        if (rate != GameSound.SampleRate) throw Refused(cue, $"{rate} Hz, not {GameSound.SampleRate}");
        if (bits != Bits) throw Refused(cue, $"{bits} bits, not {Bits}");
        if (block != Channels * SampleBytes) throw Refused(cue, $"a block of {block} bytes, not {Channels * SampleBytes}");
        if (byteRate != GameSound.SampleRate * Channels * SampleBytes)
            throw Refused(cue, $"{byteRate} bytes a second, not {GameSound.SampleRate * Channels * SampleBytes}");
    }

    /// <summary>The data chunk's samples, little-endian, counted (1 to <see cref="GameSound.MaxSamples"/>) before they are read.</summary>
    private static short[] ReadSamples(Stream wav, string cue, uint size)
    {
        if (size % SampleBytes != 0) throw Refused(cue, $"a data chunk of {size} bytes, not whole 16-bit samples");
        long count = size / SampleBytes;
        if (count == 0) throw Refused(cue, "its data chunk is empty");
        if (count > GameSound.MaxSamples) throw Refused(cue, $"{count} samples, more than {GameSound.MaxSamples}");
        var bytes = new byte[size];
        if (Fill(wav, bytes) < bytes.Length) throw Refused(cue, "it ends inside its data chunk");
        var samples = new short[count];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i * SampleBytes));
        return samples;
    }

    /// <summary>Past a chunk (or the rest of one): by a seek where the stream can, else by reading.</summary>
    private static void Skip(Stream wav, string cue, string chunk, long count)
    {
        if (count <= 0) return;
        if (wav.CanSeek)
        {
            if (wav.Length - wav.Position < count) throw Refused(cue, $"it ends inside its {chunk} chunk");
            wav.Position += count;
            return;
        }
        Span<byte> scratch = stackalloc byte[512];
        while (count > 0)
        {
            int read = wav.Read(scratch[..(int)Math.Min(scratch.Length, count)]);
            if (read == 0) throw Refused(cue, $"it ends inside its {chunk} chunk");
            count -= read;
        }
    }

    /// <summary>As many bytes as the stream has, up to the span's length: fewer only at its end.</summary>
    private static int Fill(Stream wav, Span<byte> into)
    {
        var total = 0;
        while (total < into.Length)
        {
            int read = wav.Read(into[total..]);
            if (read == 0) break;
            total += read;
        }
        return total;
    }

    private static bool Is(ReadOnlySpan<byte> bytes, string ascii)
    {
        for (var i = 0; i < ascii.Length; i++)
            if (bytes[i] != ascii[i]) return false;
        return true;
    }

    /// <summary>A chunk's four-letter id as text, a byte outside printable ASCII as '?', so a refusal can name it.</summary>
    private static string IdOf(ReadOnlySpan<byte> bytes)
    {
        Span<char> id = stackalloc char[4];
        for (var i = 0; i < id.Length; i++)
            id[i] = bytes[i] is >= 0x20 and < 0x7F ? (char)bytes[i] : '?';
        return new string(id);
    }

    private static InvalidDataException Refused(string cue, string what) => new($"Click '{cue}': {what}");
}

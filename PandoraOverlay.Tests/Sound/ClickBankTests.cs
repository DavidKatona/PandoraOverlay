using System.Buffers.Binary;
using System.IO;
using PandoraOverlay.Games;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The clicks' files as the app reads them (<see cref="ClickBank"/>; A106,
/// A109, R8): every cue has its WAV embedded in the app and the app's own
/// reader takes it; every embedded click is a cue; the reader refuses each
/// wrong field by name, at the Contract's rate and length; no click is
/// longer than the gap or louder than −14 dBFS; a file is read once, at the
/// first need, and a missing or refused one is one Warning and silence.
/// </summary>
public sealed class ClickBankTests
{
    /// <summary>
    /// A WAV of these samples in the one format, its fields changeable one at a
    /// time (the tests' bytes): RIFF/WAVE, a 16-byte PCM fmt chunk, then the
    /// data chunk; <paramref name="before"/> chunks go between the two.
    /// </summary>
    internal static byte[] Wav(short[] samples, ushort format = 1, ushort channels = 1, uint rate = GameSound.SampleRate, ushort bits = 16,
                               ushort? block = null, uint? byteRate = null, params (string Id, byte[] Body)[] before)
    {
        using var file = new MemoryStream();
        using var w = new BinaryWriter(file);
        w.Write("RIFF"u8);
        w.Write(0u); // the RIFF size, set below
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16u);
        w.Write(format);
        w.Write(channels);
        w.Write(rate);
        w.Write(byteRate ?? rate * channels * (uint)(bits / 8));
        w.Write(block ?? (ushort)(channels * (bits / 8)));
        w.Write(bits);
        foreach (var (id, body) in before)
        {
            w.Write(System.Text.Encoding.ASCII.GetBytes(id));
            w.Write((uint)body.Length);
            w.Write(body);
            if (body.Length % 2 == 1) w.Write((byte)0);
        }
        w.Write("data"u8);
        w.Write((uint)(samples.Length * 2));
        foreach (var s in samples) w.Write(s);
        w.Flush();
        var bytes = file.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)(bytes.Length - 8));
        return bytes;
    }

    /// <summary>A bank over the tests' own bytes per resource (a missing name: null), counting each open and keeping each line.</summary>
    internal sealed class Bench
    {
        public Bench(Func<string, byte[]?> files)
        {
            Bank = new ClickBank(resource =>
            {
                Opened.Add(resource);
                return files(resource) is { } bytes ? new MemoryStream(bytes) : null;
            }, (level, line) => Lines.Add((level, line)));
        }

        public ClickBank Bank { get; }

        public List<string> Opened { get; } = new();

        public List<(LogVerbosity Level, string Line)> Lines { get; } = new();
    }

    private static readonly ClickCue[] Cues = Enum.GetValues<ClickCue>();

    private static double Dbfs(short[] samples) => 20 * Math.Log10(samples.Max(s => Math.Abs((int)s)) / (double)short.MaxValue);

    // ---- The real files ----------------------------------------------------------------------

    /// <summary>Every cue has its file embedded in the app as Clicks/&lt;file&gt;.wav, and the app's reader takes it, with no line.</summary>
    [Fact]
    public void EveryCueHasItsEmbeddedFileAndTheReaderTakesIt()
    {
        var lines = new List<string>();
        var bank = new ClickBank(null, (_, line) => lines.Add(line));

        foreach (var cue in Cues)
        {
            var samples = bank.Samples(cue);
            Assert.True(samples is { Length: > 0 }, $"{cue}: no samples");
        }
        Assert.Empty(lines);
        Assert.Equal(new[] { "press", "select", "toggle-on", "toggle-off", "arm", "back" }, Cues.Select(ClickBank.FileOf)); // the plan's names (3.3)
    }

    /// <summary>Every click the app embeds is a cue's: no stray file under Clicks/, none missing.</summary>
    [Fact]
    public void EveryEmbeddedClickIsACue()
    {
        var embedded = typeof(App).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ClickBank.Folder, StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal);

        Assert.Equal(Cues.Select(ClickBank.ResourceOf).OrderBy(n => n, StringComparer.Ordinal), embedded);
    }

    /// <summary>
    /// R8, held on what ships: no click longer than the gap, so two never
    /// overlap (A99), none shorter than 15 ms, and none louder than −14 dBFS.
    /// </summary>
    [Fact]
    public void NoClickIsLongerThanTheGapOrLouderThanMinusFourteen()
    {
        var bank = new ClickBank(null, (_, _) => { });
        var longest = (int)(ClickSound.MinGap.TotalSeconds * GameSound.SampleRate); // 3,528 samples

        foreach (var cue in Cues)
        {
            var samples = bank.Samples(cue)!;
            Assert.True(samples.Length <= longest, $"{cue}: {samples.Length} samples, longer than the gap's {longest}");
            Assert.True(samples.Length >= 662, $"{cue}: {samples.Length} samples, shorter than 15 ms (R11)");
            Assert.True(Dbfs(samples) <= -14 + 1e-9, $"{cue}: {Dbfs(samples):0.00} dBFS, louder than -14");
        }
    }

    // ---- Read once, at the first need ------------------------------------------------------------------

    [Fact]
    public void ACueIsReadOnceAtItsFirstNeedAndNothingAtConstruction()
    {
        var press = Wav(new short[] { 0, 9, -9, 0 });
        var bench = new Bench(_ => press);
        Assert.Empty(bench.Opened); // nothing at construction

        var first = bench.Bank.Samples(ClickCue.Press);
        var again = bench.Bank.Samples(ClickCue.Press);
        bench.Bank.Samples(ClickCue.Back);

        Assert.Equal(new short[] { 0, 9, -9, 0 }, first);
        Assert.Same(first, again);
        Assert.Equal(new[] { "Clicks/press.wav", "Clicks/back.wav" }, bench.Opened); // its own Clicks/ resources, by name, and no other
    }

    [Fact]
    public void AMissingFileIsOneWarningNamingTheCueThenSilence()
    {
        var bench = new Bench(resource => resource == "Clicks/arm.wav" ? null : Wav(new short[] { 0, 1, 0 }));

        Assert.Null(bench.Bank.Samples(ClickCue.Arm));
        Assert.Null(bench.Bank.Samples(ClickCue.Arm));
        Assert.NotNull(bench.Bank.Samples(ClickCue.Press)); // the others play

        var (level, line) = Assert.Single(bench.Lines);
        Assert.Equal(LogVerbosity.Warning, level);
        Assert.Contains("'arm'", line, StringComparison.Ordinal);
        Assert.StartsWith("Sound: ", line, StringComparison.Ordinal);
        Assert.Single(bench.Opened, "Clicks/arm.wav");
    }

    [Fact]
    public void ARefusedFileIsOneWarningWithTheCueAndTheTypeNeverTheMessage()
    {
        var bench = new Bench(_ => Wav(new short[] { 0, 1, 0 }, channels: 2));

        Assert.Null(bench.Bank.Samples(ClickCue.Select));
        Assert.Null(bench.Bank.Samples(ClickCue.Select));

        var (level, line) = Assert.Single(bench.Lines);
        Assert.Equal(LogVerbosity.Warning, level);
        Assert.Contains("'select'", line, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidDataException), line, StringComparison.Ordinal);
        Assert.DoesNotContain("channels", line, StringComparison.Ordinal); // the type, never the message (constraint 4)
    }

    [Fact]
    public void AnOpenThatThrowsIsARefusalNeverAThrow()
    {
        var lines = new List<string>();
        var bank = new ClickBank(_ => throw new IOException("a made-up failure"), (_, line) => lines.Add(line));

        Assert.Null(bank.Samples(ClickCue.Back));
        Assert.Contains(nameof(IOException), Assert.Single(lines), StringComparison.Ordinal);
    }

    // ---- The reader ------------------------------------------------------------------------------------

    [Fact]
    public void TheReaderTakesTheOneFormatAndSkipsOtherChunks()
    {
        var samples = new short[] { 0, 1000, -1000, short.MaxValue - 1, short.MinValue + 1, 0 };

        Assert.Equal(samples, ClickBank.Read(new MemoryStream(Wav(samples)), "press"));
        Assert.Equal(samples, ClickBank.Read(new MemoryStream(Wav(samples, before: new[] { ("LIST", new byte[] { 1, 2, 3 }), ("fact", new byte[4]) })), "press"));
    }

    /// <summary>The Contract's bounds, never the reader's own (A109): one sample to <see cref="GameSound.MaxSamples"/>, at <see cref="GameSound.SampleRate"/>.</summary>
    [Fact]
    public void TheRateAndTheLongestAreTheContracts()
    {
        Assert.Equal(GameSound.MaxSamples, ClickBank.Read(new MemoryStream(Wav(new short[GameSound.MaxSamples])), "press").Length);
        Assert.Single(ClickBank.Read(new MemoryStream(Wav(new short[1])), "press"));

        var longer = Assert.Throws<InvalidDataException>(() => ClickBank.Read(new MemoryStream(Wav(new short[GameSound.MaxSamples + 1])), "press"));
        Assert.Contains($"{GameSound.MaxSamples + 1} samples", longer.Message, StringComparison.Ordinal);
        var rate = Assert.Throws<InvalidDataException>(() => ClickBank.Read(new MemoryStream(Wav(new short[3], rate: 48_000)), "press"));
        Assert.Equal($"Click 'press': 48000 Hz, not {GameSound.SampleRate}", rate.Message);
    }

    public static IEnumerable<object[]> WrongFields()
    {
        var good = Wav(new short[] { 0, 5, 0 });
        byte[] With(Action<byte[]> change)
        {
            var bytes = good.ToArray();
            change(bytes);
            return bytes;
        }
        yield return new object[] { "not a RIFF file", With(b => b[0] = (byte)'X') };
        yield return new object[] { "it ends inside its RIFF header", good[..10] };
        yield return new object[] { "a RIFF file, but not WAVE", With(b => b[8] = (byte)'X') };
        yield return new object[] { "format 3, not PCM (1)", Wav(new short[3], format: 3) };
        yield return new object[] { "2 channels, not 1", Wav(new short[3], channels: 2) };
        yield return new object[] { "22050 Hz, not 44100", Wav(new short[3], rate: 22_050) };
        yield return new object[] { "8 bits, not 16", Wav(new short[3], bits: 8, block: 2, byteRate: 88_200) };
        yield return new object[] { "a block of 4 bytes, not 2", Wav(new short[3], block: 4) };
        yield return new object[] { "176400 bytes a second, not 88200", Wav(new short[3], byteRate: 176_400) };
        yield return new object[] { "a fmt chunk of 14 bytes, shorter than 16", With(b => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(16), 14)) };
        yield return new object[] { "its data chunk is empty", Wav(Array.Empty<short>()) };
        yield return new object[] { "a data chunk of 5 bytes, not whole 16-bit samples", With(b => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(40), 5)) };
        yield return new object[] { "it ends inside its data chunk", good[..^2] };
        yield return new object[] { "no data chunk", good[..36] };
        yield return new object[] { "no fmt chunk", good[..12] };
        yield return new object[] { "it ends inside a chunk's header", good[..16] };
        yield return new object[] { "a second fmt chunk", Wav(new short[3], before: new[] { ("fmt ", good[20..36]) }) };
        yield return new object[] { "it ends inside its 'LIST' chunk", Wav(new short[3], before: new[] { ("LIST", new byte[8]) })[..48] };
        // the data chunk first: its fmt comes after it
        var dataFirst = new MemoryStream();
        dataFirst.Write(good[..12]);
        dataFirst.Write(good[36..]);
        dataFirst.Write(good[12..36]);
        yield return new object[] { "its data chunk comes before its fmt chunk", dataFirst.ToArray() };
    }

    /// <summary>Each wrong field refused by name, the cue named first, as <c>Click 'press': …</c>.</summary>
    [Theory]
    [MemberData(nameof(WrongFields))]
    public void EachWrongFieldIsRefusedByName(string what, byte[] file)
    {
        var refused = Assert.Throws<InvalidDataException>(() => ClickBank.Read(new MemoryStream(file), "toggle-on"));

        Assert.Equal($"Click 'toggle-on': {what}", refused.Message);
    }
}

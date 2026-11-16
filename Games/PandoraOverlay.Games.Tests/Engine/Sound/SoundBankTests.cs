using System.IO;
using System.Reflection;
using System.Text;
using PandoraOverlay.Games.Engine;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The engine's sounds (A78): the one WAV reader on WAVs the test writes
/// itself, a good one and each field wrong once, each refusal naming the sound
/// and the field; the chunks it skips; and the bank loaded from an assembly as
/// a game loads its own (the engine's, which has no sound, and one of the
/// test's own that serves manifest resources the way a game's assembly does).
/// </summary>
public class SoundBankTests
{
    // ---- A WAV, written by the test -----------------------------------------------------

    /// <summary>A WAV's every field, as the test wants it: the Contract's format unless a test says otherwise.</summary>
    private sealed class WavFile
    {
        public string Riff = "RIFF";
        public string Wave = "WAVE";
        public bool WithFmt = true;
        public int FmtSize = 16;
        public ushort Format = 1;
        public ushort Channels = 1;
        public uint Rate = 44_100;
        public ushort Bits = 16;
        public ushort? BlockAlign;
        public uint? ByteRate;

        /// <summary>Chunks before the fmt chunk, and between it and the data chunk.</summary>
        public readonly List<(string Id, byte[] Body)> Before = new();
        public readonly List<(string Id, byte[] Body)> Between = new();

        /// <summary>The data chunk's bytes; null: no data chunk at all.</summary>
        public byte[]? Data = Bytes(0, 1, -1, short.MaxValue, short.MinValue, 1234);

        /// <summary>Put the data chunk before the fmt chunk.</summary>
        public bool DataFirst;

        public byte[] Write()
        {
            var body = new MemoryStream();
            var w = new BinaryWriter(body);
            w.Write(Encoding.ASCII.GetBytes(Wave));
            foreach (var (id, bytes) in Before) Chunk(w, id, bytes);
            if (DataFirst && Data is not null) Chunk(w, "data", Data);
            if (WithFmt) Chunk(w, "fmt ", Fmt());
            foreach (var (id, bytes) in Between) Chunk(w, id, bytes);
            if (!DataFirst && Data is not null) Chunk(w, "data", Data);
            w.Flush();

            var file = new MemoryStream();
            var f = new BinaryWriter(file);
            f.Write(Encoding.ASCII.GetBytes(Riff));
            f.Write((uint)body.Length);
            f.Write(body.ToArray());
            f.Flush();
            return file.ToArray();
        }

        private byte[] Fmt()
        {
            var fmt = new MemoryStream();
            var w = new BinaryWriter(fmt);
            w.Write(Format);
            w.Write(Channels);
            w.Write(Rate);
            w.Write(ByteRate ?? Rate * Channels * (uint)(Bits / 8));
            w.Write(BlockAlign ?? (ushort)(Channels * Bits / 8));
            w.Write(Bits);
            w.Flush();
            var bytes = fmt.ToArray();
            Array.Resize(ref bytes, FmtSize); // shorter cuts the fields, longer adds zeros (an 18-byte one's cbSize)
            return bytes;
        }

        /// <summary>A chunk: its id, its size, its bytes, and the pad byte after an odd size.</summary>
        private static void Chunk(BinaryWriter w, string id, byte[] bytes)
        {
            w.Write(Encoding.ASCII.GetBytes(id));
            w.Write((uint)bytes.Length);
            w.Write(bytes);
            if (bytes.Length % 2 == 1) w.Write((byte)0);
        }
    }

    /// <summary>16-bit little-endian samples as a data chunk's bytes.</summary>
    private static byte[] Bytes(params short[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++) BitConverter.TryWriteBytes(bytes.AsSpan(i * 2), samples[i]);
        return bytes;
    }

    private static GameSound Read(WavFile wav, string name = "bite") => SoundBank.Read(new MemoryStream(wav.Write()), name);

    private static string Refusal(WavFile wav, string name = "bite") =>
        Assert.Throws<InvalidDataException>(() => Read(wav, name)).Message;

    // ---- The reader --------------------------------------------------------------------

    [Fact]
    public void AGoodWavIsItsSamplesUnderItsName()
    {
        var sound = Read(new WavFile(), "bite");

        Assert.Equal("bite", sound.Name);
        Assert.Equal(new short[] { 0, 1, -1, short.MaxValue, short.MinValue, 1234 }, sound.Samples.ToArray());
    }

    [Fact]
    public void ItReadsFromWhereTheStreamIsAndLeavesItOpen()
    {
        var bytes = new WavFile().Write();
        using var stream = new MemoryStream(new byte[] { 9, 9, 9 }.Concat(bytes).ToArray());
        stream.Position = 3;

        var sound = SoundBank.Read(stream, "x");

        Assert.Equal(6, sound.Samples.Length);
        Assert.True(stream.CanRead); // the caller's to close
    }

    /// <summary>One sample and exactly <see cref="GameSound.MaxSamples"/> are both a sound.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(GameSound.MaxSamples)]
    public void OneSampleUpToTwoSecondsIsASound(int count)
    {
        var sound = Read(new WavFile { Data = Bytes(new short[count]) });

        Assert.Equal(count, sound.Samples.Length);
    }

    /// <summary>Each field wrong once: the message names the sound and the field, with what was found.</summary>
    [Fact]
    public void EachWrongFieldIsRefusedByName()
    {
        Assert.Equal("Sound 'bite': not a RIFF file", Refusal(new WavFile { Riff = "RIFX" }));
        Assert.Equal("Sound 'bite': a RIFF file, but not WAVE", Refusal(new WavFile { Wave = "AVI " }));
        Assert.Equal("Sound 'bite': format 3, not PCM (1)", Refusal(new WavFile { Format = 3 }));
        Assert.Equal("Sound 'bite': 2 channels, not 1", Refusal(new WavFile { Channels = 2 }));
        Assert.Equal("Sound 'bite': 48000 Hz, not 44100", Refusal(new WavFile { Rate = 48_000 }));
        Assert.Equal("Sound 'bite': 8 bits, not 16", Refusal(new WavFile { Bits = 8 }));
        Assert.Equal("Sound 'bite': no data chunk", Refusal(new WavFile { Data = null }));
        Assert.Equal("Sound 'bite': its data chunk is empty", Refusal(new WavFile { Data = Array.Empty<byte>() }));
        Assert.Equal("Sound 'bite': 88201 samples, more than 88200 (2 s)", Refusal(new WavFile { Data = Bytes(new short[GameSound.MaxSamples + 1]) }));
    }

    /// <summary>The fields that follow from the others, and the chunks' own shape, are held as hard: a writer that got them wrong wrote a broken file.</summary>
    [Fact]
    public void ABrokenShapeIsRefusedByNameToo()
    {
        Assert.Equal("Sound 'x': a block of 4 bytes, not 2", Refusal(new WavFile { BlockAlign = 4 }, "x"));
        Assert.Equal("Sound 'x': 44100 bytes a second, not 88200", Refusal(new WavFile { ByteRate = 44_100 }, "x"));
        Assert.Equal("Sound 'x': a fmt chunk of 14 bytes, shorter than 16", Refusal(new WavFile { FmtSize = 14 }, "x"));
        Assert.Equal("Sound 'x': no fmt chunk", Refusal(new WavFile { WithFmt = false, Data = null }, "x"));
        Assert.Equal("Sound 'x': its data chunk comes before its fmt chunk", Refusal(new WavFile { DataFirst = true }, "x"));
        Assert.Equal("Sound 'x': a data chunk of 3 bytes, not whole 16-bit samples", Refusal(new WavFile { Data = new byte[] { 1, 2, 3 } }, "x"));

        var second = new WavFile();
        second.Between.Add(("fmt ", new byte[16]));
        Assert.Equal("Sound 'x': a second fmt chunk", Refusal(second, "x"));
    }

    [Fact]
    public void AStreamThatEndsTooSoonIsRefusedByName()
    {
        var whole = new WavFile().Write();
        GameSound Cut(int length) => SoundBank.Read(new MemoryStream(whole[..length]), "x");

        Assert.Equal("Sound 'x': not a RIFF file", Assert.Throws<InvalidDataException>(() => Cut(2)).Message);
        Assert.Equal("Sound 'x': it ends inside its RIFF header", Assert.Throws<InvalidDataException>(() => Cut(8)).Message);
        Assert.Equal("Sound 'x': no fmt chunk", Assert.Throws<InvalidDataException>(() => Cut(12)).Message);
        Assert.Equal("Sound 'x': it ends inside a chunk's header", Assert.Throws<InvalidDataException>(() => Cut(16)).Message);
        Assert.Equal("Sound 'x': it ends inside its fmt chunk", Assert.Throws<InvalidDataException>(() => Cut(30)).Message);
        Assert.Equal("Sound 'x': no data chunk", Assert.Throws<InvalidDataException>(() => Cut(36)).Message);
        Assert.Equal("Sound 'x': it ends inside its data chunk", Assert.Throws<InvalidDataException>(() => Cut(whole.Length - 1)).Message);
    }

    /// <summary>Any other chunk is skipped by its size, before the fmt chunk or after it, an odd-sized one with its pad byte, and an 18-byte fmt chunk too.</summary>
    [Fact]
    public void OtherChunksAreSkippedWithTheirPadByte()
    {
        var wav = new WavFile { FmtSize = 18 };
        wav.Before.Add(("LIST", Encoding.ASCII.GetBytes("INFOISFT")));
        wav.Before.Add(("junk", new byte[] { 7, 7, 7 })); // odd: a pad byte follows
        wav.Between.Add(("fact", new byte[] { 6, 0, 0, 0 }));
        wav.Between.Add(("odd!", new byte[] { 1 }));

        var sound = Read(wav);

        Assert.Equal(new short[] { 0, 1, -1, short.MaxValue, short.MinValue, 1234 }, sound.Samples.ToArray());
    }

    /// <summary>A stream that can't seek is read the same, its skipped chunks read past; one that ends inside a skipped chunk says which.</summary>
    [Fact]
    public void AStreamThatCantSeekReadsTheSame()
    {
        var wav = new WavFile();
        wav.Before.Add(("LIST", new byte[700])); // longer than the reader's scratch buffer
        var bytes = wav.Write();

        var sound = SoundBank.Read(new OneWay(bytes), "x");
        Assert.Equal(6, sound.Samples.Length);

        var cut = new WavFile();
        cut.Before.Add(("LIST", new byte[700]));
        var cutBytes = cut.Write()[..400];
        Assert.Equal("Sound 'x': it ends inside its 'LIST' chunk", Assert.Throws<InvalidDataException>(() => SoundBank.Read(new OneWay(cutBytes), "x")).Message);
        Assert.Equal("Sound 'x': it ends inside its 'LIST' chunk", Assert.Throws<InvalidDataException>(() => SoundBank.Read(new MemoryStream(cutBytes), "x")).Message);
    }

    [Fact]
    public void ReadNeedsAStreamAndAName()
    {
        Assert.Throws<ArgumentNullException>(() => SoundBank.Read(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => SoundBank.Read(new MemoryStream(new WavFile().Write()), null!));
    }

    // ---- The bank -------------------------------------------------------------------------

    /// <summary>The engine has no sound of its own: its bank is empty, as a game with no sounds gets.</summary>
    [Fact]
    public void AnAssemblyWithNoSoundsGivesAnEmptyBank()
    {
        var bank = SoundBank.Load(typeof(SoundBank).Assembly);

        Assert.Empty(bank.Names);
        Assert.False(bank.TryGet("bite", out _));
    }

    /// <summary>
    /// Every <c>Sound/&lt;name&gt;.wav</c> of an assembly, named without the folder
    /// and the extension, in name order; nothing else of its resources (its art,
    /// a file of another kind under Sound/, a sound in another folder).
    /// </summary>
    [Fact]
    public void ItLoadsEverySoundAndNothingElseFromAnAssembly()
    {
        var assembly = new ResourceAssembly("PandoraOverlay.Games.Sample", new Dictionary<string, byte[]>
        {
            ["Sound/step.wav"] = new WavFile { Data = Bytes(1, 2) }.Write(),
            ["Sound/bite.wav"] = new WavFile { Data = Bytes(3) }.Write(),
            ["Art/creatures.png"] = new byte[] { 1 },
            ["Sound/notes.txt"] = new byte[] { 1 },
            ["Sound/.wav"] = new byte[] { 1 },
            ["Music/theme.wav"] = new WavFile().Write(),
        });

        var bank = SoundBank.Load(assembly);

        Assert.Equal(new[] { "bite", "step" }, bank.Names);
        Assert.Equal("bite", bank["bite"].Name);
        Assert.Equal(new short[] { 1, 2 }, bank["step"].Samples.ToArray());
        Assert.True(bank.TryGet("bite", out var bite));
        Assert.Same(bank["bite"], bite); // one object per sound: the host goes by the object (A81)
    }

    [Fact]
    public void AMissingSoundIsNamedWithItsAssembly()
    {
        var bank = SoundBank.Load(new ResourceAssembly("PandoraOverlay.Games.Sample", new Dictionary<string, byte[]>
        {
            ["Sound/step.wav"] = new WavFile().Write(),
        }));

        var error = Assert.Throws<KeyNotFoundException>(() => bank["roar"]);

        Assert.Contains("'roar'", error.Message);
        Assert.Contains("PandoraOverlay.Games.Sample", error.Message);
        Assert.False(bank.TryGet("roar", out _));
    }

    /// <summary>A sound in the wrong format stops the load, naming that sound: a build defect a game's tests catch by loading its bank.</summary>
    [Fact]
    public void ABadSoundInAnAssemblyStopsTheLoadByName()
    {
        var assembly = new ResourceAssembly("PandoraOverlay.Games.Sample", new Dictionary<string, byte[]>
        {
            ["Sound/step.wav"] = new WavFile().Write(),
            ["Sound/roar.wav"] = new WavFile { Channels = 2 }.Write(),
        });

        Assert.Equal("Sound 'roar': 2 channels, not 1", Assert.Throws<InvalidDataException>(() => SoundBank.Load(assembly)).Message);
    }

    [Fact]
    public void LoadNeedsAnAssembly() => Assert.Throws<ArgumentNullException>(() => SoundBank.Load(null!));

    /// <summary>
    /// The engine reads a games assembly's sounds alone (A94): an assembly named as
    /// the app is, with a sound where a game keeps one, is refused by name before
    /// anything of it is read, and so is the framework's; a games assembly reads.
    /// </summary>
    [Fact]
    public void TheBankReadsAGamesAssemblyAlone()
    {
        var sounds = new Dictionary<string, byte[]> { ["Sound/step.wav"] = new WavFile().Write() };
        var app = new ResourceAssembly("PandoraOverlay", sounds);

        var error = Assert.Throws<ArgumentException>(() => SoundBank.Load(app));

        Assert.Contains("'PandoraOverlay'", error.Message);
        Assert.Contains("A94", error.Message);
        Assert.Equal(0, app.Reads);
        Assert.Throws<ArgumentException>(() => SoundBank.Load(typeof(object).Assembly));
        Assert.Equal(new[] { "step" }, SoundBank.Load(new ResourceAssembly("PandoraOverlay.Games.Sample", sounds)).Names);
    }

    // ---- Helpers ------------------------------------------------------------------------------

    /// <summary>A stream that reads forward only, as a stream from somewhere other than memory may.</summary>
    private sealed class OneWay : Stream
    {
        private readonly MemoryStream _inner;

        public OneWay(byte[] bytes) => _inner = new MemoryStream(bytes);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, Math.Min(count, 100)); // in small bites
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

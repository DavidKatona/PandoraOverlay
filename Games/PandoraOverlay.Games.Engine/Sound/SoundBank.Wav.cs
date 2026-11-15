using System.Buffers.Binary;
using System.IO;

namespace PandoraOverlay.Games.Engine;

public sealed partial class SoundBank
{
    /// <summary>
    /// The WAV reader itself, the one there is (A78). A WAV is a RIFF file: a
    /// 12-byte header (<c>RIFF</c>, a size, <c>WAVE</c>) and then chunks, each
    /// a four-letter id, a 32-bit little-endian size and that many bytes, with
    /// a pad byte after an odd size. Two chunks matter: <c>fmt </c>, the format
    /// (PCM, the channels, the rate, the bits), and <c>data</c>, the samples,
    /// which must come after it. Every other chunk (a <c>LIST</c> of tags, a
    /// <c>fact</c>) is skipped by its size, with its pad byte; whatever lies
    /// after the <c>data</c> chunk is not read. The RIFF header's own size is
    /// not checked against the stream: the chunks are what is read.
    ///
    /// <para>Everything is checked, and each failure is an
    /// <see cref="InvalidDataException"/> naming the sound and what was wrong,
    /// with the value found: the format, the channels, the rate and the bits
    /// against the Contract's (<see cref="GameSound"/>), the block size and the
    /// bytes a second that follow from them (a writer that got those wrong
    /// wrote a broken file), a data chunk that is empty, holds half a sample,
    /// or more than <see cref="GameSound.MaxSamples"/>, and a stream that ends
    /// inside a chunk. The sample count is checked before anything is
    /// allocated for it.</para>
    /// </summary>
    private static class Wav
    {
        /// <summary>A PCM format's tag in the <c>fmt </c> chunk.</summary>
        private const ushort Pcm = 1;

        private const int Channels = 1;

        private const int BitsPerSample = 16;

        /// <summary>Bytes in one sample of one channel at 16 bits.</summary>
        private const int BytesPerSample = BitsPerSample / 8;

        /// <summary>The shortest <c>fmt </c> chunk that holds the six fields read here (PCM's own).</summary>
        private const int FmtSize = 16;

        /// <summary>A chunk's id and size.</summary>
        private const int ChunkHeaderSize = 8;

        public static short[] Read(Stream wav, string name)
        {
            Span<byte> header = stackalloc byte[12];
            int got = Fill(wav, header);
            if (got < 4 || !Is(header[..4], "RIFF")) throw Bad(name, "not a RIFF file");
            if (got < header.Length) throw Bad(name, "it ends inside its RIFF header");
            if (!Is(header[8..12], "WAVE")) throw Bad(name, "a RIFF file, but not WAVE");

            var formatSeen = false;
            Span<byte> chunk = stackalloc byte[ChunkHeaderSize];
            while (true)
            {
                got = Fill(wav, chunk);
                if (got == 0) throw Bad(name, formatSeen ? "no data chunk" : "no fmt chunk");
                if (got < chunk.Length) throw Bad(name, "it ends inside a chunk's header");
                string id = Id(chunk[..4]);
                uint size = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);

                if (id == "fmt ")
                {
                    if (formatSeen) throw Bad(name, "a second fmt chunk");
                    ReadFormat(wav, name, size);
                    Skip(wav, name, "fmt", (long)size - FmtSize + (size & 1)); // a longer fmt (an 18-byte one with its cbSize) is fine
                    formatSeen = true;
                }
                else if (id == "data")
                {
                    if (!formatSeen) throw Bad(name, "its data chunk comes before its fmt chunk");
                    return ReadSamples(wav, name, size);
                }
                else
                {
                    Skip(wav, name, $"'{id}'", (long)size + (size & 1));
                }
            }
        }

        /// <summary>The <c>fmt </c> chunk's six fields, each against the Contract's format; what lies past them is the caller's to skip.</summary>
        private static void ReadFormat(Stream wav, string name, uint size)
        {
            if (size < FmtSize) throw Bad(name, $"a fmt chunk of {size} bytes, shorter than {FmtSize}");
            Span<byte> fmt = stackalloc byte[FmtSize];
            if (Fill(wav, fmt) < FmtSize) throw Bad(name, "it ends inside its fmt chunk");
            ushort format = BinaryPrimitives.ReadUInt16LittleEndian(fmt);
            ushort channels = BinaryPrimitives.ReadUInt16LittleEndian(fmt[2..]);
            uint rate = BinaryPrimitives.ReadUInt32LittleEndian(fmt[4..]);
            uint byteRate = BinaryPrimitives.ReadUInt32LittleEndian(fmt[8..]);
            ushort blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(fmt[12..]);
            ushort bits = BinaryPrimitives.ReadUInt16LittleEndian(fmt[14..]);

            if (format != Pcm) throw Bad(name, $"format {format}, not PCM ({Pcm})");
            if (channels != Channels) throw Bad(name, $"{channels} channels, not {Channels}");
            if (rate != GameSound.SampleRate) throw Bad(name, $"{rate} Hz, not {GameSound.SampleRate}");
            if (bits != BitsPerSample) throw Bad(name, $"{bits} bits, not {BitsPerSample}");
            if (blockAlign != Channels * BytesPerSample) throw Bad(name, $"a block of {blockAlign} bytes, not {Channels * BytesPerSample}");
            if (byteRate != GameSound.SampleRate * Channels * BytesPerSample)
                throw Bad(name, $"{byteRate} bytes a second, not {GameSound.SampleRate * Channels * BytesPerSample}");
        }

        /// <summary>The data chunk's samples, little-endian, counted (1 to MaxSamples) before they are read.</summary>
        private static short[] ReadSamples(Stream wav, string name, uint size)
        {
            if (size % BytesPerSample != 0) throw Bad(name, $"a data chunk of {size} bytes, not whole 16-bit samples");
            long count = size / BytesPerSample;
            if (count == 0) throw Bad(name, "its data chunk is empty");
            if (count > GameSound.MaxSamples) throw Bad(name, $"{count} samples, more than {GameSound.MaxSamples} (2 s)");

            var bytes = new byte[size];
            if (Fill(wav, bytes) < bytes.Length) throw Bad(name, "it ends inside its data chunk");
            var samples = new short[count];
            for (var i = 0; i < samples.Length; i++)
                samples[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i * BytesPerSample));
            return samples;
        }

        /// <summary>Past a chunk (or the rest of one) the reader doesn't need: by a seek where the stream can, else by reading.</summary>
        private static void Skip(Stream wav, string name, string chunk, long count)
        {
            if (count <= 0) return;
            if (wav.CanSeek)
            {
                if (wav.Length - wav.Position < count) throw Bad(name, $"it ends inside its {chunk} chunk");
                wav.Position += count;
                return;
            }
            Span<byte> scratch = stackalloc byte[512];
            while (count > 0)
            {
                int read = wav.Read(scratch[..(int)Math.Min(scratch.Length, count)]);
                if (read == 0) throw Bad(name, $"it ends inside its {chunk} chunk");
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

        /// <summary>A chunk's four-letter id as text, a byte outside printable ASCII as '?', so an error can name it.</summary>
        private static string Id(ReadOnlySpan<byte> bytes)
        {
            Span<char> id = stackalloc char[4];
            for (var i = 0; i < id.Length; i++)
                id[i] = bytes[i] is >= 0x20 and < 0x7F ? (char)bytes[i] : '?';
            return new string(id);
        }

        private static InvalidDataException Bad(string name, string what) => new($"Sound '{name}': {what}");
    }
}

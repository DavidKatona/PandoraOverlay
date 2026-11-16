using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PandoraOverlay.Games.Engine;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// A frame compared exactly with its golden picture: the way a game's drawing
/// is checked with no window (A17, A27). A test composes a
/// <see cref="PixelBuffer"/> and calls <see cref="Check(PixelBuffer, string)"/>;
/// the golden picture is <c>Frames/&lt;name&gt;.png</c> in this project's
/// SOURCE folder (found by walking up to <c>PandoraOverlay.sln</c>), checked
/// in. A name may hold folders (<c>"Engine/pixels-composed"</c>).
///
/// <para><b>Exact.</b> The sizes and every pixel must be equal: no tolerance,
/// because the engine's drawing is integer arithmetic with one rounding rule
/// and gives the same pixels on every PC. On a difference the frame is written
/// beside its golden picture as <c>&lt;name&gt;.actual.png</c> (git ignores
/// <c>*.actual.png</c>) and the test fails, saying how many pixels differ and
/// where the first one is. A missing golden picture fails the same way. A
/// check that passes removes a stale <c>.actual.png</c>, so a fixed test leaves
/// nothing behind.</para>
///
/// <para><b>Recording is a deliberate act.</b> Only with the environment
/// variable <c>GAMES_RECORD_FRAMES</c> set to <c>1</c> does a check write the
/// golden picture instead (and pass); a picture whose pixels are already the
/// frame's is left alone, so a recording run changes no file it needn't. A
/// recorded frame is LOOKED AT before it is committed, by the session that
/// recorded it, and the reviewing planner looks again: a golden picture is
/// only as right as the eyes that passed it, and an art change must never
/// rewrite one silently.</para>
///
/// <para><b>The files.</b> <see cref="Save"/> writes a straight-alpha RGBA PNG
/// (the format the art tool writes), un-premultiplying each pixel to the
/// nearest; <see cref="Load"/> reads one through the engine's own decode (the
/// one an atlas's sheet goes through), which premultiplies with
/// <see cref="PixelBuffer.Colour"/>. For every premultiplied pixel the round
/// trip is exact (a test proves it for all of them), so a golden picture holds
/// precisely the frame it was recorded from. A buffer holding a pixel that is
/// not premultiplied can't make that trip and is refused.</para>
/// </summary>
public static class FrameGolden
{
    /// <summary>The environment variable that turns a check into a recording, when it is "1".</summary>
    public const string RecordVariable = "GAMES_RECORD_FRAMES";

    /// <summary>True when this test run records golden pictures instead of checking them.</summary>
    public static bool Recording => Environment.GetEnvironmentVariable(RecordVariable) == "1";

    /// <summary>The frame against <c>Frames/&lt;name&gt;.png</c> in this project's source folder (or recorded there, see the summary).</summary>
    public static void Check(PixelBuffer frame, string name) => Check(frame, name, FramesFolder, Recording);

    /// <summary>A PNG of a buffer, for golden pictures, fixtures, contact sheets and reports. Its folder is made if need be.</summary>
    public static void Save(PixelBuffer frame, string path)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentException.ThrowIfNullOrEmpty(path);
        var straight = new uint[frame.Pixels.Length];
        for (int i = 0; i < straight.Length; i++)
        {
            uint p = frame.Pixels[i];
            uint a = p >> 24;
            uint r = (p >> 16) & 0xFF, g = (p >> 8) & 0xFF, b = p & 0xFF;
            if (r > a || g > a || b > a)
                throw new ArgumentException(
                    $"Pixel ({i % frame.Width}, {i / frame.Width}) is 0x{p:X8}, not a premultiplied colour: it can't be saved and read back the same.",
                    nameof(frame));
            straight[i] = a << 24 | Straight(r, a) << 16 | Straight(g, a) << 8 | Straight(b, a);
        }

        var image = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, straight, frame.Width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (folder is not null) Directory.CreateDirectory(folder);
        using var file = File.Create(path);
        encoder.Save(file);
    }

    /// <summary>A PNG read into a buffer through the engine's own decode, premultiplied as an atlas's sheet is.</summary>
    public static PixelBuffer Load(string path)
    {
        using var file = File.OpenRead(path);
        return PngPixels.Read(file);
    }

    /// <summary>This project's source folder: <c>Games/PandoraOverlay.Games.Tests</c> under the repo root.</summary>
    internal static string TestsFolder => Path.Combine(RepoRoot(), "Games", "PandoraOverlay.Games.Tests");

    /// <summary>Where the golden frames live, in the source folder (never the build's output).</summary>
    internal static string FramesFolder => Path.Combine(TestsFolder, "Frames");

    /// <summary>
    /// The check itself, against any folder: the public one uses
    /// <see cref="FramesFolder"/> and the environment; FrameGolden's own tests
    /// use a temporary folder, so proving that a check fails leaves nothing
    /// in the repo.
    /// </summary>
    internal static void Check(PixelBuffer frame, string name, string folder, bool record)
    {
        ArgumentNullException.ThrowIfNull(frame);
        string golden = GoldenPath(folder, name);
        string actual = ActualPath(golden);

        if (record)
        {
            if (!File.Exists(golden) || Difference(Load(golden), frame) is not null)
                Save(frame, golden);
            File.Delete(actual);
            return;
        }

        if (!File.Exists(golden))
        {
            Save(frame, actual);
            Assert.Fail(
                $"The frame '{name}' has no golden picture ({golden}). It was written to {actual}: look at it, " +
                $"then record it with {RecordVariable}=1 and look at the recorded picture before committing it.");
            return;
        }

        string? difference = Difference(Load(golden), frame);
        if (difference is null)
        {
            File.Delete(actual); // a check that passes leaves nothing behind
            return;
        }
        Save(frame, actual);
        Assert.Fail($"The frame '{name}' differs from its golden picture: {difference}. It was written to {actual}.");
    }

    /// <summary>Null when the two are the same picture; otherwise what differs, in words.</summary>
    internal static string? Difference(PixelBuffer expected, PixelBuffer actual)
    {
        if (expected.Width != actual.Width || expected.Height != actual.Height)
            return $"it is {actual.Width} x {actual.Height}, the golden picture {expected.Width} x {expected.Height}";
        int count = 0, first = -1;
        for (int i = 0; i < expected.Pixels.Length; i++)
        {
            if (expected.Pixels[i] == actual.Pixels[i]) continue;
            if (count++ == 0) first = i;
        }
        if (count == 0) return null;
        int x = first % expected.Width, y = first / expected.Width;
        return $"{count} {(count == 1 ? "pixel differs" : "pixels differ")}, the first at ({x}, {y}): " +
               $"0x{actual.Pixels[first]:X8} where the golden picture has 0x{expected.Pixels[first]:X8}";
    }

    internal static string ActualPath(string golden) => Path.ChangeExtension(golden, ".actual.png");

    /// <summary>The golden picture's path: a relative name, folders allowed, never leaving the folder.</summary>
    private static string GoldenPath(string folder, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (Path.IsPathRooted(name) || name.Split('/', '\\').Any(part => part is "" or "." or ".."))
            throw new ArgumentException($"'{name}' is not a frame name: a relative name such as \"Engine/pixels-composed\".", nameof(name));
        return Path.Combine(folder, name.Replace('/', Path.DirectorySeparatorChar) + ".png");
    }

    /// <summary>
    /// A premultiplied channel back to straight, to the nearest:
    /// c = p * 255 / a rounded. Multiplying again by a / 255 and rounding
    /// (<see cref="PixelBuffer.Colour"/>) gives p back exactly, because the
    /// error of c is at most a half and a / 255 shrinks it below a half.
    /// </summary>
    private static uint Straight(uint channel, uint alpha) =>
        alpha switch
        {
            0 => 0,
            255 => channel,
            _ => (channel * 255 + alpha / 2) / alpha,
        };

    /// <summary>Up from the test's bin folder to the folder holding PandoraOverlay.sln: a worktree finds its own.</summary>
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "PandoraOverlay.sln"))) return dir.FullName;
        throw new InvalidOperationException("The repo root (PandoraOverlay.sln) is not above the test's folder.");
    }
}

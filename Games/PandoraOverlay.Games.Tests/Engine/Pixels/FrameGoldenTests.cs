using System.IO;
using PandoraOverlay.Games.Engine;
using Xunit;
using Xunit.Sdk;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The golden frames: one composed frame checked against its picture in
/// <c>Frames/</c>, and FrameGolden's own behaviour (a changed pixel fails and
/// leaves its <c>.actual.png</c>, a missing picture fails, recording writes)
/// proved in a temporary folder, so the run leaves nothing in the repo.
/// </summary>
public class FrameGoldenTests
{
    /// <summary>
    /// A board-like composition through everything the buffer does: a clear,
    /// an opaque and a half-transparent fill overlapping, the atlas's block
    /// placed by its pivot, the glass faded over it, the block flipped and
    /// half off two edges, a mark clipped at the far corner, an asymmetric
    /// arrow both ways round. Golden picture: <c>Frames/Engine/pixels-composed.png</c>.
    /// </summary>
    [Fact]
    public void AComposedFrameMatchesItsGoldenPicture()
    {
        FrameGolden.Check(Composed(), "Engine/pixels-composed");
    }

    /// <summary>
    /// Every premultiplied pixel there is survives a Save and a Load: each
    /// alpha 0 to 255 with each channel value 0 to alpha, through a real PNG.
    /// So a golden picture holds exactly the frame it was recorded from.
    /// </summary>
    [Fact]
    public void EveryPremultipliedPixelSurvivesAPng()
    {
        var frame = new PixelBuffer(256, 256); // x: a channel's value, y: the alpha
        for (uint a = 0; a < 256; a++)
            for (uint p = 0; p <= a; p++)
                frame.Pixels[a * 256 + p] = a << 24 | p << 16 | (a - p) << 8 | (p * 7 % (a + 1));
        using var temp = new TempFolder();
        string path = Path.Combine(temp.Path, "every.png");

        FrameGolden.Save(frame, path);
        var back = FrameGolden.Load(path);

        Assert.Null(FrameGolden.Difference(frame, back));
    }

    [Fact]
    public void APixelThatIsNotPremultipliedCantBeSaved()
    {
        var frame = new PixelBuffer(2, 1);
        frame.Pixels[1] = 0x10FF0000;
        using var temp = new TempFolder();

        var error = Assert.Throws<ArgumentException>(() => FrameGolden.Save(frame, Path.Combine(temp.Path, "bad.png")));

        Assert.Contains("(1, 0)", error.Message);
    }

    [Fact]
    public void AChangedPixelFailsAndLeavesTheActualPicture()
    {
        using var temp = new TempFolder();
        var frame = Composed();
        FrameGolden.Check(frame, "sub/frame", temp.Path, record: true);
        frame.Pixels[2 * frame.Width + 3] ^= 0x00000001;

        var error = Assert.ThrowsAny<XunitException>(() => FrameGolden.Check(frame, "sub/frame", temp.Path, record: false));

        Assert.Contains("'sub/frame'", error.Message);
        Assert.Contains("1 pixel differs, the first at (3, 2)", error.Message);
        string actual = Path.Combine(temp.Path, "sub", "frame.actual.png");
        Assert.True(File.Exists(actual));
        Assert.Null(FrameGolden.Difference(frame, FrameGolden.Load(actual)));
    }

    [Fact]
    public void ManyChangedPixelsAreCounted()
    {
        using var temp = new TempFolder();
        var frame = new PixelBuffer(4, 4);
        FrameGolden.Check(frame, "frame", temp.Path, record: true);
        frame.Fill(1, 2, 3, 2, 0xFF000000);

        var error = Assert.ThrowsAny<XunitException>(() => FrameGolden.Check(frame, "frame", temp.Path, record: false));

        Assert.Contains("6 pixels differ, the first at (1, 2)", error.Message);
    }

    [Fact]
    public void AFrameOfAnotherSizeFails()
    {
        using var temp = new TempFolder();
        FrameGolden.Check(new PixelBuffer(4, 3), "frame", temp.Path, record: true);

        var error = Assert.ThrowsAny<XunitException>(() => FrameGolden.Check(new PixelBuffer(3, 4), "frame", temp.Path, record: false));

        Assert.Contains("it is 3 x 4, the golden picture 4 x 3", error.Message);
        Assert.True(File.Exists(Path.Combine(temp.Path, "frame.actual.png")));
    }

    [Fact]
    public void AMissingGoldenPictureFailsAndSaysHowToRecordIt()
    {
        using var temp = new TempFolder();

        var error = Assert.ThrowsAny<XunitException>(() => FrameGolden.Check(new PixelBuffer(2, 2), "new", temp.Path, record: false));

        Assert.Contains("no golden picture", error.Message);
        Assert.Contains(FrameGolden.RecordVariable + "=1", error.Message);
        Assert.True(File.Exists(Path.Combine(temp.Path, "new.actual.png")));
        Assert.False(File.Exists(Path.Combine(temp.Path, "new.png")));
    }

    /// <summary>Recording writes the picture, clears a stale actual, and a check then passes; recording the same pixels again leaves the file alone.</summary>
    [Fact]
    public void RecordingWritesTheGoldenPictureOnlyWhenItChanged()
    {
        using var temp = new TempFolder();
        var frame = Composed();
        string golden = Path.Combine(temp.Path, "frame.png");
        File.WriteAllText(Path.Combine(temp.Path, "frame.actual.png"), "stale");

        FrameGolden.Check(frame, "frame", temp.Path, record: true);
        Assert.True(File.Exists(golden));
        Assert.False(File.Exists(Path.Combine(temp.Path, "frame.actual.png")));
        FrameGolden.Check(frame, "frame", temp.Path, record: false);

        var longAgo = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(golden, longAgo);
        FrameGolden.Check(frame, "frame", temp.Path, record: true);
        Assert.Equal(longAgo, File.GetLastWriteTimeUtc(golden)); // the same pixels: untouched

        frame.Pixels[0] = 0xFF000000;
        FrameGolden.Check(frame, "frame", temp.Path, record: true);
        Assert.NotEqual(longAgo, File.GetLastWriteTimeUtc(golden)); // new pixels: rewritten
        FrameGolden.Check(frame, "frame", temp.Path, record: false);
    }

    [Fact]
    public void APassingCheckRemovesAStaleActualPicture()
    {
        using var temp = new TempFolder();
        var frame = new PixelBuffer(2, 2);
        FrameGolden.Check(frame, "frame", temp.Path, record: true);
        string actual = Path.Combine(temp.Path, "frame.actual.png");
        File.WriteAllText(actual, "left over from a failed run");

        FrameGolden.Check(frame, "frame", temp.Path, record: false);

        Assert.False(File.Exists(actual));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../escape")]
    [InlineData("a/../../escape")]
    [InlineData("a//b")]
    [InlineData("C:\\rooted")]
    [InlineData("/rooted")]
    public void ANameMustStayInsideTheFolder(string name)
    {
        using var temp = new TempFolder();

        Assert.ThrowsAny<ArgumentException>(() => FrameGolden.Check(new PixelBuffer(1, 1), name, temp.Path, record: true));
        Assert.Empty(Directory.EnumerateFileSystemEntries(temp.Path));
    }

    /// <summary>The golden frames live in the source folder, never in the build's output.</summary>
    [Fact]
    public void TheFramesFolderIsInTheSource()
    {
        Assert.True(File.Exists(Path.Combine(FrameGolden.TestsFolder, "PandoraOverlay.Games.Tests.csproj")));
        Assert.EndsWith(Path.Combine("PandoraOverlay.Games.Tests", "Frames"), FrameGolden.FramesFolder);
    }

    /// <summary>The composition the golden frame holds: see <see cref="AComposedFrameMatchesItsGoldenPicture"/>.</summary>
    internal static PixelBuffer Composed()
    {
        var atlas = TestAtlas.Load();
        var frame = new PixelBuffer(64, 40);
        frame.Clear(PixelBuffer.Colour(24, 32, 40));                                  // the ground
        frame.Fill(4, 4, 20, 12, PixelBuffer.Colour(60, 140, 60));                     // opaque
        frame.Fill(14, 10, 20, 12, PixelBuffer.Colour(255, 255, 255, 96));             // half, overlapping it
        frame.Draw(atlas["block/stand/0"], 40, 30);                                    // feet on (40, 30): top-left (22, 6)
        frame.Draw(atlas["glass/stand/0"], 30, 20, opacity: 200);                      // faded, over block and ground
        frame.Draw(atlas["block/stand/0"], -6, 50, flip: true);                        // off the left and bottom edges
        frame.Draw(atlas["mark/walk/0"], 63, 40);                                      // clipped at the far corner
        var arrow = Arrow();
        frame.Draw(arrow, new PixelRect(0, 0, 5, 3), 44, 1);                           // pointing right
        frame.Draw(arrow, new PixelRect(0, 0, 5, 3), 52, 1, flip: true);               // pointing left
        frame.Draw(new Sprite("arrow", arrow, new PixelRect(0, 0, 5, 3), 0, 1), 4, 30, opacity: 128);
        return frame;
    }

    /// <summary>A small asymmetric picture, so a flip shows: an arrow pointing right, its shaft white, its head red.</summary>
    private static PixelBuffer Arrow()
    {
        uint w = PixelBuffer.Colour(240, 240, 240), r = PixelBuffer.Colour(220, 40, 40);
        var arrow = new PixelBuffer(5, 3);
        new uint[]
        {
            0, 0, 0, r, 0,
            w, w, w, r, r,
            0, 0, 0, r, 0,
        }.CopyTo(arrow.Pixels, 0);
        return arrow;
    }

    /// <summary>A folder of its own under the temp folder, deleted with everything in it.</summary>
    private sealed class TempFolder : IDisposable
    {
        public TempFolder() => Path = Directory.CreateTempSubdirectory("games-frames-").FullName;

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}

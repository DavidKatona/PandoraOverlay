using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PandoraOverlay.Games.Engine;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The atlas: loaded both ways a game can (its embedded resources, two
/// streams), its pixels premultiplied by the engine's own rule, its lookups,
/// and every rule of the frame table failing with the atlas and the entry
/// named. The fixture is <see cref="TestAtlas"/>.
/// </summary>
public class AtlasTests
{
    #region Loading

    [Fact]
    public void ItLoadsFromItsEmbeddedResourcesAsAGameLoadsItsArt()
    {
        var atlas = TestAtlas.Load();

        Assert.Equal(new[] { "block/stand/0", "glass/stand/0", "mark/walk/0", "mark/walk/1" }, atlas.Names);
        Assert.Equal(new[] { "mark/walk", "mark/blink" }, atlas.Animations);
        var block = atlas["block/stand/0"];
        Assert.Equal("block/stand/0", block.Name);
        Assert.Equal(new PixelRect(0, 0, 36, 25), block.Frame);
        Assert.Equal((18, 24), (block.PivotX, block.PivotY));
        Assert.Equal(new PixelRect(36, 0, 8, 8), atlas["glass/stand/0"].Frame);
        Assert.Equal((4, 7), (atlas["glass/stand/0"].PivotX, atlas["glass/stand/0"].PivotY));
        Assert.Equal(new PixelRect(40, 8, 4, 4), atlas["mark/walk/1"].Frame);
    }

    [Fact]
    public void ItLoadsFromTwoStreamsAndLeavesThemOpen()
    {
        using var png = TestAtlas.Resource("png");
        using var json = TestAtlas.Resource("json");

        var atlas = Atlas.Load(png, json);

        Assert.Equal(TestAtlas.Load().Names, atlas.Names);
        Assert.Equal(TestAtlas.Load().Animations, atlas.Animations);
        Assert.True(png.CanRead); // the caller's to close
        Assert.True(json.CanRead);
    }

    /// <summary>One sheet for every sprite, and its pixels are the PNG's straight bytes premultiplied by Colour.</summary>
    [Fact]
    public void EverySpriteDrawsFromTheOneSheetPremultiplied()
    {
        var atlas = TestAtlas.Load();
        var sheet = atlas["block/stand/0"].Sheet;

        Assert.All(atlas.Names, name => Assert.Same(sheet, atlas[name].Sheet));
        Assert.Equal((TestAtlas.Width, TestAtlas.Height), (sheet.Width, sheet.Height));
        Assert.Equal(TestAtlas.Block, Pixel(sheet, 0, 0));
        Assert.Equal(TestAtlas.Block, Pixel(sheet, 35, 24));
        Assert.Equal(TestAtlas.Glass, Pixel(sheet, 36, 0));
        Assert.Equal(TestAtlas.Glass, Pixel(sheet, 43, 7));
        Assert.Equal(TestAtlas.MarkA, Pixel(sheet, 36, 8));
        Assert.Equal(TestAtlas.MarkB, Pixel(sheet, 43, 11));
        Assert.Equal(0u, Pixel(sheet, 44, 0));
        Assert.Equal(0u, Pixel(sheet, 36, 12));
        Assert.Equal(0u, Pixel(sheet, 47, 24));
        Assert.Null(FrameGolden.Difference(TestAtlas.DrawSheet(), sheet));
    }

    /// <summary>
    /// The PNG holds STRAIGHT alpha, as the art tool writes it: the glass is
    /// (40, 120, 255) at 128 in the file, read here by WPF alone, without the
    /// engine. The engine's premultiplying is what makes it 0x80143C80.
    /// </summary>
    [Fact]
    public void TheFixturePngHoldsStraightAlpha()
    {
        using var png = TestAtlas.Resource("png");
        var decoder = new PngBitmapDecoder(png, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
        BitmapSource image = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        var straight = new uint[TestAtlas.Width * TestAtlas.Height];
        image.CopyPixels(straight, TestAtlas.Width * 4, 0);

        Assert.Equal(0x802878FFu, straight[36]);  // (40, 120, 255) at 128
        Assert.Equal(0xFFC83C28u, straight[0]);
    }

    /// <summary>
    /// The checked-in fixture is exactly what <see cref="TestAtlas.DrawSheet"/>
    /// draws: recorded like a golden frame (<c>GAMES_RECORD_FRAMES=1</c>), so
    /// the PNG is never edited by hand and never drifts from its code.
    /// </summary>
    [Fact]
    public void TheFixtureIsWhatItsCodeDraws()
    {
        FrameGolden.Check(TestAtlas.DrawSheet(), TestAtlas.Name, Path.Combine(FrameGolden.TestsFolder, "Fixtures"), FrameGolden.Recording);
    }

    /// <summary>A PNG of another kind (RGB, no alpha) is converted on the way in: every pixel opaque, its colour kept.</summary>
    [Fact]
    public void APngWithoutAlphaLoadsOpaque()
    {
        // Two pixels of 24-bit BGR: (r, g, b) = (10, 20, 30) and (250, 128, 0).
        var rgb = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgr24, null, new byte[] { 30, 20, 10, 0, 128, 250 }, 6);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rgb));
        using var png = new MemoryStream();
        encoder.Save(png);
        png.Position = 0;

        var atlas = Atlas.Load(png, Text("""{ "frames": { "pair/stand/0": { "x": 0, "y": 0, "w": 2, "h": 1, "px": 1, "py": 0 } } }"""));

        var sheet = atlas["pair/stand/0"].Sheet;
        Assert.Equal(new uint[] { 0xFF0A141E, 0xFFFA8000 }, sheet.Pixels);
        Assert.Empty(atlas.Animations); // a table with no animations is fine
    }

    [Fact]
    public void AMissingResourceIsNamedWithItsAssembly()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Atlas.Load(typeof(AtlasTests).Assembly, "no-such-atlas"));

        Assert.Contains("Art/no-such-atlas.png", error.Message);
        Assert.Contains("PandoraOverlay.Games.Tests", error.Message);
    }

    [Fact]
    public void AnImageThatIsNotAPngIsNamed()
    {
        using var png = new MemoryStream(Encoding.ASCII.GetBytes("not a png at all"));

        var error = Assert.Throws<InvalidDataException>(() => Atlas.Load(png, TestAtlas.Resource("json")));

        Assert.Contains("Atlas 'test-atlas.png'", error.Message);
        Assert.Contains("PNG", error.Message);
    }

    #endregion

    #region Lookups

    [Fact]
    public void AnimationsAreBuiltOverTheSprites()
    {
        var atlas = TestAtlas.Load();

        var walk = atlas.Animation("mark/walk");
        Assert.True(walk.Loops);
        Assert.Equal(new[] { 3, 5 }, walk.Ticks);
        Assert.Same(atlas["mark/walk/0"], walk.At(0));
        Assert.Same(atlas["mark/walk/1"], walk.At(3));
        Assert.Same(atlas["mark/walk/0"], walk.At(8));

        var blink = atlas.Animation("mark/blink");
        Assert.False(blink.Loops);
        Assert.Same(atlas["mark/walk/1"], blink.At(0));
        Assert.Same(atlas["mark/walk/0"], blink.At(100));
    }

    [Fact]
    public void AMissingNameThrowsNamingItAndTheAtlas()
    {
        var atlas = TestAtlas.Load();

        var sprite = Assert.Throws<KeyNotFoundException>(() => atlas["nope/stand/0"]);
        Assert.Contains("'nope/stand/0'", sprite.Message);
        Assert.Contains("'test-atlas' in PandoraOverlay.Games.Tests", sprite.Message);

        var animation = Assert.Throws<KeyNotFoundException>(() => atlas.Animation("nope/walk"));
        Assert.Contains("'nope/walk'", animation.Message);
        Assert.Contains("'test-atlas'", animation.Message);
    }

    [Fact]
    public void TryGetIsForAGameWithAPlanForMissingArt()
    {
        var atlas = TestAtlas.Load();

        Assert.True(atlas.TryGet("glass/stand/0", out var glass));
        Assert.Equal("glass/stand/0", glass.Name);
        Assert.False(atlas.TryGet("nope/stand/0", out var none));
        Assert.Null(none);
    }

    /// <summary>Names are case-sensitive, as a reading spells a species.</summary>
    [Fact]
    public void NamesAreCaseSensitive()
    {
        var atlas = TestAtlas.Load();

        Assert.False(atlas.TryGet("Block/stand/0", out _));
        Assert.Throws<KeyNotFoundException>(() => atlas["BLOCK/STAND/0"]);
        Assert.Throws<KeyNotFoundException>(() => atlas.Animation("Mark/walk"));
    }

    /// <summary>The sheet is shared by every view for the life of the process: drawing onto it throws, drawing from it doesn't.</summary>
    [Fact]
    public void TheSheetIsDrawnFromNeverOnto()
    {
        var atlas = TestAtlas.Load();
        var sheet = atlas["block/stand/0"].Sheet;
        var other = new PixelBuffer(4, 4);

        Assert.Throws<InvalidOperationException>(() => sheet.Clear(0));
        Assert.Throws<InvalidOperationException>(() => sheet.Fill(0, 0, 1, 1, 0xFF000000));
        Assert.Throws<InvalidOperationException>(() => sheet.Draw(other, new PixelRect(0, 0, 1, 1), 0, 0));
        Assert.Throws<InvalidOperationException>(() => sheet.Draw(new Sprite("o", other, new PixelRect(0, 0, 1, 1), 0, 0), 0, 0));
        Assert.Null(FrameGolden.Difference(TestAtlas.DrawSheet(), sheet));

        other.Draw(atlas["mark/walk/0"], 2, 3);
        Assert.Equal(TestAtlas.MarkA, other.Pixels[0]);
    }

    #endregion

    #region The frame table's rules

    /// <summary>
    /// Every rule of the frame table, broken one at a time against the
    /// fixture's 48 x 25 PNG: an <see cref="InvalidDataException"/> naming the
    /// atlas (by the table's image, loaded from streams) and the entry.
    /// </summary>
    [Theory]
    // a frame outside the image, on each side
    [InlineData("""{ "image": "test-atlas.png", "frames": { "f/a/0": { "x": 40, "y": 0, "w": 9, "h": 1, "px": 0, "py": 0 } } }""", "frame 'f/a/0'", "outside the 48 x 25 image")]
    [InlineData("""{ "image": "test-atlas.png", "frames": { "f/a/0": { "x": -1, "y": 0, "w": 2, "h": 1, "px": 0, "py": 0 } } }""", "frame 'f/a/0'", "outside the 48 x 25 image")]
    [InlineData("""{ "image": "test-atlas.png", "frames": { "f/a/0": { "x": 0, "y": 20, "w": 2, "h": 6, "px": 0, "py": 0 } } }""", "frame 'f/a/0'", "outside the 48 x 25 image")]
    [InlineData("""{ "image": "test-atlas.png", "frames": { "f/a/0": { "x": 0, "y": -2, "w": 2, "h": 1, "px": 0, "py": 0 } } }""", "frame 'f/a/0'", "outside the 48 x 25 image")]
    [InlineData("""{ "image": "test-atlas.png", "frames": { "f/a/0": { "x": 2147483647, "y": 0, "w": 2, "h": 1, "px": 0, "py": 0 } } }""", "frame 'f/a/0'", "outside the 48 x 25 image")]
    // a frame of no size
    [InlineData("""{ "image": "test-atlas.png", "frames": { "f/a/0": { "x": 0, "y": 0, "w": 0, "h": 1, "px": 0, "py": 0 } } }""", "frame 'f/a/0'", "at least 1")]
    // a pivot outside its frame (its far edge is allowed: see the next test)
    [InlineData("""{ "image": "test-atlas.png", "frames": { "f/a/0": { "x": 0, "y": 0, "w": 2, "h": 2, "px": 3, "py": 0 } } }""", "frame 'f/a/0'", "pivot (3, 0)")]
    [InlineData("""{ "image": "test-atlas.png", "frames": { "f/a/0": { "x": 0, "y": 0, "w": 2, "h": 2, "px": 0, "py": -1 } } }""", "frame 'f/a/0'", "pivot (0, -1)")]
    [InlineData("""{ "image": "test-atlas.png", "frames": { "f/a/0": { "x": 0, "y": 0, "w": 2, "h": 2, "px": -1, "py": 0 } } }""", "frame 'f/a/0'", "pivot (-1, 0)")]
    [InlineData("""{ "image": "test-atlas.png", "frames": { "f/a/0": { "x": 0, "y": 0, "w": 2, "h": 2, "px": 0, "py": 3 } } }""", "frame 'f/a/0'", "pivot (0, 3)")]
    // a field missing, or not a whole number
    [InlineData("""{ "image": "test-atlas.png", "frames": { "f/a/0": { "x": 0, "y": 0, "h": 1, "px": 0, "py": 0 } } }""", "frame 'f/a/0'", "'w'")]
    [InlineData("""{ "image": "test-atlas.png", "frames": { "f/a/0": { "x": 0, "y": 0, "w": 1.5, "h": 1, "px": 0, "py": 0 } } }""", "frame 'f/a/0'", "'w'")]
    [InlineData("""{ "image": "test-atlas.png", "frames": { "f/a/0": { "x": "0", "y": 0, "w": 1, "h": 1, "px": 0, "py": 0 } } }""", "frame 'f/a/0'", "'x'")]
    [InlineData("""{ "image": "test-atlas.png", "frames": { "f/a/0": 7 } }""", "frame 'f/a/0'", "not an object")]
    // a name twice, or empty
    [InlineData("""{ "image": "test-atlas.png", "frames": { "f/a/0": { "x": 0, "y": 0, "w": 1, "h": 1, "px": 0, "py": 0 }, "f/a/0": { "x": 1, "y": 0, "w": 1, "h": 1, "px": 0, "py": 0 } } }""", "frame 'f/a/0'", "listed twice")]
    [InlineData("""{ "image": "test-atlas.png", "frames": { "": { "x": 0, "y": 0, "w": 1, "h": 1, "px": 0, "py": 0 } } }""", "a frame", "empty name")]
    // an animation naming a frame that isn't there
    [InlineData("""{ "image": "test-atlas.png", "frames": { "f/a/0": { "x": 0, "y": 0, "w": 1, "h": 1, "px": 0, "py": 0 } }, "animations": { "f/a": { "frames": ["f/a/0", "f/a/1"], "ticks": [1, 1], "loops": true } } }""", "animation 'f/a'", "'f/a/1'")]
    // ticks not matching the frames
    [InlineData("""{ "image": "test-atlas.png", "frames": { "f/a/0": { "x": 0, "y": 0, "w": 1, "h": 1, "px": 0, "py": 0 } }, "animations": { "f/a": { "frames": ["f/a/0", "f/a/0"], "ticks": [1], "loops": true } } }""", "animation 'f/a'", "2 frames and 1 tick counts")]
    [InlineData("""{ "image": "test-atlas.png", "frames": { "f/a/0": { "x": 0, "y": 0, "w": 1, "h": 1, "px": 0, "py": 0 } }, "animations": { "f/a": { "frames": ["f/a/0"], "ticks": [0], "loops": true } } }""", "animation 'f/a'", "at least 1")]
    [InlineData("""{ "image": "test-atlas.png", "frames": { "f/a/0": { "x": 0, "y": 0, "w": 1, "h": 1, "px": 0, "py": 0 } }, "animations": { "f/a": { "frames": ["f/a/0"], "ticks": [2.5], "loops": true } } }""", "animation 'f/a'", "'ticks'")]
    [InlineData("""{ "image": "test-atlas.png", "frames": { "f/a/0": { "x": 0, "y": 0, "w": 1, "h": 1, "px": 0, "py": 0 } }, "animations": { "f/a": { "frames": [], "ticks": [], "loops": true } } }""", "animation 'f/a'", "no frame")]
    [InlineData("""{ "image": "test-atlas.png", "frames": { "f/a/0": { "x": 0, "y": 0, "w": 1, "h": 1, "px": 0, "py": 0 } }, "animations": { "f/a": { "frames": [3], "ticks": [1], "loops": true } } }""", "animation 'f/a'", "'frames'")]
    [InlineData("""{ "image": "test-atlas.png", "frames": { "f/a/0": { "x": 0, "y": 0, "w": 1, "h": 1, "px": 0, "py": 0 } }, "animations": { "f/a": { "frames": ["f/a/0"], "loops": true } } }""", "animation 'f/a'", "'ticks'")]
    [InlineData("""{ "image": "test-atlas.png", "frames": { "f/a/0": { "x": 0, "y": 0, "w": 1, "h": 1, "px": 0, "py": 0 } }, "animations": { "f/a": { "frames": ["f/a/0"], "ticks": [1] } } }""", "animation 'f/a'", "'loops'")]
    [InlineData("""{ "image": "test-atlas.png", "frames": { "f/a/0": { "x": 0, "y": 0, "w": 1, "h": 1, "px": 0, "py": 0 } }, "animations": { "f/a": { "frames": ["f/a/0"], "ticks": [1], "loops": true }, "f/a": { "frames": ["f/a/0"], "ticks": [1], "loops": true } } }""", "animation 'f/a'", "listed twice")]
    [InlineData("""{ "image": "test-atlas.png", "frames": { "f/a/0": { "x": 0, "y": 0, "w": 1, "h": 1, "px": 0, "py": 0 } }, "animations": [] }""", "'animations'", "not an object")]
    // the table's own shape
    [InlineData("""{ "image": "test-atlas.png" }""", "'frames'", "missing")]
    [InlineData("""{ "image": "test-atlas.png", "frames": [] }""", "'frames'", "not an object")]
    [InlineData("""[]""", "frame table", "not a JSON object")]
    [InlineData("""{ "image": "test-atlas.png", "frames": { """, "frame table", "not valid JSON")]
    [InlineData("""{ "image": 5, "frames": {} }""", "'image'", "not a string")]
    public void ABrokenTableIsRefusedNamingTheAtlasAndTheEntry(string table, string entry, string what)
    {
        using var png = TestAtlas.Resource("png");

        var error = Assert.Throws<InvalidDataException>(() => Atlas.Load(png, Text(table)));

        Assert.StartsWith("Atlas ", error.Message);
        Assert.Contains(entry, error.Message);
        Assert.Contains(what, error.Message);
        if (!table.StartsWith("""{ "image": 5""") && table != "[]" && !what.Contains("JSON"))
            Assert.Contains("'test-atlas.png'", error.Message);
    }

    /// <summary>A broken table loaded from the assembly names the atlas by its resource and assembly.</summary>
    [Fact]
    public void ATableWithNoImageStillNamesTheAtlas()
    {
        using var png = TestAtlas.Resource("png");

        var error = Assert.Throws<InvalidDataException>(() =>
            Atlas.Load(png, Text("""{ "frames": { "f/a/0": { "x": 99, "y": 0, "w": 1, "h": 1, "px": 0, "py": 0 } } }""")));

        Assert.Contains("(loaded from streams)", error.Message);
        Assert.Contains("frame 'f/a/0'", error.Message);
    }

    /// <summary>The pivot may sit on the frame's far edges (0..w, 0..h): a pivot just past the feet is still the frame's.</summary>
    [Fact]
    public void APivotOnTheFramesFarEdgeIsAllowed()
    {
        using var png = TestAtlas.Resource("png");

        var atlas = Atlas.Load(png, Text("""{ "frames": { "f/a/0": { "x": 0, "y": 0, "w": 2, "h": 3, "px": 2, "py": 3 }, "f/a/1": { "x": 46, "y": 22, "w": 2, "h": 3, "px": 0, "py": 0 } } }"""));

        Assert.Equal((2, 3), (atlas["f/a/0"].PivotX, atlas["f/a/0"].PivotY));
        Assert.Equal(new PixelRect(46, 22, 2, 3), atlas["f/a/1"].Frame); // up to the image's last pixel
    }

    #endregion

    private static uint Pixel(PixelBuffer buffer, int x, int y) => buffer.Pixels[y * buffer.Width + x];

    private static MemoryStream Text(string json) => new(Encoding.UTF8.GetBytes(json));
}

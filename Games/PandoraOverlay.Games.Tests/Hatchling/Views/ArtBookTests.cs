using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The book of art (<see cref="ArtBook"/>): what it finds, what falls back to what
/// (a size to the next larger, an action to <c>stand/0</c>), and that an empty book
/// finds nothing, so the painter's stand-ins take over (H30).
/// </summary>
public class ArtBookTests
{
    // ---- Empty -----------------------------------------------------------------------

    [Fact]
    public void EmptyFindsNothing()
    {
        var book = ArtBook.Empty;
        Assert.Empty(book.Atlases);
        Assert.Null(book.Find("Dryosaurus", "stand", 0));
        Assert.Null(book.Find("Dryosaurus-s", "walk", 3));
        Assert.Null(book.Frame("ordinal", "digit", 2));
        Assert.Null(book.Frame("grass", "tile", 0));
        Assert.Equal(0, book.Variants("grass", "tile"));
        Assert.Equal("Dryosaurus", book.SubjectFor("Dryosaurus", Stages.Hatchling));   // none drawn: the full size stands for it
    }

    // ---- Sizes (H21) -------------------------------------------------------------------

    [Fact]
    public void ASizeFallsBackToTheNextLargerThatExists()
    {
        var medium = MadeAtlas.Solid(26, 18, MadeAtlas.Red);
        var full = MadeAtlas.Solid(36, 25, MadeAtlas.Blue);
        var book = MadeAtlas.Book(("creatures", MadeAtlas.Of(("X-m/stand/0", medium, 13, 17), ("X/stand/0", full, 18, 24))));

        Assert.Equal("X-m", book.SubjectFor("X", Stages.Hatchling));   // -s is not drawn: -m is the next larger
        Assert.Equal("X-m", book.SubjectFor("X", Stages.Juvenile));
        Assert.Equal("X", book.SubjectFor("X", Stages.Subadult));      // -l is not drawn: the full size
        Assert.Equal("X", book.SubjectFor("X", Stages.Adult));
        Assert.Equal("X", book.SubjectFor("X", Stages.Elder));

        // The scene names the size it wants and the book answers the size drawn.
        Assert.Equal("X-m/stand/0", book.Find("X-s", "stand", 0)!.Name);
        Assert.Equal("X-m/stand/0", book.Find("X-m", "stand", 0)!.Name);
        Assert.Equal("X/stand/0", book.Find("X-l", "stand", 0)!.Name);
        Assert.Equal("X/stand/0", book.Find("X", "stand", 0)!.Name);
        Assert.Null(book.Find("Y-s", "stand", 0));
    }

    [Fact]
    public void ASmallerSizeIsNeverTheFallback()
    {
        var small = MadeAtlas.Solid(20, 14, MadeAtlas.Red);
        var book = MadeAtlas.Book(("creatures", MadeAtlas.Of(("X-s/stand/0", small, 10, 13))));
        Assert.Equal("X-s", book.SubjectFor("X", Stages.Hatchling));
        Assert.Equal("X", book.SubjectFor("X", Stages.Juvenile));   // nothing larger drawn: the full size, not drawn either
        Assert.Null(book.Find("X-m", "stand", 0));
    }

    [Theory]
    [InlineData(Stages.Hatchling, "Dryosaurus-s")]
    [InlineData(Stages.Juvenile, "Dryosaurus-m")]
    [InlineData(Stages.Subadult, "Dryosaurus-l")]
    [InlineData(Stages.Adult, "Dryosaurus")]
    [InlineData(Stages.Elder, "Dryosaurus")]
    public void EachStageHasItsSize(int stage, string subject) =>
        Assert.Equal(subject, ArtBook.StageSubject("Dryosaurus", stage));

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void AStageOutsideTheFiveIsADefect(int stage) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ArtBook.StageSubject("Dryosaurus", stage));

    // ---- Actions ---------------------------------------------------------------------

    [Fact]
    public void AnActionNotDrawnIsStandZero()
    {
        var book = MadeAtlas.Book(("creatures", MadeAtlas.Of(
            ("X/stand/0", MadeAtlas.Solid(4, 4, MadeAtlas.Red), 2, 3),
            ("X/stand/1", MadeAtlas.Solid(4, 4, MadeAtlas.Blue), 2, 3))));
        Assert.Equal("X/stand/0", book.Find("X", "walk", 7)!.Name);
        Assert.Equal("X/stand/0", book.Find("X", "strike", 0)!.Name);
    }

    [Fact]
    public void AnActionIsItsAnimationAtTheTickElseItsFrameZero()
    {
        var walk = """{ "X/walk": { "frames": ["X/walk/0", "X/walk/1"], "ticks": [3, 3], "loops": true } }""";
        var book = MadeAtlas.Book(("creatures", MadeAtlas.Of(walk,
            ("X/stand/0", MadeAtlas.Solid(4, 4, MadeAtlas.Red), 2, 3),
            ("X/walk/0", MadeAtlas.Solid(4, 4, MadeAtlas.Green), 2, 3),
            ("X/walk/1", MadeAtlas.Solid(4, 4, MadeAtlas.Blue), 2, 3),
            ("X/eat/0", MadeAtlas.Solid(4, 4, MadeAtlas.Green), 2, 3),
            ("X/eat/1", MadeAtlas.Solid(4, 4, MadeAtlas.Blue), 2, 3))));
        Assert.Equal("X/walk/0", book.Find("X", "walk", 0)!.Name);
        Assert.Equal("X/walk/1", book.Find("X", "walk", 4)!.Name);
        Assert.Equal("X/walk/0", book.Find("X", "walk", 6)!.Name);   // it loops
        Assert.Equal("X/eat/0", book.Find("X", "eat", 5)!.Name);     // no animation: frame 0
    }

    // ---- Exact frames and variants ------------------------------------------------------

    [Fact]
    public void AFrameIsExactlyThatFrameOrNothing()
    {
        var digits = Enumerable.Range(0, 7)
            .Select(d => ($"ordinal/digit/{d}", MadeAtlas.Solid(5, 7, MadeAtlas.Red), 0, 0)).ToArray();
        var book = MadeAtlas.Book(("things", MadeAtlas.Of(digits)));
        Assert.Equal("ordinal/digit/6", book.Frame("ordinal", "digit", 6)!.Name);
        Assert.Null(book.Frame("ordinal", "digit", 7));    // never another digit in its place
        Assert.Null(book.Frame("ordinal", "digit", -1));
        Assert.Null(book.Frame("ordinal", "sign", 0));     // never stand/0 either
    }

    [Fact]
    public void VariantsCountTheFramesFromZeroWithNoGap()
    {
        var tile = MadeAtlas.Solid(4, 4, MadeAtlas.Green);
        var book = MadeAtlas.Book(("terrain", MadeAtlas.Of(
            ("grass/tile/0", tile, 0, 0), ("grass/tile/1", tile, 0, 0), ("grass/tile/2", tile, 0, 0),
            ("rock/tile/0", tile, 0, 0), ("rock/tile/2", tile, 0, 0))));
        Assert.Equal(3, book.Variants("grass", "tile"));
        Assert.Equal(1, book.Variants("rock", "tile"));
        Assert.Equal(0, book.Variants("water", "tile"));
    }

    // ---- Load ------------------------------------------------------------------------

    [Fact]
    public void LoadFindsTheGamesEmbeddedAtlases()
    {
        var book = ArtBook.Load(typeof(Island).Assembly);
        Assert.Contains("creatures", book.Atlases);   // A13's: the 21 standing sprites
        foreach (var species in SpeciesTable.All)
        {
            Assert.NotNull(book.Find(species.Name, "stand", 0));
            Assert.NotNull(book.Find(ArtBook.StageSubject(species.Name, Stages.Hatchling), "stand", 0));   // a size not drawn yet is the full size
        }
    }

    /// <summary>
    /// Every piece the painter asks for is found by the name it asks with, through
    /// <see cref="ArtNames"/>' constants. The golden frames are painted with no art, so a
    /// name that drifts from the art tool's would otherwise fail nothing: the painter
    /// would quietly draw the stand-in. What later art tasks will draw (the sizes, the
    /// actions but stand, the five other marks, portraits) is left out on purpose: this
    /// must pass before and after they arrive.
    /// </summary>
    [Fact]
    public void EveryPieceThePainterAsksForIsFoundByItsName()
    {
        var book = ArtBook.Load(typeof(Island).Assembly);
        foreach (var atlas in new[] { "creatures", "terrain", "things" })
            Assert.Contains(atlas, book.Atlases);

        var wanted = new List<(string Subject, string Action, int Index)>();
        foreach (var ground in new[] { Terrain.Grass, Terrain.Forest, Terrain.Water, Terrain.Rock })
            wanted.Add((ArtNames.TerrainOf(ground), ArtNames.Tile, 0));
        wanted.Add((ArtNames.Fog, ArtNames.Unseen, 0));
        wanted.Add((ArtNames.Fog, ArtNames.Dim, 0));
        wanted.Add((ArtNames.Mark, ArtNames.Move, 0));
        wanted.Add((ArtNames.Mark, ArtNames.Move, 1));
        wanted.Add((ArtNames.Mark, ArtNames.Attack, 0));
        wanted.Add((ArtNames.Mark, ArtNames.Hover, 0));
        wanted.Add((ArtNames.Plant, ArtNames.Stand, 0));
        wanted.Add((ArtNames.Meat, ArtNames.Pile, 0));
        wanted.Add((ArtNames.Meat, ArtNames.Pile, 1));
        wanted.Add((ArtNames.SmallGame, ArtNames.Stand, 0));
        foreach (var ring in new[] { ArtNames.You, ArtNames.Hostile, ArtNames.Apex, ArtNames.Acting })
            wanted.Add((ArtNames.Ring, ring, 0));
        wanted.Add((ArtNames.Ring, ArtNames.Acting, 1));
        wanted.Add((ArtNames.Mark, ArtNames.Hunt, 0));
        wanted.Add((ArtNames.Mark, ArtNames.Search, 0));
        wanted.Add((ArtNames.Mark, ArtNames.You, 0));
        wanted.Add((ArtNames.Mark, ArtNames.You, 1));
        for (var d = 0; d <= 9; d++)
        {
            wanted.Add((ArtNames.Ordinal, ArtNames.Digit, d));
            wanted.Add((ArtNames.NumberHurt, ArtNames.Digit, d));
            wanted.Add((ArtNames.NumberGood, ArtNames.Digit, d));
        }
        wanted.Add((ArtNames.NumberHurt, ArtNames.Sign, 0));
        wanted.Add((ArtNames.NumberGood, ArtNames.Sign, 0));
        foreach (var species in SpeciesTable.All)
            wanted.Add((species.Name, ArtNames.Stand, 0));
        foreach (var type in Enum.GetValues<PredatorType>())
            wanted.Add((ArtNames.PredatorOf(type), ArtNames.Stand, 0));

        var missing = wanted.Where(w => book.Frame(w.Subject, w.Action, w.Index) is null)
            .Select(w => $"{w.Subject}/{w.Action}/{w.Index}").ToList();
        Assert.True(missing.Count == 0, "The painter asks for art the atlases do not hold by that name:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void LoadOfAnAssemblyWithNoArtIsAnEmptyBook()
    {
        var book = ArtBook.Load(typeof(Atlas).Assembly);   // the engine embeds no Art/
        Assert.Empty(book.Atlases);
        Assert.Null(book.Find("Dryosaurus", "stand", 0));
    }

    [Fact]
    public void APieceOfArtInTwoAtlasesIsADefect()
    {
        var one = MadeAtlas.Of(("X/stand/0", MadeAtlas.Solid(4, 4, MadeAtlas.Red), 2, 3));
        var two = MadeAtlas.Of(("X/stand/0", MadeAtlas.Solid(4, 4, MadeAtlas.Blue), 2, 3));
        var error = Assert.Throws<InvalidDataException>(() => MadeAtlas.Book(("creatures", one), ("things", two)));
        Assert.Contains("X/stand/0", error.Message);
    }

    [Fact]
    public void AFrameNamedOutsideTheArtsFormIsADefect()
    {
        var odd = MadeAtlas.Of(("X/stand", MadeAtlas.Solid(4, 4, MadeAtlas.Red), 2, 3));
        Assert.Throws<InvalidDataException>(() => MadeAtlas.Book(("creatures", odd)));
    }
}

/// <summary>
/// Atlases made in a test, in memory: pictures laid side by side on one sheet,
/// encoded as the art tool writes them (a straight-alpha PNG and its frame table),
/// and loaded through the engine's own <see cref="Atlas.Load(Stream, Stream)"/>, so a
/// test of the painter or the book holds exactly the pixels it chose.
/// </summary>
internal static class MadeAtlas
{
    public static readonly uint Red = PixelBuffer.Colour(220, 40, 40);
    public static readonly uint Green = PixelBuffer.Colour(40, 200, 60);
    public static readonly uint Blue = PixelBuffer.Colour(40, 80, 220);

    /// <summary>A picture of one colour.</summary>
    public static PixelBuffer Solid(int width, int height, uint colour)
    {
        var picture = new PixelBuffer(width, height);
        picture.Clear(colour);
        return picture;
    }

    /// <summary>A book of named atlases.</summary>
    public static ArtBook Book(params (string Name, Atlas Atlas)[] atlases) => ArtBook.From(atlases);

    /// <summary>An atlas of these frames and no animation.</summary>
    public static Atlas Of(params (string Name, PixelBuffer Picture, int PivotX, int PivotY)[] frames) => Of(null, frames);

    /// <summary>An atlas of these frames and the animations table given as JSON (an object), or none.</summary>
    public static Atlas Of(string? animations, params (string Name, PixelBuffer Picture, int PivotX, int PivotY)[] frames)
    {
        var width = frames.Sum(f => f.Picture.Width);
        var height = frames.Max(f => f.Picture.Height);
        var sheet = new PixelBuffer(width, height);
        var json = new StringBuilder("{ \"image\": \"made.png\", \"frames\": {");
        var x = 0;
        for (var i = 0; i < frames.Length; i++)
        {
            var (name, picture, px, py) = frames[i];
            sheet.Draw(picture, new PixelRect(0, 0, picture.Width, picture.Height), x, 0);
            json.Append(i == 0 ? "" : ",")
                .Append($" \"{name}\": {{ \"x\": {x}, \"y\": 0, \"w\": {picture.Width}, \"h\": {picture.Height}, \"px\": {px}, \"py\": {py} }}");
            x += picture.Width;
        }
        json.Append(" }");
        if (animations is not null) json.Append(", \"animations\": ").Append(animations);
        json.Append(" }");
        using var png = new MemoryStream(Png(sheet));
        using var table = new MemoryStream(Encoding.UTF8.GetBytes(json.ToString()));
        return Atlas.Load(png, table);
    }

    /// <summary>A buffer as a straight-alpha PNG, as the art tool writes one (each channel back from premultiplied to the nearest).</summary>
    public static byte[] Png(PixelBuffer buffer)
    {
        var straight = new uint[buffer.Pixels.Length];
        for (var i = 0; i < straight.Length; i++)
        {
            var p = buffer.Pixels[i];
            uint a = p >> 24;
            straight[i] = a << 24 | Straight((p >> 16) & 0xFF, a) << 16 | Straight((p >> 8) & 0xFF, a) << 8 | Straight(p & 0xFF, a);
        }
        var image = BitmapSource.Create(buffer.Width, buffer.Height, 96, 96, PixelFormats.Bgra32, null, straight, buffer.Width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();

        static uint Straight(uint channel, uint alpha) => alpha switch
        {
            0 => 0,
            255 => channel,
            _ => (channel * 255 + alpha / 2) / alpha,
        };
    }
}

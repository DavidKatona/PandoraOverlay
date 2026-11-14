using System.IO;
using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;
using Xunit;
using Xunit.Abstractions;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The picture on Hatchling's tile (<see cref="TileArt"/>): the board in small, by the
/// board's own painter, so its stand-ins are the board's (H30). The golden frame is
/// painted with no art, so it never changes when art does.
/// </summary>
public class TileArtTests
{
    private readonly ITestOutputHelper _output;

    public TileArtTests(ITestOutputHelper output) => _output = output;

    private static PixelBuffer Painted(ArtBook art, string species)
    {
        var buffer = new PixelBuffer(TileArt.Width, TileArt.Height);
        TileArt.Paint(buffer, art, species);
        return buffer;
    }

    [Fact]
    public void Tile() => FrameGolden.Check(Painted(ArtBook.Empty, "Dryosaurus"), "Hatchling/tile");

    [Fact]
    public void TheTileIsTheFixedStripWithYouAndAHuntingTroodon()
    {
        var scene = TileArt.SceneFor("Dryosaurus");
        Assert.Equal(new[]
        {
            Terrain.Forest, Terrain.Forest, Terrain.Grass, Terrain.Grass, Terrain.Water, Terrain.Grass,
            Terrain.Grass, Terrain.Forest, Terrain.Grass, Terrain.Grass, Terrain.Grass, Terrain.Water,
        }, scene.Tiles);

        var you = scene.Sprites.Single(s => s.Kind == SpriteKind.You);
        Assert.Equal("Dryosaurus-s", you.Subject);   // the smallest size (H21)
        Assert.Equal(Ring.You, you.Ring);
        Assert.False(you.Flip);
        var troodon = scene.Sprites.Single(s => s.Kind == SpriteKind.Predator);
        Assert.Equal("Troodon", troodon.Subject);
        Assert.Equal((Ring.Hostile, Marks.Hunt, true), (troodon.Ring, troodon.Marks, troodon.Flip));   // facing you
        Assert.True(troodon.X > you.X);

        // Both stand well inside the tile, their marks and rings included.
        foreach (var sprite in scene.Sprites)
        {
            Assert.InRange(sprite.X - 18, 0, TileArt.Width - 36);
            Assert.InRange(sprite.Y - 9 - 19 - 4, 0, TileArt.Height - 1);
            Assert.InRange(sprite.Y + 7, 0, TileArt.Height - 1);
        }
    }

    [Fact]
    public void TheStripsGroundIsCentredOnTheTile()
    {
        var frame = Painted(ArtBook.Empty, "Dryosaurus");
        var colours = new Dictionary<Terrain, uint>
        {
            [Terrain.Grass] = HatchlingPalette.Grass, [Terrain.Forest] = HatchlingPalette.Forest, [Terrain.Water] = HatchlingPalette.Water,
        };
        var scene = TileArt.SceneFor("Dryosaurus");
        var checkedCells = 0;
        for (var cell = 0; cell < scene.Tiles.Length; cell++)
        {
            var (x, y) = TileArt.Hexes.Centre(cell);
            if (x < 0 || x >= TileArt.Width) continue;   // the strip is wider than the tile: its ends are cut
            Assert.Equal(colours[scene.Tiles[cell]], frame.Pixels[y * frame.Width + x]);
            checkedCells++;
        }
        Assert.Equal(10, checkedCells);
        Assert.Equal(HatchlingPalette.Ground, frame.Pixels[0]);   // above the first row's points
        Assert.Equal(TileArt.Width - TileArt.Hexes.Width, 2 * TileArt.Hexes.TopLeft(0).X);   // the same cut both ends
    }

    [Fact]
    public void CreateIsASurfaceOfThePaintedPictureAtTheContractsSize()
    {
        Assert.Equal(GameFrame.TileArtWidth, TileArt.Width);
        Assert.Equal(GameFrame.TileArtHeight, TileArt.Height);
        Sta.Run(() =>
        {
            var art = ArtBook.Load(typeof(Island).Assembly);
            var element = TileArt.Create(art, "Dryosaurus");
            var surface = Assert.IsType<PixelSurface>(element);
            Assert.Equal(Painted(art, "Dryosaurus").Pixels, surface.Buffer.Pixels);
            surface.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            Assert.Equal(new System.Windows.Size(232, 96), surface.DesiredSize);
        });
    }

    [Fact]
    public void WithTheRealArtTheTilePaintsAndDiffers()
    {
        var art = ArtBook.Load(typeof(Island).Assembly);
        var real = Painted(art, "Dryosaurus");
        Assert.NotEqual(Painted(ArtBook.Empty, "Dryosaurus").Pixels, real.Pixels);
        Assert.Equal(real.Pixels, Painted(art, "Dryosaurus").Pixels);   // the same every time
        foreach (var species in SpeciesTable.All) Painted(art, species.Name);   // every species paints
        Painted(art, "Nobody");   // a species with no art: its ring alone, and nothing throws

        var folder = Environment.GetEnvironmentVariable(BoardPainterTests.PreviewVariable);
        if (!string.IsNullOrEmpty(folder))
        {
            FrameGolden.Save(real, Path.Combine(folder, "tile-real.png"));
            _output.WriteLine($"Wrote {Path.Combine(folder, "tile-real.png")}");
        }
    }
}

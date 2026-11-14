using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;
using Xunit;
using Xunit.Abstractions;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The art is checked against the engine's rule, never the other way (A28): every
/// hex tile in the game's <c>terrain</c> atlas covers exactly the pixels
/// <see cref="HexLayout.InCell"/> gives its cell, so tiles interlock with no gap
/// and a click lands on the tile it looks like. Owed to the architecture by the
/// first game with hex tiles.
///
/// <para>The tiles are every <c>&lt;terrain&gt;/tile/&lt;n&gt;</c>, <c>fog/unseen/0</c> and
/// <c>fog/dim/0</c>. "Covers" is any pixel not fully transparent: <c>fog/dim</c> is
/// translucent all over by design. While no <c>terrain</c> atlas is embedded (task
/// H10 draws it) the test has nothing to check and says so; the second test shows
/// the check failing on a tile damaged in memory, so it will fail the day a wrong
/// tile is checked in.</para>
/// </summary>
public class TileMaskTests
{
    private readonly ITestOutputHelper _output;

    public TileMaskTests(ITestOutputHelper output) => _output = output;

    /// <summary>Whether a frame's name is a hex tile's.</summary>
    internal static bool IsTile(string name)
    {
        var parts = name.Split('/');
        return (parts.Length == 3 && parts[1] == ArtNames.Tile) || name == "fog/unseen/0" || name == "fog/dim/0";
    }

    /// <summary>What is wrong with the atlas's tiles, one line a tile: its size, its pivot, or the first pixel off the outline and how many are.</summary>
    internal static List<string> MaskProblems(Atlas atlas)
    {
        var problems = new List<string>();
        foreach (var name in atlas.Names.Where(IsTile))
        {
            var sprite = atlas[name];
            var frame = sprite.Frame;
            if (frame.Width != BoardLayout.CellWidth || frame.Height != BoardLayout.CellHeight)
            {
                problems.Add($"{name}: {frame.Width} x {frame.Height}, a cell is {BoardLayout.CellWidth} x {BoardLayout.CellHeight}");
                continue;
            }
            if (sprite.PivotX != 0 || sprite.PivotY != 0)
                problems.Add($"{name}: its pivot is ({sprite.PivotX}, {sprite.PivotY}), a tile's is (0, 0)");
            var wrong = 0;
            (int X, int Y)? first = null;
            for (var y = 0; y < frame.Height; y++)
                for (var x = 0; x < frame.Width; x++)
                {
                    var covered = sprite.Sheet.Pixels[(frame.Y + y) * sprite.Sheet.Width + frame.X + x] >> 24 != 0;
                    if (covered == HexLayout.InCell(x, y, BoardLayout.CellWidth, BoardLayout.CellHeight, BoardLayout.RowPitch)) continue;
                    wrong++;
                    first ??= (x, y);
                }
            if (wrong > 0)
                problems.Add($"{name}: {wrong} pixels off the cell's outline, the first at {first}");
        }
        return problems;
    }

    [Fact]
    public void EveryTerrainTileIsExactlyTheCellsOutline()
    {
        var assembly = typeof(Island).Assembly;
        if (!assembly.GetManifestResourceNames().Contains("Art/terrain.png"))
        {
            _output.WriteLine("No terrain atlas is embedded yet: no tile to check.");
            return;
        }
        var atlas = Atlas.Load(assembly, "terrain");
        var tiles = atlas.Names.Where(IsTile).ToList();
        _output.WriteLine($"Checked {tiles.Count} tiles: {string.Join(", ", tiles)}");
        Assert.NotEmpty(tiles);
        var problems = MaskProblems(atlas);
        Assert.True(problems.Count == 0, "A tile must cover exactly its cell's outline (A28):\n" + string.Join("\n", problems));
    }

    /// <summary>A tile of the outline exactly, in one colour.</summary>
    private static PixelBuffer Exact(uint colour)
    {
        var tile = new PixelBuffer(BoardLayout.CellWidth, BoardLayout.CellHeight);
        foreach (var span in BoardLayout.CellSpans)
            tile.Fill(span.X, span.Y, span.Length, 1, colour);
        return tile;
    }

    [Fact]
    public void TheCheckFailsOnADamagedTile()
    {
        var grass = Exact(HatchlingPalette.Grass);
        var dim = Exact(HatchlingPalette.Dim);
        var unseen = Exact(HatchlingPalette.Unseen);
        Assert.Empty(MaskProblems(MadeAtlas.Of(("grass/tile/0", grass, 0, 0), ("fog/dim/0", dim, 0, 0), ("fog/unseen/0", unseen, 0, 0))));

        // A pixel of the cell left out: a gap between two tiles.
        var holed = Exact(HatchlingPalette.Grass);
        holed.Pixels[25 * BoardLayout.CellWidth + 0] = 0;
        var gap = MaskProblems(MadeAtlas.Of(("grass/tile/0", grass, 0, 0), ("grass/tile/1", holed, 0, 0)));
        Assert.Equal("grass/tile/1: 1 pixels off the cell's outline, the first at (0, 25)", Assert.Single(gap));

        // A pixel outside the cell drawn: the tile spills onto its neighbour.
        var spilled = Exact(HatchlingPalette.Water);
        spilled.Fill(0, 0, 1, 1, HatchlingPalette.Water);
        Assert.Contains("water/tile/0", Assert.Single(MaskProblems(MadeAtlas.Of(("water/tile/0", spilled, 0, 0)))));

        // A fog tile and a translucent pixel count as covering; a wrong size or pivot is named.
        var fog = Exact(HatchlingPalette.Dim);
        fog.Pixels[0] = HatchlingPalette.Faded(HatchlingPalette.Dim, 2);
        Assert.Single(MaskProblems(MadeAtlas.Of(("fog/dim/0", fog, 0, 0))));
        Assert.Contains("44 x 50", Assert.Single(MaskProblems(MadeAtlas.Of(("rock/tile/0", MadeAtlas.Solid(40, 50, HatchlingPalette.Rock), 0, 0)))));
        Assert.Contains("pivot", Assert.Single(MaskProblems(MadeAtlas.Of(("forest/tile/0", Exact(HatchlingPalette.Forest), 22, 25)))));

        // Marks and other art are not tiles.
        Assert.Empty(MaskProblems(MadeAtlas.Of(("mark/move/0", MadeAtlas.Solid(44, 50, HatchlingPalette.MoveRim), 0, 0))));
    }
}

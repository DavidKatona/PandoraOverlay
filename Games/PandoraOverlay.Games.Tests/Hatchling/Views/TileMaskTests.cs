using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;
using Xunit;
using Xunit.Abstractions;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The art is checked against the engine's rule, never the other way (A28): every
/// hex tile and every cell rim in the game's <c>terrain</c> atlas lies exactly on the
/// pixels <see cref="HexLayout.InCell"/> gives its cell, so tiles interlock with no
/// gap, a rim never spills onto a neighbour, and a click lands on the tile it looks
/// like. Owed to the architecture by the first game with hex tiles.
///
/// <para><b>Tiles</b> (every <c>&lt;terrain&gt;/tile/&lt;n&gt;</c>, <c>fog/unseen/0</c>,
/// <c>fog/dim/0</c>): 44 x 50, pivot (0, 0); a pixel off the mask has alpha 0; a pixel
/// on it has alpha 255, and on <c>fog/dim/0</c> alpha 184 (the veil laid over a seen
/// tile out of sight). <b>Rims</b> (<c>mark/move/0</c> and <c>/1</c>,
/// <c>mark/attack/0</c>, <c>mark/hover/0</c>): 44 x 50, pivot (0, 0), no pixel with
/// alpha above 0 off the mask, and at least one on it.</para>
///
/// <para>The atlas's sheet holds its pixels premultiplied (the engine's
/// <c>PngPixels</c> premultiplies as it reads the PNG), and the alpha is the top byte
/// either way: premultiplying changes the colour channels, never the alpha. So the
/// alphas read here are the PNG's own.</para>
///
/// <para>The test asserts the atlas is embedded and that it found a tile of each of
/// the four terrains, both fogs and the four rims: a test that passed by finding
/// nothing would hide a broken embed. The number of variants is not pinned (art may
/// add one). The second test shows every check failing on a piece damaged in
/// memory.</para>
/// </summary>
public class TileMaskTests
{
    private readonly ITestOutputHelper _output;

    public TileMaskTests(ITestOutputHelper output) => _output = output;

    /// <summary>The alpha of a tile's pixels on the mask; the dim veil's own is <see cref="DimAlpha"/>.</summary>
    private const uint Opaque = 255;

    /// <summary><c>fog/dim/0</c>'s alpha on the mask: the veil's.</summary>
    private const uint DimAlpha = 184;

    private const string DimTile = "fog/dim/0";

    /// <summary>The four rims: where you can move (two frames), what you can attack, the cell under the mouse.</summary>
    internal static readonly string[] Rims = { "mark/move/0", "mark/move/1", "mark/attack/0", "mark/hover/0" };

    /// <summary>Whether a frame's name is a hex tile's.</summary>
    internal static bool IsTile(string name)
    {
        var parts = name.Split('/');
        return (parts.Length == 3 && parts[1] == ArtNames.Tile) || name == "fog/unseen/0" || name == DimTile;
    }

    /// <summary>Whether a frame's name is one of the four rims.</summary>
    internal static bool IsRim(string name) => Rims.Contains(name);

    /// <summary>What is wrong with the atlas's tiles and rims, one line a piece: its size, its pivot, or the first pixel that breaks its rule and how many do.</summary>
    internal static List<string> MaskProblems(Atlas atlas)
    {
        var problems = new List<string>();
        foreach (var name in atlas.Names.Where(n => IsTile(n) || IsRim(n)))
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

            var rim = IsRim(name);
            var onMask = name == DimTile ? DimAlpha : Opaque;
            var wrong = 0;
            var covered = 0;
            (int X, int Y, uint Alpha)? first = null;
            for (var y = 0; y < frame.Height; y++)
                for (var x = 0; x < frame.Width; x++)
                {
                    var alpha = sprite.Sheet.Pixels[(frame.Y + y) * sprite.Sheet.Width + frame.X + x] >> 24;
                    var inCell = HexLayout.InCell(x, y, BoardLayout.CellWidth, BoardLayout.CellHeight, BoardLayout.RowPitch);
                    if (alpha > 0 && inCell) covered++;
                    var right = !inCell ? alpha == 0 : rim || alpha == onMask;
                    if (right) continue;
                    wrong++;
                    first ??= (x, y, alpha);
                }
            if (wrong > 0)
                problems.Add($"{name}: {wrong} pixels break its rule, the first at ({first!.Value.X}, {first.Value.Y}) with alpha {first.Value.Alpha}");
            if (rim && covered == 0)
                problems.Add($"{name}: an empty rim, no pixel on the cell");
        }
        return problems;
    }

    [Fact]
    public void EveryTileAndRimLiesExactlyOnTheCellsOutline()
    {
        var assembly = typeof(Island).Assembly;
        Assert.Contains("Art/terrain.png", assembly.GetManifestResourceNames());
        var atlas = Atlas.Load(assembly, "terrain");
        var checkedNames = atlas.Names.Where(n => IsTile(n) || IsRim(n)).ToList();
        _output.WriteLine($"Checked {checkedNames.Count}: {string.Join(", ", checkedNames)}");

        foreach (var ground in new[] { Terrain.Grass, Terrain.Forest, Terrain.Water, Terrain.Rock })
            Assert.Contains($"{ArtNames.TerrainOf(ground)}/{ArtNames.Tile}/0", checkedNames);
        Assert.Contains("fog/unseen/0", checkedNames);
        Assert.Contains(DimTile, checkedNames);
        foreach (var rim in Rims) Assert.Contains(rim, checkedNames);

        var problems = MaskProblems(atlas);
        Assert.True(problems.Count == 0, "Every tile and rim lies exactly on its cell's outline (A28):\n" + string.Join("\n", problems));
    }

    /// <summary>A piece on the outline exactly (or on its rim), in one colour.</summary>
    private static PixelBuffer Exact(uint colour, bool rim = false)
    {
        var piece = new PixelBuffer(BoardLayout.CellWidth, BoardLayout.CellHeight);
        foreach (var span in rim ? BoardLayout.RimSpans : BoardLayout.CellSpans)
            piece.Fill(span.X, span.Y, span.Length, 1, colour);
        return piece;
    }

    /// <summary>A pixel of the cell (the middle of its left side), as an index into a piece's pixels.</summary>
    private const int Inside = 25 * BoardLayout.CellWidth + 0;

    [Fact]
    public void TheCheckFailsOnADamagedTile()
    {
        var good = MadeAtlas.Of(("grass/tile/0", Exact(HatchlingPalette.Grass), 0, 0), ("fog/dim/0", Exact(HatchlingPalette.Dim), 0, 0),
            ("fog/unseen/0", Exact(HatchlingPalette.Unseen), 0, 0), ("mark/move/0", Exact(HatchlingPalette.MoveRim, rim: true), 0, 0),
            ("mark/hover/0", Exact(HatchlingPalette.HoverRim, rim: true), 0, 0));
        Assert.Empty(MaskProblems(good));

        // A pixel of the cell left out: a gap between two tiles.
        var holed = Exact(HatchlingPalette.Grass);
        holed.Pixels[Inside] = 0;
        Assert.Equal("grass/tile/1: 1 pixels break its rule, the first at (0, 25) with alpha 0",
            Assert.Single(MaskProblems(MadeAtlas.Of(("grass/tile/1", holed, 0, 0)))));

        // A pixel outside the cell drawn: the tile spills onto its neighbour.
        var spilled = Exact(HatchlingPalette.Water);
        spilled.Fill(0, 0, 1, 1, HatchlingPalette.Water);
        Assert.Contains("water/tile/0", Assert.Single(MaskProblems(MadeAtlas.Of(("water/tile/0", spilled, 0, 0)))));

        // A tile pixel of alpha 254: not quite opaque.
        var thin = Exact(HatchlingPalette.Grass);
        thin.Pixels[Inside] = HatchlingPalette.Faded(HatchlingPalette.Grass, 254);
        Assert.Contains("alpha 254", Assert.Single(MaskProblems(MadeAtlas.Of(("grass/tile/0", thin, 0, 0)))));

        // The veil's own alpha is 184: 183 is wrong, and so is an unseen tile's 200.
        var dim = Exact(HatchlingPalette.Dim);
        dim.Pixels[Inside] = PixelBuffer.Colour(0x0B, 0x0F, 0x13, 183);
        Assert.Contains("alpha 183", Assert.Single(MaskProblems(MadeAtlas.Of(("fog/dim/0", dim, 0, 0)))));
        var unseen = Exact(HatchlingPalette.Unseen);
        unseen.Pixels[Inside] = PixelBuffer.Colour(0x11, 0x16, 0x1C, 200);
        Assert.Contains("alpha 200", Assert.Single(MaskProblems(MadeAtlas.Of(("fog/unseen/0", unseen, 0, 0)))));

        // A rim with one pixel off the mask, and an empty rim.
        var spilledRim = Exact(HatchlingPalette.AttackRim, rim: true);
        spilledRim.Fill(0, 0, 1, 1, HatchlingPalette.AttackRim);
        Assert.Contains("mark/attack/0", Assert.Single(MaskProblems(MadeAtlas.Of(("mark/attack/0", spilledRim, 0, 0)))));
        var emptyRim = new PixelBuffer(BoardLayout.CellWidth, BoardLayout.CellHeight);
        Assert.Contains("empty rim", Assert.Single(MaskProblems(MadeAtlas.Of(("mark/move/1", emptyRim, 0, 0)))));

        // A wrong size or pivot is named.
        Assert.Contains("44 x 50", Assert.Single(MaskProblems(MadeAtlas.Of(("rock/tile/0", MadeAtlas.Solid(40, 50, HatchlingPalette.Rock), 0, 0)))));
        Assert.Contains("pivot", Assert.Single(MaskProblems(MadeAtlas.Of(("forest/tile/0", Exact(HatchlingPalette.Forest), 22, 25)))));

        // A solid 44 x 50 move mark is a problem; a piece that is neither tile nor rim is not checked.
        Assert.Contains("mark/move/0", Assert.Single(MaskProblems(MadeAtlas.Of(("mark/move/0", MadeAtlas.Solid(44, 50, HatchlingPalette.MoveRim), 0, 0)))));
        Assert.Empty(MaskProblems(MadeAtlas.Of(("plant/stand/0", MadeAtlas.Solid(44, 50, HatchlingPalette.Plant), 0, 0))));
    }
}

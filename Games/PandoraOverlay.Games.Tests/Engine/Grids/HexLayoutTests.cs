using PandoraOverlay.Games.Engine;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The hex layout: where the boxes sit, and the outline rule that makes the cells
/// tile a pixel buffer with no gap and no shared pixel. The tiling is checked by
/// brute force, asking every cell's outline directly (<see cref="HexLayout.InCell"/>
/// at its <see cref="HexLayout.TopLeft"/>), never through <see cref="HexLayout.CellAt"/>,
/// which is then held to that answer on every pixel. A03.
///
/// <para>The shapes: (44, 50, 38) on 11 x 11, the size of the prototype's board,
/// (16, 18, 14) on 5 x 4, a small one whose last row is odd, and (6, 8, 5) on 7 x 6,
/// whose pixel centres fall exactly on the slanted edges (the other two never do,
/// so only it shows the strict top and loose bottom doing their work). The pinned
/// counts (pixels per cell, pixels in no cell) were also printed by a JavaScript copy
/// of the rule as <see cref="HexLayout"/>'s summary writes it (A03's report holds the
/// script): the words the art tool copies give the same pixels as the C#.</para>
/// </summary>
public class HexLayoutTests
{
    /// <summary>A shape on a board, with the sizes and counts JavaScript printed for it.</summary>
    public sealed record Shape(int Columns, int Rows, int W, int H, int Pitch, int Width, int Height, int PixelsPerCell, int InNoCell)
    {
        public HexGrid Grid() => new(Columns, Rows);
        public override string ToString() => $"({W}, {H}, {Pitch}) on {Columns} x {Rows}";
    }

    public static TheoryData<Shape> Shapes => new()
    {
        new Shape(11, 11, 44, 50, 38, Width: 506, Height: 430, PixelsPerCell: 1672, InNoCell: 15268),
        new Shape(5, 4, 16, 18, 14, Width: 88, Height: 60, PixelsPerCell: 224, InNoCell: 800),
        // and one whose pixel centres fall exactly on the slanted edges, so the
        // strict top and the loose bottom decide (see TheTopIsStrictAndTheBottomIsNot)
        new Shape(7, 6, 6, 8, 5, Width: 45, Height: 33, PixelsPerCell: 30, InNoCell: 225),
    };

    /// <summary>Every cell's pixels on the buffer, found by asking each cell's own outline: how many cells hold each pixel.</summary>
    private static int[] Owners(HexGrid grid, HexLayout layout, int cellWidth, int cellHeight, int rowPitch, out int[] perCell)
    {
        var owners = new int[layout.Width * layout.Height];
        perCell = new int[grid.Count];
        for (var cell = 0; cell < grid.Count; cell++)
        {
            var (left, top) = layout.TopLeft(cell);
            for (var y = 0; y < cellHeight; y++)
            {
                for (var x = 0; x < cellWidth; x++)
                {
                    if (!HexLayout.InCell(x, y, cellWidth, cellHeight, rowPitch))
                        continue;
                    owners[(top + y) * layout.Width + left + x]++;
                    perCell[cell]++;
                }
            }
        }
        return owners;
    }

    // ---- The boxes --------------------------------------------------------------------

    /// <summary>The board's size, as the formula: columns of cells and half a cell for the odd rows, and the rows' pitch plus one cell's height.</summary>
    [Theory]
    [MemberData(nameof(Shapes))]
    public void TheBoardsSizeIsTheFormulas(Shape shape)
    {
        var layout = new HexLayout(shape.Grid(), shape.W, shape.H, shape.Pitch);
        Assert.Equal(shape.Width, layout.Width);
        Assert.Equal(shape.Height, layout.Height);
        Assert.Equal(shape.Columns * shape.W + shape.W / 2, layout.Width);
        Assert.Equal((shape.Rows - 1) * shape.Pitch + shape.H, layout.Height);
    }

    /// <summary>A single row has no odd row to stick out: no half cell added.</summary>
    [Fact]
    public void OneRowIsNoWiderThanItsCells()
    {
        var layout = new HexLayout(new HexGrid(6, 1), 16, 18, 14);
        Assert.Equal(96, layout.Width);
        Assert.Equal(18, layout.Height);
    }

    /// <summary>The boxes: odd rows half a cell to the right, rows a pitch apart, everything moved by the origin; the centre half a box in.</summary>
    [Fact]
    public void TheBoxesSitWhereTheFormulaSays()
    {
        var grid = new HexGrid(11, 11);
        var layout = new HexLayout(grid, 44, 50, 38, originX: 7, originY: 3);

        Assert.Equal((7, 3), layout.TopLeft(0));
        Assert.Equal((7 + 44, 3), layout.TopLeft(1));
        Assert.Equal((7 + 22, 3 + 38), layout.TopLeft(11));     // row 1: shifted half a cell
        Assert.Equal((7 + 2 * 44, 3 + 2 * 38), layout.TopLeft(24)); // row 2, column 2: not shifted
        Assert.Equal((7 + 10 * 44 + 22, 3 + 9 * 38), layout.TopLeft(109));
        Assert.Equal((7 + 22, 3 + 25), layout.Centre(0));
        Assert.Equal((7 + 22 + 22, 3 + 38 + 25), layout.Centre(11));

        Assert.Throws<ArgumentOutOfRangeException>(() => layout.TopLeft(121));
        Assert.Throws<ArgumentOutOfRangeException>(() => layout.Centre(-1));
    }

    // ---- The tiling -------------------------------------------------------------------

    /// <summary>
    /// No pixel belongs to two cells, every cell owns the same number of pixels
    /// (cellWidth * rowPitch), and the pixels in no cell are as many as JavaScript
    /// counted.
    /// </summary>
    [Theory]
    [MemberData(nameof(Shapes))]
    public void TheCellsTileTheBoard(Shape shape)
    {
        var grid = shape.Grid();
        var layout = new HexLayout(grid, shape.W, shape.H, shape.Pitch);

        var owners = Owners(grid, layout, shape.W, shape.H, shape.Pitch, out var perCell);

        Assert.All(owners, n => Assert.InRange(n, 0, 1));
        Assert.All(perCell, n => Assert.Equal(shape.PixelsPerCell, n));
        Assert.Equal(shape.W * shape.Pitch, shape.PixelsPerCell);
        Assert.Equal(shape.InNoCell, owners.Count(n => n == 0));
    }

    /// <summary>
    /// The pixels in no cell are only the notches along the board's edge: in the
    /// top row's top band, the bottom row's bottom band, or the half-cell strip at
    /// the left or right end of a row. Never one inside the board.
    /// </summary>
    [Theory]
    [MemberData(nameof(Shapes))]
    public void TheGapsAreOnlyTheEdgesNotches(Shape shape)
    {
        var grid = shape.Grid();
        var w = shape.W;
        var layout = new HexLayout(grid, w, shape.H, shape.Pitch);
        var owners = Owners(grid, layout, w, shape.H, shape.Pitch, out _);
        var s = shape.H - shape.Pitch;

        for (var y = 0; y < layout.Height; y++)
        {
            for (var x = 0; x < layout.Width; x++)
            {
                if (owners[y * layout.Width + x] != 0)
                    continue;
                var byTheEdge = y < s || y >= layout.Height - s || x < w / 2 || x >= layout.Width - w / 2;
                Assert.True(byTheEdge, $"({x}, {y}) is in no cell but is not by the edge");
            }
        }
    }

    /// <summary>CellAt gives, for every pixel of the buffer and a margin round it, the one cell whose outline holds it, or -1: the brute force's answer.</summary>
    [Theory]
    [MemberData(nameof(Shapes))]
    public void CellAtAgreesWithTheOutlines(Shape shape)
    {
        var grid = shape.Grid();
        var (w, h, pitch) = (shape.W, shape.H, shape.Pitch);
        const int originX = 5, originY = 9;
        var layout = new HexLayout(grid, w, h, pitch, originX, originY);

        // The brute force: each pixel's owner by every cell's own outline.
        var owner = new int[layout.Width * layout.Height];
        Array.Fill(owner, -1);
        for (var cell = 0; cell < grid.Count; cell++)
        {
            var (left, top) = layout.TopLeft(cell);
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                    if (HexLayout.InCell(x, y, w, h, pitch))
                        owner[(top - originY + y) * layout.Width + left - originX + x] = cell;
        }

        for (var y = -3; y < layout.Height + 3; y++)
        {
            for (var x = -3; x < layout.Width + 3; x++)
            {
                var inside = x >= 0 && y >= 0 && x < layout.Width && y < layout.Height;
                var expected = inside ? owner[y * layout.Width + x] : -1;
                Assert.Equal(expected, layout.CellAt(originX + x, originY + y));
            }
        }
    }

    /// <summary>A cell's centre pixel is its own.</summary>
    [Theory]
    [MemberData(nameof(Shapes))]
    public void TheCentreIsTheCells(Shape shape)
    {
        var grid = shape.Grid();
        var layout = new HexLayout(grid, shape.W, shape.H, shape.Pitch, 13, 2);
        for (var cell = 0; cell < grid.Count; cell++)
        {
            var (x, y) = layout.Centre(cell);
            Assert.Equal(cell, layout.CellAt(x, y));
        }
    }

    /// <summary>Far off the board, and at the int's ends, there is no cell (and no overflow).</summary>
    [Fact]
    public void FarOffTheBoardIsNoCell()
    {
        var layout = new HexLayout(new HexGrid(11, 11), 44, 50, 38, -20, -20);
        Assert.Equal(-1, layout.CellAt(int.MinValue, int.MinValue));
        Assert.Equal(-1, layout.CellAt(int.MaxValue, int.MaxValue));
        Assert.Equal(-1, layout.CellAt(-21, 30));
        Assert.Equal(0, layout.CellAt(2, 5)); // (22, 25) of the first box: its middle
    }

    // ---- The outline rule -------------------------------------------------------------

    /// <summary>The outline of one box as rows of '#' (the cell's) and '.' (not).</summary>
    private static string[] Picture(int w, int h, int pitch) =>
        Enumerable.Range(0, h)
            .Select(y => new string(Enumerable.Range(0, w).Select(x => HexLayout.InCell(x, y, w, h, pitch) ? '#' : '.').ToArray()))
            .ToArray();

    /// <summary>
    /// The (16, 18, 14) box as a picture: a point at the top, the full width
    /// between the bands, a point at the bottom, the two halves mirror images.
    /// Worked out by hand from the rule (S = 4, dx = |2x - 15|; top: dx &lt; 2(2y + 1);
    /// bottom: dx &lt;= 2(2m + 1)), and JavaScript printed the same. dx is odd and the
    /// bound even, so here strict and loose never differ; the next test has ties.
    /// </summary>
    [Fact]
    public void TheOutlineOfASmallCell()
    {
        var full = new string('#', 16);
        var expected = new[]
        {
            ".......##.......", ".....######.....", "...##########...", ".##############.",
            full, full, full, full, full, full, full, full, full, full,
            ".##############.", "...##########...", ".....######.....", ".......##.......",
        };
        Assert.Equal(expected, Picture(16, 18, 14));
    }

    /// <summary>
    /// Where a pixel's centre lies exactly on a slanted edge the strict top refuses
    /// it and the loose bottom takes it, so it is counted once. The (6, 8, 5) box has
    /// such ties on every band row (S = 3, dx = |2x - 5|; top: dx &lt; 2y + 1; bottom:
    /// dx &lt;= 2m + 1; JavaScript counted 12): its top row is empty and its bottom
    /// band one pixel wider each side than the top band's mirror. Both bands still
    /// add up to one full row each for the next row's cells (the tiling test).
    /// </summary>
    [Fact]
    public void TheTopIsStrictAndTheBottomIsNot()
    {
        var expected = new[] { "......", "..##..", ".####.", "######", "######", "######", ".####.", "..##.." };
        Assert.Equal(expected, Picture(6, 8, 5));
    }

    /// <summary>Outside its own box a pixel is never the cell's.</summary>
    [Theory]
    [InlineData(-1, 25)]
    [InlineData(44, 25)]
    [InlineData(22, -1)]
    [InlineData(22, 50)]
    public void OutsideTheBoxIsNotTheCell(int x, int y)
    {
        Assert.False(HexLayout.InCell(x, y, 44, 50, 38));
    }

    /// <summary>A shape that can't tile throws: an odd or empty width, a pitch out of (0, height), rows that overlap too far.</summary>
    [Theory]
    [InlineData(43, 50, 38)] // odd width: odd rows can't shift by half of it
    [InlineData(0, 50, 38)]
    [InlineData(-2, 50, 38)]
    [InlineData(44, 50, 50)] // no overlap
    [InlineData(44, 50, 0)]
    [InlineData(44, 50, -1)]
    [InlineData(44, 50, 51)]
    [InlineData(44, 50, 24)] // 2 * 26 > 50: the bands would cross
    public void AShapeThatCantTileThrows(int w, int h, int pitch)
    {
        Assert.ThrowsAny<ArgumentException>(() => new HexLayout(new HexGrid(3, 3), w, h, pitch));
        Assert.ThrowsAny<ArgumentException>(() => HexLayout.InCell(0, 0, w, h, pitch));
    }

    /// <summary>The tightest shape that tiles, bands meeting in the middle (2 * (h - pitch) == h), is accepted and tiles.</summary>
    [Fact]
    public void TheTightestShapeTiles()
    {
        var grid = new HexGrid(4, 5);
        var layout = new HexLayout(grid, 12, 12, 6);
        var owners = Owners(grid, layout, 12, 12, 6, out var perCell);
        Assert.All(owners, n => Assert.InRange(n, 0, 1));
        Assert.All(perCell, n => Assert.Equal(12 * 6, n));
        Assert.Throws<ArgumentNullException>(() => new HexLayout(null!, 12, 12, 6));
    }
}

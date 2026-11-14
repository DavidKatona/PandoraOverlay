using PandoraOverlay.Games.Engine;

namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// Where things stand on the board, in the surface's pixels: the hexes' layout and
/// the few offsets from a cell's centre that place a creature, an item and a mark.
/// Pure numbers, no WPF, one home for each (the painter, the view's playback and
/// the tests all read them here).
///
/// <para>The board is the engine's <see cref="HexLayout"/> of the island's grid in
/// cells of 44 x 50, rows 38 apart: 506 x 430 pixels, drawn one pixel in from the
/// edge of a 508 x 432 surface (A17: the playfield is our own pixels, and the
/// surface's sides are multiples of 4). The tiles of the art are drawn by the same
/// outline rule (A28), so they interlock and a click lands on the tile it looks
/// like.</para>
///
/// <para><b>The masks.</b> A cell's outline, its 2-pixel rim and the ring's
/// ellipse are worked out once, here, as runs of pixels in a row (a span: a row,
/// the first pixel and how many). The painter fills a stand-in span by span: no
/// pixel is tested while a frame is drawn, and nothing is allocated (the view
/// paints up to 30 times a second).</para>
/// </summary>
internal static class BoardLayout
{
    /// <summary>The surface's width.</summary>
    public const int Width = 508;

    /// <summary>The surface's height.</summary>
    public const int Height = 432;

    /// <summary>A cell's box: 44 wide...</summary>
    public const int CellWidth = 44;

    /// <summary>...50 high...</summary>
    public const int CellHeight = 50;

    /// <summary>...and its rows 38 apart.</summary>
    public const int RowPitch = 38;

    /// <summary>Where the first cell's box starts: one pixel in, so the board sits in the middle of the surface.</summary>
    public const int OriginX = 1, OriginY = 1;

    /// <summary>The island's cells in the surface's pixels.</summary>
    public static readonly HexLayout Hexes = new(Island.Grid, CellWidth, CellHeight, RowPitch, OriginX, OriginY);

    /// <summary>A creature's feet stand this far below its cell's centre.</summary>
    public const int FeetBelowCentre = 9;

    /// <summary>An item's pivot sits this far below its cell's centre.</summary>
    public const int ItemBelowCentre = 8;

    /// <summary>The marks above a head are centred this far above the cell's centre.</summary>
    public const int MarkAboveCentre = 19;

    /// <summary>A predator's number: its last digit's top-left at the cell's centre plus this (H38)...</summary>
    public const int OrdinalRight = 14, OrdinalDown = 12;

    /// <summary>...and the first digit of a number from 10 to 99 this far to its left.</summary>
    public const int OrdinalDigitStep = 6;

    /// <summary>The distance between the places of the marks above a head (H52): a hunter's afflictions start this far right of its alert; your hidden mark stands this far left of mark/you, your Elder mark this far right.</summary>
    public const int MarkStep = 10;

    /// <summary>A rising number's glyphs (its sign, its digits) are this far apart: a 5-pixel glyph and a pixel's gap.</summary>
    public const int NumberStep = 6;

    /// <summary>The centre of <paramref name="cell"/>'s box.</summary>
    public static (int X, int Y) Centre(int cell) => Hexes.Centre(cell);

    /// <summary>Where a creature on <paramref name="cell"/> stands: the cell's centre plus (0, 9).</summary>
    public static (int X, int Y) Feet(int cell)
    {
        var (x, y) = Hexes.Centre(cell);
        return (x, y + FeetBelowCentre);
    }

    /// <summary>Where an item on <paramref name="cell"/> is drawn (a plant's, a carcass's pivot): the centre plus (0, 8).</summary>
    public static (int X, int Y) Item(int cell)
    {
        var (x, y) = Hexes.Centre(cell);
        return (x, y + ItemBelowCentre);
    }

    /// <summary>Where the marks above a creature on <paramref name="cell"/> are centred: the centre minus (0, 19).</summary>
    public static (int X, int Y) Mark(int cell)
    {
        var (x, y) = Hexes.Centre(cell);
        return (x, y - MarkAboveCentre);
    }

    /// <summary>A run of pixels in one row of a mask: <c>Length</c> pixels from (<c>X</c>, <c>Y</c>), relative to the mask's top-left.</summary>
    internal readonly record struct Span(int X, int Y, int Length);

    /// <summary>The pixels of a cell's box that are the cell's (<see cref="HexLayout.InCell"/>), one span a row: the outline is convex.</summary>
    internal static readonly Span[] CellSpans = Spans(CellWidth, CellHeight, InCell);

    /// <summary>
    /// The cell's 2-pixel rim: a pixel of the cell with a pixel outside it within two
    /// steps, diagonals included, so the slants are as thick as the sides. The art
    /// tool draws <c>mark/move</c>, <c>mark/attack</c> and <c>mark/hover</c> by the same
    /// rule, so the stand-in is the art's shape in one colour.
    /// </summary>
    internal static readonly Span[] RimSpans = Spans(CellWidth, CellHeight, InRim);

    /// <summary>The ring's stand-in box: 28 x 10, centred on the feet (its pivot is the box's middle, as the ring art's is).</summary>
    public const int RingWidth = 28, RingHeight = 10;

    /// <summary>The outline of the ellipse inscribed in the ring's box: a pixel inside it with a 4-neighbour outside.</summary>
    internal static readonly Span[] RingSpans = Spans(RingWidth, RingHeight, InRingOutline);

    /// <summary>The 7 x 7 diamond a plant's and a carcass's stand-in is, centred on the item's pivot.</summary>
    public const int DiamondSize = 7;

    /// <summary>The diamond's pixels: at most 3 steps from its middle, across and down together.</summary>
    internal static readonly Span[] DiamondSpans = Spans(DiamondSize, DiamondSize,
        (x, y) => Math.Abs(x - DiamondSize / 2) + Math.Abs(y - DiamondSize / 2) <= DiamondSize / 2);

    /// <summary>The side of a square stand-in: small game's, and a mark's above a head.</summary>
    public const int SquareSize = 5;

    private static bool InCell(int x, int y) => HexLayout.InCell(x, y, CellWidth, CellHeight, RowPitch);

    private static bool InRim(int x, int y)
    {
        if (!InCell(x, y)) return false;
        for (var dy = -2; dy <= 2; dy++)
            for (var dx = -2; dx <= 2; dx++)
                if (!InCell(x + dx, y + dy)) return true;
        return false;
    }

    /// <summary>
    /// Inside the ellipse inscribed in the ring's box, by the pixel's centre, in whole
    /// numbers: ((2x + 1 - w) h)^2 + ((2y + 1 - h) w)^2 &lt;= (w h)^2.
    /// </summary>
    private static bool InEllipse(int x, int y)
    {
        if (x < 0 || y < 0 || x >= RingWidth || y >= RingHeight) return false;
        long a = (2L * x + 1 - RingWidth) * RingHeight;
        long b = (2L * y + 1 - RingHeight) * RingWidth;
        long r = (long)RingWidth * RingHeight;
        return a * a + b * b <= r * r;
    }

    private static bool InRingOutline(int x, int y) =>
        InEllipse(x, y) && (!InEllipse(x - 1, y) || !InEllipse(x + 1, y) || !InEllipse(x, y - 1) || !InEllipse(x, y + 1));

    /// <summary>A mask as spans, row by row, each row's runs from the left.</summary>
    private static Span[] Spans(int width, int height, Func<int, int, bool> inside)
    {
        var spans = new List<Span>();
        for (var y = 0; y < height; y++)
        {
            var x = 0;
            while (x < width)
            {
                if (!inside(x, y)) { x++; continue; }
                var start = x;
                while (x < width && inside(x, y)) x++;
                spans.Add(new Span(start, y, x - start));
            }
        }
        return spans.ToArray();
    }
}

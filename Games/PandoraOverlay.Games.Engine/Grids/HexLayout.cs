namespace PandoraOverlay.Games.Engine;

/// <summary>
/// Where a <see cref="HexGrid"/>'s cells sit in a pixel buffer, and which cell a
/// pixel belongs to. Every pixel of the board belongs to at most one cell, and
/// the only pixels in none are the notches along the board's edge: neighbouring
/// cells meet with no gap and no shared pixel, so a tile drawn by the rule below
/// interlocks with the next and a click lands on the tile it looks like. Pure
/// maths, no WPF (A21).
///
/// <para>A cell's BOX is <c>cellWidth</c> by <c>cellHeight</c> pixels at
/// <see cref="TopLeft"/>; rows are <c>rowPitch</c> apart, so a row's box overlaps
/// the next row's by <c>S = cellHeight - rowPitch</c> pixels, and odd rows are
/// shifted right by half a cell. Which pixels of its box a cell owns is the outline
/// rule, <see cref="InCell"/>.</para>
///
/// <para><b>The outline rule, for the art tool</b> (A28: <c>tools/games-art</c>'s
/// <c>hex.js</c> draws every hex tile by these same lines, and a test holds the
/// tiles to this C#). For a pixel at (x, y) in a cell's own box, w = cellWidth,
/// h = cellHeight, p = rowPitch, all whole numbers:</para>
/// <code>
/// function inCell(x, y, w, h, p) {
///   if (x &lt; 0 || y &lt; 0 || x &gt;= w || y &gt;= h) return false;  // outside the box
///   const s = h - p;                        // how far rows overlap: the slanted bands' height
///   const dx = Math.abs(2 * x + 1 - w);     // twice the pixel centre's distance from the box's middle
///   if (y &lt; s) return 2 * dx * s &lt; w * (2 * y + 1);              // top band: STRICT
///   if (y &gt;= h - s) { const m = h - 1 - y;                        // bottom band, m rows from the bottom:
///                     return 2 * dx * s &lt;= w * (2 * m + 1); }      //   NOT strict
///   return true;                            // between the bands: the whole width
/// }
/// </code>
/// <para>In words: the top band narrows to a point at the top, the bottom band to a
/// point at the bottom, and the middle is the full width. The comparison is strict
/// at the top and not at the bottom on purpose: a row's bottom band and the next
/// row's top band cover the same pixels, and where a pixel's centre lies exactly on
/// the slanted edge between them, the loose bottom gives it to the upper cell and
/// the strict top refuses it to the lower one, so it is counted once. Both sides
/// compare whole numbers, so the JavaScript and the C# agree on every pixel.</para>
///
/// <para>Every cell owns the same number of pixels, <c>cellWidth * rowPitch</c>.</para>
/// </summary>
public sealed class HexLayout
{
    private readonly HexGrid _grid;
    private readonly int _cellWidth;
    private readonly int _cellHeight;
    private readonly int _rowPitch;
    private readonly int _originX;
    private readonly int _originY;

    /// <summary>
    /// The cells of <paramref name="grid"/> laid out in boxes of
    /// <paramref name="cellWidth"/> by <paramref name="cellHeight"/>, rows
    /// <paramref name="rowPitch"/> apart (from one row's top to the next's), the
    /// first cell's box at (<paramref name="originX"/>, <paramref name="originY"/>).
    ///
    /// <para>Throws <see cref="ArgumentException"/> unless the shape tiles: the width
    /// even and positive (odd rows shift by exactly half of it), <c>0 &lt; rowPitch &lt;
    /// cellHeight</c> (rows overlap, or the slants would have nothing to meet), and
    /// <c>2 * (cellHeight - rowPitch) &lt;= cellHeight</c> (the top and bottom bands
    /// don't cross, which also keeps any pixel row in at most two rows of cells).</para>
    /// </summary>
    public HexLayout(HexGrid grid, int cellWidth, int cellHeight, int rowPitch, int originX = 0, int originY = 0)
    {
        ArgumentNullException.ThrowIfNull(grid);
        Validate(cellWidth, cellHeight, rowPitch);

        _grid = grid;
        _cellWidth = cellWidth;
        _cellHeight = cellHeight;
        _rowPitch = rowPitch;
        _originX = originX;
        _originY = originY;

        // Odd rows stick out half a cell to the right, so the board is that much
        // wider whenever it has an odd row at all.
        Width = grid.Columns * cellWidth + (grid.Rows > 1 ? cellWidth / 2 : 0);
        Height = (grid.Rows - 1) * rowPitch + cellHeight;
    }

    /// <summary>The board's width in pixels, from the origin: every cell's box fits in it.</summary>
    public int Width { get; }

    /// <summary>The board's height in pixels, from the origin.</summary>
    public int Height { get; }

    /// <summary>The top-left corner of <paramref name="cell"/>'s box: odd rows half a cell to the right.</summary>
    public (int X, int Y) TopLeft(int cell)
    {
        var column = _grid.Column(cell);
        var row = _grid.Row(cell);
        return (_originX + column * _cellWidth + ((row & 1) == 1 ? _cellWidth / 2 : 0), _originY + row * _rowPitch);
    }

    /// <summary>The pixel at the middle of <paramref name="cell"/>'s box (rounded down where the height is odd); it is always the cell's own.</summary>
    public (int X, int Y) Centre(int cell)
    {
        var (x, y) = TopLeft(cell);
        return (x + _cellWidth / 2, y + _cellHeight / 2);
    }

    /// <summary>
    /// The one cell whose outline holds the pixel (<paramref name="x"/>,
    /// <paramref name="y"/>), or -1: off the board, or in a notch along its edge.
    /// Constant time: a pixel row lies in at most two rows of cells (the bands never
    /// cross), and in each of them in at most one box.
    /// </summary>
    public int CellAt(int x, int y)
    {
        var lx = (long)x - _originX;
        var ly = (long)y - _originY;
        if (lx < 0 || ly < 0 || lx >= Width || ly >= Height)
            return -1;

        // The lowest row whose box starts at or above the pixel, and the one above
        // it, which still holds the pixel when it lies in that row's bottom band.
        var lowest = (int)Math.Min(ly / _rowPitch, _grid.Rows - 1);
        for (var row = lowest; row >= Math.Max(0, lowest - 1); row--)
        {
            var boxY = (int)(ly - (long)row * _rowPitch);
            if (boxY >= _cellHeight)
                continue;
            var shifted = lx - ((row & 1) == 1 ? _cellWidth / 2 : 0);
            if (shifted < 0)
                continue;
            var column = shifted / _cellWidth;
            if (column >= _grid.Columns)
                continue;
            var boxX = (int)(shifted - column * _cellWidth);
            if (InCell(boxX, boxY, _cellWidth, _cellHeight, _rowPitch))
                return _grid.Index((int)column, row);
        }
        return -1;
    }

    /// <summary>
    /// The outline rule: whether the pixel (<paramref name="x"/>,
    /// <paramref name="y"/>) of a cell's own box is the cell's. False outside the
    /// box. The rule, and why its top is strict and its bottom is not, is in the
    /// class summary, in a form the art tool copies. The shape is checked as the
    /// constructor checks it.
    /// </summary>
    public static bool InCell(int x, int y, int cellWidth, int cellHeight, int rowPitch)
    {
        Validate(cellWidth, cellHeight, rowPitch);
        if (x < 0 || y < 0 || x >= cellWidth || y >= cellHeight)
            return false;

        // Whole numbers only, in long so no shape that fits an int can overflow.
        long s = cellHeight - rowPitch;
        long dx = Math.Abs(2L * x + 1 - cellWidth);
        if (y < s)
            return 2 * dx * s < cellWidth * (2L * y + 1);
        if (y >= cellHeight - s)
        {
            long m = cellHeight - 1 - y;
            return 2 * dx * s <= cellWidth * (2 * m + 1);
        }
        return true;
    }

    /// <summary>The shape's checks, shared by the constructor and <see cref="InCell"/>.</summary>
    private static void Validate(int cellWidth, int cellHeight, int rowPitch)
    {
        if (cellWidth <= 0 || (cellWidth & 1) == 1)
            throw new ArgumentException($"A cell's width must be even and positive; it is {cellWidth}.", nameof(cellWidth));
        if (rowPitch <= 0 || rowPitch >= cellHeight)
            throw new ArgumentException($"The row pitch must be above 0 and below the cell's height ({cellHeight}); it is {rowPitch}.", nameof(rowPitch));
        if (2L * (cellHeight - rowPitch) > cellHeight)
            throw new ArgumentException($"The rows overlap too far: 2 * (cellHeight - rowPitch) must be at most cellHeight ({cellHeight}).", nameof(rowPitch));
    }
}

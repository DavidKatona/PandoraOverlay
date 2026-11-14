using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace PandoraOverlay.Games.Engine;

/// <summary>
/// Pointy-top hexes in rows, odd rows shifted half a cell right (the prototype's
/// board). Cells are ints: <c>row * Columns + column</c>, row 0 at the top. Pure
/// maths, no WPF: a bot plays thousands of runs on it with no screen (A21).
///
/// <para>Every answer is the prototype's (<c>HX_NEAR</c>, <c>hxCube</c>,
/// <c>HX_DIST</c>, <c>hxStepToward</c>, <c>hxMoveTargets</c> in the prototype's
/// <c>hatch-rules.js</c>, in the games folder), and so is every ORDER: the
/// neighbour order breaks the ties of every breadth-first walk here, and a rule
/// ported from the prototype only plays the same when its walks pick the same
/// cell. The tests hold the two equal.</para>
///
/// <para>A cell outside the grid passed to any member throws
/// <see cref="ArgumentOutOfRangeException"/>: it is a bug in the caller, never a
/// state of the board, and a quiet -1 would let it travel. Only
/// <see cref="Index"/> answers -1, because asking whether a column and row are on
/// the board is its job.</para>
/// </summary>
public sealed class HexGrid
{
    /// <summary>
    /// The column and row offsets to the six neighbours, in THE order: upper-left,
    /// upper-right, left, right, lower-left, lower-right. An even row's neighbours
    /// above and below sit half a cell to its left, an odd row's half a cell to its
    /// right, hence the two tables.
    /// </summary>
    private static readonly (int Column, int Row)[] EvenRowOffsets = { (-1, -1), (0, -1), (-1, 0), (1, 0), (-1, 1), (0, 1) };

    /// <summary>The odd rows' offsets, in the same order as <see cref="EvenRowOffsets"/>.</summary>
    private static readonly (int Column, int Row)[] OddRowOffsets = { (0, -1), (1, -1), (-1, 0), (1, 0), (0, 1), (1, 1) };

    /// <summary>Every cell's neighbours, worked out once: the walks ask for them on every step.</summary>
    private readonly ReadOnlyCollection<int>[] _neighbours;

    /// <summary>A grid of <paramref name="columns"/> by <paramref name="rows"/> cells; both at least 1, and the count must fit an int.</summary>
    public HexGrid(int columns, int rows)
    {
        if (columns < 1)
            throw new ArgumentOutOfRangeException(nameof(columns), columns, "A grid has at least one column.");
        if (rows < 1)
            throw new ArgumentOutOfRangeException(nameof(rows), rows, "A grid has at least one row.");
        if ((long)columns * rows > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(rows), rows, "The grid has more cells than an int can number.");

        Columns = columns;
        Rows = rows;
        Count = columns * rows;

        _neighbours = new ReadOnlyCollection<int>[Count];
        var found = new List<int>(6);
        for (var cell = 0; cell < Count; cell++)
        {
            var column = cell % columns;
            var row = cell / columns;
            found.Clear();
            foreach (var (dc, dr) in (row & 1) == 1 ? OddRowOffsets : EvenRowOffsets)
            {
                var next = Index(column + dc, row + dr);
                if (next >= 0)
                    found.Add(next);
            }
            _neighbours[cell] = Array.AsReadOnly(found.ToArray());
        }
    }

    /// <summary>The cells in a row.</summary>
    public int Columns { get; }

    /// <summary>The rows.</summary>
    public int Rows { get; }

    /// <summary>All the cells: they are numbered 0 to Count - 1.</summary>
    public int Count { get; }

    /// <summary>The cell at <paramref name="column"/>, <paramref name="row"/>; -1 when that is off the board.</summary>
    public int Index(int column, int row) =>
        column >= 0 && row >= 0 && column < Columns && row < Rows ? row * Columns + column : -1;

    /// <summary>The column <paramref name="cell"/> is in.</summary>
    public int Column(int cell)
    {
        Check(cell);
        return cell % Columns;
    }

    /// <summary>The row <paramref name="cell"/> is in.</summary>
    public int Row(int cell)
    {
        Check(cell);
        return cell / Columns;
    }

    /// <summary>
    /// The cells next to <paramref name="cell"/>, up to six, always in the order
    /// upper-left, upper-right, left, right, lower-left, lower-right, with those off
    /// the board left out. The order is part of the contract: it breaks the ties of
    /// every breadth-first walk (the prototype's <c>HX_NEAR</c>).
    /// </summary>
    public IReadOnlyList<int> Neighbours(int cell)
    {
        Check(cell);
        return _neighbours[cell];
    }

    /// <summary>
    /// The steps between two cells on an empty board. Each cell as cube
    /// coordinates, <c>x = column - (row - (row &amp; 1)) / 2</c>, <c>z = row</c>,
    /// <c>y = -x - z</c>, and the distance is the largest of the three differences
    /// (the prototype's <c>hxCube</c> and <c>HX_DIST</c>).
    /// </summary>
    public int Distance(int a, int b)
    {
        Check(a);
        Check(b);
        var (ax, az) = Cube(a);
        var (bx, bz) = Cube(b);
        var dx = Math.Abs(ax - bx);
        var dz = Math.Abs(az - bz);
        var dy = Math.Abs((-ax - az) - (-bx - bz));
        return Math.Max(dx, Math.Max(dy, dz));
    }

    /// <summary>
    /// Every cell at most <paramref name="radius"/> steps from
    /// <paramref name="centre"/> on an empty board, the centre included, in rising
    /// cell order. A negative radius holds nothing. The answer is a finished list,
    /// so a bad centre throws at the call and not later, at the first loop over it.
    /// </summary>
    public IEnumerable<int> Within(int centre, int radius)
    {
        Check(centre);
        var cells = new List<int>();
        if (radius < 0)
            return cells;

        // A cell `radius` steps away is at most `radius` rows away: only those rows
        // are looked at, and in order, so the answer comes out sorted.
        var row = Row(centre);
        var firstRow = Math.Max(0, row - radius);
        var lastRow = (int)Math.Min(Rows - 1L, (long)row + radius);
        for (var r = firstRow; r <= lastRow; r++)
        {
            for (var c = 0; c < Columns; c++)
            {
                var cell = r * Columns + c;
                if (Distance(centre, cell) <= radius)
                    cells.Add(cell);
            }
        }
        return cells;
    }

    /// <summary>
    /// Breadth-first from <paramref name="from"/> over the cells for which
    /// <paramref name="passable"/> is true: every cell reached in 1 to
    /// <paramref name="maxSteps"/> steps, with its step count (the prototype's
    /// sprint in <c>hxMoveTargets</c>). <paramref name="from"/> itself is never in
    /// the answer and need not be passable; <paramref name="passable"/> is never
    /// asked about it. A walk of 0 steps or fewer reaches nothing.
    ///
    /// <para>The answer lists its cells in the order the walk reached them (nearest
    /// first, ties by neighbour order), the prototype's Map order, so a game that
    /// takes "the first" of them takes the prototype's.</para>
    /// </summary>
    public IReadOnlyDictionary<int, int> Reach(int from, int maxSteps, Func<int, bool> passable)
    {
        Check(from);
        ArgumentNullException.ThrowIfNull(passable);

        var reached = new ReachedCells();
        if (maxSteps <= 0)
            return reached;

        var steps = new int[Count];
        Array.Fill(steps, -1);
        steps[from] = 0;
        var queue = new Queue<int>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var at = queue.Dequeue();
            if (steps[at] >= maxSteps)
                continue;
            foreach (var next in _neighbours[at])
            {
                if (steps[next] >= 0 || !passable(next))
                    continue;
                steps[next] = steps[at] + 1;
                reached.Add(next, steps[next]);
                queue.Enqueue(next);
            }
        }
        return reached;
    }

    /// <summary>
    /// The first step of a shortest walk from <paramref name="from"/> to a cell for
    /// which <paramref name="isGoal"/> is true (the prototype's
    /// <c>hxStepToward</c>, with the goal and the way handed in).
    ///
    /// <list type="bullet">
    /// <item>-1 when <paramref name="from"/> is itself a goal: there is nowhere to go.</item>
    /// <item>Otherwise breadth-first in neighbour order over passable cells. Each
    /// cell is asked <paramref name="isGoal"/> as it is FOUND, not as it is left,
    /// and the first goal found decides: the answer is the first step of the walk
    /// that found it. That is the prototype's order, and among the goals at the
    /// nearest distance it is the one the neighbour order meets first.</item>
    /// <item>A goal must itself be passable to be reached (the prototype's rule):
    /// a cell the walk can't enter is never asked whether it is a goal. A game that
    /// walks next to something it can't enter makes the cells beside it the goal.</item>
    /// <item>-1 when no goal can be reached.</item>
    /// </list>
    /// </summary>
    public int FirstStepToward(int from, Func<int, bool> isGoal, Func<int, bool> passable)
    {
        Check(from);
        ArgumentNullException.ThrowIfNull(isGoal);
        ArgumentNullException.ThrowIfNull(passable);

        if (isGoal(from))
            return -1;
        var goal = Walk(from, isGoal, passable, out var firstSteps, out _);
        return goal >= 0 ? firstSteps[goal] : -1;
    }

    /// <summary>
    /// The cells of a shortest walk from <paramref name="from"/> to
    /// <paramref name="to"/> over passable cells: the cells after
    /// <paramref name="from"/>, ending with <paramref name="to"/>, so its length is
    /// the walk's step count. Empty when <paramref name="from"/> is
    /// <paramref name="to"/> or when there is no walk. Ties by neighbour order, as
    /// every walk here. As in <see cref="FirstStepToward"/>, <paramref name="to"/>
    /// must itself be passable to be reached, and <paramref name="from"/> need not be.
    /// </summary>
    public IReadOnlyList<int> Path(int from, int to, Func<int, bool> passable)
    {
        Check(from);
        Check(to);
        ArgumentNullException.ThrowIfNull(passable);

        if (from == to)
            return Array.Empty<int>();
        if (Walk(from, cell => cell == to, passable, out _, out var cameFrom) < 0)
            return Array.Empty<int>();

        var cells = new List<int>();
        for (var at = to; at != from; at = cameFrom[at])
            cells.Add(at);
        cells.Reverse();
        return cells.AsReadOnly();
    }

    /// <summary>
    /// The one breadth-first walk of <see cref="FirstStepToward"/> and
    /// <see cref="Path"/>, their rule written once: from <paramref name="from"/> in
    /// neighbour order over passable cells, asking <paramref name="isGoal"/> of each
    /// cell as it is found, and stopping at the first goal, which it returns (-1
    /// when none is found). For every cell found, <paramref name="firstSteps"/> holds
    /// the first step of the walk to it and <paramref name="cameFrom"/> the cell it
    /// was found from.
    /// </summary>
    private int Walk(int from, Func<int, bool> isGoal, Func<int, bool> passable,
        out int[] firstSteps, out int[] cameFrom)
    {
        // -1 marks a cell not found yet; `from` is found, and is its own origin.
        firstSteps = new int[Count];
        cameFrom = new int[Count];
        Array.Fill(cameFrom, -1);
        cameFrom[from] = from;

        var queue = new Queue<int>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var at = queue.Dequeue();
            foreach (var next in _neighbours[at])
            {
                if (cameFrom[next] >= 0 || !passable(next))
                    continue;
                cameFrom[next] = at;
                firstSteps[next] = at == from ? next : firstSteps[at];
                if (isGoal(next))
                    return next;
                queue.Enqueue(next);
            }
        }
        return -1;
    }

    /// <summary>The cube coordinates' x and z of a cell (y is -x - z).</summary>
    private (int X, int Z) Cube(int cell)
    {
        var column = cell % Columns;
        var row = cell / Columns;
        return (column - (row - (row & 1)) / 2, row);
    }

    /// <summary>Throws for a cell off the board: a caller's bug, never a state of the board.</summary>
    private void Check(int cell, [CallerArgumentExpression(nameof(cell))] string? name = null)
    {
        if (cell < 0 || cell >= Count)
            throw new ArgumentOutOfRangeException(name, cell, $"There is no such cell: the grid has cells 0 to {Count - 1}.");
    }

    /// <summary>
    /// <see cref="Reach"/>'s answer: a read-only map that lists its cells in the
    /// order they were added. A plain <see cref="Dictionary{TKey, TValue}"/> happens
    /// to do so today, but does not promise it, and a walk's order is part of what
    /// keeps a run the same from one build to the next.
    /// </summary>
    private sealed class ReachedCells : IReadOnlyDictionary<int, int>
    {
        private readonly Dictionary<int, int> _steps = new();
        private readonly List<int> _order = new();

        public void Add(int cell, int steps)
        {
            _steps.Add(cell, steps);
            _order.Add(cell);
        }

        public int this[int key] => _steps[key];
        public IEnumerable<int> Keys => _order.AsReadOnly();
        public IEnumerable<int> Values => _order.Select(cell => _steps[cell]);
        public int Count => _order.Count;
        public bool ContainsKey(int key) => _steps.ContainsKey(key);
        public bool TryGetValue(int key, out int value) => _steps.TryGetValue(key, out value);

        public IEnumerator<KeyValuePair<int, int>> GetEnumerator()
        {
            foreach (var cell in _order)
                yield return new KeyValuePair<int, int>(cell, _steps[cell]);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

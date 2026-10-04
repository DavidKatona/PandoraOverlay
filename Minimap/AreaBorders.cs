namespace PandoraOverlay;

/// <summary>
/// One traced border: its points (map pixels, at pixel corners) and the two
/// things it separates — area indices, AreaMap.None for "no area". The same
/// pair holds along the whole line, which is what lets ONE area's outline be
/// picked out of the lot (the lines that have it on a side).
/// </summary>
public sealed record BorderLine(IReadOnlyList<(double X, double Y)> Points, int A, int B)
{
    public bool Touches(int area) => A == area || B == area;
}

/// <summary>
/// The borders of an area map as LINES, for the minimap's optional border
/// layer. ONE RULE, every edge: a border runs between two neighbouring
/// pixels that belong to different things — another area, or none — so each
/// area comes out as a closed shape, its water included, exactly what the
/// lookup uses. The edges are traced into chains (from junction to
/// junction, and closed loops), and each chain is then straightened: on a
/// pixel grid a slanted border is a staircase, and drawn as pixels and
/// stretched with the map it looked low-res at 5–6× zoom; as a line through
/// the stairs it stays sharp at any zoom. The line never strays further
/// from the true edge than the tolerance (under a pixel, ~12 m), which is
/// below what the borders themselves are good for, and junctions stay
/// exactly where they are, so lines still meet. Coordinates are in map
/// pixels, 0 … Size, at pixel CORNERS (an edge lies between pixels). Pure,
/// tested; works on whatever map is loaded.
/// </summary>
public static class AreaBorders
{
    /// <summary>How far a drawn line may stray from the pixel edge it stands for: enough to turn a staircase into a line.</summary>
    public const double DefaultTolerance = 0.9;

    public static IReadOnlyList<BorderLine> Trace(AreaMap map, double tolerance = DefaultTolerance)
    {
        var n = map.Size;
        var w = n + 1; // corners per row
        // An edge is named by its left / upper corner: Across[c] runs right from c, Down[c] runs down from c.
        var across = new bool[w * w];
        var down = new bool[w * w];
        for (var y = 0; y < n; y++)
        {
            for (var x = 0; x < n; x++)
            {
                var here = map.IndexAtPixel(x, y);
                if (x < n - 1 && map.IndexAtPixel(x + 1, y) != here) down[y * w + x + 1] = true;     // between this pixel and the one to its right
                if (y < n - 1 && map.IndexAtPixel(x, y + 1) != here) across[(y + 1) * w + x] = true; // between this pixel and the one below
            }
        }

        var acrossDone = new bool[w * w];
        var downDone = new bool[w * w];

        // The up-to-four edges at a corner, as (the corner at the other end, the edge's array, its index).
        IEnumerable<(int Other, bool Across, int Edge)> EdgesAt(int c)
        {
            int x = c % w, y = c / w;
            if (x < n && across[c]) yield return (c + 1, true, c);
            if (x > 0 && across[c - 1]) yield return (c - 1, true, c - 1);
            if (y < n && down[c]) yield return (c + w, false, c);
            if (y > 0 && down[c - w]) yield return (c - w, false, c - w);
        }
        int Degree(int c) => EdgesAt(c).Count();
        bool Done((int Other, bool Across, int Edge) e) => e.Across ? acrossDone[e.Edge] : downDone[e.Edge];
        void Mark((int Other, bool Across, int Edge) e)
        {
            if (e.Across) acrossDone[e.Edge] = true; else downDone[e.Edge] = true;
        }

        var lines = new List<BorderLine>();

        // Follows edges from a corner until the next junction or end (or, on a loop, back to the start).
        void Follow(int start, (int Other, bool Across, int Edge) first)
        {
            var chain = new List<(double X, double Y)> { (start % w, start / w) };
            var current = start;
            var edge = first;
            while (true)
            {
                Mark(edge);
                current = edge.Other;
                chain.Add((current % w, current / w));
                if (current == start || Degree(current) != 2) break;
                var open = EdgesAt(current).Where(e => !Done(e)).Take(1).ToList();
                if (open.Count == 0) break;
                edge = open[0];
            }
            // What lies on either side of the first edge lies on either side of the whole chain: with no
            // junction on the way, nothing else can join it.
            int ex = first.Edge % w, ey = first.Edge / w;
            var (a, b) = first.Across
                ? (map.IndexAtPixel(ex, ey - 1), map.IndexAtPixel(ex, ey))
                : (map.IndexAtPixel(ex - 1, ey), map.IndexAtPixel(ex, ey));
            lines.Add(new BorderLine(Simplify(chain, tolerance), a, b));
        }

        // Chains that begin at a junction or a loose end first, so they run from junction to junction …
        for (var c = 0; c < w * w; c++)
        {
            if (Degree(c) is 0 or 2) continue;
            foreach (var edge in EdgesAt(c).ToList())
            {
                if (!Done(edge)) Follow(c, edge);
            }
        }
        // … then what is left: closed loops with no junction on them.
        for (var c = 0; c < w * w; c++)
        {
            if (Degree(c) == 0) continue;
            foreach (var edge in EdgesAt(c).ToList())
            {
                if (!Done(edge)) Follow(c, edge);
            }
        }
        return lines;
    }

    /// <summary>
    /// Douglas–Peucker: keeps the ends, and of the points between only those
    /// the straight line would miss by more than the tolerance. A closed
    /// loop is cut at its point furthest from the start, so it can't
    /// collapse onto itself.
    /// </summary>
    internal static IReadOnlyList<(double X, double Y)> Simplify(IReadOnlyList<(double X, double Y)> chain, double tolerance)
    {
        if (chain.Count < 3) return chain;
        var keep = new bool[chain.Count];
        keep[0] = keep[^1] = true;
        var spans = new Stack<(int From, int To)>();
        if (chain[0] == chain[^1])
        {
            var far = 0;
            var best = -1.0;
            for (var i = 1; i < chain.Count - 1; i++)
            {
                var d = Math.Pow(chain[i].X - chain[0].X, 2) + Math.Pow(chain[i].Y - chain[0].Y, 2);
                if (d > best) { best = d; far = i; }
            }
            keep[far] = true;
            spans.Push((0, far));
            spans.Push((far, chain.Count - 1));
        }
        else
        {
            spans.Push((0, chain.Count - 1));
        }

        while (spans.Count > 0)
        {
            var (from, to) = spans.Pop();
            if (to - from < 2) continue;
            var (ax, ay) = chain[from];
            var (bx, by) = chain[to];
            double dx = bx - ax, dy = by - ay, length = Math.Sqrt(dx * dx + dy * dy);
            var worst = -1.0;
            var at = from;
            for (var i = from + 1; i < to; i++)
            {
                var off = length == 0
                    ? Math.Sqrt(Math.Pow(chain[i].X - ax, 2) + Math.Pow(chain[i].Y - ay, 2))
                    : Math.Abs(dy * (chain[i].X - ax) - dx * (chain[i].Y - ay)) / length;
                if (off > worst) { worst = off; at = i; }
            }
            if (worst <= tolerance) continue;
            keep[at] = true;
            spans.Push((from, at));
            spans.Push((at, to));
        }

        var result = new List<(double X, double Y)>();
        for (var i = 0; i < chain.Count; i++)
        {
            if (keep[i]) result.Add(chain[i]);
        }
        return result;
    }
}

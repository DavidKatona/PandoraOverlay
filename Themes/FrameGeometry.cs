using System.Windows;
using System.Windows.Media;

namespace PandoraOverlay;

/// <summary>
/// The geometry of a themed frame (PLAN.md sections 3 and 4), pure and
/// tested; step 2's ThemedBorder draws it. Three pieces:
/// <list type="bullet">
/// <item>the frame's polygon with its two cut corners (45°, the hex angle,
/// top-right and bottom-left), inset so a stroke can sit inside the bounds;</item>
/// <item>the lit accents, one path round each of those two corners, rounded
/// where the frame is (<see cref="LitRun"/>, and the one WPF geometry made
/// here, <see cref="LitGeometry"/>, since a polyline has no arc);</item>
/// <item>the glow, painted as three wider strokes under a line
/// (<see cref="Glow"/>), never an Effect: a live DropShadowEffect halved
/// WPF's frame rate on the owner's PC while a widget animated (glow-spike,
/// Oct 24 2026), and the owner's NameLayer rule says no blur effect.</item>
/// </list>
/// And the device-pixel snap, so a 1.5 px line at 125% or 150% lands on
/// whole device pixels instead of softening between two (REVIEW.md, risks;
/// NameLayer snaps its text the same way).
/// Every number that is 0 in Default (a cut, a lit run, a glow) gives
/// nothing here, so a frame with Default's values is the Border's own
/// drawing, untouched: Default is identical by construction.
/// </summary>
public static class FrameGeometry
{
    #region The cut polygon

    /// <summary>
    /// The frame's outline with its top-right and bottom-left corners cut at
    /// 45° by <paramref name="cut"/>, inset by <paramref name="inset"/> on
    /// every side (half a stroke's width, so the stroke stays inside; the
    /// diagonals move in by the same distance, measured square to them).
    /// Six points, clockwise from the top-left. Null when there is no cut:
    /// the caller then draws the Border's own rectangle.
    /// </summary>
    public static IReadOnlyList<Point>? CutPolygon(double width, double height, double cut, double inset = 0)
    {
        if (cut <= 0 || width <= 0 || height <= 0) return null;
        var c = Math.Min(cut, Math.Min(width, height) / 2);
        var d = inset;
        var lean = d * (Math.Sqrt(2) - 1); // how far an inset diagonal's ends slide along the edges

        return new[]
        {
            new Point(d, d),
            new Point(width - c - lean, d),
            new Point(width - d, c + lean),
            new Point(width - d, height - d),
            new Point(c + lean, height - d),
            new Point(d, height - c - lean),
        };
    }

    #endregion

    #region The lit accents

    /// <summary>
    /// The two lit accents: round the top-right corner (from the top edge,
    /// along the cut, down the right side) and round the bottom-left one (down
    /// the left side, along the cut, along the bottom). Each runs
    /// <paramref name="edge"/> along the top or bottom edge and
    /// <paramref name="side"/> along the side, both measured past the corner.
    /// The corner itself is the frame's: the 45° cut; with no cut, a square
    /// turn (the map window: square corners, lit, no cut); with no cut and a
    /// corner <paramref name="radius"/>, an arc that follows the frame's own
    /// curve (question 23, Oct 25 2026: a rounded, lit theme's corners would
    /// otherwise stick out of the radius). <paramref name="inset"/> is where
    /// the line's centre sits in from the bounds (half its width), and the arc
    /// is the frame's radius at that inset, as the Border's own rounded
    /// outline is. Null when neither run has a length.
    /// </summary>
    public static (LitRun TopRight, LitRun BottomLeft)? LitPaths(
        double width, double height, double cut, double edge, double side, double inset = 0, double radius = 0)
    {
        if ((edge <= 0 && side <= 0) || width <= 0 || height <= 0) return null;
        var d = inset;
        var c = Math.Max(0, Math.Min(cut, Math.Min(width, height) / 2));
        if (c <= 0)
        {
            // No cut: the corner is the inset bounds' own (a square turn, or the frame's curve at the inset).
            var arc = Math.Max(0, Math.Min(radius - d, Math.Min(width, height) / 2 - d));
            return (
                new LitRun(Path(
                    new Point(Math.Max(d, width - d - arc - edge), d),
                    new Point(width - d, d),
                    new Point(width - d, Math.Min(height - d, d + arc + side))), arc),
                new LitRun(Path(
                    new Point(d, Math.Max(d, height - d - arc - side)),
                    new Point(d, height - d),
                    new Point(Math.Min(width - d, d + arc + edge), height - d)), arc));
        }

        var lean = d * (Math.Sqrt(2) - 1);
        var topRight = Path(
            new Point(Math.Max(d, width - c - lean - edge), d),
            new Point(width - c - lean, d),
            new Point(width - d, c + lean),
            new Point(width - d, Math.Min(height - d, c + lean + side)));
        var bottomLeft = Path(
            new Point(d, Math.Max(d, height - c - lean - side)),
            new Point(d, height - c - lean),
            new Point(c + lean, height - d),
            new Point(Math.Min(width - d, c + lean + edge), height - d));
        return (new LitRun(topRight, 0), new LitRun(bottomLeft, 0));
    }

    /// <summary>
    /// The geometry the lit accents are drawn with: one open figure per run,
    /// a polyline, and on a rounded run the corner point replaced by a quarter
    /// arc of the run's radius (a PathGeometry with an ArcSegment), the
    /// straight parts stopping where the arc takes over. Frozen.
    /// </summary>
    public static PathGeometry LitGeometry(params LitRun[] runs)
    {
        var geometry = new PathGeometry();
        foreach (var run in runs)
        {
            var figure = new PathFigure { StartPoint = run[0], IsClosed = false, IsFilled = false };
            if (run.Arc > 0 && run.Count == 3)
            {
                var (from, corner, to) = (run[0], run[1], run[2]);
                var inward = Towards(corner, from, run.Arc);
                var outward = Towards(corner, to, run.Arc);
                // Which way it turns: y grows downwards, so a positive cross product is clockwise on screen.
                var a = corner - from;
                var b = to - corner;
                var sweep = a.X * b.Y - a.Y * b.X >= 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;
                figure.Segments.Add(new LineSegment(inward, isStroked: true));
                figure.Segments.Add(new ArcSegment(outward, new Size(run.Arc, run.Arc), 0, isLargeArc: false, sweep, isStroked: true));
                figure.Segments.Add(new LineSegment(to, isStroked: true));
            }
            else
            {
                figure.Segments.Add(new PolyLineSegment(run.Skip(1), isStroked: true));
            }
            geometry.Figures.Add(figure);
        }
        geometry.Freeze();
        return geometry;
    }

    // The point the given distance from a corner towards a neighbour (never past it).
    private static Point Towards(Point corner, Point neighbour, double distance)
    {
        var v = neighbour - corner;
        var length = v.Length;
        return length <= 0 ? corner : corner + v * (Math.Min(distance, length) / length);
    }

    /// <summary>
    /// The lit line along the frame's whole top edge (<c>Lit.Top</c>, its
    /// <paramref name="stroke"/> width; 0 = none, so null), its centre half
    /// that width in, as the lit corners'. Without a cut it spans the straight
    /// part of the edge, from the left corner's <paramref name="radius"/> to
    /// the right one's, half the width in from each; with a cut it runs from
    /// the square top-left corner to where the top-right cut begins (the top
    /// edge's one cut). Null too when the edge leaves it no length.
    /// </summary>
    public static IReadOnlyList<Point>? LitTop(double width, double height, double cut, double radius, double stroke)
    {
        if (stroke <= 0 || width <= 0 || height <= 0) return null;
        var d = stroke / 2;
        double from, to;
        if (cut > 0)
        {
            var c = Math.Min(cut, Math.Min(width, height) / 2);
            from = d;
            to = width - c - d * (Math.Sqrt(2) - 1); // the inset top edge's end at the cut, as CutPolygon has it
        }
        else
        {
            var r = Math.Max(0, radius);
            from = r + d;
            to = width - r - d;
        }
        return to > from ? new[] { new Point(from, d), new Point(to, d) } : null;
    }

    /// <summary>A polyline's length.</summary>
    public static double Length(IReadOnlyList<Point> path)
    {
        var total = 0.0;
        for (var i = 1; i < path.Count; i++) total += (path[i] - path[i - 1]).Length;
        return total;
    }

    /// <summary>A lit run's length as drawn: on a rounded run the quarter arc in place of the square corner it rounds.</summary>
    public static double Length(LitRun run) =>
        Length(run.Points) - (run.Arc > 0 && run.Count == 3 ? (2 - Math.PI / 2) * run.Arc : 0);

    // With no cut the two corner points coincide: one of them is dropped.
    private static IReadOnlyList<Point> Path(params Point[] points)
    {
        var list = new List<Point>(points.Length);
        foreach (var p in points)
            if (list.Count == 0 || (p - list[^1]).Length > 1e-9) list.Add(p);
        return list;
    }

    #endregion

    #region The glow

    /// <summary>One stroke of a painted glow: its width and the line colour's opacity.</summary>
    public readonly record struct GlowStroke(double Width, double Opacity);

    /// <summary>
    /// A glow of the given radius as three strokes of the line's own colour,
    /// widest first (the order they are drawn, under the line): 2.0, 1.4 and
    /// 0.8 × the radius wide at 5%, 10% and 18%. Stacked, they fade out to
    /// the radius on each side of the line. Empty for a radius of 0.
    /// </summary>
    public static IReadOnlyList<GlowStroke> Glow(double radius) =>
        radius <= 0
            ? Array.Empty<GlowStroke>()
            : new[]
            {
                new GlowStroke(2.0 * radius, 0.05),
                new GlowStroke(1.4 * radius, 0.10),
                new GlowStroke(0.8 * radius, 0.18),
            };

    #endregion

    #region The device-pixel snap

    /// <summary>A coordinate moved onto the nearest whole device pixel (<paramref name="pixelsPerDip"/>: 1 at 100%, 1.25 at 125%).</summary>
    public static double Snap(double value, double pixelsPerDip) =>
        Math.Round(value * pixelsPerDip, MidpointRounding.AwayFromZero) / pixelsPerDip;

    /// <summary>A stroke's width in whole device pixels, at least one.</summary>
    public static double SnapWidth(double width, double pixelsPerDip) =>
        Math.Max(1, Math.Round(width * pixelsPerDip, MidpointRounding.AwayFromZero)) / pixelsPerDip;

    /// <summary>
    /// Where to put the centre of a stroke of the given width meant to sit at
    /// <paramref name="centre"/>, so both its edges fall on device-pixel
    /// boundaries: its near edge snapped, its width in whole device pixels
    /// (<see cref="SnapWidth"/>). Use it with that snapped width.
    /// </summary>
    public static double SnapLine(double centre, double width, double pixelsPerDip)
    {
        var snapped = SnapWidth(width, pixelsPerDip);
        var edge = Snap(centre - width / 2, pixelsPerDip);
        return edge + snapped / 2;
    }

    /// <summary>A filled shape's corners (the glass), each coordinate on a whole device pixel.</summary>
    public static IReadOnlyList<Point> SnapPoints(IReadOnlyList<Point> points, double pixelsPerDip) =>
        points.Select(p => new Point(Snap(p.X, pixelsPerDip), Snap(p.Y, pixelsPerDip))).ToList();

    /// <summary>
    /// A stroked path's points (an outline, a lit accent), each coordinate
    /// placed as <see cref="SnapLine"/> places a line of that width: the
    /// straight edges then cover whole device pixels. Draw it with
    /// <see cref="SnapWidth"/>.
    /// </summary>
    public static IReadOnlyList<Point> SnapStroke(IReadOnlyList<Point> points, double width, double pixelsPerDip) =>
        points.Select(p => new Point(SnapLine(p.X, width, pixelsPerDip), SnapLine(p.Y, width, pixelsPerDip))).ToList();

    /// <summary>
    /// A lit run snapped as a stroked path is: its line ends and its corner on
    /// whole device pixels, the arc kept, so a rounded corner's arc runs
    /// between the snapped edges as the Border's own rounded outline does.
    /// </summary>
    public static LitRun SnapStroke(LitRun run, double width, double pixelsPerDip) =>
        new(SnapStroke(run.Points, width, pixelsPerDip), run.Arc);

    #endregion
}

/// <summary>
/// One lit accent round a frame's corner (<see cref="FrameGeometry.LitPaths"/>):
/// the polyline's points, from the run's start along the first edge, round
/// the corner, to its end along the second. <see cref="Arc"/> 0 is a sharp
/// run (round a cut, or a square turn); above 0 the run has three points and
/// the middle one, the corner, is rounded by an arc of that radius
/// (<see cref="FrameGeometry.LitGeometry"/> draws it). A list of its points,
/// so it snaps and indexes as the polylines do.
/// </summary>
public sealed record LitRun(IReadOnlyList<Point> Points, double Arc) : IReadOnlyList<Point>
{
    public int Count => Points.Count;

    public Point this[int index] => Points[index];

    public IEnumerator<Point> GetEnumerator() => Points.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

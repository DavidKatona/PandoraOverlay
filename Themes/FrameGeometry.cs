using System.Windows;

namespace PandoraOverlay;

/// <summary>
/// The geometry of a themed frame (PLAN.md sections 3 and 4), pure and
/// tested; step 2's ThemedBorder draws it. Three pieces:
/// <list type="bullet">
/// <item>the frame's polygon with its two cut corners (45°, the hex angle,
/// top-right and bottom-left), inset so a stroke can sit inside the bounds;</item>
/// <item>the lit accents, one path round each of those two corners;</item>
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
    /// along the cut, down the right side) and round the bottom-left one (up
    /// the left side, along the cut, along the bottom). Each runs
    /// <paramref name="edge"/> along the top or bottom edge and
    /// <paramref name="side"/> along the side, both measured past the cut;
    /// with no cut it turns a square corner (the map window: square corners,
    /// lit, no cut). <paramref name="inset"/> is where the line's centre sits
    /// in from the bounds (half its width). Null when neither run has a length.
    /// </summary>
    public static (IReadOnlyList<Point> TopRight, IReadOnlyList<Point> BottomLeft)? LitPaths(
        double width, double height, double cut, double edge, double side, double inset = 0)
    {
        if ((edge <= 0 && side <= 0) || width <= 0 || height <= 0) return null;
        var c = Math.Max(0, Math.Min(cut, Math.Min(width, height) / 2));
        var d = inset;
        var lean = c > 0 ? d * (Math.Sqrt(2) - 1) : 0;

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
        return (topRight, bottomLeft);
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

    #endregion
}

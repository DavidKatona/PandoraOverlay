using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PandoraOverlay;

/// <summary>Which of the theme's frame looks a <see cref="ThemedBorder"/> wears.</summary>
public enum FrameKind
{
    /// <summary>A widget's frame: locked (the hairline and the lit cut corners), or Lit in edit mode (the whole outline).</summary>
    Widget,
    /// <summary>The control panel's: its own line, Lit for good with its glow painted inside the outline; no cut corners (its width varies).</summary>
    ControlPanel,
}

/// <summary>
/// The root of every widget and of the control panel (PLAN.md section 3):
/// a Border that wears the theme's frame. The XAML keeps its size, padding,
/// corner radius and thickness as before (a ThemedBorder is a Border, so
/// ApplyAppearance still finds <c>Content is Border</c> and puts the scale
/// and the glass on it), and names how deep its corners are cut,
/// <c>CutCorners="{DynamicResource Theme.Cut.Widget}"</c>: 0 in Default.
/// <para>
/// <b>Default is the Border's own drawing.</b> With nothing extra to paint
/// (no cut, no lit line or top line, no glow: Default's numbers, <see cref="Plain"/>)
/// OnRender is <c>base.OnRender</c>, so Default draws exactly as the plain
/// Border did. That is a check on the theme's numbers, never on which theme
/// is on (section 0): a theme with rounded corners and a glow gets the
/// rounded glass plus the painted extras.
/// </para>
/// <para>
/// <b>Otherwise</b> it draws the glass itself, as the polygon with its two
/// cut corners (top-right and bottom-left, <see cref="FrameGeometry"/>), the
/// outline half its width in, and over it the lit accents round the two cut
/// corners (and, in a theme that has one, a lit line along the whole top
/// edge, <c>Lit.Top</c>), with their glow painted as stacked strokes under the lines
/// (never an Effect: PLAN.md section 4) and kept inside the frame, so the
/// control panel's glow stays within its padding and its window keeps its
/// size. Every line is snapped to device pixels, at the PC's display scale
/// times the widget's own size slider. The content is clipped to the
/// polygon too (the padding already keeps it clear of the cuts).
/// </para>
/// <para>
/// <b>Lit</b> is edit mode (each window's OnEditModeChanged): the outline
/// takes the theme's edit line (Default's gold, as BorderEdit was) and, in a
/// theme that has them, its width and glow; the lit corners are the locked
/// frame's. The outline's colour is always set here, from the theme's
/// dictionary, so the XAML names no BorderBrush. Every token it reads is
/// followed by resource reference, so a theme switch redraws it by itself.
/// </para>
/// </summary>
public class ThemedBorder : Border
{
    #region The XAML's say

    public static readonly DependencyProperty CutCornersProperty = DependencyProperty.Register(
        nameof(CutCorners), typeof(double), typeof(ThemedBorder),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((ThemedBorder)d).UpdateClip()));

    /// <summary>How far the top-right and bottom-left corners are cut, 0 = none: the theme's number for this kind of frame (<c>Theme.Cut.Widget</c>).</summary>
    public double CutCorners
    {
        get => (double)GetValue(CutCornersProperty);
        set => SetValue(CutCornersProperty, value);
    }

    public static readonly DependencyProperty LitProperty = DependencyProperty.Register(
        nameof(Lit), typeof(bool), typeof(ThemedBorder),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((ThemedBorder)d).Follow()));

    /// <summary>Edit mode: the whole outline in the theme's edit look. The control panel is Lit for good.</summary>
    public bool Lit
    {
        get => (bool)GetValue(LitProperty);
        set => SetValue(LitProperty, value);
    }

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(FrameKind), typeof(ThemedBorder),
        new FrameworkPropertyMetadata(FrameKind.Widget, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((ThemedBorder)d).Follow()));

    /// <summary>Whose frame look it wears: a widget's (the default) or the control panel's.</summary>
    public FrameKind Kind
    {
        get => (FrameKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    #endregion

    #region The theme's say (followed by resource reference)

    private static DependencyProperty Followed(string name, Type type, object fallback) =>
        DependencyProperty.Register(name, type, typeof(ThemedBorder),
            new FrameworkPropertyMetadata(fallback, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyProperty LitEdgeProperty = Followed("LitEdge", typeof(double), 0.0);
    private static readonly DependencyProperty LitSideProperty = Followed("LitSide", typeof(double), 0.0);
    private static readonly DependencyProperty LitWidthProperty = Followed("LitWidth", typeof(double), 0.0);
    private static readonly DependencyProperty LitColorProperty = Followed("LitColor", typeof(Color), Colors.Transparent);
    private static readonly DependencyProperty LitTopProperty = Followed("LitTop", typeof(double), 0.0);
    private static readonly DependencyProperty CornerGlowProperty = Followed("CornerGlow", typeof(double), 0.0);
    private static readonly DependencyProperty GlowRadiusProperty = Followed("GlowRadius", typeof(double), 0.0);
    private static readonly DependencyProperty GlowColorProperty = Followed("GlowColor", typeof(Color), Colors.Transparent);

    public ThemedBorder()
    {
        SetResourceReference(LitEdgeProperty, "Theme.Lit.Edge");
        SetResourceReference(LitSideProperty, "Theme.Lit.Side");
        SetResourceReference(LitWidthProperty, "Theme.Lit.Width");
        SetResourceReference(LitColorProperty, "Theme.Frame.Locked.Lit.Color");
        SetResourceReference(LitTopProperty, "Theme.Lit.Top");
        SetResourceReference(CornerGlowProperty, "Theme.Glow.Corners"); // the top line's glow, in either state
        Follow();
    }

    /// <summary>
    /// Points the outline's colour and the glow at the tokens of this frame's
    /// kind and state: locked, a widget's lit corners glow (Glow.Corners);
    /// Lit, the whole outline does (Glow.Edit, or the control panel's own).
    /// </summary>
    private void Follow()
    {
        var (line, glowColour, glow) = (Kind, Lit) switch
        {
            (FrameKind.ControlPanel, true) => ("Theme.Frame.ControlPanel.Line", "Theme.Frame.ControlPanel.Glow.Color", "Theme.Glow.ControlPanel"),
            (FrameKind.ControlPanel, false) => ("Theme.Frame.ControlPanel.Line", "Theme.Frame.ControlPanel.Glow.Color", null),
            (_, true) => ("Theme.Frame.Edit.Line", "Theme.Frame.Edit.Glow.Color", "Theme.Glow.Edit"),
            _ => ("Theme.Frame.Locked.Line", "Theme.Frame.Locked.Glow.Color", "Theme.Glow.Corners"),
        };
        SetResourceReference(BorderBrushProperty, line);
        SetResourceReference(GlowColorProperty, glowColour);
        if (glow is null) ClearValue(GlowRadiusProperty);
        else SetResourceReference(GlowRadiusProperty, glow);
    }

    #endregion

    #region Drawing

    /// <summary>
    /// True when the theme gives this frame nothing beyond the Border's own
    /// drawing: no cut, no lit line (corners or top), no glow. Default's
    /// numbers are all 0, so every Default frame is the plain Border, untouched.
    /// </summary>
    internal static bool Plain(double cut, double litWidth, double litTop, double glow) =>
        cut <= 0 && litWidth <= 0 && litTop <= 0 && glow <= 0;

    protected override void OnRender(DrawingContext dc)
    {
        var cut = CutCorners;
        var litWidth = (double)GetValue(LitWidthProperty);
        var litTop = (double)GetValue(LitTopProperty);
        var glow = (double)GetValue(GlowRadiusProperty);
        if (Plain(cut, litWidth, litTop, glow))
        {
            base.OnRender(dc);
            return;
        }
        DrawFrame(dc, cut, litWidth, litTop, glow);
    }

    private void DrawFrame(DrawingContext dc, double cut, double litWidth, double litTop, double glow)
    {
        var w = RenderSize.Width;
        var h = RenderSize.Height;
        if (w <= 0 || h <= 0) return;
        var ppd = DevicePixels();
        var radius = CornerRadius.TopLeft;

        // The glass: the cut polygon, or the rounded box of a theme without a cut.
        var glass = FrameGeometry.CutPolygon(w, h, cut) is { } corners
            ? Polygon(FrameGeometry.SnapPoints(corners, ppd))
            : Rounded(new Rect(0, 0, w, h), radius, 0, ppd);
        dc.DrawGeometry(Background, null, glass);

        // The outline: Lit, at the theme's lit width; locked, the Border's own thickness.
        var lit = Lit;
        var outlineWidth = lit && litWidth > 0 ? litWidth : BorderThickness.Left;
        var outline = outlineWidth <= 0 ? null
            : FrameGeometry.CutPolygon(w, h, cut, outlineWidth / 2) is { } inset
                ? Polygon(FrameGeometry.SnapStroke(inset, outlineWidth, ppd))
                : Rounded(new Rect(0, 0, w, h), radius, outlineWidth, ppd);

        // A locked widget's lit accents round its two cut corners (on a
        // rounded frame without a cut, following the corners' curve).
        Geometry? accents = null;
        if (!lit && Kind == FrameKind.Widget && litWidth > 0 &&
            FrameGeometry.LitPaths(w, h, cut, (double)GetValue(LitEdgeProperty), (double)GetValue(LitSideProperty), litWidth / 2, radius) is { } paths)
        {
            accents = FrameGeometry.LitGeometry(FrameGeometry.SnapStroke(paths.TopRight, litWidth, ppd), FrameGeometry.SnapStroke(paths.BottomLeft, litWidth, ppd));
        }

        // A lit line along the whole top edge, in either state: the lit
        // corners' colour (locked) or the frame's lit line (Lit, the control
        // panel), with the corners' glow.
        var top = FrameGeometry.LitTop(w, h, cut, radius, litTop) is { } run
            ? Lines(FrameGeometry.SnapStroke(run, litTop, ppd))
            : null;

        // The glow under the lines that glow, kept inside the frame.
        var glowColour = (Color)GetValue(GlowColorProperty);
        var cornerGlow = (double)GetValue(CornerGlowProperty);
        if ((glow > 0 && (lit ? outline : accents) is not null) || (cornerGlow > 0 && top is not null))
        {
            dc.PushClip(glass);
            if (glow > 0 && (lit ? outline : accents) is { } glowing)
                foreach (var stroke in FrameGeometry.Glow(glow))
                    dc.DrawGeometry(null, GlowPen(glowColour, stroke), glowing);
            if (top is not null)
                foreach (var stroke in FrameGeometry.Glow(cornerGlow))
                    dc.DrawGeometry(null, GlowPen(glowColour, stroke), top);
            dc.Pop();
        }

        if (outline is not null && BorderBrush is { } line)
            dc.DrawGeometry(null, Frozen(new Pen(line, FrameGeometry.SnapWidth(outlineWidth, ppd))), outline);
        var litBrush = Brush((Color)GetValue(LitColorProperty));
        if (accents is not null)
            dc.DrawGeometry(null, Frozen(new Pen(litBrush, FrameGeometry.SnapWidth(litWidth, ppd))), accents);
        if (top is not null && (!lit && Kind == FrameKind.Widget ? litBrush : BorderBrush) is { } topBrush)
            dc.DrawGeometry(null, Frozen(new Pen(topBrush, FrameGeometry.SnapWidth(litTop, ppd))), top);
    }

    /// <summary>Device pixels per DIP here: the display scale times the widget's own size slider (ApplyAppearance's LayoutTransform).</summary>
    private double DevicePixels() => DevicePixelsOf(this);

    /// <summary>
    /// Device pixels per DIP at any element of a themed window: the display
    /// scale times every size slider above it (ApplyAppearance puts the
    /// widget's on its root as a LayoutTransform). The bars' inset line
    /// (BarTrack) snaps with it as the frame does.
    /// </summary>
    internal static double DevicePixelsOf(Visual element)
    {
        var scale = 1.0;
        for (DependencyObject? at = element; at is not null; at = VisualTreeHelper.GetParent(at))
            if (at is FrameworkElement { LayoutTransform: ScaleTransform s }) scale *= s.ScaleX;
        return VisualTreeHelper.GetDpi(element).PixelsPerDip * scale;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        UpdateClip();
    }

    /// <summary>The content clipped to the cut polygon; no clip at all without a cut (Default).</summary>
    private void UpdateClip()
    {
        if (FrameGeometry.CutPolygon(ActualWidth, ActualHeight, CutCorners) is { } corners) Clip = Polygon(corners);
        else ClearValue(ClipProperty);
    }

    /// <summary>One stroke of a painted glow in the given colour (its alpha times the stroke's opacity), with round joins and ends.</summary>
    internal static Pen GlowPen(Color colour, FrameGeometry.GlowStroke stroke) => Frozen(new Pen(
        Brush(Color.FromArgb((byte)Math.Round(colour.A * stroke.Opacity), colour.R, colour.G, colour.B)), stroke.Width)
    {
        LineJoin = PenLineJoin.Round,
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round,
    });

    private static SolidColorBrush Brush(Color colour)
    {
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        return brush;
    }

    private static Pen Frozen(Pen pen)
    {
        pen.Freeze();
        return pen;
    }

    private static StreamGeometry Polygon(IReadOnlyList<Point> points) => Figures(closed: true, points);

    private static StreamGeometry Lines(params IReadOnlyList<Point>[] paths) => Figures(closed: false, paths);

    private static StreamGeometry Figures(bool closed, params IReadOnlyList<Point>[] figures)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            foreach (var points in figures)
            {
                context.BeginFigure(points[0], isFilled: closed, isClosed: closed);
                context.PolyLineTo(points.Skip(1).ToList(), isStroked: true, isSmoothJoin: false);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    /// <summary>A box with rounded corners, inset by half a stroke (0 for a fill), its edges on device pixels.</summary>
    private static Geometry Rounded(Rect bounds, double radius, double stroke, double ppd)
    {
        Rect box;
        if (stroke > 0)
        {
            var left = FrameGeometry.SnapLine(bounds.Left + stroke / 2, stroke, ppd);
            var top = FrameGeometry.SnapLine(bounds.Top + stroke / 2, stroke, ppd);
            var right = FrameGeometry.SnapLine(bounds.Right - stroke / 2, stroke, ppd);
            var bottom = FrameGeometry.SnapLine(bounds.Bottom - stroke / 2, stroke, ppd);
            box = new Rect(new Point(left, top), new Point(Math.Max(left, right), Math.Max(top, bottom)));
        }
        else
        {
            box = new Rect(new Point(FrameGeometry.Snap(bounds.Left, ppd), FrameGeometry.Snap(bounds.Top, ppd)),
                           new Point(FrameGeometry.Snap(bounds.Right, ppd), FrameGeometry.Snap(bounds.Bottom, ppd)));
        }
        var r = Math.Max(0, radius - stroke / 2);
        var geometry = new RectangleGeometry(box, r, r);
        geometry.Freeze();
        return geometry;
    }

    #endregion
}

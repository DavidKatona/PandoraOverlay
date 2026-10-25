using System.Windows;
using System.Windows.Media;

namespace PandoraOverlay;

/// <summary>
/// The inner outline of a square cut at two corners, like a frame's
/// (<see cref="FrameGeometry.CutPolygon"/>: top-right and bottom-left), for a
/// surface INSIDE a frame that has a cut of its own: the minimap's map
/// square (<c>Theme.Cut.MapSquare</c>, DESIGN.md: "the map square gets the
/// 6 px cut corners and a 1 px accent-line inner outline"). It lies over
/// the surface, which its host clips to the same polygon, and draws the
/// line half its width in, snapped to device pixels as ThemedBorder's are
/// (the display scale times the widget's size slider). Without a cut
/// (Default) it draws nothing at all: the map square has no outline of its
/// own today, and must not get one.
/// </summary>
public sealed class CutOutline : FrameworkElement
{
    public static readonly DependencyProperty CutProperty = DependencyProperty.Register(
        nameof(Cut), typeof(double), typeof(CutOutline),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>How far the two corners are cut, 0 = none (and no outline).</summary>
    public double Cut
    {
        get => (double)GetValue(CutProperty);
        set => SetValue(CutProperty, value);
    }

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(CutOutline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(CutOutline),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public CutOutline()
    {
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = StrokeThickness;
        if (Stroke is not { } brush || width <= 0 ||
            FrameGeometry.CutPolygon(RenderSize.Width, RenderSize.Height, Cut, width / 2) is not { } inset) return;
        var ppd = ThemedBorder.DevicePixelsOf(this);
        var points = FrameGeometry.SnapStroke(inset, width, ppd);
        var outline = new StreamGeometry();
        using (var context = outline.Open())
        {
            context.BeginFigure(points[0], isFilled: false, isClosed: true);
            context.PolyLineTo(points.Skip(1).ToList(), isStroked: true, isSmoothJoin: false);
        }
        outline.Freeze();
        var pen = new Pen(brush, FrameGeometry.SnapWidth(width, ppd));
        pen.Freeze();
        dc.DrawGeometry(null, pen, outline);
    }
}

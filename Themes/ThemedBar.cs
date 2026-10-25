using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PandoraOverlay;

/// <summary>
/// A stats bar's track (PLAN.md section 6): a Border that draws its glass as
/// before and, in a theme that has one, an inset line round its inside
/// (<c>Bars.&lt;kind&gt;.TrackLine</c>, <c>Bars.TrackLineWidth</c>). The line
/// is a stroke of its own, not a BorderThickness: a border would push the fill
/// (the track's child) inwards and move every bar by a pixel. The fill sits
/// over the line, so the line shows round the empty part only.
/// <para>
/// With Default's width of 0 OnRender is the Border's own drawing, untouched,
/// so Default is identical by construction (a check on the number, never on
/// which theme is on). The line's outer edges are held on device pixels by a
/// guideline set and its width is whole device pixels, at the PC's display
/// scale times the widget's size slider, so a 1 px line stays one crisp pixel
/// at 125%.
/// </para>
/// </summary>
public class BarTrack : Border
{
    public static readonly DependencyProperty LineWidthProperty = DependencyProperty.Register(
        nameof(LineWidth), typeof(double), typeof(BarTrack),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The inset line's width, 0 = none (<c>{DynamicResource Theme.Bars.TrackLineWidth}</c>).</summary>
    public double LineWidth
    {
        get => (double)GetValue(LineWidthProperty);
        set => SetValue(LineWidthProperty, value);
    }

    public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
        nameof(LineBrush), typeof(Brush), typeof(BarTrack),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The inset line's colour (<c>{DynamicResource Theme.Bars.Health.TrackLine}</c>).</summary>
    public Brush? LineBrush
    {
        get => (Brush?)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = LineWidth;
        var w = RenderSize.Width;
        var h = RenderSize.Height;
        if (width <= 0 || LineBrush is not { } brush || w <= 0 || h <= 0) return;

        var stroke = FrameGeometry.SnapWidth(width, ThemedBorder.DevicePixelsOf(this));
        var pen = new Pen(brush, stroke);
        pen.Freeze();
        var r = Math.Max(0, CornerRadius.TopLeft - stroke / 2);
        dc.PushGuidelineSet(new GuidelineSet(new[] { 0.0, w }, new[] { 0.0, h }));
        dc.DrawRoundedRectangle(null, pen, new Rect(stroke / 2, stroke / 2, Math.Max(0, w - stroke), Math.Max(0, h - stroke)), r, r);
        dc.Pop();
    }
}

/// <summary>
/// A stats bar's fill: a Border whose background is the bar (MainWindow.Stats
/// paints its colour and length), and, while the bar is critical, the theme's
/// halo behind it (<c>Bars.Below25Halo</c>, PLAN.md section 6): a glow in the
/// fill's own colour, painted as FrameGeometry's three strokes round the fill
/// before the fill itself is drawn over them (never an Effect: PLAN.md
/// section 4). The halo pulses with the fill, as part of the same alarm.
/// With no halo (Default's 0, or a bar that isn't critical) OnRender is the
/// Border's own drawing.
/// </summary>
public class BarFill : Border
{
    public static readonly DependencyProperty HaloProperty = DependencyProperty.Register(
        nameof(Halo), typeof(double), typeof(BarFill),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The halo's radius, 0 = none: set by the stats panel from the bar's state and the theme's number.</summary>
    public double Halo
    {
        get => (double)GetValue(HaloProperty);
        set => SetValue(HaloProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var halo = Halo;
        if (halo > 0 && Background is SolidColorBrush fill && RenderSize.Width > 0 && RenderSize.Height > 0)
        {
            var r = CornerRadius.TopLeft;
            var outline = new RectangleGeometry(new Rect(RenderSize), r, r);
            outline.Freeze();
            foreach (var stroke in FrameGeometry.Glow(halo))
                dc.DrawGeometry(null, ThemedBorder.GlowPen(fill.Color, stroke), outline);
        }
        base.OnRender(dc);
    }
}

using System.Windows;
using System.Windows.Media;

namespace PandoraOverlay;

/// <summary>
/// A glow round a shape, painted (PLAN.md section 4): the shape's outline
/// stroked three times, wider and fainter (<see cref="FrameGeometry.Glow"/>),
/// in the theme's glow colour. It sits UNDER the shape it lights, with the
/// same geometry and the same transform, so the shape's own fill covers the
/// glow's inner half and only the outer half shows: a soft halo, never an
/// Effect (a live DropShadowEffect halved the frame rate while the minimap
/// glides; the owner's NameLayer rule says no blur). Your arrow on the maps
/// wears one (<c>Theme.Glow.You</c>, <c>Theme.Map.You.Glow</c>). With a
/// radius of 0 (Default) it draws nothing at all, so a map without a glow is
/// exactly the map it was. The drawing is recorded once per change; turning
/// and gliding move it by its transform, as they move the arrow.
/// </summary>
public sealed class PaintedGlow : FrameworkElement
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(Geometry), typeof(PaintedGlow),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The shape it lights (your arrow's own geometry).</summary>
    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(
        nameof(Radius), typeof(double), typeof(PaintedGlow),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>How far the glow reaches, 0 = none: the theme's number.</summary>
    public double Radius
    {
        get => (double)GetValue(RadiusProperty);
        set => SetValue(RadiusProperty, value);
    }

    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color), typeof(Color), typeof(PaintedGlow),
        new FrameworkPropertyMetadata(Colors.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The glow's colour at full strength; each stroke takes its share of the alpha.</summary>
    public Color Color
    {
        get => (Color)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public PaintedGlow()
    {
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (Data is not { } shape || Radius <= 0 || Color.A == 0) return;
        foreach (var stroke in FrameGeometry.Glow(Radius))
            dc.DrawGeometry(null, ThemedBorder.GlowPen(Color, stroke), shape);
    }
}

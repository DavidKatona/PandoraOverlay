using System.Windows;
using System.Windows.Media;

namespace PandoraOverlay;

/// <summary>
/// Draws the shape a theme picks for one slot (PLAN.md section 3): the
/// fracture badge behind its text, the title glyph before a title, the
/// radio (its ring, and its mark Inset) and the drawn slider's thumb. The slot is the XAML's
/// (<see cref="Slot"/>), the pick is the theme's
/// (<c>Kind="{DynamicResource Theme.Shapes.Badge}"</c>), and the geometry is
/// the catalogue's (<see cref="ShapeCatalogue"/>), so no theme ships XAML.
/// <para>
/// A box and a circle are drawn with the very calls the Border and the
/// Ellipse they replace made (DrawRoundedRectangle, DrawEllipse), so
/// Default draws as before. <see cref="ShapeKind.None"/> draws nothing and
/// takes no room at all, gap included: Default's title glyph must leave the
/// title exactly where it was. A slot that fills its cell (the badge) asks
/// for no room and takes what the cell gives it. A pick the slot doesn't
/// offer draws nothing (ThemeFileTests refuse such a theme first). The glow
/// is painted (FrameGeometry.Glow), never an Effect.
/// </para>
/// </summary>
public class ThemeShape : FrameworkElement
{
    public static readonly DependencyProperty SlotProperty = DependencyProperty.Register(
        nameof(Slot), typeof(ShapeSlot), typeof(ThemeShape),
        new FrameworkPropertyMetadata(ShapeSlot.Badge, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Which slot this shape fills: its natural size and the picks it accepts.</summary>
    public ShapeSlot Slot
    {
        get => (ShapeSlot)GetValue(SlotProperty);
        set => SetValue(SlotProperty, value);
    }

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(ShapeKind), typeof(ThemeShape),
        new FrameworkPropertyMetadata(ShapeKind.None, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The theme's pick for the slot (<c>{DynamicResource Theme.Shapes.…}</c>).</summary>
    public ShapeKind Kind
    {
        get => (ShapeKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(ThemeShape),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(ThemeShape),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(ThemeShape),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(CornerRadius), typeof(ThemeShape),
        new FrameworkPropertyMetadata(new CornerRadius(), FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>A Box's corners (<c>{DynamicResource Theme.Corner.Badge}</c>); every rounded corner here is uniform.</summary>
    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public static readonly DependencyProperty GlowRadiusProperty = DependencyProperty.Register(
        nameof(GlowRadius), typeof(double), typeof(ThemeShape),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The painted glow round the outline, 0 = none.</summary>
    public double GlowRadius
    {
        get => (double)GetValue(GlowRadiusProperty);
        set => SetValue(GlowRadiusProperty, value);
    }

    public static readonly DependencyProperty GlowColorProperty = DependencyProperty.Register(
        nameof(GlowColor), typeof(Color), typeof(ThemeShape),
        new FrameworkPropertyMetadata(Colors.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public Color GlowColor
    {
        get => (Color)GetValue(GlowColorProperty);
        set => SetValue(GlowColorProperty, value);
    }

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(ThemeShape),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>The space after a drawn shape (the title glyph before its title); none when nothing is drawn.</summary>
    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    public static readonly DependencyProperty InsetProperty = DependencyProperty.Register(
        nameof(Inset), typeof(double), typeof(ThemeShape),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// Draws the shape this far inside its box on every side, at the same
    /// centre, taking the same room: the radio's mark is the slot's own
    /// pick drawn 4 px in (Default's 7 px dot in the 15 px ring, as the
    /// Ellipse it replaces had it; a hexagon theme's inner hexagon).
    /// </summary>
    public double Inset
    {
        get => (double)GetValue(InsetProperty);
        set => SetValue(InsetProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        ShapeCatalogue.NaturalSize(Slot, Kind) is { Width: > 0 } box ? new Size(box.Width + Gap, box.Height) : new Size(0, 0);

    protected override void OnRender(DrawingContext dc)
    {
        var slot = Slot;
        var kind = Kind;
        if (!ShapeCatalogue.Offers(slot, kind)) return;

        // A shape of its own size sits at the left, centred down, the gap after it; a filling one takes the whole cell.
        var box = ShapeCatalogue.NaturalSize(slot, kind) ?? RenderSize;
        var origin = new Point(0, Math.Max(0, (RenderSize.Height - box.Height) / 2));
        if (Inset > 0)
        {
            box = new Size(box.Width - 2 * Inset, box.Height - 2 * Inset);
            origin.Offset(Inset, Inset);
        }
        if (box.Width <= 0 || box.Height <= 0) return;

        var pen = Stroke is { } stroke && StrokeThickness > 0 ? Frozen(new Pen(stroke, StrokeThickness)) : null;
        var t = pen?.Thickness ?? 0;
        switch (ShapeCatalogue.Outline(slot, kind, box, CornerRadius.TopLeft, t))
        {
            case BoxOutline b:
                // A stroke sits inside the box, as a Border's does.
                var rect = new Rect(origin.X + b.Bounds.X + t / 2, origin.Y + b.Bounds.Y + t / 2,
                                    Math.Max(0, b.Bounds.Width - t), Math.Max(0, b.Bounds.Height - t));
                var r = Math.Max(0, b.Radius - t / 2);
                dc.DrawRoundedRectangle(Fill, pen, rect, r, r);
                break;
            case EllipseOutline e:
                var centre = new Point(origin.X + e.Centre.X, origin.Y + e.Centre.Y);
                if (GlowRadius > 0) Glow(dc, new EllipseGeometry(centre, e.RadiusX, e.RadiusY));
                dc.DrawEllipse(Fill, pen, centre, e.RadiusX, e.RadiusY);
                break;
            case PolygonOutline p:
                // Inset by half the stroke, so the line stays inside the box.
                var sx = box.Width > t ? (box.Width - t) / box.Width : 1;
                var sy = box.Height > t ? (box.Height - t) / box.Height : 1;
                var polygon = Polygon(p.Points.Select(q => new Point(origin.X + t / 2 + q.X * sx, origin.Y + t / 2 + q.Y * sy)));
                if (GlowRadius > 0) Glow(dc, polygon);
                dc.DrawGeometry(Fill, pen, polygon);
                break;
        }
    }

    /// <summary>The painted glow: three wider strokes of the glow colour, under the outline.</summary>
    private void Glow(DrawingContext dc, Geometry outline)
    {
        var colour = GlowColor;
        foreach (var stroke in FrameGeometry.Glow(GlowRadius))
        {
            var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(colour.A * stroke.Opacity), colour.R, colour.G, colour.B));
            brush.Freeze();
            dc.DrawGeometry(null, Frozen(new Pen(brush, stroke.Width) { LineJoin = PenLineJoin.Round }), outline);
        }
    }

    private static Pen Frozen(Pen pen)
    {
        pen.Freeze();
        return pen;
    }

    private static StreamGeometry Polygon(IEnumerable<Point> points)
    {
        var list = points.ToList();
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(list[0], isFilled: true, isClosed: true);
            context.PolyLineTo(list.Skip(1).ToList(), isStroked: true, isSmoothJoin: false);
        }
        geometry.Freeze();
        return geometry;
    }
}

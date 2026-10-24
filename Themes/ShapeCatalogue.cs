using System.Windows;

namespace PandoraOverlay;

/// <summary>The shapes a theme can pick by name, for the slots that have a shape (<see cref="ShapeCatalogue"/>).</summary>
public enum ShapeKind
{
    /// <summary>Nothing drawn, and nothing in the layout (the title glyph in Default).</summary>
    None,
    Circle,
    Hexagon,
    /// <summary>A rounded box of the slot's corner radius (the fracture badge in Default).</summary>
    Box,
    /// <summary>The slider's own WPF template: a Style pick, not a drawing (Theme.Apply).</summary>
    Stock,
}

/// <summary>The four slots that have a shape at all.</summary>
public enum ShapeSlot
{
    Radio,
    SliderThumb,
    Badge,
    TitleGlyph,
}

/// <summary>
/// The shape catalogue (PLAN.md section 3, the owner's ask, Oct 24 2026): the
/// shapes a theme can PICK, by name, for each slot, and their geometry. No
/// theme ships XAML (a theme editor could never write one); a shape the
/// catalogue lacks is added here once, in code, and every theme can pick it.
/// A theme can't invent one: that is what "a theme only paints" means.
/// <para>
/// Pure geometry, WPF-free but for <see cref="Point"/> / <see cref="Rect"/>
/// (plain structs), tested without rendering. Step 2's ThemeShape draws what
/// this describes: a polygon as a path, a circle and a box with the very
/// calls the old Ellipse and Border made (DrawEllipse, DrawRoundedRectangle),
/// so Default draws as today.
/// </para>
/// </summary>
public static class ShapeCatalogue
{
    #region The catalogue

    /// <summary>The title glyph's box: a 12 × 13 hexagon outline (DESIGN.md).</summary>
    public static readonly Size TitleGlyphSize = new(12, 13);

    /// <summary>The radio's box, square, as today's 15 px circle.</summary>
    public const double RadioSize = 15;

    /// <summary>How far a hexagon badge's pointed ends reach in from its sides.</summary>
    public const double BadgePoint = 5;

    private static readonly IReadOnlyDictionary<ShapeSlot, ShapeKind[]> Picks = new Dictionary<ShapeSlot, ShapeKind[]>
    {
        [ShapeSlot.Radio] = new[] { ShapeKind.Circle, ShapeKind.Hexagon },
        [ShapeSlot.SliderThumb] = new[] { ShapeKind.Stock, ShapeKind.Circle, ShapeKind.Hexagon },
        [ShapeSlot.Badge] = new[] { ShapeKind.Box, ShapeKind.Hexagon },
        [ShapeSlot.TitleGlyph] = new[] { ShapeKind.None, ShapeKind.Hexagon },
    };

    /// <summary>What a slot offers.</summary>
    public static IReadOnlyList<ShapeKind> Allowed(ShapeSlot slot) => Picks[slot];

    public static bool Offers(ShapeSlot slot, ShapeKind kind) => Picks[slot].Contains(kind);

    /// <summary>
    /// A shape by the name a theme file gives it ("hexagon"), ignoring case.
    /// An unknown name (or a number) is a FormatException, never a blank.
    /// </summary>
    public static ShapeKind Parse(string name)
    {
        var word = name?.Trim() ?? "";
        // Letters only: Enum.TryParse would take "1" or "+1" as a member's number.
        if (word.Length > 0 && word.All(char.IsLetter) && Enum.TryParse<ShapeKind>(word, ignoreCase: true, out var kind))
            return kind;
        throw new FormatException($"\"{name}\" is not a shape the catalogue knows ({string.Join(", ", Enum.GetNames<ShapeKind>().Select(n => n.ToLowerInvariant()))})");
    }

    /// <summary>The name a theme file uses for a shape.</summary>
    public static string NameOf(ShapeKind kind) => kind.ToString().ToLowerInvariant();

    /// <summary>
    /// A theme's four picks, each checked against its slot: a pick the slot
    /// doesn't offer (a "box" radio) is a FormatException naming the slot.
    /// </summary>
    public static void Check(ThemeTokens.ShapePicks picks)
    {
        Check(ShapeSlot.Radio, picks.Radio);
        Check(ShapeSlot.SliderThumb, picks.SliderThumb);
        Check(ShapeSlot.Badge, picks.Badge);
        Check(ShapeSlot.TitleGlyph, picks.TitleGlyph);
    }

    private static void Check(ShapeSlot slot, ShapeKind kind)
    {
        if (!Offers(slot, kind))
            throw new FormatException($"Shapes.{slot}: \"{NameOf(kind)}\" is not offered there ({string.Join(", ", Allowed(slot).Select(NameOf))})");
    }

    #endregion

    #region Geometry

    /// <summary>
    /// The outline a slot's pick draws in a box of the given size. A hexagon
    /// badge is the long hexagon (pointed ends <see cref="BadgePoint"/> in);
    /// every other hexagon is the upright one. A circle is inset by half its
    /// stroke, as WPF's Ellipse shape insets its own; a box fills its bounds
    /// with the corner radius, as a Border's background does. None and Stock
    /// draw nothing. A pick the slot doesn't offer throws.
    /// </summary>
    public static ShapeOutline Outline(ShapeSlot slot, ShapeKind kind, Size box, double cornerRadius = 0, double stroke = 0)
    {
        Check(slot, kind);
        return kind switch
        {
            ShapeKind.Hexagon when slot == ShapeSlot.Badge => new PolygonOutline(LongHexagon(box.Width, box.Height, BadgePoint)),
            ShapeKind.Hexagon => new PolygonOutline(Hexagon(box.Width, box.Height)),
            ShapeKind.Circle => new EllipseOutline(new Point(box.Width / 2, box.Height / 2),
                                                   Math.Max(0, (box.Width - stroke) / 2), Math.Max(0, (box.Height - stroke) / 2)),
            ShapeKind.Box => new BoxOutline(new Rect(0, 0, box.Width, box.Height), cornerRadius),
            _ => NoOutline.Instance, // None, Stock
        };
    }

    /// <summary>
    /// An upright hexagon (a point at the top and the bottom) filling the box:
    /// the game's hex HUD. For a 12 × 13 box it is near-regular (a regular one
    /// 13 tall is 11.3 wide). Clockwise from the top.
    /// </summary>
    public static IReadOnlyList<Point> Hexagon(double width, double height) => new[]
    {
        new Point(width / 2, 0),
        new Point(width, height / 4),
        new Point(width, height * 3 / 4),
        new Point(width / 2, height),
        new Point(0, height * 3 / 4),
        new Point(0, height / 4),
    };

    /// <summary>A long hexagon with pointed ends, for a badge: flat top and bottom, points reaching <paramref name="point"/> in from each side. Clockwise from the top-left.</summary>
    public static IReadOnlyList<Point> LongHexagon(double width, double height, double point)
    {
        var p = Math.Min(point, width / 2);
        return new[]
        {
            new Point(p, 0),
            new Point(width - p, 0),
            new Point(width, height / 2),
            new Point(width - p, height),
            new Point(p, height),
            new Point(0, height / 2),
        };
    }

    #endregion
}

/// <summary>What a catalogue shape draws (<see cref="ShapeCatalogue.Outline"/>).</summary>
public abstract record ShapeOutline;

/// <summary>A closed polygon (the last point joins the first).</summary>
public sealed record PolygonOutline(IReadOnlyList<Point> Points) : ShapeOutline;

/// <summary>An ellipse, drawn as DrawEllipse draws it.</summary>
public sealed record EllipseOutline(Point Centre, double RadiusX, double RadiusY) : ShapeOutline;

/// <summary>A rounded rectangle, drawn as DrawRoundedRectangle draws it.</summary>
public sealed record BoxOutline(Rect Bounds, double Radius) : ShapeOutline;

/// <summary>Nothing (None, and Stock, which is a Style pick).</summary>
public sealed record NoOutline : ShapeOutline
{
    public static readonly NoOutline Instance = new();
}

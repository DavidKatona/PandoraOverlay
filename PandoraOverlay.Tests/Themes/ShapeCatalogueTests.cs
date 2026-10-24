using System.Windows;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>The shape catalogue (Themes/ShapeCatalogue.cs): what each slot offers, the names, the geometry.</summary>
public class ShapeCatalogueTests
{
    public static IEnumerable<object[]> EveryPick() =>
        from slot in Enum.GetValues<ShapeSlot>()
        from kind in ShapeCatalogue.Allowed(slot)
        select new object[] { slot, kind };

    /// <summary>Every shape a slot offers draws something (or, for none and stock, deliberately nothing), and never throws.</summary>
    [Theory]
    [MemberData(nameof(EveryPick))]
    public void EveryPickDraws(ShapeSlot slot, ShapeKind kind)
    {
        var outline = ShapeCatalogue.Outline(slot, kind, new Size(15, 15), cornerRadius: 2, stroke: 1);
        switch (kind)
        {
            case ShapeKind.Hexagon: Assert.IsType<PolygonOutline>(outline); break;
            case ShapeKind.Circle: Assert.IsType<EllipseOutline>(outline); break;
            case ShapeKind.Box: Assert.IsType<BoxOutline>(outline); break;
            default: Assert.IsType<NoOutline>(outline); break; // none, stock
        }
    }

    [Fact]
    public void EachSlotOffersItsOwnShapes()
    {
        Assert.Equal(new[] { ShapeKind.Circle, ShapeKind.Hexagon }, ShapeCatalogue.Allowed(ShapeSlot.Radio));
        Assert.Equal(new[] { ShapeKind.Stock, ShapeKind.Circle, ShapeKind.Hexagon }, ShapeCatalogue.Allowed(ShapeSlot.SliderThumb));
        Assert.Equal(new[] { ShapeKind.Box, ShapeKind.Hexagon }, ShapeCatalogue.Allowed(ShapeSlot.Badge));
        Assert.Equal(new[] { ShapeKind.None, ShapeKind.Hexagon }, ShapeCatalogue.Allowed(ShapeSlot.TitleGlyph));
    }

    [Fact]
    public void APickTheSlotDoesNotOfferThrows()
    {
        Assert.Throws<FormatException>(() => ShapeCatalogue.Outline(ShapeSlot.Radio, ShapeKind.Box, new Size(15, 15)));
        Assert.Throws<FormatException>(() => ShapeCatalogue.Outline(ShapeSlot.Badge, ShapeKind.Stock, new Size(30, 15)));
    }

    [Theory]
    [InlineData("circle", ShapeKind.Circle)]
    [InlineData("hexagon", ShapeKind.Hexagon)]
    [InlineData("Hexagon", ShapeKind.Hexagon)]
    [InlineData("box", ShapeKind.Box)]
    [InlineData("none", ShapeKind.None)]
    [InlineData("stock", ShapeKind.Stock)]
    public void NamesParse(string name, ShapeKind kind) => Assert.Equal(kind, ShapeCatalogue.Parse(name));

    /// <summary>An unknown name is a failure, never a blank shape; a member's number is not a name.</summary>
    [Theory]
    [InlineData("star")]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("+1")]
    [InlineData("hexagon2")]
    public void AnUnknownNameIsAFailure(string name) => Assert.Throws<FormatException>(() => ShapeCatalogue.Parse(name));

    [Fact]
    public void TheTitleGlyphIsASixPointedHexagonInItsBox() =>
        AssertHexagon(ShapeCatalogue.Outline(ShapeSlot.TitleGlyph, ShapeKind.Hexagon, ShapeCatalogue.TitleGlyphSize), ShapeCatalogue.TitleGlyphSize);

    [Fact]
    public void TheRadioHexagonIsASixPointedHexagonInItsBox()
    {
        var box = new Size(ShapeCatalogue.RadioSize, ShapeCatalogue.RadioSize);
        AssertHexagon(ShapeCatalogue.Outline(ShapeSlot.Radio, ShapeKind.Hexagon, box), box);
    }

    [Fact]
    public void TheBadgeHexagonIsLongWithPointedEnds()
    {
        var box = new Size(40, 15);
        var points = AssertHexagon(ShapeCatalogue.Outline(ShapeSlot.Badge, ShapeKind.Hexagon, box), box);

        // Flat top and bottom, the ends pointed 5 px in, at half height.
        Assert.Equal(new Point(ShapeCatalogue.BadgePoint, 0), points[0]);
        Assert.Equal(new Point(40 - ShapeCatalogue.BadgePoint, 0), points[1]);
        Assert.Equal(new Point(40, 7.5), points[2]);
        Assert.Equal(new Point(0, 7.5), points[5]);
    }

    /// <summary>A circle is inset by half its stroke, as WPF's Ellipse insets its own: today's 15 px radio ring.</summary>
    [Fact]
    public void ACircleSitsAsTheOldEllipseDid()
    {
        var circle = Assert.IsType<EllipseOutline>(ShapeCatalogue.Outline(ShapeSlot.Radio, ShapeKind.Circle, new Size(15, 15), stroke: 1));
        Assert.Equal(new Point(7.5, 7.5), circle.Centre);
        Assert.Equal(7, circle.RadiusX);
        Assert.Equal(7, circle.RadiusY);
    }

    /// <summary>A box fills its bounds with the corner radius, as the old badge Border's background did.</summary>
    [Fact]
    public void ABoxFillsItsBoundsAsTheOldBorderDid()
    {
        var box = Assert.IsType<BoxOutline>(ShapeCatalogue.Outline(ShapeSlot.Badge, ShapeKind.Box, new Size(40, 15), cornerRadius: 4));
        Assert.Equal(new Rect(0, 0, 40, 15), box.Bounds);
        Assert.Equal(4, box.Radius);
    }

    [Fact]
    public void DefaultsPicksAreInTheCatalogue() => ShapeCatalogue.Check(DefaultTheme.Tokens.Shapes);

    /// <summary>Six distinct points, all inside the box, mirror-symmetric left to right and top to bottom.</summary>
    private static IReadOnlyList<Point> AssertHexagon(ShapeOutline outline, Size box)
    {
        var points = Assert.IsType<PolygonOutline>(outline).Points;
        Assert.Equal(6, points.Count);
        Assert.Equal(6, points.Distinct().Count());
        foreach (var p in points)
        {
            Assert.InRange(p.X, 0, box.Width);
            Assert.InRange(p.Y, 0, box.Height);
            Assert.Contains(points, q => Math.Abs(q.X - (box.Width - p.X)) < 1e-9 && Math.Abs(q.Y - p.Y) < 1e-9);  // left ↔ right
            Assert.Contains(points, q => Math.Abs(q.X - p.X) < 1e-9 && Math.Abs(q.Y - (box.Height - p.Y)) < 1e-9); // top ↔ bottom
        }
        // It reaches every side of its box: nothing drawn smaller than its slot.
        Assert.Equal(0, points.Min(p => p.X));
        Assert.Equal(box.Width, points.Max(p => p.X));
        Assert.Equal(0, points.Min(p => p.Y));
        Assert.Equal(box.Height, points.Max(p => p.Y));
        return points;
    }
}

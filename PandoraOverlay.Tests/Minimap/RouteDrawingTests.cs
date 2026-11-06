using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit;
using Path = System.Windows.Shapes.Path;

namespace PandoraOverlay.Tests;

/// <summary>
/// What the maps draw of a route: the owner's decisions 20 to 24 (PLAN.md). The
/// pixels are the golden renders' (outside the repo), the fainter stroke over
/// real terrain the owner's look in game. In the theme-on-screen collection: a
/// test builds a MapCanvas.
/// </summary>
[Collection(TheThemeOnScreen.Name)]
public sealed class RouteDrawingTests
{
    // World cm = map units, the map 1000 across, y flipped: (250, 750) is a quarter in from the top-left.
    private static readonly MapCalibration Cal = new(0, 0, 1, 1, 1000);

    private readonly List<Waypoint> _waypoints = new();

    private Waypoint Spot(string name, double x, double y, int colour)
    {
        var w = new Waypoint { Name = name, X = x, Y = y, Colour = colour };
        _waypoints.Add(w);
        return w;
    }

    private Waypoint? Find(Guid id) => _waypoints.FirstOrDefault(w => w.Id == id);

    private static Route RouteOf(params Waypoint[] stops)
    {
        var route = new Route();
        foreach (var stop in stops) route.Add(stop.Id);
        return route;
    }

    [Fact]
    public void EachLegRunsBetweenStopsInItsTargetsColourFromTheCurrentStopOn()
    {
        var (a, b, c, d) = (Spot("A", 100, 900, 1), Spot("B", 200, 800, 2), Spot("C", 300, 700, 3), Spot("D", 400, 600, 4));
        var route = RouteOf(a, b, c, d);
        route.Skip(); // B is current: A is behind you, and B's own line is the tracking line

        var legs = RouteDrawing.Legs(route, Find, Cal, 1000);

        Assert.Equal(new[]
        {
            new RouteLeg(new Point(200, 200), new Point(300, 300), 3),
            new RouteLeg(new Point(300, 300), new Point(400, 400), 4),
        }, legs.Select(Rounded));
    }

    /// <summary>The calibration's flip leaves a few parts in 10^14 on a coordinate.</summary>
    private static RouteLeg Rounded(RouteLeg leg) =>
        new(new Point(Math.Round(leg.From.X, 6), Math.Round(leg.From.Y, 6)), new Point(Math.Round(leg.To.X, 6), Math.Round(leg.To.Y, 6)), leg.Colour);

    [Fact]
    public void LegsAreInMapPixelsSoTheyFollowTheMapsRenderedSize()
    {
        var (a, b) = (Spot("A", 100, 900, 1), Spot("B", 200, 800, 2));

        var leg = Rounded(Assert.Single(RouteDrawing.Legs(RouteOf(a, b), Find, Cal, 2000)));

        Assert.Equal(new Point(200, 200), leg.From);
        Assert.Equal(new Point(400, 400), leg.To);
    }

    [Fact]
    public void NoRouteOrOneStopHasNoLegs()
    {
        Assert.Empty(RouteDrawing.Legs(new Route(), Find, Cal, 1000));
        Assert.Empty(RouteDrawing.Legs(RouteOf(Spot("A", 1, 1, 1)), Find, Cal, 1000));
    }

    [Fact]
    public void AFinishedRouteHasNoLegs()
    {
        var route = RouteOf(Spot("A", 100, 900, 1), Spot("B", 200, 800, 2));
        route.Skip();
        route.Skip();

        Assert.Empty(RouteDrawing.Legs(route, Find, Cal, 1000));
    }

    [Fact]
    public void AWaypointTwiceInARowHasNothingToDrawBetween()
    {
        var (a, b) = (Spot("A", 100, 900, 1), Spot("B", 200, 800, 2));

        Assert.Single(RouteDrawing.Legs(RouteOf(a, a, b), Find, Cal, 1000));
    }

    [Fact]
    public void AStopWithNoWaypointIsPassedOver()
    {
        var (a, b) = (Spot("A", 100, 900, 1), Spot("B", 200, 800, 2));
        var route = new Route();
        route.Add(a.Id);
        route.Add(Guid.NewGuid());
        route.Add(b.Id);

        Assert.Single(RouteDrawing.Legs(route, Find, Cal, 1000));
    }

    [Fact]
    public void UpcomingWaypointsAreEachOnceAndNotThosePassed()
    {
        var (a, b, c) = (Spot("A", 1, 1, 1), Spot("B", 2, 2, 2), Spot("C", 3, 3, 3));
        var route = RouteOf(a, b, c, b);
        route.Skip();

        Assert.Equal(new[] { b, c }, RouteDrawing.UpcomingWaypoints(route, Find));
    }

    [Fact]
    public void NumbersCountFromTheFirstStopAndOnlyUpcomingStopsHaveThem()
    {
        var (a, b, c) = (Spot("A", 1, 1, 1), Spot("B", 2, 2, 2), Spot("C", 3, 3, 3));
        var route = RouteOf(a, b, c);
        route.Skip();

        var numbers = RouteDrawing.Numbers(route);

        Assert.False(numbers.ContainsKey(a.Id));
        Assert.Equal("2", numbers[b.Id]);
        Assert.Equal("3", numbers[c.Id]);
    }

    [Fact]
    public void AWaypointYouPassTwiceShowsBothNumbers()
    {
        var (a, b) = (Spot("A", 1, 1, 1), Spot("B", 2, 2, 2));

        Assert.Equal("1, 3", RouteDrawing.Numbers(RouteOf(a, b, a))[a.Id]);
    }

    [Fact]
    public void ARepeatAlreadyPassedDropsItsNumber()
    {
        var (a, b) = (Spot("A", 1, 1, 1), Spot("B", 2, 2, 2));
        var route = RouteOf(a, b, a);
        route.Skip();

        Assert.Equal("3", RouteDrawing.Numbers(route)[a.Id]);
    }

    [Theory]
    [InlineData("3", "Delta", "3 · Delta")]
    [InlineData("3, 7", "Delta", "3, 7 · Delta")]
    [InlineData("3", null, "3")]
    [InlineData(null, "Delta", "Delta")]
    [InlineData(null, null, "")]
    public void ALabelIsTheNumberThenTheName(string? number, string? name, string expected)
    {
        Assert.Equal(expected, RouteDrawing.Label(number, name));
    }

    // ---- The canvas ----------------------------------------------------------

    private static Canvas LegsOf(MapCanvas map) => (Canvas)map.FindName("RouteLegsLayer");

    [Fact]
    public void NoRouteDrawsNothingAtAll() => TheThemeOnScreen.OnSta(() =>
    {
        Theme.EnsureLoaded();
        var map = new MapCanvas();

        map.ShowRouteLegs(Array.Empty<RouteLeg>());

        Assert.Empty(LegsOf(map).Children);
    });

    [Fact]
    public void LegsAreDashedAndFainterInTheirStopsColourOnePathPerColour() => TheThemeOnScreen.OnSta(() =>
    {
        Theme.EnsureLoaded();
        var map = new MapCanvas();

        map.ShowRouteLegs(new[]
        {
            new RouteLeg(new Point(0, 0), new Point(10, 0), 1),
            new RouteLeg(new Point(10, 0), new Point(20, 0), 2),
            new RouteLeg(new Point(20, 0), new Point(30, 0), 1),
        });

        var paths = LegsOf(map).Children.OfType<Path>().ToList();
        Assert.Equal(2, paths.Count);
        Assert.Same(MapBrushes.Of(1), paths[0].Stroke); // data: the palette's own brush, the same in every theme
        Assert.Same(MapBrushes.Of(2), paths[1].Stroke);
        Assert.All(paths, p =>
        {
            Assert.NotEmpty(p.StrokeDashArray);
            Assert.True(p.Opacity < 1);
            Assert.False(p.IsHitTestVisible);
        });
    });

    [Fact]
    public void ALegSitsUnderTheTrackingLinesAndTheMarkers() => TheThemeOnScreen.OnSta(() =>
    {
        Theme.EnsureLoaded();
        var map = new MapCanvas();

        // In the map's own layer, which is drawn before the canvas holding the lines and the markers.
        var layer = (Canvas)map.FindName("MapLayer");
        Assert.Contains(LegsOf(map), layer.Children.Cast<UIElement>());
        Assert.DoesNotContain(map.FindName("WaypointLine"), layer.Children.Cast<object>());
    });

    [Fact]
    public void ANewSetOfLegsReplacesTheLast() => TheThemeOnScreen.OnSta(() =>
    {
        Theme.EnsureLoaded();
        var map = new MapCanvas();
        map.ShowRouteLegs(new[] { new RouteLeg(new Point(0, 0), new Point(10, 0), 1) });

        map.ShowRouteLegs(Array.Empty<RouteLeg>());

        Assert.Empty(LegsOf(map).Children);
    });
}

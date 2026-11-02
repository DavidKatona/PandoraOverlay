using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Xunit;
using Path = System.Windows.Shapes.Path;

namespace PandoraOverlay.Tests;

/// <summary>
/// The tracking lines (the owner's decisions, Nov 2 2026): a dashed line
/// from your arrow to the tracked waypoint and one to the tracked friend, in
/// the brush the target's ring wears, shown only while the target is
/// tracked and drawn and your arrow is. Built on a bare MapCanvas, placed
/// without a glide, as the maps do at a snap; the pixels themselves are the
/// golden renders' (outside the repo), the glides the owner's look in game.
/// In the theme-on-screen collection: a test switches the theme.
/// </summary>
[Collection(TheThemeOnScreen.Name)]
public class TrackingLineTests
{
    private static readonly Size Panel = new(284, 284);

    // World cm = map units, the map 1000 across: (250, 750) is a quarter in from the top-left.
    private static readonly MapCalibration Cal = new(0, 0, 1, 1, 1000);

    private static Waypoint At(double x, double y, int colour) => new() { Name = "Spot", X = x, Y = y, Colour = colour };

    private static FriendState Friend(string id, double x, double y) =>
        new(id, "Friend", "Deinosuchus", "male", 1, 1, 1, 1, 1, 0, false, false, false, x, y, true, false);

    private static Line WaypointLine(MapCanvas map) => (Line)map.FindName("WaypointLine");

    private static Line FriendLine(MapCanvas map) => (Line)map.FindName("FriendLine");

    /// <summary>The panel point a marker was moved to: its translate (the tracked waypoint's diamond, or a friend's ring).</summary>
    private static Point Placed(Shape marker) =>
        marker.RenderTransform is TranslateTransform t ? new Point(t.X, t.Y) : throw new InvalidOperationException("not a placed marker");

    private static Path TrackedDiamond(MapCanvas map) =>
        ((Canvas)map.FindName("MarkerLayer")).Children.OfType<Path>().Single();

    private static Ellipse FriendRing(MapCanvas map) =>
        ((Canvas)map.FindName("FriendLayer")).Children.OfType<Ellipse>().Single();

    private static void AssertEnds(Line line, Point from, Point to)
    {
        Assert.Equal(from, new Point(line.X1, line.Y1));
        Assert.Equal(to, new Point(line.X2, line.Y2));
    }

    private static MapCanvas NewMap()
    {
        Theme.EnsureLoaded();
        return new MapCanvas { ArrowVisible = true };
    }

    [Fact]
    public void ALineRunsFromYourArrowToTheTrackedWaypointInItsColour() => TheThemeOnScreen.OnSta(() =>
    {
        var map = NewMap();
        var tracked = At(250, 750, 4);
        var view = MapViewport.Whole(Panel);
        map.ShowWaypoints(new[] { At(600, 400, 1), tracked, At(800, 200, 2) }, tracked.Id);
        map.PlaceWaypoints(Cal, view, edgeIndicator: false, glide: null);
        var you = new Point(150, 120);
        map.MoveArrow(you, glide: null);

        var line = WaypointLine(map);
        Assert.Equal(Visibility.Visible, line.Visibility);
        AssertEnds(line, you, Placed(TrackedDiamond(map)));
        Assert.Equal(view.ToPanel(0.25, 0.25), new Point(line.X2, line.Y2));
        Assert.Same(MapBrushes.Of(tracked.Colour), line.Stroke); // the ring's brush: data, the waypoint's palette colour
        Assert.NotEmpty(line.StrokeDashArray);                  // dashed
        Assert.Equal(Visibility.Collapsed, FriendLine(map).Visibility); // no friend tracked
    });

    [Fact]
    public void NoTrackedWaypointNoLineAndAnUntrackTakesItAway() => TheThemeOnScreen.OnSta(() =>
    {
        var map = NewMap();
        var spot = At(250, 750, 4);
        map.ShowWaypoints(new[] { spot }, trackedId: null);
        map.PlaceWaypoints(Cal, MapViewport.Whole(Panel), edgeIndicator: false, glide: null);
        Assert.Equal(Visibility.Collapsed, WaypointLine(map).Visibility);

        map.ShowWaypoints(new[] { spot }, spot.Id); // tracked
        map.PlaceWaypoints(Cal, MapViewport.Whole(Panel), edgeIndicator: false, glide: null);
        Assert.Equal(Visibility.Visible, WaypointLine(map).Visibility);

        map.ShowWaypoints(new[] { spot }, trackedId: null); // untracked: the same call without the id
        Assert.Equal(Visibility.Collapsed, WaypointLine(map).Visibility);

        map.ShowWaypoints(Array.Empty<Waypoint>(), Guid.NewGuid()); // a tracked id that is not drawn
        Assert.Equal(Visibility.Collapsed, WaypointLine(map).Visibility);
    });

    [Fact]
    public void WithoutYourArrowThereIsNoLine() => TheThemeOnScreen.OnSta(() =>
    {
        var map = NewMap();
        var spot = At(250, 750, 4);
        map.ShowWaypoints(new[] { spot }, spot.Id);
        map.ShowFriends(new[] { Friend("1", 400, 600) }, "1", _ => MapBrushes.Of(7));
        map.PlaceWaypoints(Cal, MapViewport.Whole(Panel), edgeIndicator: false, glide: null);
        map.PlaceFriends(Cal, MapViewport.Whole(Panel), edgeIndicator: false, glide: null, yawOffset: 0);

        map.ArrowVisible = false; // not in game
        Assert.Equal(Visibility.Collapsed, WaypointLine(map).Visibility);
        Assert.Equal(Visibility.Collapsed, FriendLine(map).Visibility);

        map.ArrowVisible = true; // back in game
        Assert.Equal(Visibility.Visible, WaypointLine(map).Visibility);
        Assert.Equal(Visibility.Visible, FriendLine(map).Visibility);
    });

    [Fact]
    public void ATrackedFriendGetsTheSecondLineInTheirColour() => TheThemeOnScreen.OnSta(() =>
    {
        var map = NewMap();
        var spot = At(250, 750, 4);
        var friendBrush = MapBrushes.Of(9);
        var view = MapViewport.Whole(Panel);
        map.ShowWaypoints(new[] { spot }, spot.Id);
        map.ShowFriends(new[] { Friend("1", 400, 600), Friend("2", 900, 100) }, "2", f => f.SteamId == "2" ? friendBrush : MapBrushes.Of(0));
        map.PlaceWaypoints(Cal, view, edgeIndicator: false, glide: null);
        map.PlaceFriends(Cal, view, edgeIndicator: false, glide: null, yawOffset: 0);
        var you = new Point(150, 120);
        map.MoveArrow(you, glide: null);

        var line = FriendLine(map);
        Assert.Equal(Visibility.Visible, line.Visibility);
        AssertEnds(line, you, Placed(FriendRing(map)));
        Assert.Equal(view.ToPanel(0.9, 0.9), new Point(line.X2, line.Y2));
        Assert.Same(friendBrush, line.Stroke);
        Assert.NotEmpty(line.StrokeDashArray);

        // Both tracked: two lines, each to its own target in its own colour.
        Assert.Equal(Visibility.Visible, WaypointLine(map).Visibility);
        Assert.Same(MapBrushes.Of(spot.Colour), WaypointLine(map).Stroke);
        AssertEnds(WaypointLine(map), you, Placed(TrackedDiamond(map)));

        map.ShowFriends(new[] { Friend("1", 400, 600), Friend("2", 900, 100) }, trackedId: null, _ => friendBrush); // untracked
        Assert.Equal(Visibility.Collapsed, FriendLine(map).Visibility);
        Assert.Equal(Visibility.Visible, WaypointLine(map).Visibility);
    });

    [Fact]
    public void InTheCenteredViewAFarTargetsLineEndsAtItsClampedMarker() => TheThemeOnScreen.OnSta(() =>
    {
        var map = NewMap();
        var far = At(950, 500, 4); // far right of a view centred on the middle at 5×
        var view = MapViewport.Centered(Panel, 5, 0.5, 0.5);
        map.ShowWaypoints(new[] { far }, far.Id);
        map.ShowFriends(new[] { Friend("1", 50, 500) }, "1", _ => MapBrushes.Of(9)); // and a friend far left
        map.PlaceWaypoints(Cal, view, edgeIndicator: true, glide: null);
        map.PlaceFriends(Cal, view, edgeIndicator: true, glide: null, yawOffset: 0);
        map.MoveArrow(view.Center, glide: null);

        var waypointEnd = new Point(WaypointLine(map).X2, WaypointLine(map).Y2);
        Assert.False(view.InPanel(view.ToPanel(0.95, 0.5)));        // the waypoint itself is off the panel,
        Assert.True(view.InPanel(waypointEnd));                     // its line a stub inside it,
        Assert.Equal(Placed(TrackedDiamond(map)), waypointEnd);     // ending at the edge indicator
        AssertEnds(FriendLine(map), view.Center, Placed(FriendRing(map)));
        Assert.True(view.InPanel(Placed(FriendRing(map))));
        Assert.Equal(Visibility.Visible, WaypointLine(map).Visibility);
        Assert.Equal(Visibility.Visible, FriendLine(map).Visibility);
    });

    [Fact]
    public void AThemeSwitchLeavesTheLinesColoursAlone() => TheThemeOnScreen.OnSta(() =>
    {
        var map = NewMap();
        var spot = At(250, 750, 4);
        var friendBrush = MapBrushes.Of(9);
        map.ShowWaypoints(new[] { spot }, spot.Id);
        map.ShowFriends(new[] { Friend("1", 400, 600) }, "1", _ => friendBrush);
        map.PlaceWaypoints(Cal, MapViewport.Whole(Panel), edgeIndicator: false, glide: null);
        map.PlaceFriends(Cal, MapViewport.Whole(Panel), edgeIndicator: false, glide: null, yawOffset: 0);
        try
        {
            foreach (var theme in Theme.All)
            {
                Theme.Apply(theme.Id);
                map.ApplyTheme();
                Assert.Same(MapBrushes.Of(spot.Colour), WaypointLine(map).Stroke);
                Assert.Same(friendBrush, FriendLine(map).Stroke);
                Assert.Equal(Visibility.Visible, WaypointLine(map).Visibility);
                Assert.Equal(Visibility.Visible, FriendLine(map).Visibility);
            }
        }
        finally
        {
            Theme.Apply(Theme.DefaultId);
        }
    });

    /// <summary>The map's mode (UseMapSpace): the lines ride the map's translate with the markers, so a pan moves them with everything else.</summary>
    [Fact]
    public void InMapSpaceTheLinesPanWithTheMarkers() => TheThemeOnScreen.OnSta(() =>
    {
        var map = NewMap();
        map.UseMapSpace(30);
        var lines = (Canvas)map.FindName("TrackLines");
        Assert.Same(((Canvas)map.FindName("StillMarks")).RenderTransform, lines.RenderTransform);
    });
}

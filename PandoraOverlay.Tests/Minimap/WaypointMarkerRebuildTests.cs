using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// A rebuild of the waypoint set keeps the markers where they stand. The
/// "nearest" policy rebuilds the set as the player moves (ShowWaypoints
/// makes new shapes every time), and until Nov 2 2026 every marker then
/// restarted at the panel's corner and, with the next poll's glide, slid in
/// from there (found by the tracking-line session, fixed with the merge).
/// Now a waypoint drawn before carries its marker's place over, as a friend
/// does, and a marker never drawn before snaps into place on its first
/// placement, glide or not. Built on a bare MapCanvas; in the theme-on-screen
/// collection like every canvas test.
/// </summary>
[Collection(TheThemeOnScreen.Name)]
public class WaypointMarkerRebuildTests
{
    private static readonly Size Panel = new(284, 284);

    // World cm = map units, the map 1000 across.
    private static readonly MapCalibration Cal = new(0, 0, 1, 1, 1000);

    private static Waypoint At(double x, double y, int colour) => new() { Name = "Spot", X = x, Y = y, Colour = colour };

    /// <summary>A waypoint's dot, found by its palette brush (a ring's fill is transparent, a diamond is a Path).</summary>
    private static Ellipse DotOf(MapCanvas map, Waypoint w) =>
        ((Canvas)map.FindName("MarkerLayer")).Children.OfType<Ellipse>().Single(e => ReferenceEquals(e.Fill, MapBrushes.Of(w.Colour)));

    private static TranslateTransform TranslateOf(Shape marker) =>
        marker.RenderTransform as TranslateTransform ?? throw new InvalidOperationException("not a placed marker");

    private static MapCanvas NewMap()
    {
        Theme.EnsureLoaded();
        return new MapCanvas { ArrowVisible = true };
    }

    [Fact]
    public void ARebuiltSetKeepsAPlacedMarkerWhereItStands() => TheThemeOnScreen.OnSta(() =>
    {
        var map = NewMap();
        var kept = At(250, 750, 1);
        var gone = At(600, 400, 2);
        var view = MapViewport.Whole(Panel);
        map.ShowWaypoints(new[] { kept, gone }, null);
        map.PlaceWaypoints(Cal, view, edgeIndicator: false, glide: null);
        var before = TranslateOf(DotOf(map, kept));
        var stood = new Point(before.X, before.Y);
        Assert.Equal(view.ToPanel(0.25, 0.25), stood);

        // The set changes under the player: one waypoint leaves, a new one arrives.
        var fresh = At(800, 200, 3);
        map.ShowWaypoints(new[] { kept, fresh }, null);

        var after = TranslateOf(DotOf(map, kept));
        Assert.NotSame(before, after);                       // new shapes, as every rebuild makes
        Assert.Equal(stood, new Point(after.X, after.Y));    // starting where the old marker stood, not at the corner
        Assert.Equal(new Point(0, 0), new Point(TranslateOf(DotOf(map, fresh)).X, TranslateOf(DotOf(map, fresh)).Y)); // not placed yet
    });

    [Fact]
    public void OnAGlideAKeptMarkerGlidesAndANewOneSnapsIntoPlace() => TheThemeOnScreen.OnSta(() =>
    {
        var map = NewMap();
        var kept = At(250, 750, 1);
        var view = MapViewport.Whole(Panel);
        map.ShowWaypoints(new[] { kept }, null);
        map.PlaceWaypoints(Cal, view, edgeIndicator: false, glide: null);

        var fresh = At(800, 200, 3);
        map.ShowWaypoints(new[] { kept, fresh }, null);
        map.PlaceWaypoints(Cal, view, edgeIndicator: false, glide: TimeSpan.FromMilliseconds(300));

        Assert.True(TranslateOf(DotOf(map, kept)).HasAnimatedProperties);   // drawn before: it glides with the map
        var freshTranslate = TranslateOf(DotOf(map, fresh));
        Assert.False(freshTranslate.HasAnimatedProperties);                  // never drawn before: it snaps
        var f = Cal.ToFraction(fresh.X, fresh.Y); // y flipped, as the live map's
        Assert.Equal(view.ToPanel(f.Fx, f.Fy), new Point(freshTranslate.X, freshTranslate.Y));

        // From then on it glides like the rest.
        map.PlaceWaypoints(Cal, view, edgeIndicator: false, glide: TimeSpan.FromMilliseconds(300));
        Assert.True(TranslateOf(DotOf(map, fresh)).HasAnimatedProperties);
    });
}

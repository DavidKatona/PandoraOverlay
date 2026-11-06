using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// When the route moves on (PLAN.md, Exact rules). World cm on a straight road
/// along X, the stops a kilometre or more apart unless a test says otherwise,
/// and a poll every 3 s. Nothing here counts on any particular map.
/// </summary>
public sealed class RouteTrackerTests
{
    private const double Km = 100_000;

    private readonly Dictionary<Guid, (double X, double Y)> _places = new();
    private readonly RouteTracker _tracker = new();
    private DateTime _now = new(2026, 11, 6, 20, 0, 0, DateTimeKind.Utc);

    private Guid Place(double x, double y = 0)
    {
        var id = Guid.NewGuid();
        _places[id] = (x, y);
        return id;
    }

    private (double X, double Y)? Where(Guid id) => _places.TryGetValue(id, out var at) ? at : null;

    private static Route RouteOf(params Guid[] stops)
    {
        var route = new Route();
        foreach (var stop in stops) route.Add(stop);
        return route;
    }

    /// <summary>One poll, 3 s after the last; the route runs while its current stop is tracked, as these tests assume unless they say.</summary>
    private IReadOnlyList<RouteArrival> At(Route route, double x, double y = 0, bool running = true, double seconds = 3)
    {
        _now = _now.AddSeconds(seconds);
        return _tracker.Step(route, Where, x, y, running, _now);
    }

    [Fact]
    public void ReachingTheCurrentStopPassesItAndTheNextTakesOver()
    {
        var (a, b) = (Place(0), Place(5 * Km));
        var route = RouteOf(a, b);

        var arrivals = At(route, 0);

        var arrival = Assert.Single(arrivals);
        Assert.Equal(new RouteArrival(1, 2, a, false), arrival);
        Assert.Equal(b, route.CurrentStop);
    }

    [Fact]
    public void FarFromEveryStopNothingHappens()
    {
        var route = RouteOf(Place(Km), Place(5 * Km));

        Assert.Empty(At(route, 0));
        Assert.Equal(0, route.Current);
    }

    [Theory]
    [InlineData(RouteTracker.ReachCm, true)]
    [InlineData(RouteTracker.ReachCm + 1, false)]
    public void ReachedMeansWithinFiftyMetres(double away, bool reached)
    {
        var route = RouteOf(Place(0), Place(5 * Km));

        Assert.Equal(reached, At(route, away).Count == 1);
    }

    [Fact]
    public void HeightPlaysNoPartOnlyTheMapPosition()
    {
        // The position is X and Y; a stop diagonally inside the circle counts.
        var route = RouteOf(Place(3000, 3000), Place(5 * Km));

        Assert.Single(At(route, 0, 0));
    }

    [Fact]
    public void RunningThroughACircleBetweenTwoPollsCounts()
    {
        var route = RouteOf(Place(Km), Place(5 * Km));
        Assert.Empty(At(route, Km - 7500));   // 75 m short of it

        var arrivals = At(route, Km + 7500);  // 75 m past it: 50 m/s, both polls outside the circle

        Assert.Single(arrivals);
        Assert.Equal(1, route.Current);
    }

    [Fact]
    public void APathThatMissesTheCircleDoesNotCount()
    {
        var route = RouteOf(Place(Km, 6000), Place(5 * Km));
        At(route, Km - 7500);

        Assert.Empty(At(route, Km + 7500)); // passes 60 m to the side
    }

    [Fact]
    public void AJumpNeverPassesTheStopsAlongItsLine()
    {
        var route = RouteOf(Place(Km), Place(5 * Km));
        At(route, 0);

        Assert.Empty(At(route, 2 * Km)); // 2 km in 3 s: a respawn, not travel
        Assert.Equal(0, route.Current);
    }

    [Fact]
    public void AJumpThatLandsInTheCircleCounts()
    {
        var route = RouteOf(Place(Km), Place(5 * Km));
        At(route, 0);

        Assert.Single(At(route, Km)); // you are there
    }

    [Fact]
    public void AfterLeavingTheGameThereIsNoPathFromTheLastPosition()
    {
        var route = RouteOf(Place(Km), Place(5 * Km));
        At(route, Km - 7500);

        _tracker.Forget();

        Assert.Empty(At(route, Km + 7500));
    }

    [Fact]
    public void CloseStopsCanPassTogether()
    {
        var (a, b, c) = (Place(0), Place(1000), Place(5 * Km));
        var route = RouteOf(a, b, c);

        var arrivals = At(route, 0);

        Assert.Equal(new[] { a, b }, arrivals.Select(x => x.Waypoint));
        Assert.Equal(c, route.CurrentStop);
    }

    [Fact]
    public void ManyStopsPassingAreOneChange()
    {
        var route = RouteOf(Place(0), Place(1000), Place(5 * Km));
        var changes = 0;
        route.Changed += () => changes++;

        At(route, 0);

        Assert.Equal(1, changes);
    }

    [Fact]
    public void TheLastStopSaysSoAndTheRouteIsFinished()
    {
        var a = Place(0);
        var route = RouteOf(a);

        var arrival = Assert.Single(At(route, 0));

        Assert.True(arrival.Last);
        Assert.True(route.IsFinished);
        Assert.Empty(At(route, 0)); // and nothing more happens
    }

    [Fact]
    public void AnEmptyRouteDoesNothing()
    {
        Assert.Empty(At(new Route(), 0));
    }

    // ---- The next-in-line ----------------------------------------------------

    [Fact]
    public void ReachingTheNextInLinePassesTheCurrentStopWithoutACue()
    {
        var (a, b, c) = (Place(9 * Km), Place(Km), Place(20 * Km));
        var route = RouteOf(a, b, c);
        At(route, 0); // seen outside the next-in-line

        var arrival = Assert.Single(At(route, Km));

        Assert.Equal(new RouteArrival(2, 3, b, false), arrival); // only the stop reached is announced
        Assert.Equal(c, route.CurrentStop);
    }

    [Fact]
    public void ReachingTheStopAfterTheNextDoesNothing()
    {
        var route = RouteOf(Place(9 * Km), Place(8 * Km), Place(Km), Place(20 * Km));
        At(route, 0);

        Assert.Empty(At(route, Km));
        Assert.Equal(0, route.Current);
    }

    [Fact]
    public void RunningThroughTheNextInLineBetweenTwoPollsCounts()
    {
        var route = RouteOf(Place(9 * Km), Place(Km), Place(20 * Km));
        At(route, Km - 7500);

        Assert.Single(At(route, Km + 7500));
        Assert.Equal(2, route.Current);
    }

    [Fact]
    public void GoingOutAndBackDoesNotFinishTheRouteWhereItStarted()
    {
        // Water, mud, water: reaching the first water makes mud current and the second
        // water next-in-line, and you are standing in it. Mud must not be skipped.
        var (water, mud) = (Place(0), Place(5 * Km));
        var route = RouteOf(water, mud, water);

        Assert.Single(At(route, 0));
        Assert.Equal(mud, route.CurrentStop);

        Assert.Empty(At(route, 0));
        Assert.Empty(At(route, 1000));
        Assert.Equal(mud, route.CurrentStop);

        Assert.Single(At(route, 5 * Km, seconds: 300)); // walking to the mud
        Assert.Equal(water, route.CurrentStop);
        Assert.Single(At(route, 0, seconds: 300)); // and back
        Assert.True(route.IsFinished);
    }

    [Fact]
    public void AddingAStopWhereYouStandDoesNotSkipTheCurrentOne()
    {
        var (far, here) = (Place(9 * Km), Place(0));
        var route = RouteOf(far);
        At(route, 0);

        route.Add(here);

        Assert.Empty(At(route, 0));
        Assert.Equal(far, route.CurrentStop);
    }

    [Fact]
    public void TheNextInLineCountsOnceYouHaveLeftAndComeBack()
    {
        var (far, here, after) = (Place(9 * Km), Place(0), Place(20 * Km));
        var route = RouteOf(far, here, after);

        Assert.Empty(At(route, 0));          // standing in it
        Assert.Empty(At(route, 10_000));     // walked out: seen outside
        Assert.Single(At(route, 0));         // walked back in
        Assert.Equal(after, route.CurrentStop);
    }

    [Fact]
    public void AStopThatBecomesNextInLineStartsAgain()
    {
        var (a, b, c) = (Place(9 * Km), Place(Km), Place(20 * Km));
        var route = RouteOf(a, b);
        At(route, 5 * Km); // b seen outside

        route.AddNext(c);  // c is next-in-line now, not b, and has not been seen outside

        Assert.Empty(At(route, 20 * Km, seconds: 3000));
        Assert.Equal(a, route.CurrentStop);
    }

    // ---- Paused --------------------------------------------------------------

    [Fact]
    public void WhilePausedNothingPasses()
    {
        var route = RouteOf(Place(0), Place(5 * Km));

        Assert.Empty(At(route, 0, running: false));

        Assert.Equal(0, route.Current);
    }

    [Fact]
    public void AResumeInsideTheCurrentStopCountsAtOnce()
    {
        var route = RouteOf(Place(0), Place(5 * Km));
        At(route, 0, running: false);

        Assert.Single(At(route, 0)); // a resume is not a hand pick
    }

    [Fact]
    public void AResumeKeepsThePathFromWhileItWasPaused()
    {
        var route = RouteOf(Place(Km), Place(5 * Km));
        At(route, Km - 7500, running: false);

        Assert.Single(At(route, Km + 7500));
    }

    [Fact]
    public void WalkingIntoTheNextInLineWhilePausedDoesNotSkipOnResume()
    {
        var (a, b, c) = (Place(9 * Km), Place(0), Place(20 * Km));
        var route = RouteOf(a, b, c);
        At(route, 5 * Km);                     // b seen outside
        At(route, 0, running: false, seconds: 3000); // walked into b while paused

        Assert.Empty(At(route, 0));
        Assert.Equal(a, route.CurrentStop);
    }

    // ---- Picked by hand ------------------------------------------------------

    [Fact]
    public void AStopPickedByHandWhileStandingInItDoesNotPassAgain()
    {
        var (a, b) = (Place(0), Place(5 * Km));
        var route = RouteOf(a, b);
        Assert.Single(At(route, 0)); // walked through it by accident
        Assert.Equal(b, route.CurrentStop);

        route.GoTo(0);
        _tracker.HandPicked();

        Assert.Empty(At(route, 0));
        Assert.Empty(At(route, 1000));
        Assert.Equal(a, route.CurrentStop);
    }

    [Fact]
    public void AStopPickedByHandCountsOnceYouHaveLeftAndComeBack()
    {
        var (a, b) = (Place(0), Place(5 * Km));
        var route = RouteOf(a, b);
        route.GoTo(0);
        _tracker.HandPicked();
        At(route, 0);

        Assert.Empty(At(route, 7000)); // the path out of the circle does not count
        Assert.Single(At(route, 0));   // the way back in does
        Assert.Equal(b, route.CurrentStop);
    }

    [Fact]
    public void AStopPickedByHandFromFarAwayCountsWhenYouGetThere()
    {
        var (a, b) = (Place(0), Place(5 * Km));
        var route = RouteOf(a, b);
        route.Skip();
        route.GoTo(0);
        _tracker.HandPicked();

        Assert.Empty(At(route, Km));
        Assert.Single(At(route, 0, seconds: 300));
    }

    [Fact]
    public void GoingBackAfterAWrongSkipDoesNotSkipItAgain()
    {
        // Heading for a, b reached first: a is skipped. Picking a again while standing at b,
        // its next-in-line, must not skip it a second time.
        var (a, b, c) = (Place(9 * Km), Place(0), Place(20 * Km));
        var route = RouteOf(a, b, c);
        At(route, 1000 * 10);
        Assert.Single(At(route, 0));
        Assert.Equal(c, route.CurrentStop);

        route.GoTo(0);
        _tracker.HandPicked();

        Assert.Empty(At(route, 0));
        Assert.Equal(a, route.CurrentStop);
    }

    [Fact]
    public void AWaypointNoLongerInTheLibraryStopsTheCheckQuietly()
    {
        var gone = Guid.NewGuid();
        var route = RouteOf(gone, Place(0));

        Assert.Empty(At(route, 0));
        Assert.Equal(0, route.Current);
    }

    [Fact]
    public void TheShortestDistanceToAPathIsToItsNearestPoint()
    {
        Assert.Equal(300, RouteTracker.DistanceToPath((50, 300), (0, 0), 100, 0)); // beside the path
        Assert.Equal(500, RouteTracker.DistanceToPath((600, 0), (0, 0), 100, 0));  // beyond its end
        Assert.Equal(500, RouteTracker.DistanceToPath((0, 500), (0, 0), 0, 0));    // no path at all
    }
}

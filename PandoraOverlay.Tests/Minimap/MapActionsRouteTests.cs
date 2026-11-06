using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The route's actions and the tracking kept in step with them: the current
/// stop is the tracked waypoint, and the route runs exactly while that is so
/// (PLAN.md, decisions 2, 11, 12, 14; defaults D1, D4, D9).
/// </summary>
public sealed class MapActionsRouteTests
{
    private const double Km = 100_000;

    private readonly OverlayConfig _config = new();
    private readonly WaypointLibrary _library = new();
    private readonly FriendBook _book = new();
    private readonly MapActions _actions;

    public MapActionsRouteTests() => _actions = new MapActions(_config, _library, _book, name => name);

    private Route Route => _actions.Route;

    private Waypoint Spot(string name, double x = 0) => _library.Add(name, x, 0, 0)!;

    /// <summary>A route of three stops a few km apart, running at its first stop.</summary>
    private (Waypoint A, Waypoint B, Waypoint C) ARoute()
    {
        var (a, b, c) = (Spot("A", 5 * Km), Spot("B", 10 * Km), Spot("C", 15 * Km));
        _actions.AddToRoute(a);
        _actions.AddToRoute(b);
        _actions.AddToRoute(c);
        return (a, b, c);
    }

    [Fact]
    public void TheFirstStopAddedIsTrackedReplacingAnythingTracked()
    {
        var (other, a) = (Spot("Other"), Spot("A"));
        _actions.Track(other.Id);

        var notice = _actions.AddToRoute(a);

        Assert.Equal(a.Id, _config.TrackedWaypointId);
        Assert.True(_actions.RouteRunning);
        Assert.Equal("A is stop 1", notice);
    }

    [Fact]
    public void AStopAddedToARunningRouteLeavesTheTrackingAlone()
    {
        var (a, _, _) = ARoute();

        Assert.Equal(a.Id, _config.TrackedWaypointId);
        Assert.Equal(3, Route.Count);
    }

    [Fact]
    public void AStopAddedToAPausedRouteDoesNotResumeIt()
    {
        var (_, _, _) = ARoute();
        var other = Spot("Other");
        _actions.Track(other.Id);

        _actions.AddToRoute(Spot("D"));

        Assert.Equal(other.Id, _config.TrackedWaypointId);
        Assert.True(_actions.RoutePaused);
    }

    [Fact]
    public void AStopAddedToAFinishedRouteCarriesOnToIt()
    {
        var a = Spot("A");
        _actions.AddToRoute(a);
        _actions.SkipStop();
        Assert.Null(_config.TrackedWaypointId);

        var b = Spot("B");
        var notice = _actions.AddToRoute(b);

        Assert.Equal(b.Id, _config.TrackedWaypointId);
        Assert.Equal("B is stop 2", notice);
    }

    [Fact]
    public void AddAsNextStopGoesStraightAfterTheCurrentOneAndSaysWhichItIs()
    {
        var (a, b, c) = ARoute();
        var d = Spot("D");

        var notice = _actions.AddAsNextStop(d);

        Assert.Equal(new[] { a.Id, d.Id, b.Id, c.Id }, Route.Stops);
        Assert.Equal(a.Id, _config.TrackedWaypointId);
        Assert.Equal("D is stop 2", notice);
    }

    [Fact]
    public void AddAsNextStopOnAnEmptyRouteStartsIt()
    {
        var a = Spot("A");

        Assert.Equal("A is stop 1", _actions.AddAsNextStop(a));
        Assert.Equal(a.Id, _config.TrackedWaypointId);
    }

    [Fact]
    public void AFullRouteTakesNoMore()
    {
        var a = Spot("A");
        for (var i = 0; i < Route.Capacity; i++) _actions.AddToRoute(a);
        var before = _library.Count;

        Assert.Equal("route full", _actions.AddToRoute(a));
        Assert.Equal("route full", _actions.AddAsNextStop(a));
        Assert.Equal("route full", _actions.AddStopHere(5, 5));
        Assert.Equal(before, _library.Count); // no waypoint left behind for a stop that didn't fit
    }

    [Fact]
    public void AddStopHereMakesANormalWaypointThatStaysInTheLibrary()
    {
        var actions = new MapActions(_config, _library, _book, name => name, (_, _) => "Somewhere");

        var notice = actions.AddStopHere(1000, 2000);

        var added = Assert.Single(_library.Items);
        Assert.Equal("Somewhere", added.Name);
        Assert.Equal(added.Id, actions.Route.CurrentStop);
        Assert.Equal("Somewhere is stop 1", notice);

        actions.ClearRoute();
        Assert.Single(_library.Items);
    }

    [Fact]
    public void AddStopHereWithAFullLibrarySaysSo()
    {
        for (var i = 0; i < WaypointLibrary.Capacity; i++) _library.Add($"w{i}", i * 100, 0, 0);

        Assert.Equal("library full", _actions.AddStopHere(5, 5));
        Assert.True(Route.IsEmpty);
    }

    // ---- New waypoints while a route runs ----------------------------------------

    [Fact]
    public void ANewWaypointDoesNotTakeTheLineFromARunningRoute()
    {
        var (a, _, _) = ARoute();
        var count = _library.Count;

        var notice = _actions.AddWaypoint(1000, 2000);

        Assert.Equal(a.Id, _config.TrackedWaypointId);
        Assert.Equal(count + 1, _library.Count);
        Assert.EndsWith(" added", notice);
    }

    [Fact]
    public void APastedSpotDoesNotTakeTheLineFromARunningRouteEither()
    {
        var (a, _, _) = ARoute();

        _actions.AddFromCode("pandora:10,20 Pasted");

        Assert.Equal(a.Id, _config.TrackedWaypointId);
    }

    [Fact]
    public void ANewWaypointIsTrackedWhileARouteIsPausedOrFinished()
    {
        ARoute();
        _actions.Track(null);

        _actions.AddWaypoint(1000, 2000);
        Assert.Equal(_library.Items[^1].Id, _config.TrackedWaypointId);

        _actions.ClearRoute();
        _actions.AddWaypoint(3000, 4000);
        Assert.Equal(_library.Items[^1].Id, _config.TrackedWaypointId);
    }

    // ---- The position ------------------------------------------------------------

    [Fact]
    public void ReachingAStopMovesTheTrackingToTheNext()
    {
        var (a, b, _) = ARoute();

        var arrival = Assert.Single(_actions.RouteAt(a.X, a.Y));

        Assert.Equal(a.Id, arrival.Waypoint);
        Assert.Equal(b.Id, _config.TrackedWaypointId);
    }

    [Fact]
    public void TheMapsHearTheTrackingMoveOn()
    {
        var (a, _, _) = ARoute();
        var heard = 0;
        _actions.WaypointTracked += () => heard++;

        _actions.RouteAt(a.X, a.Y);

        Assert.Equal(1, heard);
    }

    [Fact]
    public void ReachingTheLastStopFinishesAndUntracks()
    {
        var a = Spot("A");
        _actions.AddToRoute(a);

        var arrival = Assert.Single(_actions.RouteAt(a.X, a.Y));

        Assert.True(arrival.Last);
        Assert.True(Route.IsFinished);
        Assert.Null(_config.TrackedWaypointId);
    }

    [Fact]
    public void WhilePausedNothingPassesAndYourTrackingIsLeftAlone()
    {
        var (a, _, _) = ARoute();
        var other = Spot("Other");
        _actions.Track(other.Id);

        Assert.Empty(_actions.RouteAt(a.X, a.Y));

        Assert.Equal(0, Route.Current);
        Assert.Equal(other.Id, _config.TrackedWaypointId);
    }

    [Fact]
    public void TheNoticeNamesTheStopReachedOrTheEnd()
    {
        var (a, b) = (Spot("A", 5 * Km), Spot("B", 10 * Km));
        _actions.AddToRoute(a);
        _actions.AddToRoute(b);
        string? said = null;
        _actions.Notice += text => said = text;

        _actions.SayReached(_actions.RouteAt(a.X, a.Y));
        Assert.Equal("A reached", said);

        _actions.SayReached(_actions.RouteAt(b.X, b.Y));
        Assert.Equal("Route finished", said);
    }

    [Fact]
    public void NothingReachedSaysNothing()
    {
        var said = false;
        _actions.Notice += _ => said = true;

        _actions.SayReached(Array.Empty<RouteArrival>());

        Assert.False(said);
    }

    // ---- Pausing and resuming ----------------------------------------------------

    [Fact]
    public void TrackingAnythingElseOrNothingPausesTheRoute()
    {
        ARoute();
        Assert.True(_actions.RouteRunning);

        _actions.Track(Spot("Other").Id);
        Assert.True(_actions.RoutePaused);

        _actions.Track(null);
        Assert.True(_actions.RoutePaused);
    }

    [Fact]
    public void ALaterStopTrackedByHandPausesTheRouteLikeAnyOtherWaypoint()
    {
        var (_, b, _) = ARoute();

        _actions.Track(b.Id);

        Assert.True(_actions.RoutePaused);
        Assert.Equal(0, Route.Current);
    }

    [Fact]
    public void ResumeOrTrackingTheStopAgainBringsItBack()
    {
        var (a, _, _) = ARoute();

        _actions.Track(null);
        _actions.ResumeRoute();
        Assert.Equal(a.Id, _config.TrackedWaypointId);
        Assert.True(_actions.RouteRunning);

        _actions.Track(null);
        _actions.Track(a.Id);
        Assert.True(_actions.RouteRunning);
    }

    [Fact]
    public void AResumeInsideTheCurrentStopCountsAtOnce()
    {
        var (a, b, _) = ARoute();
        _actions.Track(null);
        _actions.RouteAt(a.X, a.Y);

        _actions.ResumeRoute();
        Assert.Single(_actions.RouteAt(a.X, a.Y));

        Assert.Equal(b.Id, _config.TrackedWaypointId);
    }

    // ---- Skip, go to, restart, clear ---------------------------------------------

    [Fact]
    public void SkipMovesARunningRouteOnAndSaysWhatWasSkipped()
    {
        var (_, b, _) = ARoute();

        Assert.Equal("A skipped", _actions.SkipStop());

        Assert.Equal(b.Id, _config.TrackedWaypointId);
    }

    [Fact]
    public void SkipLeavesAPausedRoutePaused()
    {
        var (_, b, _) = ARoute();
        var other = Spot("Other");
        _actions.Track(other.Id);

        _actions.SkipStop();

        Assert.Equal(b.Id, Route.CurrentStop);
        Assert.Equal(other.Id, _config.TrackedWaypointId);
    }

    [Fact]
    public void SkipOnNoRouteDoesNothing()
    {
        Assert.Null(_actions.SkipStop());
    }

    [Fact]
    public void SkippingTheLastStopFinishesAndUntracks()
    {
        _actions.AddToRoute(Spot("A"));

        _actions.SkipStop();

        Assert.True(Route.IsFinished);
        Assert.Null(_config.TrackedWaypointId);
    }

    [Fact]
    public void GoingToAStopTracksItEvenFromAPausedRoute()
    {
        var (_, _, c) = ARoute();
        _actions.Track(null);

        _actions.GoToStop(2);

        Assert.Equal(c.Id, _config.TrackedWaypointId);
        Assert.Equal(2, Route.Current);
    }

    [Fact]
    public void GoingToAPassedStopUndoesAnAccidentalPassWithoutPassingItAgain()
    {
        var (a, b, _) = ARoute();
        Assert.Single(_actions.RouteAt(a.X, a.Y)); // walked through it
        Assert.Equal(b.Id, Route.CurrentStop);

        _actions.GoToStop(0);

        Assert.Empty(_actions.RouteAt(a.X, a.Y)); // still standing in it
        Assert.Equal(a.Id, Route.CurrentStop);
    }

    [Fact]
    public void GoingToAStopOfAFinishedRouteStartsItAgainFromThere()
    {
        var (a, b, c) = ARoute();
        _actions.GoToStop(2);
        _actions.SkipStop();
        Assert.True(Route.IsFinished);

        _actions.GoToStop(1);

        Assert.Equal(b.Id, _config.TrackedWaypointId);
    }

    [Fact]
    public void ClickingTheCurrentStopOfAPausedRouteResumesIt()
    {
        var (a, _, _) = ARoute();
        _actions.Track(null);

        _actions.GoToStop(0);

        Assert.Equal(a.Id, _config.TrackedWaypointId);
    }

    [Fact]
    public void RestartWalksItAgainFromTheFirstStopWhateverItWasDoing()
    {
        var (a, _, _) = ARoute();
        _actions.SkipStop();
        _actions.Track(null);

        _actions.RestartRoute();

        Assert.Equal(a.Id, _config.TrackedWaypointId);
        Assert.Equal(0, Route.Current);
    }

    [Fact]
    public void ClearingARunningRouteUntracksItsStop()
    {
        ARoute();

        Assert.Equal("route cleared", _actions.ClearRoute());

        Assert.True(Route.IsEmpty);
        Assert.Null(_config.TrackedWaypointId);
        Assert.Equal(3, _library.Count); // the waypoints stay
    }

    [Fact]
    public void ClearingAPausedRouteLeavesYourTrackingAlone()
    {
        ARoute();
        var other = Spot("Other");
        _actions.Track(other.Id);

        _actions.ClearRoute();

        Assert.Equal(other.Id, _config.TrackedWaypointId);
    }

    // ---- Moving and removing -----------------------------------------------------

    [Fact]
    public void AStopDraggedToTheCurrentPlaceTakesTheTrackingOfARunningRoute()
    {
        var (_, _, c) = ARoute();

        _actions.MoveStop(2, 0);

        Assert.Equal(c.Id, _config.TrackedWaypointId);
    }

    [Fact]
    public void AStopDraggedToTheCurrentPlaceOfAPausedRouteLeavesItPaused()
    {
        var (_, _, c) = ARoute();
        var other = Spot("Other");
        _actions.Track(other.Id);

        _actions.MoveStop(2, 0);

        Assert.Equal(c.Id, Route.CurrentStop);
        Assert.Equal(other.Id, _config.TrackedWaypointId);
    }

    [Fact]
    public void RemovingTheCurrentStopLetsTheNextTakeTheTracking()
    {
        var (a, b, _) = ARoute();

        Assert.Equal("A left the route", _actions.RemoveFromRoute(a));

        Assert.Equal(b.Id, _config.TrackedWaypointId);
        Assert.Contains(_library.Items, w => w.Id == a.Id); // never off the library
    }

    [Fact]
    public void RemovingTheOnlyStopFinishesNothingAndUntracks()
    {
        var a = Spot("A");
        _actions.AddToRoute(a);

        _actions.RemoveStop(0);

        Assert.True(Route.IsEmpty);
        Assert.Null(_config.TrackedWaypointId);
    }

    [Fact]
    public void DeletingTheCurrentStopsWaypointLetsTheNextTakeOver()
    {
        var (a, b, _) = ARoute();

        _actions.Remove(a);

        Assert.DoesNotContain(a.Id, Route.Stops);
        Assert.Equal(b.Id, _config.TrackedWaypointId);
    }

    [Fact]
    public void DeletingTheLastStopsWaypointFinishesTheRouteAndUntracks()
    {
        var a = Spot("A");
        _actions.AddToRoute(a);

        _actions.Remove(a);

        Assert.True(Route.IsEmpty);
        Assert.Null(_config.TrackedWaypointId);
    }

    [Fact]
    public void DeletingALaterStopsWaypointLeavesTheCurrentOneAlone()
    {
        var (a, b, c) = ARoute();

        _actions.Remove(b);

        Assert.Equal(new[] { a.Id, c.Id }, Route.Stops);
        Assert.Equal(a.Id, _config.TrackedWaypointId);
    }

    [Fact]
    public void DeletingTheCurrentStopsWaypointOfAPausedRouteDoesNotResumeIt()
    {
        var (a, b, _) = ARoute();
        var other = Spot("Other");
        _actions.Track(other.Id);

        _actions.Remove(a);

        Assert.Equal(b.Id, Route.CurrentStop);
        Assert.Equal(other.Id, _config.TrackedWaypointId);
    }

    [Fact]
    public void SettingsDeletingTheCurrentStopsWaypointLetsTheNextTakeOverToo()
    {
        var (a, b, _) = ARoute();

        // What SettingsWindow.FlushChanges does: the tracking first, then the list.
        _config.TrackedWaypointId = null;
        _library.ReplaceWith(_library.Clone().Where(w => w.Id != a.Id));

        Assert.Equal(b.Id, _config.TrackedWaypointId);
    }

    // ---- Which stop a waypoint means ---------------------------------------------

    [Fact]
    public void AWaypointMeansItsNextStopElseTheLastOneYouPassed()
    {
        var (a, b) = (Spot("A", 5 * Km), Spot("B", 10 * Km));
        _actions.AddToRoute(a);
        _actions.AddToRoute(b);
        _actions.AddToRoute(a);
        _actions.AddToRoute(Spot("C", 15 * Km));

        Assert.Equal(0, _actions.StopOf(a.Id));

        _actions.GoToStop(1);
        Assert.Equal(2, _actions.StopOf(a.Id)); // the upcoming repeat

        _actions.GoToStop(3);
        Assert.Equal(2, _actions.StopOf(a.Id)); // both passed: the last one
        Assert.Equal(1, _actions.StopOf(b.Id));

        Assert.Equal(-1, _actions.StopOf(Guid.NewGuid()));
    }

    [Fact]
    public void RemovingFromTheRouteTakesTheStopItMeans()
    {
        var (a, b) = (Spot("A", 5 * Km), Spot("B", 10 * Km));
        _actions.AddToRoute(a);
        _actions.AddToRoute(b);
        _actions.AddToRoute(a);
        _actions.GoToStop(1);

        _actions.RemoveFromRoute(a);

        Assert.Equal(new[] { a.Id, b.Id }, Route.Stops);
    }
}

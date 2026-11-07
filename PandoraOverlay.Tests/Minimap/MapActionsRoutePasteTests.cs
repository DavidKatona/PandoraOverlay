using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// A shared route, copied and pasted (PLAN.md, decisions 25 to 27, D8): stops
/// arrive like a pack import, a paste is all or nothing, and it starts at stop 1.
/// Through the text, never the real clipboard, which is the player's.
/// </summary>
public sealed class MapActionsRoutePasteTests
{
    private const double Km = 100_000;

    private readonly OverlayConfig _config = new();
    private readonly WaypointLibrary _library = new();
    private readonly MapActions _actions;

    public MapActionsRoutePasteTests() => _actions = new MapActions(_config, _library, new FriendBook(), name => name);

    private Route Route => _actions.Route;

    private Waypoint Spot(string name, double x = 0, double y = 0) => _library.Add(name, x, y, 0)!;

    private List<string> StopNames() => Route.Stops.Select(id => _library.Find(id)!.Name).ToList();

    [Fact]
    public void NewStopsArriveHiddenAndGroupedWithTheNextColours()
    {
        var paste = _actions.PasteRouteFrom("pandora:1000,0 Delta\npandora:2000,0 Forks");

        Assert.True(paste.Done);
        Assert.Equal(new[] { "Delta", "Forks" }, StopNames());
        Assert.All(_library.Items, w =>
        {
            Assert.False(w.Visible);
            Assert.Equal(RouteCode.SharedPack, w.Pack);
        });
        Assert.NotEqual(_library.Items[0].Colour, _library.Items[1].Colour);
    }

    [Fact]
    public void ThePasteStartsAtStopOneAndIsTracked()
    {
        _actions.PasteRouteFrom("pandora:1000,0 Delta\npandora:2000,0 Forks");

        Assert.Equal(0, Route.Current);
        Assert.Equal(Route.Stops[0], _config.TrackedWaypointId);
        Assert.True(_actions.RouteRunning);
    }

    [Fact]
    public void AWaypointOfTheSameNameWithinTwentyMetresIsReused()
    {
        var delta = Spot("Delta", 100_000);

        var paste = _actions.PasteRouteFrom("pandora:1010,0 Delta\npandora:2000,0 Forks"); // 10 m off

        Assert.Equal(delta.Id, Route.Stops[0]);
        Assert.Equal(2, _library.Count);
        Assert.Contains("1 new waypoint, 1 you already had", paste.Words);
        Assert.True(_library.Find(delta.Id)!.Visible); // yours stays as you had it
        Assert.Null(_library.Find(delta.Id)!.Pack);
    }

    [Fact]
    public void TheNameIsComparedWithoutCase()
    {
        var delta = Spot("Delta", 100_000);

        _actions.PasteRouteFrom("pandora:1000,0 DELTA\npandora:2000,0 Forks");

        Assert.Equal(delta.Id, Route.Stops[0]);
    }

    [Fact]
    public void TheSameNameFurtherAwayIsAnotherPlace()
    {
        var delta = Spot("Delta", 100_000);

        _actions.PasteRouteFrom("pandora:1100,0 Delta\npandora:2000,0 Forks"); // 100 m off

        Assert.NotEqual(delta.Id, Route.Stops[0]);
        Assert.Equal(3, _library.Count);
    }

    [Fact]
    public void AnotherNameOnTheSameSpotIsAnotherWaypoint()
    {
        var delta = Spot("Delta", 100_000);

        _actions.PasteRouteFrom("pandora:1000,0 Water\npandora:2000,0 Forks");

        Assert.NotEqual(delta.Id, Route.Stops[0]);
    }

    [Fact]
    public void AStopRepeatedInTheTextIsOneWaypoint()
    {
        _actions.PasteRouteFrom("pandora:1000,0 Water\npandora:2000,0 Mud\npandora:1000,0 Water");

        Assert.Equal(3, Route.Count);
        Assert.Equal(2, _library.Count);
        Assert.Equal(Route.Stops[0], Route.Stops[2]);
    }

    [Fact]
    public void ACodeWithNoNameMeansAnyWaypointOnThatSpot()
    {
        var delta = Spot("Delta", 100_000);

        _actions.PasteRouteFrom("pandora:1000,0\npandora:2000,0");

        Assert.Equal(delta.Id, Route.Stops[0]);
        Assert.Equal(2, _library.Count);
    }

    [Fact]
    public void CodesWithNoNameTakeTheAreasNamesWithoutClashing()
    {
        var actions = new MapActions(_config, _library, new FriendBook(), name => name, (_, _) => "An area");

        actions.PasteRouteFrom("pandora:1000,0\npandora:2000,0\npandora:3000,0");

        Assert.Equal(new[] { "An area", "An area 2", "An area 3" }, actions.Route.Stops.Select(id => _library.Find(id)!.Name));
    }

    [Fact]
    public void WithNoAreaNamelessCodesAreNumberedWaypoints()
    {
        _actions.PasteRouteFrom("pandora:1000,0\npandora:2000,0");

        Assert.Equal(new[] { "Waypoint 1", "Waypoint 2" }, StopNames());
    }

    [Fact]
    public void APasteReplacesTheRouteButNoWaypoint()
    {
        var old = Spot("Old", Km);
        _actions.AddToRoute(old);

        _actions.PasteRouteFrom("pandora:1000,0 Delta\npandora:2000,0 Forks");

        Assert.DoesNotContain(old.Id, Route.Stops);
        Assert.NotNull(_library.Find(old.Id));
    }

    [Fact]
    public void APasteTakesTheLineFromAnythingTracked()
    {
        var other = Spot("Other", Km);
        _actions.Track(other.Id);

        _actions.PasteRouteFrom("pandora:1000,0 Delta\npandora:2000,0 Forks");

        Assert.Equal(Route.Stops[0], _config.TrackedWaypointId);
    }

    [Fact]
    public void WithoutRoomInTheLibraryNothingChangesAtAll()
    {
        for (var i = 0; i < WaypointLibrary.Capacity - 1; i++) _library.Add($"w{i}", i * 100, 50_000, 0);
        var old = _library.Items[0];
        _actions.AddToRoute(old);
        var heard = 0;
        _library.Changed += () => heard++;

        var paste = _actions.PasteRouteFrom("pandora:1000,0 Delta\npandora:2000,0 Forks"); // needs two, room for one

        Assert.False(paste.Done);
        Assert.Equal("library full", paste.Notice);
        Assert.Contains("2 new waypoints, room for 1", paste.Words);
        Assert.Equal(old.Id, Assert.Single(Route.Stops));
        Assert.Equal(WaypointLibrary.Capacity - 1, _library.Count);
        Assert.Equal(0, heard);
        Assert.DoesNotContain(_library.Items, w => w.Name == "Delta");
    }

    [Fact]
    public void AFullLibraryCanStillTakeARouteOfWaypointsItHas()
    {
        for (var i = 0; i < WaypointLibrary.Capacity; i++) _library.Add($"w{i}", i * 10_000, 0, 0);

        var paste = _actions.PasteRouteFrom("pandora:0,0 w0\npandora:100,0 w1");

        Assert.True(paste.Done);
        Assert.Equal(2, Route.Count);
        Assert.Equal(WaypointLibrary.Capacity, _library.Count);
    }

    [Fact]
    public void MoreThanARouteHoldsPastesTheFirstOnesAndSaysSo()
    {
        var text = string.Join("\n", Enumerable.Range(0, Route.Capacity + 6).Select(i => $"pandora:{i * 100},0 Stop {i}"));

        var paste = _actions.PasteRouteFrom(text);

        Assert.True(paste.Done);
        Assert.Equal(Route.Capacity, Route.Count);
        Assert.Equal("Stop 0", StopNames()[0]);
        Assert.Equal($"first {Route.Capacity} stops pasted", paste.Notice);
        Assert.Contains($"The first {Route.Capacity} of {Route.Capacity + 6}", paste.Words);
    }

    [Theory]
    [InlineData("pandora:1000,0 Delta")] // one spot: that's Paste waypoint's
    [InlineData("nothing useful")]
    [InlineData("")]
    [InlineData(null)]
    public void TextThatIsNoRouteChangesNothing(string? text)
    {
        var a = Spot("A", Km);
        _actions.AddToRoute(a);

        var paste = _actions.PasteRouteFrom(text);

        Assert.False(paste.Done);
        Assert.Equal("no route in the clipboard", paste.Notice);
        Assert.Equal(a.Id, Assert.Single(Route.Stops));
        Assert.Single(_library.Items);
    }

    [Fact]
    public void OneChangeOfTheLibraryForThemAll()
    {
        var heard = 0;
        _library.Changed += () => heard++;

        _actions.PasteRouteFrom(string.Join("\n", Enumerable.Range(0, 20).Select(i => $"pandora:{i * 100},0 Stop {i}")));

        Assert.Equal(1, heard); // a save and a redraw per stop would be twenty of each
    }

    [Fact]
    public void ARouteSurvivesARoundTrip()
    {
        var (a, b) = (Spot("A", Km), Spot("B words", 2 * Km));
        _actions.AddToRoute(a);
        _actions.AddToRoute(b);
        _actions.AddToRoute(a);
        var text = _actions.RouteCodes();

        var friend = new WaypointLibrary();
        var theirs = new MapActions(new OverlayConfig(), friend, new FriendBook(), name => name);
        theirs.PasteRouteFrom(text);

        Assert.Equal(new[] { "A", "B words", "A" }, theirs.Route.Stops.Select(id => friend.Find(id)!.Name));
        Assert.Equal(new[] { a.X, b.X, a.X }, theirs.Route.Stops.Select(id => friend.Find(id)!.X));
    }

    [Fact]
    public void CopyTakesTheWholeListPassedStopsToo()
    {
        var (a, b) = (Spot("A", Km), Spot("B", 2 * Km));
        _actions.AddToRoute(a);
        _actions.AddToRoute(b);
        _actions.SkipStop();
        string? copied = null;
        _actions.WriteClipboard = text => copied = text;

        Assert.Equal(MapActions.RouteCopiedNotice, _actions.CopyRoute());

        Assert.Equal("pandora:1000,0 A\npandora:2000,0 B", copied);
    }

    [Fact]
    public void ABusyClipboardIsSaidNotThrown()
    {
        _actions.AddToRoute(Spot("A", Km));
        _actions.WriteClipboard = _ => throw new InvalidOperationException();
        _actions.ReadClipboard = () => throw new InvalidOperationException();

        Assert.Equal("couldn't reach the clipboard", _actions.CopyRoute());
        Assert.Equal("couldn't reach the clipboard", _actions.PasteRoute().Notice);
        Assert.False(_actions.ClipboardHasRoute());
    }

    /// <summary>A hand pick left waiting must not hold a stop you stand at once the walk starts over (found writing this step).</summary>
    [Fact]
    public void RestartForgetsAHandPick()
    {
        var (a, b) = (Spot("A", Km), Spot("B", 2 * Km));
        _actions.AddToRoute(a);
        _actions.AddToRoute(b);
        _actions.GoToStop(1); // B, picked by hand: it must be left before it counts

        _actions.RestartRoute();
        var arrivals = _actions.RouteAt(a.X, a.Y); // standing at A, the first stop

        Assert.Equal(1, Assert.Single(arrivals).StopNumber);
    }

    [Fact]
    public void APasteForgetsAHandPick()
    {
        var (a, b) = (Spot("A", Km), Spot("B", 2 * Km));
        _actions.AddToRoute(a);
        _actions.AddToRoute(b);
        _actions.GoToStop(1);

        _actions.PasteRouteFrom("pandora:5000,0 Delta\npandora:6000,0 Forks");
        var delta = _library.Find(Route.Stops[0])!;

        Assert.Equal(1, Assert.Single(_actions.RouteAt(delta.X, delta.Y)).StopNumber);
    }

    [Fact]
    public void AWaypointDeletedFromTheSharedGroupLeavesTheRoute()
    {
        _actions.PasteRouteFrom("pandora:1000,0 Delta\npandora:2000,0 Forks\npandora:3000,0 Lake");
        var forks = _library.Items.Single(w => w.Name == "Forks");

        _actions.Remove(forks);

        Assert.Equal(new[] { "Delta", "Lake" }, StopNames());
    }
}

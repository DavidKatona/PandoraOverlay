using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>The route's list and its current stop: the owner's rules (PLAN.md), with the file's round trip. Never touches the real route.json.</summary>
public sealed class RouteTests
{
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid(), D = Guid.NewGuid();

    private static Route Of(params Guid[] stops)
    {
        var route = new Route();
        foreach (var stop in stops) route.Add(stop);
        return route;
    }

    [Fact]
    public void TheFirstStopAddedIsTheCurrentOne()
    {
        var route = new Route();
        Assert.False(route.HasCurrent);
        Assert.False(route.IsFinished); // an empty route has not finished anything

        route.Add(A);

        Assert.Equal(A, route.CurrentStop);
        route.Add(B);
        Assert.Equal(A, route.CurrentStop);
        Assert.Equal(B, route.NextStop);
    }

    [Fact]
    public void AFinishedRouteKeepsItsListAndCarriesOnToAStopAddedLater()
    {
        var route = Of(A);
        route.Skip();
        Assert.True(route.IsFinished);
        Assert.Null(route.CurrentStop);
        Assert.Equal(1, route.Count);

        route.Add(B);

        Assert.False(route.IsFinished);
        Assert.Equal(B, route.CurrentStop);
    }

    [Fact]
    public void TheSameWaypointCanBeOnARouteTwice()
    {
        var route = Of(A, B, A);
        Assert.Equal(new[] { A, B, A }, route.Stops);
    }

    [Fact]
    public void AddNextGoesStraightAfterTheCurrentStop()
    {
        var route = Of(A, B, C);
        route.Skip(); // B is current

        route.AddNext(D);

        Assert.Equal(new[] { A, B, D, C }, route.Stops);
        Assert.Equal(B, route.CurrentStop);
        Assert.Equal(D, route.NextStop);
    }

    [Fact]
    public void AddNextOnAnEmptyOrFinishedRouteIsAnAdd()
    {
        var route = new Route();
        route.AddNext(A);
        Assert.Equal(A, route.CurrentStop);

        route.Skip();
        route.AddNext(B);
        Assert.Equal(new[] { A, B }, route.Stops);
        Assert.Equal(B, route.CurrentStop);
    }

    [Fact]
    public void AFullRouteTakesNoMoreAndSaysSo()
    {
        var route = new Route();
        for (var i = 0; i < Route.Capacity; i++) Assert.True(route.Add(A));

        Assert.True(route.IsFull);
        Assert.False(route.Add(B));
        Assert.False(route.AddNext(B));
        Assert.Equal(Route.Capacity, route.Count);
    }

    [Fact]
    public void RemovingAStopBeforeTheCurrentOneKeepsTheSameCurrentStop()
    {
        var route = Of(A, B, C);
        route.Skip();
        route.Skip(); // C is current

        route.RemoveAt(0);

        Assert.Equal(C, route.CurrentStop);
        Assert.Equal(1, route.Current);
    }

    [Fact]
    public void RemovingTheCurrentStopLetsTheNextOneTakeOver()
    {
        var route = Of(A, B, C);
        route.Skip(); // B is current

        route.RemoveAt(1);

        Assert.Equal(C, route.CurrentStop);
    }

    [Fact]
    public void RemovingTheLastUpcomingStopFinishesTheRoute()
    {
        var route = Of(A, B);
        route.Skip(); // B is current

        route.RemoveAt(1);

        Assert.True(route.IsFinished);
    }

    [Fact]
    public void AWaypointDeletedFromTheLibraryLeavesTheRouteEveryRepeatOfIt()
    {
        var route = Of(A, B, A, C);
        route.Skip();
        route.Skip(); // the second A is current

        route.KeepOnly(id => id != A);

        Assert.Equal(new[] { B, C }, route.Stops);
        Assert.Equal(C, route.CurrentStop); // the next one takes over
    }

    [Fact]
    public void KeepingEverythingChangesNothing()
    {
        var route = Of(A, B);
        var changes = 0;
        route.Changed += () => changes++;

        route.KeepOnly(_ => true);

        Assert.Equal(0, changes);
    }

    [Fact]
    public void MovingAStopToTheTopOfTheUpcomingMakesItCurrent()
    {
        var route = Of(A, B, C, D);
        route.Skip(); // B is current

        route.Move(3, 1);

        Assert.Equal(new[] { A, D, B, C }, route.Stops);
        Assert.Equal(D, route.CurrentStop);
    }

    [Fact]
    public void MovingTheCurrentStopDownLetsTheNextOneTakeOver()
    {
        var route = Of(A, B, C);

        route.Move(0, 2);

        Assert.Equal(new[] { B, C, A }, route.Stops);
        Assert.Equal(B, route.CurrentStop);
    }

    [Theory]
    [InlineData(0, 2)] // a passed stop can't be picked up
    [InlineData(2, 0)] // nor can anything be dropped among the passed
    [InlineData(2, 9)] // past the end
    public void MovesTouchingPassedStopsAreIgnored(int from, int to)
    {
        var route = Of(A, B, C, D);
        route.Skip(); // B is current
        var changes = 0;
        route.Changed += () => changes++;

        route.Move(from, to);

        Assert.Equal(new[] { A, B, C, D }, route.Stops);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void AnyStopCanBeMadeCurrentInEitherDirection()
    {
        var route = Of(A, B, C);

        route.GoTo(2);
        Assert.Equal(C, route.CurrentStop);

        route.GoTo(0);
        Assert.Equal(A, route.CurrentStop);

        route.GoTo(9);
        Assert.Equal(A, route.CurrentStop);
    }

    [Fact]
    public void RestartWalksItAgainFromTheFirstStop()
    {
        var route = Of(A, B);
        route.Skip();
        route.Skip();

        route.Restart();

        Assert.Equal(A, route.CurrentStop);
    }

    [Fact]
    public void ClearLeavesNoRoute()
    {
        var route = Of(A, B);
        route.Skip();

        route.Clear();

        Assert.True(route.IsEmpty);
        Assert.Equal(0, route.Current);
        Assert.False(route.IsFinished);
    }

    [Fact]
    public void APastedRouteReplacesAndStartsAtTheFirstStop()
    {
        var route = Of(A, B);
        route.Skip();

        route.ReplaceWith(new[] { C, D });

        Assert.Equal(new[] { C, D }, route.Stops);
        Assert.Equal(C, route.CurrentStop);
    }

    [Fact]
    public void APastedRouteTakesTheFirstStopsUpToTheLimit()
    {
        var route = new Route();

        route.ReplaceWith(Enumerable.Repeat(A, Route.Capacity + 5));

        Assert.Equal(Route.Capacity, route.Count);
    }

    [Fact]
    public void TheRouteRunsOnlyWhileItsCurrentStopIsTracked()
    {
        var route = Of(A, B);

        Assert.True(route.IsRunning(A));
        Assert.False(route.IsPaused(A));

        Assert.False(route.IsRunning(B)); // a later stop tracked by hand pauses it like any other waypoint
        Assert.True(route.IsPaused(B));

        Assert.False(route.IsRunning(null));
        Assert.True(route.IsPaused(null));
    }

    [Fact]
    public void AFinishedOrEmptyRouteIsNeitherRunningNorPaused()
    {
        var route = new Route();
        Assert.False(route.IsRunning(null));
        Assert.False(route.IsPaused(null));

        route.Add(A);
        route.Skip();
        Assert.False(route.IsRunning(A));
        Assert.False(route.IsPaused(A));
    }

    [Fact]
    public void EveryMutationThatChangesSomethingSaysSo()
    {
        var route = new Route();
        var changes = 0;
        route.Changed += () => changes++;

        route.Add(A); route.Add(B); route.AddNext(C); route.Move(1, 2); route.GoTo(1); route.Skip();
        route.RemoveAt(0); route.Restart(); route.ReplaceWith(new[] { D }); route.Clear();

        Assert.Equal(10, changes);
    }

    [Fact]
    public void TheFileKeepsTheStopsTheirOrderAndTheCurrentStop()
    {
        var route = Of(A, B, A);
        route.Skip();

        var back = Route.FromJson(route.ToJson());

        Assert.Equal(new[] { A, B, A }, back.Stops);
        Assert.Equal(1, back.Current);
    }

    [Fact]
    public void AnOddFileIsReadForWhatIsGoodInIt()
    {
        var json = $$"""{"Format":1,"Current":99,"Stops":["{{A}}","00000000-0000-0000-0000-000000000000","{{B}}"]}""";

        var route = Route.FromJson(json);

        Assert.Equal(new[] { A, B }, route.Stops);
        Assert.True(route.IsFinished); // the index is clamped, not trusted
    }

    [Fact]
    public void AFileWithNoStopsIsNoRoute()
    {
        Assert.True(Route.FromJson("{}").IsEmpty);
    }

    [Fact]
    public void AFileLongerThanTheLimitIsCut()
    {
        var ids = string.Join(",", Enumerable.Repeat($"\"{A}\"", Route.Capacity + 3));

        var route = Route.FromJson($$"""{"Current":0,"Stops":[{{ids}}]}""");

        Assert.Equal(Route.Capacity, route.Count);
    }
}

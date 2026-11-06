using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>A shared route as text: one spot code per line (PLAN.md, decision 25).</summary>
public sealed class RouteCodeTests
{
    private static Waypoint At(double x, double y, string name) => new() { Name = name, X = x, Y = y };

    [Fact]
    public void EachStopIsASpotCodeOnItsOwnLine()
    {
        var text = RouteCode.Format(new[] { At(6200, -316800, "First"), At(100, 200, "Second") });

        Assert.Equal("pandora:62,-3168 First\npandora:1,2 Second", text);
    }

    [Fact]
    public void ARouteComesBackInOrderWithItsNamesAndRepeats()
    {
        var text = RouteCode.Format(new[] { At(100, 200, "One"), At(300, 400, "Two words"), At(100, 200, "One") });

        var stops = RouteCode.Parse(text);

        Assert.Equal(new[] { "One", "Two words", "One" }, stops.Select(s => s.Name));
        Assert.Equal((100.0, 200.0), (stops[0].X, stops[0].Y));
        Assert.Equal((300.0, 400.0), (stops[1].X, stops[1].Y));
    }

    [Fact]
    public void EachLineStillWorksAsASingleSpot()
    {
        var line = RouteCode.Format(new[] { At(6200, -316800, "First"), At(100, 200, "Second") }).Split('\n')[1];

        Assert.True(ShareCode.TryParse(line, out var x, out var y, out var name));
        Assert.Equal((100.0, 200.0, "Second"), (x, y, name));
    }

    [Fact]
    public void WindowsLineEndingsDoNotReachTheNames()
    {
        var stops = RouteCode.Parse("pandora:1,2 One\r\npandora:3,4 Two\r\n");

        Assert.Equal(new[] { "One", "Two" }, stops.Select(s => s.Name));
    }

    [Fact]
    public void OnlyLinesWithTheCodesPrefixCount()
    {
        var chat = "meet at 20, 21 tonight\npandora:1,2 One\nthen go here:\npandora:3,4 Two\nsee you at 8, 30";

        var stops = RouteCode.Parse(chat);

        Assert.Equal(new[] { "One", "Two" }, stops.Select(s => s.Name));
    }

    [Fact]
    public void ACodeCanSitAfterOtherTextOnItsLine()
    {
        Assert.Equal("One", Assert.Single(RouteCode.Parse("stop: pandora:1,2 One")).Name);
    }

    [Fact]
    public void AStopWithNoNameIsKeptWithAnEmptyOne()
    {
        Assert.Equal("", Assert.Single(RouteCode.Parse("pandora:1,2")).Name);
    }

    [Fact]
    public void APositionOffAnyMapIsDropped()
    {
        Assert.Empty(RouteCode.Parse("pandora:999999,2 Nowhere"));
    }

    [Theory]
    [InlineData("pandora:1,2 One\npandora:3,4 Two", true)]
    [InlineData("pandora:1,2 One", false)]
    [InlineData("nothing here", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TwoOrMoreStopsMakeARoute(string? text, bool isRoute)
    {
        Assert.Equal(isRoute, RouteCode.IsRoute(text));
    }

    [Fact]
    public void ParseHoldsEverythingForTheCallerToLimit()
    {
        var text = string.Join("\n", Enumerable.Range(0, Route.Capacity + 3).Select(i => $"pandora:{i},0"));

        Assert.Equal(Route.Capacity + 3, RouteCode.Parse(text).Count);
    }
}

using Xunit;

namespace PandoraOverlay.Tests;

public class WaypointPacksTests
{
    private static Waypoint Wp(string name, double xMeters, double yMeters, int colour = 0, bool visible = true, string? pack = null) =>
        new() { Name = name, X = xMeters * 100, Y = yMeters * 100, Colour = colour, Visible = visible, Pack = pack };

    [Fact]
    public void ExportKeepsIdsClearsPacksAndShowsEverything()
    {
        var mine = Wp("Nest", 10, 20, colour: 3, visible: false, pack: "old pack");
        var json = WaypointPacks.Export(new[] { mine }, "  Dave's  landmarks ");
        var pack = WaypointPacks.Parse(json, "fallback");

        Assert.NotNull(pack);
        Assert.Equal("Dave's landmarks", pack!.Name);
        var w = Assert.Single(pack.Waypoints);
        Assert.Equal(mine.Id, w.Id);
        Assert.Equal("Nest", w.Name);
        Assert.Equal(3, w.Colour);
        Assert.True(w.Visible);
        Assert.Null(w.Pack);
    }

    [Fact]
    public void ParseFallsBackToTheFileNameAndRejectsNonPacks()
    {
        var pack = WaypointPacks.Parse("""{"Waypoints":[{"Name":"a","X":1,"Y":2}]}""", "my-waypoints");
        Assert.Equal("my-waypoints", pack!.Name);
        Assert.Null(WaypointPacks.Parse("not json", "x"));
        Assert.Null(WaypointPacks.Parse("""{"Format":1}""", "x"));
        Assert.Null(WaypointPacks.Parse("", "x"));
    }

    [Fact]
    public void ImportsArriveHiddenAndTagged()
    {
        var target = new List<Waypoint>();
        var pack = new WaypointPacks.Pack("Dave's pack", new[] { Wp("Water", 1, 1, colour: 5) });
        var result = WaypointPacks.Merge(target, pack);

        Assert.Equal(new WaypointPacks.MergeResult(1, 0, 0), result);
        var w = Assert.Single(target);
        Assert.False(w.Visible);
        Assert.Equal("Dave's pack", w.Pack);
        Assert.Equal(5, w.Colour);
    }

    [Fact]
    public void SkipsEntriesAlreadyPresentById()
    {
        var existing = Wp("Nest", 10, 10);
        var target = new List<Waypoint> { existing };
        var incoming = new Waypoint { Id = existing.Id, Name = "Renamed elsewhere", X = 999_900, Y = 0 };
        var result = WaypointPacks.Merge(target, new WaypointPacks.Pack("p", new[] { incoming }));
        Assert.Equal(new WaypointPacks.MergeResult(0, 1, 0), result);
        Assert.Single(target);
    }

    [Fact]
    public void SkipsSameNameWithinTwentyMetres()
    {
        var target = new List<Waypoint> { Wp("Nest", 100, 100) };
        var near = Wp("nest", 112, 116); // 20 m away, different case
        var far = Wp("Nest", 130, 100);  // 30 m away
        var elsewhere = Wp("Water", 100, 100);
        var result = WaypointPacks.Merge(target, new WaypointPacks.Pack("p", new[] { near, far, elsewhere }));
        Assert.Equal(new WaypointPacks.MergeResult(2, 1, 0), result);
        Assert.Equal(3, target.Count);
    }

    [Fact]
    public void StopsAtCapacityAndCountsTheOverflow()
    {
        var target = new List<Waypoint>();
        for (var i = 0; i < WaypointLibrary.Capacity - 2; i++) target.Add(Wp($"w{i}", i, 0));
        var pack = new WaypointPacks.Pack("big", new[] { Wp("a", 1000, 1), Wp("b", 1000, 2), Wp("c", 1000, 3), Wp("d", 1000, 4) });
        var result = WaypointPacks.Merge(target, pack);
        Assert.Equal(new WaypointPacks.MergeResult(2, 0, 2), result);
        Assert.Equal(WaypointLibrary.Capacity, target.Count);
    }

    [Fact]
    public void ReimportingAnExportIsAllDuplicates()
    {
        var mine = new List<Waypoint> { Wp("Nest", 10, 20), Wp("Water", 30, 40) };
        var json = WaypointPacks.Export(mine, "mine");
        var result = WaypointPacks.Merge(mine, WaypointPacks.Parse(json, "x")!);
        Assert.Equal(new WaypointPacks.MergeResult(0, 2, 0), result);
    }
}

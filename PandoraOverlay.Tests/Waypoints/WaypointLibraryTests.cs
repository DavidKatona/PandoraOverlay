using Xunit;

namespace PandoraOverlay.Tests;

public class WaypointLibraryTests
{
    [Fact]
    public void AddsWithSanitizedNameAndWrappedColour()
    {
        var lib = new WaypointLibrary();
        var wp = lib.Add("  Nest\tsite \r\n ", 6200, -316800, 14);
        Assert.NotNull(wp);
        Assert.Equal("Nest site", wp!.Name);
        Assert.Equal(2, wp.Colour); // 14 wraps to purple in a 12-colour palette
        Assert.True(wp.Visible);
        Assert.Single(lib.Items);
    }

    [Fact]
    public void RejectsOffIslandPositions()
    {
        var lib = new WaypointLibrary();
        Assert.Null(lib.Add("far", 99_000_000, 0, 0));
        Assert.Null(lib.Add("nan", double.NaN, 0, 0));
        Assert.Empty(lib.Items);
    }

    [Fact]
    public void CapsAtCapacity()
    {
        var lib = new WaypointLibrary();
        for (var i = 0; i < WaypointLibrary.Capacity; i++) Assert.NotNull(lib.Add($"w{i}", i, i, i));
        Assert.True(lib.IsFull);
        Assert.Null(lib.Add("one too many", 0, 0, 0));
        Assert.Equal(256, lib.Count);
    }

    [Fact]
    public void NamesAreCappedAndNeverEmpty()
    {
        Assert.Equal("Waypoint", WaypointLibrary.SanitizeName(""));
        Assert.Equal("Waypoint", WaypointLibrary.SanitizeName("\u0001\u0002"));
        Assert.Equal(32, WaypointLibrary.SanitizeName(new string('a', 40)).Length);
    }

    [Fact]
    public void NextNameSkipsTakenOnes()
    {
        var lib = new WaypointLibrary();
        lib.Add("Waypoint 1", 0, 0, 0);
        lib.Add("Waypoint 2", 0, 0, 0);
        Assert.Equal("Waypoint 3", lib.NextName());
        lib.Add("Waypoint 3", 0, 0, 0);
        lib.Remove(lib.Items[0].Id);
        Assert.Equal("Waypoint 4", lib.NextName()); // count is 2, but "Waypoint 3" is in use
    }

    /// <summary>A new waypoint takes its area's name, then the name numbered from 2: the first not in use, whatever its case.</summary>
    [Fact]
    public void NextNameUsesTheAreaThenNumbersIt()
    {
        var lib = new WaypointLibrary();
        Assert.Equal("Test Valley", lib.NextName("Test Valley"));
        lib.Add("Test Valley", 0, 0, 0);
        Assert.Equal("Test Valley 2", lib.NextName("Test Valley"));
        lib.Add("test valley 2", 0, 0, 0);
        Assert.Equal("Test Valley 3", lib.NextName("Test Valley"));

        lib.Remove(lib.Items[0].Id); // the bare name is free again
        Assert.Equal("Test Valley", lib.NextName("Test Valley"));
    }

    /// <summary>No area (out at sea, nothing to place it with) keeps the old "Waypoint N".</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NextNameWithoutAnAreaIsWaypointN(string? area)
    {
        var lib = new WaypointLibrary();
        lib.Add("Somewhere", 0, 0, 0);
        Assert.Equal("Waypoint 2", lib.NextName(area));
    }

    /// <summary>A name at the length cap gives way to its number, so the numbered names still differ.</summary>
    [Fact]
    public void ALongAreaNameMakesRoomForItsNumber()
    {
        var area = new string('a', WaypointLibrary.MaxNameLength);
        var lib = new WaypointLibrary();
        lib.Add(lib.NextName(area), 0, 0, 0);
        var second = lib.NextName(area);
        Assert.Equal(WaypointLibrary.MaxNameLength, second.Length);
        Assert.EndsWith(" 2", second);
        lib.Add(second, 0, 0, 0);
        Assert.Equal(2, lib.Items.Select(w => w.Name).Distinct().Count());
    }

    [Fact]
    public void NearestHonoursTheFilter()
    {
        var lib = new WaypointLibrary();
        var near = lib.Add("near", 1000, 0, 0)!;
        var far = lib.Add("far", 5000, 0, 0)!;
        near.Visible = false;
        Assert.Equal("near", lib.Nearest(0, 0)!.Value.Waypoint.Name);
        var visibleOnly = lib.Nearest(0, 0, w => w.Visible);
        Assert.Equal("far", visibleOnly!.Value.Waypoint.Name);
        Assert.Equal(50, visibleOnly.Value.Meters);
        Assert.Same(far, visibleOnly.Value.Waypoint);
    }

    [Fact]
    public void RoundTripsThroughJson()
    {
        var lib = new WaypointLibrary();
        var a = lib.Add("Nest", 6200, -316800, 3)!;
        var b = lib.Add("Water", 100, 200, 5)!;
        b.Visible = false;

        var copy = WaypointLibrary.FromJson(lib.ToJson());
        Assert.Equal(2, copy.Count);
        Assert.Equal(a.Id, copy.Items[0].Id);
        Assert.Equal("Nest", copy.Items[0].Name);
        Assert.Equal(3, copy.Items[0].Colour);
        Assert.False(copy.Items[1].Visible);
    }

    [Fact]
    public void ImportGateDropsBadEntriesAndFixesIds()
    {
        const string json = """
            {"Format":1,"Waypoints":[
              {"Id":"00000000-0000-0000-0000-000000000000","Name":"no id","X":1,"Y":2,"Colour":99},
              {"Id":"11111111-1111-1111-1111-111111111111","Name":"dup","X":1,"Y":2},
              {"Id":"11111111-1111-1111-1111-111111111111","Name":"dup again","X":3,"Y":4},
              {"Name":"off island","X":99999999,"Y":0},
              {"Name":"ok","X":5,"Y":6,"Pack":"  Dave's pack  "}
            ]}
            """;
        var lib = WaypointLibrary.FromJson(json);
        Assert.Equal(4, lib.Count);
        Assert.NotEqual(Guid.Empty, lib.Items[0].Id);
        Assert.Equal(3, lib.Items[0].Colour); // 99 wrapped
        Assert.NotEqual(lib.Items[1].Id, lib.Items[2].Id);
        Assert.Equal("Dave's pack", lib.Items[3].Pack);
        Assert.DoesNotContain(lib.Items, w => w.Name == "off island");
    }

    [Fact]
    public void GarbageFileIsEmptyNotFatal()
    {
        Assert.Empty(WaypointLibrary.FromJson("{}").Items);
        Assert.Empty(WaypointLibrary.FromJson("""{"Waypoints":null}""").Items);
    }

    [Fact]
    public void ChangedFiresOnMutationsOnly()
    {
        var lib = new WaypointLibrary();
        var fired = 0;
        lib.Changed += () => fired++;
        var wp = lib.Add("a", 0, 0, 0)!;
        lib.Nearest(0, 0);
        lib.Remove(wp.Id);
        lib.Remove(wp.Id); // already gone
        lib.Clear();       // already empty
        Assert.Equal(2, fired);
    }

    [Fact]
    public void PaletteHasTwelveDistinctColours()
    {
        Assert.Equal(12, WaypointPalette.Count);
        Assert.Equal(12, WaypointPalette.Colours.Select(c => c.Hex).Distinct().Count());
        Assert.Equal(0, WaypointPalette.Wrap(12));
        Assert.Equal(11, WaypointPalette.Wrap(-1));
    }
}

using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>The request gates the big map widens: nothing new is fetched, and nothing faster.</summary>
public sealed class MapGateTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

    private static OverlayConfig Quiet() => new()
    {
        MinimapEnabled = true, HeatmapEnabled = false, FriendsOnMinimap = false,
        ActivityEnabled = false, ActivityIncludeFriends = false,
        BigMapHeatmap = false, BigMapFriends = false
    };

    [Fact]
    public void TheMinimapsHeatmapGateIsUnchanged()
    {
        var c = Quiet();
        Assert.False(PollService.HeatmapWantedFor(c, bigMapOpen: false));

        c.HeatmapEnabled = true;
        Assert.True(PollService.HeatmapWantedFor(c, bigMapOpen: false));

        c.MinimapEnabled = false; // the layer is on, the widget isn't: nothing to show it on
        Assert.False(PollService.HeatmapWantedFor(c, bigMapOpen: false));
    }

    [Fact]
    public void TheBigMapWantsTheHeatmapOnlyWhileOpenWithItsLayerOn()
    {
        var c = Quiet();
        c.BigMapHeatmap = true;

        Assert.True(PollService.HeatmapWantedFor(c, bigMapOpen: true));
        Assert.False(PollService.HeatmapWantedFor(c, bigMapOpen: false)); // closed: its layer costs nothing

        c.BigMapHeatmap = false;
        Assert.False(PollService.HeatmapWantedFor(c, bigMapOpen: true)); // open, layer off: nothing either
    }

    [Fact]
    public void TheFriendsGateKeepsTheWidgetsRules()
    {
        var c = Quiet();
        Assert.False(PollService.FriendsWantedFor(c, bigMapOpen: false));

        c.ActivityEnabled = c.ActivityIncludeFriends = true;
        Assert.True(PollService.FriendsWantedFor(c, bigMapOpen: false));

        c.ActivityIncludeFriends = false;
        c.FriendsOnMinimap = true;
        Assert.True(PollService.FriendsWantedFor(c, bigMapOpen: false));
    }

    [Fact]
    public void TheBigMapWantsFriendsOnlyWhileOpenWithItsLayerOn()
    {
        var c = Quiet();
        c.BigMapFriends = true;

        Assert.True(PollService.FriendsWantedFor(c, bigMapOpen: true));
        Assert.False(PollService.FriendsWantedFor(c, bigMapOpen: false));

        c.BigMapFriends = false;
        Assert.False(PollService.FriendsWantedFor(c, bigMapOpen: true));
    }

    [Fact]
    public void AKeptHeatmapIsFreshForOneFetchInterval()
    {
        Assert.True(PollService.HeatmapFresh(Now - TimeSpan.FromSeconds(59), Now));
        Assert.False(PollService.HeatmapFresh(Now - TimeSpan.FromSeconds(60), Now)); // the timer's own pace: fetch again
        Assert.False(PollService.HeatmapFresh(DateTime.MinValue, Now));             // never fetched
    }

    [Fact]
    public void AFreshConfigOpensTheBigMapWithEverythingButTheHeatmap()
    {
        var c = new OverlayConfig();

        Assert.True(c.BigMapAreas && c.BigMapNames && c.BigMapWaypoints && c.BigMapFriends && c.BigMapTrail);
        Assert.False(c.BigMapHeatmap); // it costs requests: off until asked for
        Assert.Equal("Ctrl+M", c.HotkeyBigMap);
        Assert.Equal(new HotkeySpec(System.Windows.Input.ModifierKeys.Control, System.Windows.Input.Key.M), HotkeySpec.TryParse(c.HotkeyBigMap));
    }
}

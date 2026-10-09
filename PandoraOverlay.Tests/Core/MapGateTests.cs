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
        Assert.False(PollService.HeatmapWantedFor(c, bigMapOpen: false, overlayHidden: false));

        c.HeatmapEnabled = true;
        Assert.True(PollService.HeatmapWantedFor(c, bigMapOpen: false, overlayHidden: false));

        c.MinimapEnabled = false; // the layer is on, the widget isn't: nothing to show it on
        Assert.False(PollService.HeatmapWantedFor(c, bigMapOpen: false, overlayHidden: false));
    }

    [Fact]
    public void TheBigMapWantsTheHeatmapOnlyWhileOpenWithItsLayerOn()
    {
        var c = Quiet();
        c.BigMapHeatmap = true;

        Assert.True(PollService.HeatmapWantedFor(c, bigMapOpen: true, overlayHidden: false));
        Assert.False(PollService.HeatmapWantedFor(c, bigMapOpen: false, overlayHidden: false)); // closed: its layer costs nothing

        c.BigMapHeatmap = false;
        Assert.False(PollService.HeatmapWantedFor(c, bigMapOpen: true, overlayHidden: false)); // open, layer off: nothing either
    }

    /// <summary>A minimap hidden with the overlay (hide-all, the auto-hide) shows no heatmap, so none is fetched for it; the friends gate has no such input on purpose (the chime, the Activity log).</summary>
    [Fact]
    public void AHiddenOverlaysMinimapWantsNoHeatmap()
    {
        var c = Quiet();
        c.HeatmapEnabled = true;
        Assert.False(PollService.HeatmapWantedFor(c, bigMapOpen: false, overlayHidden: true));
        Assert.True(PollService.HeatmapWantedFor(c, bigMapOpen: false, overlayHidden: false)); // shown again: wanted again

        c.BigMapHeatmap = true;
        Assert.True(PollService.HeatmapWantedFor(c, bigMapOpen: true, overlayHidden: true)); // the open map's own layer is its own surface
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
        Assert.True(PollService.HeatmapFresh(Now - TimeSpan.FromSeconds(1), Now));    // a hotkey right after a fetch: no request
        Assert.True(PollService.HeatmapFresh(Now - TimeSpan.FromSeconds(58.9), Now));
        Assert.False(PollService.HeatmapFresh(Now - TimeSpan.FromSeconds(59.9), Now)); // the re-armed timer's tick, a hair early by the clock: still fetches
        Assert.False(PollService.HeatmapFresh(Now - TimeSpan.FromSeconds(60), Now));
        Assert.False(PollService.HeatmapFresh(DateTime.MinValue, Now));               // never fetched
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

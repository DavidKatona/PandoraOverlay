using System.Text.Json;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The Pack starts OFF (the owner, Nov 10 2026): an update must put no new
/// widget under anyone's stats panel and send no friends requests for a player
/// who had stopped them. Only the control panel's Pack button turns it on.
/// </summary>
public class PackOffByDefaultTests
{
    /// <summary>A player who had switched every other friends surface off.</summary>
    private static OverlayConfig NoFriendsSurfaces(OverlayConfig c)
    {
        c.ActivityIncludeFriends = false;
        c.FriendsOnMinimap = false;
        c.BigMapFriends = false;
        return c;
    }

    [Fact]
    public void AFreshConfigShowsNoPackAndHasNoPackFriendsSurface()
    {
        var c = NoFriendsSurfaces(new OverlayConfig());
        Assert.False(c.PackEnabled);
        Assert.False(PackRows.Wanted(c, ticked: 0));
        Assert.False(PackRows.Wanted(c, ticked: 3)); // ticks never switch it on
        Assert.False(PollService.FriendsWantedFor(c, bigMapOpen: false, packShown: PackRows.Wanted(c, 0)));
        Assert.True(PackRows.HasAnyone(c, 0)); // and yet its button is live: one click turns it on
    }

    /// <summary>An update reads a config.json from before the Pack: no key, so the default, off.</summary>
    [Fact]
    public void AnOlderConfigFileReadsOff()
    {
        var old = JsonSerializer.Deserialize<OverlayConfig>("""{"ActivityEnabled":true}""")!;
        Assert.False(old.PackEnabled);
        Assert.False(PackRows.Wanted(old, 0));
    }

    /// <summary>No migration: an rc.1 tester's file that has the key keeps it.</summary>
    [Fact]
    public void AFileThatTurnedItOnKeepsItOn()
    {
        var kept = JsonSerializer.Deserialize<OverlayConfig>("""{"PackEnabled":true}""")!;
        Assert.True(kept.PackEnabled);
        Assert.True(PackRows.Wanted(kept, 0)); // every friend on your dino: shown
        Assert.True(PollService.FriendsWantedFor(NoFriendsSurfaces(kept), bigMapOpen: false, packShown: PackRows.Wanted(kept, 0)));
    }

    [Fact]
    public void TurnedOnInTheTickedModeItNeedsATick()
    {
        var c = new OverlayConfig { PackEnabled = true, PackShows = "ticked" };
        Assert.False(PackRows.HasAnyone(c, 0)); // the button greyed
        Assert.False(PackRows.Wanted(c, 0));
        Assert.True(PackRows.Wanted(c, 1));
    }
}

using System.Reflection;
using System.Windows.Threading;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The Pack adds no request and no pace (CLAUDE.md, constraint 2): shown, it
/// is one more friends surface on the same roster. Appearing asks once, and
/// only when the last roster is a poll interval old; going away asks nothing;
/// a Watch tick on a Pack already shown, its drawing, a setting and a theme
/// switch ask nothing. The website is a counting fake behind the poll
/// service's site client (<see cref="CountingSite"/>), so nothing leaves the test.
/// </summary>
[Collection(TheThemeOnScreen.Name)]
public sealed class PackRequestTests
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>Signed in, no other friends surface on, polling "running" (its timer marked on, never ticking here).</summary>
    private static (PollService Poll, CountingSite Site, OverlayConfig Config) Polling()
    {
        var config = new OverlayConfig { ActivityEnabled = false, ActivityIncludeFriends = false, FriendsOnMinimap = false, MinimapEnabled = false };
        config.SetCookie("made-up-test-value-not-a-session");
        var poll = new PollService(config); // never started: nothing ticks
        var site = CountingSite.Install(poll);
        ((DispatcherTimer)typeof(PollService).GetField("_timer", Any)!.GetValue(poll)!).IsEnabled = true;
        return (poll, site, config);
    }

    [Fact]
    public async Task ThePackAppearingAsksOnceUnderTheFloorAndGoingAwayAsksNothing()
    {
        var (poll, site, _) = Polling();

        await poll.RefreshFriendsAsync("Pack widget shown"); // not shown yet: no surface, nothing asked
        Assert.Equal(0, site.Requests);

        poll.PackShown = true;
        await poll.RefreshFriendsAsync("Pack widget shown");
        Assert.Equal(1, site.Requests); // the last roster was older than a poll interval: one fetch

        await poll.RefreshFriendsAsync("Pack widget shown"); // shown again at once (a hide and a show in a hurry)
        Assert.Equal(1, site.Requests); // under one interval: held for the next poll, no request

        poll.PackShown = false;
        poll.FriendsSurfaceHidden("Pack widget hidden");
        Assert.Equal(1, site.Requests); // a surface going away never asks
    }

    [Fact]
    public void AWatchTickItsDrawingASettingAndAThemeSwitchAskNothing() => TheThemeOnScreen.OnSta(() =>
    {
        Theme.EnsureLoaded();
        var (poll, site, config) = Polling();
        poll.PackShown = true;
        var roster = new[] { PackRowsTests.F("a"), PackRowsTests.F("b"), PackRowsTests.F("c", dino: "Carnotaurus") };
        var book = PackRowsTests.Book(roster, "a");

        var pack = new PackWindow(config, poll, book);
        book.SetWatch("b", true);   // a Watch tick on a Pack already shown: it redraws, nothing more
        book.SetWatch("c", true);
        config.PackFrame = "large";
        config.PackRows = "compact";
        pack.ApplySettingsFromConfig();
        pack.ApplyTheme();
        pack.SetEditMode(true);
        pack.SetEditMode(false);
        Assert.Equal(0, site.Requests);
        pack.Close();
    });
}

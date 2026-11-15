using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The pages' cues (H99): one for each click that did something, none for a refusal,
/// none for the first click of anything armed, <c>reward</c> in place of the click's
/// own when the click earned an achievement, and none for what only moves about. The
/// handlers are called as the events call them; the host's <see cref="FakeSound"/>
/// records what the game handed it.
/// </summary>
[Collection(LoopTimers.Name)]
public class PageSoundTests
{
    /// <summary>A Camp with something to spend and a run behind it.</summary>
    private static GameState Rich(long balance = 5_000, int runs = 3)
    {
        var state = ViewHarness.Ready(runs: runs);
        state.Wallet = new Wallet(balance, balance);
        return state;
    }

    // ---- Camp ----------------------------------------------------------------------------

    [Fact]
    public void StartARunHatches() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(Rich());
        using var view = ViewHarness.ViewOf(game);
        Assert.Empty(host.Sound.Played);   // Camp shown: nothing (H100)
        PageHarness.Shown<CampPage>(view).ActionClicked();
        Assert.NotNull(game.State.Run);
        Assert.Equal(new[] { "hatch" }, host.Sound.Played);
    });

    [Fact]
    public void TheUnlocksSecondClickUnlocksAndItsFirstIsSilent() => Sta.Run(() =>
    {
        var saved = Rich(balance: 1_000);
        saved.Bought = 2;
        saved.Selected = "Maiasaura";
        var (game, host) = ViewHarness.Attached(saved);
        using var view = ViewHarness.ViewOf(game);
        var page = PageHarness.Shown<CampPage>(view);
        page.ActionClicked();
        Assert.True(page.UnlockArmed);
        Assert.Empty(host.Sound.Played);   // arming plays none
        page.ActionClicked();
        Assert.True(Unlocks.IsUnlocked(game.State, "Maiasaura"));
        Assert.Equal(new[] { "unlock" }, host.Sound.Played);
    });

    [Fact]
    public void CampsRefusalsAndItsChoicesAreSilent() => Sta.Run(() =>
    {
        // Amber short for a locked species: neither click unlocks, nothing starts, nothing sounds.
        var saved = Rich(balance: 10);
        saved.Selected = "Maiasaura";
        saved.MaxDanger = 3;
        var (game, host) = ViewHarness.Attached(saved);
        using var view = ViewHarness.ViewOf(game);
        var page = PageHarness.Shown<CampPage>(view);
        page.ActionClicked();
        page.ActionClicked();
        Assert.False(Unlocks.IsUnlocked(game.State, "Maiasaura"));
        Assert.Null(game.State.Run);
        // A danger not unlocked is refused; a danger and a species chosen are taken, silently.
        page.DangerClicked(5);
        page.DangerClicked(2);
        Assert.Equal(2, game.State.Danger);
        page.CardPressed("Dryosaurus");
        page.CardReleased("Dryosaurus");
        Assert.Equal("Dryosaurus", game.State.Selected);
        // Tabs, the Guide and Records move about only.
        view.TabClicked(PageKind.Guide);
        view.TabClicked(PageKind.Records);
        view.TabClicked(PageKind.Camp);
        Assert.Empty(host.Sound.Played);
    });

    // ---- the offer ------------------------------------------------------------------------

    /// <summary>A run just started with the whole lineage: its offer at the start open.</summary>
    private static GameState Offered(long balance)
    {
        var state = HatchlingGame.Started(ViewHarness.Ready(whole: true))!;   // Born ready owes a pick at the start
        Assert.NotNull(state.Run!.Offer);
        state.Wallet = new Wallet(balance, balance);
        return state;
    }

    [Fact]
    public void APickedCardChoosesAndARedrawShuffles() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(Offered(1_000));
        using var view = ViewHarness.ViewOf(game);
        var page = view.RunPage;
        page.RedrawClicked();
        Assert.Equal(new[] { "shuffle" }, host.Sound.Played);
        page.PickClicked(0);
        Assert.Equal(new[] { "shuffle", "choose" }, host.Sound.Played);
    });

    [Fact]
    public void ARedrawShortOfAmberIsSilent() => Sta.Run(() =>
    {
        var short_ = Offered(0);
        short_.Run!.FreeRedrawUsed = true;   // Second thoughts' free one spent: the next costs amber
        var (game, host) = ViewHarness.Attached(short_);
        using var view = ViewHarness.ViewOf(game);
        Assert.True(Offers.RedrawPrice(game.State.Run!) > 0);
        var offer = game.State.Run!.Offer;
        view.RunPage.RedrawClicked();
        Assert.Same(offer, game.State.Run!.Offer);   // refused
        Assert.Empty(host.Sound.Played);
    });

    // ---- the run's own controls ------------------------------------------------------------

    [Fact]
    public void ThePacesFastLeaveAndBackToCampAreSilentButLeavesSummaryRewards() => Sta.Run(() =>
    {
        // A turn played first: a run left before its first turn earns nothing (H47).
        var played = HatchlingGame.Advance(ViewHarness.Running(), new PlayerAction.Rest())!.Value.State;
        Assert.Null(played.Run!.Over);
        var (game, host) = ViewHarness.Attached(played);
        using var view = ViewHarness.ViewOf(game);
        var page = view.RunPage;
        page.PaceClicked(Pace.Sneak);
        page.PaceClicked(Pace.Walk);
        page.FastClicked(true);
        page.FastClicked(false);
        page.LeaveClicked();                  // armed: nothing
        Assert.Empty(host.Sound.Played);

        var earned = game.State.Achievements.Unlocked.Count;
        page.LeaveClicked();                  // Leave itself is silent; its summary earned "first" (H99)
        Assert.NotNull(game.State.Run!.Over);
        Assert.True(game.State.Achievements.Unlocked.Count > earned);
        Assert.Equal(new[] { "reward" }, host.Sound.Played);

        page.BackClicked();
        Assert.Null(game.State.Run);
        Assert.Equal(new[] { "reward" }, host.Sound.Played);
    });

    // ---- Lineage ------------------------------------------------------------------------

    private static Trait Buyable => Lineage.All.First(t => Lineage.Above(t.Trait) is null).Trait;

    [Fact]
    public void ARankTakenChoosesAndOneThatEarnsRootsRewardsInstead() => Sta.Run(() =>
    {
        var saved = Rich(balance: 100_000);
        var (game, host) = ViewHarness.Attached(saved);
        using var view = ViewHarness.ViewOf(game);
        var page = PageHarness.Open<LineagePage>(view, PageKind.Lineage);
        page.BuyClicked(Buyable);
        Assert.Equal(new[] { "choose" }, host.Sound.Played);

        // One rank short of "Take 10 ranks of traits" (roots): the rank earns it, and Reward replaces Choose.
        var shy = Rich(balance: 100_000);
        shy.Totals.RanksBought = AchievementList.RootsRanks - 1;
        var (rooted, rootedHost) = ViewHarness.Attached(shy);
        using var rootedView = ViewHarness.ViewOf(rooted);
        PageHarness.Open<LineagePage>(rootedView, PageKind.Lineage).BuyClicked(Buyable);
        Assert.True(rooted.State.Achievements.Has(AchievementList.Roots));
        Assert.Equal(new[] { "reward" }, rootedHost.Sound.Played);
    });

    [Fact]
    public void ARankShortOfAmberIsSilent() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(Rich(balance: 0));
        using var view = ViewHarness.ViewOf(game);
        PageHarness.Open<LineagePage>(view, PageKind.Lineage).BuyClicked(Buyable);
        Assert.Equal(0, game.State.Rank(Buyable));
        Assert.Empty(host.Sound.Played);
    });

    // ---- Journal ------------------------------------------------------------------------

    private static GameState Asking(long balance)
    {
        var state = ViewHarness.Ready(runs: 2);
        state.Wallet = new Wallet(balance, balance);
        state.Journal.Open = new[] { "Maiasaura", "Triceratops", "Pteranodon" }
            .Select(n => SpeciesTable.TryGet(n, out var info) ? Journal.EntryFor(info) : throw new ArgumentException(n)).ToList();
        return state;
    }

    [Fact]
    public void TheSwapsSecondClickShufflesAndItsFirstIsSilent() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(Asking(120));
        using var view = ViewHarness.ViewOf(game);
        var page = PageHarness.Open<JournalPage>(view, PageKind.Journal);
        page.SwapClicked(1);
        Assert.Equal(1, page.ArmedEntry);
        Assert.Empty(host.Sound.Played);
        page.SwapClicked(1);
        Assert.NotEqual("Triceratops", game.State.Journal.Open[1].Species);
        Assert.Equal(new[] { "shuffle" }, host.Sound.Played);
    });

    [Fact]
    public void ASwapShortOfAmberIsSilent() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(Asking(Tuning.SwapPrice - 1));
        using var view = ViewHarness.ViewOf(game);
        var page = PageHarness.Open<JournalPage>(view, PageKind.Journal);
        page.SwapClicked(0);
        page.SwapClicked(0);
        Assert.Equal("Maiasaura", game.State.Journal.Open[0].Species);
        Assert.Empty(host.Sound.Played);
    });

    // ---- the view's door (A79) ---------------------------------------------------------

    [Fact]
    public void APausedOrDisposedViewHandsTheHostNothing() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(Rich());
        var view = ViewHarness.ViewOf(game);
        view.Pause();
        view.Sound(Cue.Hatch);
        view.Resume();
        view.Sound(Cue.Hatch);
        view.Dispose();
        view.Sound(Cue.Hatch);
        Assert.Equal(new[] { "hatch" }, host.Sound.Played);
    });
}

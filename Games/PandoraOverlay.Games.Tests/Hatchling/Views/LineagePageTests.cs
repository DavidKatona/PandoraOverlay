using System.Windows;
using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The Lineage (H11, H19, H27): sixteen cards in four columns, each card's bottom
/// line in its four states, a purchase by a press and a release on a card that can
/// be bought (one rank, the wallet in the bar following), and nothing for a card
/// that can't. The handlers are called as the events call them.
/// </summary>
[Collection(LoopTimers.Name)]
public class LineagePageTests
{
    private static int Writes(FakeHost host) => host.Memory.Writes.Count;

    private static GameState WithWallet(long balance)
    {
        var state = ViewHarness.Ready(runs: 2);
        state.Wallet = new Wallet(balance, balance);
        return state;
    }

    [Fact]
    public void SixteenCardsStandInFourColumnsFirstTierAtTheTop() => Sta.Run(() =>
    {
        var (game, _) = ViewHarness.Attached(WithWallet(0));
        using var view = ViewHarness.ViewOf(game);
        var page = PageHarness.Open<LineagePage>(view, PageKind.Lineage);
        var cards = page.CardsShown;
        Assert.Equal(16, cards.Count);
        Assert.Equal(Lineage.All.Select(t => t.Trait), cards.Select(c => c.Trait));
        foreach (var card in cards)
        {
            var info = Lineage.Of(card.Trait);
            Assert.Equal(info.Branch * (LineagePage.ColumnWidth + LineagePage.ColumnGap), card.Place.Left);
            Assert.Equal(LineagePage.BranchNameHeight + (info.Tier - 1) * (LineagePage.CardHeight + LineagePage.CardGap), card.Place.Top);
            Assert.Equal(HatchlingText.TraitName(card.Trait), card.Name);
            Assert.Equal(HatchlingText.TraitText(card.Trait), card.Rule);
            Assert.Equal(HatchlingText.Pips(0, info.MaxRank), card.Pips);
        }
        // The four columns' x and the last card's bottom are the plan's boxes: 774 wide, 426 high under the line.
        Assert.Equal(774, 3 * (LineagePage.ColumnWidth + LineagePage.ColumnGap) + LineagePage.ColumnWidth);
        Assert.Equal(426, LineagePage.BranchNameHeight + 4 * LineagePage.CardHeight + 3 * LineagePage.CardGap);
    });

    [Fact]
    public void ACardsBottomLineHasItsFourStates() => Sta.Run(() =>
    {
        var saved = WithWallet(300);
        saved.Ranks[Trait.KeenNose] = Lineage.Of(Trait.KeenNose).MaxRank;   // complete
        saved.Ranks[Trait.DeepStomach] = 2;                                   // next rank 180: the wallet pays
        var (game, _) = ViewHarness.Attached(saved);
        using var view = ViewHarness.ViewOf(game);
        var cards = PageHarness.Open<LineagePage>(view, PageKind.Lineage).CardsShown.ToDictionary(c => c.Trait);

        var complete = cards[Trait.KeenNose];
        Assert.Equal((HatchlingText.TraitComplete, "Theme.Text.Good"), (complete.Bottom, complete.BottomKey));
        Assert.Equal(HatchlingText.Pips(2, 2), complete.Pips);

        var affordable = cards[Trait.DeepStomach];
        Assert.Equal((HatchlingText.TraitPrice(180), "Theme.Text.Highlight"), (affordable.Bottom, affordable.BottomKey));
        Assert.Equal("●●○○○", affordable.Pips);

        var unblocked = cards[Trait.QuickReflexes];   // 200 a tier below a complete Keen nose: the wallet's 300 pays it
        Assert.Equal((HatchlingText.TraitPrice(200), "Theme.Text.Highlight"), (unblocked.Bottom, unblocked.BottomKey));
        var tooDear = cards[Trait.SharpInstincts];  // 500 against 300
        Assert.Equal((HatchlingText.TraitPrice(500), "Theme.Text.Off"), (tooDear.Bottom, tooDear.BottomKey));
        Assert.Equal("Theme.Text.Bright", tooDear.NameKey);

        var needs = cards[Trait.SecondThoughts];
        Assert.Equal((HatchlingText.TraitNeeds(Trait.SharpInstincts), "Theme.Text.Dim"), (needs.Bottom, needs.BottomKey));
        Assert.Equal("Theme.Text.Dim", needs.NameKey);
        Assert.Equal("Needs Sharp instincts first", needs.Bottom);
    });

    [Fact]
    public void AClickBuysExactlyOneRankAndTheWalletFollows() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(WithWallet(1_000));
        using var view = ViewHarness.ViewOf(game);
        var page = PageHarness.Open<LineagePage>(view, PageKind.Lineage);
        var writes = Writes(host);
        page.CardPressed(Trait.DeepStomach);
        page.CardReleased(Trait.DeepStomach);
        Assert.Equal(writes + 1, Writes(host));
        Assert.Equal(1, game.State.Rank(Trait.DeepStomach));
        Assert.Equal(940, game.State.Wallet.Balance);
        Assert.Equal(HatchlingText.Money(940), view.WalletShown);
        var card = page.CardsShown.Single(c => c.Trait == Trait.DeepStomach);
        Assert.Equal(HatchlingText.Pips(1, 5), card.Pips);
        Assert.Equal(HatchlingText.TraitPrice(120), card.Bottom);
        // The one under it no longer needs it.
        Assert.Equal(HatchlingText.TraitPrice(200), page.CardsShown.Single(c => c.Trait == Trait.ThickHide).Bottom);

        page.CardPressed(Trait.DeepStomach);
        page.CardReleased(Trait.DeepStomach);
        Assert.Equal(2, game.State.Rank(Trait.DeepStomach));
        Assert.Equal(820, game.State.Wallet.Balance);
        Assert.Equal(HatchlingText.Money(820), view.WalletShown);
        Assert.Empty(host.Faults);
    });

    [Fact]
    public void ACardThatCantBeBoughtDoesNothingWhenClicked() => Sta.Run(() =>
    {
        var saved = WithWallet(100);
        saved.Ranks[Trait.SharpInstincts] = 1;
        var (game, host) = ViewHarness.Attached(saved);
        using var view = ViewHarness.ViewOf(game);
        var page = PageHarness.Open<LineagePage>(view, PageKind.Lineage);
        var writes = Writes(host);
        foreach (var trait in new[] { Trait.SharpInstincts, Trait.ThickHide, Trait.SecondThoughts, Trait.EscapeArtist })
        {
            page.CardEntered(trait);
            Assert.Equal("Theme.Card.Line", page.CardsShown.Single(c => c.Trait == trait).LineKey);   // no hover line: not a click
            page.CardPressed(trait);
            page.CardReleased(trait);
            page.CardLeft(trait);
        }
        Assert.Equal(writes, Writes(host));
        Assert.Equal(100, game.State.Wallet.Balance);
        // One it can buy wears the hover line.
        page.CardEntered(Trait.DeepStomach);
        Assert.Equal("Theme.Card.HoverLine", page.CardsShown.Single(c => c.Trait == Trait.DeepStomach).LineKey);
    });

    [Fact]
    public void APurchaseIsAPressAndAReleaseOnTheSameCard() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(WithWallet(1_000));
        using var view = ViewHarness.ViewOf(game);
        var page = PageHarness.Open<LineagePage>(view, PageKind.Lineage);
        var writes = Writes(host);
        page.CardReleased(Trait.DeepStomach);
        page.CardPressed(Trait.DeepStomach);
        page.CardReleased(Trait.KeenNose);
        page.CardReleased(Trait.DeepStomach);
        page.CardPressed(Trait.KeenNose);
        page.CardLeft(Trait.KeenNose);
        page.CardReleased(Trait.KeenNose);
        Assert.Equal(writes, Writes(host));
    });

    /// <summary>
    /// H85: a reading that changes the state redraws the page shown about every 3 s
    /// while the player's dino grows, and a press and a release straddling one keep
    /// the purchase (the release still asks the game: <c>CanBuy</c>, then <c>BuyTrait</c>).
    /// </summary>
    [Fact]
    public void APressSurvivesAReadingThatRedrawsThePage() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(WithWallet(100));   // Deep stomach's first rank
        using var view = ViewHarness.ViewOf(game);
        var page = PageHarness.Open<LineagePage>(view, PageKind.Lineage);
        var before = game.State.Wallet.Balance;

        page.CardPressed(Trait.DeepStomach);
        game.OnReading(new DinoReading(true, "Dryosaurus", "Male", 0.300));
        host.ManualClock.Advance(TimeSpan.FromSeconds(30));
        game.OnReading(new DinoReading(true, "Dryosaurus", "Male", 0.310));
        Assert.True(game.State.Wallet.Balance > before, $"{game.State.Wallet.Balance} after {before}");
        Assert.Equal(HatchlingText.Money(game.State.Wallet.Balance), view.WalletShown);   // the view redrew
        Assert.Equal(PageKind.Lineage, view.Shown);

        page.CardReleased(Trait.DeepStomach);
        Assert.Equal(1, game.State.Rank(Trait.DeepStomach));
        Assert.Empty(host.Faults);
    });

    [Fact]
    public void ATraitBoughtDuringARunLeavesTheRunAlone() => Sta.Run(() =>
    {
        var saved = ViewHarness.Running();
        saved.Wallet = new Wallet(1_000, 1_000);
        var (game, _) = ViewHarness.Attached(saved);
        using var view = ViewHarness.ViewOf(game);
        var page = PageHarness.Open<LineagePage>(view, PageKind.Lineage);
        var before = new Dictionary<Trait, int>(game.State.Run!.Ranks);
        page.CardPressed(Trait.KeenNose);
        page.CardReleased(Trait.KeenNose);
        Assert.Equal(1, game.State.Rank(Trait.KeenNose));
        Assert.Equal(before, game.State.Run!.Ranks);   // H19: it counts from the next run
        Assert.Equal(LoopRate.Off, view.TheLoop.Rate);
    });

    [Fact]
    public void TheLayoutStatesShowWhatThePlanNames() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(ViewHarness.Running());
        using var view = ViewHarness.ViewOf(game);

        view.ShowLayoutState("lineage-fresh");
        var page = PageHarness.Shown<LineagePage>(view);
        var cards = page.CardsShown;
        Assert.All(cards.Where(c => Lineage.Of(c.Trait).Tier == 1), c => Assert.Equal("Theme.Text.Off", c.BottomKey));
        Assert.All(cards.Where(c => Lineage.Of(c.Trait).Tier > 1), c => Assert.Equal("Theme.Text.Dim", c.BottomKey));
        Assert.Equal(HatchlingText.Money(0), view.WalletShown);

        view.ShowLayoutState("lineage-mid");
        var keys = page.CardsShown.Select(c => c.BottomKey).ToHashSet();
        Assert.Equal(new HashSet<string> { "Theme.Text.Good", "Theme.Text.Highlight", "Theme.Text.Off", "Theme.Text.Dim" }, keys);

        view.ShowLayoutState("lineage-full");
        Assert.All(page.CardsShown, c => Assert.Equal(HatchlingText.TraitComplete, c.Bottom));

        // A sample takes no click.
        var writes = Writes(host);
        view.ShowLayoutState("lineage-mid");
        page.CardPressed(Trait.DeepStomach);
        page.CardReleased(Trait.DeepStomach);
        Assert.Equal(writes, Writes(host));
    });
}

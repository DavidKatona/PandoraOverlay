using PandoraOverlay.Games.Hatchling;
using Xunit;
using static PandoraOverlay.Games.Tests.ProgressSamples;

namespace PandoraOverlay.Games.Tests;

/// <summary>Buying the lineage (H11, H19): prices, the tier rule, and the two achievements it earns.</summary>
public class LineageRulesTests
{
    /// <summary>The plan's table, written out here: ranks and the tier's base, trait by trait (by the traits' save ids: the enum is the game's own).</summary>
    public static TheoryData<string, int, int> ThePlansTable() => new()
    {
        { "stomach", 5, 60 }, { "hide", 4, 200 }, { "dry", 3, 800 }, { "wind", 1, 4000 },
        { "nose", 2, 60 }, { "reflexes", 4, 200 }, { "step", 2, 800 }, { "escape", 1, 4000 },
        { "metabolism", 5, 60 }, { "start", 3, 200 }, { "legacy", 3, 800 }, { "patience", 1, 4000 },
        { "sharp", 1, 500 }, { "thoughts", 1, 1500 }, { "bloomer", 1, 3000 }, { "ready", 1, 6000 },
    };

    [Fact]
    public void ThePlansTableNamesEveryTraitOnce()
    {
        var ids = ThePlansTable().Select(row => (string)row[0]).ToList();
        Assert.Equal(Lineage.All.Select(t => t.Id), ids);
    }

    [Theory]
    [MemberData(nameof(ThePlansTable))]
    public void EveryRanksPriceIsTheTiersBaseTimesTheRankAndACompleteTraitCostsNothing(string id, int ranks, int price)
    {
        Assert.True(Lineage.TryParse(id, out var trait));
        Assert.Equal(ranks, Lineage.Of(trait).MaxRank);
        var state = GameState.Fresh(Moment);
        for (var rank = 0; rank < ranks; rank++)
        {
            state.Ranks[trait] = rank;
            Assert.Equal(price * (rank + 1), LineageRules.PriceOf(state, trait));
        }
        state.Ranks[trait] = ranks;
        Assert.Equal(0, LineageRules.PriceOf(state, trait));
    }

    [Fact]
    public void TheWholeLineageIsThirtyEightRanksForTheWholePrice()
    {
        var table = ThePlansTable().Select(row => ((int)row[1], (int)row[2])).ToList();
        Assert.Equal(38, table.Sum(r => r.Item1));
        Assert.Equal(42180, table.Sum(r => Enumerable.Range(1, r.Item1).Sum(k => r.Item2 * k)));
    }

    [Fact]
    public void ASecondTierIsBlockedUntilTheFirstHasARank()
    {
        var state = WithPay(100_000);
        Assert.False(LineageRules.Blocked(state, Trait.DeepStomach));   // a first tier never is
        Assert.True(LineageRules.Blocked(state, Trait.ThickHide));
        Assert.False(LineageRules.CanBuy(state, Trait.ThickHide));
        Assert.Null(Unchanged(state, s => LineageRules.Buy(s, Trait.ThickHide)));
        Assert.True(LineageRules.Blocked(state, Trait.BornReady));

        var next = LineageRules.Buy(state, Trait.DeepStomach)!;
        Assert.False(LineageRules.Blocked(next, Trait.ThickHide));
        Assert.True(LineageRules.Blocked(next, Trait.DrySpells));   // its own tier above is still at 0
        Assert.NotNull(LineageRules.Buy(next, Trait.ThickHide));
    }

    [Fact]
    public void ACompleteTraitCanNotBeBought()
    {
        var state = WithPay(100_000);
        state.Ranks[Trait.SharpInstincts] = 1;
        Assert.False(LineageRules.CanBuy(state, Trait.SharpInstincts));
        Assert.Null(Unchanged(state, s => LineageRules.Buy(s, Trait.SharpInstincts)));
    }

    [Fact]
    public void ShortOfPayCanNotBuyAndExactlyThePriceCan()
    {
        var shortOf = WithPay(59);
        Assert.False(LineageRules.CanBuy(shortOf, Trait.DeepStomach));
        Assert.Null(Unchanged(shortOf, s => LineageRules.Buy(s, Trait.DeepStomach)));

        var exactly = WithPay(60);
        Assert.True(LineageRules.CanBuy(exactly, Trait.DeepStomach));
        var next = Unchanged(exactly, s => LineageRules.Buy(s, Trait.DeepStomach))!;
        Assert.Equal(0, next.Wallet.Balance);
    }

    [Fact]
    public void BuyingSpendsExactlyThePriceAndRaisesTheRankByOne()
    {
        var state = WithPay(1000);
        state.Ranks[Trait.DeepStomach] = 2;
        state.Totals.RanksBought = 2;

        var next = Unchanged(state, s => LineageRules.Buy(s, Trait.DeepStomach))!;
        Assert.Equal(1000 - 180, next.Wallet.Balance);
        Assert.Equal(1000, next.Wallet.Earned);   // spending is not earning back
        Assert.Equal(3, next.Rank(Trait.DeepStomach));
        Assert.Equal(3, next.Totals.RanksBought);
        Assert.Empty(next.Achievements.Unlocked);
    }

    [Fact]
    public void BuyingAllThirtyEightRanksSpendsTheWholePriceAndGrantsRootsAtTheTenthAndBloodAtTheLast()
    {
        var state = WithPay(42180);
        var bought = 0;
        while (true)
        {
            var trait = Lineage.All.Select(t => t.Trait).FirstOrDefault(t => LineageRules.CanBuy(state, t), (Trait)(-1));
            if ((int)trait < 0) break;
            state = LineageRules.Buy(state, trait)!;
            bought++;
            Assert.Equal(bought >= 10, state.Achievements.Has(AchievementList.Roots));
            Assert.Equal(bought == 38, state.Achievements.Has(AchievementList.Blood));
        }

        Assert.Equal(38, bought);
        Assert.True(LineageRules.Complete(state));
        Assert.All(Lineage.All, t => Assert.Equal(t.MaxRank, state.Rank(t.Trait)));
        // Every rank's price was spent: what is left is exactly what Deep roots paid
        // (Ancient blood pays nothing), and nothing else came in.
        Assert.Equal(PayOf(AchievementList.Roots), state.Wallet.Balance);
        Assert.Equal(0, PayOf(AchievementList.Blood));
        Assert.Equal(42180 + PayOf(AchievementList.Roots), state.Wallet.Earned);
        Assert.Equal(new[] { AchievementList.Roots, AchievementList.Blood }, state.Achievements.Unlocked);
    }

    [Fact]
    public void ARunUnderWayIsUntouchedByAPurchase()
    {
        var state = WithPay(1000);
        state.Ranks[Trait.DeepStomach] = 1;
        state.Run = new RunState { Species = "Dryosaurus", Turn = 12 };
        state.Run.Ranks[Trait.DeepStomach] = 1;
        var runBefore = Fingerprint(state.Run);

        var next = LineageRules.Buy(state, Trait.DeepStomach)!;
        Assert.Equal(2, next.Rank(Trait.DeepStomach));
        Assert.Equal(1, next.Run!.Rank(Trait.DeepStomach));   // H19: the run keeps the ranks it began with
        Assert.Equal(runBefore, Fingerprint(next.Run));
    }
}

using PandoraOverlay.Games.Hatchling;
using Xunit;
using static PandoraOverlay.Games.Tests.ProgressSamples;

namespace PandoraOverlay.Games.Tests;

/// <summary>Unlocking a species (H10): the price that rises and stops, buying, granting, and "six" and "island".</summary>
public class UnlocksTests
{
    [Fact]
    public void ThePriceIsFourHundredAndAHundredMoreEachTimeAndNeverAboveFifteenHundred()
    {
        var state = GameState.Fresh(Moment);
        var prices = new List<int>();
        for (var bought = 0; bought <= 14; bought++)
        {
            state.Bought = bought;
            prices.Add(Unlocks.PriceNow(state));
        }
        Assert.Equal(new[] { 400, 500, 600, 700, 800, 900, 1000, 1100, 1200, 1300, 1400, 1500, 1500, 1500, 1500 }, prices);

        state.Bought = int.MaxValue;   // no overflow, however many
        Assert.Equal(1500, Unlocks.PriceNow(state));
    }

    [Fact]
    public void BuyingASpeciesUnlocksItAndSpendsThePriceAndRaisesTheNext()
    {
        var state = WithPay(1000);
        var next = Unchanged(state, s => Unlocks.Buy(s, "maiasaura"))!;

        Assert.Equal(new[] { "Dryosaurus", "Herrerasaurus", "Maiasaura" }, next.Unlocked);   // the table's spelling (H36)
        Assert.Equal(600, next.Wallet.Balance);
        Assert.Equal(1, next.Bought);
        Assert.Equal(500, Unlocks.PriceNow(next));
        Assert.True(Unlocks.IsUnlocked(next, "Maiasaura"));
    }

    [Fact]
    public void BuyingIsRefusedWhenUnlockedAlreadyUnknownOrShort()
    {
        var rich = WithPay(10_000);
        Assert.Null(Unchanged(rich, s => Unlocks.Buy(s, "Dryosaurus")));
        Assert.Null(Unchanged(rich, s => Unlocks.Buy(s, "DRYOSAURUS")));
        Assert.Null(Unchanged(rich, s => Unlocks.Buy(s, "Velociraptor")));
        Assert.Null(Unchanged(rich, s => Unlocks.Buy(s, "")));

        var shortOf = WithPay(399);
        Assert.Null(Unchanged(shortOf, s => Unlocks.Buy(s, "Maiasaura")));
        Assert.NotNull(Unlocks.Buy(WithPay(400), "Maiasaura"));
    }

    [Fact]
    public void AGrantCostsNothingAndDoesNotMoveThePrice()
    {
        var state = WithPay(1000);
        Unlocks.Grant(state, "triceratops");
        Assert.Contains("Triceratops", state.Unlocked);
        Assert.Equal(1000, state.Wallet.Balance);
        Assert.Equal(0, state.Bought);
        Assert.Equal(400, Unlocks.PriceNow(state));
    }

    [Fact]
    public void AGrantOfAnUnlockedOrUnknownSpeciesDoesNothing()
    {
        var state = GameState.Fresh(Moment);
        var before = Fingerprint(state);
        Unlocks.Grant(state, "Dryosaurus");
        Unlocks.Grant(state, "herrerasaurus");
        Unlocks.Grant(state, "Velociraptor");
        Assert.Equal(before, Fingerprint(state));
    }

    [Fact]
    public void SixUnlockedEarnsSixOnceAndAllTwentyOneEarnIslandOnce()
    {
        var state = GameState.Fresh(Moment);
        var others = SpeciesTable.All.Select(s => s.Name).Where(n => !SpeciesTable.Starters.Contains(n)).ToList();
        for (var i = 0; i < others.Count; i++)
        {
            Unlocks.Grant(state, others[i]);
            var count = SpeciesTable.Starters.Count + i + 1;
            Assert.Equal(count >= 6, state.Achievements.Has(AchievementList.Six));
            Assert.Equal(count == 21, state.Achievements.Has(AchievementList.Island));
        }

        Assert.Equal(21, state.Unlocked.Count);
        Assert.Equal(new[] { AchievementList.Six, AchievementList.Island }, state.Achievements.Unlocked);
        Assert.Equal(PayOf(AchievementList.Six) + PayOf(AchievementList.Island), state.Wallet.Earned);   // each paid once
    }

    [Fact]
    public void ABoughtSpeciesCountsTowardSixToo()
    {
        var state = WithPay(10_000);
        foreach (var name in new[] { "Maiasaura", "Troodon", "Stegosaurus" }) Unlocks.Grant(state, name);
        Assert.False(state.Achievements.Has(AchievementList.Six));

        var next = Unlocks.Buy(state, "Allosaurus")!;
        Assert.True(next.Achievements.Has(AchievementList.Six));
        Assert.Equal(10_000 - 400 + PayOf(AchievementList.Six), next.Wallet.Balance);
    }
}

using PandoraOverlay.Games.Hatchling;
using Xunit;
using static PandoraOverlay.Games.Tests.ProgressSamples;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// Settling a run (H8, H15, H26): the pay banked once, the totals and the record,
/// the next danger, the seed, and each run achievement granted by a run built for
/// it and NOT by its near miss.
/// </summary>
public class SettleTests
{
    private static GameState Settled(GameState state) => Unchanged(state, Settle.Run);

    private static bool Earns(GameState ended, string id)
    {
        var next = Settled(ended);
        Assert.Equal(next.Achievements.Has(id), next.Run!.Over!.Earned.Contains(id));
        return next.Achievements.Has(id);
    }

    // ---- The pay, once ---------------------------------------------------------------------

    [Fact]
    public void ALostRunBanksItsTotalOnce()
    {
        var ended = Ended(total: 45, best: 55, start: 0);
        var once = Settled(ended);
        var pay = 45 + PayOf(AchievementList.First) + PayOf(AchievementList.Juvenile);
        Assert.Equal(pay, once.Wallet.Balance);
        Assert.Equal(pay, once.Wallet.Earned);
        Assert.True(once.Run!.Over!.Settled);

        var twice = Settled(once);
        Assert.Equal(Fingerprint(once), Fingerprint(twice));
    }

    [Fact]
    public void ARunUnderWayOrNoRunIsNotSettled()
    {
        var camp = GameState.Fresh(Moment);
        Assert.Equal(Fingerprint(camp), Fingerprint(Settled(camp)));

        var underWay = GameState.Fresh(Moment);
        underWay.Run = new RunState { Species = "Dryosaurus", Turn = 3 };
        Assert.Equal(Fingerprint(underWay), Fingerprint(Settled(underWay)));
    }

    [Fact]
    public void ARunLeftAtItsFirstTurnPaysOnlyItsTotalAndNoStage()
    {
        var left = Ended(reason: EndReason.Left, start: 30, best: 30, total: 0);
        var next = Settled(left);
        Assert.Equal(new[] { AchievementList.First }, next.Run!.Over!.Earned);
        Assert.Equal(PayOf(AchievementList.First), next.Wallet.Earned);
    }

    [Fact]
    public void TheSeedMovesByTheStepAndWraps()
    {
        var ended = Ended();
        var seed = ended.NextSeed;
        Assert.Equal(unchecked(seed + Tuning.SeedStep), Settled(ended).NextSeed);

        ended.NextSeed = uint.MaxValue;
        Assert.Equal(Tuning.SeedStep - 1, Settled(ended).NextSeed);
    }

    // ---- The totals and the record ---------------------------------------------------------

    [Fact]
    public void ALossCountsTheRunAndItsKillsAndTheBestGrowth()
    {
        var ended = Ended("Maiasaura", start: 10, best: 62, kills: 2);
        ended.Species["Maiasaura"] = new SpeciesRecord { Runs = 3, BestGrowth = 70, Grown = 5 };
        var next = Settled(ended);

        Assert.Equal(1, next.Totals.Runs);
        Assert.Equal(0, next.Totals.Wins);
        Assert.Equal(2, next.Totals.Kills);
        Assert.Empty(next.Totals.BuildsWon);
        Assert.Empty(next.Totals.SpeciesWon);
        var record = next.Record("Maiasaura");
        Assert.Equal(4, record.Runs);
        Assert.Equal(70, record.BestGrowth);   // the best stays the best
        Assert.Equal(0, record.WonAt);
        Assert.Equal(5, record.Grown);         // a run is not server growth
        Assert.Equal(1, next.MaxDanger);
        Assert.Equal(0, next.Run!.Over!.DangerUnlocked);
    }

    [Fact]
    public void AFirstRunOfASpeciesStoresItsRecord()
    {
        var next = Settled(Ended("maiasaura", best: 33));
        Assert.True(next.Species.ContainsKey("Maiasaura"));   // the table's spelling (H36)
        Assert.Equal(1, next.Record("Maiasaura").Runs);
        Assert.Equal(33, next.Record("Maiasaura").BestGrowth);
    }

    [Fact]
    public void AWinCountsTheBuildTheSpeciesAndTheDangerWonAt()
    {
        var ended = Ended("Stegosaurus", danger: 2, won: true, best: 100);
        ended.Species["Stegosaurus"] = new SpeciesRecord { WonAt = 1 };
        var next = Settled(ended);

        Assert.Equal(1, next.Totals.Wins);
        Assert.Equal(new[] { Build.Tank }, next.Totals.BuildsWon);
        Assert.Equal(new[] { "Stegosaurus" }, next.Totals.SpeciesWon);
        Assert.Equal(2, next.Record("Stegosaurus").WonAt);

        var lower = Ended("Stegosaurus", danger: 1, won: true, best: 100);
        lower.Species["Stegosaurus"] = new SpeciesRecord { WonAt = 3 };
        lower.MaxDanger = 4;
        Assert.Equal(3, Settled(lower).Record("Stegosaurus").WonAt);   // the highest stays
    }

    // ---- The next danger (H15) -------------------------------------------------------------

    [Fact]
    public void AWinAtDangerTwoUnlocksThreeAndSaysSo()
    {
        var next = Settled(Ended(danger: 2, won: true, best: 100));
        Assert.Equal(3, next.MaxDanger);
        Assert.Equal(3, next.Run!.Over!.DangerUnlocked);
    }

    [Fact]
    public void AWinAtDangerFiveLeavesItAndSaysNothing()
    {
        var next = Settled(Ended(danger: 5, won: true, best: 100));
        Assert.Equal(5, next.MaxDanger);
        Assert.Equal(0, next.Run!.Over!.DangerUnlocked);
    }

    [Fact]
    public void AWinAtDangerOneWithThreeUnlockedSaysNothing()
    {
        var ended = Ended(danger: 1, won: true, best: 100);
        ended.MaxDanger = 3;
        var next = Settled(ended);
        Assert.Equal(3, next.MaxDanger);
        Assert.Equal(0, next.Run!.Over!.DangerUnlocked);
    }

    [Fact]
    public void ALossUnlocksNoDanger()
    {
        var next = Settled(Ended(danger: 1, best: 99));
        Assert.Equal(1, next.MaxDanger);
        Assert.Equal(0, next.Run!.Over!.DangerUnlocked);
    }

    // ---- Each run achievement, and its near miss -------------------------------------------

    [Fact]
    public void FirstIsAnyRunSettled() => Assert.True(Earns(Ended(), AchievementList.First));

    [Theory]
    [InlineData(0, 25, true)]
    [InlineData(20, 30, true)]
    [InlineData(24.9, 25, true)]
    [InlineData(30, 40, false)]   // grown past it, but not IN this run (H8)
    [InlineData(25, 60, false)]   // began on the line: already past it
    [InlineData(0, 24.9, false)]
    public void JuvenileOnlyForTheLineCrossedInTheRun(double start, double best, bool earned) =>
        Assert.Equal(earned, Earns(Ended(start: start, best: best), AchievementList.Juvenile));

    [Theory]
    [InlineData(50, 75, true)]
    [InlineData(74, 76, true)]
    [InlineData(75, 90, false)]
    [InlineData(0, 74.9, false)]
    public void AdultOnlyForTheLineCrossedInTheRun(double start, double best, bool earned) =>
        Assert.Equal(earned, Earns(Ended(start: start, best: best), AchievementList.Adult));

    [Theory]
    [InlineData(60, 100, true)]
    [InlineData(0, 99.9, false)]
    [InlineData(100, 100, false)]
    public void ElderOnlyForTheLineCrossedInTheRun(double start, double best, bool earned) =>
        Assert.Equal(earned, Earns(Ended(start: start, best: best), AchievementList.Elder));

    [Fact]
    public void WinIsAWonRunAndNotALostOne()
    {
        Assert.True(Earns(Ended(won: true, best: 100), AchievementList.Win));
        Assert.False(Earns(Ended(won: false, best: 100), AchievementList.Win));
    }

    [Fact]
    public void SlayerIsTheApexDefeatedAndNotOutlasted()
    {
        Assert.True(Earns(Ended(won: true, reason: EndReason.DefeatedApex, best: 100), AchievementList.Slayer));
        Assert.False(Earns(Ended(won: true, reason: EndReason.Outlasted, best: 100), AchievementList.Slayer));
    }

    [Fact]
    public void HunterIsTwentyFiveDefeatedOverAllRuns()
    {
        var ended = Ended(kills: 3);
        ended.Totals.Kills = 21;
        Assert.False(Earns(ended, AchievementList.Hunter));   // 24

        ended.Totals.Kills = 22;
        Assert.True(Earns(ended, AchievementList.Hunter));    // 25
    }

    [Theory]
    [InlineData("Dryosaurus", true)]      // Runner
    [InlineData("Gallimimus", true)]      // Forager
    [InlineData("Herrerasaurus", false)]  // Hunter
    [InlineData("Stegosaurus", false)]    // Tank
    public void UnderdogIsAWinAsARunnerOrAForager(string species, bool earned)
    {
        Assert.Equal(earned, Earns(Ended(species, won: true, best: 100), AchievementList.Underdog));
        Assert.False(Earns(Ended(species, won: false, best: 100), AchievementList.Underdog));
    }

    [Theory]
    [InlineData("Stegosaurus", true)]     // Tank
    [InlineData("Triceratops", true)]     // Tank
    [InlineData("Tyrannosaurus", true)]   // Apex
    [InlineData("Allosaurus", false)]     // Stalker
    public void HeavyIsAWinAsATankOrAnApex(string species, bool earned)
    {
        Assert.Equal(earned, Earns(Ended(species, won: true, best: 100), AchievementList.Heavy));
        Assert.False(Earns(Ended(species, won: false, best: 100), AchievementList.Heavy));
    }

    [Fact]
    public void ShapesIsAWinWithEachOfTheNineBuilds()
    {
        var state = GameState.Fresh(Moment);
        var species = OnePerBuild();
        Assert.Equal(9, species.Count);
        for (var i = 0; i < species.Count; i++)
        {
            state.Run = EndedRun(species[i], won: true, best: 100);
            state = Settle.Run(state);
            Assert.Equal(i == species.Count - 1, state.Run!.Over!.Earned.Contains(AchievementList.Shapes));
            state = Settle.BackToCamp(state);
        }
        Assert.True(state.Achievements.Has(AchievementList.Shapes));

        // Nine wins with eight builds are not every shape.
        var eight = GameState.Fresh(Moment);
        foreach (var name in species.Take(8).Append(species[0]))
        {
            eight.Run = EndedRun(name, won: true, best: 100);
            eight = Settle.BackToCamp(Settle.Run(eight));
        }
        Assert.False(eight.Achievements.Has(AchievementList.Shapes));
    }

    [Theory]
    [InlineData(3, true, true)]
    [InlineData(4, true, true)]
    [InlineData(2, true, false)]
    [InlineData(3, false, false)]
    public void Danger3IsAWinAtDangerThree(int danger, bool won, bool earned) =>
        Assert.Equal(earned, Earns(Ended(danger: danger, won: won, best: 100), AchievementList.Danger3));

    [Theory]
    [InlineData(5, true, true)]
    [InlineData(4, true, false)]
    [InlineData(5, false, false)]
    public void Danger5IsAWinAtDangerFive(int danger, bool won, bool earned) =>
        Assert.Equal(earned, Earns(Ended(danger: danger, won: won, best: 100), AchievementList.Danger5));

    [Fact]
    public void UnseenIsAdultCrossedInTheRunWithoutABite()
    {
        Assert.True(Earns(Ended(start: 0, best: 80, bitten: false), AchievementList.Unseen));
        Assert.False(Earns(Ended(start: 0, best: 80, bitten: true), AchievementList.Unseen));
        Assert.False(Earns(Ended(start: 0, best: 74, bitten: false), AchievementList.Unseen));
        Assert.False(Earns(Ended(start: 75, best: 100, bitten: false), AchievementList.Unseen));   // not crossed in the run
    }

    [Fact]
    public void GentleIsAWinWithNoPredatorDefeated()
    {
        Assert.True(Earns(Ended(won: true, kills: 0, best: 100), AchievementList.Gentle));
        Assert.False(Earns(Ended(won: true, kills: 1, best: 100), AchievementList.Gentle));
        Assert.False(Earns(Ended(won: false, kills: 0, best: 100), AchievementList.Gentle));
    }

    [Fact]
    public void AllIsAWinWithEverySpecies()
    {
        var state = GameState.Fresh(Moment);
        var all = SpeciesTable.All.Select(s => s.Name).ToList();
        for (var i = 0; i < all.Count; i++)
        {
            state.Run = EndedRun(all[i], won: true, best: 100);
            state = Settle.Run(state);
            Assert.Equal(i == all.Count - 1, state.Run!.Over!.Earned.Contains(AchievementList.All21));
            state = Settle.BackToCamp(state);
        }
        Assert.Equal(21, state.Totals.SpeciesWon.Count);
        Assert.True(state.Achievements.Has(AchievementList.All21));
    }

    // ---- What the achievements pay ---------------------------------------------------------

    [Fact]
    public void AFreshWinPaysItsTotalAndItsAchievementsInTheTablesOrder()
    {
        var ended = Ended("Dryosaurus", won: true, reason: EndReason.Outlasted, start: 0, best: 100, kills: 0, total: 135);
        var next = Settled(ended);

        var expected = new[]
        {
            AchievementList.First, AchievementList.Juvenile, AchievementList.Adult, AchievementList.Elder,
            AchievementList.Win, AchievementList.Underdog, AchievementList.Unseen, AchievementList.Gentle,
        };
        Assert.Equal(expected, next.Run!.Over!.Earned);
        Assert.Equal(expected, next.Achievements.Unlocked);
        var pay = 135 + expected.Sum(PayOf);
        Assert.Equal(135 + 540, pay);
        Assert.Equal(pay, next.Wallet.Balance);
        Assert.Equal(pay, next.Wallet.Earned);
        Assert.Equal(135, next.Run.Over.Total);   // the run's own total is its pay lines', unchanged
    }

    [Fact]
    public void AnAchievementAlreadyHadIsNotListedOrPaidAgain()
    {
        var first = Settle.BackToCamp(Settle.Run(Ended(best: 30, total: 15)));
        var earnedBefore = first.Wallet.Earned;

        first.Run = EndedRun(best: 30, total: 15);
        var second = Settled(first);
        Assert.Empty(second.Run!.Over!.Earned);
        Assert.Equal(earnedBefore + 15, second.Wallet.Earned);
    }

    // ---- Back to camp ----------------------------------------------------------------------

    [Fact]
    public void BackToCampForgetsTheEndedRunAndKeepsEverythingElse()
    {
        var settled = Settle.Run(Ended(total: 20));
        var camp = Unchanged(settled, Settle.BackToCamp);
        Assert.Null(camp.Run);
        Assert.Equal(settled.Wallet.Balance, camp.Wallet.Balance);
        Assert.Equal(settled.Totals.Runs, camp.Totals.Runs);
        Assert.True(Camp.CanStart(camp));
    }

    [Fact]
    public void BackToCampWithNoRunOrARunUnderWayChangesNothing()
    {
        var camp = GameState.Fresh(Moment);
        Assert.Equal(Fingerprint(camp), Fingerprint(Unchanged(camp, Settle.BackToCamp)));

        var underWay = GameState.Fresh(Moment);
        underWay.Run = new RunState { Species = "Dryosaurus", Turn = 5 };
        var next = Unchanged(underWay, Settle.BackToCamp);
        Assert.NotNull(next.Run);
        Assert.Equal(Fingerprint(underWay), Fingerprint(next));
    }

    [Fact]
    public void BackToCampFromAnEndedRunNotYetSettledBanksItFirst()
    {
        var camp = Unchanged(Ended(total: 20), Settle.BackToCamp);
        Assert.Null(camp.Run);
        Assert.Equal(1, camp.Totals.Runs);
        Assert.Equal(20 + PayOf(AchievementList.First), camp.Wallet.Earned);
    }
}

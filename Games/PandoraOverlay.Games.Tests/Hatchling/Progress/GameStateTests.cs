using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;
using Xunit;
using static PandoraOverlay.Games.Tests.ProgressSamples;

namespace PandoraOverlay.Games.Tests;

/// <summary>What is kept between runs: a fresh save (H25), the deep copy, and the reads every screen uses.</summary>
public class GameStateTests
{
    // ---- A fresh save (H25) ----------------------------------------------------------------

    [Fact]
    public void AFreshSaveHasTheTwoStartersDryosaurusChosenAndDangerOneOfOne()
    {
        var state = GameState.Fresh(Moment);

        Assert.Equal(new[] { "Dryosaurus", "Herrerasaurus" }, state.Unlocked);
        Assert.Equal("Dryosaurus", state.Selected);
        Assert.Equal(1, state.Danger);
        Assert.Equal(1, state.MaxDanger);
        Assert.False(state.Fast);
        Assert.Equal(0, state.Wallet.Balance);
        Assert.Equal(0, state.Wallet.Earned);
        Assert.Equal(0, state.Carry);
        Assert.Equal(0, state.Bought);
        Assert.Empty(state.Ranks);
        Assert.Empty(state.Species);
        Assert.Empty(state.Achievements.Unlocked);
        Assert.Equal(24, state.Achievements.All.Count);
        Assert.Equal(Moment, state.FreshAsOf);
        Assert.Null(state.Run);
        Assert.Empty(state.Lately);
        Assert.Equal(0, state.Journal.Done);
        Assert.Equal(Tuning.JournalEntries, state.Journal.Open.Count);
        Assert.True(state.IsFresh);
    }

    [Fact]
    public void AFreshSavesSeedsAreMixedFromTheCalendar()
    {
        var ticks = (ulong)Moment.Ticks;
        var seed0 = unchecked((uint)ticks ^ (uint)(ticks >> 32));
        Assert.Equal(seed0, GameState.Seed0(Moment));

        var state = GameState.Fresh(Moment);
        // The journal's stream starts at Mix(seed0, 1); a fresh save has drawn its
        // three entries from it (task H06), one roll each.
        var journal = new Rng(Rng.Mix(seed0, 1));
        for (var i = 0; i < Tuning.JournalEntries; i++) journal.NextDouble();
        Assert.Equal(journal.State, state.Journal.Rng);
        Assert.Equal(Rng.Mix(seed0, 2), state.NextSeed);
        Assert.NotEqual(state.Journal.Rng, state.NextSeed);   // two streams
    }

    [Fact]
    public void TheSameMomentGivesTheSameSeedsAndDifferentMomentsDifferentOnes()
    {
        Assert.Equal(GameState.Fresh(Moment).NextSeed, GameState.Fresh(Moment).NextSeed);
        Assert.Equal(GameState.Fresh(Moment).Journal.Rng, GameState.Fresh(Moment).Journal.Rng);

        Assert.NotEqual(GameState.Fresh(Moment).NextSeed, GameState.Fresh(Moment.AddTicks(1)).NextSeed);
        Assert.NotEqual(GameState.Fresh(Moment).NextSeed, GameState.Fresh(Moment.AddDays(1)).NextSeed);

        // A thousand players starting a second apart get a thousand first islands.
        var seeds = Enumerable.Range(0, 1000).Select(s => GameState.Fresh(Moment.AddSeconds(s)).NextSeed).ToHashSet();
        Assert.Equal(1000, seeds.Count);
    }

    // ---- The deep copy ---------------------------------------------------------------------

    /// <summary>A state with something in every member, run included.</summary>
    private static GameState Filled()
    {
        var state = GameState.Fresh(Moment);
        state.Wallet.Earn(1234);
        state.Wallet.TrySpend(34);
        state.Carry = 0.25;
        state.Ranks[Trait.DeepStomach] = 2;
        state.Ranks[Trait.KeenNose] = 1;
        state.Unlocked.Add("Maiasaura");
        state.Bought = 1;
        state.Selected = "Maiasaura";
        state.Danger = 2;
        state.MaxDanger = 3;
        state.Fast = true;
        state.Species["Maiasaura"] = new SpeciesRecord { Grown = 61.5, Fresh = 1.8, Adults = 1, Runs = 4, BestGrowth = 80, WonAt = 2 };
        state.Journal.Open.Add(new JournalEntry { Species = "Troodon", Target = 75, Reward = 400, Seen = 12.5 });
        state.Journal.Done = 2;
        state.Journal.Rng = 99;
        state.Achievements.Grant(AchievementList.First, state.Wallet);
        state.Totals.Runs = 4;
        state.Totals.Wins = 1;
        state.Totals.Kills = 7;
        state.Totals.BuildsWon.Add(Build.Grazer);
        state.Totals.SpeciesWon.Add("Maiasaura");
        state.Totals.RanksBought = 3;
        state.NextSeed = 4242;
        state.Lately.Add(new LatelyLine(LatelyKind.Grew, "Maiasaura", 3.2, 2.4, 31, 0));
        state.Run = EndedRun("Maiasaura", 2);
        state.Run.Ranks[Trait.DeepStomach] = 2;
        return state;
    }

    [Fact]
    public void ACloneEqualsTheStateAndSharesNothingWithIt()
    {
        var state = Filled();
        var before = Fingerprint(state);
        var copy = state.Clone();
        Assert.Equal(before, Fingerprint(copy));

        // Change everything in the copy.
        copy.Wallet.Earn(1);
        copy.Wallet.TrySpend(500);
        copy.Carry = 0.5;
        copy.Ranks[Trait.DeepStomach] = 3;
        copy.Ranks[Trait.ThickHide] = 1;
        copy.Unlocked.Add("Troodon");
        copy.Bought = 2;
        copy.Selected = "Troodon";
        copy.Danger = 3;
        copy.MaxDanger = 4;
        copy.Fast = false;
        copy.Species["Maiasaura"].Grown = 70;
        copy.Species["Maiasaura"].Fresh = 1.0;
        copy.Species["Maiasaura"].Adults = 2;
        copy.Species["Maiasaura"].Runs = 5;
        copy.Species["Maiasaura"].BestGrowth = 90;
        copy.Species["Maiasaura"].WonAt = 3;
        copy.Species["Troodon"] = new SpeciesRecord();
        copy.FreshAsOf = Moment.AddDays(3);
        copy.Journal.Open[0].Seen = 50;
        copy.Journal.Open[0].Species = "Allosaurus";
        copy.Journal.Open[0].Target = 50;
        copy.Journal.Open[0].Reward = 500;
        copy.Journal.Open.Add(new JournalEntry());
        copy.Journal.Done = 3;
        copy.Journal.Rng = 100;
        copy.Achievements.Grant(AchievementList.Juvenile, copy.Wallet);
        copy.Totals.Runs = 5;
        copy.Totals.Wins = 2;
        copy.Totals.Kills = 8;
        copy.Totals.BuildsWon.Add(Build.Tank);
        copy.Totals.SpeciesWon.Add("Stegosaurus");
        copy.Totals.RanksBought = 4;
        copy.NextSeed = 1;
        copy.Lately.Add(new LatelyLine(LatelyKind.Star, "Maiasaura", 0, 0, 0, 0));
        copy.Run!.Kills = 9;
        copy.Run.Ranks[Trait.DeepStomach] = 5;
        copy.Run.You.Best = 99;
        copy.Run.Over!.Earned.Add("x");
        copy.Run.Over.Settled = true;

        Assert.Equal(before, Fingerprint(state));
        Assert.NotSame(state.Wallet, copy.Wallet);
        Assert.NotSame(state.Achievements, copy.Achievements);
        Assert.False(state.Achievements.Has(AchievementList.Juvenile));
    }

    [Fact]
    public void ACloneOfACampStateHasNoRun()
    {
        var copy = GameState.Fresh(Moment).Clone();
        Assert.Null(copy.Run);
        Assert.Equal(Fingerprint(GameState.Fresh(Moment)), Fingerprint(copy));
    }

    // ---- The reads -------------------------------------------------------------------------

    [Fact]
    public void ASpeciesWithNoRecordReadsAsGrownNothingAndFreshAndIsNotStored()
    {
        var state = GameState.Fresh(Moment);
        var record = state.Record("Maiasaura");
        Assert.Equal(0, record.Grown);
        Assert.Equal(3.0, record.Fresh);
        Assert.Equal(0, record.Runs);
        Assert.Equal(0, record.WonAt);

        record.Grown = 40;   // a default record is nobody's
        Assert.Empty(state.Species);
        Assert.Equal(0, state.Record("Maiasaura").Grown);
    }

    [Fact]
    public void ARecordIsFoundIgnoringCase()
    {
        var state = GameState.Fresh(Moment);
        state.Species["Maiasaura"] = new SpeciesRecord { Grown = 12 };
        Assert.Equal(12, state.Record("maiasaura").Grown);
        Assert.Equal(12, state.Record("MAIASAURA").Grown);
        Assert.Equal(0, state.Record("Velociraptor").Grown);
    }

    [Fact]
    public void ARankMissingIsZero()
    {
        var state = GameState.Fresh(Moment);
        Assert.Equal(0, state.Rank(Trait.Legacy));
        state.Ranks[Trait.Legacy] = 2;
        Assert.Equal(2, state.Rank(Trait.Legacy));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(49.9, 0)]
    [InlineData(50, 1)]
    [InlineData(149.99, 1)]
    [InlineData(150, 2)]
    [InlineData(299, 2)]
    [InlineData(300, 3)]
    [InlineData(5000, 3)]
    public void StarsComeAtFiftyOneHundredFiftyAndThreeHundredPercent(double grown, int stars)
    {
        var state = GameState.Fresh(Moment);
        state.Species["Maiasaura"] = new SpeciesRecord { Grown = grown };
        Assert.Equal(stars, state.Stars("Maiasaura"));
    }

    [Fact]
    public void AStarIsReachedBySmallStepsThatSumToItsLine()
    {
        var grown = 0.0;
        for (var i = 0; i < 500; i++) grown += 0.1;   // 49.99999999999... in floating point
        Assert.NotEqual(50.0, grown);
        var state = GameState.Fresh(Moment);
        state.Species["Maiasaura"] = new SpeciesRecord { Grown = grown };
        Assert.Equal(1, state.Stars("Maiasaura"));
    }

    [Fact]
    public void ASpeciesNeverGrownHasNoStars()
    {
        Assert.Equal(0, GameState.Fresh(Moment).Stars("Triceratops"));
    }

    // ---- IsFresh ---------------------------------------------------------------------------

    public static TheoryData<string> Progress() => new()
    {
        "earned", "carry", "rank", "bought", "unlocked", "danger", "grown", "adult", "runs", "done", "seen",
        "achievement", "totals", "ranks bought", "lately", "run",
    };

    [Theory]
    [MemberData(nameof(Progress))]
    public void AnythingPlayedEarnedBoughtOrGrownIsNotFresh(string what)
    {
        var state = GameState.Fresh(Moment);
        switch (what)
        {
            case "earned": state.Wallet.Earn(1); break;
            case "carry": state.Carry = 0.3; break;
            case "rank": state.Ranks[Trait.DeepStomach] = 1; break;
            case "bought": state.Bought = 1; break;
            case "unlocked": state.Unlocked.Add("Maiasaura"); break;
            case "danger": state.MaxDanger = 2; break;
            case "grown": state.Species["Maiasaura"] = new SpeciesRecord { Grown = 0.1 }; break;
            case "adult": state.Species["Maiasaura"] = new SpeciesRecord { Adults = 1 }; break;
            case "runs": state.Species["Maiasaura"] = new SpeciesRecord { Runs = 1 }; break;
            case "done": state.Journal.Done = 1; break;
            case "seen": state.Journal.Open.Add(new JournalEntry { Species = "Troodon", Target = 75, Reward = 400, Seen = 1 }); break;
            case "achievement": state.Achievements.Grant(AchievementList.Blood, state.Wallet); break;
            case "totals": state.Totals.Runs = 1; break;
            case "ranks bought": state.Totals.RanksBought = 1; break;
            case "lately": state.Lately.Add(new LatelyLine(LatelyKind.Grew, "Maiasaura", 0.1, 3, 1, 0)); break;
            case "run": state.Run = new RunState { Species = "Dryosaurus" }; break;
        }
        Assert.False(state.IsFresh);
    }

    [Fact]
    public void CampsChoicesAndAnUntouchedJournalAreNotProgress()
    {
        var state = GameState.Fresh(Moment);
        state.Selected = "Triceratops";
        state.Fast = true;
        state.Journal.Open.Add(new JournalEntry { Species = "Troodon", Target = 75, Reward = 400 });
        state.Journal.Rng = 5;
        state.NextSeed = 7;
        state.FreshAsOf = Moment.AddDays(2);
        Assert.True(state.IsFresh);
    }

    // ---- What a run begins with (H19) ------------------------------------------------------

    [Fact]
    public void ARunBeginsWithACopyOfTheRanksAndTheStarsAsTheyAreNow()
    {
        var state = GameState.Fresh(Moment);
        state.Ranks[Trait.DeepStomach] = 2;
        state.Ranks[Trait.ThickHide] = 0;
        state.Species["Maiasaura"] = new SpeciesRecord { Grown = 160 };

        var setup = state.SetupFor("maiasaura", 3);
        Assert.Equal("Maiasaura", setup.Species);   // the table's spelling (H36)
        Assert.Equal(3, setup.Danger);
        Assert.Equal(2, setup.Stars);
        Assert.Equal(new Dictionary<Trait, int> { [Trait.DeepStomach] = 2 }, setup.Ranks);

        state.Ranks[Trait.DeepStomach] = 3;   // bought later
        state.Species["Maiasaura"].Grown = 400;
        Assert.Equal(2, setup.Ranks[Trait.DeepStomach]);
        Assert.Equal(2, setup.Stars);
    }

    [Fact]
    public void ARunCanNotBeginWithAnUnknownSpeciesOrDanger()
    {
        var state = GameState.Fresh(Moment);
        Assert.Throws<ArgumentException>(() => state.SetupFor("Velociraptor", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SetupFor("Dryosaurus", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SetupFor("Dryosaurus", 6));
    }
}

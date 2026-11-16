using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;
using Xunit;
using static PandoraOverlay.Games.Tests.ProgressSamples;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// What the server-play tests share: every step goes through <see cref="Apply"/>,
/// which holds the state given unchanged and the answer a new state, so no test
/// can forget to check it.
/// </summary>
internal static class Server
{
    /// <summary><see cref="ServerPlay.Apply"/>, asserting the state given reads as before and the answer's state is another object.</summary>
    public static ServerResult Apply(GameState state, SeenStep step, DateTime moment)
    {
        var result = Unchanged(state, s => ServerPlay.Apply(s, step, moment));
        Assert.NotSame(state, result.State);
        return result;
    }

    /// <summary>A seen step of <paramref name="species"/> from <paramref name="from"/> to <paramref name="to"/>, growth as fractions.</summary>
    public static SeenStep Step(string species, double from, double to) => new(species, "", from, to);

    /// <summary>
    /// Grows <paramref name="species"/> in one life from <paramref name="fromPerMille"/>
    /// to <paramref name="toPerMille"/> (thousandths of growth), <paramref name="stepPerMille"/>
    /// a step (1 is 0.1%, the overlay's usual poll), every step at <paramref name="moment"/>,
    /// as <see cref="SeenGrowth"/> would hand them over: each step from the last
    /// reading to the next. <paramref name="each"/> sees every result with the
    /// growth it reached, in thousandths.
    /// </summary>
    public static GameState Grow(GameState state, string species, int fromPerMille, int toPerMille, DateTime moment,
        int stepPerMille = 1, Action<ServerResult, int>? each = null)
    {
        for (var k = fromPerMille; k < toPerMille; k += stepPerMille)
        {
            var to = Math.Min(toPerMille, k + stepPerMille);
            var result = Apply(state, Step(species, k / 1000.0, to / 1000.0), moment);
            each?.Invoke(result, to);
            state = result.State;
        }
        return state;
    }

    /// <summary>A fresh save at <see cref="ProgressSamples.Moment"/> with no open journal entry, so growth pays alone.</summary>
    public static GameState Quiet()
    {
        var state = GameState.Fresh(Moment);
        state.Journal.Open.Clear();
        return state;
    }

    /// <summary>The amount a step at freshness <paramref name="fresh"/> owes for <paramref name="percent"/>, by the plan's arithmetic (4.4), kept in the test.</summary>
    public static double Owed(double percent, double fresh) => percent * 4 * fresh;
}

/// <summary>
/// Server play (H10, H12, H18; plan 4.4): what one seen step pays, in its order,
/// the state given always unchanged.
/// </summary>
public class ServerPlayTests
{
    // ---- The pay, by freshness --------------------------------------------------------

    [Fact]
    public void TenPercentOfAFreshSpeciesPaysFourTimesTheSumOfFreshness()
    {
        // 3.0 falling to 2.8: the sum, worked out here step by step, is 116.04.
        var state = Server.Quiet();
        double sum = 0, fresh = 3.0;
        for (var k = 0; k < 100; k++)
        {
            var percent = ((k + 1) / 1000.0 - k / 1000.0) * 100;
            sum += Server.Owed(percent, fresh);
            fresh = Math.Max(1, fresh - 0.02 * percent);
        }
        Assert.InRange(sum, 115, 117);

        var next = Server.Grow(state, "Maiasaura", 0, 100, Moment);
        Assert.Equal((long)Math.Floor(sum), next.Wallet.Earned);
        Assert.InRange(next.Wallet.Earned, 115, 117);
        Assert.Equal(2.8, next.Record("Maiasaura").Fresh, 9);
        Assert.Equal(10, next.Record("Maiasaura").Grown, 9);
    }

    [Fact]
    public void TheCarryIsNeverLostAThousandStepsPayTheFloorOfTheExactSum()
    {
        var state = Server.Quiet();
        // The exact sum, in decimal from each step's own percent and freshness.
        decimal exact = 0;
        double fresh = 3.0;
        for (var k = 0; k < 1000; k++)
        {
            var percent = ((k + 1) / 1000.0 - k / 1000.0) * 100;
            exact += (decimal)percent * 4 * (decimal)fresh;
            fresh = Math.Max(1, fresh - 0.02 * percent);
        }

        var next = Server.Grow(state, "Maiasaura", 0, 1000, Moment);
        Assert.Equal((long)decimal.Floor(exact), next.Wallet.Earned);
        Assert.InRange(next.Carry, 0, 1);
        // What is earned and carried is the whole owed: nothing fell between steps.
        Assert.Equal((double)exact, next.Wallet.Earned + next.Carry, 6);
    }

    [Fact]
    public void AFullGrowFromFreshPaysEightHundredAndLeavesTimesOneAndASecondPaysFourHundred()
    {
        var state = Server.Quiet();
        var once = Server.Grow(state, "Maiasaura", 0, 1000, Moment);
        Assert.InRange(once.Wallet.Earned, 799, 801);
        Assert.Equal(1.0, once.Record("Maiasaura").Fresh, 9);

        var twice = Server.Grow(once, "Maiasaura", 0, 1000, Moment);   // a second life, the same moment
        Assert.InRange(twice.Wallet.Earned - once.Wallet.Earned, 399, 401);
        Assert.Equal(1.0, twice.Record("Maiasaura").Fresh, 9);
    }

    [Fact]
    public void EverySpeciesPaysTheSameAtTheSameFreshness()
    {
        // Nobody is punished (the public promise): a Tyrannosaurus at a freshness pays
        // exactly what any species pays at that freshness.
        foreach (var fresh in new[] { 1.0, 1.7, 3.0 })
        {
            var pays = new HashSet<long>();
            foreach (var species in SpeciesTable.All)
            {
                var state = Server.Quiet();
                state.RecordToChange(species.Name).Fresh = fresh;
                pays.Add(Server.Grow(state, species.Name, 100, 300, Moment).Wallet.Earned);
            }
            Assert.Single(pays);
        }
    }

    [Fact]
    public void FreshnessIsBroughtUpToTheMomentBeforeTheStepPays()
    {
        var state = Server.Quiet();
        state.RecordToChange("Maiasaura").Fresh = 1.0;
        // A day on: 1.25 before this step; 10% at 1.25 then falling to 1.05.
        var next = Server.Grow(state, "Maiasaura", 0, 100, Moment.AddDays(1));
        Assert.Equal(Moment.AddDays(1), next.FreshAsOf);
        Assert.InRange(next.Wallet.Earned, 46, 47);   // 4 * (10 * 1.25 - 0.01 * 100) = 46
        Assert.Equal(1.05, next.Record("Maiasaura").Fresh, 9);
    }

    // ---- What pays nothing ------------------------------------------------------------

    [Fact]
    public void AnUnknownSpeciesPaysNothingAndIsNotNotable()
    {
        var state = GameState.Fresh(Moment);
        foreach (var species in new[] { "Velociraptor", "", "Maiasaura ", "Rex" })
        {
            var result = Server.Apply(state, Server.Step(species, 0.2, 0.8), Moment.AddDays(3));
            Assert.False(result.Notable);
            Assert.Equal(Fingerprint(state), Fingerprint(result.State));   // freshness not even brought up
        }
    }

    [Fact]
    public void AStepWithNoGrowthInItPaysNothing()
    {
        // SeenGrowth never makes one; if it did, it must not throw or pay.
        var state = GameState.Fresh(Moment);
        foreach (var (from, to) in new[] { (0.5, 0.5), (0.6, 0.5), (0.5, double.NaN) })
        {
            var result = Server.Apply(state, Server.Step("Maiasaura", from, to), Moment);
            Assert.False(result.Notable);
            Assert.Equal(Fingerprint(state), Fingerprint(result.State));
        }
    }

    [Fact]
    public void ASpeciesIsFoundIgnoringCaseAndKeptAsTheTableSpellsIt()
    {
        var state = Server.Quiet();
        var next = Server.Apply(state, Server.Step("mAIASAURA", 0.1, 0.2), Moment).State;
        Assert.Equal(new[] { "Maiasaura" }, next.Species.Keys);   // H36
        Assert.Equal("Maiasaura", next.Lately[0].Species);
        Assert.Equal(10, next.Record("Maiasaura").Grown, 9);
    }

    [Fact]
    public void APayOnlyStepIsNotNotable()
    {
        var state = Server.Quiet();
        var result = Server.Apply(state, Server.Step("Maiasaura", 0.1, 0.12), Moment);
        Assert.False(result.Notable);
        Assert.True(result.State.Wallet.Earned > 0);
    }

    // ---- Stars ----------------------------------------------------------------------------

    [Fact]
    public void StarsComeAtFiftyOneFiftyAndThreeHundredPercentEachOnceEachALine()
    {
        var state = Server.Quiet();
        Unlocks.Grant(state, "Maiasaura");   // so no unlock line or notable comes between
        var notableAt = new List<double>();
        for (var life = 0; life < 4; life++)
        {
            state = Server.Grow(state, "Maiasaura", 0, life < 3 ? 1000 : 100, Moment, each: (result, _) =>
            {
                if (!result.Notable) return;
                notableAt.Add(result.State.Record("Maiasaura").Grown);
                Assert.Equal(new LatelyLine(LatelyKind.Star, "Maiasaura", 0, 0, 0, 0), result.State.Lately[0]);
            });
        }

        // 3,100 steps of 0.1%: each line reached once, at its own step, despite the sums' last bits.
        Assert.Equal(3, notableAt.Count);
        Assert.Equal(new[] { 50.0, 150.0, 300.0 }, notableAt.Select(g => Math.Floor(g + 1e-6)));
        Assert.Equal(3, state.Stars("Maiasaura"));
    }

    // ---- Unlocking by growing (H10) ---------------------------------------------------------

    [Fact]
    public void CrossingAdultUnlocksALockedSpeciesForNothingAndCountsAnAdult()
    {
        var state = Server.Quiet();
        var result = Server.Apply(state, Server.Step("Maiasaura", 0.749, 0.751), Moment);
        var next = result.State;

        Assert.True(result.Notable);
        Assert.True(Unlocks.IsUnlocked(next, "Maiasaura"));
        Assert.Equal(1, next.Record("Maiasaura").Adults);
        Assert.Equal(new LatelyLine(LatelyKind.Unlocked, "Maiasaura", 0, 0, 0, 0), next.Lately[0]);
        Assert.Equal(LatelyKind.Grew, next.Lately[1].Kind);
        Assert.Equal(0, next.Bought);
        Assert.Equal(Unlocks.PriceNow(state), Unlocks.PriceNow(next));   // the price does not move
        Assert.Equal(2, next.Wallet.Earned);   // 0.2% at x3.0, and nothing for the unlock
    }

    [Fact]
    public void AStepAfterAdultDoesNotUnlock()
    {
        var state = Server.Quiet();
        var result = Server.Apply(state, Server.Step("Maiasaura", 0.76, 0.77), Moment);
        Assert.False(result.Notable);
        Assert.False(Unlocks.IsUnlocked(result.State, "Maiasaura"));
        Assert.Equal(0, result.State.Record("Maiasaura").Adults);

        // A step that starts on the line did not cross it; one that ends on it did.
        Assert.Equal(0, Server.Apply(state, Server.Step("Maiasaura", 0.75, 0.76), Moment).State.Record("Maiasaura").Adults);
        Assert.Equal(1, Server.Apply(state, Server.Step("Maiasaura", 0.74, 0.75), Moment).State.Record("Maiasaura").Adults);
    }

    [Fact]
    public void AnUnlockedSpeciesCountsTheAdultAndWritesNoLine()
    {
        var state = Server.Quiet();
        var result = Server.Apply(state, Server.Step("Dryosaurus", 0.749, 0.751), Moment);
        Assert.Equal(1, result.State.Record("Dryosaurus").Adults);
        Assert.Equal(state.Unlocked, result.State.Unlocked);
        Assert.Single(result.State.Lately);
        Assert.Equal(LatelyKind.Grew, result.State.Lately[0].Kind);
        Assert.False(result.Notable);
    }

    [Fact]
    public void UnlockingByGrowingEarnsSixAtTheSixth()
    {
        var state = Server.Quiet();
        foreach (var name in new[] { "Maiasaura", "Troodon", "Stegosaurus" }) Unlocks.Grant(state, name);
        var result = Server.Apply(state, Server.Step("Triceratops", 0.7, 0.8), Moment);
        Assert.True(result.State.Achievements.Has(AchievementList.Six));
        Assert.Equal(6, result.State.Unlocked.Count);
    }

    // ---- Lately -----------------------------------------------------------------------------

    [Fact]
    public void OneSpeciesGrowingIsOneLineThatGrows()
    {
        var state = Server.Quiet();
        var next = Server.Grow(state, "Maiasaura", 100, 200, Moment);   // 10% in 100 steps
        var line = Assert.Single(next.Lately);
        Assert.Equal(LatelyKind.Grew, line.Kind);
        Assert.Equal("Maiasaura", line.Species);
        Assert.Equal(10, line.Percent, 9);
        Assert.Equal(3.0, line.Fresh);   // the multiplier at its first step
        Assert.Equal(next.Wallet.Earned, line.Amount);
    }

    [Fact]
    public void AnotherSpeciesOrAStarInBetweenStartsANewLine()
    {
        var state = Server.Quiet();
        state = Server.Grow(state, "Maiasaura", 100, 110, Moment);
        state = Server.Grow(state, "Troodon", 100, 110, Moment);
        state = Server.Grow(state, "Maiasaura", 110, 120, Moment);
        Assert.Equal(new[] { "Maiasaura", "Troodon", "Maiasaura" }, state.Lately.Select(l => l.Species));
        Assert.All(state.Lately, l => Assert.Equal(LatelyKind.Grew, l.Kind));

        // A star in between: the growth after it is a line of its own.
        var starred = Server.Quiet();
        starred.RecordToChange("Maiasaura").Grown = 49.95;
        starred.RecordToChange("Maiasaura").Fresh = 2.0;
        starred = Server.Grow(starred, "Maiasaura", 100, 103, Moment);
        Assert.Equal(new[] { LatelyKind.Grew, LatelyKind.Star, LatelyKind.Grew }, starred.Lately.Select(l => l.Kind));
        Assert.Equal(0.2, starred.Lately[0].Percent, 9);   // after the star
        Assert.Equal(0.1, starred.Lately[2].Percent, 9);   // the step that reached it
        Assert.Equal(2.0, starred.Lately[2].Fresh);
        Assert.Equal(2.0 - 0.002, starred.Lately[0].Fresh, 9);   // its own first step's multiplier
    }

    [Fact]
    public void LatelyKeepsSixLinesNewestFirst()
    {
        var state = Server.Quiet();
        var order = new[] { "Maiasaura", "Troodon", "Stegosaurus", "Triceratops", "Gallimimus", "Omniraptor", "Allosaurus", "Pteranodon" };
        foreach (var name in order) state = Server.Grow(state, name, 100, 101, Moment);
        Assert.Equal(order.Reverse().Take(6), state.Lately.Select(l => l.Species));
    }

    // ---- The journal, as server play counts it (H12) ------------------------------------------

    /// <summary>A quiet save whose journal holds exactly these entries, as the journal would draw them.</summary>
    private static GameState WithEntries(params string[] species)
    {
        var state = Server.Quiet();
        foreach (var name in species)
        {
            SpeciesTable.TryGet(name, out var info);
            state.Journal.Open.Add(Journal.EntryFor(info));
        }
        return state;
    }

    [Fact]
    public void AnEntryCountsOverTwoLivesAndASpeciesChangeAndIsDoneExactlyAtItsTarget()
    {
        var state = WithEntries("Troodon", "Maiasaura", "Gallimimus");
        state = Server.Grow(state, "Maiasaura", 300, 700, Moment);   // 40% in one life
        state = Server.Grow(state, "Troodon", 300, 400, Moment);     // another species: 10% of its own
        Assert.Equal(40, state.Journal.Open[1].Seen, 9);
        Assert.Equal(10, state.Journal.Open[0].Seen, 9);

        // A new life of Maiasaura: done at 35% more, not one step sooner.
        state = Server.Grow(state, "Maiasaura", 0, 349, Moment);
        Assert.Equal("Maiasaura", state.Journal.Open[1].Species);
        Assert.Equal(0, state.Journal.Done);

        var before = state.Wallet.Earned;
        var result = Server.Apply(state, Server.Step("Maiasaura", 0.349, 0.350), Moment);
        var next = result.State;
        Assert.True(result.Notable);
        Assert.Equal(1, next.Journal.Done);
        Assert.Equal(new LatelyLine(LatelyKind.Journal, "Maiasaura", 0, 0, 400, 75), next.Lately[0]);
        Assert.InRange(next.Wallet.Earned - before - 400, 0, 1);   // the reward, and the step's own bit of growth

        // A new entry in the same place, for none of the species open or just finished.
        var drawn = next.Journal.Open[1];
        Assert.NotEqual("Maiasaura", drawn.Species);
        Assert.DoesNotContain(drawn.Species, new[] { "Troodon", "Gallimimus" });
        Assert.Equal(0, drawn.Seen);
        Assert.Equal(new[] { "Troodon", "Gallimimus" }, new[] { next.Journal.Open[0].Species, next.Journal.Open[2].Species });
    }

    [Fact]
    public void TheEntryDrawnAfterOneIsFinishedIsNeverItsSpecies()
    {
        // Over many journals' generators: the species just finished never comes back at once.
        for (uint seed = 0; seed < 300; seed++)
        {
            var state = WithEntries("Maiasaura", "Troodon", "Gallimimus");
            state.Journal.Open[0].Seen = 74.95;
            state.Journal.Rng = seed;
            var next = Server.Apply(state, Server.Step("Maiasaura", 0.1, 0.101), Moment).State;
            Assert.Equal(1, next.Journal.Done);
            Assert.NotEqual("Maiasaura", next.Journal.Open[0].Species);
            Assert.Equal(3, next.Journal.Open.Select(e => e.Species).Distinct().Count());
        }
    }

    [Fact]
    public void ABigBuildsEntryIsDoneAtFiftyAndPaysFiveHundred()
    {
        var state = WithEntries("Stegosaurus", "Troodon", "Gallimimus");
        var before = Server.Grow(state, "Stegosaurus", 0, 499, Moment);
        Assert.Equal(0, before.Journal.Done);
        var after = Server.Grow(before, "Stegosaurus", 499, 500, Moment);
        Assert.Equal(1, after.Journal.Done);
        Assert.Equal(500, after.Lately[0].Amount);
        Assert.Equal(50, after.Lately[0].Target);
    }

    // ---- The achievements of server play, each once ---------------------------------------------

    [Fact]
    public void ResearcherAtThreeEntriesAndNaturalistAtFifteenEachOnce()
    {
        foreach (var (done, id) in new[] { (AchievementList.ResearcherEntries, AchievementList.Researcher), (AchievementList.NaturalistEntries, AchievementList.Naturalist) })
        {
            var state = WithEntries("Maiasaura", "Troodon", "Gallimimus");
            state.Journal.Done = done - 2;
            state.Journal.Open[0].Seen = 74.95;
            var near = Server.Apply(state, Server.Step("Maiasaura", 0.1, 0.101), Moment).State;
            Assert.False(near.Achievements.Has(id));   // one short

            near.Journal.Open[1].Seen = 74.95;
            var result = Server.Apply(near, Server.Step("Troodon", 0.1, 0.101), Moment);
            Assert.True(result.State.Achievements.Has(id));
            Assert.True(result.Notable);

            // Once: another entry later pays its reward and no second achievement.
            var later = result.State;
            var slot = later.Journal.Open.FindIndex(e => e.Species != "Maiasaura" && e.Species != "Troodon");
            later.Journal.Open[slot].Seen = later.Journal.Open[slot].Target - 0.05;
            var again = Server.Apply(later, Server.Step(later.Journal.Open[slot].Species, 0.1, 0.101), Moment).State;
            Assert.Equal(later.Achievements.Unlocked.Count, again.Achievements.Unlocked.Count);
        }
    }

    [Fact]
    public void DiverseAtTheFifthSpeciesSeenCrossingAdultOnce()
    {
        var state = Server.Quiet();
        foreach (var name in new[] { "Maiasaura", "Troodon", "Stegosaurus", "Triceratops" })
            state.RecordToChange(name).Adults = 1;
        var fourth = Server.Apply(state, Server.Step("Maiasaura", 0.749, 0.751), Moment).State;   // a second Adult of one already counted
        Assert.False(fourth.Achievements.Has(AchievementList.Diverse));

        var result = Server.Apply(fourth, Server.Step("Dryosaurus", 0.749, 0.751), Moment);   // unlocked already: notable by the achievement alone
        Assert.True(result.State.Achievements.Has(AchievementList.Diverse));
        Assert.True(result.Notable);
        Assert.Equal(PayOf(AchievementList.Diverse), result.State.Wallet.Earned - fourth.Wallet.Earned - 2);

        // Once: a sixth species crossing Adult (an unlock, the fourth species unlocked) grants nothing again.
        var again = Server.Apply(result.State, Server.Step("Gallimimus", 0.749, 0.751), Moment).State;
        Assert.Equal(6, SpeciesTable.All.Count(s => again.Record(s.Name).Adults > 0));
        Assert.Equal(result.State.Achievements.Unlocked, again.Achievements.Unlocked);
    }

    [Fact]
    public void Star21AtTheLastFirstStarAndMasteryAtTheLastThirdOnce()
    {
        var state = Server.Quiet();
        foreach (var s in SpeciesTable.All) state.RecordToChange(s.Name).Grown = 50;
        state.RecordToChange("Gallimimus").Grown = 49.95;
        var result = Server.Apply(state, Server.Step("Gallimimus", 0.1, 0.101), Moment);
        Assert.True(result.State.Achievements.Has(AchievementList.Star21));
        Assert.False(result.State.Achievements.Has(AchievementList.Mastery));
        Assert.True(result.Notable);
        Assert.Equal(PayOf(AchievementList.Star21), result.State.Wallet.Earned - (long)Math.Floor(Server.Owed(0.1, 3.0) + 1e-9));

        var mastered = result.State;
        foreach (var s in SpeciesTable.All) mastered.RecordToChange(s.Name).Grown = 300;
        mastered.RecordToChange("Troodon").Grown = 299.95;
        var last = Server.Apply(mastered, Server.Step("Troodon", 0.1, 0.101), Moment);
        Assert.True(last.State.Achievements.Has(AchievementList.Mastery));
        Assert.True(last.Notable);
        Assert.Equal(new[] { AchievementList.Star21, AchievementList.Mastery }, last.State.Achievements.Unlocked);

        // Once: a further star pays no achievement again.
        var again = Server.Apply(last.State, Server.Step("Troodon", 0.2, 0.3), Moment);
        Assert.Equal(last.State.Achievements.Unlocked, again.State.Achievements.Unlocked);
    }

    // ---- Nothing pays but a seen step ------------------------------------------------------------

    [Fact]
    public void WhatSeenGrowthDoesNotCallAStepPaysNothing()
    {
        // The game's way in, as the plan has it: SeenGrowth first, ServerPlay only for
        // a step. A first reading, a swap of stored dinos and a long gap give no step,
        // so nothing is paid, and nothing here makes up for it.
        var seen = new SeenGrowth();
        var state = Server.Quiet();
        var at = TimeSpan.Zero;
        DinoReading Reading(string species, double growth) => new(true, species, "Female", growth);

        void Feed(DinoReading reading, TimeSpan later)
        {
            at += later;
            if (seen.Update(reading, at) is { } step) state = Server.Apply(state, step, Moment).State;
        }

        Feed(Reading("Maiasaura", 0.50), TimeSpan.Zero);             // a first reading
        Feed(Reading("Maiasaura", 0.90), TimeSpan.FromSeconds(3));   // a swap: 40% in a poll
        Feed(Reading("Maiasaura", 0.92), TimeSpan.FromMinutes(30));  // a long gap
        Assert.Equal(0, state.Wallet.Earned);
        Assert.Empty(state.Lately);

        Feed(Reading("Maiasaura", 0.921), TimeSpan.FromSeconds(3));  // a step seen: paid
        Assert.True(state.Wallet.Earned + state.Carry > 0);
        Assert.Equal(0.1, state.Record("Maiasaura").Grown, 6);
    }

    [Fact]
    public void NothingInServerPlayAnswersHowLongUntil()
    {
        // G4: freshness is a number, never a wait. No function of these three returns
        // a time or a span.
        var types = new[] { typeof(ServerPlay), typeof(Freshness), typeof(Journal) };
        var functions = types.SelectMany(t => t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly));
        Assert.All(functions, f => Assert.True(f.ReturnType != typeof(TimeSpan) && f.ReturnType != typeof(DateTime)
            && f.ReturnType != typeof(TimeSpan?) && f.ReturnType != typeof(DateTime?), f.Name));
    }
}

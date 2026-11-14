using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// A predator's bite on you (<see cref="Fights.Bite"/>): the dodge and its one roll,
/// the damage and its bounds, Braced only when you stood your ground, Frill's cap,
/// Spikes' share and a predator (or the apex) defeated by them, Second wind once,
/// and the end as caught with the predator's name kept. The expected numbers are
/// worked out here from the design's formula and the same generator, roll for roll.
/// </summary>
public class BiteTests
{
    private const int Middle = Turns.Middle;
    private static int Right(int steps) => Turns.Right(steps);

    /// <summary>A board with <paramref name="species"/> at <paramref name="growth"/> and a predator of <paramref name="type"/> beside you, its last creature.</summary>
    private static RunState Beside(string species, PredatorType type = PredatorType.Carnotaurus, double growth = 75) =>
        Turns.Board(species, growth: growth).Put(type, Right(1));

    /// <summary>A copy of <paramref name="run"/> whose generator's next rolls pass <paramref name="rolls"/>, in order.</summary>
    internal static RunState Rolling(RunState run, params Func<double, bool>[] rolls)
    {
        for (uint seed = 1; seed < 5_000_000; seed++)
        {
            var rng = new Rng(seed);
            var ok = true;
            foreach (var roll in rolls)
            {
                if (roll(rng.NextDouble())) continue;
                ok = false;
                break;
            }
            if (!ok) continue;
            var copy = run.Clone();
            copy.Rng = seed;
            return copy;
        }
        throw new Xunit.Sdk.XunitException("No seed gives those rolls.");
    }

    /// <summary>The bite of the run's last creature on a copy of <paramref name="run"/>, with the run's own generator.</summary>
    private static (RunState After, List<TurnEvent> Events, Rng Rng) Bite(RunState run, Stats? stats = null)
    {
        var before = Runs.Fingerprint(run);
        var copy = run.Clone();
        var rng = new Rng(copy.Rng);
        var events = new List<TurnEvent>();
        Fights.Bite(copy, stats ?? Stats.Of(copy), rng, copy.Creatures[^1], events);
        copy.Rng = rng.State;
        Assert.Equal(before, Runs.Fingerprint(run));
        return (copy, events, rng);
    }

    /// <summary>The design's damage before any perk: <c>max(1, Round(Attack * Threat * Swing * Armour(Defense)))</c>.</summary>
    private static int Expected(RunState run, PredatorType type, double swingRoll)
    {
        var swing = Tuning.SwingBase + Tuning.SwingSpread * swingRoll;
        var blow = Predators.Of(type).Attack * Island.Threat(run);
        return (int)Math.Max(1, Tuning.Round(blow * swing * Fights.Armour(Stats.Of(run).Defense)));
    }

    /// <summary>The <paramref name="skip"/>+1-th roll of <paramref name="run"/>'s generator.</summary>
    private static double Roll(RunState run, int skip = 0)
    {
        var rng = new Rng(run.Rng);
        for (var i = 0; i < skip; i++) rng.NextDouble();
        return rng.NextDouble();
    }

    /// <summary><paramref name="rolls"/> rolls taken from <paramref name="run"/>'s generator: the state after them.</summary>
    private static uint After(RunState run, int rolls)
    {
        var rng = new Rng(run.Rng);
        for (var i = 0; i < rolls; i++) rng.NextDouble();
        return rng.State;
    }

    // ---- The dodge ---------------------------------------------------------------------

    [Fact]
    public void ARollUnderYourDodgeIsADodgeAndNothingMore()
    {
        var run = Rolling(Beside("Gallimimus"), r => r < 0.2);   // Evasive: 20%
        Assert.Equal(0.2, Stats.Of(run).Dodge, 10);

        var (after, events, rng) = Bite(run);

        Assert.Equal(new TurnEvent[] { new Bit(run.Creatures[^1].Id, 0, Dodged: true, Softened: false) }, events);
        Assert.Equal(1.0, after.You.Health);
        Assert.False(after.You.Bitten);
        Assert.Equal(After(run, 1), rng.State);   // the swing is never rolled
    }

    [Fact]
    public void ARollAtYourDodgeOrAboveBites()
    {
        var run = Rolling(Beside("Gallimimus"), r => r >= 0.2);
        var (after, events, rng) = Bite(run);
        var bit = Assert.IsType<Bit>(Assert.Single(events));
        Assert.False(bit.Dodged);
        Assert.Equal(Expected(run, PredatorType.Carnotaurus, Roll(run, 1)), bit.Damage);
        Assert.True(after.You.Bitten);
        Assert.Equal(After(run, 2), rng.State);
    }

    [Fact]
    public void TheDodgeIsRolledEvenAtNoDodge()
    {
        var run = Rolling(Beside("Dryosaurus"), r => r < 0.01);
        Assert.Equal(0.0, Stats.Of(run).Dodge);
        var (_, events, rng) = Bite(run);
        Assert.False(Assert.IsType<Bit>(Assert.Single(events)).Dodged);
        Assert.Equal(After(run, 2), rng.State);
    }

    // ---- The damage --------------------------------------------------------------------

    [Fact]
    public void TheDamageIsTheFormulaWithinTheSwingsBounds()
    {
        foreach (var type in new[] { PredatorType.Troodon, PredatorType.Carnotaurus, PredatorType.Tyrannosaurus })
        {
            var start = Beside("Tenontosaurus", type);
            var blow = Predators.Of(type).Attack * Island.Threat(start) * Fights.Armour(Stats.Of(start).Defense);
            var least = (int)Math.Max(1, Tuning.Round(blow * Tuning.SwingBase));
            var most = (int)Math.Max(1, Tuning.Round(blow * (Tuning.SwingBase + Tuning.SwingSpread)));
            for (uint seed = 1; seed <= 300; seed++)
            {
                var run = start.Clone();
                run.Rng = seed;
                run.You.LastMove = MoveKind.Walk;   // not braced
                var (after, events, _) = Bite(run);
                var bit = Assert.IsType<Bit>(events[0]);
                Assert.Equal(Expected(run, type, Roll(run, 1)), bit.Damage);
                Assert.InRange(bit.Damage, least, most);
                Assert.Equal(1.0 - (double)bit.Damage / Stats.Of(run).MaxHealth, after.You.Health, 10);
            }
        }
    }

    [Fact]
    public void ABiteUsesTheNumbersItIsHanded()
    {
        // The world's turn takes your numbers once, at its start: a bite reads those, not the run's now.
        var run = Rolling(Beside("Tenontosaurus"), r => r >= 0.5, r => true);
        run.You.LastMove = MoveKind.Walk;
        var taken = Stats.Of(run) with { MaxHealth = 1000, Defense = 0 };
        var (after, events, _) = Bite(run, taken);
        var damage = Assert.IsType<Bit>(events[0]).Damage;
        var swing = Tuning.SwingBase + Tuning.SwingSpread * Roll(run, 1);
        Assert.Equal((int)Math.Max(1, Tuning.Round(Predators.Of(PredatorType.Carnotaurus).Attack * Island.Threat(run) * swing)), damage);
        Assert.Equal(1.0 - damage / 1000.0, after.You.Health, 10);
    }

    // ---- Braced and Frill ---------------------------------------------------------------

    [Theory]
    [InlineData((int)MoveKind.Rest, true)]
    [InlineData((int)MoveKind.Eat, true)]
    [InlineData((int)MoveKind.Drink, true)]
    [InlineData((int)MoveKind.Attack, true)]
    [InlineData((int)MoveKind.None, true)]
    [InlineData((int)MoveKind.Walk, false)]
    [InlineData((int)MoveKind.Sneak, false)]
    [InlineData((int)MoveKind.Sprint, false)]
    public void BracedSoftensABiteOnlyWhenYouDidNotMove(int last, bool braced)
    {
        var run = Rolling(Beside("Tenontosaurus"), r => true, r => true);
        run.You.LastMove = (MoveKind)last;
        var raw = Expected(run, PredatorType.Carnotaurus, Roll(run, 1));
        var bit = Assert.IsType<Bit>(Bite(run).Events[0]);
        var expected = braced ? (int)Math.Max(1, Tuning.Round(raw * Tuning.BracedShare)) : raw;
        Assert.Equal(expected, bit.Damage);
        Assert.Equal(expected != raw, bit.Softened);
    }

    [Fact]
    public void BracedIsTheTenontosaurusAlone()
    {
        var run = Rolling(Beside("Pachycephalosaurus"), r => true, r => true);   // the same build, another perk
        run.You.LastMove = MoveKind.Rest;
        var bit = Assert.IsType<Bit>(Bite(run).Events[0]);
        Assert.Equal(Expected(run, PredatorType.Carnotaurus, Roll(run, 1)), bit.Damage);
        Assert.False(bit.Softened);
    }

    [Fact]
    public void FrillCapsABiteAtItsShareOfYourHealth()
    {
        // A hatchling Triceratops under an Allosaurus: the bite would be far over 15% of its health.
        var run = Rolling(Beside("Triceratops", PredatorType.Allosaurus, growth: 0), r => true, r => true);
        var stats = Stats.Of(run);
        var cap = (int)Math.Ceiling(Tuning.FrillMost * stats.MaxHealth);
        var raw = Expected(run, PredatorType.Allosaurus, Roll(run, 1));
        Assert.True(raw > cap, "the test's premise");
        var (after, events, _) = Bite(run);
        var bit = Assert.IsType<Bit>(events[0]);
        Assert.Equal(cap, bit.Damage);
        Assert.True(bit.Softened);
        Assert.Equal(1.0 - (double)cap / stats.MaxHealth, after.You.Health, 10);
    }

    [Fact]
    public void FrillLeavesASmallBiteAlone()
    {
        var run = Rolling(Beside("Triceratops", PredatorType.Troodon, growth: 100), r => true, r => true);
        var raw = Expected(run, PredatorType.Troodon, Roll(run, 1));
        Assert.True(raw <= Math.Ceiling(Tuning.FrillMost * Stats.Of(run).MaxHealth), "the test's premise");
        var bit = Assert.IsType<Bit>(Bite(run).Events[0]);
        Assert.Equal(raw, bit.Damage);
        Assert.False(bit.Softened);
    }

    // ---- Spikes ------------------------------------------------------------------------

    [Fact]
    public void SpikesAnswerABiteWithTheirShare()
    {
        var run = Rolling(Beside("Kentrosaurus"), r => true, r => true);
        var predator = run.Creatures[^1];
        var (after, events, _) = Bite(run);
        var bit = Assert.IsType<Bit>(events[0]);
        var back = (int)Math.Max(1, Tuning.Round(bit.Damage * Tuning.SpikesShare));
        Assert.Equal(new Hurt(predator.Id, predator.At, back, HurtBy.Spikes, Defeated: false), events[1]);
        Assert.Equal(2, events.Count);
        Assert.Equal(predator.Health - back, after.Creatures.Single(c => c.Id == predator.Id).Health);
    }

    [Fact]
    public void SpikesAreKentrosaurusAlone()
    {
        var run = Rolling(Beside("Tenontosaurus"), r => true, r => true);
        Assert.Single(Bite(run).Events);
    }

    [Fact]
    public void SpikesCanDefeatTheBiter()
    {
        var run = Rolling(Beside("Kentrosaurus"), r => true, r => true);
        var predator = run.Creatures[^1];
        predator.Health = 1;
        var (after, events, _) = Bite(run);
        Assert.Equal(new Hurt(predator.Id, predator.At, ((Hurt)events[1]).Damage, HurtBy.Spikes, Defeated: true), events[1]);
        Assert.DoesNotContain(after.Creatures, c => c.Id == predator.Id);
        Assert.Equal(run.Meat[predator.At] + Predators.Of(predator.Type).Meat, after.Meat[predator.At]);
        Assert.Equal(run.Kills + 1, after.Kills);
        Assert.Null(after.Over);
    }

    [Fact]
    public void TheApexDefeatedBySpikesWinsTheRun()
    {
        var run = Rolling(Beside("Kentrosaurus", PredatorType.Tyrannosaurus), r => true, r => true);
        var apex = run.Creatures[^1];
        apex.Health = 1;
        var (after, events, _) = Bite(run);
        Assert.True(((Hurt)events[1]).Defeated);
        Assert.Equal(new RunEnded(true, EndReason.DefeatedApex, 0), events[^1]);
        Assert.Equal(3, events.Count);
        Assert.NotNull(after.Over);
        Assert.True(after.Over!.Won);
        Assert.Equal(run.Kills, after.Kills);   // the apex is a win, not a kill
    }

    // ---- Second wind and the end ----------------------------------------------------------

    [Fact]
    public void SecondWindLeavesYouAtAQuarterOnce()
    {
        var run = Rolling(Beside("Kentrosaurus"), r => true, r => true, r => true, r => true);
        run.You.Health = 0.01;
        run.You.Wind = true;
        var predator = run.Creatures[^1];
        predator.Health = 500;

        var (after, events, rng) = Bite(run);
        Assert.IsType<Bit>(events[0]);
        Assert.IsType<SecondWind>(events[1]);
        Assert.Equal(HurtBy.Spikes, Assert.IsType<Hurt>(events[2]).By);   // the bite, Second wind, then your spikes
        Assert.Equal(Tuning.SecondWindHealth, after.You.Health);
        Assert.False(after.You.Wind);
        Assert.Null(after.Over);

        // Spent: the next bite that takes you to 0 ends the run.
        after.You.Health = 0.01;
        after.Rng = rng.State;
        var (ended, more, _) = Bite(after);
        Assert.DoesNotContain(more, e => e is SecondWind);
        Assert.NotNull(ended.Over);
    }

    [Fact]
    public void ABiteToZeroWithNoWindIsCaughtAndNamesThePredator()
    {
        // The second Carnotaurus of the run: its number is kept for the summary.
        var run = Rolling(Turns.Board("Dryosaurus", growth: 30).Put(PredatorType.Carnotaurus, Right(4)).Put(PredatorType.Carnotaurus, Right(1)),
            r => true, r => true);
        run.You.Health = 0.01;
        var catcher = run.Creatures[^1];
        Assert.Equal(2, catcher.Ordinal);

        var (after, events, _) = Bite(run);

        Assert.IsType<Bit>(events[0]);
        Assert.Equal(new RunEnded(false, EndReason.Caught, catcher.Id), events[^1]);
        Assert.Equal(2, events.Count);
        Assert.Equal(0, after.You.Health);
        var over = after.Over!;
        Assert.False(over.Won);
        Assert.Equal(EndReason.Caught, over.Reason);
        Assert.Equal(PredatorType.Carnotaurus, over.ByType);
        Assert.Equal(2, over.ByOrdinal);
        Assert.Equal(Stages.Juvenile, over.Stage);
        Assert.Equal(RunPay.Of(after), over.Lines);
        Assert.Equal(RunPay.Total(over.Lines), over.Total);
    }

    [Fact]
    public void ABiteThatCatchesYouEndsTheRunBeforeYourSpikes()
    {
        var run = Rolling(Beside("Kentrosaurus"), r => true, r => true);
        run.You.Health = 0.01;
        var predator = run.Creatures[^1];
        var (after, events, _) = Bite(run);
        Assert.DoesNotContain(events, e => e is Hurt);
        Assert.Equal(predator.Health, after.Creatures.Single(c => c.Id == predator.Id).Health);
        Assert.Equal(EndReason.Caught, after.Over!.Reason);
    }

    [Fact]
    public void ADodgeNeverEndsARun()
    {
        var run = Rolling(Beside("Gallimimus"), r => r < 0.2);
        run.You.Health = 0.001;
        var (after, _, _) = Bite(run);
        Assert.Null(after.Over);
        Assert.Equal(0.001, after.You.Health);
    }
}

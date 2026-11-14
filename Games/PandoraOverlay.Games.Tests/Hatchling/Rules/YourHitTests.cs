using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// Your hit (<see cref="Fights.YourHit"/> and the Attack move): reach and Spit, the
/// damage and its bounds, every boost alone and together, armour and Crushing bite,
/// what Headbutt, Venom and Bleeder leave on a creature, Thagomizer's sweep, a
/// creature that falls, and the apex's fall that wins the run with no world's turn.
/// The expected damage is worked out here from the design's formula and the same
/// generator, roll for roll.
/// </summary>
public class YourHitTests
{
    private const int Middle = Turns.Middle;
    private static int Right(int steps) => Turns.Right(steps);

    /// <summary>A board of <paramref name="ground"/> with <paramref name="species"/> at <paramref name="growth"/> and a predator of <paramref name="type"/> on <paramref name="cell"/>, its last creature.</summary>
    private static RunState Facing(string species, PredatorType type, int cell, double growth = 75, Terrain ground = Terrain.Grass) =>
        Turns.Board(species, ground, growth).Put(type, cell);

    /// <summary>The design's damage: <c>max(1, Round(attack * boost * Swing * armour))</c>, in the rules' own order of multiplying.</summary>
    private static int Expected(double attack, double boost, double roll, Creature target, bool crushing = false)
    {
        var swing = Tuning.SwingBase + Tuning.SwingSpread * roll;
        var armour = crushing ? 1 : Fights.Armour(Fights.DefenseOf(target));
        return (int)Math.Max(1, Tuning.Round(attack * boost * swing * armour));
    }

    /// <summary>The <paramref name="skip"/>+1-th roll of <paramref name="run"/>'s generator.</summary>
    private static double Roll(RunState run, int skip = 0)
    {
        var rng = new Rng(run.Rng);
        for (var i = 0; i < skip; i++) rng.NextDouble();
        return rng.NextDouble();
    }

    /// <summary>Your hit straight through <see cref="Fights.YourHit"/> on <paramref name="run"/> itself (the caller's copy), its events and what it returned.</summary>
    private static (List<TurnEvent> Events, bool ApexFell) Hit(RunState run, Rng rng, int target, Stats? stats = null)
    {
        var events = new List<TurnEvent>();
        var fell = Fights.YourHit(run, stats ?? Stats.Of(run), rng, run.Creatures.Single(c => c.Id == target), events);
        return (events, fell);
    }

    // ---- Armour -----------------------------------------------------------------------

    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(1, 0.94)]
    [InlineData(3, 0.82)]
    [InlineData(5, 0.7)]
    [InlineData(10, 0.4)]
    [InlineData(14, 0.4)]   // never more than 60% off
    public void ArmourTakesSixPercentAPointUpToSixty(int defense, double left)
    {
        Assert.Equal(left, Fights.Armour(defense), 10);
    }

    // ---- Reach --------------------------------------------------------------------------

    [Fact]
    public void AnAttackIsRefusedForACreatureOutOfReach()
    {
        var run = Facing("Herrerasaurus", PredatorType.Allosaurus, Right(2));
        Assert.Empty(Moves.Attackable(run, Stats.Of(run)));
        Turns.MoveRefused(run, new PlayerAction.Attack(run.Creatures[0].Id));
        Turns.MoveRefused(run, new PlayerAction.Attack(99));
    }

    [Fact]
    public void EveryCreatureBesideYouCanBeHitInIdOrder()
    {
        var neighbours = Island.Grid.Neighbours(Middle);
        var run = Turns.Board("Herrerasaurus").Put(PredatorType.Troodon, neighbours[3]).PutSmallGame(neighbours[0])
            .Put(PredatorType.Allosaurus, neighbours[5]).Put(PredatorType.Troodon, Right(2));
        var ids = Moves.Attackable(run, Stats.Of(run)).Select(c => c.Id);
        Assert.Equal(run.Creatures.Take(3).Select(c => c.Id), ids);
    }

    [Fact]
    public void SpitReachesAKnownCreatureTwoCellsAwayAndNoFurther()
    {
        var run = Turns.Board("Dilophosaurus", growth: 75).Put(PredatorType.Allosaurus, Right(2))
            .PutSmallGame(Right(-2)).Put(PredatorType.Troodon, Right(3)).Put(PredatorType.Troodon, Right(1));
        var stats = Stats.Of(run);
        var all = run.Creatures.Select(c => c.Id).ToList();
        Assert.Equal(new[] { all[0], all[1], all[3] }, Moves.Attackable(run, stats).Select(c => c.Id));

        // Only what you know: a dino that saw one cell would not know either at two.
        var nearSighted = stats with { Sight = 1 };
        Assert.Equal(new[] { all[3] }, Moves.Attackable(run, nearSighted).Select(c => c.Id));

        // Without Spit, two cells is out of reach.
        var hunter = Turns.Board("Herrerasaurus", growth: 75).Put(PredatorType.Allosaurus, Right(2));
        Assert.Empty(Moves.Attackable(hunter, Stats.Of(hunter)));
    }

    [Fact]
    public void ASpitIsRangedAtSixtyPercentAndNotBoosted()
    {
        var run = Facing("Dilophosaurus", PredatorType.Allosaurus, Right(2));
        var target = run.Creatures[0];
        var (after, events, done, _) = Turns.Move(run, new PlayerAction.Attack(target.Id));
        var struck = Assert.IsType<Struck>(events.Single());
        Assert.True(struck.Ranged);
        Assert.False(struck.Boosted);
        Assert.Equal(Right(2), struck.At);
        Assert.Equal(Expected(Stats.Of(run).Attack, Tuning.SpitShare, Roll(run), target), struck.Damage);
        Assert.Equal(target.Health - struck.Damage, after.Creatures[0].Health);
        Assert.Equal(Tuning.AttackNoise, done.Noise);

        // Beside you a spitter bites as anyone: not ranged, no share.
        var close = Facing("Dilophosaurus", PredatorType.Allosaurus, Right(1));
        var bite = Assert.IsType<Struck>(Turns.Move(close, new PlayerAction.Attack(close.Creatures[0].Id)).Events.Single());
        Assert.False(bite.Ranged);
        Assert.Equal(Expected(Stats.Of(close).Attack, 1, Roll(close), close.Creatures[0]), bite.Damage);
    }

    // ---- The damage ---------------------------------------------------------------------

    [Fact]
    public void APlainHitIsTheSwingTimesTheArmourOverTwoHundredSeeds()
    {
        // A Carnotaurus (Fleet: nothing in a hit) on open ground against an Allosaurus (defense 3).
        var board = Facing("Carnotaurus", PredatorType.Allosaurus, Right(1));
        var attack = Stats.Of(board).Attack;
        var low = (int)Tuning.Round(attack * Tuning.SwingBase * Fights.Armour(3));
        var high = (int)Tuning.Round(attack * (Tuning.SwingBase + Tuning.SwingSpread) * Fights.Armour(3));
        var seen = new HashSet<int>();
        for (uint seed = 1; seed <= 200; seed++)
        {
            var run = board.Clone();
            run.Rng = seed;
            var target = run.Creatures[0];
            var (after, events, _, _) = Turns.Move(run, new PlayerAction.Attack(target.Id));
            var damage = Expected(attack, 1, Roll(run), target);
            Assert.Equal(new Struck(target.Id, Right(1), damage, false, false, false, false, Tuning.AttackNoise), events.Single());
            Assert.Equal(target.Health - damage, after.Creatures[0].Health);
            Assert.InRange(damage, low, high);
            seen.Add(damage);
        }
        Assert.Contains(low, seen);
        Assert.Contains(high, seen);
    }

    [Fact]
    public void EveryBlowTakesAtLeastOnePoint()
    {
        var run = Facing("Dryosaurus", PredatorType.Allosaurus, Right(1), growth: 0);
        var weak = Stats.Of(run) with { Attack = 0.1 };
        var (events, _) = Hit(run, new Rng(run.Rng), run.Creatures[0].Id, weak);
        Assert.Equal(Tuning.LeastDamage, Assert.IsType<Struck>(events.Single()).Damage);
    }

    [Fact]
    public void FierceHitsWithItsAttack()
    {
        var plain = Facing("Herrerasaurus", PredatorType.Allosaurus, Right(1));
        var fierce = plain.Picked(Instinct.Fierce);
        var attack = Stats.Of(fierce).Attack;
        Assert.Equal(Tuning.Round1(Stats.Of(plain).Attack * Tuning.FierceAttack), attack, 10);
        var struck = Assert.IsType<Struck>(Turns.Move(fierce, new PlayerAction.Attack(fierce.Creatures[0].Id)).Events.Single());
        Assert.Equal(Expected(attack, 1, Roll(fierce), fierce.Creatures[0]), struck.Damage);
    }

    [Fact]
    public void CrushingBiteIgnoresDefense()
    {
        // The Tyrannosaurus's perk against the toughest hide there is (defense 5)...
        var rex = Facing("Tyrannosaurus", PredatorType.Tyrannosaurus, Right(1));
        var target = rex.Creatures[0];
        var struck = Assert.IsType<Struck>(Turns.Move(rex, new PlayerAction.Attack(target.Id)).Events.Single());
        Assert.Equal(Expected(Stats.Of(rex).Attack, 1, Roll(rex), target, crushing: true), struck.Damage);

        // ...where anyone else loses 30% of the blow.
        var stalker = Facing("Carnotaurus", PredatorType.Tyrannosaurus, Right(1));
        var other = Assert.IsType<Struck>(Turns.Move(stalker, new PlayerAction.Attack(stalker.Creatures[0].Id)).Events.Single());
        Assert.Equal(Expected(Stats.Of(stalker).Attack, 1, Roll(stalker), stalker.Creatures[0]), other.Damage);
        Assert.Equal(0.7, Fights.Armour(Fights.DefenseOf(stalker.Creatures[0])), 10);
    }

    // ---- The boosts, alone ---------------------------------------------------------------

    /// <summary>The boost of a hit by <paramref name="run"/>'s dino on its first creature.</summary>
    private static double BoostOf(RunState run, bool ranged = false, int streak = 0) =>
        Fights.Boost(run, Stats.Of(run), run.Creatures[0], ranged, streak);

    [Theory]
    [InlineData("Dryosaurus", Terrain.Forest, true, 1.5)]    // Ambusher, from cover
    [InlineData("Dryosaurus", Terrain.Grass, true, 1.0)]     // not in the open
    [InlineData("Dryosaurus", Terrain.Forest, false, 1.0)]   // and only with the instinct
    [InlineData("Triceratops", Terrain.Forest, true, 1.0)]   // a Tank is no cover in a forest
    [InlineData("Herrerasaurus", Terrain.Forest, false, 2.0)] // Pounce, from cover
    [InlineData("Herrerasaurus", Terrain.Grass, false, 1.0)]
    [InlineData("Deinosuchus", Terrain.Water, false, 2.0)]   // Lunge, from the water
    [InlineData("Deinosuchus", Terrain.Grass, false, 1.0)]
    [InlineData("Pteranodon", Terrain.Water, false, 1.0)]    // over water is no lunge without the perk
    public void EachBoostFromWhereYouStand(string species, object ground, bool ambush, double boost)
    {
        var run = Facing(species, PredatorType.Allosaurus, Right(1), ground: (Terrain)ground);
        if (ambush) run = run.Picked(Instinct.Ambush);
        Assert.Equal(boost, BoostOf(run), 10);
    }

    [Fact]
    public void FreezesStillnessIsNoCover()
    {
        var run = Facing("Hypsilophodon", PredatorType.Allosaurus, Right(1)).Picked(Instinct.Ambush);
        run.You.LastMove = MoveKind.Rest;
        Assert.True(Sight.Hidden(run, Stats.Of(run)));
        Assert.Equal(1.0, BoostOf(run), 10);
    }

    [Fact]
    public void ChargeDoublesOnlyAFirstHit()
    {
        var run = Facing("Diabloceratops", PredatorType.Allosaurus, Right(1));
        Assert.Equal(Tuning.ChargeBoost, BoostOf(run), 10);
        run.Creatures[0].HitByYou = true;
        Assert.Equal(1.0, BoostOf(run), 10);
    }

    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(1, 1.25)]
    [InlineData(2, 1.5)]
    [InlineData(3, 1.75)]
    [InlineData(4, 2.0)]
    [InlineData(5, 2.0)]   // four hits counted at most
    [InlineData(9, 2.0)]
    public void LatchGrowsAQuarterAHitInARowUpToDouble(int streak, double boost)
    {
        var run = Facing("Omniraptor", PredatorType.Allosaurus, Right(1));
        Assert.Equal(boost, BoostOf(run, streak: streak), 10);
        Assert.Equal(1.0, BoostOf(Facing("Herrerasaurus", PredatorType.Allosaurus, Right(1)), streak: streak), 10);   // only with Latch
    }

    [Fact]
    public void SpitFromTwoCellsIsSixtyPercent()
    {
        Assert.Equal(Tuning.SpitShare, BoostOf(Facing("Dilophosaurus", PredatorType.Allosaurus, Right(2)), ranged: true), 10);
    }

    // ---- The boosts, together -------------------------------------------------------------

    [Theory]
    [InlineData("Herrerasaurus", Terrain.Forest, 0, true, 3.0)]   // Ambusher x Pounce
    [InlineData("Deinosuchus", Terrain.Water, 0, true, 3.0)]      // Ambusher x Lunge (water is a swimmer's cover)
    [InlineData("Omniraptor", Terrain.Forest, 4, true, 3.0)]      // Ambusher x Latch's double
    [InlineData("Omniraptor", Terrain.Forest, 2, true, 2.25)]
    [InlineData("Diabloceratops", Terrain.Forest, 0, true, 3.0)]  // Ambusher x Charge
    public void BoostsMultiply(string species, object ground, int streak, bool ambush, double boost)
    {
        var run = Facing(species, PredatorType.Allosaurus, Right(1), ground: (Terrain)ground);
        if (ambush) run = run.Picked(Instinct.Ambush);
        Assert.Equal(boost, BoostOf(run, streak: streak), 10);
    }

    [Fact]
    public void ASpitFromCoverWithAmbusherIsStillUnderOne()
    {
        var run = Facing("Dilophosaurus", PredatorType.Allosaurus, Right(2), ground: Terrain.Forest).Picked(Instinct.Ambush);
        Assert.Equal(Tuning.AmbusherBoost * Tuning.SpitShare, BoostOf(run, ranged: true), 10);
    }

    [Fact]
    public void NoBoostIsEverAboveTheCap()
    {
        // Every species, from every ground, with and without Ambusher, on a creature hit
        // before or not, near or far, at every streak: the product never passes MaxBoost.
        foreach (var species in SpeciesTable.All)
        foreach (var ground in new[] { Terrain.Grass, Terrain.Forest, Terrain.Water })
        foreach (var ambush in new[] { false, true })
        foreach (var hitBefore in new[] { false, true })
        foreach (var ranged in new[] { false, true })
        for (var streak = 0; streak <= Tuning.LatchMost + 1; streak++)
        {
            var run = Facing(species.Name, PredatorType.Allosaurus, Right(1), ground: ground);
            if (ambush) run = run.Picked(Instinct.Ambush);
            run.Creatures[0].HitByYou = hitBefore;
            Assert.InRange(BoostOf(run, ranged, streak), 0, Tuning.MaxBoost);
        }
    }

    [Fact]
    public void ABoostedHitSaysSoAndDealsItsShare()
    {
        var run = Facing("Herrerasaurus", PredatorType.Allosaurus, Right(1), ground: Terrain.Forest);
        var target = run.Creatures[0];
        var struck = Assert.IsType<Struck>(Turns.Move(run, new PlayerAction.Attack(target.Id)).Events.Single());
        Assert.True(struck.Boosted);
        Assert.Equal(Expected(Stats.Of(run).Attack, Tuning.PounceBoost, Roll(run), target), struck.Damage);
    }

    // ---- Charge and Latch over several hits -------------------------------------------

    [Fact]
    public void ChargeIsSpentOnTheCreatureItHit()
    {
        var run = Facing("Diabloceratops", PredatorType.Allosaurus, Right(1));
        var id = run.Creatures[0].Id;
        var rng = new Rng(run.Rng);
        var attack = Stats.Of(run).Attack;

        var first = Assert.IsType<Struck>(Hit(run, rng, id).Events.Single());
        Assert.True(first.Boosted);
        Assert.Equal(Expected(attack, Tuning.ChargeBoost, Roll(run), run.Creatures[0]), first.Damage);
        Assert.True(run.Creatures[0].HitByYou);

        var second = Assert.IsType<Struck>(Hit(run, rng, id).Events.Single());
        Assert.False(second.Boosted);
        Assert.Equal(Expected(attack, 1, Roll(run, skip: 1), run.Creatures[0]), second.Damage);
    }

    [Fact]
    public void LatchCountsHitsInARowOnOneCreatureAndAnotherTargetStartsAgain()
    {
        var run = Facing("Omniraptor", PredatorType.Allosaurus, Right(1)).Put(PredatorType.Allosaurus, Right(-1));
        var first = run.Creatures[0].Id;
        var second = run.Creatures[1].Id;
        var rng = new Rng(run.Rng);
        var attack = Stats.Of(run).Attack;
        var rolls = 0;

        var boosts = new[] { 1.0, 1.25, 1.5, 1.75, 2.0, 2.0 };
        for (var hit = 0; hit < boosts.Length; hit++)
        {
            var struck = Assert.IsType<Struck>(Hit(run, rng, first).Events.Single());
            Assert.Equal(Expected(attack, boosts[hit], Roll(run, rolls++), run.Creatures[0]), struck.Damage);
            Assert.Equal(boosts[hit] > 1, struck.Boosted);
            Assert.Equal(first, run.You.LatchOn);
            Assert.Equal(hit + 1, run.You.LatchStreak);
        }

        // A hit on another creature: its streak starts at that hit.
        var other = Assert.IsType<Struck>(Hit(run, rng, second).Events.Single());
        Assert.False(other.Boosted);
        Assert.Equal(Expected(attack, 1, Roll(run, rolls++), run.Creatures[1]), other.Damage);
        Assert.Equal(second, run.You.LatchOn);
        Assert.Equal(1, run.You.LatchStreak);

        // And back on the first, the old streak is gone.
        Assert.False(Assert.IsType<Struck>(Hit(run, rng, first).Events.Single()).Boosted);
        Assert.Equal(1, run.You.LatchStreak);
    }

    [Theory]
    [InlineData(typeof(PlayerAction.Rest))]
    [InlineData(typeof(PlayerAction.Move))]
    [InlineData(typeof(PlayerAction.Eat))]
    [InlineData(typeof(PlayerAction.Drink))]
    public void AnyOtherMoveBreaksTheStreak(Type kind)
    {
        var run = Facing("Omniraptor", PredatorType.Allosaurus, Right(1)).Ground(Right(-1), Terrain.Water);
        run.You.LatchOn = run.Creatures[0].Id;
        run.You.LatchStreak = 3;
        run.Meat[Middle] = 1;
        run.You.Food = 50;
        run.You.Water = 50;
        PlayerAction action = kind == typeof(PlayerAction.Move) ? new PlayerAction.Move(Island.Grid.Neighbours(Middle)[0])
            : kind == typeof(PlayerAction.Eat) ? new PlayerAction.Eat()
            : kind == typeof(PlayerAction.Drink) ? new PlayerAction.Drink()
            : new PlayerAction.Rest();
        var after = Turns.Move(run, action).After;
        Assert.Equal(0, after.You.LatchOn);
        Assert.Equal(0, after.You.LatchStreak);
        // Through the whole turn too.
        Assert.Equal(0, Turns.Must(run, action).After.You.LatchStreak);
    }

    [Fact]
    public void ChoosingThePaceIsNoMoveAndKeepsTheStreak()
    {
        var run = Facing("Omniraptor", PredatorType.Allosaurus, Right(1));
        run.You.LatchOn = run.Creatures[0].Id;
        run.You.LatchStreak = 3;
        Assert.Equal(3, Turns.Must(run, new PlayerAction.SetPace(Pace.Sneak)).After.You.LatchStreak);
    }

    // ---- What a hit leaves on a creature that lives -------------------------------------

    [Fact]
    public void HeadbuttDazesThenNotAgainForThreeTurnsThenAgain()
    {
        var run = Facing("Pachycephalosaurus", PredatorType.Allosaurus, Right(1));
        var target = run.Creatures[0];
        var rng = new Rng(run.Rng);
        for (var turn = 0; turn <= 6; turn++)
        {
            run.Turn = turn;
            target.Dazed = false;   // as its own action would have spent it
            var (events, _) = Hit(run, rng, target.Id);
            var dazes = turn % Tuning.HeadbuttAgainAfter == 0;
            Assert.IsType<Struck>(events[0]);
            if (dazes)
            {
                Assert.Equal(new Afflicted(target.Id, Affliction.Dazed), Assert.Single(events.Skip(1)));
                Assert.True(target.Dazed);
                Assert.Equal(turn + Tuning.HeadbuttAgainAfter, target.DazeFreeAt);
            }
            else
            {
                Assert.Single(events);
                Assert.False(target.Dazed);
            }
        }
    }

    [Fact]
    public void VenomSlowsForTwoActions()
    {
        var run = Facing("Troodon", PredatorType.Allosaurus, Right(1));
        var target = run.Creatures[0];
        var (after, events, _, _) = Turns.Move(run, new PlayerAction.Attack(target.Id));
        Assert.IsType<Struck>(events[0]);
        Assert.Equal(new Afflicted(target.Id, Affliction.Slowed), events[1]);
        Assert.Equal(2, events.Count);
        Assert.Equal(Tuning.VenomActions, after.Creatures[0].Slowed);

        // A second hit sets it again, it does not add.
        after.Creatures[0].Slowed = 1;
        Hit(after, new Rng(after.Rng), target.Id);
        Assert.Equal(Tuning.VenomActions, after.Creatures[0].Slowed);
    }

    [Fact]
    public void BleederBleedsAQuarterOfTheHitForThreeActionsAndANewHitReplacesIt()
    {
        var run = Facing("Allosaurus", PredatorType.Allosaurus, Right(1));
        var target = run.Creatures[0];
        var (after, events, _, _) = Turns.Move(run, new PlayerAction.Attack(target.Id));
        var struck = Assert.IsType<Struck>(events[0]);
        Assert.Equal(new Afflicted(target.Id, Affliction.Bleeding), events[1]);
        var bled = after.Creatures[0];
        Assert.Equal(Tuning.BleedActions, bled.Bleed);
        Assert.Equal((int)Math.Max(1, Tuning.Round(struck.Damage * Tuning.BleedShare)), bled.BleedDamage);

        bled.Bleed = 1;
        bled.BleedDamage = 99;
        var (again, _) = Hit(after, new Rng(after.Rng), target.Id);
        Assert.Equal(Tuning.BleedActions, bled.Bleed);
        Assert.Equal((int)Math.Max(1, Tuning.Round(((Struck)again[0]).Damage * Tuning.BleedShare)), bled.BleedDamage);
    }

    [Fact]
    public void ASmallHitStillBleedsAPoint()
    {
        var run = Facing("Allosaurus", PredatorType.Allosaurus, Right(1));
        var weak = Stats.Of(run) with { Attack = 0.1 };
        Hit(run, new Rng(run.Rng), run.Creatures[0].Id, weak);
        Assert.Equal(1, run.Creatures[0].BleedDamage);
    }

    [Theory]
    [InlineData("Herrerasaurus")]
    [InlineData("Carnotaurus")]
    [InlineData("Tyrannosaurus")]
    public void AHitWithNoSuchPerkLeavesNothing(string species)
    {
        var run = Facing(species, PredatorType.Allosaurus, Right(1));
        var (after, events, _, _) = Turns.Move(run, new PlayerAction.Attack(run.Creatures[0].Id));
        Assert.Single(events);
        var target = after.Creatures[0];
        Assert.False(target.Dazed);
        Assert.Equal(0, target.Slowed);
        Assert.Equal(0, target.Bleed);
        Assert.True(target.HitByYou);
    }

    [Theory]
    [InlineData("Pachycephalosaurus")]
    [InlineData("Troodon")]
    [InlineData("Allosaurus")]
    public void ACreatureThatFallsTakesNoAffliction(string species)
    {
        var run = Facing(species, PredatorType.Troodon, Right(1));
        run.Creatures[0].Health = 1;
        var (after, events, _, _) = Turns.Move(run, new PlayerAction.Attack(run.Creatures[0].Id));
        Assert.True(Assert.IsType<Struck>(events.Single()).Defeated);
        Assert.Empty(after.Creatures);
    }

    // ---- A creature that falls ----------------------------------------------------------

    [Theory]
    [InlineData(PredatorType.Troodon, 1)]
    [InlineData(PredatorType.Herrerasaurus, 2)]
    [InlineData(PredatorType.Carnotaurus, 2)]
    [InlineData(PredatorType.Allosaurus, 3)]
    public void ADefeatedPredatorLeavesItsMeatAndCounts(object type, int meat)
    {
        var run = Facing("Herrerasaurus", (PredatorType)type, Right(1));
        run.Creatures[0].Health = 1;
        run.Meat[Right(1)] = 1;   // meat already there: the carcass grows
        var (after, events, _, _) = Turns.Move(run, new PlayerAction.Attack(run.Creatures[0].Id));
        var struck = Assert.IsType<Struck>(events.Single());
        Assert.True(struck.Defeated);
        Assert.Equal(Right(1), struck.At);
        Assert.Empty(after.Creatures);
        Assert.Equal(1 + meat, after.Meat[Right(1)]);
        Assert.Equal(run.Kills + 1, after.Kills);
        Assert.Null(after.Over);
    }

    [Fact]
    public void CaughtSmallGameLeavesAMealAndIsNoKill()
    {
        var run = Turns.Board("Herrerasaurus", growth: 75).PutSmallGame(Right(1));
        var (after, events, _, _) = Turns.Move(run, new PlayerAction.Attack(run.Creatures[0].Id));
        Assert.True(Assert.IsType<Struck>(events.Single()).Defeated);   // 6 health against an adult's bite
        Assert.Empty(after.Creatures);
        Assert.Equal(Tuning.SmallGameMeat, after.Meat[Right(1)]);
        Assert.Equal(run.Kills, after.Kills);
    }

    [Fact]
    public void DefeatRemovesTheCreatureAndCountsOnlyAPredatorThatIsNotTheApex()
    {
        var run = Turns.Board("Herrerasaurus").Put(PredatorType.Troodon, Right(1)).PutSmallGame(Right(2))
            .Put(PredatorType.Tyrannosaurus, Right(3));
        var (troodon, game, apex) = (run.Creatures[0], run.Creatures[1], run.Creatures[2]);
        Fights.Defeat(run, game);
        Assert.Equal(0, run.Kills);
        Fights.Defeat(run, troodon);
        Assert.Equal(1, run.Kills);
        Fights.Defeat(run, apex);
        Assert.Equal(1, run.Kills);   // the apex is a win, not a kill
        Assert.Empty(run.Creatures);
        Assert.Equal(new[] { 1, 1, 4 }, new[] { run.Meat[Right(1)], run.Meat[Right(2)], run.Meat[Right(3)] });
    }

    // ---- Thagomizer ---------------------------------------------------------------------

    [Fact]
    public void ThagomizerStrikesEveryOtherCreatureBesideYouWithARollOfItsOwn()
    {
        var near = Island.Grid.Neighbours(Middle);
        // Ids in this order: a Troodon beside, the target, small game beside, a Troodon two away.
        var run = Turns.Board("Stegosaurus", growth: 75).Put(PredatorType.Troodon, near[0])
            .Put(PredatorType.Allosaurus, Right(1)).PutSmallGame(near[4]).Put(PredatorType.Troodon, Right(-2));
        var (troodon, target, game, far) = (run.Creatures[0], run.Creatures[1], run.Creatures[2], run.Creatures[3]);
        var stats = Stats.Of(run);
        var noise = Moves.NoiseOf(stats, Tuning.AttackNoise);   // a Tank's +1

        var (after, events, _, _) = Turns.Move(run, new PlayerAction.Attack(target.Id));
        Assert.Equal(3, events.Count);
        var main = Assert.IsType<Struck>(events[0]);
        Assert.Equal(new Struck(target.Id, Right(1), Expected(stats.Attack, 1, Roll(run), target), false, false, false, false, noise), main);

        // Then the others in id order, a swing each after the target's, 60% and no boost, no noise.
        var sweptTroodon = Assert.IsType<Struck>(events[1]);
        var troodonDamage = Expected(stats.Attack, Tuning.SweepShare, Roll(run, 1), troodon);
        Assert.Equal(new Struck(troodon.Id, near[0], troodonDamage, false, false, true, false, 0), sweptTroodon);
        var sweptGame = Assert.IsType<Struck>(events[2]);
        Assert.Equal(game.Id, sweptGame.Creature);
        Assert.Equal(Expected(stats.Attack, Tuning.SweepShare, Roll(run, 2), game), sweptGame.Damage);
        Assert.True(sweptGame.Swept);
        Assert.True(sweptGame.Defeated);   // 6 health against an adult Stegosaurus's sweep

        var left = after.Creatures.ToDictionary(c => c.Id);
        Assert.Equal(troodon.Health - troodonDamage, left[troodon.Id].Health);
        Assert.False(left.ContainsKey(game.Id));
        Assert.Equal(Tuning.SmallGameMeat, after.Meat[near[4]]);
        Assert.Equal(far.Health, left[far.Id].Health);   // two away: untouched
        Assert.True(left[target.Id].HitByYou);
        Assert.False(left[troodon.Id].HitByYou);          // a sweep is not a hit of yours on it
        Assert.Equal(target.Id, after.You.LatchOn);

        // Three swings: the generator moved three rolls.
        var rolled = new Rng(run.Rng);
        for (var i = 0; i < 3; i++) rolled.NextDouble();
        Assert.Equal(rolled.State, Turns.Move(run, new PlayerAction.Attack(target.Id)).Rng.State);
    }

    [Fact]
    public void WithoutThagomizerOnlyTheTargetIsStruck()
    {
        var near = Island.Grid.Neighbours(Middle);
        var run = Turns.Board("Triceratops", growth: 75).Put(PredatorType.Troodon, near[0]).Put(PredatorType.Allosaurus, Right(1));
        var events = Turns.Move(run, new PlayerAction.Attack(run.Creatures[1].Id)).Events;
        Assert.Equal(run.Creatures[1].Id, Assert.IsType<Struck>(events.Single()).Creature);
    }

    // ---- The apex's fall ------------------------------------------------------------------

    [Fact]
    public void DefeatingTheApexEndsTheRunWonWithNoWorldsTurn()
    {
        var run = Facing("Herrerasaurus", PredatorType.Tyrannosaurus, Right(1));
        var apex = run.Creatures[0];
        apex.Health = 1;
        var result = Turns.Must(run, new PlayerAction.Attack(apex.Id));
        Assert.Equal(2, result.Events.Count);
        Assert.True(Assert.IsType<Struck>(result.Events[0]).Defeated);
        Assert.Equal(new RunEnded(true, EndReason.DefeatedApex, 0), result.Events[1]);
        var over = Assert.IsType<RunOver>(result.After.Over);
        Assert.True(over.Won);
        Assert.Equal(EndReason.DefeatedApex, over.Reason);
        Assert.Equal(run.Turn, result.After.Turn);   // no world's turn
        Assert.Equal(run.Kills, result.After.Kills);
        Assert.Equal(Predators.Of(PredatorType.Tyrannosaurus).Meat, result.After.Meat[Right(1)]);
        Assert.Equal(MoveKind.Attack, result.After.You.LastMove);

        var copy = run.Clone();
        Assert.True(Hit(copy, new Rng(copy.Rng), apex.Id).ApexFell);
    }

    [Fact]
    public void AHitThatDoesNotFellTheApexIsNoWin()
    {
        var run = Facing("Herrerasaurus", PredatorType.Tyrannosaurus, Right(1));
        Assert.False(Hit(run.Clone(), new Rng(run.Rng), run.Creatures[0].Id).ApexFell);
        run.Creatures[0].Type = PredatorType.Allosaurus;
        run.Creatures[0].Health = 1;
        Assert.False(Hit(run.Clone(), new Rng(run.Rng), run.Creatures[0].Id).ApexFell);   // a fall, but not the apex's
    }

    [Fact]
    public void ASweepThatFellsTheApexWinsAndTheWholeBlowLands()
    {
        var near = Island.Grid.Neighbours(Middle);
        var run = Turns.Board("Stegosaurus", growth: 75).Put(PredatorType.Troodon, Right(1)).Put(PredatorType.Tyrannosaurus, near[0])
            .Put(PredatorType.Troodon, near[5]);
        foreach (var creature in run.Creatures) creature.Health = 1;
        var result = Turns.Must(run, new PlayerAction.Attack(run.Creatures[0].Id));
        Assert.Equal(4, result.Events.Count);
        Assert.All(result.Events.Take(3), e => Assert.True(Assert.IsType<Struck>(e).Defeated));
        Assert.Equal(new RunEnded(true, EndReason.DefeatedApex, 0), result.Events[3]);
        Assert.Equal(run.Kills + 2, result.After.Kills);   // both Troodons; the apex is the win
        Assert.Equal(run.Turn, result.After.Turn);
    }
}

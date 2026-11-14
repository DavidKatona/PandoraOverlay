using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// Walk, sneak, sprint, eat, drink and rest (<see cref="Moves"/> and your half of
/// <see cref="TurnRules"/>), on hand-built boards: who can go where, by which
/// cells, the noise with the build and the drain, and what each meal, drink and
/// rest gives. Every move is looked at apart from the world's turn after it
/// (<see cref="Turns.Move"/>), with the run given held unchanged.
/// </summary>
public class MovesTests
{
    private const int Middle = Turns.Middle;
    private static int Right(int steps) => Turns.Right(steps);

    // ---- Where you can go ------------------------------------------------------------

    [Fact]
    public void AWalkGoesToEveryNeighbourInRisingOrder()
    {
        var run = Turns.Board("Dryosaurus");
        var targets = Moves.Targets(run, Stats.Of(run));
        Assert.Equal(Island.Grid.Neighbours(Middle).OrderBy(c => c), targets);
        Assert.Equal(targets, Moves.Targets(run.Paced(Pace.Sneak), Stats.Of(run)));   // a sneak goes as far as a walk
    }

    [Theory]
    [InlineData("Herrerasaurus", false, false)]
    [InlineData("Triceratops", false, false)]
    [InlineData("Deinosuchus", false, false)]
    [InlineData("Pteranodon", true, true)]     // a flyer is the only one over rock
    public void RockIsOpenOnlyToAFlyer(string species, bool walk, bool sprint)
    {
        var run = Turns.Board(species).Ground(Right(1), Terrain.Rock);
        var stats = Stats.Of(run);
        Assert.Equal(walk, Moves.OpenToYou(run, stats, Right(1), sprinting: false));
        Assert.Equal(sprint, Moves.OpenToYou(run, stats, Right(1), sprinting: true));
        Assert.Equal(walk, Moves.Targets(run, stats).Contains(Right(1)));
        Assert.Equal(sprint, Moves.Targets(run.Paced(Pace.Sprint), stats).Contains(Right(1)));
    }

    [Theory]
    [InlineData("Herrerasaurus", true, false)]   // wades in, can't sprint through
    [InlineData("Beipiaosaurus", true, false)]   // the Wader wades free, and still can't sprint in water
    [InlineData("Deinosuchus", true, true)]      // a swimmer
    [InlineData("Pteranodon", true, true)]       // a flyer
    public void WaterIsWalkedIntoByAllAndSprintedOnlyBySwimmersAndFlyers(string species, bool walk, bool sprint)
    {
        var run = Turns.Board(species).Ground(Right(1), Terrain.Water);
        var stats = Stats.Of(run);
        Assert.Equal(walk, Moves.OpenToYou(run, stats, Right(1), sprinting: false));
        Assert.Equal(sprint, Moves.OpenToYou(run, stats, Right(1), sprinting: true));
        Assert.Equal(sprint, Moves.Targets(run.Paced(Pace.Sprint), stats).Contains(Right(1)));
    }

    [Theory]
    [InlineData(Terrain.Grass)]
    [InlineData(Terrain.Forest)]
    [InlineData(Terrain.Rock)]
    public void ASwimmerSprintsOnlyInWater(object ground)
    {
        var run = Turns.Board("Deinosuchus");
        var stats = Stats.Of(run);
        Assert.False(Moves.OpenToYou(run.Ground(Right(1), (Terrain)ground), stats, Right(1), sprinting: true));
        // A river along your row: the sprint keeps to it.
        var river = Turns.Board("Deinosuchus").Paced(Pace.Sprint);
        for (var c = Right(-5); c <= Right(5); c++) river.Tiles[c] = Terrain.Water;
        Assert.Equal(new[] { Right(-2), Right(-1), Right(1), Right(2) }, Moves.Targets(river, stats));
    }

    [Fact]
    public void AFlyersSprintIsAsItsWalk()
    {
        var run = Turns.Board("Pteranodon").Ground(Right(1), Terrain.Rock).Ground(Right(2), Terrain.Water).Paced(Pace.Sprint);
        var stats = Stats.Of(run);
        var targets = Moves.Targets(run, stats);
        Assert.Contains(Right(1), targets);
        Assert.Contains(Right(2), targets);
        Assert.Contains(Right(3), targets);   // the Flyer's range is 3
    }

    [Fact]
    public void ACreatureClosesItsCellToAWalk()
    {
        var predator = Turns.Board("Dryosaurus").Put(PredatorType.Troodon, Right(1));
        Assert.DoesNotContain(Right(1), Moves.Targets(predator, Stats.Of(predator)));
        Assert.Equal(5, Moves.Targets(predator, Stats.Of(predator)).Count);
        var game = Turns.Board("Dryosaurus").PutSmallGame(Right(1));
        Assert.DoesNotContain(Right(1), Moves.Targets(game, Stats.Of(game)));
        Turns.MoveRefused(predator, new PlayerAction.Move(Right(1)));
    }

    [Fact]
    public void ASprintCannotPassThroughACreature()
    {
        // A corridor: rock everywhere but your cell and the three to your right.
        var corridor = Turns.Board("Dryosaurus", Terrain.Rock).Paced(Pace.Sprint);
        for (var c = Middle; c <= Right(3); c++) corridor.Tiles[c] = Terrain.Grass;
        var stats = Stats.Of(corridor);
        Assert.Equal(new[] { Right(1), Right(2) }, Moves.Targets(corridor, stats));   // the Runner's range is 2

        var blocked = corridor.Put(PredatorType.Troodon, Right(1));
        Assert.Empty(Moves.Targets(blocked, stats));
        Turns.MoveRefused(blocked, new PlayerAction.Move(Right(2)));
        var gameInTheWay = corridor.PutSmallGame(Right(1));
        Assert.Empty(Moves.Targets(gameInTheWay, stats));
    }

    [Theory]
    [InlineData("Dryosaurus", 0, false, 2)]
    [InlineData("Dryosaurus", 1, false, 3)]    // Escape artist
    [InlineData("Dryosaurus", 0, true, 3)]     // Long stride
    [InlineData("Dryosaurus", 1, true, 4)]     // both
    [InlineData("Carnotaurus", 0, false, 3)]   // Fleet
    [InlineData("Carnotaurus", 1, true, 5)]    // Fleet, Escape artist and Long stride
    [InlineData("Pteranodon", 0, false, 3)]    // the Flyer's own
    public void TheSprintReachesItsRangeAndNoFurther(string species, int escapeArtist, bool longStride, int range)
    {
        var run = Turns.Board(species).With(Runs.Ranks((Trait.EscapeArtist, escapeArtist)), stars: 0).Paced(Pace.Sprint);
        if (longStride) run = run.Picked(Instinct.Stride);
        var stats = Stats.Of(run);
        Assert.Equal(range, stats.SprintRange);
        var expected = Island.Grid.Within(Middle, range).Where(c => c != Middle).OrderBy(c => c);
        Assert.Equal(expected, Moves.Targets(run, stats));
    }

    // ---- The way there ----------------------------------------------------------------

    [Fact]
    public void AWalksPathIsTheOneCell()
    {
        var run = Turns.Board("Dryosaurus");
        foreach (var to in Moves.Targets(run, Stats.Of(run)))
        {
            Assert.Equal(new[] { to }, Moves.PathTo(run, Stats.Of(run), to));
            var walked = Assert.IsType<Walked>(Turns.Move(run, new PlayerAction.Move(to)).Events.Single());
            Assert.Equal(new[] { to }, walked.Path);
            Assert.Equal(Middle, walked.From);
        }
    }

    [Fact]
    public void ASprintsPathStepsFromNeighbourToNeighbourAndEndsAtTheTarget()
    {
        var run = Turns.Board("Carnotaurus").With(Runs.Ranks((Trait.EscapeArtist, 1)), stars: 0).Picked(Instinct.Stride)
            .Paced(Pace.Sprint);
        var stats = Stats.Of(run);
        foreach (var to in Moves.Targets(run, stats))
        {
            var path = Moves.PathTo(run, stats, to);
            Assert.Equal(Island.Grid.Distance(Middle, to), path.Count);   // the shortest way, on open ground
            Assert.Equal(to, path[^1]);
            var at = Middle;
            foreach (var cell in path)
            {
                Assert.Equal(1, Island.Grid.Distance(at, cell));
                at = cell;
            }
            var walked = Assert.IsType<Walked>(Turns.Move(run, new PlayerAction.Move(to)).Events.Single());
            Assert.Equal(path, walked.Path);
            Assert.Equal(to, Turns.Move(run, new PlayerAction.Move(to)).After.You.At);
        }
    }

    [Fact]
    public void ASprintGoesRoundRockAndACreatureWithinItsRange()
    {
        // The cell two to your right has one shortest way, through the cell between:
        // with rock there, it is three steps away by any other.
        var run = Turns.Board("Dryosaurus").Ground(Right(1), Terrain.Rock).Paced(Pace.Sprint);
        Assert.DoesNotContain(Right(2), Moves.Targets(run, Stats.Of(run)));   // range 2

        var further = run.With(Runs.Ranks((Trait.EscapeArtist, 1)), stars: 0);   // range 3
        var stats = Stats.Of(further);
        var path = Moves.PathTo(further, stats, Right(2));
        Assert.Equal(3, path.Count);
        Assert.DoesNotContain(Right(1), path);
        Assert.Equal(Right(2), path[^1]);

        var aroundACreature = further.Put(PredatorType.Troodon, path[0]);
        var detour = Moves.PathTo(aroundACreature, stats, Right(2));
        Assert.Equal(3, detour.Count);
        Assert.DoesNotContain(path[0], detour);
    }

    [Fact]
    public void ACellThatIsNoTargetHasNoPath()
    {
        var run = Turns.Board("Dryosaurus");
        var stats = Stats.Of(run);
        Assert.Empty(Moves.PathTo(run, stats, Right(2)));
        Assert.Empty(Moves.PathTo(run, stats, Middle));
        Assert.Empty(Moves.PathTo(run.Paced(Pace.Sprint), stats, Right(3)));
    }

    // ---- Noise and drain ----------------------------------------------------------------

    [Theory]
    [InlineData("Herrerasaurus", Pace.Walk, 0, false, 3)]
    [InlineData("Herrerasaurus", Pace.Sneak, 0, false, 1)]
    [InlineData("Herrerasaurus", Pace.Sprint, 0, false, 5)]
    [InlineData("Dryosaurus", Pace.Walk, 0, false, 2)]       // a Runner is quiet: -1
    [InlineData("Dryosaurus", Pace.Sneak, 0, false, 0)]
    [InlineData("Dryosaurus", Pace.Sprint, 0, false, 4)]
    [InlineData("Tyrannosaurus", Pace.Walk, 0, false, 5)]    // the Apex is loud: +2
    [InlineData("Tyrannosaurus", Pace.Sneak, 0, false, 3)]
    [InlineData("Herrerasaurus", Pace.Walk, 1, false, 2)]    // Light step
    [InlineData("Herrerasaurus", Pace.Walk, 2, false, 1)]
    [InlineData("Herrerasaurus", Pace.Walk, 0, true, 2)]     // Soft feet
    [InlineData("Herrerasaurus", Pace.Walk, 2, true, 0)]
    [InlineData("Dryosaurus", Pace.Sneak, 2, true, 0)]       // 1 - 1 - 2 - 1: never under 0
    [InlineData("Tyrannosaurus", Pace.Sprint, 2, true, 4)]
    public void AMovesNoiseIsItsOwnWithTheBuildNeverUnderZero(string species, object pace, int lightStep, bool softFeet, int noise)
    {
        var run = Turns.Board(species).With(Runs.Ranks((Trait.LightStep, lightStep)), stars: 0).Paced((Pace)pace);
        if (softFeet) run = run.Picked(Instinct.Feet);
        var (_, events, done, _) = Turns.Move(run, new PlayerAction.Move(Right(1)));
        Assert.Equal(noise, Assert.IsType<Walked>(events.Single()).Noise);
        Assert.Equal(noise, done.Noise);
    }

    [Fact]
    public void NoiseOfIsTheMovesWithTheBuildAndNeverUnderZero()
    {
        var stats = Stats.Of(Turns.Board("Herrerasaurus"));
        Assert.Equal(4, Moves.NoiseOf(stats, 4));
        Assert.Equal(3, Moves.NoiseOf(stats with { NoiseMod = -1 }, 4));
        Assert.Equal(0, Moves.NoiseOf(stats with { NoiseMod = -6 }, 4));
        Assert.Equal(0, Moves.NoiseOf(stats with { NoiseMod = -1 }, 0));
        Assert.Equal(2, Moves.NoiseOf(stats with { NoiseMod = 2 }, 0));
    }

    [Theory]
    [InlineData("Herrerasaurus", Pace.Walk, 1.0)]
    [InlineData("Herrerasaurus", Pace.Sneak, 1.5)]
    [InlineData("Herrerasaurus", Pace.Sprint, 1.5)]    // the Hunter's sprint drain
    [InlineData("Triceratops", Pace.Sprint, 2.5)]      // the Tank's
    [InlineData("Dryosaurus", Pace.Sprint, 1.0)]       // Tireless: a walk's
    [InlineData("Carnotaurus", Pace.Sprint, 1.4)]      // Fleet: 2 x 0.7
    public void AMovesDrainIsItsPaces(string species, object pace, double drain)
    {
        var run = Turns.Board(species).Paced((Pace)pace);
        var (_, events, done, _) = Turns.Move(run, new PlayerAction.Move(Right(1)));
        Assert.Equal(drain, done.Drain, 10);
        Assert.False(Assert.IsType<Walked>(events.Single()).Waded);
    }

    [Theory]
    [InlineData("Herrerasaurus", Pace.Walk, true, 4, 1.5)]      // a walk into water: +1 noise, x1.5 drain
    [InlineData("Herrerasaurus", Pace.Sneak, true, 2, 2.25)]    // a sneak's x1.5, then x1.5 more
    [InlineData("Gallimimus", Pace.Walk, true, 3, 1.5)]         // a Forager wades (its -1 noise after)
    [InlineData("Beipiaosaurus", Pace.Walk, false, 2, 1.0)]     // the Wader: no cost
    [InlineData("Beipiaosaurus", Pace.Sneak, false, 0, 1.5)]
    [InlineData("Deinosuchus", Pace.Walk, false, 3, 1.0)]       // a swimmer swims
    [InlineData("Deinosuchus", Pace.Sprint, false, 5, 2.0)]     // and sprints there
    [InlineData("Pteranodon", Pace.Walk, false, 3, 1.0)]        // a flyer flies over
    public void WadingIntoWaterCostsNoiseAndDrain(string species, object pace, bool waded, int noise, double drain)
    {
        var run = Turns.Board(species).Ground(Right(1), Terrain.Water).Paced((Pace)pace);
        var (after, events, done, _) = Turns.Move(run, new PlayerAction.Move(Right(1)));
        var walked = Assert.IsType<Walked>(events.Single());
        Assert.Equal(waded, walked.Waded);
        Assert.Equal(noise, walked.Noise);
        Assert.Equal(drain, done.Drain, 10);
        Assert.Equal(Right(1), after.You.At);
    }

    [Fact]
    public void WalkingOutOfWaterIsNoWade()
    {
        var run = Turns.Board("Herrerasaurus").Ground(Middle, Terrain.Water);
        var walked = Assert.IsType<Walked>(Turns.Move(run, new PlayerAction.Move(Right(1))).Events.Single());
        Assert.False(walked.Waded);
        Assert.Equal(Tuning.WalkNoise, walked.Noise);
    }

    // ---- Eat ----------------------------------------------------------------------------

    [Fact]
    public void EatingAPlantGivesItsMealAndThePlantIsGone()
    {
        var run = Turns.Board("Dryosaurus");
        run.Plants[Middle] = true;
        run.You.Food = 50;
        var (after, events, done, _) = Turns.Move(run, new PlayerAction.Eat());
        Assert.Equal(new Ate(Food.Plants, 30, 0), events.Single());   // noise 1, a Runner's -1
        Assert.Equal(80, after.You.Food, 10);
        Assert.False(after.Plants[Middle]);
        Assert.Equal(Tuning.PlainDrain, done.Drain);
        Assert.Equal(0, done.Noise);
    }

    [Fact]
    public void EatingACarcassTakesOneMealOfIt()
    {
        var run = Turns.Board("Herrerasaurus");
        run.Meat[Middle] = 2;
        run.You.Food = 50;
        var (after, events, _, _) = Turns.Move(run, new PlayerAction.Eat());
        Assert.Equal(new Ate(Food.Meat, 45, 1), events.Single());
        Assert.Equal(95, after.You.Food, 10);
        Assert.Equal(1, after.Meat[Middle]);
        var (last, _, _, _) = Turns.Move(after, new PlayerAction.Eat());
        Assert.Equal(0, last.Meat[Middle]);
    }

    [Fact]
    public void FishIsTheWaterAndStays()
    {
        var run = Turns.Board("Deinosuchus").Ground(Middle, Terrain.Water);
        run.You.Food = 50;
        var (after, events, done, _) = Turns.Move(run, new PlayerAction.Eat());
        Assert.Equal(new Ate(Food.Fish, 25, 2), events.Single());   // fishing splashes: noise 2
        Assert.Equal(75, after.You.Food, 10);
        Assert.Equal(Terrain.Water, after.Tiles[Middle]);
        Assert.Equal(2, done.Noise);
    }

    [Fact]
    public void AWaderEatsFishAndAnotherForagerDoesNot()
    {
        var wader = Turns.Board("Beipiaosaurus").Ground(Middle, Terrain.Water);
        wader.You.Food = 50;
        Assert.Equal(Food.Fish, Moves.FoodHere(wader, Stats.Of(wader)));
        Assert.Equal(Food.Fish, Assert.IsType<Ate>(Turns.Move(wader, new PlayerAction.Eat()).Events.Single()).Kind);

        var forager = Turns.Board("Gallimimus").Ground(Middle, Terrain.Water);
        forager.You.Food = 50;
        Assert.Null(Moves.FoodHere(forager, Stats.Of(forager)));
        Turns.MoveRefused(forager, new PlayerAction.Eat());
    }

    [Fact]
    public void AForagerOnAPlantAndACarcassEatsThePlant()
    {
        var run = Turns.Board("Gallimimus");
        run.Plants[Middle] = true;
        run.Meat[Middle] = 2;
        run.You.Food = 10;
        var (after, events, _, _) = Turns.Move(run, new PlayerAction.Eat());
        Assert.Equal(Food.Plants, Assert.IsType<Ate>(events.Single()).Kind);
        Assert.False(after.Plants[Middle]);
        Assert.Equal(2, after.Meat[Middle]);
    }

    [Theory]
    [InlineData("Dryosaurus", false, Food.Plants, 30)]
    [InlineData("Dryosaurus", true, Food.Plants, 45)]        // Big appetite
    [InlineData("Herrerasaurus", true, Food.Meat, 68)]       // 67.5 goes up (H24)
    [InlineData("Maiasaura", false, Food.Plants, 45)]        // Big bites
    [InlineData("Maiasaura", true, Food.Plants, 68)]         // both: 2.25 times, 67.5 up
    [InlineData("Ceratosaurus", false, Food.Meat, 68)]       // Scavenger, on meat
    [InlineData("Ceratosaurus", false, Food.Plants, 30)]     // ...and on meat only
    [InlineData("Ceratosaurus", true, Food.Meat, 101)]       // with Big appetite: 101.25
    [InlineData("Deinosuchus", false, Food.Fish, 25)]
    [InlineData("Deinosuchus", true, Food.Fish, 38)]         // 37.5 goes up (H24)
    public void AMealsGainIsItsBaseTimesTheMealsFactorsRoundedUpFromAHalf(string species, bool bigAppetite, object food, int gain)
    {
        var run = Turns.Board(species);
        if (bigAppetite) run = run.Picked(Instinct.Appetite);
        Assert.Equal(gain, Moves.MealGain(Stats.Of(run), (Food)food));
    }

    [Theory]
    [InlineData("Maiasaura", true, 68)]
    [InlineData("Ceratosaurus", true, 101)]
    public void AnEatenMealGivesTheMealsGain(string species, bool bigAppetite, int gain)
    {
        var run = Turns.Board(species);
        if (bigAppetite) run = run.Picked(Instinct.Appetite);
        run.Plants[Middle] = true;
        run.Meat[Middle] = 1;
        run.You.Food = 0;
        var (after, events, _, _) = Turns.Move(run, new PlayerAction.Eat());
        var added = (int)Math.Min(Tuning.FoodMost, gain);   // from 0: the meal, held at the bar's most (101 is 100)
        Assert.Equal(added, Assert.IsType<Ate>(events.Single()).Gain);
        Assert.Equal(added, after.You.Food, 10);
    }

    [Fact]
    public void FoodIsHeldAtItsMost()
    {
        var run = Turns.Board("Dryosaurus");
        run.Plants[Middle] = true;
        run.You.Food = 90;
        var (after, events, _, _) = Turns.Move(run, new PlayerAction.Eat());
        Assert.Equal(Tuning.FoodMost, after.You.Food);
        Assert.Equal(10, Assert.IsType<Ate>(events.Single()).Gain);   // what went into the bar (H49), not the meal's 30
        Assert.False(after.Plants[Middle]);                            // and the plant is gone all the same
    }

    [Theory]
    [InlineData(72.4, 28)]   // 27.6 went in: rounded halves up (H24)
    [InlineData(99.6, 0)]    // allowed, and 0.4 is no point
    public void AMealsGainIsWhatWentInEvenAFraction(double food, int gain)
    {
        var run = Turns.Board("Dryosaurus");
        run.Plants[Middle] = true;
        run.You.Food = food;
        var (after, events, _, _) = Turns.Move(run, new PlayerAction.Eat());
        Assert.Equal(Tuning.FoodMost, after.You.Food);
        Assert.Equal(gain, Assert.IsType<Ate>(events.Single()).Gain);
        Assert.False(after.Plants[Middle]);
    }

    [Fact]
    public void EatIsRefusedWithNothingHereAtFullAndForWhatYouDoNotEat()
    {
        var bare = Turns.Board("Dryosaurus");
        bare.You.Food = 50;
        Turns.MoveRefused(bare, new PlayerAction.Eat());

        var full = Turns.Board("Dryosaurus");
        full.Plants[Middle] = true;
        full.You.Food = Tuning.FoodMost;
        Turns.MoveRefused(full, new PlayerAction.Eat());

        var hunterOnAPlant = Turns.Board("Herrerasaurus");
        hunterOnAPlant.Plants[Middle] = true;
        hunterOnAPlant.You.Food = 50;
        Turns.MoveRefused(hunterOnAPlant, new PlayerAction.Eat());

        var runnerOnACarcass = Turns.Board("Dryosaurus");
        runnerOnACarcass.Meat[Middle] = 2;
        runnerOnACarcass.You.Food = 50;
        Turns.MoveRefused(runnerOnACarcass, new PlayerAction.Eat());

        var runnerInWater = Turns.Board("Dryosaurus").Ground(Middle, Terrain.Water);
        runnerInWater.You.Food = 50;
        Turns.MoveRefused(runnerInWater, new PlayerAction.Eat());
    }

    [Fact]
    public void FoodHereIsThePlantThenTheCarcassThenTheWater()
    {
        var run = Turns.Board("Beipiaosaurus").Ground(Middle, Terrain.Water);   // plants, meat and fish
        var stats = Stats.Of(run);
        Assert.Equal(Food.Fish, Moves.FoodHere(run, stats));
        run.Meat[Middle] = 1;
        Assert.Equal(Food.Meat, Moves.FoodHere(run, stats));
        run.Plants[Middle] = true;
        Assert.Equal(Food.Plants, Moves.FoodHere(run, stats));
    }

    // ---- Drink --------------------------------------------------------------------------

    [Fact]
    public void ADrinkOnWaterOrBesideItGivesForty()
    {
        var onWater = Turns.Board("Herrerasaurus").Ground(Middle, Terrain.Water);
        onWater.You.Water = 30;
        var (after, events, done, _) = Turns.Move(onWater, new PlayerAction.Drink());
        Assert.Equal(new Drank(40, 1), events.Single());
        Assert.Equal(70, after.You.Water, 10);
        Assert.Equal(Tuning.PlainDrain, done.Drain);

        foreach (var near in Island.Grid.Neighbours(Middle))
        {
            var beside = Turns.Board("Dryosaurus").Ground(near, Terrain.Water);
            beside.You.Water = 30;
            Assert.True(Moves.WaterNear(beside));
            Assert.Equal(new Drank(40, 0), Turns.Move(beside, new PlayerAction.Drink()).Events.Single());   // a Runner's -1
        }
    }

    [Fact]
    public void ADrinkIsRefusedAwayFromWaterAndAtFull()
    {
        var far = Turns.Board("Herrerasaurus").Ground(Right(2), Terrain.Water);
        far.You.Water = 30;
        Assert.False(Moves.WaterNear(far));
        Turns.MoveRefused(far, new PlayerAction.Drink());

        var full = Turns.Board("Herrerasaurus").Ground(Middle, Terrain.Water);
        full.You.Water = Tuning.WaterMost;
        Turns.MoveRefused(full, new PlayerAction.Drink());
    }

    [Fact]
    public void WaterIsHeldAtItsMost()
    {
        var run = Turns.Board("Herrerasaurus").Ground(Right(1), Terrain.Water);
        run.You.Water = 75;
        var (after, events, _, _) = Turns.Move(run, new PlayerAction.Drink());
        Assert.Equal(Tuning.WaterMost, after.You.Water);
        Assert.Equal(25, Assert.IsType<Drank>(events.Single()).Gain);   // what went in (H49), not the drink's 40
    }

    // ---- Rest -----------------------------------------------------------------------------

    [Theory]
    [InlineData(false, 0.5, 0.58, 8)]
    [InlineData(true, 0.5, 0.66, 16)]     // Fast healer
    [InlineData(false, 0.95, 1.0, 5)]     // held at full: 5 points went in (H49)
    [InlineData(true, 1.0, 1.0, 0)]       // always allowed, and at full it regains nothing
    public void ARestHealsItsShareToFullAtMost(bool fastHealer, double health, double after, int healed)
    {
        var run = Turns.Board("Herrerasaurus");
        if (fastHealer) run = run.Picked(Instinct.Healer);
        run.You.Health = health;
        var (rested, events, done, _) = Turns.Move(run, new PlayerAction.Rest());
        Assert.Equal(after, rested.You.Health, 10);
        Assert.Equal(new Rested(healed, 0), events.Single());
        Assert.Equal(0, done.Noise);
        Assert.Equal(Tuning.PlainDrain, done.Drain);
    }

    [Fact]
    public void EvenARestIsHeardFromALoudBuild()
    {
        // The rest's own noise is 0; the Apex's +2 is added to every move's (the design's NoiseMod).
        var (_, events, done, _) = Turns.Move(Turns.Board("Tyrannosaurus"), new PlayerAction.Rest());
        Assert.Equal(2, Assert.IsType<Rested>(events.Single()).Noise);
        Assert.Equal(2, done.Noise);
    }

    // ---- What a move would add: the button and the event ask the same (H49) --------------

    [Theory]
    [InlineData("Dryosaurus", 0.0)]
    [InlineData("Dryosaurus", 50.0)]
    [InlineData("Dryosaurus", 72.4)]
    [InlineData("Dryosaurus", 99.6)]
    [InlineData("Maiasaura", 40.0)]       // a 45-point meal
    [InlineData("Ceratosaurus", 10.0)]    // a 68-point carcass
    [InlineData("Ceratosaurus", 60.0)]
    [InlineData("Deinosuchus", 85.5)]     // fish
    public void EatGainIsTheNumberTheAteEventCarries(string species, double food)
    {
        var run = Turns.Board(species).Ground(Middle, species == "Deinosuchus" ? Terrain.Water : Terrain.Grass);
        run.Plants[Middle] = true;
        run.Meat[Middle] = 1;
        run.You.Food = food;
        var stats = Stats.Of(run);
        var gain = Moves.EatGain(run, stats);
        var (after, events, _, _) = Turns.Move(run, new PlayerAction.Eat());
        Assert.Equal(gain, Assert.IsType<Ate>(events.Single()).Gain);
        Assert.Equal((int)Tuning.Round(after.You.Food - food), gain);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(30.0)]
    [InlineData(60.0)]
    [InlineData(60.5)]
    [InlineData(99.9)]
    public void DrinkGainIsTheNumberTheDrankEventCarries(double water)
    {
        var run = Turns.Board("Herrerasaurus").Ground(Right(1), Terrain.Water);
        run.You.Water = water;
        var gain = Moves.DrinkGain(run);
        var (after, events, _, _) = Turns.Move(run, new PlayerAction.Drink());
        Assert.Equal(gain, Assert.IsType<Drank>(events.Single()).Gain);
        Assert.Equal((int)Tuning.Round(after.You.Water - water), gain);
    }

    [Theory]
    [InlineData(false, 0.1)]
    [InlineData(false, 0.5)]
    [InlineData(false, 0.95)]
    [InlineData(false, 0.997)]
    [InlineData(false, 1.0)]
    [InlineData(true, 0.5)]
    [InlineData(true, 0.9)]
    public void RestGainIsTheNumberTheRestedEventCarries(bool fastHealer, double health)
    {
        var run = Turns.Board("Herrerasaurus");
        if (fastHealer) run = run.Picked(Instinct.Healer);
        run.You.Health = health;
        var gain = Moves.RestGain(run, Stats.Of(run));
        var (after, events, _, _) = Turns.Move(run, new PlayerAction.Rest());
        Assert.Equal(gain, Assert.IsType<Rested>(events.Single()).Healed);
        Assert.Equal((int)Tuning.Round((after.You.Health - health) * 100), gain);
    }

    [Fact]
    public void AGainIsZeroWhenItsMoveIsRefused()
    {
        var bare = Turns.Board("Dryosaurus");
        bare.You.Food = 50;
        Assert.Equal(0, Moves.EatGain(bare, Stats.Of(bare)));               // nothing you eat here
        var hunter = Turns.Board("Herrerasaurus");
        hunter.Plants[Middle] = true;
        hunter.You.Food = 50;
        Assert.Equal(0, Moves.EatGain(hunter, Stats.Of(hunter)));           // a plant, and you don't eat plants
        var full = Turns.Board("Dryosaurus");
        full.Plants[Middle] = true;
        full.You.Food = Tuning.FoodMost;
        Assert.Equal(0, Moves.EatGain(full, Stats.Of(full)));               // food at its most

        var dry = Turns.Board("Herrerasaurus");
        dry.You.Water = 30;
        Assert.Equal(0, Moves.DrinkGain(dry));                               // no water near
        var sated = Turns.Board("Herrerasaurus").Ground(Middle, Terrain.Water);
        sated.You.Water = Tuning.WaterMost;
        Assert.Equal(0, Moves.DrinkGain(sated));                             // water at its most

        var whole = Turns.Board("Herrerasaurus");
        whole.You.Health = 1;
        Assert.Equal(0, Moves.RestGain(whole, Stats.Of(whole)));            // a rest is never refused, and here adds nothing
    }
}

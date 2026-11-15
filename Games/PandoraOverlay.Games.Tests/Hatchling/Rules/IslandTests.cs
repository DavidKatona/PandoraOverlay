using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The island and the start of a run (<see cref="Island"/>): pinned against the
/// prototype roll for roll, and held over 300 seeds to the shape every run must have.
/// </summary>
public class IslandTests
{
    // ---- The island matches the prototype ------------------------------------------
    //
    // Printed by a Node script that loads the prototype's hatch-rules.js as its own
    // hatch-sim.js does, and calls hxNewRun(meta, "Dryosaurus") on hxNewMeta(1) with
    // meta.runSeed set to the seed (task H02's report has the script and its output).
    // Tiles: g grass, f forest, w water, r rock, cell 0 first. Creatures: S small game,
    // T Troodon, with their cells, in id order. State: the generator's state after
    // the run's last roll (the prototype keeps it in run.seed). If one differs, the
    // port is wrong, not the pin.

    public static IEnumerable<object[]> PrototypeIslands() => new[]
    {
        new object[] { 1u,
            "gggggfwwggggggfffgwggggggfgggwggggrrffgfwwgggfgfwggfwgggffwwfffwrrggffggfgwfrgffgggggwffgfggggggwgfgggggggwwfgggggggwwggg",
            104, "0,8,14,17,25,30,36,39,61,66,78,81,95,105,115,119", "3,22,119", "S39 S87 S11 S30 T26", 1944295608u },
        new object[] { 17u,
            "gggfffwwgggggffffwwgggggffgfwggggggggffwwggggggggffwwggggggggggwgggggggggwwgfggggwwwwwffgfffggggwfggggfgggwwfgggffffgwggg",
            104, "8,9,10,15,16,19,21,31,35,41,56,62,71,94,108,116", "23,30,116", "S98 S47 S101 S62 T33", 3008567073u },
        new object[] { 7919u,
            "ggrgggwgggfgfrgggwggffffgrrgwggffffggrrwwgffwfgggrwwggfwwgfffwgggggggfffwgggggggffwwggggggfgwwgfffgfgffwwgffggffffwwggggg",
            94, "9,30,31,36,43,45,59,66,74,81,91,97,106,107,108,119", "11,42,67", "S22 S19 S31 S99 T57", 1752479935u },
        new object[] { 123456789u,
            "ggrwgffgggggrrwggfffggggwwgffgfffggwggffgffgggwrrfffggggwwrgggggggwwfrgwwggggwfgggwgggggwffggfgggggwfffffgggggwggffffgggg",
            104, "0,4,9,11,29,30,31,36,41,49,51,81,91,109,111,115", "32,90,101", "S68 S64 S65 S38 T32", 2635646869u },
        new object[] { 4294967295u,
            "gggfggffwwgggffggffgwggggfffffgwggggffffggwggggggffffwgrgggfgfffwgrgggggffgwwrgwgggffggwffwwgggggwwfffggggggwggfffgggrrwg",
            104, "2,10,18,25,44,46,49,59,62,63,72,83,86,88,94,99", "16,100,105", "S22 S85 S56 S3 T32", 3016143355u },
    };

    [Theory]
    [MemberData(nameof(PrototypeIslands))]
    public void IslandMatchesThePrototype(uint seed, string tiles, int start, string plants, string meat, string creatures, uint state)
    {
        var run = Runs.Fresh("Dryosaurus", seed);

        Assert.Equal(tiles, string.Concat(run.Tiles.Select(Letter)));
        Assert.Equal(start, run.You.At);
        Assert.Equal(plants, string.Join(",", Enumerable.Range(0, Tuning.Cells).Where(i => run.Plants[i])));
        Assert.Equal(meat, string.Join(",", Enumerable.Range(0, Tuning.Cells).Where(i => run.Meat[i] > 0)));
        Assert.Equal(creatures, string.Join(" ", run.Creatures.Select(c =>
            (c.Kind == CreatureKind.SmallGame ? "S" : c.Type == PredatorType.Troodon ? "T" : "?") + c.At)));
        Assert.Equal(state, run.Rng);   // the same number of rolls, to the last
        Assert.Equal(seed, run.Seed);
    }

    private static char Letter(Terrain ground) => ground switch
    {
        Terrain.Grass => 'g',
        Terrain.Forest => 'f',
        Terrain.Water => 'w',
        Terrain.Rock => 'r',
        _ => '?',
    };

    // ---- 300 seeds: the shape of every start -------------------------------------

    /// <summary>300 seeds spread over the whole range (Knuth's multiplier); the pins above hold the smallest and the top.</summary>
    public static IEnumerable<uint> ManySeeds() =>
        Enumerable.Range(0, 300).Select(i => unchecked((uint)i * 2654435761u + 12345u));

    [Fact]
    public void EveryStartHasTheShapeTheDesignPromises()
    {
        foreach (var seed in ManySeeds())
        {
            var run = Runs.Fresh("Dryosaurus", seed);
            var grid = Island.Grid;
            var you = run.You.At;
            var where = $"seed {seed}";

            // The start is land.
            Assert.True(Island.IsLand(run, you), where);

            // Every cell that is not rock can be walked to from the start without crossing rock.
            var reached = grid.Reach(you, Tuning.Cells, c => run.Tiles[c] != Terrain.Rock);
            for (var cell = 0; cell < Tuning.Cells; cell++)
                if (cell != you && run.Tiles[cell] != Terrain.Rock)
                    Assert.True(reached.ContainsKey(cell), $"{where}: cell {cell} is cut off");

            // 12 to 16 plants, on land, never on the start.
            var plants = Enumerable.Range(0, Tuning.Cells).Where(i => run.Plants[i]).ToList();
            Assert.InRange(plants.Count, 12, Tuning.Plants);
            Assert.All(plants, p => Assert.True(Island.IsLand(run, p) && p != you, where));

            // 3 carcasses of 2 meals, on land, never on the start.
            var meat = Enumerable.Range(0, Tuning.Cells).Where(i => run.Meat[i] > 0).ToList();
            Assert.Equal(Tuning.Carcasses, meat.Count);
            Assert.All(meat, m => Assert.True(run.Meat[m] == Tuning.CarcassMeals && Island.IsLand(run, m) && m != you, where));

            // 4 small game and then one Troodon, each on its own land cell.
            Assert.Equal(Tuning.SmallGame + 1, run.Creatures.Count);
            Assert.Equal(run.Creatures.Count, run.Creatures.Select(c => c.At).Distinct().Count());
            Assert.All(run.Creatures, c => Assert.True(Island.IsLand(run, c.At) && c.At != you, where));
            var smallGame = run.Creatures.Take(Tuning.SmallGame).ToList();
            Assert.All(smallGame, c =>
            {
                Assert.Equal(CreatureKind.SmallGame, c.Kind);
                Assert.True(grid.Distance(c.At, you) >= Tuning.SmallGameDistance, where);
                Assert.Equal(Tuning.SmallGameHealth, c.Health);
                Assert.Equal(Tuning.NoTarget, c.Target);
            });
            var troodon = run.Creatures[^1];
            Assert.Equal(CreatureKind.Predator, troodon.Kind);
            Assert.Equal(PredatorType.Troodon, troodon.Type);
            Assert.True(grid.Distance(troodon.At, you) >= Tuning.PredatorDistance, where);
            Assert.Equal(0, troodon.Alert);
            Assert.Equal(Tuning.NoTarget, troodon.Target);
            Assert.Equal(1, troodon.Ordinal);

            // Ids from 1, in order; the next goes on after them.
            Assert.Equal(Enumerable.Range(1, run.Creatures.Count), run.Creatures.Select(c => c.Id));
            Assert.Equal(run.Creatures.Count + 1, run.NextId);
        }
    }

    [Fact]
    public void TheSameSeedTwiceGivesEqualRuns()
    {
        foreach (var seed in ManySeeds())
            Assert.Equal(Runs.Fingerprint(Runs.Fresh("Dryosaurus", seed)), Runs.Fingerprint(Runs.Fresh("Dryosaurus", seed)));
    }

    [Fact]
    public void TwoSeedsGiveDifferentIslands()
    {
        var islands = ManySeeds().Select(seed => string.Concat(Runs.Fresh("Dryosaurus", seed).Tiles.Select(Letter))).ToList();
        Assert.Equal(islands.Count, islands.Distinct().Count());
    }

    /// <summary>The species changes you, never the island: the same seed is the same ground for a Rex.</summary>
    [Fact]
    public void TheSpeciesDoesNotChangeTheIsland()
    {
        var a = Runs.Fresh("Dryosaurus", 7919);
        var b = Runs.Fresh("Tyrannosaurus", 7919);
        Assert.Equal(a.Tiles, b.Tiles);
        Assert.Equal(a.Plants, b.Plants);
        Assert.Equal(a.Meat, b.Meat);
        Assert.Equal(a.Creatures.Select(c => c.At), b.Creatures.Select(c => c.At));
    }

    // ---- You at the start ----------------------------------------------------------

    [Fact]
    public void YouStartFullOfHealthAt80FoodAnd80Water()
    {
        var run = Runs.Fresh("Dryosaurus", 1);
        Assert.Equal(1, run.You.Health);
        Assert.Equal(80, run.You.Food);
        Assert.Equal(80, run.You.Water);
        Assert.Equal(0, run.You.Growth);
        Assert.Equal(0, run.You.Start);
        Assert.Equal(0, run.You.Best);
        Assert.False(run.You.Wind);
        Assert.Equal(MoveKind.None, run.You.LastMove);
        Assert.Equal(Pace.Walk, run.Pace);
        Assert.Equal(0, run.Turn);
        Assert.Equal(Tuning.ApexNotYet, run.ApexAt);
        Assert.Null(run.Offer);
        Assert.Equal(0, run.PicksOwed);
        Assert.Null(run.Over);
        Assert.Empty(run.Instincts);
    }

    [Fact]
    public void StrongStartGrowsYouTenPercentARankAndSecondWindIsReady()
    {
        var run = Runs.Fresh("Dryosaurus", 1, Runs.Ranks((Trait.StrongStart, 2), (Trait.SecondWind, 1)), stars: 0);
        Assert.Equal(20, run.You.Growth);
        Assert.Equal(20, run.You.Start);
        Assert.Equal(20, run.You.Best);
        Assert.True(run.You.Wind);
    }

    /// <summary>The lineage can be changed at Camp while the run is saved: the run keeps its own copy (H19).</summary>
    [Fact]
    public void TheRunKeepsACopyOfTheRanksAndTheStars()
    {
        var ranks = new Dictionary<Trait, int> { [Trait.KeenNose] = 1 };
        var run = Island.NewRun(new RunSetup("Dryosaurus", 2, ranks, 3), 1);
        ranks[Trait.KeenNose] = 2;
        ranks[Trait.Legacy] = 1;
        Assert.Equal(1, run.Rank(Trait.KeenNose));
        Assert.Equal(0, run.Rank(Trait.Legacy));
        Assert.Equal(3, run.Stars);
        Assert.Equal(2, run.Danger);
    }

    [Fact]
    public void TheRunUsesTheTablesSpellingOfTheSpecies()
    {
        Assert.Equal("Dryosaurus", Runs.Fresh("dryOSAURUS", 1).Species);   // H36
    }

    [Fact]
    public void AnUnknownSpeciesOrDangerIsADefect()
    {
        Assert.Throws<ArgumentException>(() => Runs.Fresh("Velociraptor", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Runs.Fresh("Dryosaurus", 1, danger: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Runs.Fresh("Dryosaurus", 1, danger: 6));
    }

    [Fact]
    public void WhatIsInSightAtTheStartIsSeenAndNothingElse()
    {
        var run = Runs.Fresh("Dryosaurus", 17);
        var sight = Stats.Of(run).Sight;
        for (var cell = 0; cell < Tuning.Cells; cell++)
            Assert.Equal(Island.Grid.Distance(run.You.At, cell) <= sight, run.Seen[cell]);
    }

    // ---- Threat (H17) ----------------------------------------------------------------

    [Fact]
    public void ThreatIsTheBuildsTimesTheSpeciesTimesTheDangers()
    {
        Assert.Equal(0.84 * 1.10, Island.Threat(Runs.Fresh("Dryosaurus", 1)), 12);
        Assert.Equal(1.43 * 1.04 * 1.40, Island.Threat(Runs.Fresh("Tyrannosaurus", 1, danger: 3)), 12);
        Assert.Equal(1.85 * 1.10 * 2.00, Island.Threat(Runs.Fresh("Deinosuchus", 1, danger: 5)), 12);
    }

    [Fact]
    public void TheFirstTroodonsHealthCarriesTheThreatRoundedHalvesUp()
    {
        // 18 x 0.84 x 1.10 = 16.632 -> 17; 18 x 1.43 x 1.04 x 2.00 = 53.54 -> 54; 18 x 1.85 x 1.10 x 1.40 = 51.28 -> 51.
        Assert.Equal(17, Runs.Fresh("Dryosaurus", 1).Creatures[^1].Health);
        Assert.Equal(54, Runs.Fresh("Tyrannosaurus", 1, danger: 5).Creatures[^1].Health);
        Assert.Equal(51, Runs.Fresh("Deinosuchus", 1, danger: 3).Creatures[^1].Health);
    }

    /// <summary>H34: the first predator is a Troodon even for a large build, whose pools never hold one.</summary>
    [Fact]
    public void TheFirstPredatorIsATroodonWhateverTheSize()
    {
        foreach (var species in new[] { "Tyrannosaurus", "Triceratops", "Deinosuchus", "Allosaurus" })
            Assert.Equal(PredatorType.Troodon, Runs.Fresh(species, 1).Creatures.Single(c => c.Kind == CreatureKind.Predator).Type);
    }

    // ---- PickCell ---------------------------------------------------------------------

    [Fact]
    public void PickCellTakesTheFirstPickThatPasses()
    {
        var run = Runs.Fresh("Dryosaurus", 1);
        var rng = new Rng(42);
        var copy = new Rng(42);
        var expected = copy.Next(Tuning.Cells);
        Assert.Equal(expected, Island.PickCell(run, rng, _ => true));
        Assert.Equal(copy.State, rng.State);   // one roll
    }

    [Fact]
    public void AfterTwoHundredMissesPickCellTakesTheLowestCellThatPasses()
    {
        var run = Runs.Fresh("Dryosaurus", 1);
        var rng = new Rng(42);
        var asked = 0;
        // Refuses every random pick (the first 200 questions), then passes cells 7 and 9.
        var cell = Island.PickCell(run, rng, c => ++asked > Tuning.PickCellTries && (c == 7 || c == 9));
        Assert.Equal(7, cell);

        var copy = new Rng(42);
        for (var i = 0; i < Tuning.PickCellTries; i++) copy.NextDouble();
        Assert.Equal(copy.State, rng.State);   // exactly 200 rolls, none for the sweep
    }

    [Fact]
    public void PickCellIsMinusOneWhenNoCellPasses()
    {
        Assert.Equal(-1, Island.PickCell(Runs.Fresh("Dryosaurus", 1), new Rng(3), _ => false));
    }

    // ---- AddPredator and AddSmallGame ------------------------------------------------

    [Fact]
    public void AnAlertedArrivalSearchesHeadingForYourCell()
    {
        var run = Runs.Fresh("Dryosaurus", 1);
        var c = Island.AddPredator(run, new Rng(5), PredatorType.Carnotaurus, alerted: true)!;
        Assert.Equal(Tuning.AlertHeard, c.Alert);
        Assert.Equal(run.You.At, c.Target);
        Assert.Equal(CreatureKind.Predator, c.Kind);
        Assert.Equal((int)Tuning.Round(75 * 0.84 * 1.10), c.Health);   // 69.3 -> 69
        Assert.True(Island.Grid.Distance(c.At, run.You.At) >= Tuning.PredatorDistance);
        Assert.True(Island.IsLand(run, c.At));
        Assert.Same(c, run.Creatures[^1]);
    }

    [Fact]
    public void AnUnalertedArrivalIsUnawareWithNoTarget()
    {
        var run = Runs.Fresh("Dryosaurus", 1);
        var c = Island.AddPredator(run, new Rng(5), PredatorType.Allosaurus, alerted: false)!;
        Assert.Equal(0, c.Alert);
        Assert.Equal(Tuning.NoTarget, c.Target);
    }

    [Fact]
    public void ArrivalsTakeTheNextIdAndTheNextNumberOfTheirSpecies()
    {
        var run = Runs.Fresh("Dryosaurus", 1).Clear();
        var rng = new Rng(9);
        var id = run.NextId;
        var c1 = Island.AddPredator(run, rng, PredatorType.Carnotaurus, false)!;
        var h1 = Island.AddPredator(run, rng, PredatorType.Herrerasaurus, false)!;
        var c2 = Island.AddPredator(run, rng, PredatorType.Carnotaurus, false)!;
        var s = Island.AddSmallGame(run, rng)!;
        Assert.Equal(new[] { id, id + 1, id + 2, id + 3 }, new[] { c1.Id, h1.Id, c2.Id, s.Id });
        Assert.Equal(1, c1.Ordinal);
        Assert.Equal(1, h1.Ordinal);
        Assert.Equal(2, c2.Ordinal);
        Assert.Equal(id + 4, run.NextId);
    }

    /// <summary>H38: a number is never given twice in a run, the defeated counted.</summary>
    [Fact]
    public void ANumberIsNeverGivenTwiceInARun()
    {
        var run = Runs.Fresh("Dryosaurus", 1).Clear()
            .Put(PredatorType.Carnotaurus, 0).Put(PredatorType.Carnotaurus, 2);
        run.Creatures.RemoveAt(0);   // "Carnotaurus" is defeated, "Carnotaurus 2" lives
        Assert.Equal(3, Island.AddPredator(run, new Rng(1), PredatorType.Carnotaurus, false)!.Ordinal);

        var none = Runs.Fresh("Dryosaurus", 1).Clear()
            .Put(PredatorType.Carnotaurus, 0).Put(PredatorType.Carnotaurus, 2);
        none.Creatures.Clear();      // both defeated: still the third, then the fourth
        var rng = new Rng(1);
        Assert.Equal(3, Island.AddPredator(none, rng, PredatorType.Carnotaurus, false)!.Ordinal);
        Assert.Equal(4, Island.AddPredator(none, rng, PredatorType.Carnotaurus, false)!.Ordinal);

        // Another species counts on its own (the fresh run's Troodon was the first).
        Assert.Equal(1, Island.AddPredator(none, rng, PredatorType.Allosaurus, false)!.Ordinal);
        Assert.Equal(2, Island.AddPredator(none, rng, PredatorType.Troodon, false)!.Ordinal);
        Assert.Equal(
            new Dictionary<PredatorType, int> { [PredatorType.Troodon] = 2, [PredatorType.Carnotaurus] = 4, [PredatorType.Allosaurus] = 1 },
            none.Arrived);
    }

    [Fact]
    public void AFreshRunHasHadOneTroodonAndNothingElse()
    {
        foreach (var seed in ManySeeds().Take(20))
            Assert.Equal(new Dictionary<PredatorType, int> { [PredatorType.Troodon] = 1 }, Runs.Fresh("Dryosaurus", seed).Arrived);
    }

    [Fact]
    public void SmallGameCountsNoArrival()
    {
        var run = Runs.Fresh("Dryosaurus", 1).Clear();
        var before = new Dictionary<PredatorType, int>(run.Arrived);
        Island.AddSmallGame(run, new Rng(2));
        Assert.Equal(before, run.Arrived);
    }

    [Fact]
    public void NoArrivalWhereThereIsNoLand()
    {
        var run = Runs.Fresh("Dryosaurus", 1).Clear().Paved(Terrain.Water);
        var before = run.NextId;
        var arrived = new Dictionary<PredatorType, int>(run.Arrived);
        Assert.Null(Island.AddPredator(run, new Rng(1), PredatorType.Troodon, false));
        Assert.Null(Island.AddSmallGame(run, new Rng(1)));
        Assert.Empty(run.Creatures);
        Assert.Equal(before, run.NextId);     // an arrival that found no place took no id...
        Assert.Equal(arrived, run.Arrived);   // ...and counted nothing (H38)
    }

    [Fact]
    public void SmallGameArrivesAtThreeOrMoreFromYou()
    {
        var run = Runs.Fresh("Dryosaurus", 1).Clear();
        var rng = new Rng(77);
        for (var i = 0; i < 20; i++)
        {
            var s = Island.AddSmallGame(run, rng)!;
            Assert.Equal(CreatureKind.SmallGame, s.Kind);
            Assert.Equal(Tuning.SmallGameHealth, s.Health);
            Assert.True(Island.Grid.Distance(s.At, run.You.At) >= Tuning.SmallGameDistance);
        }
        Assert.Equal(20, run.Creatures.Select(c => c.At).Distinct().Count());
    }
}

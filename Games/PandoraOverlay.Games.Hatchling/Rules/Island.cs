using PandoraOverlay.Games.Engine;

namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// The island: the board every run is played on, the start of a run, and how
/// creatures are placed on it.
///
/// <para><see cref="NewRun"/> is the prototype's <c>hxNewRun</c> roll for roll: the
/// same seed gives the same ground, start, plants, carcasses and first creatures'
/// cells, and <c>IslandTests</c> pins five seeds taken from the prototype run in
/// Node. That is what lets the prototype's bot findings carry over to this port
/// (A21: the engine's <see cref="Rng"/> rolls the prototype's numbers). So the order
/// of the rolls below is not style: moving one roll moves every island after it.</para>
/// </summary>
internal static class Island
{
    /// <summary>The board: 11 by 11 hexes, made once (it is immutable, and the walks ask it on every step).</summary>
    public static readonly HexGrid Grid = new(Tuning.Columns, Tuning.Rows);

    /// <summary>
    /// A new run of <paramref name="setup"/>'s species at its danger, with its
    /// snapshot of the lineage and the stars (H19), on the island of
    /// <paramref name="seed"/>. The design's fourteen steps, in their order, each roll
    /// where the design takes it. A species the table does not know, or a danger
    /// outside 1 to 5, throws: Camp only offers what the tables hold.
    /// </summary>
    public static RunState NewRun(RunSetup setup, uint seed)
    {
        ArgumentNullException.ThrowIfNull(setup);
        if (!SpeciesTable.TryGet(setup.Species, out var species))
            throw new ArgumentException("The species is not in the table.", nameof(setup));
        Dangers.Of(setup.Danger);   // throws for a danger outside the table

        var rng = new Rng(seed);
        var run = new RunState
        {
            Species = species.Name,   // the table's spelling, never the setup's (H36)
            Danger = setup.Danger,
            Ranks = setup.Ranks.ToDictionary(r => r.Key, r => r.Value),   // a copy: the snapshot never follows the lineage (H19)
            Stars = setup.Stars,
            Seed = seed,
        };
        var tiles = run.Tiles;

        // 1. All grass (the array's default is Grass; said, not assumed).
        Array.Fill(tiles, Terrain.Grass);

        // 2. The river, top to bottom, sometimes stepping a column aside.
        var column = Tuning.RiverStartBase + rng.Next(Tuning.RiverStartPicks);
        for (var row = 0; row < Tuning.Rows; row++)
        {
            tiles[Grid.Index(column, row)] = Terrain.Water;
            if (rng.Chance(Tuning.RiverBend))
            {
                column = Math.Clamp(column + (rng.Chance(Tuning.RiverBendLeft) ? -1 : 1), 0, Tuning.Columns - 1);
                tiles[Grid.Index(column, row)] = Terrain.Water;
            }
        }

        // 3 and 4. The pond, the forests and the outcrops. A size's pick is taken
        // before its grow's own picks, as the prototype's call evaluates its argument first.
        Grow(tiles, rng, Terrain.Water, Tuning.PondSize);
        for (var k = 0; k < Tuning.Forests; k++)
            Grow(tiles, rng, Terrain.Forest, Tuning.ForestSizeBase + rng.Next(Tuning.ForestSizePicks));
        for (var k = 0; k < Tuning.Rocks; k++)
            Grow(tiles, rng, Terrain.Rock, Tuning.RockSizeBase + rng.Next(Tuning.RockSizePicks));

        // 5. The start: the land nearest the south's middle, the lower cell on a tie.
        var want = Grid.Index(Tuning.StartColumn, Tuning.StartRow);
        var start = -1;
        for (var cell = 0; cell < Tuning.Cells; cell++)
        {
            if (IsLand(tiles[cell]) && (start < 0 || Grid.Distance(cell, want) < Grid.Distance(start, want)))
                start = cell;
        }

        // 6. Rock must never cut the island in two: if it shuts any cell off from
        // the start, the rock goes (all of it, as the prototype does).
        var reached = Grid.Reach(start, Tuning.Cells, cell => tiles[cell] != Terrain.Rock);
        for (var cell = 0; cell < Tuning.Cells; cell++)
        {
            if (cell != start && tiles[cell] != Terrain.Rock && !reached.ContainsKey(cell))
            {
                for (var i = 0; i < Tuning.Cells; i++)
                    if (tiles[i] == Terrain.Rock) tiles[i] = Terrain.Grass;
                break;
            }
        }

        // 7. You.
        var growth = Tuning.StrongStartGrowth * run.Rank(Trait.StrongStart);
        run.You = new You
        {
            At = start,
            Growth = growth,
            Start = growth,
            Best = growth,
            Health = 1,
            Food = Tuning.StartFood,
            Water = Tuning.StartWater,
            Wind = run.Rank(Trait.SecondWind) > 0,
            LastMove = MoveKind.None,
        };
        run.Pace = Pace.Walk;

        // 8 to 12. Plants, carcasses, small game and the first predator, each on a
        // cell picked by PickCell (step 8), in that order.
        for (var k = 0; k < Tuning.Plants; k++)
        {
            var at = PickCell(run, rng, cell => IsLand(run, cell) && !run.Plants[cell] && cell != start);
            if (at >= 0) run.Plants[at] = true;
        }
        for (var k = 0; k < Tuning.Carcasses; k++)
        {
            var at = PickCell(run, rng, cell => IsLand(run, cell) && run.Meat[cell] == 0 && cell != start);
            if (at >= 0) run.Meat[at] = Tuning.CarcassMeals;
        }
        for (var k = 0; k < Tuning.SmallGame; k++)
            AddSmallGame(run, rng);
        AddPredator(run, rng, Tuning.FirstPredator, alerted: false);   // H34: a Troodon, whatever the size class

        // 13. What you see from the start.
        Sight.Reveal(run, Stats.Of(run));

        // 14. The picks owed at the start: a line the starting growth is already
        // past (Strong start) owes the pick it would have owed when crossed, and
        // Born ready owes one more. Only Juvenile, Subadult and Adult owe one: Late
        // bloomer's Elder pick can't be owed at the start, which is under Elder.
        var owed = 0;
        for (var line = 0; line < Stages.Adult; line++)
            if (growth >= Tuning.StageLines[line]) owed++;
        if (run.Rank(Trait.BornReady) > 0) owed++;
        Offers.Owe(run, owed);
        Offers.OpenIfOwed(run, rng);

        run.Rng = rng.State;   // the run's generator goes on from the island's last roll
        return run;
    }

    /// <summary>
    /// The predators' threat in this run (H17): the build's factor times the
    /// species' times the danger's. A predator's health and attack are its table's
    /// times this. A species the table does not know throws (a defect).
    /// </summary>
    public static double Threat(RunState run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (!SpeciesTable.TryGet(run.Species, out var species))
            throw new ArgumentException("The run's species is not in the table.", nameof(run));
        return Builds.Of(species.Build).Threat * species.Threat * Dangers.Of(run.Danger).Threat;
    }

    /// <summary>
    /// A cell that passes <paramref name="test"/>, picked at random: up to
    /// <see cref="Tuning.PickCellTries"/> picks of the whole board (one roll each), the
    /// first that passes; then, so a crowded board still finds a place, the lowest
    /// cell that passes, with no roll; -1 when none does. The prototype's
    /// <c>hxPickTile</c>: the number of rolls taken depends on the board, and that
    /// too is part of the seeded run.
    /// </summary>
    public static int PickCell(RunState run, Rng rng, Func<int, bool> test)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(test);
        for (var tries = 0; tries < Tuning.PickCellTries; tries++)
        {
            var cell = rng.Next(Tuning.Cells);
            if (test(cell)) return cell;
        }
        for (var cell = 0; cell < Tuning.Cells; cell++)
            if (test(cell)) return cell;
        return -1;
    }

    /// <summary>
    /// A predator of <paramref name="type"/> arrives on land with no creature, not
    /// your cell, at least <see cref="Tuning.PredatorDistance"/> from you, and joins
    /// the run's creatures with the next id. Its health is its table's times the
    /// run's <see cref="Threat"/>, rounded halves up (H24). Alerted, it searches
    /// (<see cref="Tuning.AlertHeard"/>) heading for your cell; not alerted, it is
    /// unaware with no target (never cell 0, which is a cell). Null, and nothing
    /// changed but the rolls taken, when no cell passes. It changes the run given:
    /// called on a rule's own copy.
    /// </summary>
    public static Creature? AddPredator(RunState run, Rng rng, PredatorType type, bool alerted)
    {
        ArgumentNullException.ThrowIfNull(run);
        var info = Predators.Of(type);
        var you = run.You.At;
        var at = PickCell(run, rng, cell =>
            IsLand(run, cell) && CreatureAt(run, cell) is null && cell != you
            && Grid.Distance(cell, you) >= Tuning.PredatorDistance);
        if (at < 0) return null;

        var creature = new Creature
        {
            Id = run.NextId++,
            Kind = CreatureKind.Predator,
            Type = type,
            Ordinal = TakeOrdinal(run, type),   // after the cell is found: no place, no count (H38)
            At = at,
            Health = (int)Tuning.Round(info.Health * Threat(run)),
            Alert = alerted ? Tuning.AlertHeard : 0,
            Target = alerted ? you : Tuning.NoTarget,
        };
        run.Creatures.Add(creature);
        return creature;
    }

    /// <summary>
    /// Small game arrives on land with no creature, not your cell, at least
    /// <see cref="Tuning.SmallGameDistance"/> from you, with the next id and
    /// <see cref="Tuning.SmallGameHealth"/>. Null when no cell passes. It changes the
    /// run given: called on a rule's own copy.
    /// </summary>
    public static Creature? AddSmallGame(RunState run, Rng rng)
    {
        ArgumentNullException.ThrowIfNull(run);
        var you = run.You.At;
        var at = PickCell(run, rng, cell =>
            IsLand(run, cell) && CreatureAt(run, cell) is null && cell != you
            && Grid.Distance(cell, you) >= Tuning.SmallGameDistance);
        if (at < 0) return null;

        var creature = new Creature
        {
            Id = run.NextId++,
            Kind = CreatureKind.SmallGame,
            At = at,
            Health = Tuning.SmallGameHealth,
            Alert = 0,
            Target = Tuning.NoTarget,
        };
        run.Creatures.Add(creature);
        return creature;
    }

    /// <summary><paramref name="cell"/> is land: grass or forest, where plants, carcasses and creatures can be.</summary>
    public static bool IsLand(RunState run, int cell) => IsLand(run.Tiles[cell]);

    /// <summary>The creature on <paramref name="cell"/>, or null: at most one stands on a cell.</summary>
    public static Creature? CreatureAt(RunState run, int cell)
    {
        foreach (var creature in run.Creatures)
            if (creature.At == cell) return creature;
        return null;
    }

    /// <summary>
    /// Counts one more arrival of <paramref name="type"/> in the run's
    /// <see cref="RunState.Arrived"/> and answers the arrival's ordinal (H7): how
    /// many of its species have arrived in this run, counting itself, the defeated
    /// included. So a number is never given twice in a run (H38): after two
    /// Carnotaurus the next is "Carnotaurus 3" whether the first two live or not,
    /// and the turn list can never name two creatures of one turn alike. The apex
    /// counts like any arrival of its species. It changes the run: call it only
    /// where a predator joins <see cref="RunState.Creatures"/>, after its cell is
    /// found, so an arrival that found no place counts nothing, as it spends no id.
    /// </summary>
    public static int TakeOrdinal(RunState run, PredatorType type)
    {
        ArgumentNullException.ThrowIfNull(run);
        var ordinal = (run.Arrived.TryGetValue(type, out var arrived) ? arrived : 0) + 1;
        run.Arrived[type] = ordinal;
        return ordinal;
    }

    private static bool IsLand(Terrain ground) => ground is Terrain.Grass or Terrain.Forest;

    /// <summary>
    /// Grows a cluster of <paramref name="kind"/> of up to <paramref name="size"/>
    /// cells on grass (the design's step 3, the prototype's <c>grow</c>): a seed cell
    /// that is grass, picked in at most 1 + <see cref="Tuning.GrowSeedPicks"/> picks
    /// (none if every pick missed), then up to <see cref="Tuning.GrowTries"/> tries,
    /// each picking a cell of the cluster and, if it has grass beside it, one of those
    /// in neighbour order. A try whose cell has no grass beside it still spent its pick.
    /// </summary>
    private static void Grow(Terrain[] tiles, Rng rng, Terrain kind, int size)
    {
        var at = rng.Next(Tuning.Cells);
        for (var tries = 0; tries < Tuning.GrowSeedPicks && tiles[at] != Terrain.Grass; tries++)
            at = rng.Next(Tuning.Cells);
        if (tiles[at] != Terrain.Grass) return;

        var cluster = new List<int> { at };
        tiles[at] = kind;
        var near = new List<int>(6);
        for (var guard = 0; guard < Tuning.GrowTries && cluster.Count < size; guard++)
        {
            var from = cluster[rng.Next(cluster.Count)];
            near.Clear();
            foreach (var next in Grid.Neighbours(from))
                if (tiles[next] == Terrain.Grass) near.Add(next);
            if (near.Count == 0) continue;
            var chosen = near[rng.Next(near.Count)];
            tiles[chosen] = kind;
            cluster.Add(chosen);
        }
    }
}

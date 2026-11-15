using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The saves' hand-built states (task H08): fixed in code, so they never move when the
/// rules or the balance do (task H09 tunes the threat while this runs), and each the
/// state one of the fixtures is recorded from.
///
/// <list type="bullet">
/// <item><see cref="Fresh"/>: a new player's save (H25), at a fixed moment.</item>
/// <item><see cref="MidRun"/>: a run under way in which EVERY member of the save holds
/// something other than its default (A51): an offer open, a redraw paid and the free
/// one spent, three afflicted predators with targets, the apex about, arrivals of three
/// species, a Latch streak, instincts, a turn list, a ranked lineage, stars, a journal
/// in progress. It is a shape, not a run: no single run afflicts with three perks or
/// writes a sprint's second number and a catch of small game in one turn list, and it
/// does all of it so that a round trip proves every member. Only <c>Run.Over</c> is
/// empty, as it is in every run under way; <see cref="RunOver"/> and
/// <see cref="Everything"/> fill it.</item>
/// <item><see cref="RunOver"/>: a won run, settled, waiting for "Back to camp".</item>
/// <item><see cref="Everything"/>: a long career at its fullest (every species, every
/// rank, every achievement and one this build does not know, every line of lately) with
/// a run that was caught at danger 5, settled.</item>
/// <item><see cref="Pinned"/>: the mid-run state with an end as well, so the pinned
/// text holds every name of the format.</item>
/// </list>
/// Every one passes the save's own check (a test holds them to it).
/// </summary>
internal static class SaveSamples
{
    /// <summary>The samples' moment: fixed, so a fresh save's seeds are the same every time (H25).</summary>
    public static readonly DateTime Moment = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>The samples by the names the tests and the fixtures use.</summary>
    public static GameState Named(string name) => name switch
    {
        "fresh" => Fresh(),
        "mid-run" => MidRun(),
        "run-over" => RunOver(),
        "everything" => Everything(),
        "pinned" => Pinned(),
        _ => throw new ArgumentException($"No sample is named \"{name}\".", nameof(name)),
    };

    /// <summary>The four fixtures' names, each a sample's (v1-&lt;name&gt;.json).</summary>
    public static readonly IReadOnlyList<string> FixtureNames = new[] { "fresh", "mid-run", "run-over", "everything" };

    public static GameState Fresh() => GameState.Fresh(Moment);

    // ---- mid-run: something in every member ------------------------------------------------

    public static GameState MidRun()
    {
        var state = GameState.Fresh(Moment);
        state.Wallet = new Wallet(1240, 5375);
        state.Carry = 0.4;
        state.Ranks = MidRunRanks();
        state.Unlocked = new List<string> { "Dryosaurus", "Herrerasaurus", "Pachycephalosaurus", "Omniraptor" };
        state.Bought = 1;
        state.Selected = "Omniraptor";
        state.Danger = 3;
        state.MaxDanger = 4;
        state.Fast = true;
        state.Species["Omniraptor"] = new SpeciesRecord { Grown = 162.5, Fresh = 1.75, Adults = 2, Runs = 7, BestGrowth = 100, WonAt = 3 };
        state.Species["Dryosaurus"] = new SpeciesRecord { Grown = 12.4, Fresh = 3.0, Adults = 0, Runs = 3, BestGrowth = 64.4, WonAt = 1 };
        state.Species["Maiasaura"] = new SpeciesRecord { Grown = 31.2, Fresh = 2.376 };
        state.FreshAsOf = Moment.AddHours(-30);
        state.Journal = new JournalState
        {
            Open =
            {
                new JournalEntry { Species = "Maiasaura", Target = Tuning.JournalTarget, Reward = Tuning.JournalReward, Seen = 31.2 },
                new JournalEntry { Species = "Gallimimus", Target = Tuning.JournalTarget, Reward = Tuning.JournalReward },
                new JournalEntry { Species = "Stegosaurus", Target = Tuning.JournalBigTarget, Reward = Tuning.JournalBigReward },
            },
            Done = 2,
            Rng = 2893472201,
        };
        state.Achievements = new Achievements(AchievementList.All, new[]
        {
            AchievementList.First, AchievementList.Juvenile, AchievementList.Adult, AchievementList.Elder,
            AchievementList.Win, AchievementList.Danger3, AchievementList.Roots,
        });
        state.Totals = new Totals
        {
            Runs = 10,
            Wins = 2,
            Kills = 14,
            BuildsWon = new HashSet<Build> { Build.Hunter, Build.Runner },
            SpeciesWon = new HashSet<string>(StringComparer.Ordinal) { "Omniraptor", "Dryosaurus" },
            RanksBought = 10,
        };
        state.NextSeed = 0x5EED1234;
        state.Lately = new List<LatelyLine>
        {
            new(LatelyKind.Grew, "Maiasaura", 3.2, 2.4, 31, 0),
            new(LatelyKind.Journal, "Pteranodon", 0, 0, Tuning.JournalReward, (int)Tuning.JournalTarget),
            new(LatelyKind.Star, "Omniraptor", 0, 0, 0, 0),
            new(LatelyKind.Unlocked, "Pachycephalosaurus", 0, 0, 0, 0),
        };
        state.Run = MidRunRun(state.Ranks);
        return state;
    }

    /// <summary>The lineage of the mid-run sample: Sharp instincts, Second thoughts, Late bloomer and Second wind among it, ten ranks.</summary>
    private static Dictionary<Trait, int> MidRunRanks() => new()
    {
        [Trait.DeepStomach] = 2,
        [Trait.ThickHide] = 1,
        [Trait.DrySpells] = 1,
        [Trait.SecondWind] = 1,
        [Trait.FastMetabolism] = 1,
        [Trait.StrongStart] = 1,
        [Trait.SharpInstincts] = 1,
        [Trait.SecondThoughts] = 1,
        [Trait.LateBloomer] = 1,
    };

    /// <summary>The cell you stand on in the run samples: grass, water to the west, a carcass under you.</summary>
    public const int YourCell = 60;

    private static RunState MidRunRun(Dictionary<Trait, int> ranks)
    {
        var run = new RunState
        {
            Species = "Omniraptor",
            Danger = 3,
            Ranks = new Dictionary<Trait, int>(ranks),
            Stars = 1,
            Seed = 0xC0FFEE,
            Rng = 123456789,
            Turn = 74,
            NextId = 9,
            Pace = Pace.Sneak,
            Offer = new List<Instinct> { Instinct.Fierce, Instinct.Hardy, Instinct.Quick, Instinct.Healer },
            PicksOwed = 1,
            Redraws = 1,
            FreeRedrawUsed = true,
            ApexAt = 72,
            Kills = 1,
        };
        Ground(run);
        foreach (var cell in Island.Grid.Within(YourCell, 4)) run.Seen[cell] = true;
        foreach (var cell in new[] { 2, 15, 33, 48, 70, 91, 102, 117 }) run.Plants[cell] = true;
        run.Meat[YourCell] = 2;
        run.Meat[49] = 1;

        run.Creatures.Add(new Creature
        {
            Id = 3, Kind = CreatureKind.Predator, Type = PredatorType.Troodon, Ordinal = 2, At = 62, Health = 14,
            Alert = 4, Target = YourCell, SawYou = true, Slowed = 1, HitByYou = true,
        });
        run.Creatures.Add(new Creature
        {
            Id = 5, Kind = CreatureKind.Predator, Type = PredatorType.Carnotaurus, Ordinal = 1, At = 59, Health = 61,
            Alert = 2, Target = 57, Dazed = true, DazeFreeAt = 77, Bleed = 2, BleedDamage = 4, HitByYou = true,
        });
        run.Creatures.Add(new Creature { Id = 7, Kind = CreatureKind.SmallGame, At = 82, Health = Tuning.SmallGameHealth });
        run.Creatures.Add(new Creature
        {
            Id = 8, Kind = CreatureKind.Predator, Type = PredatorType.Tyrannosaurus, Ordinal = 1, At = 104, Health = 299,
            Alert = 4, Target = YourCell, Relentless = true, SawYou = true,
        });
        run.Arrived[PredatorType.Troodon] = 2;
        run.Arrived[PredatorType.Carnotaurus] = 1;
        run.Arrived[PredatorType.Tyrannosaurus] = 1;

        run.You = new You
        {
            At = YourCell, Growth = 100, Start = 10, Best = 100, Health = 0.62, Food = 71.3, Water = 48.6, Wind = true,
            LastMove = MoveKind.Attack, LatchOn = 5, LatchStreak = 2, Bitten = true, AdultUnbitten = true,
        };
        run.Instincts.AddRange(new[] { Instinct.Eyes, Instinct.Ambush, Instinct.Wary });
        run.LastTurn.AddRange(new[]
        {
            new TurnRow(RowActor.You, default, 0, 0, RowCode.Hit, RowCode.None, 9, 2, Food.Meat, PredatorType.Carnotaurus, 1, false),
            new TurnRow(RowActor.Predator, PredatorType.Troodon, 2, 4, RowCode.Spotted, RowCode.Bit, 12, 0, default, default, 0, false),
            new TurnRow(RowActor.Predator, PredatorType.Carnotaurus, 1, 2, RowCode.None, RowCode.Dazed, 0, 0, default, default, 0, false),
            new TurnRow(RowActor.Predator, PredatorType.Tyrannosaurus, 1, 4, RowCode.None, RowCode.Hunting, 0, 0, default, default, 0, false),
            new TurnRow(RowActor.SmallGame, default, 0, 0, RowCode.Scattered, RowCode.None, 0, 0, default, default, 0, true),
            new TurnRow(RowActor.Unseen, default, 0, 0, RowCode.Quiet, RowCode.None, 0, 0, default, default, 0, false),
        });
        return run;
    }

    /// <summary>A fixed island: a river down the fifth column, two forests, a rock outcrop, grass elsewhere.</summary>
    private static void Ground(RunState run)
    {
        for (var cell = 0; cell < Tuning.Cells; cell++)
        {
            var column = cell % Tuning.Columns;
            var row = cell / Tuning.Columns;
            run.Tiles[cell] = column == 4 ? Terrain.Water
                : row is >= 1 and <= 3 && column is >= 6 and <= 8 ? Terrain.Forest
                : row is >= 5 and <= 6 && column is >= 0 and <= 2 ? Terrain.Forest
                : row == 8 && column is >= 7 and <= 9 ? Terrain.Rock
                : Terrain.Grass;
        }
    }

    // ---- a won run, settled ----------------------------------------------------------------

    public static GameState RunOver()
    {
        var state = GameState.Fresh(Moment);
        state.Wallet = new Wallet(312, 612);
        state.Ranks = new Dictionary<Trait, int> { [Trait.DeepStomach] = 1, [Trait.KeenNose] = 1 };
        state.MaxDanger = 3;
        state.Danger = 2;
        state.Species["Dryosaurus"] = new SpeciesRecord { Runs = 4, BestGrowth = 100, WonAt = 2 };
        state.Achievements = new Achievements(AchievementList.All, new[]
            { AchievementList.First, AchievementList.Juvenile, AchievementList.Adult, AchievementList.Elder, AchievementList.Win });
        state.Totals = new Totals
        {
            Runs = 4,
            Wins = 1,
            Kills = 2,
            BuildsWon = new HashSet<Build> { Build.Runner },
            SpeciesWon = new HashSet<string>(StringComparer.Ordinal) { "Dryosaurus" },
            RanksBought = 2,
        };
        state.NextSeed = unchecked(state.NextSeed + Tuning.SeedStep * 4);

        var run = new RunState
        {
            Species = "Dryosaurus",
            Danger = 2,
            Ranks = new Dictionary<Trait, int>(state.Ranks),
            Seed = 4242,
            Rng = 987654321,
            Turn = 88,
            NextId = 12,
            Pace = Pace.Sprint,
            ApexAt = 70,
            Kills = 2,
        };
        Ground(run);
        foreach (var cell in Island.Grid.Within(YourCell, 3)) run.Seen[cell] = true;
        run.Plants[33] = true;
        run.Meat[48] = 3;
        run.Creatures.Add(new Creature
        {
            Id = 9, Kind = CreatureKind.Predator, Type = PredatorType.Allosaurus, Ordinal = 1, At = 16, Health = 140,
            Alert = 1, Target = 50,
        });
        run.Creatures.Add(new Creature
        {
            Id = 11, Kind = CreatureKind.Predator, Type = PredatorType.Tyrannosaurus, Ordinal = 1, At = 82, Health = 288,
            Alert = 4, Target = YourCell, Relentless = true, SawYou = true,
        });
        run.Arrived[PredatorType.Troodon] = 1;
        run.Arrived[PredatorType.Herrerasaurus] = 2;
        run.Arrived[PredatorType.Allosaurus] = 1;
        run.Arrived[PredatorType.Tyrannosaurus] = 1;
        run.You = new You
        {
            At = YourCell, Growth = 100, Start = 0, Best = 100, Health = 0.4, Food = 22.5, Water = 61, LastMove = MoveKind.Sprint,
        };
        run.Instincts.AddRange(new[] { Instinct.Feet, Instinct.Stride, Instinct.Quick });
        run.Over = new RunOver
        {
            Won = true,
            Reason = EndReason.Outlasted,
            Stage = Stages.Elder,
            Lines = new List<PayLine>
            {
                new(PayKind.Grew, 100, 50), new(PayKind.Reached, 4, 75), new(PayKind.Defeated, 2, 6),
                new(PayKind.Outlasted, 0, 60), new(PayKind.Danger, 2, 48),
            },
            Total = 239,
            Earned = new List<string> { AchievementList.Elder, AchievementList.Win },
            DangerUnlocked = 3,
            Settled = true,
        };
        run.LastTurn.AddRange(new[]
        {
            new TurnRow(RowActor.You, default, 0, 0, RowCode.Sprinted, RowCode.None, 3, 5, default, default, 0, false),
            new TurnRow(RowActor.Predator, PredatorType.Tyrannosaurus, 1, 4, RowCode.None, RowCode.Hunting, 0, 0, default, default, 0, false),
            new TurnRow(RowActor.Unseen, default, 0, 0, RowCode.HeardYou, RowCode.None, 0, 0, default, default, 0, false),
        });
        state.Run = run;
        return state;
    }

    // ---- a career at its fullest, a caught run on screen -----------------------------------

    public static GameState Everything()
    {
        var state = GameState.Fresh(Moment.AddDays(-90));
        state.Wallet = new Wallet(18_455, 81_230);
        state.Carry = 0.92;
        foreach (var trait in Lineage.All) state.Ranks[trait.Trait] = trait.MaxRank;
        state.Unlocked = SpeciesTable.All.Select(s => s.Name).ToList();
        state.Bought = 9;
        state.Selected = "Tyrannosaurus";
        state.Danger = Dangers.Highest;
        state.MaxDanger = Dangers.Highest;
        var n = 0;
        foreach (var species in SpeciesTable.All)
        {
            n++;
            state.Species[species.Name] = new SpeciesRecord
            {
                Grown = 300 + 17.5 * n,
                Fresh = Tuning.FreshFloor + 0.1 * (n % 20),
                Adults = 4 + n % 3,
                Runs = 10 + n,
                BestGrowth = 100,
                WonAt = 1 + n % Dangers.Highest,
            };
        }
        state.FreshAsOf = Moment;
        state.Journal = new JournalState
        {
            Open =
            {
                new JournalEntry { Species = "Hypsilophodon", Target = Tuning.JournalTarget, Reward = Tuning.JournalReward, Seen = 74.9 },
                new JournalEntry { Species = "Allosaurus", Target = Tuning.JournalBigTarget, Reward = Tuning.JournalBigReward, Seen = 12 },
                new JournalEntry { Species = "Beipiaosaurus", Target = Tuning.JournalTarget, Reward = Tuning.JournalReward, Seen = 0.1 },
            },
            Done = 31,
            Rng = 0xDEADBEEF,
        };
        // Every achievement, and one this build does not know: the engine keeps it (G22),
        // so a later version that defines it finds it still had.
        state.Achievements = new Achievements(AchievementList.All,
            AchievementList.All.Select(a => a.Id).Append("a-later-achievement"));
        state.Totals = new Totals
        {
            Runs = 412,
            Wins = 230,
            Kills = 3104,
            BuildsWon = Builds.All.Select(b => b.Build).ToHashSet(),
            SpeciesWon = new HashSet<string>(SpeciesTable.All.Select(s => s.Name), StringComparer.Ordinal),
            RanksBought = 38,
        };
        state.NextSeed = 0xFFFFFFFF;
        state.Lately = new List<LatelyLine>
        {
            new(LatelyKind.Grew, "Gallimimus", 0.4, 1.3, 2, 0),
            new(LatelyKind.Star, "Triceratops", 0, 0, 0, 0),
            new(LatelyKind.Journal, "Triceratops", 0, 0, Tuning.JournalBigReward, (int)Tuning.JournalBigTarget),
            new(LatelyKind.Grew, "Triceratops", 51.2, 1.0, 205, 0),
            new(LatelyKind.Unlocked, "Triceratops", 0, 0, 0, 0),
            new(LatelyKind.Grew, "Kentrosaurus", 12.3, 3.0, 147, 0),
        };

        var run = new RunState
        {
            Species = "Tyrannosaurus",
            Danger = Dangers.Highest,
            Ranks = new Dictionary<Trait, int>(state.Ranks),
            Stars = 3,
            Seed = 0x9E3779B9,
            Rng = 0,
            Turn = 53,
            NextId = 17,
            Pace = Pace.Walk,
            Kills = 4,
        };
        Ground(run);
        Array.Fill(run.Seen, true);
        run.Plants[3] = true;
        run.Meat[YourCell] = 1;
        run.Creatures.Add(new Creature
        {
            Id = 13, Kind = CreatureKind.Predator, Type = PredatorType.Allosaurus, Ordinal = 3, At = 61, Health = 37,
            Alert = 4, Target = YourCell, SawYou = true,
        });
        run.Creatures.Add(new Creature
        {
            Id = 16, Kind = CreatureKind.Predator, Type = PredatorType.Carnotaurus, Ordinal = 4, At = 20, Health = 139,
        });
        run.Arrived[PredatorType.Troodon] = 1;
        run.Arrived[PredatorType.Herrerasaurus] = 2;
        run.Arrived[PredatorType.Carnotaurus] = 4;
        run.Arrived[PredatorType.Allosaurus] = 3;
        run.You = new You
        {
            At = YourCell, Growth = 88.2, Start = 30, Best = 88.2, Health = 0, Food = 0, Water = 13.4, LastMove = MoveKind.Attack,
            LatchOn = 0, Bitten = true, AdultUnbitten = false,
        };
        run.Instincts.AddRange(new[] { Instinct.Fierce, Instinct.Skin, Instinct.Appetite, Instinct.Nose });
        run.Over = new RunOver
        {
            Won = false,
            Reason = EndReason.Caught,
            ByType = PredatorType.Allosaurus,
            ByOrdinal = 3,
            Stage = Stages.Adult,
            Lines = new List<PayLine>
            {
                new(PayKind.Grew, 58, 29), new(PayKind.Reached, 2, 60), new(PayKind.Defeated, 4, 12),
                new(PayKind.Danger, 5, 101), new(PayKind.Legacy, 30, 61),
            },
            Total = 263,
            Earned = new List<string>(),
            Settled = true,
        };
        run.LastTurn.AddRange(new[]
        {
            new TurnRow(RowActor.You, default, 0, 0, RowCode.Defeated, RowCode.None, 8, 0, default, default, 0, true),
            new TurnRow(RowActor.Predator, PredatorType.Allosaurus, 3, 4, RowCode.None, RowCode.Bit, 41, 0, default, default, 0, false),
            new TurnRow(RowActor.Predator, PredatorType.Carnotaurus, 4, 0, RowCode.None, RowCode.Wandered, 0, 0, default, default, 0, false),
        });
        state.Run = run;
        return state;
    }

    // ---- the pinned state: every name of the format -----------------------------------------

    /// <summary>The mid-run state with an end as well: what the pinned text is written from, so every property name of the format is in it.</summary>
    public static GameState Pinned()
    {
        var state = MidRun();
        state.Run!.Over = new RunOver
        {
            Won = true,
            Reason = EndReason.DefeatedApex,
            ByType = PredatorType.Herrerasaurus,
            ByOrdinal = 2,
            Stage = Stages.Elder,
            Lines = new List<PayLine> { new(PayKind.Grew, 90, 45), new(PayKind.DefeatedApex, 0, 60) },
            Total = 105,
            Earned = new List<string> { AchievementList.Slayer },
            DangerUnlocked = 4,
            Settled = true,
        };
        return state;
    }
}

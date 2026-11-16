using PandoraOverlay.Games.Hatchling;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The few lines every Hatchling rule's test starts with: a fresh run, a run with
/// a lineage, a board cleared of creatures, a creature put where the test wants it,
/// you moved. Each returns a NEW run and leaves the one given as it was, so a test
/// can keep its "before" and hold a rule to its purity with <see cref="Fingerprint"/>.
///
/// <para>The helpers that take a run are extension methods, so a test may write
/// <c>Runs.Clear(run)</c> or <c>run.Clear()</c>, whichever reads better.</para>
/// </summary>
internal static class Runs
{
    /// <summary>No lineage: every trait at rank 0.</summary>
    public static IReadOnlyDictionary<Trait, int> NoRanks { get; } = new Dictionary<Trait, int>();

    /// <summary>A fresh run of <paramref name="species"/> on <paramref name="seed"/>'s island, with no lineage and no stars.</summary>
    public static RunState Fresh(string species, uint seed, int danger = Dangers.Lowest) =>
        Island.NewRun(new RunSetup(species, danger, NoRanks, 0), seed);

    /// <summary>
    /// A fresh run made WITH the lineage's <paramref name="ranks"/> and the species'
    /// <paramref name="stars"/>: the start of the run sees them (Strong start's growth,
    /// Second wind, the picks owed), as a real run's would.
    /// </summary>
    public static RunState Fresh(string species, uint seed, IReadOnlyDictionary<Trait, int> ranks, int stars,
        int danger = Dangers.Lowest) =>
        Island.NewRun(new RunSetup(species, danger, ranks, stars), seed);

    /// <summary>
    /// A copy of <paramref name="run"/> whose snapshot of the lineage and stars is
    /// <paramref name="ranks"/> and <paramref name="stars"/>, the island and you left as
    /// they are: for a test of a number that reads the ranks, not of the start.
    /// </summary>
    public static RunState With(this RunState run, IReadOnlyDictionary<Trait, int> ranks, int stars)
    {
        var copy = run.Clone();
        copy.Ranks = ranks.ToDictionary(r => r.Key, r => r.Value);
        copy.Stars = stars;
        return copy;
    }

    /// <summary>A ranks table from pairs: <c>Runs.Ranks((Trait.KeenNose, 2))</c>.</summary>
    public static IReadOnlyDictionary<Trait, int> Ranks(params (Trait Trait, int Rank)[] ranks) =>
        ranks.ToDictionary(r => r.Trait, r => r.Rank);

    /// <summary>A copy of <paramref name="run"/> with no creature on the island (ids go on from where they were).</summary>
    public static RunState Clear(this RunState run)
    {
        var copy = run.Clone();
        copy.Creatures.Clear();
        return copy;
    }

    /// <summary>
    /// A copy of <paramref name="run"/> with a predator of <paramref name="type"/> on
    /// <paramref name="cell"/> at <paramref name="alert"/>, whatever the ground: the next
    /// id, the next ordinal of its species (counted in the copy's Arrived, as an arrival
    /// is, H38), the run's threat on its health, and, when alerted, heading for your
    /// cell. It is the last of the copy's creatures.
    /// </summary>
    public static RunState Put(this RunState run, PredatorType type, int cell, int alert = 0)
    {
        var copy = run.Clone();
        copy.Creatures.Add(new Creature
        {
            Id = copy.NextId++,
            Kind = CreatureKind.Predator,
            Type = type,
            Ordinal = Island.TakeOrdinal(copy, type),
            At = cell,
            Health = (int)Tuning.Round(Predators.Of(type).Health * Island.Threat(copy)),
            Alert = alert,
            Target = alert > 0 ? copy.You.At : Tuning.NoTarget,
        });
        return copy;
    }

    /// <summary>A copy of <paramref name="run"/> with small game on <paramref name="cell"/>. It is the last of the copy's creatures.</summary>
    public static RunState PutSmallGame(this RunState run, int cell)
    {
        var copy = run.Clone();
        copy.Creatures.Add(new Creature
        {
            Id = copy.NextId++,
            Kind = CreatureKind.SmallGame,
            At = cell,
            Health = Tuning.SmallGameHealth,
        });
        return copy;
    }

    /// <summary>A copy of <paramref name="run"/> with you on <paramref name="cell"/>; nothing else moves and nothing more is seen.</summary>
    public static RunState At(this RunState run, int cell)
    {
        var copy = run.Clone();
        copy.You.At = cell;
        return copy;
    }

    /// <summary>A copy of <paramref name="run"/> whose every cell is <paramref name="ground"/>: for a test that wants no river or rock in the way.</summary>
    public static RunState Paved(this RunState run, Terrain ground)
    {
        var copy = run.Clone();
        Array.Fill(copy.Tiles, ground);
        return copy;
    }

    /// <summary>Everything a run holds, as one text: equal texts are equal runs. For "the same seed gives the same run" and "the state given is unchanged".</summary>
    public static string Fingerprint(RunState r) => string.Join("|",
        r.Species, r.Danger, string.Join(",", r.Ranks.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")), r.Stars, r.Seed, r.Rng,
        r.Turn, string.Join(",", r.Tiles), string.Join(",", r.Seen), string.Join(",", r.Plants), string.Join(",", r.Meat),
        string.Join(";", r.Creatures.Select(c => string.Join(",", c.Id, c.Kind, c.Type, c.Ordinal, c.At, c.Health, c.Alert, c.Target,
            c.Relentless, c.SawYou, c.Dazed, c.DazeFreeAt, c.Slowed, c.Bleed, c.BleedDamage, c.HitByYou))),
        r.NextId, string.Join(",", r.Arrived.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")), r.Pace,
        string.Join(",", r.You.At, r.You.Growth, r.You.Start, r.You.Best, r.You.Health, r.You.Food, r.You.Water, r.You.Wind,
            r.You.LastMove, r.You.LatchOn, r.You.LatchStreak, r.You.Bitten, r.You.AdultUnbitten),
        string.Join(",", r.Instincts), r.Offer is null ? "none" : string.Join(",", r.Offer), r.PicksOwed, r.Redraws, r.FreeRedrawUsed,
        r.ApexAt, r.Kills,
        r.Over is null ? "none" : string.Join(",", r.Over.Won, r.Over.Reason, r.Over.ByType, r.Over.ByOrdinal, r.Over.Stage,
            string.Join(";", r.Over.Lines), r.Over.Total, string.Join(";", r.Over.Earned), r.Over.DangerUnlocked, r.Over.Settled),
        string.Join(";", r.LastTurn));
}

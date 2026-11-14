using System.Collections.Concurrent;
using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The bot's runs the balance tests read (H32), played once and shared: B1, B2 and
/// B6 read the same fresh runs, B3 and B5 the same full ones. Run <c>k</c> of a
/// species uses the seed <c>Rng.Mix(20261114, k)</c>, k from 0, the same seeds for
/// every species, lineage and danger, so two rows of a table differ by what they
/// name and nothing else.
///
/// <para>The runs of a sweep are played side by side (<see cref="Parallel.For(int, int, Action{int})"/>):
/// each writes only its own slot, and a run's result depends on its seed alone (the
/// bot and the rules are pure, B11), so the tables are the same on any machine and
/// in any order. Only a summary of each run is kept, not its whole state.</para>
/// </summary>
internal static class Sweeps
{
    /// <summary>The plan's base seed (5.3).</summary>
    public const uint BaseSeed = 20261114;

    /// <summary>Run <paramref name="k"/>'s seed.</summary>
    public static uint SeedOf(int k) => Rng.Mix(BaseSeed, (uint)k);

    /// <summary>"Fresh": no rank, no star.</summary>
    public static IReadOnlyDictionary<Trait, int> Fresh { get; } = new Dictionary<Trait, int>();

    /// <summary>"Full": every rank of every trait (and no star).</summary>
    public static IReadOnlyDictionary<Trait, int> Full { get; } = Lineage.All.ToDictionary(t => t.Trait, t => t.MaxRank);

    /// <summary>What a balance table needs of one run.</summary>
    internal readonly record struct RunSummary(double Best, bool Won, EndReason? Reason, int Turns, int Pay,
        bool EndedInApexTurns, bool TimedOut, bool NoNegativeLine, int Kills)
    {
        public bool ReachedAdult => Stages.Of(Best) >= Stages.Adult;
        public bool Starved => Reason is EndReason.Hunger or EndReason.Thirst;
    }

    /// <summary>A run's summary.</summary>
    public static RunSummary Summary(Bot.Outcome outcome)
    {
        var run = outcome.Final;
        return new RunSummary(run.You.Best, outcome.Won, outcome.Reason, run.Turn, outcome.Pay,
            outcome.EndedInApexTurns, outcome.TimedOut,
            run.Over is null || run.Over.Lines.All(l => l.Amount >= 0), run.Kills);
    }

    private static readonly ConcurrentDictionary<(string Lineage, int Danger, int Count), Lazy<IReadOnlyDictionary<string, RunSummary[]>>> Cache = new();

    /// <summary>
    /// <paramref name="count"/> runs of every species at <paramref name="danger"/>
    /// with the lineage named <paramref name="lineage"/> (<c>"fresh"</c> or
    /// <c>"full"</c>), by species in the table's order; played once per process
    /// and shared by every test that asks.
    /// </summary>
    public static IReadOnlyDictionary<string, RunSummary[]> Of(string lineage, int danger, int count) =>
        Cache.GetOrAdd((lineage, danger, count),
            key => new Lazy<IReadOnlyDictionary<string, RunSummary[]>>(() => Play(RanksOf(key.Lineage), key.Danger, key.Count))).Value;

    /// <summary>The ranks a lineage's name stands for.</summary>
    public static IReadOnlyDictionary<Trait, int> RanksOf(string lineage) => lineage switch
    {
        "fresh" => Fresh,
        "full" => Full,
        _ => throw new ArgumentOutOfRangeException(nameof(lineage), lineage, "fresh or full"),
    };

    /// <summary>
    /// Plays <paramref name="count"/> runs of every species with <paramref name="ranks"/>
    /// at <paramref name="danger"/>, each from <paramref name="start"/> when given (a
    /// test that forces something at turn 0), side by side.
    /// </summary>
    public static IReadOnlyDictionary<string, RunSummary[]> Play(IReadOnlyDictionary<Trait, int> ranks, int danger, int count,
        Func<RunState, RunState>? start = null, IReadOnlyList<SpeciesInfo>? species = null)
    {
        var list = species ?? SpeciesTable.All;
        var slots = new RunSummary[list.Count][];
        for (var i = 0; i < list.Count; i++) slots[i] = new RunSummary[count];
        Parallel.For(0, list.Count * count, n =>
        {
            var (i, k) = (n / count, n % count);
            var run = Island.NewRun(new RunSetup(list[i].Name, danger, ranks, 0), SeedOf(k));
            if (start is not null) run = start(run);
            slots[i][k] = Summary(Bot.Play(run));
        });
        var byName = new Dictionary<string, RunSummary[]>(StringComparer.Ordinal);
        for (var i = 0; i < list.Count; i++) byName[list[i].Name] = slots[i];
        return byName;
    }

    /// <summary>A share as a whole percent, for a table.</summary>
    public static string Pct(double share) => $"{share * 100,5:F1}%";

    /// <summary>The share of <paramref name="runs"/> for which <paramref name="test"/> holds.</summary>
    public static double Share(IReadOnlyCollection<RunSummary> runs, Func<RunSummary, bool> test) =>
        runs.Count == 0 ? 0 : runs.Count(test) / (double)runs.Count;
}

namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// Settling a run that has just ended, in the same step that ends it, so what the
/// run paid is in the save before its summary is shown (A20): its total banked, the
/// totals and the species' record brought up, the next danger unlocked by a win
/// (H15), the next run's seed moved, and the run's achievements granted.
///
/// <para><b>Once.</b> A settled run is marked (<c>Over.Settled</c>), and settling it
/// again changes nothing, so a view, a reload or a bug that calls this twice can't
/// pay twice. A run pays only for what it gained (H8): its pay lines were worked
/// out by the rules from <c>Start</c> and <c>Best</c>, and the stage achievements
/// here read the same two, so leaving a run at its first turn pays nothing and
/// earns no stage.</para>
/// </summary>
internal static class Settle
{
    /// <summary>
    /// Banks a run that has just ended (<c>state.Run.Over</c> set, not yet settled):
    /// a new state, the one given unchanged. Once: a run already settled, a run
    /// still under way, or no run at all give a new state equal to the one given.
    /// </summary>
    public static GameState Run(GameState state)
    {
        var next = state.Clone();
        var run = next.Run;
        if (run?.Over is not { Settled: false } over) return next;

        var known = SpeciesTable.TryGet(run.Species, out var species);
        var won = over.Won;

        // The pay, into the game's one wallet (G3).
        next.Wallet.Earn(over.Total);

        // The totals.
        next.Totals.Runs++;
        next.Totals.Kills += run.Kills;
        if (won)
        {
            next.Totals.Wins++;
            if (known)
            {
                next.Totals.BuildsWon.Add(species.Build);
                next.Totals.SpeciesWon.Add(species.Name);
            }
        }

        // The species' record.
        var record = next.RecordToChange(run.Species);
        record.Runs++;
        record.BestGrowth = Math.Max(record.BestGrowth, run.You.Best);
        if (won) record.WonAt = Math.Max(record.WonAt, run.Danger);

        // A win unlocks the next danger, for every species (H15).
        if (won)
        {
            var reached = Math.Max(next.MaxDanger, Math.Min(Dangers.Highest, run.Danger + 1));
            if (reached > next.MaxDanger)
            {
                next.MaxDanger = reached;
                over.DangerUnlocked = reached;
            }
        }

        // The next run's seed (wrapping): the golden ratio's step spreads them.
        next.NextSeed = unchecked(next.NextSeed + Tuning.SeedStep);

        // The run's achievements, in the table's order.
        foreach (var id in Earned(next, run, known ? species.Build : null))
            if (next.Achievements.Grant(id, next.Wallet) is not null) over.Earned.Add(id);

        over.Settled = true;
        return next;
    }

    /// <summary>
    /// Leaves an ended run's summary for Camp: a new state with no run. A run still
    /// under way is not dropped (H20: leaving one is its own armed click, which ends
    /// it first), and a run that ended and was somehow not settled is settled first,
    /// so leaving its summary can never lose its pay. No run: a new state equal to
    /// the one given.
    /// </summary>
    public static GameState BackToCamp(GameState state)
    {
        if (state.Run?.Over is null) return state.Clone();
        var next = state.Run.Over.Settled ? state.Clone() : Run(state);
        next.Run = null;
        return next;
    }

    /// <summary>
    /// The run crossed <paramref name="line"/> (a percent) IN this run: it began
    /// under it and reached it (H8). A run that began at 30% and ended at 40% crossed
    /// no Juvenile line, however grown it was.
    /// </summary>
    internal static bool CrossedInRun(You you, double line) => you.Start < line && line <= you.Best;

    /// <summary>
    /// The ids the settled run qualifies for, in the table's order, read from the
    /// state after its totals were brought up (the engine pays only those not yet
    /// had). <paramref name="build"/> is null for a species the table does not know.
    /// </summary>
    private static IEnumerable<string> Earned(GameState state, RunState run, Build? build)
    {
        var over = run.Over!;
        var won = over.Won;

        yield return AchievementList.First;
        if (CrossedInRun(run.You, Tuning.StageLines[0])) yield return AchievementList.Juvenile;
        if (CrossedInRun(run.You, Tuning.StageLines[2])) yield return AchievementList.Adult;
        if (CrossedInRun(run.You, Tuning.StageLines[3])) yield return AchievementList.Elder;
        if (won) yield return AchievementList.Win;
        if (won && over.Reason == EndReason.DefeatedApex) yield return AchievementList.Slayer;
        if (state.Totals.Kills >= AchievementList.HunterKills) yield return AchievementList.Hunter;
        if (won && build is Build.Runner or Build.Forager) yield return AchievementList.Underdog;
        if (won && build is Build.Tank or Build.Apex) yield return AchievementList.Heavy;
        if (Builds.All.All(b => state.Totals.BuildsWon.Contains(b.Build))) yield return AchievementList.Shapes;
        if (won && run.Danger >= AchievementList.IntoDanger) yield return AchievementList.Danger3;
        if (won && run.Danger >= Dangers.Highest) yield return AchievementList.Danger5;
        if (CrossedInRun(run.You, Tuning.StageLines[2]) && !run.You.Bitten) yield return AchievementList.Unseen;
        if (won && run.Kills == 0) yield return AchievementList.Gentle;
        if (SpeciesTable.All.All(s => state.Totals.SpeciesWon.Contains(s.Name))) yield return AchievementList.All21;
    }
}

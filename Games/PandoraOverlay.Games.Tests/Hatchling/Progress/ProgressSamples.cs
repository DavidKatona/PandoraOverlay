using System.Text.Json;
using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// What the progress tests share: a fixed moment, a fingerprint of a whole state,
/// the check that a rule left the state it was given as it was, and ended runs
/// built to order (no rule of a run exists in this task, so they are built by hand,
/// as the rules would leave them before settling).
/// </summary>
internal static class ProgressSamples
{
    /// <summary>The tests' moment: fixed, so a fresh save's seeds are the same every time (H25).</summary>
    public static readonly DateTime Moment = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    /// <summary>Everything a state holds, its run included, as one text: equal texts are equal states.</summary>
    public static string Fingerprint(GameState state) => JsonSerializer.Serialize(state, Options);

    /// <summary>A run's whole content as one text.</summary>
    public static string Fingerprint(RunState run) => JsonSerializer.Serialize(run, Options);

    /// <summary>
    /// Runs <paramref name="rule"/> on <paramref name="state"/> and asserts the state
    /// given reads exactly as before (every function returns a new state), and that
    /// the answer, when there is one, is another object.
    /// </summary>
    public static T Unchanged<T>(GameState state, Func<GameState, T> rule)
    {
        var before = Fingerprint(state);
        var answer = rule(state);
        Assert.Equal(before, Fingerprint(state));
        if (answer is GameState next) Assert.NotSame(state, next);
        return answer;
    }

    /// <summary>A fresh save holding <paramref name="pay"/> to spend.</summary>
    public static GameState WithPay(long pay)
    {
        var state = GameState.Fresh(Moment);
        state.Wallet.Earn(pay);
        return state;
    }

    /// <summary>
    /// A fresh save whose run has just ended, not yet settled: the given species
    /// and danger, its growth from <paramref name="start"/> to <paramref name="best"/>,
    /// paying <paramref name="total"/>, ended at <paramref name="turn"/> (40 unless a
    /// test asks: a run with no turn earns nothing, H47). <paramref name="adultUnbitten"/>
    /// is what the world's turn would have set at an unbitten crossing of Adult (H46).
    /// MaxDanger is raised to the danger played, as it must have been for the run to begin.
    /// </summary>
    public static GameState Ended(string species = "Dryosaurus", int danger = 1, bool won = false,
        EndReason? reason = null, double start = 0, double best = 10, int kills = 0, bool bitten = false, int total = 5,
        bool adultUnbitten = false, int turn = 40)
    {
        var state = GameState.Fresh(Moment);
        state.MaxDanger = Math.Max(state.MaxDanger, danger);
        state.Danger = danger;
        state.Run = EndedRun(species, danger, won, reason, start, best, kills, bitten, total, adultUnbitten, turn);
        return state;
    }

    /// <summary>An ended, unsettled run, to put into a state of a test's own.</summary>
    public static RunState EndedRun(string species = "Dryosaurus", int danger = 1, bool won = false,
        EndReason? reason = null, double start = 0, double best = 10, int kills = 0, bool bitten = false, int total = 5,
        bool adultUnbitten = false, int turn = 40)
    {
        var run = new RunState { Species = species, Danger = danger, Kills = kills, Turn = turn };
        run.You.Start = start;
        run.You.Best = best;
        run.You.Growth = best;
        run.You.Bitten = bitten;
        run.You.AdultUnbitten = adultUnbitten;   // the world's turn sets it at the crossing (H46); here by hand
        run.Over = new RunOver
        {
            Won = won,
            Reason = reason ?? (won ? EndReason.Outlasted : EndReason.Caught),
            Total = total,
            Lines = new List<PayLine> { new(PayKind.Grew, (int)(best - start), total) },
        };
        return run;
    }

    /// <summary>The pay an achievement gives, from the list: 0 for one that pays nothing.</summary>
    public static int PayOf(string id) =>
        AchievementList.All.Single(a => a.Id == id).Reward is Engine.AchievementReward.Currency c ? c.Amount : 0;

    /// <summary>One species of each build, the first of it in the table's order.</summary>
    public static IReadOnlyList<string> OnePerBuild() =>
        Builds.All.Select(b => SpeciesTable.All.First(s => s.Build == b.Build).Name).ToList();
}

using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// Hand-built turns for the turn script's tests: no rule exists in this task, so
/// every state and every event is written out. Most scenes sit on row 5 of the
/// island, where the distance between two cells is the difference of their
/// columns, so a test can say "3 cells away" and mean it.
/// </summary>
internal static class Scenes
{
    public static readonly HexGrid Grid = new(Tuning.Columns, Tuning.Rows);

    /// <summary>The sight of the stand-in for Sight.Knows: the base sight, no build or trait added.</summary>
    public const int Sight = 3;

    /// <summary>
    /// A stand-in for <c>Sight.Knows</c> (task H02's): a creature is known when its
    /// cell is within <see cref="Sight"/> of yours, and the apex always (H6).
    /// </summary>
    public static readonly Known Sees = (run, c) =>
        (c.Kind == CreatureKind.Predator && c.Type == Tuning.Apex) || Grid.Distance(run.You.At, c.At) <= Sight;

    /// <summary>The cell at <paramref name="column"/> of row 5.</summary>
    public static int Row5(int column) => Grid.Index(column, 5);

    public static Creature Predator(int id, PredatorType type, int at, int ordinal = 1, int alert = 0) => new()
    {
        Id = id, Kind = CreatureKind.Predator, Type = type, Ordinal = ordinal, At = at, Health = 30, Alert = alert,
        Relentless = type == Tuning.Apex,
    };

    public static Creature Game(int id, int at) => new()
    {
        Id = id, Kind = CreatureKind.SmallGame, At = at, Health = Tuning.SmallGameHealth,
    };

    /// <summary>A run with you at <paramref name="youAt"/>, every cell seen, and these creatures (copies), in id order.</summary>
    public static RunState Run(int youAt, params Creature[] creatures)
    {
        var run = new RunState
        {
            Species = "Dryosaurus",
            Turn = 5,
            You = new You { At = youAt, Growth = 10, Health = 1, Food = 80, Water = 80 },
            Creatures = creatures.Select(c => c.Clone()).OrderBy(c => c.Id).ToList(),
        };
        Array.Fill(run.Seen, true);
        return run;
    }

    /// <summary>The run <paramref name="run"/> with one creature moved (a copy).</summary>
    public static RunState With(RunState run, int id, int at, int? alert = null)
    {
        var copy = run.Clone();
        var c = copy.Creatures.Single(x => x.Id == id);
        c.At = at;
        if (alert is int a) c.Alert = a;
        return copy;
    }

    /// <summary>The run <paramref name="run"/> without the creature <paramref name="id"/> (a copy).</summary>
    public static RunState Without(RunState run, int id)
    {
        var copy = run.Clone();
        copy.Creatures.RemoveAll(c => c.Id == id);
        return copy;
    }

    /// <summary>The run <paramref name="run"/> with you moved (a copy).</summary>
    public static RunState YouAt(RunState run, int at)
    {
        var copy = run.Clone();
        copy.You.At = at;
        return copy;
    }

    public static TurnResult Turn(RunState before, RunState after, params TurnEvent[] events) => new(before, after, events);

    public static TurnScript Build(RunState before, RunState after, params TurnEvent[] events) =>
        TurnScripts.Build(Turn(before, after, events), Sees);

    // ---- events, short --------------------------------------------------------------

    public static Walked Walk(int from, params int[] path) => new(Pace.Walk, from, path, false, Tuning.WalkNoise);
    public static Walked Sneak(int from, params int[] path) => new(Pace.Sneak, from, path, false, Tuning.SneakNoise);
    public static Walked Sprint(int from, params int[] path) => new(Pace.Sprint, from, path, false, Tuning.SprintNoise);
    public static Rested Rest() => new(8, Tuning.RestNoise);
    public static TimePassed Time() => new(0, 0, 0);
    public static Stepped Step(int id, Gait gait, int from, params int[] path) => new(id, from, path, gait);

    /// <summary>The one beat of <paramref name="kind"/> in the script; fails when there is none or several.</summary>
    public static Beat One(TurnScript script, BeatKind kind) => Xunit.Assert.Single(script.Beats, b => b.Kind == kind);

    /// <summary>Every banner of the script and every row's words, for the "nothing leaks" checks.</summary>
    public static IEnumerable<string> AllWords(TurnScript script) =>
        script.Beats.Select(b => b.Banner)
            .Concat(script.Rows.Select(HatchlingText.RowName))
            .Concat(script.Rows.Select(HatchlingText.RowText));
}

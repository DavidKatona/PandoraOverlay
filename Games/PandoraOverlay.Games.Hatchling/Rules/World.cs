using PandoraOverlay.Games.Engine;

namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// STAND-IN UNTIL H04. The world's turn and the end of a run, reduced to what
/// <see cref="TurnRules"/> needs to be built and tested before the real one exists:
/// the turn counter, and a <see cref="RunOver"/> with no pay lines. Task H04
/// replaces this whole file with the predators, small game, time, arrivals, the apex
/// and the pay; its signatures are the ones H04's brief gives, so
/// <see cref="TurnRules"/> does not change when it lands. Nothing but
/// <see cref="TurnRules"/> may lean on what this stand-in does.
/// </summary>
internal static class World
{
    /// <summary>The world's turn after your move. Stand-in: one more turn, nothing else; no roll is taken.</summary>
    public static void Turn(RunState run, Rng rng, int noise, double drain, List<TurnEvent> events)
    {
        ArgumentNullException.ThrowIfNull(run);
        run.Turn += 1;
    }

    /// <summary>
    /// Ends the run: fills <see cref="RunState.Over"/> with how it ended and adds
    /// <see cref="RunEnded"/>. Stand-in: no pay lines and a total of 0.
    /// </summary>
    public static void End(RunState run, bool won, EndReason reason, Creature? by, List<TurnEvent> events)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(events);
        run.Over = new RunOver
        {
            Won = won,
            Reason = reason,
            ByType = by?.Type ?? default,
            ByOrdinal = by?.Ordinal ?? 0,
            Stage = Stages.Of(run.You.Growth),
        };
        events.Add(new RunEnded(won, reason, by?.Id ?? 0));
    }
}

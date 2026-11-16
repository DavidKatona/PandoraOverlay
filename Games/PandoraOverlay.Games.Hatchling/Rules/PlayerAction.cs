namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// What the player can do in a run. The rules answer each with
/// the new state and the turn's events, or refuse it. Closed: every kind is
/// nested here, and nothing else can derive.
/// </summary>
internal abstract record PlayerAction
{
    private PlayerAction() { }

    /// <summary>Walk, sneak or sprint (the run's pace) to cell <paramref name="To"/>. A turn.</summary>
    public sealed record Move(int To) : PlayerAction;

    /// <summary>Eat what is here: plants, a carcass, or fish on water. A turn.</summary>
    public sealed record Eat : PlayerAction;

    /// <summary>Drink, on water or beside it. A turn.</summary>
    public sealed record Drink : PlayerAction;

    /// <summary>Rest and heal. A turn.</summary>
    public sealed record Rest : PlayerAction;

    /// <summary>Attack the creature whose id is <paramref name="Creature"/>. A turn.</summary>
    public sealed record Attack(int Creature) : PlayerAction;

    /// <summary>Choose the pace. No turn passes.</summary>
    public sealed record SetPace(Pace Pace) : PlayerAction;

    /// <summary>Pick the instinct at <paramref name="Index"/> of the open offer. No turn passes.</summary>
    public sealed record Pick(int Index) : PlayerAction;

    /// <summary>A new offer. No turn passes; the game object pays its price from the wallet first.</summary>
    public sealed record Redraw : PlayerAction;

    /// <summary>End the run as "left": it pays what was reached and shows the summary (H20).</summary>
    public sealed record Leave : PlayerAction;
}

/// <summary>
/// One answered action: the state before and after, and what happened, in order.
/// The state after is final before the playback begins (A20): the
/// playback only shows it.
/// </summary>
internal sealed record TurnResult(RunState Before, RunState After, IReadOnlyList<TurnEvent> Events);

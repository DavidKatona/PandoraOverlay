namespace PandoraOverlay;

/// <summary>
/// The four states every bar is in (PLAN.md section 6, the owner's call,
/// Oct 24 2026: the same four quarters for every bar, pre-defined like the
/// shape catalogue). The lines are the code's because they are behaviour,
/// and from 1.39 the app acts on them THROUGH these states, so each line
/// lives once: 25% is the pulse's, 50% the game's Wounded word and the
/// Survival view's wake line, 75% the Combat view's wake line
/// (StatsAttention). A theme only colours a state (ThemeTokens.Bar); it can
/// never move a line. LowStatAlert's 20% is a chime line, not a colour line,
/// and stays its own number; so do StatsAttention's calm lines (hysteresis).
/// </summary>
public enum BarState
{
    /// <summary>75% and up.</summary>
    Above75,
    /// <summary>50% up to (not including) 75%.</summary>
    From50To75,
    /// <summary>25% up to (not including) 50%.</summary>
    From25To50,
    /// <summary>Under 25%.</summary>
    Below25,
}

/// <summary>The bars a theme colours: the Survival view's four and the Combat view's Damage (speed is a number, no bar).</summary>
public enum BarKind
{
    Health,
    Stamina,
    Hunger,
    Thirst,
    Damage,
}

/// <summary>The rule from a 0–1 value to its <see cref="BarState"/>, and the questions the code asks of a state.</summary>
public static class BarStates
{
    /// <summary>
    /// The state of a 0–1 value. A value ON a line belongs to the quarter
    /// above it (0.75 is Above75, 0.5 is From50To75, 0.25 is From25To50),
    /// exactly as the old "&lt; 0.25" / "&lt; 0.50" / "&lt; 0.75" tests read.
    /// Written as those same "under" tests on purpose: a NaN (never seen from
    /// the API) lands in Above75, where the old tests left it too (no pulse,
    /// no wake).
    /// </summary>
    public static BarState Of(double value) =>
        value < 0.25 ? BarState.Below25
        : value < 0.50 ? BarState.From25To50
        : value < 0.75 ? BarState.From50To75
        : BarState.Above75;

    /// <summary>Under 25%: the pulse's line.</summary>
    public static bool UnderQuarter(this BarState state) => state == BarState.Below25;

    /// <summary>Under 50%: the game's Wounded word, the Survival view's wake line.</summary>
    public static bool UnderHalf(this BarState state) => state is BarState.From25To50 or BarState.Below25;

    /// <summary>Under 75%: the Combat view's wake line.</summary>
    public static bool UnderThreeQuarters(this BarState state) => state != BarState.Above75;
}

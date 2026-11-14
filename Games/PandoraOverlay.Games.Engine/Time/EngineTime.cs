namespace PandoraOverlay.Games.Engine;

/// <summary>
/// The engine's time base (A19): every duration in the engine and in the
/// art's animations is counted in its ticks, never in seconds or frames. The
/// loop that makes the ticks runs only while a view has something to move
/// (nothing moves, nothing runs): the full rate while a playback plays, the
/// ambient rate for a quiet cue, and no timer at all otherwise. These three
/// numbers have this one home so that every part of the engine counts the
/// same ticks.
/// </summary>
public static class EngineTime
{
    /// <summary>Every duration in the engine and in the art's animations is in these ticks: 30 a second.</summary>
    public const int TicksPerSecond = 30;

    /// <summary>
    /// How often the loop LOOKS at its quiet rate, for a cue that only needs
    /// to breathe (a "your turn" mark): 4 times a second. Time still runs at
    /// <see cref="TicksPerSecond"/>, so each look hands the step the 7 or 8
    /// ticks that passed (A55).
    /// </summary>
    public const int AmbientPerSecond = 4;

    /// <summary>
    /// After a stall at most this many LOOKS' worth of ticks are made up in
    /// one go, 5 at the full rate and 37 at the ambient one; the rest is
    /// dropped, never caught up (A55).
    /// </summary>
    public const int MaxTicksPerFrame = 5;
}

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

    /// <summary>The loop's quiet rate, for a cue that only needs to breathe (a "your turn" mark): 4 ticks a second.</summary>
    public const int AmbientPerSecond = 4;

    /// <summary>After a stall the rest is dropped, never caught up: at most this many ticks are made up in one go.</summary>
    public const int MaxTicksPerFrame = 5;
}

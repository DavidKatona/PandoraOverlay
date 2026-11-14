namespace PandoraOverlay.Games.Engine;

/// <summary>
/// How often a <see cref="GameLoop"/> ticks (A19): nothing moves, nothing runs.
/// </summary>
public enum LoopRate
{
    /// <summary>No timer exists: the view is still, and costs nothing.</summary>
    Off,

    /// <summary>
    /// The loop looks <see cref="EngineTime.AmbientPerSecond"/> times a
    /// second, for a quiet cue that only needs to breathe (a "your turn"
    /// mark). Time still runs at <see cref="EngineTime.TicksPerSecond"/>: each
    /// look hands the step the 7 or 8 ticks that passed, not 1 (A55), so a cue
    /// that flips every few looks counts looks, or engine ticks, never the
    /// calls' tick counts as if each were one.
    /// </summary>
    Ambient,

    /// <summary>The loop looks <see cref="EngineTime.TicksPerSecond"/> times a second, while a playback plays: usually 1 tick a look.</summary>
    Full,
}

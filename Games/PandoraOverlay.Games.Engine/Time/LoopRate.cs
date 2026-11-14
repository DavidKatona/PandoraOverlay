namespace PandoraOverlay.Games.Engine;

/// <summary>
/// How often a <see cref="GameLoop"/> ticks (A19): nothing moves, nothing runs.
/// </summary>
public enum LoopRate
{
    /// <summary>No timer exists: the view is still, and costs nothing.</summary>
    Off,

    /// <summary><see cref="EngineTime.AmbientPerSecond"/> times a second, for a quiet cue that only needs to breathe (a "your turn" mark).</summary>
    Ambient,

    /// <summary><see cref="EngineTime.TicksPerSecond"/> times a second, while a playback plays.</summary>
    Full,
}

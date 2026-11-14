namespace PandoraOverlay.Games;

/// <summary>
/// The host's clock, the only time a game may read: the wall clock and the
/// system's own timers are on the forbidden list (A29), so a game's rules
/// stay deterministic and its tests move time by hand. The app's clock is
/// <c>DateTime.UtcNow</c> for the calendar and a stopwatch started with the
/// host for the rest.
/// </summary>
public interface IGameClock
{
    /// <summary>The calendar: dates only, never for measuring.</summary>
    DateTime UtcNow { get; }

    /// <summary>
    /// Monotonic since the overlay started: every duration. Tests move it by
    /// hand. It starts again at every launch, so nothing a game measures with
    /// it bridges a restart: no progress is counted for time the overlay was
    /// closed (A6).
    /// </summary>
    TimeSpan Elapsed { get; }
}

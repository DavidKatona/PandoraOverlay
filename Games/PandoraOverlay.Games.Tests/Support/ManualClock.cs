namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The host's clock in a test: time moves only when the test moves it. Every
/// game's tests and the engine's loop and playback tests run on it, so a test
/// never sleeps and never depends on the machine's clock (the wall clock and
/// the system's timers are on the forbidden list, A29, for the same reason).
/// <para>
/// <see cref="UtcNow"/> starts at a fixed date, never the real one, so a test
/// that reads the calendar gets the same answer every run; a test may set it
/// to any date it needs (a run across midnight). <see cref="Elapsed"/> starts
/// at zero, as the overlay's does at launch, and only goes forward.
/// </para>
/// </summary>
public sealed class ManualClock : IGameClock
{
    /// <summary>Where <see cref="UtcNow"/> starts: Nov 14 2026, 10:00 UTC, a fixed date (the day the games' plan was written).</summary>
    public static readonly DateTime Start = new(2026, 11, 14, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>The calendar, as the test sets it or <see cref="Advance"/> moves it.</summary>
    public DateTime UtcNow { get; set; } = Start;

    /// <summary>The monotonic clock: zero to begin with, moved only by <see cref="Advance"/>.</summary>
    public TimeSpan Elapsed { get; private set; }

    /// <summary>Moves both clocks forward by <paramref name="by"/>. Backwards is refused: the host's Elapsed is monotonic, and a test that needs it to fall is a test that can't happen.</summary>
    public void Advance(TimeSpan by)
    {
        if (by < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(by), by, "the clock only goes forward");
        Elapsed += by;
        UtcNow += by;
    }
}

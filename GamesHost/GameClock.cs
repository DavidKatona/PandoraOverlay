using System.Diagnostics;
using PandoraOverlay.Games;

namespace PandoraOverlay;

/// <summary>
/// The games' only time (the Contract's <see cref="IGameClock"/>): a game may
/// read no clock of its own (the forbidden list, A29), so its rules stay
/// deterministic and its tests move time by hand. One for the whole host,
/// made with it in MainWindow's constructor, so <see cref="Elapsed"/> counts
/// from about the overlay's start.
/// <para>
/// <see cref="Elapsed"/> is monotonic: a stopwatch, which a change of the PC's
/// clock or a time-zone switch can't move. It starts again at every launch,
/// so nothing a game measures with it bridges a restart, and no progress is
/// counted for time the overlay was closed (A6). <see cref="UtcNow"/> is the
/// calendar, for dates only, never for measuring.
/// </para>
/// </summary>
internal sealed class GameClock : IGameClock
{
    private readonly Stopwatch _watch = Stopwatch.StartNew();

    public DateTime UtcNow => DateTime.UtcNow;

    public TimeSpan Elapsed => _watch.Elapsed;
}

namespace PandoraOverlay.Games;

/// <summary>
/// What the app gives a game at <see cref="IGame.Attach"/>: its whole world.
/// The time, its own save and the log come from here, and nowhere else: a
/// game reaches no file, no network, no process and no clock of its own (the
/// forbidden list, A29, held on the built assemblies), and it can't reach
/// the app's website client, cookie or config, since nothing under Games/
/// references the app (A2). The host itself names no request, cookie or
/// process type (a source scan holds it), so a game adds no request and no
/// request trigger.
/// </summary>
public interface IGameHost
{
    /// <summary>The only time a game has: the calendar and a monotonic clock that tests move by hand.</summary>
    IGameClock Clock { get; }

    /// <summary>The game's own save. The host, not the game, owns every byte on disk (A7).</summary>
    IGameStorage Storage { get; }

    /// <summary>The overlay's log, for facts and counts.</summary>
    IGameLog Log { get; }

    /// <summary>What <see cref="IGame.Tile"/> returns changed: the grid redraws that tile if it is on screen.</summary>
    void TileChanged();

    /// <summary>
    /// A view's handler or the engine's loop caught this: the window closes
    /// the game, the overlay lives.
    /// <para>
    /// The second of the three layers of containment (A10): the host's guard
    /// catches what the host's own calls throw, but a click in a game's view
    /// and the engine's loop are not calls from the host, so they report here.
    /// The game is then stopped: its view disposed, fed nothing, its tile
    /// offering to start it again from its last save. The log gets the
    /// exception's type and stack, never its message.
    /// </para>
    /// </summary>
    void Fault(Exception error);
}

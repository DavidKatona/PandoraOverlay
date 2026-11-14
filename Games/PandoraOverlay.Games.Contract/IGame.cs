using System.Windows;

namespace PandoraOverlay.Games;

/// <summary>
/// One game in the Games window. Every member is called on the UI thread,
/// through the host's guard.
/// <para>
/// This interface is all the app knows of a game (A2, A4): the app references
/// the Contract and each game, never names an engine type, and only its game
/// catalog names a game's namespace. A call that throws does not take the
/// overlay down: the host's guard catches it and the game is stopped (A10),
/// fed nothing, closed if it is on screen, and its tile offers to start it
/// again: a NEW instance, attached to the last save on disk. So what a game
/// keeps only in memory is lost with a faulted instance, and the save must be
/// a whole state at every moment (a turn is committed and saved before it is
/// played back, A20).
/// </para>
/// <para>
/// What a game never learns: the player's Steam ID, name, position or
/// health (only <see cref="DinoReading"/>, A5), the website, the cookie, the
/// config, the files on disk (only its own save, through
/// <see cref="IGameHost.Storage"/>), or the game process. Achievements are the
/// game's own data (G22): it shows them on its own pages and in its tile's
/// <see cref="GameTile.Detail"/>, so nothing about them is in this Contract.
/// </para>
/// </summary>
public interface IGame
{
    /// <summary>
    /// Stable for ever: the save folder's name. Lower-case a-z, 0-9 and '-',
    /// 1 to 32 characters. The window also remembers it in the config, as the
    /// game on screen and the game played last (A31), so a changed id strands
    /// the player's save and the window's memory of it.
    /// </summary>
    string Id { get; }

    /// <summary>On the tile and in the window's header.</summary>
    string Title { get; }

    /// <summary>One line on the tile.</summary>
    string Pitch { get; }

    /// <summary>
    /// Once per instance, before anything else: read the save through
    /// host.Storage. Never throws for a bad save: it says so.
    /// <para>
    /// Why a result and not an exception or a quiet fresh start: a save that
    /// can't be read must be the window's business (A7). On
    /// <see cref="AttachResult.SaveUnreadable"/> or
    /// <see cref="AttachResult.SaveFromNewerVersion"/> the host holds the save
    /// (every write refused, the files kept untouched), the tile says what
    /// happened, and only the player's armed "Start fresh" sets the files
    /// aside and calls <see cref="Reset"/>. A game writes nothing here unless
    /// it upgraded an existing save.
    /// </para>
    /// </summary>
    AttachResult Attach(IGameHost host);

    /// <summary>
    /// Every successful poll while Ready, window open or not. Cheap, no UI.
    /// A failed poll sends nothing: the game sees a gap, which is the truth
    /// (A5). It costs every poll of the overlay, so it does a reading's
    /// worth of work and no more.
    /// </summary>
    void OnReading(DinoReading reading);

    /// <summary>
    /// Cheap, no side effects: asked whenever the grid draws. It carries no
    /// picture: the tile's picture is <see cref="CreateTileArt"/>. When what it
    /// returns changes, the game calls <see cref="IGameHost.TileChanged"/>.
    /// </summary>
    GameTile Tile();

    /// <summary>
    /// <see cref="GameFrame.TileArtWidth"/> x <see cref="GameFrame.TileArtHeight"/>,
    /// never more. An element the game builds itself (with the engine's
    /// surface), so the app needs no engine type to show a game's picture (A2).
    /// </summary>
    FrameworkElement CreateTileArt();

    /// <summary>
    /// A fresh view of the state as it is now; the window owns it and
    /// disposes it. Reopening the window builds a new one, which is how a
    /// player finds the game where they left it, mid-run included (G14).
    /// </summary>
    IGameView CreateView();

    /// <summary>
    /// The window's armed second click: forget everything, write the fresh
    /// save. Also called on an instance that is not Ready: the host's "Start
    /// fresh" on a held save sets its files aside first, then calls this.
    /// Before a Reset of a Ready game the host keeps the save it replaces
    /// (A7), so a Reset by mistake is not the end of a player's progress.
    /// </summary>
    void Reset();
}

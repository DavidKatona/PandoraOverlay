using System.Windows;
using PandoraOverlay.Games;

namespace PandoraOverlay;

/// <summary>Where a listed game stands, as the window shows it on its tile.</summary>
internal enum GameCondition
{
    /// <summary>It plays: fed every reading, its tile its own, its view one click away.</summary>
    Ready,

    /// <summary>
    /// It threw and was stopped (A10): fed nothing, its view closed, its tile
    /// offering "Start it again" (<see cref="GamesHost.StartAgain"/>), a new
    /// instance from its last save, which is a whole state at every moment.
    /// </summary>
    Faulted,

    /// <summary>Its save could not be read (by the host or by the game's own schema): held, every write refused, until the player's armed "Start fresh" (A7).</summary>
    SaveUnreadable,

    /// <summary>Its save was written by a newer version of the game: held untouched for that version, until the player's armed "Start fresh" (A7).</summary>
    SaveFromNewerVersion,
}

/// <summary>
/// One listed game as the window sees it. Every call into the game goes
/// through <see cref="GameGuard"/>, so a game that throws is stopped and the
/// overlay goes on (A10); the window holds these and never the game itself.
/// <para>
/// One per catalog entry for the whole run. Its instance changes only by
/// <see cref="GamesHost.StartAgain"/>, which makes a new one after a fault;
/// everything the instance was given (its <see cref="GameHost"/> and its
/// storage) is made again with it, so a stopped instance can never write over,
/// fault or redraw the one that replaced it.
/// </para>
/// <para>
/// The tile's texts, its picture and the view are asked of a game only while
/// it is Ready and attached: a game is attached before anything else is asked
/// of it (the Contract's <see cref="IGame.Attach"/>), and a game that is not
/// Ready shows the host's words, never its own (its save is held, or it threw).
/// </para>
/// </summary>
internal sealed class HostedGame
{
    internal HostedGame(GamesHost owner, GameEntry entry)
    {
        Owner = owner;
        Entry = entry;
        Id = entry.Id;
        Title = entry.Id;
        Pitch = "";
    }

    /// <summary>The catalog's id: the save folder's name and the config's memory of the game (A31).</summary>
    public string Id { get; }

    /// <summary>The game's title, read once per instance through the guard; its id when it could not be read.</summary>
    public string Title { get; internal set; }

    /// <summary>The game's one line for its tile, read once per instance; "" when it could not be read.</summary>
    public string Pitch { get; internal set; }

    public GameCondition Condition { get; internal set; }

    internal GamesHost Owner { get; }

    internal GameEntry Entry { get; }

    /// <summary>The instance playing now; null when making one threw (the game is then Faulted).</summary>
    internal IGame? Instance { get; set; }

    /// <summary>What the instance was given at <see cref="IGame.Attach"/>; null until it was attached.</summary>
    internal GameHost? Host { get; set; }

    /// <summary>The instance's Attach has returned (whatever it answered).</summary>
    internal bool Attached { get; set; }

    /// <summary>
    /// Why this entry can never play, when it can't: an id the store would
    /// refuse, or one another entry already has (two games in one folder would
    /// share a save, against G3). A build defect the catalog's test catches;
    /// at run time the game is only Faulted, and "Start it again" does nothing.
    /// </summary>
    internal string? Defect { get; set; }

    /// <summary>Ready, attached, with an instance: the only state in which the game is asked anything but its title.</summary>
    internal bool Plays => Condition == GameCondition.Ready && Attached && Instance is not null;

    /// <summary>What the tile says. Null unless Ready, or when the game threw (it is then Faulted).</summary>
    public GameTile? Tile() =>
        Plays ? GameGuard.Run(this, "Tile", Instance!.Tile) : null;

    /// <summary>The tile's picture, <see cref="GameFrame.TileArtWidth"/> x <see cref="GameFrame.TileArtHeight"/>. Null unless Ready, or when the game threw.</summary>
    public FrameworkElement? CreateTileArt() =>
        Plays ? GameGuard.Run(this, "CreateTileArt", Instance!.CreateTileArt) : null;

    /// <summary>
    /// A fresh view of the game as it is now, for the window to own and
    /// dispose; every call the window makes on it goes through the guard too.
    /// Null unless Ready, or when the game threw (it is then Faulted).
    /// </summary>
    public IGameView? CreateView() =>
        Plays ? GameGuard.Run(this, "CreateView", Instance!.CreateView) : null;
}

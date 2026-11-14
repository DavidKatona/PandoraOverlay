namespace PandoraOverlay;

/// <summary>
/// Whether the overlay can see the player's health while the Games window is
/// open (A16), as MainWindow tells the window (<c>GamesWindow.SetWatch</c>).
/// The window's promise, "it pauses if your health drops" (G18), holds only
/// while the overlay can see the player; when it can't, the header says so
/// instead of pretending. A state, never a reason: the window knows nothing
/// of how the overlay reads the player, only whether it does.
/// </summary>
internal enum GamesWatch
{
    /// <summary>In game, and the last reading came through: health is watched. Nothing is shown.</summary>
    Watching,

    /// <summary>The player has no dino spawned: there is no health to watch, and nothing to warn about. Nothing is shown.</summary>
    NotInGame,

    /// <summary>The last reading of the player failed: "Disconnected · health not watched".</summary>
    Disconnected,

    /// <summary>No session (signed out, or ended by the website): "Not signed in · health not watched".</summary>
    SignedOut,
}

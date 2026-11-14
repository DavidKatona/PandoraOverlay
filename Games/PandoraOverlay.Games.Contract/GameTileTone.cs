namespace PandoraOverlay.Games;

/// <summary>
/// The tone of a tile's status line (<see cref="GameTile.Status"/>): a state
/// the game names, never a colour. The window colours it from the theme, as
/// the app's code names states and a theme colours them (A25).
/// </summary>
public enum GameTileTone
{
    /// <summary>Nothing to point out.</summary>
    Neutral,

    /// <summary>Good news.</summary>
    Good,

    /// <summary>Worth the player's attention.</summary>
    Attention,
}

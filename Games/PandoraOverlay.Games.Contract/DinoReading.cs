namespace PandoraOverlay.Games;

/// <summary>
/// All a game ever learns about the real dino (A5).
/// <para>
/// The public promise was species and growth only: never the Steam ID, the
/// name, the position or the health (a drop in health is the Games window's
/// business, G18, never a game's). The app makes one from every successful
/// poll, on the UI thread, whether the window is open or not: in game, the
/// player's dino, gender and growth as the website's API gives them; out of
/// game, <see cref="NotInGame"/>. A failed poll makes none, so a game sees a
/// gap as a gap. A species a game doesn't know is ignored, never an error
/// (A23).
/// </para>
/// </summary>
/// <param name="InGame">The player has a dino spawned on the server.</param>
/// <param name="Species">The API's name of the dino, as it spells it.</param>
/// <param name="Gender">The API's gender of the dino, as it spells it.</param>
/// <param name="Growth">0 to 1, as the API gives it.</param>
public readonly record struct DinoReading(bool InGame, string? Species, string? Gender, double Growth)
{
    /// <summary>Not in game: no dino, nothing carried.</summary>
    public static DinoReading NotInGame { get; } = new(false, null, null, 0);
}

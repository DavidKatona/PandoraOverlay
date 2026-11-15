namespace PandoraOverlay.Games;

/// <summary>
/// How a game makes a sound (A77, A79): it hands the host one of its own and
/// the host plays it, or drops it. Never throws; the game never learns which.
/// UI thread.
/// <para>
/// A door, as <see cref="IGameStorage"/> is: the game asks, the host decides
/// and does. The host plays a sound only while that game's view is on screen
/// in the Games window, not paused by the health warning, and not muted by
/// the player (the window's mute and volume, A82); anything else is dropped,
/// whatever the game does (a sound from <see cref="IGame.OnReading"/> with the
/// window closed, or from a page not on screen), so the guarantee is the
/// host's, not each game's (A79). A game sounds only for what happens on its
/// screen, the player's clicks and the playback of what they did, never for a
/// reading: the real dino's moments have the overlay's own chimes (A85).
/// </para>
/// <para>
/// The host also keeps its own limits (A81): a few sounds at once, the same
/// sound not started twice within a moment, and a sound that breaks
/// <see cref="GameSound"/>'s rules refused with one line in the log. None of
/// it is the game's to know: a dropped sound is silence, never an error.
/// </para>
/// </summary>
public interface IGameSound
{
    /// <summary>One of the game's own sounds, from now, or nothing: the host decides (A79, A81). Never throws.</summary>
    void Play(GameSound sound);
}

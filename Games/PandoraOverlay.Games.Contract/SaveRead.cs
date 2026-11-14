namespace PandoraOverlay.Games;

/// <summary>
/// A game's save as <see cref="IGameStorage.Read"/> found it: the text the
/// game last wrote and the version it wrote it with, which the game's own
/// schema upgrades from (A9). A payload of a higher version than the game
/// knows is answered <see cref="AttachResult.SaveFromNewerVersion"/>, and the
/// host keeps it untouched.
/// </summary>
/// <param name="State">What the host found.</param>
/// <param name="Version">The version the payload was written with.</param>
/// <param name="Payload">The game's own text, as it was written; none when there is no save to give (None, Unreadable).</param>
public readonly record struct SaveRead(SaveReadState State, int Version, string? Payload);

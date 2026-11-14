namespace PandoraOverlay.Games;

/// <summary>
/// What <see cref="IGame.Attach"/> found. A save that can't be used is
/// answered, never thrown and never quietly replaced by a fresh start: the
/// host then holds the save (every write refused, the files kept) until the
/// player's armed "Start fresh" (A7).
/// </summary>
public enum AttachResult
{
    /// <summary>The save was read (or there was none, and the game starts fresh): the game plays.</summary>
    Ready,

    /// <summary>The host could not read the save, or the game's own schema refused what it holds.</summary>
    SaveUnreadable,

    /// <summary>The save was written by a newer version of the game than this one: kept untouched for that version.</summary>
    SaveFromNewerVersion,
}

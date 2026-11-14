namespace PandoraOverlay.Games.Engine;

/// <summary>
/// What <see cref="SaveSchema{T}.Load"/> made of a save (A9). Three answers and
/// no fourth: a save is read, or it is from a later build and left alone, or it
/// can't be read and is reported. It is never guessed at.
/// </summary>
public enum SaveOutcome
{
    /// <summary>Read: the state is the save's, upgraded to the current version when it was older.</summary>
    Ok,

    /// <summary>
    /// Written by a later build than this one (its version is above the schema's).
    /// The text was not even parsed: the game answers
    /// <c>AttachResult.SaveFromNewerVersion</c> and the host holds the file
    /// untouched (A7), so the newer build that wrote it can still read it.
    /// </summary>
    Newer,

    /// <summary>
    /// Not a save this build can read: no version, not a JSON object, an upgrade
    /// step that failed, a shape that is not the state's, or a state the game's own
    /// check refused. The game answers <c>AttachResult.SaveUnreadable</c> and the
    /// host holds the file until the player's armed "Start fresh" (A7).
    /// </summary>
    Corrupt,
}

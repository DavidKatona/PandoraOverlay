namespace PandoraOverlay.Games;

/// <summary>
/// The game's own save, and the only way a game's state reaches the disk.
/// Losing a player's progress is the worst bug this feature can have, so the
/// host, not each game, owns every byte on disk (A7): one folder per game (so
/// no file is shared between two games, G3), an envelope with the version and
/// a check around the game's text, an atomic swap that keeps the previous
/// save as a backup, and a read that falls back to that backup. The game
/// hands over text and a version; its shape is the game's (the engine's save
/// schema, A9).
/// </summary>
public interface IGameStorage
{
    /// <summary>The save as the host finds it on disk, with the backup's fallback; a game reads it at <see cref="IGame.Attach"/>.</summary>
    SaveRead Read();

    /// <summary>
    /// Never throws. Now: on disk before it returns. Soon: within a minute,
    /// and on window close, session end and exit. Refused while the save is
    /// held (A7: a save that could not be read is never silently replaced).
    /// <para>
    /// A write that fails is a warning in the log; the host keeps the pending
    /// text and tries it again at its next flush (A8).
    /// </para>
    /// </summary>
    void Write(int version, string payload, SaveUrgency urgency);
}

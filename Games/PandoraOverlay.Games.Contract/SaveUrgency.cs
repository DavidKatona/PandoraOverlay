namespace PandoraOverlay.Games;

/// <summary>
/// How soon a <see cref="IGameStorage.Write"/> must reach the disk (A8). There
/// is no timer behind either: a Soon write rides on the next reading, so a
/// crash loses at most a minute of background accrual and nothing else.
/// </summary>
public enum SaveUrgency
{
    /// <summary>
    /// Background accrual (growth seen, a reading's worth of currency): the
    /// host keeps the newest pending text and writes it when a reading arrives
    /// and the game's last write is a minute old, when the window closes, when
    /// Windows ends the session, and at exit.
    /// </summary>
    Soon,

    /// <summary>On disk before the write returns: every committed turn, every purchase, every unlock, anything the player would miss.</summary>
    Now,
}

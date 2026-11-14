namespace PandoraOverlay.Games;

/// <summary>What the host found on disk for a game's save (A7).</summary>
public enum SaveReadState
{
    /// <summary>No save and no backup: the game starts fresh.</summary>
    None,

    /// <summary>The save was read and its check matched.</summary>
    Ok,

    /// <summary>The save could not be read, its backup could (the save before the last write); the host logs a warning.</summary>
    FromBackup,

    /// <summary>A save or a backup exists, and neither can be read: the game answers <see cref="AttachResult.SaveUnreadable"/>.</summary>
    Unreadable,
}

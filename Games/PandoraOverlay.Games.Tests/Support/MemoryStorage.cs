namespace PandoraOverlay.Games.Tests;

/// <summary>
/// A game's save in a test, kept in memory: no file is ever touched (tests never
/// write to the player's data folder, A7). It answers
/// as the host's store does, as far as a game can tell: <see cref="Read"/>
/// gives back what was stored, a write while the save is held is refused (A7),
/// and so is a version under 1 (A34).
/// <para>
/// A test sets <see cref="Stored"/> to the save a game should find at Attach (a
/// fixture of an old version, an Unreadable read, a FromBackup), sets
/// <see cref="Held"/> to see a game keep playing on a held save, and reads
/// <see cref="Writes"/> to see what the game asked for and how urgently (A8:
/// Now for every committed turn, purchase and unlock, Soon for accrual).
/// </para>
/// </summary>
public sealed class MemoryStorage : IGameStorage
{
    private readonly List<(int Version, string Payload, SaveUrgency Urgency)> _writes = new();

    /// <summary>What <see cref="Read"/> answers. To begin with there is no save: State None, Version 0, no payload (A34: the same as <c>default</c>).</summary>
    public SaveRead Stored { get; set; }

    /// <summary>True: every write is refused, as the host refuses them while a save it could not read is held (A7). Refused writes are still recorded.</summary>
    public bool Held { get; set; }

    /// <summary>Every call to <see cref="Write"/>, in order, refused ones included.</summary>
    public IReadOnlyList<(int Version, string Payload, SaveUrgency Urgency)> Writes => _writes;

    /// <summary>The save as stored: no fallback, no upgrade, no check; those are the host's own tests'.</summary>
    public SaveRead Read() => Stored;

    /// <summary>
    /// Records the call and, unless the save is held, makes it what
    /// <see cref="Read"/> answers (State Ok), whatever the urgency: a Soon write
    /// is on the "disk" at once here, where the host would wait for its next
    /// flush. A version under 1 or a null payload is refused as the host
    /// refuses it (recorded, not stored). Never throws, as the Contract says.
    /// </summary>
    public void Write(int version, string payload, SaveUrgency urgency)
    {
        _writes.Add((version, payload, urgency));
        if (Held || version < 1 || payload is null) return;
        Stored = new SaveRead(SaveReadState.Ok, version, payload);
    }
}

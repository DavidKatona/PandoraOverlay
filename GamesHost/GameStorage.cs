using PandoraOverlay.Games;

namespace PandoraOverlay;

/// <summary>
/// A game's own save as the game sees it (the Contract's <see cref="IGameStorage"/>):
/// a thin layer over <see cref="GameSaveStore"/>, which owns every byte on
/// disk (A7). What it adds is WHEN a write reaches the store (A8) and what a
/// game may not do: write over a held save, or write at all once it was
/// stopped.
/// <para>
/// <b>Now and Soon (A8).</b> A <see cref="SaveUrgency.Now"/> write goes to the
/// store at once (a committed turn, a purchase, an unlock: anything the player
/// would miss) and supersedes anything pending, being newer. A
/// <see cref="SaveUrgency.Soon"/> write (background accrual) is kept as the
/// ONE pending write, the newest winning, and written on the back of a reading
/// once this game's last write is <see cref="SoonInterval"/> old
/// (<see cref="FlushIfDue"/>, from <see cref="GamesHost.Feed"/>), or at once by
/// <see cref="Flush"/> (the window closing, Windows ending the session, exit).
/// There is no timer: a crash loses at most a minute of accrual and nothing
/// else. A write the store refused or failed (a locked file, a full disk)
/// stays pending and is tried again at the next due flush, so a broken disk
/// costs one attempt a minute, not one a poll.
/// </para>
/// <para>
/// <b>Held (A7).</b> While the store holds the save (it could not be read, or
/// it is from a newer version), every write is dropped, with one Warning per
/// hold, and nothing is kept pending: a held save is replaced only after the
/// player's armed "Start fresh", whose Reset writes the fresh state itself.
/// The first read of an instance (the one at Attach) that finds the save
/// Unreadable holds it AT ONCE, before the game can answer: so nothing the
/// game writes during Attach can push the damaged files aside and start it
/// fresh behind the player's back, whatever it answers (the host then calls
/// it SaveUnreadable anyway).
/// </para>
/// <para>
/// <b>Stopped.</b> Once its game faulted, or its instance was replaced, a
/// write is dropped (one Warning): an instance that threw can no longer be
/// trusted, and its last save is a whole state. What it handed over BEFORE
/// it failed stays pending and is still flushed: that was a whole state too.
/// </para>
/// One per instance: <see cref="GamesHost"/> makes a new one with each new
/// instance of a game. Never throws (the Contract's promise): a failure is a
/// Warning and the pending text. Not thread-safe: everything in the games
/// runs on the UI thread.
/// </summary>
internal sealed class GameStorage : IGameStorage
{
    /// <summary>How old this game's last write must be before a pending Soon write is written on a reading (A8): the bound on what a crash can lose.</summary>
    internal static readonly TimeSpan SoonInterval = TimeSpan.FromSeconds(60);

    private readonly GameSaveStore _store;
    private readonly string _id;
    private readonly IGameClock _clock;
    private readonly IGameLog _log;

    private int _pendingVersion;
    private string? _pendingPayload; // null: nothing pending
    private TimeSpan? _lastWrite;    // when this game's text last went to the store (written or tried); null: not this run
    private bool _holdWarned;        // the one Warning of the current hold was written
    private bool _stopped;
    private bool _stopWarned;

    /// <param name="store">The store every game's saves go through.</param>
    /// <param name="id">The game's id, valid by <see cref="GameIds.IsValid"/> (the host makes no storage for one that isn't).</param>
    /// <param name="clock">The host's clock: <see cref="IGameClock.Elapsed"/> times the writes.</param>
    /// <param name="log">The game's own lines ("&lt;id&gt;: ...").</param>
    public GameStorage(GameSaveStore store, string id, IGameClock clock, IGameLog log)
    {
        _store = store;
        _id = id;
        _clock = clock;
        _log = log;
    }

    /// <summary>The first read of this instance, the one at Attach: what the host checks once Attach has returned. Null: not read yet.</summary>
    internal SaveRead? AttachRead { get; private set; }

    /// <summary>A write is waiting for the next flush.</summary>
    internal bool HasPending => _pendingPayload is not null;

    /// <summary>The store's read, with the backup's fallback. An exception (never expected) reads as Unreadable, so it holds rather than starting fresh.</summary>
    public SaveRead Read()
    {
        SaveRead read;
        try
        {
            read = _store.Read(_id);
        }
        catch (Exception ex)
        {
            _log.Warning($"the save could not be read: {AppLog.Short(ex)}");
            read = new SaveRead(SaveReadState.Unreadable, 0, null);
        }
        if (AttachRead is null)
        {
            AttachRead = read;
            if (read.State == SaveReadState.Unreadable) Hold();
        }
        return read;
    }

    public void Write(int version, string payload, SaveUrgency urgency)
    {
        if (_stopped)
        {
            if (!_stopWarned) _log.Warning("a write after the game was stopped was dropped");
            _stopWarned = true;
            return;
        }
        // The store's own rule (A34), checked here too: a refusal of THIS kind
        // never succeeds, so it must not sit pending and be retried for ever.
        if (version < 1 || payload is null)
        {
            _log.Warning(version < 1 ? $"a write of version {version} was dropped: versions start at 1" : "a write with no text was dropped");
            return;
        }
        if (DroppedForHold()) return;
        if (urgency == SaveUrgency.Soon)
        {
            Keep(version, payload);
            return;
        }
        // Now (and anything that is not Soon: on disk is the safe reading).
        // Newer than anything pending, so it supersedes it.
        Clear();
        Put(version, payload, _clock.Elapsed);
    }

    /// <summary>On a reading: the pending write, when this game's last write is <see cref="SoonInterval"/> old or more (or there was none this run).</summary>
    public void FlushIfDue(TimeSpan elapsed)
    {
        if (_pendingPayload is null) return;
        if (_lastWrite is { } last && elapsed - last < SoonInterval) return;
        Put(_pendingVersion, _pendingPayload, elapsed);
    }

    /// <summary>The pending write, now: the window closing, Windows ending the session, exit, a game restarted or reset.</summary>
    public void Flush()
    {
        if (_pendingPayload is null) return;
        Put(_pendingVersion, _pendingPayload, _clock.Elapsed);
    }

    /// <summary>Forgets the pending write: what a Reset forgets, the state it belonged to.</summary>
    internal void Clear() => _pendingPayload = null;

    /// <summary>The game faulted or was replaced: no new write is taken; what is pending is still flushed.</summary>
    internal void Stop() => _stopped = true;

    private void Keep(int version, string payload)
    {
        _pendingVersion = version;
        _pendingPayload = payload;
    }

    /// <summary>The text to the store. Written: nothing pending. Refused or failed: pending, for the next due flush.</summary>
    private void Put(int version, string payload, TimeSpan at)
    {
        if (DroppedForHold()) return;
        _lastWrite = at;
        bool written;
        try
        {
            written = _store.Write(_id, version, payload);
        }
        catch (Exception ex)
        {
            _log.Warning($"the save could not be written: {AppLog.Short(ex)}");
            written = false;
        }
        if (written) Clear();
        else Keep(version, payload);
    }

    /// <summary>
    /// While the store holds the save: true, nothing kept, and the hold's one
    /// Warning (the store would refuse and warn on every write, and a game
    /// accruing in the background writes on every reading). The Warning is
    /// written again for a later hold, once this one was released.
    /// </summary>
    private bool DroppedForHold()
    {
        if (!IsHeld())
        {
            _holdWarned = false;
            return false;
        }
        Clear();
        if (!_holdWarned) _log.Warning("the save is held: its writes are dropped until it is set aside");
        _holdWarned = true;
        return true;
    }

    private bool IsHeld()
    {
        try
        {
            return _store.IsHeld(_id);
        }
        catch (Exception)
        {
            return true; // never expected (the id is valid); when in doubt, hold
        }
    }

    private void Hold()
    {
        try
        {
            _store.Hold(_id);
        }
        catch (Exception ex)
        {
            _log.Warning($"the save could not be held: {AppLog.Short(ex)}");
        }
    }
}

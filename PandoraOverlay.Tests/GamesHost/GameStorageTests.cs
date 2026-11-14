using System.IO;
using PandoraOverlay.Games;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// A game's storage over the real store (A7, A8): Now on disk at once, Soon
/// coalesced to one write a minute on the back of a reading or flushed on
/// demand, a held save written over by nothing, a failed write tried again,
/// a stopped game's writes dropped. Every test in a temporary folder of its
/// own, never the real games folder.
/// </summary>
public sealed class GameStorageTests : IDisposable
{
    private const string Id = "sample-game";

    private readonly TempGamesRoot _temp = new();
    private readonly TestGameClock _clock = new();
    private readonly TestGameLog _log = new();
    private readonly GameSaveStore _store;

    public GameStorageTests() => _store = new GameSaveStore(_temp.Root, () => _clock.UtcNow);

    public void Dispose() => _temp.Dispose();

    private GameStorage Storage() => new(_store, Id, _clock, _log);

    private string? OnDisk() => _temp.SavedPayload(Id);

    private FileStream Lock(string name) =>
        new(_temp.FileOf(Id, name), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    // ---- Now and Soon (A8) ---------------------------------------------------------

    [Fact]
    public void ANowWriteIsOnDiskBeforeItReturns()
    {
        var storage = Storage();

        storage.Write(1, "turn 1", SaveUrgency.Now);

        Assert.Equal("turn 1", OnDisk());
        Assert.False(storage.HasPending);
    }

    [Fact]
    public void ASoonWriteWaitsForAFlushOrAMinuteAfterTheLastWrite()
    {
        var storage = Storage();
        storage.Write(1, "turn 1", SaveUrgency.Now);

        _clock.Advance(TimeSpan.FromSeconds(1));
        storage.Write(1, "accrued", SaveUrgency.Soon);
        Assert.Equal("turn 1", OnDisk());

        _clock.Advance(TimeSpan.FromSeconds(58)); // 59 s after the last write
        storage.FlushIfDue(_clock.Elapsed);
        Assert.Equal("turn 1", OnDisk());
        Assert.True(storage.HasPending);

        _clock.Advance(TimeSpan.FromSeconds(1)); // 60 s: due
        storage.FlushIfDue(_clock.Elapsed);
        Assert.Equal("accrued", OnDisk());
        Assert.False(storage.HasPending);
    }

    [Fact]
    public void TheIntervalIsOneMinute() => Assert.Equal(TimeSpan.FromSeconds(60), GameStorage.SoonInterval);

    [Fact]
    public void WithNoWriteYetThisRunASoonWriteIsDueAtTheFirstReading()
    {
        // Nothing to coalesce with yet: measuring from the launch instead would
        // let a crash in the first minute lose what the first readings accrued.
        var storage = Storage();
        storage.Write(1, "accrued", SaveUrgency.Soon);
        Assert.Null(OnDisk());

        storage.FlushIfDue(_clock.Elapsed);

        Assert.Equal("accrued", OnDisk());
    }

    [Fact]
    public void FlushWritesThePendingOneNow()
    {
        var storage = Storage();
        storage.Write(1, "turn 1", SaveUrgency.Now);
        storage.Write(1, "accrued", SaveUrgency.Soon);

        storage.Flush();

        Assert.Equal("accrued", OnDisk());
        Assert.False(storage.HasPending);
        storage.Flush(); // nothing pending: nothing written
        Assert.Equal("accrued", OnDisk());
    }

    [Fact]
    public void TheNewestSoonWriteWins()
    {
        var storage = Storage();
        storage.Write(1, "one", SaveUrgency.Soon);
        storage.Write(1, "two", SaveUrgency.Soon);
        storage.Write(2, "three", SaveUrgency.Soon);

        storage.Flush();

        var read = _store.Read(Id);
        Assert.Equal((2, "three"), (read.Version, read.Payload));
    }

    [Fact]
    public void ANowWriteSupersedesWhatIsPending()
    {
        var storage = Storage();
        storage.Write(1, "accrued", SaveUrgency.Soon);
        storage.Write(1, "turn 1", SaveUrgency.Now);

        Assert.False(storage.HasPending);
        _clock.Advance(TimeSpan.FromMinutes(5));
        storage.FlushIfDue(_clock.Elapsed);
        storage.Flush();
        Assert.Equal("turn 1", OnDisk());
    }

    [Fact]
    public void ANowWriteRestartsTheMinute()
    {
        var storage = Storage();
        storage.Write(1, "turn 1", SaveUrgency.Now);
        _clock.Advance(TimeSpan.FromSeconds(50));
        storage.Write(1, "turn 2", SaveUrgency.Now);
        storage.Write(1, "accrued", SaveUrgency.Soon);

        _clock.Advance(TimeSpan.FromSeconds(20)); // 70 s after the first, 20 after the last
        storage.FlushIfDue(_clock.Elapsed);
        Assert.Equal("turn 2", OnDisk());

        _clock.Advance(TimeSpan.FromSeconds(40));
        storage.FlushIfDue(_clock.Elapsed);
        Assert.Equal("accrued", OnDisk());
    }

    [Fact]
    public void AnUnknownUrgencyIsWrittenNow()
    {
        Storage().Write(1, "turn 1", (SaveUrgency)7);

        Assert.Equal("turn 1", OnDisk());
    }

    // ---- Refused and failed writes -------------------------------------------------

    [Fact]
    public void AFailedWriteStaysPendingAndIsTriedAgainAMinuteLater()
    {
        var storage = Storage();
        storage.Write(1, "turn 1", SaveUrgency.Now);
        using (Lock(GameSaveStore.SaveFile))
        {
            storage.Write(1, "turn 2", SaveUrgency.Now); // the save can't be opened: refused
            Assert.True(storage.HasPending);
        }
        Assert.Equal("turn 1", OnDisk());

        _clock.Advance(TimeSpan.FromSeconds(30));
        storage.FlushIfDue(_clock.Elapsed); // a broken disk costs one try a minute, not one a poll
        Assert.Equal("turn 1", OnDisk());

        _clock.Advance(TimeSpan.FromSeconds(30));
        storage.FlushIfDue(_clock.Elapsed);
        Assert.Equal("turn 2", OnDisk());
        Assert.False(storage.HasPending);
    }

    [Fact]
    public void AFailedSoonWriteIsKeptForTheNextFlush()
    {
        var storage = Storage();
        storage.Write(1, "turn 1", SaveUrgency.Now);
        storage.Write(1, "accrued", SaveUrgency.Soon);
        using (Lock(GameSaveStore.SaveFile)) storage.Flush();
        Assert.True(storage.HasPending);

        storage.Flush();

        Assert.Equal("accrued", OnDisk());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AVersionUnderOneIsDroppedNotKept(int version)
    {
        // Versions start at 1 (A34): such a write would be refused for ever,
        // so it must not sit pending and be retried every minute.
        var storage = Storage();

        storage.Write(version, "text", SaveUrgency.Now);
        storage.Write(version, "text", SaveUrgency.Soon);

        Assert.False(storage.HasPending);
        Assert.Null(OnDisk());
        Assert.Equal(2, _log.Warnings.Count);
    }

    [Fact]
    public void NoTextIsDroppedNotKept()
    {
        var storage = Storage();

        storage.Write(1, null!, SaveUrgency.Soon);

        Assert.False(storage.HasPending);
        Assert.Single(_log.Warnings);
    }

    // ---- Held (A7) -----------------------------------------------------------------

    [Fact]
    public void AHeldSaveIsWrittenOverByNothingWithOneWarningPerHold()
    {
        var storage = Storage();
        storage.Write(1, "the player's save", SaveUrgency.Now);
        _store.Hold(Id);
        var before = File.ReadAllBytes(_temp.FileOf(Id, GameSaveStore.SaveFile));

        storage.Write(1, "now", SaveUrgency.Now);
        storage.Write(1, "soon", SaveUrgency.Soon);
        _clock.Advance(TimeSpan.FromMinutes(2));
        storage.FlushIfDue(_clock.Elapsed);
        storage.Flush();

        Assert.Equal(before, File.ReadAllBytes(_temp.FileOf(Id, GameSaveStore.SaveFile)));
        Assert.False(File.Exists(_temp.FileOf(Id, GameSaveStore.BackupFile)));
        Assert.False(storage.HasPending); // nothing kept to replace it later
        Assert.Single(_log.Warnings);

        // Released by "Start fresh", the slot takes writes again; a later hold warns again.
        Assert.True(_store.SetAside(Id));
        storage.Write(1, "fresh", SaveUrgency.Now);
        Assert.Equal("fresh", OnDisk());
        _store.Hold(Id);
        storage.Write(1, "dropped", SaveUrgency.Now);
        Assert.Equal(2, _log.Warnings.Count);
        Assert.Equal("fresh", OnDisk());
    }

    [Fact]
    public void AnUnreadableSaveAtTheFirstReadIsHeldAtOnce()
    {
        // Before the game can answer: so nothing it writes during Attach can
        // push the damaged files aside and start it fresh behind the player.
        _temp.Plant(Id, GameSaveStore.SaveFile, "damaged");
        var storage = Storage();

        var read = storage.Read();

        Assert.Equal(new SaveRead(SaveReadState.Unreadable, 0, null), read);
        Assert.Equal(read, storage.AttachRead);
        Assert.True(_store.IsHeld(Id));
        storage.Write(1, "fresh start", SaveUrgency.Now);
        Assert.Equal("damaged", File.ReadAllText(_temp.FileOf(Id, GameSaveStore.SaveFile)));
    }

    [Fact]
    public void OnlyTheFirstReadIsRememberedAndOnlyItHolds()
    {
        // Mid-session the game's whole state is in memory and is what is being
        // written: holding then would drop it, and the store sets a damaged
        // file aside on the next write anyway (A36).
        var storage = Storage();
        Assert.Equal(default, storage.Read());
        _temp.Plant(Id, GameSaveStore.SaveFile, "damaged");

        Assert.Equal(SaveReadState.Unreadable, storage.Read().State);

        Assert.Equal(default(SaveRead), storage.AttachRead);
        Assert.False(_store.IsHeld(Id));
        storage.Write(1, "the state in memory", SaveUrgency.Now);
        Assert.Equal("the state in memory", OnDisk());
        Assert.True(File.Exists(_temp.FileOf(Id, GameSaveStore.UnreadableSave(1))));
    }

    [Fact]
    public void AGoodReadIsTheStoresAndHoldsNothing()
    {
        Assert.True(_store.Write(Id, 3, "saved"));
        var storage = Storage();

        Assert.Equal(new SaveRead(SaveReadState.Ok, 3, "saved"), storage.Read());
        Assert.False(_store.IsHeld(Id));
    }

    // ---- Stopped -------------------------------------------------------------------

    [Fact]
    public void AStoppedGamesNewWritesAreDroppedButWhatItHandedOverBeforeIsFlushed()
    {
        var storage = Storage();
        storage.Write(1, "turn 1", SaveUrgency.Now);
        storage.Write(1, "accrued before the fault", SaveUrgency.Soon);

        storage.Stop();
        storage.Write(1, "after the fault", SaveUrgency.Now);
        storage.Write(1, "after the fault", SaveUrgency.Soon);
        Assert.Equal("turn 1", OnDisk());
        Assert.Single(_log.Warnings);

        storage.Flush();
        Assert.Equal("accrued before the fault", OnDisk());
    }

    [Fact]
    public void ClearForgetsThePendingWrite()
    {
        var storage = Storage();
        storage.Write(1, "accrued", SaveUrgency.Soon);

        storage.Clear();
        storage.Flush();

        Assert.False(storage.HasPending);
        Assert.Null(OnDisk());
    }
}

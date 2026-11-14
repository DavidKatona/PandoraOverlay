using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The fakes every game's tests stand on (<c>Support/</c>): if one of them lies,
/// every test built on it passes for the wrong reason, so they get tests of
/// their own. They live here, with the architecture's, because the fakes are
/// the architecture's test support (A02).
/// </summary>
public class TestSupportTests
{
    [Fact]
    public void AStorageStartsWithNoSave()
    {
        var storage = new MemoryStorage();

        Assert.Equal(new SaveRead(SaveReadState.None, 0, null), storage.Read());
        Assert.Equal(default, storage.Read()); // A34: "no save" is default(SaveRead)
        Assert.Empty(storage.Writes);
        Assert.False(storage.Held);
    }

    [Fact]
    public void AWriteIsReadBack()
    {
        var storage = new MemoryStorage();

        storage.Write(1, "{\"turn\":3}", SaveUrgency.Now);
        storage.Write(2, "{\"turn\":4}", SaveUrgency.Soon);

        Assert.Equal(new SaveRead(SaveReadState.Ok, 2, "{\"turn\":4}"), storage.Read());
        Assert.Equal(new[] { (1, "{\"turn\":3}", SaveUrgency.Now), (2, "{\"turn\":4}", SaveUrgency.Soon) }, storage.Writes);
    }

    [Fact]
    public void AHeldStorageKeepsTheOld()
    {
        var old = new SaveRead(SaveReadState.Unreadable, 0, null);
        var storage = new MemoryStorage { Stored = old, Held = true };

        storage.Write(1, "{\"fresh\":true}", SaveUrgency.Now);

        Assert.Equal(old, storage.Read());
        Assert.Single(storage.Writes); // refused, and still recorded

        // Released, the next write lands.
        storage.Held = false;
        storage.Write(1, "{\"fresh\":true}", SaveUrgency.Now);
        Assert.Equal(new SaveRead(SaveReadState.Ok, 1, "{\"fresh\":true}"), storage.Read());
    }

    [Fact]
    public void AVersionUnderOneIsRefusedAsTheHostRefusesIt()
    {
        var storage = new MemoryStorage();
        storage.Write(1, "kept", SaveUrgency.Now);

        storage.Write(0, "version zero", SaveUrgency.Now);
        storage.Write(-1, "below", SaveUrgency.Soon);

        Assert.Equal(new SaveRead(SaveReadState.Ok, 1, "kept"), storage.Read());
        Assert.Equal(3, storage.Writes.Count);
    }

    [Fact]
    public void TheClockStartsFixedAndMovesOnlyWhenMoved()
    {
        var clock = new ManualClock();

        Assert.Equal(ManualClock.Start, clock.UtcNow);
        Assert.Equal(DateTimeKind.Utc, clock.UtcNow.Kind);
        Assert.Equal(TimeSpan.Zero, clock.Elapsed);
        Assert.Equal(new ManualClock().UtcNow, clock.UtcNow); // the same every run, never the machine's

        clock.Advance(TimeSpan.FromSeconds(3));
        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(TimeSpan.FromSeconds(63), clock.Elapsed);
        Assert.Equal(ManualClock.Start.AddSeconds(63), clock.UtcNow);
    }

    [Fact]
    public void TheCalendarCanBeSetAndTheMonotonicClockCanNotFall()
    {
        var clock = new ManualClock { UtcNow = new DateTime(2027, 1, 1, 23, 59, 0, DateTimeKind.Utc) };

        clock.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal(new DateTime(2027, 1, 2, 0, 1, 0, DateTimeKind.Utc), clock.UtcNow);
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(TimeSpan.FromSeconds(-1)));
        Assert.Equal(TimeSpan.FromMinutes(2), clock.Elapsed);
    }

    [Fact]
    public void TheLogKeepsEveryLineAndTheWarningsApart()
    {
        var log = new CollectingLog();

        log.Log("run started");
        log.Warning("save held: writes refused");
        log.Log("turn 4");

        Assert.Equal(new[] { "run started", "save held: writes refused", "turn 4" }, log.Lines);
        Assert.Equal(new[] { "save held: writes refused" }, log.Warnings);
    }

    [Fact]
    public void TheHostHandsOutItsFakesAndCountsWhatComesBack()
    {
        var host = new FakeHost();

        Assert.Same(host.ManualClock, host.Clock);
        Assert.Same(host.Memory, host.Storage);
        Assert.Same(host.Collected, host.Log);

        host.TileChanged();
        host.TileChanged();
        host.Storage.Write(1, "x", SaveUrgency.Now);
        host.Log.Log("fact");
        host.ManualClock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(2, host.TileChanges);
        Assert.Equal("x", host.Memory.Read().Payload);
        Assert.Equal(new[] { "fact" }, host.Collected.Lines);
        Assert.Equal(TimeSpan.FromSeconds(1), host.Clock.Elapsed);
    }

    /// <summary>A fault is recorded and not rethrown, so a test can make a game throw and assert it was reported (A10).</summary>
    [Fact]
    public void AFaultIsRecordedAndNotRethrown()
    {
        var host = new FakeHost();
        var first = new InvalidOperationException("one");
        var second = new ArgumentException("two");

        host.Fault(first);
        host.Fault(second);

        Assert.Equal(new Exception[] { first, second }, host.Faults);
    }
}

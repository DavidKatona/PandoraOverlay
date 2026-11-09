using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>The Pack's Watch tick in the friend book: kept in friends.json, at most eleven, gone with the friend.</summary>
public class FriendWatchTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 18, 0, 0, DateTimeKind.Utc);

    private static FriendState F(string id) =>
        new(id, "N" + id, "Deinosuchus", "Male", 0.4, 1, 1, 1, 1, 0, false, false, false, 0, 0, true, false);

    private static FriendBook BookOf(int friends)
    {
        var book = new FriendBook();
        book.Sync(Enumerable.Range(1, friends).Select(i => F(i.ToString())).ToList(), Now);
        return book;
    }

    [Fact]
    public void WatchRoundTripsAndAnOldFileReadsFalse()
    {
        var book = BookOf(2);
        Assert.True(book.SetWatch("1", true));
        var back = FriendBook.FromJson(book.ToJson());
        Assert.True(back.Find("1")!.Watch);
        Assert.False(back.Find("2")!.Watch);
        Assert.Contains("\"Format\": 1", book.ToJson()); // the format stays 1

        var old = FriendBook.FromJson("""{"Format":1,"Friends":[{"SteamId":"7","Name":"a","ShowOnMap":true,"Notify":true}]}""");
        Assert.False(old.Find("7")!.Watch);
        Assert.Equal(0, old.WatchedCount);
    }

    [Fact]
    public void ApplyPrefsCopiesWatchAndRefusesATwelfth()
    {
        var book = BookOf(13);
        var draft = book.Clone();
        foreach (var d in draft) d.Watch = true; // thirteen ticked in a draft
        book.ApplyPrefs(draft);
        Assert.Equal(FriendBook.MaxWatched, book.WatchedCount);
        Assert.Equal(11, FriendBook.MaxWatched);

        // A swap in one batch: one unticked, another ticked; the untick lands first.
        var swap = book.Clone();
        var was = swap.First(d => d.Watch);
        var now = swap.First(d => !d.Watch);
        was.Watch = false;
        now.Watch = true;
        book.ApplyPrefs(swap);
        Assert.False(book.Watches(was.SteamId));
        Assert.True(book.Watches(now.SteamId));
        Assert.Equal(11, book.WatchedCount);
    }

    [Fact]
    public void CanWatchAtTheCap()
    {
        var book = BookOf(12);
        for (var i = 1; i <= 11; i++) Assert.True(book.SetWatch(i.ToString(), true));
        Assert.False(book.CanWatch("12"));
        Assert.False(book.SetWatch("12", true)); // refused
        Assert.False(book.Watches("12"));
        Assert.True(book.CanWatch("3"));         // a watched one stays watchable
        Assert.True(book.SetWatch("3", false));  // unwatching never is refused
        Assert.True(book.CanWatch("12"));
    }

    [Fact]
    public void SyncDropsTheTickWithTheFriend()
    {
        var book = BookOf(3);
        book.SetWatch("2", true);
        Assert.Equal(1, book.WatchedCount);
        book.Sync(new[] { F("1"), F("3") }, Now.AddMinutes(1)); // unfriended on the site
        Assert.Equal(0, book.WatchedCount);
        book.Sync(new[] { F("1"), F("2"), F("3") }, Now.AddMinutes(2)); // friends again: a fresh entry, not watched
        Assert.False(book.Watches("2"));
    }

    [Fact]
    public void SetWatchRaisesChangedOnlyWhenItChanges()
    {
        var book = BookOf(1);
        var raised = 0;
        book.Changed += () => raised++;
        book.SetWatch("1", true);
        book.SetWatch("1", true);
        Assert.Equal(1, raised);
        Assert.False(book.SetWatch("unknown", true));
    }
}

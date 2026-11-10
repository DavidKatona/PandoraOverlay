using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>The Pack's Watch tick in the friend book (its ticked mode): kept in friends.json, any number, gone with the friend.</summary>
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

    /// <summary>The 11 cap of 1.44.0-rc.1 is gone (the testers, Nov 10 2026): the frame cuts, nearest first.</summary>
    [Fact]
    public void AnyNumberCanBeTicked()
    {
        var book = BookOf(20);
        var draft = book.Clone();
        foreach (var d in draft) d.Watch = true;
        book.ApplyPrefs(draft);
        Assert.Equal(20, book.WatchedCount);

        var one = BookOf(13);
        for (var i = 1; i <= 13; i++) Assert.True(one.SetWatch(i.ToString(), true));
        Assert.Equal(13, one.WatchedCount);
        Assert.True(one.SetWatch("3", false));
        Assert.Equal(12, one.WatchedCount);
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

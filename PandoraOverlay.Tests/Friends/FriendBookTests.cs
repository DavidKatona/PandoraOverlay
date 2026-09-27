using Xunit;

namespace PandoraOverlay.Tests;

public class FriendBookTests
{
    private static readonly DateTime Now = new(2026, 9, 27, 18, 0, 0, DateTimeKind.Utc);

    private static FriendState F(string id, string name, bool inGame = true, string? dino = "Deinosuchus", double growth = 0.4) =>
        new(id, name, dino, "Male", growth, 1, 1, 1, 1, 0, false, false, false, 0, 0, inGame, false);

    [Fact]
    public void SyncAddsNewFriendsAndRaisesChangedOnce()
    {
        var book = new FriendBook();
        var raised = 0;
        book.Changed += () => raised++;

        Assert.True(book.Sync(new[] { F("1", "Ann"), F("2", "Bob", inGame: false) }, Now));
        Assert.Equal(1, raised);
        Assert.Equal(2, book.Count);
        Assert.Equal("Ann", book.Find("1")!.Name);
        Assert.Equal("Deinosuchus", book.Find("1")!.LastDino);
        Assert.Equal(Now, book.Find("1")!.LastSeenUtc);
        Assert.Null(book.Find("2")!.LastSeenUtc); // never seen in game

        // Same roster again, only growth moved: last-seen updates silently.
        Assert.False(book.Sync(new[] { F("1", "Ann", growth: 0.5), F("2", "Bob", inGame: false) }, Now.AddSeconds(6)));
        Assert.Equal(1, raised);
        Assert.Equal(0.5, book.Find("1")!.LastGrowth);
        Assert.Equal(Now.AddSeconds(6), book.Find("1")!.LastSeenUtc);
    }

    [Fact]
    public void SyncFollowsRenamesAndDropsUnfriended()
    {
        var book = new FriendBook();
        book.Sync(new[] { F("1", "Ann"), F("2", "Bob") }, Now);
        book.Find("2")!.Nickname = "Bobby";

        Assert.True(book.Sync(new[] { F("1", "Annie") }, Now.AddMinutes(1)));
        Assert.Equal("Annie", book.Find("1")!.Name);
        Assert.Null(book.Find("2")); // unfriended on the site: gone, preferences with it
    }

    [Fact]
    public void SyncIgnoresEntriesWithoutASteamIdAndDuplicates()
    {
        var book = new FriendBook();
        book.Sync(new[] { F("", "ghost"), F("1", "Ann"), F("1", "Ann again") }, Now);
        var only = Assert.Single(book.Items);
        Assert.Equal("Ann", only.Name);
    }

    [Fact]
    public void ApplyPrefsCopiesOnlyPreferencesOntoLiveEntries()
    {
        var book = new FriendBook();
        book.Sync(new[] { F("1", "Ann"), F("2", "Bob") }, Now);
        var draft = book.Clone();
        draft[0].Nickname = "  Annie  ";
        draft[0].Colour = 15; // wraps
        draft[0].ShowOnMap = false;
        draft[0].Notify = false;
        draft[0].Name = "hacked";       // roster fact: ignored
        draft[0].LastDino = "Troodon";  // roster fact: ignored
        draft.Add(new FriendEntry { SteamId = "3", Nickname = "stranger" }); // not on the roster: ignored

        var raised = 0;
        book.Changed += () => raised++;
        book.ApplyPrefs(draft);

        Assert.Equal(1, raised);
        var ann = book.Find("1")!;
        Assert.Equal("Annie", ann.Nickname);
        Assert.Equal(3, ann.Colour);
        Assert.False(ann.ShowOnMap);
        Assert.False(ann.Notify);
        Assert.Equal("Ann", ann.Name);
        Assert.Equal("Deinosuchus", ann.LastDino);
        Assert.Null(book.Find("3"));
    }

    [Fact]
    public void DisplayNamePrefersNicknameThenSiteNameThenFallback()
    {
        var book = new FriendBook();
        book.Sync(new[] { F("1", "Ann") }, Now);
        Assert.Equal("Ann", book.DisplayName("1"));
        book.Find("1")!.Nickname = "Annie";
        Assert.Equal("Annie", book.DisplayName("1"));
        Assert.Equal("Zed", book.DisplayName("9", "Zed"));
        Assert.Equal("A friend", book.DisplayName("9"));
        Assert.Equal("A friend", book.DisplayName(null));
    }

    [Fact]
    public void ColourFallsBackToTheStableDefault()
    {
        var book = new FriendBook();
        book.Sync(new[] { F("76561198000000001", "Ann") }, Now);
        Assert.Equal(FriendColour.Default("76561198000000001"), book.ColourOf("76561198000000001"));
        book.Find("76561198000000001")!.Colour = 7;
        Assert.Equal(7, book.ColourOf("76561198000000001"));
        Assert.True(book.ShowsOnMap("unknown")); // defaults for a friend the book hasn't met
        Assert.True(book.Notifies("unknown"));
    }

    [Fact]
    public void DefaultColourIsStableAndNeverOrangeOrWhite()
    {
        Assert.Equal(FriendColour.Default("76561198000000001"), FriendColour.Default("76561198000000001"));
        var seen = new HashSet<int>();
        for (var i = 0; i < 500; i++)
        {
            var colour = FriendColour.Default($"7656119800000{i:0000}");
            Assert.InRange(colour, 0, WaypointPalette.Count - 1);
            Assert.NotEqual(3, colour);  // orange: your own arrow
            Assert.NotEqual(10, colour); // white: the outline colour
            seen.Add(colour);
        }
        Assert.True(seen.Count >= 8, "the hash should spread across the palette");
    }

    [Fact]
    public void NicknamesAreSanitizedAndEmptyMeansNone()
    {
        Assert.Null(FriendBook.SanitizeNickname(null));
        Assert.Null(FriendBook.SanitizeNickname("   "));
        Assert.Equal("Big Ann", FriendBook.SanitizeNickname("  Big\tAnn \r\n"));
        Assert.Equal(FriendBook.MaxNicknameLength, FriendBook.SanitizeNickname(new string('x', 80))!.Length);
    }

    [Fact]
    public void JsonRoundTripsAndGatesBadEntries()
    {
        var book = new FriendBook();
        book.Sync(new[] { F("1", "Ann"), F("2", "Bob", inGame: false) }, Now);
        book.Find("1")!.Nickname = "Annie";
        book.Find("1")!.Colour = 5;

        var back = FriendBook.FromJson(book.ToJson());
        Assert.Equal(2, back.Count);
        Assert.Equal("Annie", back.Find("1")!.Nickname);
        Assert.Equal(5, back.Find("1")!.Colour);
        Assert.Equal(Now, back.Find("1")!.LastSeenUtc);

        var gated = FriendBook.FromJson("""{"Format":1,"Friends":[{"SteamId":"","Name":"x"},{"SteamId":"1","Name":"a","Colour":40},{"SteamId":"1","Name":"dup"},null]}""");
        var only = Assert.Single(gated.Items);
        Assert.Equal(4, only.Colour); // 40 wrapped into the 12-colour palette
        Assert.Empty(FriendBook.FromJson("not json").Items);
        Assert.Empty(FriendBook.FromJson("""{"Format":1}""").Items);
    }
}

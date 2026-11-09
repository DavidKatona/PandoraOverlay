using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>The Pack's rows: who gets one, in what order, showing what, and how many fit the frame (BRIEF.md).</summary>
public class PackRowsTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 18, 0, 0, DateTimeKind.Utc);
    private const string Deino = "Deinosuchus";

    internal static FriendState F(string id, string dino = Deino, bool inGame = true, double health = 0.9, bool hide = false,
                                  double? x = 1, double? y = 1, bool head = false) =>
        new(id, id, dino, "Male", 0.5, health, 0.8, 0.7, 0.6, 0, head, false, false, x, y, inGame, hide);

    /// <summary>A book that has seen these friends and watches the ones named.</summary>
    internal static FriendBook Book(IReadOnlyList<FriendState> roster, params string[] watch)
    {
        var book = new FriendBook();
        book.Sync(roster, Now);
        foreach (var id in watch) Assert.True(book.SetWatch(id, true));
        return book;
    }

    private static PackView Build(IReadOnlyList<FriendState>? roster, FriendBook book, bool edit = false, bool youInGame = true,
                                  PackFrame frame = PackFrame.Large, PackRowSize size = PackRowSize.Compact, PackLeavers? leavers = null,
                                  DateTime? now = null, string? you = Deino) =>
        PackRows.Build(roster, book, you, youInGame, leavers ?? new PackLeavers(), now ?? Now, edit, frame, size);

    private static string[] Ids(PackView v) => v.Rows.Select(r => r.SteamId).ToArray();

    [Fact]
    public void TheCapacitiesComeFromTheFrame()
    {
        Assert.Equal(2, PackRows.Capacity(PackFrame.Small, PackRowSize.Full));
        Assert.Equal(5, PackRows.Capacity(PackFrame.Small, PackRowSize.Compact));
        Assert.Equal(5, PackRows.Capacity(PackFrame.Large, PackRowSize.Full));
        Assert.Equal(11, PackRows.Capacity(PackFrame.Large, PackRowSize.Compact));
        Assert.Equal(FriendBook.MaxWatched, PackRows.Capacity(PackFrame.Large, PackRowSize.Compact)); // only Large compact holds the cap
        Assert.Equal(120, PackRows.Room(PackFrame.Small));
        Assert.Equal(270, PackRows.Room(PackFrame.Large));
    }

    [Fact]
    public void BarsFirstThenTheRestByTheNameYouSee()
    {
        var roster = new[] { F("zed"), F("amy", dino: "Carnotaurus"), F("bob"), F("cat", hide: true) };
        var book = Book(roster, "zed", "amy", "bob", "cat");
        book.Find("zed")!.Nickname = "Al"; // the name you see sorts it
        var v = Build(roster, book);
        Assert.Equal(new[] { "zed", "bob", "amy", "cat" }, Ids(v));
        Assert.Equal(new[] { PackRowKind.Live, PackRowKind.Live, PackRowKind.OtherSpecies, PackRowKind.NoStats }, v.Rows.Select(r => r.Kind));
        Assert.Equal("Al", v.Rows[0].Name);
        Assert.Equal("4 of 4 in game", v.Count);
    }

    [Fact]
    public void OwnSpeciesOnlyAsTheWebsiteComparesIt()
    {
        var roster = new[] { F("a", dino: "deinosuchus"), F("b", dino: "Carnotaurus"), F("c") };
        var book = Book(roster, "a", "b", "c");
        var v = Build(roster, book);
        Assert.Equal(PackRowKind.Live, v.Rows.Single(r => r.SteamId == "c").Kind);
        Assert.Equal(PackRowKind.OtherSpecies, v.Rows.Single(r => r.SteamId == "a").Kind); // a case difference is another species, as on the site
        Assert.Equal(PackRowKind.OtherSpecies, v.Rows.Single(r => r.SteamId == "b").Kind);
    }

    [Fact]
    public void AHiddenLocationOrNoPositionShowsNoStats()
    {
        var roster = new[] { F("hid", hide: true), F("nox", x: null), F("noy", y: null) };
        var book = Book(roster, "hid", "nox", "noy");
        var v = Build(roster, book);
        Assert.All(v.Rows, r => Assert.Equal(PackRowKind.NoStats, r.Kind));
        Assert.Equal(3, v.Rows.Count); // the rows stay (the owner, Nov 9)
    }

    [Fact]
    public void YouNotInGameShowsNoRows()
    {
        var roster = new[] { F("a") };
        var book = Book(roster, "a");
        var v = Build(roster, book, youInGame: false);
        Assert.Empty(v.Rows);
        Assert.Equal(PackRows.NotInGame, v.Empty);
        Assert.Equal("1 of 1 in game", v.Count);
        Assert.Equal(PackRows.NotInGame, Build(roster, book, youInGame: false, edit: true).Empty); // edit mode too
    }

    [Fact]
    public void OnlyWatchedFriendsAndOnlyInGameWhileLocked()
    {
        var roster = new[] { F("a"), F("b"), F("c", inGame: false) };
        var book = Book(roster, "a", "c");
        Assert.Equal(new[] { "a" }, Ids(Build(roster, book)));
        Assert.Equal("1 of 2 in game", Build(roster, book).Count);

        var nobody = Book(roster, "c");
        var v = Build(roster, nobody);
        Assert.Empty(v.Rows);
        Assert.Equal(PackRows.NobodyInGame, v.Empty);
    }

    [Fact]
    public void EditModeListsAwayFriendsLast()
    {
        var roster = new[] { F("a", inGame: false), F("b", dino: "Carnotaurus"), F("c") };
        var book = Book(roster, "a", "b", "c");
        var v = Build(roster, book, edit: true);
        Assert.Equal(new[] { "c", "b", "a" }, Ids(v));
        Assert.Equal(PackRowKind.Away, v.Rows[2].Kind);
    }

    [Fact]
    public void MoreFriendsThanRowsCloseWithACount()
    {
        var roster = Enumerable.Range(1, 6).Select(i => F("f" + i)).ToList();
        var book = Book(roster, roster.Select(f => f.SteamId!).ToArray());

        var smallFull = Build(roster, book, frame: PackFrame.Small, size: PackRowSize.Full);
        Assert.Equal(2, smallFull.Rows.Count); // the line fits under two full rows
        Assert.Equal(4, smallFull.More);
        Assert.Equal("+4 more in game", smallFull.MoreLine(editMode: false));
        Assert.Equal("+4 more", smallFull.MoreLine(editMode: true));

        var smallCompact = Build(roster, book, frame: PackFrame.Small, size: PackRowSize.Compact);
        Assert.Equal(4, smallCompact.Rows.Count); // the line takes the fifth row's room
        Assert.Equal(2, smallCompact.More);

        var five = Build(roster.Take(5).ToList(), book, frame: PackFrame.Small, size: PackRowSize.Compact);
        Assert.Equal(5, five.Rows.Count); // exactly the capacity: no line
        Assert.Equal(0, five.More);
        Assert.Null(five.MoreLine(false));
    }

    [Fact]
    public void ElevenFitTheLargeCompactFrame()
    {
        var roster = Enumerable.Range(1, 11).Select(i => F("f" + i)).ToList();
        var book = Book(roster, roster.Select(f => f.SteamId!).ToArray());
        var v = Build(roster, book, frame: PackFrame.Large, size: PackRowSize.Compact);
        Assert.Equal(11, v.Rows.Count);
        Assert.Equal(0, v.More);
        Assert.Equal("11 of 11 in game", v.Count);
    }

    [Fact]
    public void ALeaverIsHeldFiveMinutesInPlaceThenGone()
    {
        var with = new[] { F("a"), F("b"), F("c") };
        var book = Book(with, "a", "b", "c");
        var leavers = new PackLeavers();
        leavers.Update(with, Deino, Now);
        Assert.Equal(new[] { "a", "b", "c" }, Ids(Build(with, book, leavers: leavers)));

        var without = new[] { F("a"), F("b", inGame: false), F("c") };
        var left = Now.AddSeconds(6);
        leavers.Update(without, Deino, left);
        var held = Build(without, book, leavers: leavers, now: left);
        Assert.Equal(new[] { "a", "b", "c" }, Ids(held)); // in place, its rank kept
        Assert.Equal(PackRowKind.Held, held.Rows[1].Kind);
        Assert.Equal("2 of 3 in game", held.Count);

        var back = left.AddMinutes(2);
        leavers.Update(with, Deino, back);
        var live = Build(with, book, leavers: leavers, now: back);
        Assert.Equal(PackRowKind.Live, live.Rows[1].Kind); // back within it: live in place

        var leftAgain = back.AddSeconds(6);
        leavers.Update(without, Deino, leftAgain);
        Assert.Equal(PackRowKind.Held, Build(without, book, leavers: leavers, now: leftAgain.AddMinutes(4.9)).Rows[1].Kind);
        var after = leftAgain + PackRows.HoldAfterLeaving;
        leavers.Update(without, Deino, after);
        Assert.Equal(new[] { "a", "c" }, Ids(Build(without, book, leavers: leavers, now: after))); // the rows close up
    }

    [Fact]
    public void AHeldOtherSpeciesKeepsItsRankBelowTheBars()
    {
        var with = new[] { F("a", dino: "Carnotaurus"), F("z") };
        var book = Book(with, "a", "z");
        var leavers = new PackLeavers();
        leavers.Update(with, Deino, Now);
        var without = new[] { F("a", dino: "Carnotaurus", inGame: false), F("z") };
        leavers.Update(without, Deino, Now.AddSeconds(6));
        Assert.Equal(new[] { "z", "a" }, Ids(Build(without, book, leavers: leavers, now: Now.AddSeconds(6))));
    }

    [Fact]
    public void AClearedRosterHoldsNobodyNew()
    {
        var with = new[] { F("a") };
        var leavers = new PackLeavers();
        leavers.Update(with, Deino, Now);
        leavers.Update(null, Deino, Now.AddSeconds(6));
        Assert.Null(leavers.Held("a", Now.AddSeconds(6)));
    }

    [Fact]
    public void TheConfigWordsReadSafely()
    {
        Assert.Equal(PackFrame.Small, PackRows.FrameOf("small"));
        Assert.Equal(PackFrame.Large, PackRows.FrameOf("Large"));
        Assert.Equal(PackFrame.Small, PackRows.FrameOf(null));
        Assert.Equal(PackRowSize.Full, PackRows.SizeOf("full"));
        Assert.Equal(PackRowSize.Compact, PackRows.SizeOf("compact"));
        Assert.Equal(PackRowSize.Full, PackRows.SizeOf("odd"));
    }
}

using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The Pack's rows (BRIEF.md; reworked Nov 10 after the first testers): every
/// friend on your dino by default or only the ticked ones, bars only, a held
/// leaver, the nearest filling the frame with a margin, and name order.
/// </summary>
public class PackRowsTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 18, 0, 0, DateTimeKind.Utc);
    private const string Deino = "Deinosuchus";
    private const double M = 100; // world cm per metre

    internal static FriendState F(string id, string dino = Deino, bool inGame = true, double health = 0.9, bool hide = false,
                                  double? x = 1, double? y = 1, bool head = false) =>
        new(id, id, dino, "Male", 0.5, health, 0.8, 0.7, 0.6, 0, head, false, false, x, y, inGame, hide);

    /// <summary>A friend <paramref name="metres"/> east of you (you stand at 0, 0).</summary>
    private static FriendState At(string id, double metres, string dino = Deino) => F(id, dino, x: metres * M, y: 0);

    private static readonly PlayerState Me =
        new("me", "me", Deino, "Male", 0.5, 1, 1, 1, 1, 0, false, false, false, 0, 0, 0);

    /// <summary>A book that has seen these friends and ticks the ones named.</summary>
    internal static FriendBook Book(IReadOnlyList<FriendState> roster, params string[] watch)
    {
        var book = new FriendBook();
        book.Sync(roster, Now);
        foreach (var id in watch) Assert.True(book.SetWatch(id, true));
        return book;
    }

    private static PackView Build(IReadOnlyList<FriendState>? roster, FriendBook book, bool ticked = false, PlayerState? me = null,
                                  bool notInGame = false, PackFrame frame = PackFrame.Large, PackRowSize size = PackRowSize.Compact,
                                  PackLeavers? leavers = null, IReadOnlySet<string>? shown = null, DateTime? now = null) =>
        PackRows.Build(roster, book, notInGame ? null : me ?? Me, ticked, leavers ?? new PackLeavers(), shown ?? new HashSet<string>(),
                       now ?? Now, frame, size);

    private static string[] Ids(PackView v) => v.Rows.Select(r => r.SteamId).ToArray();

    [Fact]
    public void TheCapacitiesComeFromTheFrame()
    {
        Assert.Equal(2, PackRows.Capacity(PackFrame.Small, PackRowSize.Full));
        Assert.Equal(5, PackRows.Capacity(PackFrame.Small, PackRowSize.Compact));
        Assert.Equal(5, PackRows.Capacity(PackFrame.Large, PackRowSize.Full));
        Assert.Equal(11, PackRows.Capacity(PackFrame.Large, PackRowSize.Compact));
        Assert.Equal(120, PackRows.Room(PackFrame.Small));
        Assert.Equal(270, PackRows.Room(PackFrame.Large));
    }

    [Fact]
    public void EveryFriendOnYourDinoByDefaultWithNoTicks()
    {
        var roster = new[] { F("bob"), F("amy") };
        var v = Build(roster, Book(roster));
        Assert.Equal(new[] { "amy", "bob" }, Ids(v));
        Assert.All(v.Rows, r => Assert.Equal(PackRowKind.Live, r.Kind));
        Assert.Equal("2 on your dino", v.Count);
    }

    [Fact]
    public void TheTickedModeShowsOnlyTheTickedOnes()
    {
        var roster = new[] { F("a"), F("b"), F("c") };
        var book = Book(roster, "a", "c");
        Assert.Equal(new[] { "a", "c" }, Ids(Build(roster, book, ticked: true)));
        Assert.Equal("2 on your dino", Build(roster, book, ticked: true).Count);
        Assert.Equal(PackRows.NobodyOnYourDino, Build(roster, Book(roster), ticked: true).Empty); // none ticked
    }

    [Fact]
    public void NoTickedCapAnyMore()
    {
        var roster = Enumerable.Range(1, 15).Select(i => F("f" + i)).ToList();
        var book = Book(roster, roster.Select(f => f.SteamId!).ToArray()); // fifteen ticks all take
        Assert.Equal(15, book.WatchedCount);
        Assert.Equal("15 on your dino", Build(roster, book, ticked: true).Count);
    }

    [Fact]
    public void OnlyRowsWithBars()
    {
        var roster = new[] { F("same"), F("case", dino: "deinosuchus"), F("carno", dino: "Carnotaurus"), F("hid", hide: true),
                             F("nox", x: null), F("noy", y: null), F("out", inGame: false) };
        var v = Build(roster, Book(roster));
        Assert.Equal(new[] { "same" }, Ids(v)); // a case difference is another species, as on the site; hidden, no position, away: left out
    }

    [Fact]
    public void YouNotInGameShowsNoRows()
    {
        var roster = new[] { F("a") };
        var v = Build(roster, Book(roster), notInGame: true);
        Assert.Empty(v.Rows);
        Assert.Equal(PackRows.NotInGame, v.Empty);
    }

    [Fact]
    public void NobodyOnYourDinoSaysSo()
    {
        var roster = new[] { F("a", dino: "Carnotaurus") };
        var v = Build(roster, Book(roster));
        Assert.Equal(PackRows.NobodyOnYourDino, v.Empty);
        Assert.Equal("0 on your dino", v.Count);
    }

    [Fact]
    public void TheNearestFillTheFrameListedByName()
    {
        // Seven on your dino at 10..70 m; the small compact frame holds four and the closing line.
        var roster = new[] { At("g", 70), At("a", 60), At("f", 10), At("c", 40), At("e", 20), At("b", 50), At("d", 30) };
        var v = Build(roster, Book(roster), frame: PackFrame.Small, size: PackRowSize.Compact);
        Assert.Equal(new[] { "c", "d", "e", "f" }, Ids(v)); // the four nearest (f 10, e 20, d 30, c 40), in name order
        Assert.Equal(3, v.More);
        Assert.Equal("+3 more", v.MoreLine);
        Assert.Equal("7 on your dino", v.Count);

        var smallFull = Build(roster, Book(roster), frame: PackFrame.Small, size: PackRowSize.Full);
        Assert.Equal(new[] { "e", "f" }, Ids(smallFull)); // two full rows and the line
        Assert.Equal(5, smallFull.More);
    }

    [Fact]
    public void ExactlyTheCapacityNeedsNoLine()
    {
        var roster = Enumerable.Range(1, 11).Select(i => At("f" + i.ToString("00"), i * 10)).ToList();
        var v = Build(roster, Book(roster), frame: PackFrame.Large, size: PackRowSize.Compact);
        Assert.Equal(11, v.Rows.Count);
        Assert.Equal(0, v.More);
        Assert.Null(v.MoreLine);
    }

    [Fact]
    public void AFriendOnScreenKeepsTheirRowUntilAnotherIsClearlyNearer()
    {
        var first = new[] { At("a", 10), At("b", 20), At("c", 30), At("d", 40), At("e", 50), At("far", 200) };
        var book = Book(first);
        var v = Build(first, book, frame: PackFrame.Small, size: PackRowSize.Compact);
        Assert.Equal(new[] { "a", "b", "c", "d" }, Ids(v));

        // e walks in to 35 m, nearer than d at 40 but within the margin: d keeps the row.
        var close = new[] { At("a", 10), At("b", 20), At("c", 30), At("d", 40), At("e", 35), At("far", 200) };
        v = Build(close, book, frame: PackFrame.Small, size: PackRowSize.Compact, shown: v.Shown);
        Assert.Equal(new[] { "a", "b", "c", "d" }, Ids(v));

        // d wanders off to 300 m: now e is clearly nearer, and takes the row.
        var gone = new[] { At("a", 10), At("b", 20), At("c", 30), At("d", 300), At("e", 35), At("far", 200) };
        v = Build(gone, book, frame: PackFrame.Small, size: PackRowSize.Compact, shown: v.Shown);
        Assert.Equal(new[] { "a", "b", "c", "e" }, Ids(v));
        Assert.Equal(PackRows.Hysteresis, 100 * M);
    }

    [Fact]
    public void ALeaverWithBarsIsHeldFiveMinutesInPlaceThenGone()
    {
        var with = new[] { F("a"), F("b"), F("c") };
        var book = Book(with);
        var leavers = new PackLeavers();
        leavers.Update(with, Deino, Now);

        var without = new[] { F("a"), F("b", inGame: false), F("c") };
        var left = Now.AddSeconds(6);
        leavers.Update(without, Deino, left);
        var held = Build(without, book, leavers: leavers, now: left);
        Assert.Equal(new[] { "a", "b", "c" }, Ids(held)); // in place, by name
        Assert.Equal(PackRowKind.Held, held.Rows[1].Kind);
        Assert.Equal("2 on your dino", held.Count); // a held friend is not counted as on your dino

        var back = left.AddMinutes(2);
        leavers.Update(with, Deino, back);
        Assert.Equal(PackRowKind.Live, Build(with, book, leavers: leavers, now: back).Rows[1].Kind); // back within it: live in place

        var leftAgain = back.AddSeconds(6);
        leavers.Update(without, Deino, leftAgain);
        var after = leftAgain + PackRows.HoldAfterLeaving;
        leavers.Update(without, Deino, after);
        Assert.Equal(new[] { "a", "c" }, Ids(Build(without, book, leavers: leavers, now: after))); // the rows close up
    }

    [Fact]
    public void LosingTheBarsInGameIsNotLeaving()
    {
        var with = new[] { F("a"), F("b") };
        var leavers = new PackLeavers();
        leavers.Update(with, Deino, Now);
        var swapped = new[] { F("a"), F("b", dino: "Carnotaurus") }; // still in game, another dino now
        leavers.Update(swapped, Deino, Now.AddSeconds(6));
        Assert.Null(leavers.Held("b", Now.AddSeconds(6)));
        Assert.Equal(new[] { "a" }, Ids(Build(swapped, Book(with), leavers: leavers, now: Now.AddSeconds(6))));

        var hidden = new[] { F("a", hide: true), F("b") };
        leavers.Update(hidden, Deino, Now.AddSeconds(12)); // hid their location: gone at once, never held
        Assert.Null(leavers.Held("a", Now.AddSeconds(12)));
    }

    [Fact]
    public void AHeldUntickedFriendStaysOutOfTheTickedMode()
    {
        var with = new[] { F("a"), F("b") };
        var book = Book(with, "a");
        var leavers = new PackLeavers();
        leavers.Update(with, Deino, Now);
        var without = new[] { F("a", inGame: false), F("b", inGame: false) };
        leavers.Update(without, Deino, Now.AddSeconds(6));
        Assert.Equal(new[] { "a" }, Ids(Build(without, book, ticked: true, leavers: leavers, now: Now.AddSeconds(6))));
    }

    [Fact]
    public void AClearedRosterHoldsNobodyNew()
    {
        var leavers = new PackLeavers();
        leavers.Update(new[] { F("a") }, Deino, Now);
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
        Assert.False(PackRows.TickedOnly("all"));
        Assert.False(PackRows.TickedOnly(null));
        Assert.True(PackRows.TickedOnly("ticked"));
        Assert.Equal("all", new OverlayConfig().PackShows); // every friend on your dino by default
    }
}

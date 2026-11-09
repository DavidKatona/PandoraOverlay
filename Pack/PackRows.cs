namespace PandoraOverlay;

/// <summary>The Pack's frame: one of the two widget frames (<see cref="WidgetFrame"/>), the player's choice on Settings → Friends.</summary>
public enum PackFrame
{
    /// <summary>The Activity feed's, 298 × 168: the default.</summary>
    Small,
    /// <summary>The minimap's, 298 × 318.</summary>
    Large,
}

/// <summary>The Pack's rows: Full (the name line, then four captioned thin bars) or Compact (one line, health only).</summary>
public enum PackRowSize
{
    Full,
    Compact,
}

/// <summary>What one row of the Pack shows.</summary>
public enum PackRowKind
{
    /// <summary>In game, sharing their location, with a position, on exactly your dino: the bars (the website's Friends stats rule).</summary>
    Live,
    /// <summary>In game on another dino: "Other species · no stats shown", no bars.</summary>
    OtherSpecies,
    /// <summary>In game but hiding their location or without a position: "No stats shown", no bars and no reason (it is their setting).</summary>
    NoStats,
    /// <summary>Left the game under five minutes ago: their row held as "Not in game" where it was, so the rows under it don't jump.</summary>
    Held,
    /// <summary>Not in game: listed in edit mode only, as a sample.</summary>
    Away,
}

/// <summary>One row to draw: who, what it shows, and the record it shows it from (the last one seen, for a held row).</summary>
public sealed record PackRow(string SteamId, string Name, int Colour, PackRowKind Kind, FriendState? Friend, DateTime? LastSeenUtc)
{
    /// <summary>The friend's dino for the name line: the record's, else the book's last seen.</summary>
    public string? Dino { get; init; }

    /// <summary>Growth 0–1 for the name line; null = unknown.</summary>
    public double? Growth { get; init; }
}

/// <summary>What the Pack draws this moment: the rows that fit, the closing line's count, the header's count and the body's words when there are no rows.</summary>
/// <param name="Rows">The rows on screen, in order.</param>
/// <param name="More">Rows that didn't fit: the closing line "+N more in game" ("+N more" in edit mode); 0 = no line.</param>
/// <param name="InGame">Watched friends in game, for the header's "3 of 5 in game".</param>
/// <param name="Watched">Watched friends.</param>
/// <param name="Empty">The body's line when there are no rows ("You're not in game", "No watched friend in game"), else null.</param>
public sealed record PackView(IReadOnlyList<PackRow> Rows, int More, int InGame, int Watched, string? Empty)
{
    /// <summary>The header's right-hand count.</summary>
    public string Count => $"{InGame} of {Watched} in game";

    /// <summary>The closing line, or null.</summary>
    public string? MoreLine(bool editMode) => More <= 0 ? null : editMode ? $"+{More} more" : $"+{More} more in game";
}

/// <summary>
/// The Pack widget's rows, pure and tested (BRIEF.md, Nov 2026). From the
/// roster, the friend book's Watch ticks, your dino and whether you are in
/// game, the held leavers and the clock: which watched friends get a row, in
/// what order, showing what, and how many fit the frame.
/// <para>
/// The bars follow the website's own "Friends stats" panel (its live-map
/// script, read Nov 9 2026): a friend who is in game, not hiding their
/// location and with a position (<see cref="FriendState.OnMap"/>, the same
/// filter as the map's arrows), and of those only the ones whose dino equals
/// yours exactly, compared as the site compares them (case counts). The
/// server's rules forbid mixpacking too. Another dino gets a row without
/// bars; so does a friend who hides their location, with no reason given.
/// </para>
/// <para>
/// Order: friends with bars first, then the rest in game, each by the name
/// you see (nickname, else the site's), case ignored; away friends last in
/// edit mode. A friend who leaves keeps their row and its rank for
/// <see cref="HoldAfterLeaving"/> (<see cref="PackLeavers"/>).
/// </para>
/// <para>
/// The heights are the code's, never a theme's: a full row 42 with 6 between,
/// a compact row 20 with 4, the closing line 20. The room is the frame less
/// the border, the padding and the header (<see cref="FrameContract"/>), so
/// the capacity (Small 2 full or 5 compact, Large 5 or 11) is computed, not
/// typed in. With more rows than fit, the closing line takes a row's room
/// where needed. The widget is click-through and never scrolls.
/// </para>
/// </summary>
public static class PackRows
{
    public const double FullRowHeight = 42, FullRowGap = 6;
    public const double CompactRowHeight = 20, CompactRowGap = 4;
    public const double MoreLineHeight = 20;

    /// <summary>How long a friend who left keeps their row as "Not in game" (the owner, Nov 8 2026).</summary>
    public static readonly TimeSpan HoldAfterLeaving = TimeSpan.FromMinutes(5);

    public const string NotInGame = "You're not in game";
    public const string NobodyInGame = "No watched friend in game";

    /// <summary>The frame's outer height at 100%.</summary>
    public static double FrameHeight(PackFrame frame) => frame == PackFrame.Large ? WidgetFrame.LargeHeight : WidgetFrame.SmallHeight;

    /// <summary>The rows' room: the frame less its border, its padding and the header with its gap.</summary>
    public static double Room(PackFrame frame) =>
        FrameHeight(frame) - 2 * FrameContract.WidgetBorder - 2 * FrameContract.WidgetPaddingY
        - FrameContract.PackHeaderHeight - FrameContract.PackHeaderGap;

    public static double RowHeight(PackRowSize size) => size == PackRowSize.Compact ? CompactRowHeight : FullRowHeight;

    public static double RowGap(PackRowSize size) => size == PackRowSize.Compact ? CompactRowGap : FullRowGap;

    /// <summary>Rows that fit the room, with or without the closing line under them (its gap is a row's).</summary>
    public static int RowsThatFit(PackFrame frame, PackRowSize size, bool withMore)
    {
        var room = Room(frame);
        double h = RowHeight(size), gap = RowGap(size);
        var n = 0;
        while ((n + 1) * h + n * gap + (withMore ? gap + MoreLineHeight : 0) <= room) n++;
        return n;
    }

    /// <summary>How many rows the frame holds with no closing line: Small 2 full or 5 compact, Large 5 or 11.</summary>
    public static int Capacity(PackFrame frame, PackRowSize size) => RowsThatFit(frame, size, withMore: false);

    /// <summary>The config's words ("small" | "large"; anything else is small).</summary>
    public static PackFrame FrameOf(string? value) =>
        string.Equals(value, "large", StringComparison.OrdinalIgnoreCase) ? PackFrame.Large : PackFrame.Small;

    /// <summary>The config's words ("full" | "compact"; anything else is full).</summary>
    public static PackRowSize SizeOf(string? value) =>
        string.Equals(value, "compact", StringComparison.OrdinalIgnoreCase) ? PackRowSize.Compact : PackRowSize.Full;

    /// <summary>
    /// The website's Friends stats rule: bars only for a friend on the map
    /// (in game, sharing, with a position) on exactly your dino.
    /// </summary>
    public static bool HasBars(FriendState friend, string? yourDino) =>
        friend.OnMap && yourDino is not null && string.Equals(friend.Dino, yourDino, StringComparison.Ordinal);

    /// <summary>Everything the Pack draws now.</summary>
    /// <param name="roster">The last roster (you filtered out); null = none yet, or cleared.</param>
    /// <param name="book">The friend book: who is watched, their names and colours.</param>
    /// <param name="yourDino">Your dino while in game.</param>
    /// <param name="youInGame">False: "You're not in game", no rows ("your species" can't be judged).</param>
    /// <param name="leavers">The friends who left in the last five minutes.</param>
    public static PackView Build(IReadOnlyList<FriendState>? roster, FriendBook book, string? yourDino, bool youInGame,
                                 PackLeavers leavers, DateTime now, bool editMode, PackFrame frame, PackRowSize size)
    {
        var watched = book.Watched.ToList();
        var byId = new Dictionary<string, FriendState>();
        foreach (var f in roster ?? Array.Empty<FriendState>())
        {
            if (f.SteamId is { } id && !byId.ContainsKey(id)) byId[id] = f;
        }
        var inGame = watched.Count(e => byId.TryGetValue(e.SteamId, out var f) && f.InGame);
        if (!youInGame) return new PackView(Array.Empty<PackRow>(), 0, inGame, watched.Count, NotInGame);

        var ranked = new List<(int Rank, PackRow Row)>();
        foreach (var entry in watched)
        {
            var name = book.DisplayName(entry.SteamId, entry.Name);
            var colour = book.ColourOf(entry.SteamId);
            if (byId.TryGetValue(entry.SteamId, out var f) && f.InGame)
            {
                var kind = HasBars(f, yourDino) ? PackRowKind.Live : f.OnMap ? PackRowKind.OtherSpecies : PackRowKind.NoStats;
                ranked.Add((kind == PackRowKind.Live ? 0 : 1,
                            new PackRow(entry.SteamId, name, colour, kind, f, entry.LastSeenUtc) { Dino = f.Dino, Growth = f.Growth }));
            }
            else if (leavers.Held(entry.SteamId, now) is { } held)
            {
                ranked.Add((held.Rank, new PackRow(entry.SteamId, name, colour, PackRowKind.Held, held.Last, entry.LastSeenUtc)
                                       { Dino = held.Last.Dino, Growth = held.Last.Growth }));
            }
            else if (editMode)
            {
                ranked.Add((2, new PackRow(entry.SteamId, name, colour, PackRowKind.Away, null, entry.LastSeenUtc)
                               { Dino = entry.LastDino, Growth = entry.LastGrowth }));
            }
        }

        var rows = ranked
            .OrderBy(r => r.Rank)
            .ThenBy(r => r.Row.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Row.SteamId, StringComparer.Ordinal)
            .Select(r => r.Row)
            .ToList();
        if (rows.Count == 0) return new PackView(rows, 0, inGame, watched.Count, NobodyInGame);

        var more = 0;
        if (rows.Count > Capacity(frame, size))
        {
            var fit = RowsThatFit(frame, size, withMore: true);
            more = rows.Count - fit;
            rows = rows.Take(fit).ToList();
        }
        return new PackView(rows, more, inGame, watched.Count, null);
    }
}

/// <summary>
/// The Pack's held leavers: a friend who was in game in the last roster and
/// is not in this one keeps their row as "Not in game" for
/// <see cref="PackRows.HoldAfterLeaving"/>, at the rank they had (bars first,
/// then the rest), so the rows under it don't jump mid-fight. Back within
/// it, the row goes live in place; after it, the rows close up. A cleared
/// roster (null) tells nothing about who left, so it holds no one new.
/// </summary>
public sealed class PackLeavers
{
    /// <summary>One held friend: when they left, the last record seen and the rank they had (0 bars, 1 the rest).</summary>
    public sealed record Leaver(DateTime LeftUtc, FriendState Last, int Rank);

    private readonly Dictionary<string, Leaver> _held = new();
    private Dictionary<string, (FriendState Friend, int Rank)> _wasInGame = new();

    /// <summary>A new roster: who left is held, who came back is let go, and the old holds expire.</summary>
    public void Update(IReadOnlyList<FriendState>? roster, string? yourDino, DateTime now)
    {
        Expire(now);
        if (roster is null) return;
        var inGame = new Dictionary<string, (FriendState, int)>();
        foreach (var f in roster)
        {
            if (f.SteamId is not { } id || !f.InGame || inGame.ContainsKey(id)) continue;
            inGame[id] = (f, PackRows.HasBars(f, yourDino) ? 0 : 1);
            _held.Remove(id); // back: the row is live again, in place
        }
        foreach (var (id, was) in _wasInGame)
        {
            if (!inGame.ContainsKey(id)) _held[id] = new Leaver(now, was.Friend, was.Rank);
        }
        _wasInGame = inGame;
    }

    /// <summary>The hold on this friend, or null (never left, back, or over five minutes ago).</summary>
    public Leaver? Held(string steamId, DateTime now) =>
        _held.TryGetValue(steamId, out var l) && now - l.LeftUtc < PackRows.HoldAfterLeaving ? l : null;

    /// <summary>True while anyone is held: the window ages the holds on a timer of its own only then.</summary>
    public bool Any(DateTime now) => _held.Values.Any(l => now - l.LeftUtc < PackRows.HoldAfterLeaving);

    private void Expire(DateTime now)
    {
        foreach (var id in _held.Where(kv => now - kv.Value.LeftUtc >= PackRows.HoldAfterLeaving).Select(kv => kv.Key).ToList()) _held.Remove(id);
    }
}

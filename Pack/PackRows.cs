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
    /// <summary>Had bars and left the game under five minutes ago: their row held as "Not in game" where it was, so the rows under it don't jump.</summary>
    Held,
}

/// <summary>One row to draw: who, what it shows, and the record it shows it from (the last one seen, for a held row).</summary>
public sealed record PackRow(string SteamId, string Name, int Colour, PackRowKind Kind, FriendState Friend)
{
    /// <summary>The friend's dino for the name line.</summary>
    public string? Dino => Friend.Dino;

    /// <summary>Growth 0–1 for the name line.</summary>
    public double Growth => Friend.Growth;
}

/// <summary>What the Pack draws this moment.</summary>
/// <param name="Rows">The rows on screen, in name order.</param>
/// <param name="More">Friends with bars that didn't fit, the farthest: the closing line "+N more"; 0 = no line.</param>
/// <param name="OnYourDino">Friends the Pack counts now (with bars, and watched in the ticked mode), for the header.</param>
/// <param name="Empty">The body's line when there are no rows ("You're not in game", "No friend on your dino in game"), else null.</param>
/// <param name="Shown">Who is on screen, handed back next time so a friend at the edge keeps their row (<see cref="PackRows.Hysteresis"/>).</param>
public sealed record PackView(IReadOnlyList<PackRow> Rows, int More, int OnYourDino, string? Empty, IReadOnlySet<string> Shown)
{
    /// <summary>The header's right-hand count.</summary>
    public string Count => $"{OnYourDino} on your dino";

    /// <summary>The closing line, or null.</summary>
    public string? MoreLine => More <= 0 ? null : $"+{More} more";
}

/// <summary>
/// The Pack widget's rows, pure and tested (BRIEF.md, Nov 2026; reworked
/// Nov 10 after the first testers' feedback). From the roster, the friend
/// book, you, the held leavers and the clock: which friends get a row, in
/// what order, and how many fit the frame.
/// <para>
/// Who: the friends the website's own "Friends stats" panel would show (its
/// live-map script, read Nov 9 2026): in game, not hiding their location and
/// with a position (<see cref="FriendState.OnMap"/>, the map's arrows' filter),
/// and of those only the ones whose dino equals yours exactly, compared as the
/// site compares them (case counts; the server's rules forbid mixpacking).
/// All of them by default; only the ones you tick in the ticked mode
/// (<see cref="OverlayConfig.PackShows"/>), with no cap: the frame cuts. No
/// row has no bars (the owner, Nov 10: another species and a hidden friend
/// are left out), but a friend who had bars and LEFT keeps their row for
/// <see cref="HoldAfterLeaving"/> (<see cref="PackLeavers"/>).
/// </para>
/// <para>
/// Which, when more qualify than the frame holds: the NEAREST to you (a tester's
/// idea, Nov 10: with a big friends list, the ones beside you are your pack),
/// with a margin (<see cref="Hysteresis"/>): a friend on screen keeps their row
/// until another is that much nearer, so one at the edge doesn't flicker in
/// and out. The rest are "+N more". The rows themselves are in NAME order (the
/// name you see, case ignored): sorted by distance they would swap places every
/// roster as the pack moves, mid-fight (the owner's call: nearest fill, name
/// order; nothing rearranges by itself). Distance is only ever computed for a
/// friend on the map, from positions already polled, and never shown.
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

    /// <summary>
    /// A friend on screen keeps their row until one off screen is this much
    /// nearer (world cm: 100 m). A pack in a fight stays within a few dozen
    /// metres; a friend 100 m nearer than one shown is clearly closer.
    /// </summary>
    public const double Hysteresis = 10_000;

    public const string NotInGame = "You're not in game";
    public const string NobodyOnYourDino = "No friend on your dino in game";

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

    /// <summary>The config's words for who the Pack shows: "ticked" = only the friends you tick; anything else = all your friends on your dino (the default).</summary>
    public static bool TickedOnly(string? value) => string.Equals(value, "ticked", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The website's Friends stats rule: bars only for a friend on the map
    /// (in game, sharing, with a position) on exactly your dino.
    /// </summary>
    public static bool HasBars(FriendState friend, string? yourDino) =>
        friend.OnMap && yourDino is not null && string.Equals(friend.Dino, yourDino, StringComparison.Ordinal);

    /// <summary>Everything the Pack draws now.</summary>
    /// <param name="roster">The last roster (you filtered out); null = none yet, or cleared.</param>
    /// <param name="book">The friend book: names, colours and, in the ticked mode, who is ticked.</param>
    /// <param name="me">You while in game; null: "You're not in game", no rows ("your dino" can't be judged).</param>
    /// <param name="tickedOnly">Only the friends you tick (else every friend on your dino).</param>
    /// <param name="leavers">The friends with bars who left in the last five minutes.</param>
    /// <param name="shownBefore">Who was on screen last time (<see cref="PackView.Shown"/>), for the margin.</param>
    public static PackView Build(IReadOnlyList<FriendState>? roster, FriendBook book, PlayerState? me, bool tickedOnly,
                                 PackLeavers leavers, IReadOnlySet<string> shownBefore, DateTime now, PackFrame frame, PackRowSize size)
    {
        var none = new HashSet<string>();
        if (me is null) return new PackView(Array.Empty<PackRow>(), 0, 0, NotInGame, none);

        bool Counts(string id) => !tickedOnly || book.Watches(id);
        var candidates = new List<PackRow>();
        var seen = new HashSet<string>();
        var inGame = new HashSet<string>();
        foreach (var f in roster ?? Array.Empty<FriendState>())
        {
            if (f.SteamId is not { } id || !seen.Add(id)) continue;
            if (f.InGame) inGame.Add(id);
            if (HasBars(f, me.Dino) && Counts(id))
                candidates.Add(new PackRow(id, book.DisplayName(id, f.Name), book.ColourOf(id), PackRowKind.Live, f));
        }
        var onYourDino = candidates.Count;
        foreach (var held in leavers.HeldNow(now))
        {
            var id = held.Last.SteamId!;
            if (inGame.Contains(id) || !Counts(id)) continue; // in game again (with bars or not): never held
            candidates.Add(new PackRow(id, book.DisplayName(id, held.Last.Name), book.ColourOf(id), PackRowKind.Held, held.Last));
        }
        if (candidates.Count == 0) return new PackView(candidates, 0, 0, NobodyOnYourDino, none);

        var picked = candidates;
        var more = 0;
        if (candidates.Count > Capacity(frame, size))
        {
            var fit = RowsThatFit(frame, size, withMore: true);
            picked = candidates
                .OrderBy(r => Distance(r.Friend, me) - (shownBefore.Contains(r.SteamId) ? Hysteresis : 0))
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.SteamId, StringComparer.Ordinal)
                .Take(fit)
                .ToList();
            more = candidates.Count - fit;
        }
        var rows = picked
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.SteamId, StringComparer.Ordinal)
            .ToList();
        return new PackView(rows, more, onYourDino, null, rows.Select(r => r.SteamId).ToHashSet());
    }

    /// <summary>World cm between you and a friend on the map (a held friend: where they were last seen).</summary>
    internal static double Distance(FriendState friend, PlayerState me) =>
        friend is { X: { } x, Y: { } y } ? Math.Sqrt((x - me.X) * (x - me.X) + (y - me.Y) * (y - me.Y)) : double.MaxValue;
}

/// <summary>
/// The Pack's held leavers: a friend who had bars in the last roster (on the
/// map, on your dino) and is not in game in this one keeps their row as "Not
/// in game" for <see cref="PackRows.HoldAfterLeaving"/>, so the rows under it
/// don't jump mid-fight. Back within it, the row goes live in place; after it,
/// the rows close up. A friend who is still in game but lost their bars (they
/// changed dino or hid their location, or you changed yours) is not held: they
/// didn't leave. A cleared roster (null) tells nothing about who left, so it
/// holds no one new.
/// </summary>
public sealed class PackLeavers
{
    /// <summary>One held friend: when they left and the last record seen with bars.</summary>
    public sealed record Leaver(DateTime LeftUtc, FriendState Last);

    private readonly Dictionary<string, Leaver> _held = new();
    private Dictionary<string, FriendState> _hadBars = new();

    /// <summary>A new roster: who left is held, who is back in game is let go, and the old holds expire.</summary>
    public void Update(IReadOnlyList<FriendState>? roster, string? yourDino, DateTime now)
    {
        Expire(now);
        if (roster is null) return;
        var inGame = new HashSet<string>();
        var hadBars = new Dictionary<string, FriendState>();
        foreach (var f in roster)
        {
            if (f.SteamId is not { } id || !f.InGame) continue;
            inGame.Add(id);
            _held.Remove(id); // back in game: no longer held (with bars, the row is live again, in place)
            if (PackRows.HasBars(f, yourDino)) hadBars[id] = f;
        }
        foreach (var (id, last) in _hadBars)
        {
            if (!inGame.Contains(id)) _held[id] = new Leaver(now, last);
        }
        _hadBars = hadBars;
    }

    /// <summary>The hold on this friend, or null (never left, back, or over five minutes ago).</summary>
    public Leaver? Held(string steamId, DateTime now) =>
        _held.TryGetValue(steamId, out var l) && now - l.LeftUtc < PackRows.HoldAfterLeaving ? l : null;

    /// <summary>Every hold still running.</summary>
    public IEnumerable<Leaver> HeldNow(DateTime now) => _held.Values.Where(l => now - l.LeftUtc < PackRows.HoldAfterLeaving);

    /// <summary>True while anyone is held: the window ages the holds on a timer of its own only then.</summary>
    public bool Any(DateTime now) => HeldNow(now).Any();

    private void Expire(DateTime now)
    {
        foreach (var id in _held.Where(kv => now - kv.Value.LeftUtc >= PackRows.HoldAfterLeaving).Select(kv => kv.Key).ToList()) _held.Remove(id);
    }
}

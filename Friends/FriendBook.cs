using System.IO;
using System.Text.Json;

namespace PandoraOverlay;

/// <summary>
/// What the overlay remembers about one friend, by steamId. The roster
/// itself comes from the site on every fetch; this holds YOUR side of it —
/// a local nickname, a colour, whether they show on the map and in the feed
/// — plus the last-seen facts the Settings page lists. Mutable on purpose:
/// the Settings page edits rows in place on a draft copy.
/// </summary>
public sealed class FriendEntry
{
    public string SteamId { get; set; } = "";

    /// <summary>The site's name for them, as last seen.</summary>
    public string Name { get; set; } = "";

    /// <summary>Your own name for them (≤ 32 chars); null = use the site's. Never leaves this machine.</summary>
    public string? Nickname { get; set; }

    /// <summary>Palette index; null = the stable default FriendColour picks from the steamId.</summary>
    public int? Colour { get; set; }

    /// <summary>Draw their arrow on the minimap (when they are in game and share their location).</summary>
    public bool ShowOnMap { get; set; } = true;

    /// <summary>Post their events to the Friends widget's feed.</summary>
    public bool Notify { get; set; } = true;

    public string? LastDino { get; set; }
    public double? LastGrowth { get; set; }

    /// <summary>The last fetch that saw them in game (UTC); null = never this side of the file.</summary>
    public DateTime? LastSeenUtc { get; set; }

    public FriendEntry Clone() => (FriendEntry)MemberwiseClone();
}

/// <summary>
/// The default marker colour for a friend: a stable hash of the steamId into
/// the waypoint palette, so the same friend has the same colour every
/// session and on every machine (the site assigns by roster order, which
/// shifts whenever someone joins). Orange and white are skipped: orange is
/// your own arrow, white is a marker outline. A collision between two
/// friends is accepted — the feed names them, and the colour can be
/// overridden per friend.
/// </summary>
public static class FriendColour
{
    private static readonly int[] Defaults = { 0, 1, 2, 4, 5, 6, 7, 8, 9, 11 }; // palette indices minus orange (3) and white (10)

    public static int Default(string steamId)
    {
        // FNV-1a, 32-bit: tiny, deterministic, no dependency on string.GetHashCode's per-process seed.
        uint hash = 2166136261;
        foreach (var c in steamId ?? "")
        {
            hash ^= c;
            hash *= 16777619;
        }
        return Defaults[(int)(hash % (uint)Defaults.Length)];
    }
}

/// <summary>
/// The local friend book: one FriendEntry per friend the roster has ever
/// shown, kept in friends.json next to config.json — user content like the
/// waypoint library, apart from runtime state and the cookie vault. The
/// roster is the site's; Sync mirrors its membership and last-seen facts,
/// the Settings page edits the preferences. Changed fires for membership
/// and preference changes only (the owner saves on it), never for the
/// per-fetch last-seen updates — those are flushed on exit. Fail-soft:
/// nothing here throws.
/// </summary>
public sealed class FriendBook
{
    public const int MaxNicknameLength = 32;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string FilePath { get; } = Path.Combine(AppContext.BaseDirectory, "friends.json");

    private readonly List<FriendEntry> _items = new();

    public IReadOnlyList<FriendEntry> Items => _items;
    public int Count => _items.Count;

    /// <summary>Raised after a membership or preference change (not for last-seen updates).</summary>
    public event Action? Changed;

    // ---- Persistence --------------------------------------------------------

    public static FriendBook Load()
    {
        try
        {
            if (File.Exists(FilePath)) return FromJson(File.ReadAllText(FilePath));
        }
        catch
        {
            // Corrupt or unreadable: start empty; the roster refills it on the first fetch.
        }
        return new FriendBook();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath, ToJson());
        }
        catch
        {
            // Non-fatal: the in-memory book keeps working.
        }
    }

    /// <summary>Parses the file, dropping entries without a steamId and later duplicates; nicknames and colours are re-validated.</summary>
    internal static FriendBook FromJson(string json)
    {
        var book = new FriendBook();
        BookFile? file;
        try
        {
            file = JsonSerializer.Deserialize<BookFile>(json);
        }
        catch (JsonException)
        {
            return book;
        }
        if (file?.Friends is null) return book;

        var seen = new HashSet<string>();
        foreach (var raw in file.Friends)
        {
            if (raw is null || string.IsNullOrWhiteSpace(raw.SteamId) || !seen.Add(raw.SteamId)) continue;
            raw.Nickname = SanitizeNickname(raw.Nickname);
            raw.Name = raw.Name ?? "";
            if (raw.Colour is { } c) raw.Colour = WaypointPalette.Wrap(c);
            book._items.Add(raw);
        }
        return book;
    }

    internal string ToJson() =>
        JsonSerializer.Serialize(new BookFile { Format = 1, Friends = _items }, JsonOpts);

    private sealed class BookFile
    {
        public int Format { get; set; } = 1;
        public List<FriendEntry>? Friends { get; set; }
    }

    // ---- The roster ----------------------------------------------------------

    /// <summary>
    /// Mirrors a successful roster: unknown friends are added, friends no
    /// longer on it are dropped (their preferences with them — you unfriended
    /// them on the site), names follow the site, and in-game friends refresh
    /// their last-seen dino, growth and time. Returns true — and raises
    /// Changed — only when the membership or a name changed, so a fetch every
    /// few seconds never touches the disk.
    /// </summary>
    public bool Sync(IReadOnlyList<FriendState> roster, DateTime nowUtc)
    {
        var changed = false;
        var present = new HashSet<string>();
        foreach (var f in roster)
        {
            if (string.IsNullOrWhiteSpace(f.SteamId) || !present.Add(f.SteamId)) continue;
            var entry = Find(f.SteamId);
            if (entry is null)
            {
                entry = new FriendEntry { SteamId = f.SteamId };
                _items.Add(entry);
                changed = true;
            }
            var name = f.Name ?? "";
            if (name.Length > 0 && entry.Name != name)
            {
                entry.Name = name;
                changed = true;
            }
            if (f.InGame)
            {
                entry.LastDino = f.Dino ?? entry.LastDino;
                entry.LastGrowth = f.Growth;
                entry.LastSeenUtc = nowUtc;
            }
        }
        if (_items.RemoveAll(e => !present.Contains(e.SteamId)) > 0) changed = true;
        if (changed) Notify();
        return changed;
    }

    /// <summary>
    /// Commits the Settings draft: preferences (nickname, colour, map, feed)
    /// are copied onto the matching live entries by steamId. Membership and
    /// last-seen facts stay the roster's — a draft opened minutes ago must
    /// not resurrect a friend Sync has since dropped.
    /// </summary>
    public void ApplyPrefs(IEnumerable<FriendEntry> draft)
    {
        foreach (var d in draft)
        {
            if (Find(d.SteamId) is not { } live) continue;
            live.Nickname = SanitizeNickname(d.Nickname);
            live.Colour = d.Colour is { } c ? WaypointPalette.Wrap(c) : null;
            live.ShowOnMap = d.ShowOnMap;
            live.Notify = d.Notify;
        }
        Notify();
    }

    /// <summary>Deep copy for a draft the Settings page can edit and discard.</summary>
    public List<FriendEntry> Clone() => _items.Select(e => e.Clone()).ToList();

    public void Notify() => Changed?.Invoke();

    // ---- Queries -------------------------------------------------------------

    public FriendEntry? Find(string? steamId) =>
        steamId is null ? null : _items.FirstOrDefault(e => e.SteamId == steamId);

    /// <summary>Your nickname for them, else the site's name, else the fallback.</summary>
    public string DisplayName(string? steamId, string? fallback = null)
    {
        var entry = Find(steamId);
        if (entry?.Nickname is { Length: > 0 } nick) return nick;
        if (entry?.Name is { Length: > 0 } name) return name;
        return fallback is { Length: > 0 } ? fallback : "A friend";
    }

    /// <summary>The palette index to draw them in: their override, else the stable default.</summary>
    public int ColourOf(string steamId) => Find(steamId)?.Colour ?? FriendColour.Default(steamId);

    public bool ShowsOnMap(string steamId) => Find(steamId)?.ShowOnMap ?? true;

    public bool Notifies(string steamId) => Find(steamId)?.Notify ?? true;

    /// <summary>Trims and caps a nickname; whitespace-only or empty becomes null (= use the site's name).</summary>
    public static string? SanitizeNickname(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var chars = raw.Select(c => char.IsWhiteSpace(c) ? ' ' : c).Where(c => !char.IsControl(c)).ToArray();
        var collapsed = string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (collapsed.Length > MaxNicknameLength) collapsed = collapsed[..MaxNicknameLength].TrimEnd();
        return collapsed.Length == 0 ? null : collapsed;
    }
}

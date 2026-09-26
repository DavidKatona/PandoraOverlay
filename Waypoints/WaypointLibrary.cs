using System.IO;
using System.Text.Json;

namespace PandoraOverlay;

/// <summary>
/// One library entry. World cm, a palette colour, a name. Mutable on purpose:
/// the Settings page edits rows in place on a draft copy.
/// </summary>
public sealed class Waypoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }

    /// <summary>Index into WaypointPalette.</summary>
    public int Colour { get; set; }

    public bool Visible { get; set; } = true;

    /// <summary>The import pack this came from; null = placed by hand (packs arrive in phase 3).</summary>
    public string? Pack { get; set; }

    public Waypoint Clone() => (Waypoint)MemberwiseClone();
}

/// <summary>
/// Twelve marker colours, chosen to stay tellable on a 230 px map (owner's
/// call, Sep 2026). The first three are the v1.20 slot colours, so the
/// migrated slots keep their look. WPF-free: hex strings, brushes are made
/// where they are drawn.
/// </summary>
public static class WaypointPalette
{
    public static readonly (string Name, string Hex)[] Colours =
    {
        ("blue", "#4FC3F7"), ("green", "#81C784"), ("purple", "#CE93D8"), ("orange", "#FFB74D"),
        ("red", "#EF5350"), ("yellow", "#FFF176"), ("teal", "#4DB6AC"), ("pink", "#F48FB1"),
        ("lime", "#C5E1A5"), ("sky", "#90CAF9"), ("white", "#ECEFF1"), ("tan", "#BCAAA4")
    };

    public static int Count => Colours.Length;

    /// <summary>Any int → a valid palette index (wraps, so cycling never falls off the end).</summary>
    public static int Wrap(int index) => (index % Count + Count) % Count;
}

/// <summary>
/// The user's waypoint library: up to 256 named, coloured places, kept in
/// waypoints.json next to config.json — user content, separate from runtime
/// state and the cookie vault, and the thing export/import will move around.
/// Every mutation validates (bounds, name, colour, capacity) and raises
/// Changed; the owner (MainWindow) saves on Changed, the minimap redraws.
/// Session state such as which waypoint is tracked lives in config, not
/// here. Fail-soft like OverlayConfig: nothing in this class throws.
/// </summary>
public sealed class WaypointLibrary
{
    public const int Capacity = 256;
    public const int MaxNameLength = 32;
    private const double MaxMeters = 50_000; // the island spans ~12.5 km; matches ShareCode

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string FilePath { get; } = Path.Combine(AppContext.BaseDirectory, "waypoints.json");

    private readonly List<Waypoint> _items = new();

    public IReadOnlyList<Waypoint> Items => _items;
    public int Count => _items.Count;
    public bool IsFull => _items.Count >= Capacity;

    /// <summary>Raised after any change to the list or an entry.</summary>
    public event Action? Changed;

    // ---- Persistence --------------------------------------------------------

    public static WaypointLibrary Load()
    {
        try
        {
            if (File.Exists(FilePath)) return FromJson(File.ReadAllText(FilePath));
        }
        catch
        {
            // Corrupt or unreadable: start empty rather than crash. The file
            // is only rewritten on the next change, so a hand fix stays possible.
        }
        return new WaypointLibrary();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath, ToJson());
        }
        catch
        {
            // Non-fatal: the in-memory library keeps working.
        }
    }

    /// <summary>Parses a library file, dropping anything invalid — the same gate an import will use.</summary>
    internal static WaypointLibrary FromJson(string json)
    {
        var library = new WaypointLibrary();
        var file = JsonSerializer.Deserialize<LibraryFile>(json);
        if (file?.Waypoints is null) return library;

        var seen = new HashSet<Guid>();
        foreach (var raw in file.Waypoints)
        {
            if (raw is null || !InBounds(raw.X, raw.Y)) continue;
            if (library.IsFull) break;
            var wp = new Waypoint
            {
                Id = raw.Id == Guid.Empty || !seen.Add(raw.Id) ? Guid.NewGuid() : raw.Id,
                Name = SanitizeName(raw.Name),
                X = raw.X,
                Y = raw.Y,
                Colour = WaypointPalette.Wrap(raw.Colour),
                Visible = raw.Visible,
                Pack = string.IsNullOrWhiteSpace(raw.Pack) ? null : SanitizeName(raw.Pack)
            };
            seen.Add(wp.Id);
            library._items.Add(wp);
        }
        return library;
    }

    internal string ToJson() =>
        JsonSerializer.Serialize(new LibraryFile { Format = 1, Waypoints = _items }, JsonOpts);

    private sealed class LibraryFile
    {
        public int Format { get; set; } = 1;
        public List<Waypoint>? Waypoints { get; set; }
    }

    // ---- Mutations (each validates, then raises Changed) ---------------------

    /// <summary>Adds a waypoint; null when the library is full or the position is not on the island.</summary>
    public Waypoint? Add(string name, double x, double y, int colour)
    {
        if (IsFull || !InBounds(x, y)) return null;
        var wp = new Waypoint { Name = SanitizeName(name), X = x, Y = y, Colour = WaypointPalette.Wrap(colour) };
        _items.Add(wp);
        Notify();
        return wp;
    }

    public bool Remove(Guid id)
    {
        var removed = _items.RemoveAll(w => w.Id == id) > 0;
        if (removed) Notify();
        return removed;
    }

    public void Clear()
    {
        if (_items.Count == 0) return;
        _items.Clear();
        Notify();
    }

    /// <summary>Replaces the whole list (the Settings page commits its draft this way).</summary>
    public void ReplaceWith(IEnumerable<Waypoint> items)
    {
        _items.Clear();
        foreach (var w in items)
        {
            if (IsFull) break;
            if (!InBounds(w.X, w.Y)) continue;
            w.Name = SanitizeName(w.Name);
            w.Colour = WaypointPalette.Wrap(w.Colour);
            _items.Add(w);
        }
        Notify();
    }

    /// <summary>Deep copy for a draft the Settings page can edit and discard.</summary>
    public List<Waypoint> Clone() => _items.Select(w => w.Clone()).ToList();

    public void Notify() => Changed?.Invoke();

    // ---- Queries -------------------------------------------------------------

    public Waypoint? Find(Guid? id) => id is { } g ? _items.FirstOrDefault(w => w.Id == g) : null;

    /// <summary>The nearest waypoint (among those passing the filter) to a world point, with its distance in metres.</summary>
    public (Waypoint Waypoint, double Meters)? Nearest(double x, double y, Func<Waypoint, bool>? filter = null)
    {
        (Waypoint, double)? best = null;
        foreach (var w in _items)
        {
            if (filter is not null && !filter(w)) continue;
            var meters = Distance(w.X, w.Y, x, y) / 100;
            if (best is null || meters < best.Value.Item2) best = (w, meters);
        }
        return best;
    }

    /// <summary>"Waypoint N", the first N not already in use.</summary>
    public string NextName()
    {
        var used = _items.Select(w => w.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var n = _items.Count + 1; ; n++)
        {
            var candidate = $"Waypoint {n}";
            if (!used.Contains(candidate)) return candidate;
        }
    }

    /// <summary>The palette colour a new waypoint gets: round-robin, so neighbours placed together differ.</summary>
    public int NextColour() => WaypointPalette.Wrap(_items.Count);

    /// <summary>Trims, strips control characters (tabs and newlines count as spaces), collapses whitespace, caps the length; empty becomes "Waypoint".</summary>
    public static string SanitizeName(string? raw)
    {
        var chars = (raw ?? "").Select(c => char.IsWhiteSpace(c) ? ' ' : c).Where(c => !char.IsControl(c)).ToArray();
        var collapsed = string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (collapsed.Length > MaxNameLength) collapsed = collapsed[..MaxNameLength].TrimEnd();
        return collapsed.Length == 0 ? "Waypoint" : collapsed;
    }

    public static bool InBounds(double xCm, double yCm) =>
        !double.IsNaN(xCm) && !double.IsNaN(yCm) &&
        Math.Abs(xCm) <= MaxMeters * 100 && Math.Abs(yCm) <= MaxMeters * 100;

    public static double Distance(double x1, double y1, double x2, double y2) =>
        Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
}

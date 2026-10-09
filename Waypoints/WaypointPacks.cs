using System.Text.Json;

namespace PandoraOverlay;

/// <summary>
/// Waypoint packs: the export/import file that lets players share libraries
/// (landmarks, sanctuaries, spawns…). A pack is the library's own JSON shape
/// plus a Name. Export keeps ids (so a re-import is recognised), clears the
/// per-entry pack tag and sets everything visible — the receiver's view is
/// their business. Import runs through WaypointLibrary's gate (bounds,
/// names, colours, cap), then merges into the target list: entries already
/// present by id, or by the same name within DuplicateMeters, are skipped;
/// the rest arrive HIDDEN and tagged with the pack name, so a 200-entry pack
/// can't bury anyone's map. Pure and tested; the Settings page owns the
/// file dialogs.
/// Hidden on arrival is the owner's decision: an import must never bury a
/// map — the player shows what they want, per pack or per waypoint.
/// </summary>
public static class WaypointPacks
{
    public const double DuplicateMeters = 20;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public sealed record Pack(string Name, IReadOnlyList<Waypoint> Waypoints);

    public sealed record MergeResult(int Added, int Duplicates, int Overflow);

    private sealed class PackFile
    {
        public int Format { get; set; } = 1;
        public string? Name { get; set; }
        public List<Waypoint>? Waypoints { get; set; }
    }

    /// <summary>The pack text for a set of waypoints, named after the file the user chose.</summary>
    public static string Export(IEnumerable<Waypoint> items, string name)
    {
        var file = new PackFile
        {
            Name = WaypointLibrary.SanitizeName(name),
            Waypoints = items.Select(w => new Waypoint
            {
                Id = w.Id, Name = w.Name, X = w.X, Y = w.Y, Colour = w.Colour, Visible = true, Pack = null
            }).ToList()
        };
        return JsonSerializer.Serialize(file, JsonOpts);
    }

    /// <summary>
    /// Reads a pack (or a plain library file) through the import gate; null
    /// when the text isn't one. A file without a Name takes the fallback —
    /// the file's base name, in practice.
    /// </summary>
    public static Pack? Parse(string json, string fallbackName)
    {
        PackFile? file;
        try
        {
            file = JsonSerializer.Deserialize<PackFile>(json);
        }
        catch
        {
            return null;
        }
        if (file?.Waypoints is null) return null;

        var name = WaypointLibrary.SanitizeName(string.IsNullOrWhiteSpace(file.Name) ? fallbackName : file.Name);
        return new Pack(name, WaypointLibrary.FromJson(json).Items);
    }

    /// <summary>
    /// Merges a pack into a list (the Settings page's draft): skips entries
    /// already there by id or by the same name within DuplicateMeters, stops
    /// at the library's capacity, and adds the rest hidden and tagged with
    /// the pack name.
    /// </summary>
    public static MergeResult Merge(List<Waypoint> target, Pack pack)
    {
        int added = 0, duplicates = 0, overflow = 0;
        foreach (var w in pack.Waypoints)
        {
            if (target.Any(t => t.Id == w.Id || IsSameSpot(t, w)))
            {
                duplicates++;
                continue;
            }
            if (target.Count >= WaypointLibrary.Capacity)
            {
                overflow++;
                continue;
            }
            target.Add(new Waypoint
            {
                Id = w.Id, Name = w.Name, X = w.X, Y = w.Y, Colour = w.Colour, Visible = false, Pack = pack.Name
            });
            added++;
        }
        return new MergeResult(added, duplicates, overflow);
    }

    private static bool IsSameSpot(Waypoint a, Waypoint b) =>
        string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase) &&
        WaypointLibrary.Distance(a.X, a.Y, b.X, b.Y) / 100 <= DuplicateMeters;
}

using System.Text.Json;

namespace PandoraOverlay;

/// <summary>The game's three kinds of zone (VulnonaMAP's sections). A mass migration is a Migration with Mass set.</summary>
public enum ZoneKind { Migration, Patrol, Sanctuary }

/// <summary>Which kinds a map shows: the three zone ticks of the map's Layers menu.</summary>
[Flags]
public enum ZoneKinds { None = 0, Migrations = 1, Patrols = 2, Sanctuaries = 4 }

/// <summary>A round zone in world cm: its centre and radius.</summary>
public sealed record ZoneCircle(double X, double Y, double R);

/// <summary>
/// One zone in world cm: a polygon (three or more points, either winding)
/// or a circle. What it is called in the map's readout is its Label;
/// never "active" — which zone is active is decided inside the game.
/// </summary>
public sealed class Zone
{
    private readonly double _minX, _minY, _maxX, _maxY; // the bounding box: most points are rejected by it alone

    public Zone(ZoneKind kind, bool mass, IReadOnlyList<(double X, double Y)>? polygon, ZoneCircle? circle)
    {
        Kind = kind;
        Mass = mass && kind == ZoneKind.Migration;
        Polygon = polygon;
        Circle = circle;
        if (circle is not null)
        {
            (_minX, _maxX, _minY, _maxY) = (circle.X - circle.R, circle.X + circle.R, circle.Y - circle.R, circle.Y + circle.R);
        }
        else if (polygon is { Count: > 0 })
        {
            (_minX, _maxX) = (polygon.Min(p => p.X), polygon.Max(p => p.X));
            (_minY, _maxY) = (polygon.Min(p => p.Y), polygon.Max(p => p.Y));
        }
    }

    public ZoneKind Kind { get; }

    /// <summary>A mass migration zone (VulnonaMAP's "mmz" tag): drawn with a dashed edge, named apart in the readout. Only a migration can be one.</summary>
    public bool Mass { get; }

    public IReadOnlyList<(double X, double Y)>? Polygon { get; }

    public ZoneCircle? Circle { get; }

    /// <summary>Its tick in the Layers menu.</summary>
    public ZoneKinds Flag => Kind switch
    {
        ZoneKind.Migration => ZoneKinds.Migrations,
        ZoneKind.Patrol => ZoneKinds.Patrols,
        _ => ZoneKinds.Sanctuaries,
    };

    /// <summary>What the map's readout calls it.</summary>
    public string Label => Kind switch
    {
        ZoneKind.Migration => Mass ? "Mass migration zone" : "Migration zone",
        ZoneKind.Patrol => "Patrol zone",
        _ => "Sanctuary",
    };

    /// <summary>Whether a world point (cm) lies inside: within the radius, or inside the polygon by the even-odd rule.</summary>
    public bool Contains(double x, double y)
    {
        if (x < _minX || x > _maxX || y < _minY || y > _maxY) return false;
        if (Circle is { } c) return (x - c.X) * (x - c.X) + (y - c.Y) * (y - c.Y) <= c.R * c.R;

        var p = Polygon!;
        var inside = false;
        for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
        {
            if ((p[i].Y > y) != (p[j].Y > y) &&
                x < (p[j].X - p[i].X) * (y - p[i].Y) / (p[j].Y - p[i].Y) + p[i].X)
            {
                inside = !inside;
            }
        }
        return inside;
    }
}

/// <summary>
/// The map's zone layers (Oct 2026, the owner's choice of source): where the
/// game's migration zones, patrol zones and sanctuaries CAN be, as shapes
/// in world cm, read from Assets/zones.json. That file is a dated copy of
/// VulnonaMAP's community data, converted by tools/zones/make-zones.ps1 and
/// never fetched; the website's own zone pictures are NOT the source (not
/// cleared for use, and older than the game's layout when checked). WHICH
/// zone is active is decided inside the game — per species for migrations,
/// per player or group for patrols — and nothing outside the game can know
/// it, so this is only the shapes, and nothing calls a zone active.
/// MAP-AGNOSTIC (owner's rule): the code knows three kinds and nothing about
/// where they are; a new game version or map is a new file and a converter
/// run. Pure and tested; the drawing is ZoneAsset's and MapCanvas's job.
/// </summary>
public sealed class ZoneSet
{
    private ZoneSet(IReadOnlyList<Zone> zones, string source, string dataset, string gameVersions, string copiedOn)
    {
        Zones = zones;
        Source = source;
        Dataset = dataset;
        GameVersions = gameVersions;
        CopiedOn = copiedOn;
    }

    public IReadOnlyList<Zone> Zones { get; }

    /// <summary>Where the shapes come from ("VulnonaMAP"), for the Layers menu's foot line.</summary>
    public string Source { get; }

    /// <summary>The source's own name for the data ("Gateway v0.21.772").</summary>
    public string Dataset { get; }

    /// <summary>The game versions the shapes were mapped for, as the source states them.</summary>
    public string GameVersions { get; }

    public string CopiedOn { get; }

    /// <summary>
    /// Reads zones.json. Tolerant: a zone of an unknown kind, a polygon under
    /// three points, a circle without a radius, a zone with both shapes or
    /// none is skipped. Never throws; a file with nothing usable is null.
    /// </summary>
    public static ZoneSet? Parse(string json)
    {
        FileDoc? doc;
        try
        {
            doc = JsonSerializer.Deserialize<FileDoc>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return null;
        }
        if (doc?.Zones is null) return null;

        var zones = new List<Zone>();
        foreach (var z in doc.Zones)
        {
            if (z is null || KindOf(z.Kind) is not { } kind) continue;
            var polygon = z.Polygon is { } raw && raw.All(p => p is { Length: 2 } && double.IsFinite(p[0]) && double.IsFinite(p[1]))
                ? raw.Select(p => (p![0], p[1])).ToList()
                : null;
            var circle = z.Circle is { R: > 0 } c && double.IsFinite(c.X) && double.IsFinite(c.Y) && double.IsFinite(c.R)
                ? new ZoneCircle(c.X, c.Y, c.R)
                : null;
            if (polygon is { Count: < 3 }) polygon = null;
            if ((polygon is null) == (circle is null)) continue; // one shape, exactly
            zones.Add(new Zone(kind, z.Mass, polygon, circle));
        }
        return zones.Count == 0
            ? null
            : new ZoneSet(zones, doc.Source?.Trim() ?? "", doc.Dataset?.Trim() ?? "", doc.GameVersions?.Trim() ?? "", doc.CopiedOn?.Trim() ?? "");
    }

    private static ZoneKind? KindOf(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "migration" => ZoneKind.Migration,
        "patrol" => ZoneKind.Patrol,
        "sanctuary" => ZoneKind.Sanctuary,
        _ => null,
    };

    /// <summary>The zones holding a world point (cm), in the file's order.</summary>
    public IReadOnlyList<Zone> At(double x, double y) => Zones.Where(z => z.Contains(x, y)).ToList();

    /// <summary>
    /// What the map's readout names for the zones under the cursor: the
    /// kinds that are shown, each name once (two overlapping patrol zones are
    /// one "Patrol zone"), patrol zones first, then migrations, then
    /// sanctuaries.
    /// </summary>
    public static IReadOnlyList<Zone> ForReadout(IEnumerable<Zone> zones, ZoneKinds shown) =>
        zones.Where(z => (shown & z.Flag) != 0)
             .OrderBy(z => z.Kind switch { ZoneKind.Patrol => 0, ZoneKind.Migration => 1, _ => 2 })
             .ThenBy(z => z.Mass ? 1 : 0)
             .DistinctBy(z => z.Label)
             .ToList();

    /// <summary>
    /// A polygon's signed area by the shoelace formula, in its own units:
    /// positive when it runs clockwise on screen (y down). The drawing turns
    /// every shape the same way round, so that zones of one kind that
    /// overlap fill as one and never cancel into a hole.
    /// </summary>
    public static double SignedArea(IReadOnlyList<(double X, double Y)> points)
    {
        var sum = 0.0;
        for (int i = 0, j = points.Count - 1; i < points.Count; j = i++) sum += points[j].X * points[i].Y - points[i].X * points[j].Y;
        return sum / 2;
    }

    // ---- The file's shape ---------------------------------------------------------

    private sealed record FileDoc(string? Source, string? Dataset, string? GameVersions, string? CopiedOn, List<ZoneDoc?>? Zones);

    private sealed record ZoneDoc(string? Kind, bool Mass, List<double[]?>? Polygon, CircleDoc? Circle);

    private sealed record CircleDoc(double X, double Y, double R);
}

using System.Globalization;
using System.Text.Json;

namespace PandoraOverlay;

/// <summary>One named area of the legend: its colour on the area map and its label point in world cm.</summary>
public sealed record AreaEntry(string Name, string Colour, double X, double Y);

/// <summary>
/// The area map's legend (Assets/areas.json): which colour is which area,
/// where the names came from and when. Parse is tolerant and pure.
/// </summary>
public sealed record AreaLegend(string Source, string CopiedOn, IReadOnlyList<AreaEntry> Areas)
{
    /// <summary>Entries without a name or a readable "#RRGGBB" colour are dropped; nothing usable is null.</summary>
    public static AreaLegend? Parse(string json)
    {
        AreaLegend? doc;
        try
        {
            doc = JsonSerializer.Deserialize<AreaLegend>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return null;
        }
        if (doc is null) return null;

        var areas = (doc.Areas ?? Array.Empty<AreaEntry>())
            .Where(a => a is not null && !string.IsNullOrWhiteSpace(a.Name) && AreaMap.TryParseColour(a.Colour, out _))
            .Select(a => a with { Name = a.Name.Trim() })
            .Take(AreaMap.MaxAreas)
            .ToList();
        return areas.Count == 0 ? null : new AreaLegend(doc.Source ?? "", doc.CopiedOn ?? "", areas);
    }
}

/// <summary>
/// "Which area am I in" (v1.29, the owner's idea): an image the size of the
/// island map with one flat colour per named area and nothing else, read
/// once into a grid. A position goes through the same world→map transform
/// as the arrow (MapCalibration.ToFraction), the pixel there names the
/// area. Purely local — your own position and a bundled image, no request.
/// The NAMES are VulnonaMAP's Gateway labels; the BORDERS are the
/// overlay's own (tools/area-map computes them from the 26 label points —
/// no source has borders, and none are drawn on the map), so they are a
/// judgement, not a fact, and are meant to be corrected by painting over
/// Assets/areas.png. Pure and tested; decoding the PNG is AreaMapAsset's job.
/// </summary>
public sealed class AreaMap
{
    public const int None = -1;
    public const int MaxAreas = 255; // one byte per pixel, 0 = no area

    private readonly byte[] _grid;

    private AreaMap(int size, byte[] grid, IReadOnlyList<AreaEntry> areas, int unknownPixels)
    {
        Size = size;
        _grid = grid;
        Areas = areas;
        UnknownPixels = unknownPixels;
    }

    /// <summary>Pixels per side.</summary>
    public int Size { get; }

    public IReadOnlyList<AreaEntry> Areas { get; }

    /// <summary>
    /// Pixels that are neither empty nor one of the legend's colours — a
    /// soft brush edge, a JPEG artefact, a colour picked by eye. They read
    /// as "no area" rather than as a wrong one; a clean map has none.
    /// </summary>
    public int UnknownPixels { get; }

    /// <summary>
    /// Builds the grid from 32-bit BGRA pixels. A fully transparent pixel is
    /// no area; a fully opaque one in a legend colour is that area; anything
    /// else counts as unknown. Null when the image isn't square or the
    /// buffer is short.
    /// </summary>
    public static AreaMap? FromPixels(int width, int height, byte[] bgra, AreaLegend legend)
    {
        if (width <= 0 || width != height || bgra.Length < width * height * 4) return null;

        var byColour = new Dictionary<int, byte>();
        for (var i = 0; i < legend.Areas.Count && i < MaxAreas; i++)
        {
            if (TryParseColour(legend.Areas[i].Colour, out var rgb)) byColour.TryAdd(rgb, (byte)(i + 1));
        }

        var grid = new byte[width * height];
        var unknown = 0;
        for (int p = 0, o = 0; p < grid.Length; p++, o += 4)
        {
            var alpha = bgra[o + 3];
            if (alpha == 0) continue;
            if (alpha == 255 && byColour.TryGetValue((bgra[o + 2] << 16) | (bgra[o + 1] << 8) | bgra[o], out var area)) grid[p] = area;
            else unknown++;
        }
        return new AreaMap(width, grid, legend.Areas, unknown);
    }

    /// <summary>"#RRGGBB" → 0xRRGGBB.</summary>
    public static bool TryParseColour(string? hex, out int rgb)
    {
        rgb = 0;
        return hex is { Length: 7 } && hex[0] == '#' &&
               int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb);
    }

    /// <summary>The area at a map fraction (0–1 across and down the map), or None.</summary>
    public int IndexAt(double fx, double fy)
    {
        if (double.IsNaN(fx) || double.IsNaN(fy)) return None;
        var x = Math.Clamp((int)(fx * Size), 0, Size - 1);
        var y = Math.Clamp((int)(fy * Size), 0, Size - 1);
        return _grid[y * Size + x] - 1;
    }

    public string? NameOf(int index) => index >= 0 && index < Areas.Count ? Areas[index].Name : null;

    public string? NameAt(double fx, double fy) => NameOf(IndexAt(fx, fy));
}

/// <summary>
/// The area you are in, without flicker. Borders are lines on a map, and a
/// dino walking along one would flip the readout with every poll, so a new
/// area is taken only once you are CLEARLY inside it: the same area at your
/// spot and at eight points 25 m around it. The first reading of a life is
/// taken as it is, and so is one after a jump (respawn, teleport) — when
/// the area shown is nowhere around you any more, there is no border to
/// wait for. "No area" (open sea) is a reading like any other. Pure, tested.
/// </summary>
public sealed class AreaReadout
{
    /// <summary>How far inside counts as clearly inside: 2 px of the 1000 px map, about 25 m.</summary>
    public const int MarginPixels = 2;

    private int _current = AreaMap.None;
    private bool _has;

    /// <summary>The settled area's index for a new position (AreaMap.None = no area).</summary>
    public int Update(AreaMap map, double fx, double fy)
    {
        var here = map.IndexAt(fx, fy);
        if (!_has)
        {
            _has = true;
            return _current = here;
        }
        if (here == _current) return _current;

        var margin = (double)MarginPixels / map.Size;
        var allHere = true;
        var currentNear = false;
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var around = map.IndexAt(fx + dx * margin, fy + dy * margin);
                if (around != here) allHere = false;
                if (around == _current) currentNear = true;
            }
        }
        if (allHere || !currentNear) _current = here;
        return _current;
    }

    /// <summary>A new life or not in game: the next reading starts afresh.</summary>
    public void Reset()
    {
        _has = false;
        _current = AreaMap.None;
    }
}

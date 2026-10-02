using System.Globalization;
using System.Text.Json;

namespace PandoraOverlay;

/// <summary>One named area of the legend: its colour on the area map and its label point in world cm.</summary>
public sealed record AreaEntry(string Name, string Colour, double X, double Y);

/// <summary>
/// The area map's legend (Assets/areas.json): which colour is which area,
/// where the names came from and when, and the map calibration the
/// generator placed the label points with (the overlay itself always uses
/// the live one; this is the record of what the map was built for). Parse
/// is tolerant and pure.
/// </summary>
public sealed record AreaLegend(string Source, string CopiedOn, IReadOnlyList<AreaEntry> Areas, MapCalibration? Calibration = null)
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
        var calibration = doc.Calibration is { MapSize: > 0 } c && c.ScaleX != 0 && c.ScaleY != 0 ? c : null;
        return areas.Count == 0 ? null : new AreaLegend(doc.Source ?? "", doc.CopiedOn ?? "", areas, calibration);
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

    private AreaMap(int size, byte[] grid, AreaLegend legend, int unknownPixels)
    {
        Size = size;
        _grid = grid;
        Legend = legend;
        UnknownPixels = unknownPixels;
    }

    /// <summary>Pixels per side.</summary>
    public int Size { get; }

    public AreaLegend Legend { get; }

    public IReadOnlyList<AreaEntry> Areas => Legend.Areas;

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
        return new AreaMap(width, grid, legend, unknown);
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

    /// <summary>
    /// Where the borders run, as one flag per pixel (row by row): a pixel
    /// whose right or lower neighbour belongs to ANOTHER AREA. An area's
    /// edge to nothing (open sea) is not a border — drawn, those edges were
    /// rings around the whole coast that said nothing and dominated the
    /// map. With a land mask (one flag per pixel, true = land) a border
    /// also needs LAND ON BOTH SIDES, so the lines end at the coast instead
    /// of running on between two areas' coastal water and stopping in the
    /// middle of the sea. That rule leaves an area that is an ISLAND OF ITS
    /// OWN with no line at all, so such an area gets its shoreline traced
    /// instead. What makes it one is its neighbours — none of its land
    /// touches another area's land — never its name or its size. Thickness
    /// widens the line in whole pixels (1–4), for views that show the map
    /// so small that a one-pixel line would vanish. For the minimap's
    /// optional border layer; the lookup never needs it.
    /// </summary>
    public bool[] BorderMask(int thickness, bool[]? land = null)
    {
        thickness = Math.Clamp(thickness, 1, 4);
        var n = Size;
        if (land is not null && land.Length != n * n) land = null; // a mask of another size says nothing about this map
        var thin = new bool[n * n];
        var hasLand = new bool[MaxAreas + 1];   // per grid value: the area owns land at all
        var bordered = new bool[MaxAreas + 1];  // … and some of it touches another area's land
        for (var y = 0; y < n; y++)
        {
            for (var x = 0; x < n; x++)
            {
                var i = y * n + x;
                var here = _grid[i];
                if (here == 0 || (land is not null && !land[i])) continue;
                hasLand[here] = true;
                var right = x < n - 1 && Other(i + 1);
                var below = y < n - 1 && Other(i + n);
                thin[i] = right || below;
                if (right) bordered[here] = bordered[_grid[i + 1]] = true;
                if (below) bordered[here] = bordered[_grid[i + n]] = true;

                bool Other(int j) => _grid[j] != 0 && _grid[j] != here && (land is null || land[j]);
            }
        }

        // An island of its own: its shore — its land pixels that have water beside them — is its outline.
        if (land is not null && Enumerable.Range(1, MaxAreas).Any(a => hasLand[a] && !bordered[a]))
        {
            for (var y = 0; y < n; y++)
            {
                for (var x = 0; x < n; x++)
                {
                    var i = y * n + x;
                    if (!land[i] || _grid[i] == 0 || bordered[_grid[i]]) continue;
                    thin[i] = (x > 0 && !land[i - 1]) || (x < n - 1 && !land[i + 1]) || (y > 0 && !land[i - n]) || (y < n - 1 && !land[i + n]);
                }
            }
        }
        if (thickness == 1) return thin;

        // Widen with a thickness × thickness square, one axis at a time.
        int before = (thickness - 1) / 2, after = thickness / 2;
        var wide = new bool[n * n];
        for (var y = 0; y < n; y++)
        {
            for (var x = 0; x < n; x++)
            {
                if (!thin[y * n + x]) continue;
                for (var k = Math.Max(0, x - before); k <= Math.Min(n - 1, x + after); k++) wide[y * n + k] = true;
            }
        }
        var mask = new bool[n * n];
        for (var y = 0; y < n; y++)
        {
            for (var x = 0; x < n; x++)
            {
                if (!wide[y * n + x]) continue;
                for (var k = Math.Max(0, y - before); k <= Math.Min(n - 1, y + after); k++) mask[k * n + x] = true;
            }
        }
        return mask;
    }

    /// <summary>The area of one pixel (None outside the map) — for checks that walk the grid.</summary>
    internal int IndexAtPixel(int x, int y) => x < 0 || y < 0 || x >= Size || y >= Size ? None : _grid[y * Size + x] - 1;
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
    /// <summary>How far inside counts as clearly inside, in pixels of the area map (about 25 m on a map of 12.5 m per pixel).</summary>
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

/// <summary>
/// Your area over time: the settled area for the minimap's pill, and the
/// moments worth an "Entered Highland" line in the Activity feed. One rule
/// for the lines: the feed names an area when it differs from the last one
/// it named and the last such line is at least half a minute old. So a
/// first crossing is announced at once; a step over the border and straight
/// back says nothing more; and if you stay on the other side, the feed
/// catches up when the half minute is over — it never ends on an area you
/// have left. Where a life begins is where it begins, not an entry (the
/// spawn line already has that moment), and open sea is never announced.
/// Pure, tested; MainWindow owns it, so the lines keep coming with the
/// minimap hidden.
/// </summary>
public sealed class AreaJournal
{
    public static readonly TimeSpan Quiet = TimeSpan.FromSeconds(30);

    private readonly AreaReadout _readout = new();
    private bool _started;
    private string? _named;   // the area the feed last named, or where this life began
    private DateTime _lineAt;

    /// <summary>The area you are settled in; null at open sea, off the map, or before the first position.</summary>
    public string? Current { get; private set; }

    /// <summary>A new position. Returns the area to announce, or null.</summary>
    public string? Update(AreaMap map, double fx, double fy, DateTime now)
    {
        Current = map.NameOf(_readout.Update(map, fx, fy));
        if (!_started)
        {
            _started = true;
            _named = Current;
            return null;
        }
        if (Current is null || Current == _named || now - _lineAt < Quiet) return null;
        _named = Current;
        _lineAt = now;
        return Current;
    }

    /// <summary>Not in game: nowhere, and the next life starts afresh.</summary>
    public void Reset()
    {
        _readout.Reset();
        _started = false;
        _named = null;
        _lineAt = default;
        Current = null;
    }
}

using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PandoraOverlay;

/// <summary>
/// The bundled area map: Assets/areas.png (the colours) and Assets/areas.json
/// (the legend), both embedded resources like rules.json, so the tests read
/// the very files that ship. This is the WPF-imaging half — it decodes the
/// PNG and hands the pixels to the pure AreaMap. Loaded once, on first use;
/// fail-soft: a missing or unreadable map just means no area readout.
/// </summary>
public static class AreaMapAsset
{
    public const string ImageResource = "areas.png";
    public const string LegendResource = "areas.json";

    private static readonly Lazy<AreaMap?> Bundled = new(LoadBundled);

    /// <summary>The app's one copy (a megabyte of grid): decoded the first time anything asks.</summary>
    public static AreaMap? Shared => Bundled.Value;

    public static AreaMap? LoadBundled()
    {
        try
        {
            var assembly = typeof(AreaMapAsset).Assembly;
            using var legendStream = assembly.GetManifestResourceStream(LegendResource);
            using var imageStream = assembly.GetManifestResourceStream(ImageResource);
            if (legendStream is null || imageStream is null) return null;
            using var reader = new StreamReader(legendStream);
            return AreaLegend.Parse(reader.ReadToEnd()) is { } legend ? Decode(imageStream, legend) : null;
        }
        catch
        {
            return null;
        }
    }

    private static readonly Lazy<IReadOnlyList<BorderLine>> Traced =
        new(() => Shared is { } map ? AreaBorders.Trace(map) : Array.Empty<BorderLine>());
    private static readonly Lazy<Geometry?> BorderLines = new(() => Shared is null ? null : Draw(Traced.Value));
    private static readonly Dictionary<int, Geometry> Outlines = new();

    /// <summary>
    /// The borders of the bundled map as LINES (AreaBorders), in map-pixel
    /// coordinates (0 … Size), for the minimap's border layer. Vector, not a
    /// picture: a picture of the borders is stretched with the map and went
    /// blocky at 5–6× zoom, a line stays sharp at any zoom and is drawn at a
    /// fixed screen width. Traced once, on first use, and frozen. Null when
    /// there is no map.
    /// </summary>
    public static Geometry? Borders => BorderLines.Value;

    /// <summary>
    /// ONE area's outline — the same traced lines, only those with that area
    /// on a side, so it lies exactly on the borders it highlights. Made on
    /// first use and kept. Null for "no area" or when there is no map.
    /// </summary>
    public static Geometry? OutlineOf(int area)
    {
        if (area < 0 || Shared is null) return null;
        if (!Outlines.TryGetValue(area, out var outline)) Outlines[area] = outline = Draw(Traced.Value.Where(l => l.Touches(area)));
        return outline;
    }

    private static Geometry Draw(IEnumerable<BorderLine> lines)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            foreach (var line in lines)
            {
                var points = line.Points;
                if (points.Count < 2) continue;
                context.BeginFigure(new System.Windows.Point(points[0].X, points[0].Y), isFilled: false, isClosed: false);
                for (var i = 1; i < points.Count; i++) context.LineTo(new System.Windows.Point(points[i].X, points[i].Y), isStroked: true, isSmoothJoin: true);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    /// <summary>
    /// Decodes an area image against a legend. The colours must arrive
    /// exactly as painted, so the colour profile is ignored and the pixels
    /// are read as straight (not premultiplied) BGRA.
    /// </summary>
    public static AreaMap? Decode(Stream image, AreaLegend legend)
    {
        try
        {
            var decoder = BitmapDecoder.Create(image,
                BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            BitmapSource frame = decoder.Frames[0];
            if (frame.Format != PixelFormats.Bgra32) frame = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var stride = frame.PixelWidth * 4;
            var pixels = new byte[stride * frame.PixelHeight];
            frame.CopyPixels(pixels, stride, 0);
            return AreaMap.FromPixels(frame.PixelWidth, frame.PixelHeight, pixels, legend);
        }
        catch
        {
            return null;
        }
    }
}

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

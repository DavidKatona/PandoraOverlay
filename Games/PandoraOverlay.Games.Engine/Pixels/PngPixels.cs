using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PandoraOverlay.Games.Engine;

/// <summary>
/// A PNG into a <see cref="PixelBuffer"/>: the one decode an atlas's sheet
/// goes through, and the one the games' frame tests read their golden
/// pictures with, so a picture checked in compares with what the engine would
/// load from it.
///
/// <para>WPF's own decoder (no package, A2; it reads the stream it is handed,
/// never a file, A29), every pixel read at once
/// (<see cref="BitmapCacheOption.OnLoad"/>, so nothing keeps a hold on the
/// caller's stream), any colour profile ignored (a sheet's colours are data,
/// G21: the same bytes on every PC). Whatever the PNG's own format (RGBA as the
/// art tool writes it, RGB, grey, a palette), WPF converts it to straight
/// 8-bit BGRA, and the engine premultiplies it ITSELF, by
/// <see cref="PixelBuffer.Colour"/>: so a sheet's pixel is exactly
/// <c>Colour(r, g, b, a)</c> of the PNG's bytes, by the engine's one rounding,
/// whatever rounding WIC's own Pbgra32 conversion would have used.</para>
/// </summary>
internal static class PngPixels
{
    /// <summary>Decodes the PNG's first (only) frame. Throws whatever WPF throws for a stream that isn't one.</summary>
    public static PixelBuffer Read(Stream png)
    {
        var decoder = new PngBitmapDecoder(
            png,
            BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
            BitmapCacheOption.OnLoad);
        BitmapSource image = decoder.Frames[0];
        if (image.Format != PixelFormats.Bgra32)
            image = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);

        var buffer = new PixelBuffer(image.PixelWidth, image.PixelHeight);
        uint[] pixels = buffer.Pixels;
        // Straight BGRA read as little-endian ints is 0xAARRGGBB, straight.
        image.CopyPixels(pixels, buffer.Width * 4, 0);
        for (int i = 0; i < pixels.Length; i++)
        {
            uint p = pixels[i];
            pixels[i] = PixelBuffer.Colour((byte)(p >> 16), (byte)(p >> 8), (byte)p, (byte)(p >> 24));
        }
        return buffer;
    }
}

namespace PandoraOverlay.Games.Engine;

/// <summary>
/// The picture a playfield is composed in (A17): our own pixels, so a game's
/// drawing can be checked exactly with no window and no display (a frame test
/// compares a buffer pixel for pixel), and WPF only ever shows the result.
///
/// <para><b>The pixels.</b> Premultiplied 32-bit colours, <c>0xAARRGGBB</c>, one
/// <see cref="uint"/> per pixel, row by row from the top-left,
/// <c>Width * Height</c> of them. Read as little-endian ints that is exactly
/// WPF's <c>Pbgra32</c> (the bytes B, G, R, A in memory), so the surface that
/// shows a buffer (A17) copies its rows into a bitmap without converting
/// anything. Premultiplied: each colour channel is already multiplied by the
/// alpha, so no channel is ever above its alpha and a fully transparent pixel
/// is 0. <see cref="Colour"/> makes such a value from straight bytes, and
/// every draw here keeps the rule (a blend of two premultiplied colours is
/// one). A value a game writes into <see cref="Pixels"/> by hand must keep it
/// too; <see cref="Clear"/> and <see cref="Fill"/> refuse a colour that breaks
/// it (a "red" written as <c>0x00FF0000</c> forgot its alpha and would blend
/// as nothing at all).</para>
///
/// <para><b>One blend, one rounding.</b> Every draw is source-over on the
/// premultiplied values, per channel <c>out = src + dst * (255 - srcA) / 255</c>.
/// With an opacity under 255 the source's four channels are first scaled by
/// <c>opacity / 255</c>. A fully opaque source at full opacity is a plain copy,
/// a fully transparent one is skipped. Every one of those divisions, and the
/// premultiplying in <see cref="Colour"/>, is the one private
/// <see cref="Mul"/>: <c>(c * f + 127) / 255</c>, which is c * f / 255
/// rounded to the nearest (it never falls on a half: 255 is odd). The frame
/// tests pin it, so a change to the rounding shows as a changed picture, never
/// as a quiet drift.</para>
///
/// <para><b>Clipping and defects.</b> Everything clips to the buffer silently:
/// a sprite half off the board draws its other half, one wholly off draws
/// nothing. What can only be a mistake in the code or the art throws instead,
/// because a test catches it at once and a player would otherwise see a wrong
/// picture: a size of zero or less, a source rectangle or a sprite's frame
/// that leaves its sheet, a colour that isn't premultiplied, a buffer drawn
/// onto itself, and drawing onto an atlas's sheet (it is shared by every view
/// for the life of the process: draw onto a buffer of your own).</para>
///
/// <para><b>Cost.</b> No allocation per draw: plain loops over the two arrays,
/// no <c>unsafe</c>, no <c>Marshal</c> (A29). A board is composed this way 30
/// times a second while it plays; the surface's own measurement says what that
/// costs.</para>
/// </summary>
public sealed class PixelBuffer
{
    /// <summary>A transparent buffer of this size. Each side is at least 1.</summary>
    public PixelBuffer(int width, int height)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), width, "A pixel buffer is at least one pixel wide.");
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), height, "A pixel buffer is at least one pixel high.");
        if ((long)width * height > Array.MaxLength)
            throw new ArgumentOutOfRangeException(nameof(height), height, $"A pixel buffer of {width} x {height} is more pixels than one array holds.");
        Width = width;
        Height = height;
        Pixels = new uint[width * height];
    }

    /// <summary>Its width in pixels.</summary>
    public int Width { get; }

    /// <summary>Its height in pixels.</summary>
    public int Height { get; }

    /// <summary>The pixels, premultiplied <c>0xAARRGGBB</c>, row by row: pixel (x, y) is <c>Pixels[y * Width + x]</c>.</summary>
    public uint[] Pixels { get; }

    /// <summary>
    /// True for an atlas's sheet once it is loaded: it is drawn from, never
    /// onto. Internal on purpose: only the atlas sets it, and a game has no
    /// reason to (its own buffers are its own).
    /// </summary>
    internal bool Frozen { get; private set; }

    /// <summary>Makes the buffer read-only for every draw (the atlas's sheet, once loaded).</summary>
    internal void Freeze() => Frozen = true;

    /// <summary>
    /// A premultiplied colour from straight bytes: each colour channel is
    /// multiplied by the alpha, rounded to the nearest. Opaque colours are
    /// unchanged; a fully transparent one is 0 whatever its colour.
    /// </summary>
    public static uint Colour(byte r, byte g, byte b, byte a = 255) =>
        (uint)a << 24 | Mul(r, a) << 16 | Mul(g, a) << 8 | Mul(b, a);

    /// <summary>Every pixel set to this premultiplied colour, transparent included: nothing is blended.</summary>
    public void Clear(uint colour)
    {
        ThrowIfFrozen();
        CheckPremultiplied(colour, nameof(colour));
        Array.Fill(Pixels, colour);
    }

    /// <summary>A premultiplied colour blended over a rectangle, clipped to the buffer.</summary>
    public void Fill(int x, int y, int width, int height, uint colour)
    {
        ThrowIfFrozen();
        CheckSize(width, height, "fill");
        CheckPremultiplied(colour, nameof(colour));
        if (!Clip(x, y, width, height, out int x0, out int x1, out int y0, out int y1)) return;
        uint alpha = colour >> 24;
        if (alpha == 0) return; // premultiplied and transparent: it adds nothing
        for (int row = y0; row < y1; row++)
        {
            int start = row * Width;
            if (alpha == 255)
            {
                Array.Fill(Pixels, colour, start + x0, x1 - x0);
                continue;
            }
            for (int i = start + x0; i < start + x1; i++)
                Pixels[i] = Over(colour, Pixels[i]);
        }
    }

    /// <summary>
    /// A sprite drawn with its pivot on (x, y): the frame's top-left goes to
    /// <c>(x - PivotX, y - PivotY)</c>. Flipped, the frame is mirrored left to
    /// right and its top-left goes to <c>(x - (Frame.Width - 1 - PivotX), y - PivotY)</c>,
    /// so the pivot pixel lands on the same point either way (a flipped dino
    /// keeps its feet where they were). An opacity under 255 fades the whole
    /// sprite. Clipped to the buffer.
    /// </summary>
    public void Draw(Sprite sprite, int x, int y, bool flip = false, byte opacity = 255)
    {
        ArgumentNullException.ThrowIfNull(sprite);
        if (sprite.Sheet is null)
            throw new ArgumentException($"The sprite '{sprite.Name}' has no sheet.", nameof(sprite));
        ThrowIfFrozen();
        if (ReferenceEquals(sprite.Sheet, this))
            throw new ArgumentException($"The sprite '{sprite.Name}' is on this buffer: a buffer can't be drawn onto itself.", nameof(sprite));
        var frame = sprite.Frame;
        CheckInside(sprite.Sheet, frame, sprite.Name, nameof(sprite));
        long left = flip ? (long)x - ((long)frame.Width - 1 - sprite.PivotX) : (long)x - sprite.PivotX;
        long top = (long)y - sprite.PivotY;
        Blit(sprite.Sheet, frame, left, top, flip, opacity);
    }

    /// <summary>
    /// The part <paramref name="from"/> of another buffer drawn with its
    /// top-left on (x, y); flipped, mirrored left to right in the same place.
    /// An opacity under 255 fades it. Clipped to this buffer.
    /// </summary>
    public void Draw(PixelBuffer source, PixelRect from, int x, int y, bool flip = false, byte opacity = 255)
    {
        ArgumentNullException.ThrowIfNull(source);
        ThrowIfFrozen();
        if (ReferenceEquals(source, this))
            throw new ArgumentException("A buffer can't be drawn onto itself.", nameof(source));
        CheckInside(source, from, spriteName: null, nameof(from));
        Blit(source, from, x, y, flip, opacity);
    }

    /// <summary>
    /// The one draw loop under both Draws: the source rectangle (already
    /// checked to lie inside its sheet) with its top-left on (left, top),
    /// which may lie anywhere, even beyond an int after a pivot is taken off,
    /// hence the longs; the loop itself runs on the clipped, in-range part.
    /// </summary>
    private void Blit(PixelBuffer source, PixelRect from, long left, long top, bool flip, byte opacity)
    {
        if (opacity == 0) return; // faded to nothing
        if (!Clip(left, top, from.Width, from.Height, out int x0, out int x1, out int y0, out int y1)) return;
        uint[] src = source.Pixels;
        uint[] dst = Pixels;
        for (int y = y0; y < y1; y++)
        {
            int sourceRow = (from.Y + (int)(y - top)) * source.Width;
            int targetRow = y * Width;
            for (int x = x0; x < x1; x++)
            {
                int offset = (int)(x - left); // 0 .. from.Width - 1
                int sourceX = from.X + (flip ? from.Width - 1 - offset : offset);
                uint colour = src[sourceRow + sourceX];
                if (opacity != 255) colour = Fade(colour, opacity);
                dst[targetRow + x] = Over(colour, dst[targetRow + x]);
            }
        }
    }

    /// <summary>
    /// Source-over on premultiplied values: per channel src + dst * (255 - srcA) / 255,
    /// through <see cref="Mul"/>. A sum never passes 255 (src is at most its
    /// alpha, and dst * (255 - srcA) / 255 at most 255 - srcA), so no channel
    /// spills into its neighbour, and the result is premultiplied again.
    /// </summary>
    private static uint Over(uint source, uint target)
    {
        uint alpha = source >> 24;
        if (alpha == 255) return source; // opaque: a plain copy
        if (alpha == 0) return target;   // transparent: skipped
        uint keep = 255 - alpha;
        return (alpha + Mul(target >> 24, keep)) << 24
             | (((source >> 16) & 0xFF) + Mul((target >> 16) & 0xFF, keep)) << 16
             | (((source >> 8) & 0xFF) + Mul((target >> 8) & 0xFF, keep)) << 8
             | ((source & 0xFF) + Mul(target & 0xFF, keep));
    }

    /// <summary>All four premultiplied channels scaled by opacity / 255: the colour, faded, still premultiplied.</summary>
    private static uint Fade(uint colour, uint opacity) =>
        Mul(colour >> 24, opacity) << 24
        | Mul((colour >> 16) & 0xFF, opacity) << 16
        | Mul((colour >> 8) & 0xFF, opacity) << 8
        | Mul(colour & 0xFF, opacity);

    /// <summary>
    /// THE rounding: c * f / 255 to the nearest whole number, for c and f from
    /// 0 to 255. Every premultiply, fade and blend goes through it, so there
    /// is one rule to pin and one to change.
    /// </summary>
    private static uint Mul(uint c, uint f) => (c * f + 127) / 255;

    /// <summary>
    /// The part of a rectangle at (left, top) that lies on the buffer, as
    /// half-open ranges [x0, x1) and [y0, y1). False when nothing does.
    /// </summary>
    private bool Clip(long left, long top, int width, int height, out int x0, out int x1, out int y0, out int y1)
    {
        x0 = (int)Math.Clamp(left, 0, Width);
        x1 = (int)Math.Clamp(left + width, 0, Width);
        y0 = (int)Math.Clamp(top, 0, Height);
        y1 = (int)Math.Clamp(top + height, 0, Height);
        return x0 < x1 && y0 < y1;
    }

    private static void CheckSize(int width, int height, string what)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(width <= 0 ? nameof(width) : nameof(height), $"A {what} of {width} x {height}: each side is at least 1.");
    }

    /// <summary>
    /// A rectangle that must lie wholly on its sheet: anything else is a build
    /// defect, and throws, naming the sprite when there is one. The message is
    /// built only when it throws: a check that passes allocates nothing.
    /// </summary>
    private static void CheckInside(PixelBuffer sheet, PixelRect rect, string? spriteName, string parameter)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            throw new ArgumentException($"{What(spriteName)} is {rect.Width} x {rect.Height}: each side is at least 1.", parameter);
        if (rect.X < 0 || rect.Y < 0 || rect.X > sheet.Width - rect.Width || rect.Y > sheet.Height - rect.Height)
            throw new ArgumentException(
                $"{What(spriteName)} ({rect.X}, {rect.Y}, {rect.Width} x {rect.Height}) leaves its {sheet.Width} x {sheet.Height} sheet.", parameter);

        static string What(string? spriteName) => spriteName is null ? "The rectangle" : $"The sprite '{spriteName}'";
    }

    private static void CheckPremultiplied(uint colour, string parameter)
    {
        uint alpha = colour >> 24;
        if (((colour >> 16) & 0xFF) > alpha || ((colour >> 8) & 0xFF) > alpha || (colour & 0xFF) > alpha)
            throw new ArgumentException(
                $"0x{colour:X8} is not a premultiplied colour: a channel is above its alpha. Make colours with PixelBuffer.Colour.", parameter);
    }

    private void ThrowIfFrozen()
    {
        if (Frozen)
            throw new InvalidOperationException(
                "This buffer is an atlas's sheet, shared by every view for the life of the process: draw onto a buffer of your own.");
    }
}

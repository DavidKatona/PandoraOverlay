using PandoraOverlay.Games.Engine;

namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// The board's data colours: what the painter fills a stand-in with while a piece
/// of art is missing (H30), and the ground every frame starts from. Data art, the
/// same in every theme, like the map's data colours (G21): a theme never reaches
/// the playfield, so none of these is a theme token.
///
/// <para>Every colour is made with <see cref="PixelBuffer.Colour"/>, premultiplied
/// (A39), so it can be handed to a fill as it is. They live here, beside
/// <c>Views/</c> and not in it, because the architecture's scan holds a game's
/// views to no literal colour at all: every colour the painter uses is a name from
/// this class. The values are the ones the art is drawn in (H22: gold yours, red a
/// hunter's, a heavier red the apex's), so a stand-in and the piece that replaces
/// it read alike.</para>
/// </summary>
internal static class HatchlingPalette
{
    /// <summary>Under everything: the whole buffer starts in it.</summary>
    public static readonly uint Ground = PixelBuffer.Colour(0x0B, 0x0F, 0x13);

    /// <summary>A grass tile's stand-in.</summary>
    public static readonly uint Grass = PixelBuffer.Colour(0x3F, 0x62, 0x37);

    /// <summary>A forest tile's stand-in.</summary>
    public static readonly uint Forest = PixelBuffer.Colour(0x24, 0x50, 0x2F);

    /// <summary>A water tile's stand-in.</summary>
    public static readonly uint Water = PixelBuffer.Colour(0x1E, 0x5F, 0x82);

    /// <summary>A rock tile's stand-in.</summary>
    public static readonly uint Rock = PixelBuffer.Colour(0x50, 0x56, 0x5C);

    /// <summary>A cell never seen.</summary>
    public static readonly uint Unseen = PixelBuffer.Colour(0x11, 0x16, 0x1C);

    /// <summary>Laid over a seen tile that is out of sight: the ground's colour, translucent.</summary>
    public static readonly uint Dim = PixelBuffer.Colour(0x0B, 0x0F, 0x13, 184);

    /// <summary>The rim of a cell you can move to.</summary>
    public static readonly uint MoveRim = PixelBuffer.Colour(0xFF, 0xC8, 0x64);

    /// <summary>The rim of a cell whose creature you can attack.</summary>
    public static readonly uint AttackRim = PixelBuffer.Colour(0xFF, 0x8A, 0x80);

    /// <summary>The rim of the cell under the mouse.</summary>
    public static readonly uint HoverRim = PixelBuffer.Colour(0xEC, 0xF2, 0xF8);

    /// <summary>Your ring, and the marks above you (H22: gold is yours).</summary>
    public static readonly uint YouRing = PixelBuffer.Colour(0xFF, 0xC8, 0x64);

    /// <summary>A hunter's ring, and the marks above it (H22).</summary>
    public static readonly uint HostileRing = PixelBuffer.Colour(0xFF, 0x6A, 0x5E);

    /// <summary>The apex's ring, and the marks above it (H22: a heavier red).</summary>
    public static readonly uint ApexRing = PixelBuffer.Colour(0xFF, 0x3B, 0x30);

    /// <summary>The ring of the one acting in a playback.</summary>
    public static readonly uint ActingRing = PixelBuffer.Colour(0xFF, 0xFF, 0xFF);

    /// <summary>A plant's stand-in.</summary>
    public static readonly uint Plant = PixelBuffer.Colour(0x9B, 0xE0, 0xA5);

    /// <summary>A carcass's stand-in.</summary>
    public static readonly uint Meat = PixelBuffer.Colour(0xE8, 0xA8, 0x7C);

    /// <summary>Small game's stand-in.</summary>
    public static readonly uint SmallGame = PixelBuffer.Colour(0xFF, 0xB7, 0x4D);

    /// <summary>
    /// A premultiplied colour of this palette faded by <paramref name="opacity"/>,
    /// still premultiplied: what a stand-in is filled with when the sprite it belongs
    /// to fades (an arrival, a defeat). All four channels are scaled by
    /// opacity / 255 to the nearest, the rounding <see cref="PixelBuffer"/> fades a
    /// sprite with, so a faded stand-in and a faded sprite of the same colour are
    /// the same pixels. At 255 it is the colour itself.
    /// </summary>
    public static uint Faded(uint colour, byte opacity)
    {
        if (opacity == 255) return colour;
        return Scale(colour >> 24, opacity) << 24
             | Scale((colour >> 16) & 0xFF, opacity) << 16
             | Scale((colour >> 8) & 0xFF, opacity) << 8
             | Scale(colour & 0xFF, opacity);

        static uint Scale(uint channel, uint factor) => (channel * factor + 127) / 255;
    }
}

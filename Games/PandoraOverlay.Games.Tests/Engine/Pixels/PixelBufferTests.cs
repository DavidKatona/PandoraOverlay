using PandoraOverlay.Games.Engine;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The buffer's drawing, as exact arrays worked out by hand: the premultiply,
/// the one blend and its rounding, opacity, clipping on every edge, the flip
/// about the pivot, the defects that throw, and the empty rectangles that
/// draw nothing (A39). Every expected value here was
/// computed on paper from <c>(c * f + 127) / 255</c>, never by calling the
/// code under test, so a change to the rounding fails here first.
/// </summary>
public class PixelBufferTests
{
    // Opaque colours for the copy and clipping tests: one value per source pixel.
    private const uint K = 0xFF000000; // opaque black, the ground
    private const uint P1 = 0xFF000011, P2 = 0xFF000022, P3 = 0xFF000033,
                       P4 = 0xFF000044, P5 = 0xFF000055, P6 = 0xFF000066,
                       P7 = 0xFF000077, P8 = 0xFF000088, P9 = 0xFF000099;

    #region The buffer and Colour

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 5)]
    [InlineData(5, -3)]
    public void ZeroOrNegativeSizesThrow(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelBuffer(width, height));
    }

    [Fact]
    public void ANewBufferIsTransparentRowByRow()
    {
        var buffer = new PixelBuffer(3, 2);

        Assert.Equal(3, buffer.Width);
        Assert.Equal(2, buffer.Height);
        Assert.Equal(new uint[6], buffer.Pixels);
    }

    /// <summary>Straight bytes in, premultiplied out: each channel times alpha / 255, to the nearest.</summary>
    [Theory]
    [InlineData(255, 0, 0, 255, 0xFFFF0000u)]
    [InlineData(200, 60, 40, 255, 0xFFC83C28u)]   // opaque: unchanged
    [InlineData(40, 120, 255, 128, 0x80143C80u)]  // 20.08, 60.24, 128.0
    [InlineData(255, 255, 255, 128, 0x80808080u)]
    [InlineData(200, 100, 50, 51, 0x3328140Au)]   // 40.0, 20.0, 10.0
    [InlineData(9, 9, 9, 0, 0x00000000u)]         // transparent is 0, whatever its colour
    [InlineData(255, 128, 1, 1, 0x01010100u)]     // 1.0, 0.50 rounds up, 0.004 rounds down
    [InlineData(191, 0, 0, 2, 0x02010000u)]       // 1.498: down
    [InlineData(192, 0, 0, 2, 0x02020000u)]       // 1.506: up (truncating would give 1)
    public void ColourPremultipliesToTheNearest(byte r, byte g, byte b, byte a, uint expected)
    {
        Assert.Equal(expected, PixelBuffer.Colour(r, g, b, a));
    }

    [Fact]
    public void ColourIsOpaqueUnlessToldOtherwise()
    {
        Assert.Equal(0xFF112233u, PixelBuffer.Colour(0x11, 0x22, 0x33));
    }

    [Fact]
    public void ClearSetsEveryPixelTransparentIncluded()
    {
        var buffer = new PixelBuffer(2, 2);
        buffer.Clear(0x80143C80);
        Assert.Equal(new uint[] { 0x80143C80, 0x80143C80, 0x80143C80, 0x80143C80 }, buffer.Pixels);

        buffer.Clear(0);
        Assert.Equal(new uint[4], buffer.Pixels);
    }

    /// <summary>A "red" without its alpha is a transparent pixel with colour in it: refused, it would blend as nothing.</summary>
    [Theory]
    [InlineData(0x00FF0000u)]
    [InlineData(0x80FF0000u)] // straight half red, not premultiplied
    [InlineData(0x10000011u)]
    public void AColourThatIsNotPremultipliedIsRefused(uint colour)
    {
        var buffer = new PixelBuffer(2, 2);

        Assert.Throws<ArgumentException>(() => buffer.Clear(colour));
        Assert.Throws<ArgumentException>(() => buffer.Fill(0, 0, 1, 1, colour));
        Assert.Equal(new uint[4], buffer.Pixels);
    }

    #endregion

    #region Copying and blending

    [Fact]
    public void AnOpaqueSourceIsAPlainCopy()
    {
        var source = Buffer(2, 2, P1, P2, P3, P4);
        var target = new PixelBuffer(4, 3);
        target.Clear(K);

        target.Draw(source, new PixelRect(0, 0, 2, 2), 1, 1);

        Assert.Equal(new[]
        {
            K, K,  K,  K,
            K, P1, P2, K,
            K, P3, P4, K,
        }, target.Pixels);
    }

    /// <summary>
    /// Source-over on premultiplied values, out = src + dst * (255 - srcA) / 255:
    /// the glass (0x80143C80, keep 127) over opaque (32, 64, 96) is
    /// 20 + 16.4, 60 + 32.4, 128 + 48.3, alpha 128 + 127.5.
    /// </summary>
    [Fact]
    public void AHalfTransparentPixelOverAColour()
    {
        var target = new PixelBuffer(2, 1);
        target.Clear(0xFF204060);

        target.Fill(0, 0, 1, 1, 0x80143C80);

        Assert.Equal(new uint[] { 0xFF245CB0, 0xFF204060 }, target.Pixels);
    }

    /// <summary>Half red over half blue: alpha 128 + 64.3, red 128, blue 0 + 64.3.</summary>
    [Fact]
    public void AHalfTransparentPixelOverAnotherOne()
    {
        var target = new PixelBuffer(1, 1);
        target.Clear(0x80000080);

        target.Draw(Buffer(1, 1, 0x80800000), new PixelRect(0, 0, 1, 1), 0, 0);

        Assert.Equal(new uint[] { 0xC0800040 }, target.Pixels);
    }

    /// <summary>
    /// The rounding pinned where it matters: half black over (2, 2, 2) keeps
    /// 2 * 127 / 255 = 0.996 of each channel, which rounds to 1. Truncating
    /// would give 0, and every blended edge of every sprite would darken.
    /// </summary>
    [Fact]
    public void TheBlendRoundsToTheNearest()
    {
        var target = new PixelBuffer(1, 1);
        target.Clear(0xFF020202);

        target.Fill(0, 0, 1, 1, 0x80000000);

        Assert.Equal(new uint[] { 0xFF010101 }, target.Pixels);
    }

    [Fact]
    public void ATransparentSourceIsSkipped()
    {
        var target = new PixelBuffer(2, 1);
        target.Clear(0x80143C80);

        target.Draw(Buffer(2, 1, 0, 0), new PixelRect(0, 0, 2, 1), 0, 0);
        target.Fill(0, 0, 2, 1, 0);

        Assert.Equal(new uint[] { 0x80143C80, 0x80143C80 }, target.Pixels);
    }

    /// <summary>
    /// Opacity scales the source's four channels first: (255, 200, 100, 50)
    /// at 128 is (128, 100.4, 50.2, 25.1), then over black.
    /// </summary>
    [Fact]
    public void OpacityFadesTheSourceBeforeTheBlend()
    {
        var target = new PixelBuffer(1, 1);
        target.Clear(K);

        target.Draw(Buffer(1, 1, 0xFFC86432), new PixelRect(0, 0, 1, 1), 0, 0, opacity: 128);

        Assert.Equal(new uint[] { 0xFF643219 }, target.Pixels);
    }

    /// <summary>The glass at opacity 128 is (64.3, 10.0, 30.1, 64.3), still premultiplied, alone on a transparent ground.</summary>
    [Fact]
    public void OpacityOnAHalfTransparentSource()
    {
        var target = new PixelBuffer(1, 1);

        target.Draw(Buffer(1, 1, 0x80143C80), new PixelRect(0, 0, 1, 1), 0, 0, opacity: 128);

        Assert.Equal(new uint[] { 0x400A1E40 }, target.Pixels);
    }

    [Fact]
    public void FullOpacityIsNoFadeAndNoneDrawsNothing()
    {
        var source = Buffer(1, 1, 0x80143C80);
        var faded = new PixelBuffer(1, 1);
        var plain = new PixelBuffer(1, 1);
        var none = new PixelBuffer(1, 1);
        none.Clear(K);

        faded.Draw(source, new PixelRect(0, 0, 1, 1), 0, 0, opacity: 255);
        plain.Draw(source, new PixelRect(0, 0, 1, 1), 0, 0);
        none.Draw(source, new PixelRect(0, 0, 1, 1), 0, 0, opacity: 0);

        Assert.Equal(plain.Pixels, faded.Pixels);
        Assert.Equal(new uint[] { K }, none.Pixels);
    }

    /// <summary>A part of a bigger sheet: only the rectangle is drawn, its top-left on (x, y).</summary>
    [Fact]
    public void APartOfASheetIsDrawnFromItsTopLeft()
    {
        var sheet = Buffer(3, 3, P1, P2, P3, P4, P5, P6, P7, P8, P9);
        var target = new PixelBuffer(3, 2);

        target.Draw(sheet, new PixelRect(1, 1, 2, 2), 1, 0);

        Assert.Equal(new uint[]
        {
            0, P5, P6,
            0, P8, P9,
        }, target.Pixels);
    }

    #endregion

    #region Clipping

    /// <summary>The 3 x 3 source P1..P9 at each corner of a 4 x 4 target: only the part on the buffer is drawn.</summary>
    [Theory]
    [InlineData(-1, -1, new uint[] { P5, P6, 0, 0, P8, P9, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 })]   // off the left and top
    [InlineData(2, 2, new uint[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, P1, P2, 0, 0, P4, P5 })]     // off the right and bottom
    [InlineData(2, -2, new uint[] { 0, 0, P7, P8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 })]     // off the right and top
    [InlineData(-2, 3, new uint[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, P3, 0, 0, 0 })]      // off the left and bottom
    public void ADrawClipsOnEveryEdge(int x, int y, uint[] expected)
    {
        var target = new PixelBuffer(4, 4);

        target.Draw(Nine(), new PixelRect(0, 0, 3, 3), x, y);

        Assert.Equal(expected, target.Pixels);
    }

    [Theory]
    [InlineData(4, 0)]
    [InlineData(-3, 0)]
    [InlineData(0, 4)]
    [InlineData(0, -3)]
    [InlineData(int.MaxValue, int.MaxValue)]
    [InlineData(int.MinValue, int.MinValue)]
    public void ADrawWhollyOutsideChangesNothing(int x, int y)
    {
        var target = new PixelBuffer(4, 4);
        target.Clear(K);
        var sprite = new Sprite("nine", Nine(), new PixelRect(0, 0, 3, 3), 0, 0); // placed as the rectangle is

        target.Draw(Nine(), new PixelRect(0, 0, 3, 3), x, y);
        target.Draw(sprite, x, y);
        target.Fill(x, y, 3, 3, P1);

        Assert.All(target.Pixels, p => Assert.Equal(K, p));
    }

    /// <summary>
    /// Taking the pivot off a point at the very edge of an int, either way
    /// round, still lands off the buffer: the placement is worked out past an
    /// int's range, never wrapped round onto the board.
    /// </summary>
    [Fact]
    public void APivotTakenOffTheEdgeOfAnIntDoesNotWrap()
    {
        var target = new PixelBuffer(4, 4);
        var pivoted = new Sprite("pivoted", Nine(), new PixelRect(0, 0, 3, 3), 2, 2);
        var corner = new Sprite("corner", Nine(), new PixelRect(0, 0, 3, 3), 0, 0);

        target.Draw(pivoted, int.MinValue, int.MinValue);            // top-left at MinValue - 2
        target.Draw(corner, int.MinValue, int.MinValue, flip: true); // top-left at MinValue - 2 too
        target.Draw(corner, int.MaxValue, int.MaxValue, flip: true);

        Assert.Equal(new uint[16], target.Pixels);
    }

    [Fact]
    public void AFillClipsToo()
    {
        const uint W = 0xFFFFFFFF;
        var target = new PixelBuffer(4, 3);
        target.Clear(K);

        target.Fill(-1, 1, 3, 5, W);

        Assert.Equal(new[]
        {
            K, K, K, K,
            W, W, K, K,
            W, W, K, K,
        }, target.Pixels);
    }

    /// <summary>A half fill blends every pixel it covers, by the same rule as a draw.</summary>
    [Fact]
    public void AHalfFillBlendsEveryPixel()
    {
        var target = new PixelBuffer(3, 2);
        target.Clear(0xFF020202);

        target.Fill(0, 0, 3, 2, 0x80000000);

        Assert.All(target.Pixels, p => Assert.Equal(0xFF010101u, p));
    }

    #endregion

    #region Sprites and the flip

    /// <summary>
    /// An asymmetric 3 x 2 frame (A B C over D E F) with its pivot on D, its
    /// bottom-left: drawn at (2, 2), D lands on (2, 2) both ways, the frame
    /// to its right, and mirrored to its left when flipped.
    /// </summary>
    [Fact]
    public void ASpriteIsPlacedByItsPivotAndFlipsAboutIt()
    {
        const uint Z = 0xFFFFFFFF, A = 0xFF0000A0, B = 0xFF0000B0, C = 0xFF0000C0, D = 0xFF0000D0, E = 0xFF0000E0, F = 0xFF0000F0;
        var sheet = Buffer(5, 2, Z, A, B, C, Z, Z, D, E, F, Z);
        var sprite = new Sprite("asymmetric", sheet, new PixelRect(1, 0, 3, 2), PivotX: 0, PivotY: 1);
        var plain = new PixelBuffer(6, 4);
        var flipped = new PixelBuffer(6, 4);

        plain.Draw(sprite, 2, 2);
        flipped.Draw(sprite, 2, 2, flip: true);

        Assert.Equal(new uint[]
        {
            0, 0, 0, 0, 0, 0,
            0, 0, A, B, C, 0,
            0, 0, D, E, F, 0,
            0, 0, 0, 0, 0, 0,
        }, plain.Pixels);
        Assert.Equal(new uint[]
        {
            0, 0, 0, 0, 0, 0,
            C, B, A, 0, 0, 0,
            F, E, D, 0, 0, 0,
            0, 0, 0, 0, 0, 0,
        }, flipped.Pixels);
    }

    /// <summary>A centred pivot: the frame's top-left is (x - PivotX, y - PivotY), mirrored about the same column when flipped.</summary>
    [Fact]
    public void ACentredPivotKeepsItsColumnWhenFlipped()
    {
        var sprite = new Sprite("row", Buffer(3, 1, P1, P2, P3), new PixelRect(0, 0, 3, 1), PivotX: 1, PivotY: 0);
        var plain = new PixelBuffer(5, 1);
        var flipped = new PixelBuffer(5, 1);

        plain.Draw(sprite, 2, 0);
        flipped.Draw(sprite, 2, 0, flip: true);

        Assert.Equal(new uint[] { 0, P1, P2, P3, 0 }, plain.Pixels);
        Assert.Equal(new uint[] { 0, P3, P2, P1, 0 }, flipped.Pixels);
    }

    /// <summary>A flipped part of a sheet is mirrored in the same place: its top-left still on (x, y).</summary>
    [Fact]
    public void AFlippedPartStaysInItsPlace()
    {
        var target = new PixelBuffer(4, 2);

        target.Draw(Nine(), new PixelRect(0, 1, 3, 2), 1, 0, flip: true);

        Assert.Equal(new uint[]
        {
            0, P6, P5, P4,
            0, P9, P8, P7,
        }, target.Pixels);
    }

    [Fact]
    public void ASpriteFadesWithItsOpacity()
    {
        var sprite = new Sprite("dot", Buffer(1, 1, 0xFFC86432), new PixelRect(0, 0, 1, 1), 0, 0);
        var target = new PixelBuffer(1, 1);
        target.Clear(K);

        target.Draw(sprite, 0, 0, opacity: 128);

        Assert.Equal(new uint[] { 0xFF643219 }, target.Pixels);
    }

    #endregion

    #region Defects

    /// <summary>
    /// A rectangle of 1 x 1 or more off its sheet, or one with a negative
    /// side, is a build defect: it throws, it never clips. (An empty one draws
    /// nothing: the region below.)
    /// </summary>
    [Theory]
    [InlineData(-1, 0, 1, 1)]
    [InlineData(0, -1, 1, 1)]
    [InlineData(0, 0, 4, 1)]
    [InlineData(0, 0, 1, 4)]
    [InlineData(2, 2, 2, 1)]
    [InlineData(0, 0, -1, 1)]
    [InlineData(0, 0, 1, -1)]
    [InlineData(0, 0, -1, -1)]
    [InlineData(int.MaxValue, 0, 1, 1)]
    [InlineData(1, 0, int.MaxValue, 1)]
    public void ARectangleOffItsSheetOrNegativeThrows(int x, int y, int width, int height)
    {
        var target = new PixelBuffer(4, 4);
        var rect = new PixelRect(x, y, width, height);
        var sprite = new Sprite("broken", Nine(), rect, 0, 0);

        Assert.Throws<ArgumentException>(() => target.Draw(Nine(), rect, 0, 0));
        var error = Assert.Throws<ArgumentException>(() => target.Draw(sprite, 0, 0));
        Assert.Contains("'broken'", error.Message);
        Assert.Throws<ArgumentException>(() => target.Draw(sprite, 0, 0, flip: true));
        Assert.Equal(new uint[16], target.Pixels);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    [InlineData(-2, -2)]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void AFillOfANegativeSizeThrows(int width, int height)
    {
        var target = new PixelBuffer(2, 2);

        Assert.Throws<ArgumentOutOfRangeException>(() => target.Fill(0, 0, width, height, K));
        Assert.Equal(new uint[4], target.Pixels);
    }

    [Fact]
    public void ABufferCantBeDrawnOntoItself()
    {
        var buffer = Nine();
        var sprite = new Sprite("self", buffer, new PixelRect(0, 0, 1, 1), 0, 0);

        Assert.Throws<ArgumentException>(() => buffer.Draw(buffer, new PixelRect(0, 0, 1, 1), 1, 1));
        Assert.Throws<ArgumentException>(() => buffer.Draw(sprite, 1, 1));
    }

    #endregion

    #region Empty rectangles (A39)

    /// <summary>
    /// A fill with a width or a height of 0 draws nothing and throws nothing:
    /// a bar drawn as Fill(x, y, (int)(fraction * 40), 3, ...) reaches 0
    /// exactly when it empties.
    /// </summary>
    [Theory]
    [InlineData(0, 3)]
    [InlineData(3, 0)]
    [InlineData(0, 0)]
    public void AnEmptyFillDrawsNothing(int width, int height)
    {
        var target = new PixelBuffer(4, 4);
        target.Clear(K);

        target.Fill(1, 1, width, height, 0xFFFFFFFF);
        target.Fill(1, 1, width, height, 0x80143C80);

        Assert.All(target.Pixels, p => Assert.Equal(K, p));
    }

    /// <summary>
    /// A source rectangle with a width or a height of 0 draws nothing,
    /// wherever it lies, on its sheet or far off it (an empty rectangle is
    /// nowhere): a crop of computed width at zero.
    /// </summary>
    [Theory]
    [InlineData(0, 0, 0, 2)]
    [InlineData(1, 1, 2, 0)]
    [InlineData(0, 0, 0, 0)]
    [InlineData(3, 3, 0, 1)]        // on the sheet's far corner
    [InlineData(99, 99, 0, 1)]      // off the sheet
    [InlineData(-5, 0, 3, 0)]       // off the sheet, zero high
    [InlineData(int.MaxValue, int.MinValue, 0, 0)]
    public void AnEmptySourceRectangleDrawsNothingWhereverItLies(int x, int y, int width, int height)
    {
        var target = new PixelBuffer(4, 4);
        target.Clear(K);
        var rect = new PixelRect(x, y, width, height);

        target.Draw(Nine(), rect, 1, 1);
        target.Draw(Nine(), rect, 1, 1, flip: true, opacity: 128);

        Assert.All(target.Pixels, p => Assert.Equal(K, p));
    }

    /// <summary>A sprite cropped to a frame with a width or a height of 0 draws nothing, flipped or not, wherever its frame lies.</summary>
    [Theory]
    [InlineData(0, 0, 0, 3)]
    [InlineData(0, 1, 3, 0)]
    [InlineData(50, 50, 0, 0)]
    public void AnEmptySpriteFrameDrawsNothing(int x, int y, int width, int height)
    {
        var target = new PixelBuffer(4, 4);
        target.Clear(K);
        var whole = new Sprite("bar", Nine(), new PixelRect(0, 0, 3, 3), 1, 2);
        var cropped = whole with { Frame = new PixelRect(x, y, width, height) };

        target.Draw(cropped, 2, 2);
        target.Draw(cropped, 2, 2, flip: true);

        Assert.All(target.Pixels, p => Assert.Equal(K, p));
    }

    #endregion

    /// <summary>
    /// The draws allocate nothing: 30 frames a second of a board must not feed the collector.
    /// Bound as the feed's cost test is, fewer bytes than rounds, not an exact zero: the
    /// runtime's tiering can allocate a few KB once on the test's own thread while the whole
    /// suite runs (seen: 7,912 bytes, the final read's D5), and a real allocation in a draw
    /// costs at least 24 bytes on every one of the six draws of every round.
    /// </summary>
    [Fact]
    public void DrawingAllocatesNothing()
    {
        var target = new PixelBuffer(64, 64);
        var sheet = Nine();
        var sprite = new Sprite("nine", sheet, new PixelRect(0, 0, 3, 3), 1, 2);
        void DrawAll(int i)
        {
            target.Clear(K);
            target.Fill(i % 50, 3, 20, 20, 0x80143C80);
            target.Draw(sprite, i % 64, 10, flip: i % 2 == 0, opacity: (byte)(i % 256));
            target.Draw(sheet, new PixelRect(1, 1, 2, 2), 60, i % 64);
            target.Fill(5, 5, i % 2, 4, 0xFF000000);                // a bar at zero half the time (A39)
            target.Draw(sheet, new PixelRect(9, 9, 0, 1), 0, 0);    // an empty crop
        }
        for (int i = 0; i < 100; i++) DrawAll(i); // warmed up

        const int Rounds = 20_000;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Rounds; i++) DrawAll(i);
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.True(after - before < Rounds, $"{after - before} bytes over {Rounds} rounds of six draws");
    }

    /// <summary>The 3 x 3 source of the clipping tests, P1 .. P9 row by row.</summary>
    private static PixelBuffer Nine() => Buffer(3, 3, P1, P2, P3, P4, P5, P6, P7, P8, P9);

    /// <summary>A buffer holding exactly these pixels, row by row.</summary>
    private static PixelBuffer Buffer(int width, int height, params uint[] pixels)
    {
        var buffer = new PixelBuffer(width, height);
        pixels.CopyTo(buffer.Pixels, 0);
        return buffer;
    }
}

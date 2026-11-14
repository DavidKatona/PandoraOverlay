using System.Windows.Media;
using PandoraOverlay.Games.Engine;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The arithmetic that puts a playfield on screen (A17): the device size, the
/// exact copies at a whole-number scale, the area average at a fractional one
/// (a flat colour stays flat, the premultiplied rule holds, a stripe gives the
/// values worked out by hand), and where the bitmap goes to land on whole
/// device pixels. What it can't show is a real display at a fractional scale:
/// that is on the owner's list.
/// </summary>
public class PresenterTests
{
    private const uint Black = 0xFF000000, White = 0xFFFFFFFF;

    // ---- DeviceSize ------------------------------------------------------------------

    [Theory]
    [InlineData(1.0, 532, 456)]
    [InlineData(1.25, 665, 570)]
    [InlineData(1.5, 798, 684)]
    [InlineData(1.75, 931, 798)]
    [InlineData(2.0, 1064, 912)]
    public void TheBoardsDeviceSizeAtEveryDisplayScale(double scale, int width, int height) =>
        Assert.Equal((width, height), Presenter.DeviceSize(532, 456, scale, scale));

    [Fact]
    public void ADeviceSizeIsRoundedPerSideAndNeverBelowOne()
    {
        Assert.Equal((5, 3), Presenter.DeviceSize(4, 2, 1.125, 1.25));   // 4.5 rounds away from zero; 2.5 too
        Assert.Equal((3, 2), Presenter.DeviceSize(4, 2, 0.7, 0.9));      // 2.8 and 1.8
        Assert.Equal((1, 1), Presenter.DeviceSize(4, 4, 0.01, 0.01));    // never 0
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AScaleIsAPositiveFiniteNumber(double scale)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Presenter.DeviceSize(4, 4, scale, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Presenter.DeviceSize(4, 4, 1, scale));
    }

    // ---- Whole-number scales ----------------------------------------------------------

    [Fact]
    public void ScaleOneIsACopy()
    {
        var source = Pattern(12, 8, seed: 1);
        var target = new uint[12 * 8];
        Presenter.Scale(source, 12, 8, target, 12, 8);
        Assert.Equal(source, target);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void AWholeNumberScaleMakesBlocks(int k)
    {
        const int w = 12, h = 8;
        var source = Pattern(w, h, seed: 2);
        var target = new uint[w * k * h * k];
        Presenter.Scale(source, w, h, target, w * k, h * k);

        for (var y = 0; y < h * k; y++)
            for (var x = 0; x < w * k; x++)
                Assert.Equal(source[y / k * w + x / k], target[y * w * k + x]);
    }

    [Fact]
    public void AWholeNumberScalePlanIsABlockAndOnlyThen()
    {
        Assert.Equal(1, new ScalePlan(8, 4, 8, 4).Block);
        Assert.Equal(3, new ScalePlan(8, 4, 24, 12).Block);
        Assert.Null(new ScalePlan(8, 4, 12, 6).Block);   // 1.5
        Assert.Null(new ScalePlan(8, 4, 16, 12).Block);  // 2 along x, 3 along y: not one k
        Assert.Null(new ScalePlan(8, 4, 4, 2).Block);    // shrinking
    }

    [Fact]
    public void WholeNumbersAlongEachAxisAreExactEvenWhenTheyDiffer()
    {
        // 2 along x and 3 along y is no block (not one k), but the area average
        // gives each device pixel one logical pixel's weight of 256: still exact.
        const int w = 8, h = 4;
        var source = Pattern(w, h, seed: 3);
        var target = new uint[16 * 12];
        Presenter.Scale(source, w, h, target, 16, 12);
        for (var y = 0; y < 12; y++)
            for (var x = 0; x < 16; x++)
                Assert.Equal(source[y / 3 * w + x / 2], target[y * 16 + x]);
    }

    // ---- Fractional scales ----------------------------------------------------------------

    [Theory]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    [InlineData(0.8)]
    [InlineData(0.37)]
    public void AFlatColourStaysExactlyThatColour(double scale)
    {
        const int w = 532, h = 456;
        var (dw, dh) = Presenter.DeviceSize(w, h, scale, scale);
        foreach (var colour in new[] { PixelBuffer.Colour(201, 87, 13), PixelBuffer.Colour(40, 120, 255, 128), PixelBuffer.Colour(255, 255, 255, 1), 0u, White })
        {
            var source = Enumerable.Repeat(colour, w * h).ToArray();
            var target = new uint[dw * dh];
            Presenter.Scale(source, w, h, target, dw, dh);
            Assert.All(target, p => Assert.Equal(colour, p));
        }
    }

    [Fact]
    public void AStripeAtOneAndAHalfGivesTheThreeValuesWorkedOutByHand()
    {
        // Four logical columns black, white, black, white made six device columns.
        // Device pixel i covers the logical span [i * 4 / 6, (i + 1) * 4 / 6): in
        // sixths, [4i, 4i + 4) against logical pixel j's [6j, 6j + 6).
        //   i = 0: [0, 4)   inside column 0          -> black, weight 256
        //   i = 1: [4, 8)   2 of column 0, 2 of 1    -> weights 128 + 128
        //   i = 2: [8, 12)  inside column 1          -> white
        // and again for columns 2 and 3. The half and half is, per channel,
        // (0 x 128 + 255 x 128 + 128) >> 8 = 32768 >> 8 = 128, and the alpha
        // (255 x 256 + 128) >> 8 = 255: 0xFF808080.
        const uint Half = 0xFF808080;
        var columns = new uint[] { Black, White, Black, White };
        var source = new uint[4 * 4];
        for (var y = 0; y < 4; y++)
            Array.Copy(columns, 0, source, y * 4, 4);

        var target = new uint[6 * 6];
        Presenter.Scale(source, 4, 4, target, 6, 6);

        var expected = new uint[] { Black, Half, White, Black, Half, White };
        for (var y = 0; y < 6; y++)
            Assert.Equal(expected, target.Skip(y * 6).Take(6).ToArray());

        // The same stripe across, through the pass along y.
        var rows = new uint[4 * 4];
        for (var y = 0; y < 4; y++)
            for (var x = 0; x < 4; x++)
                rows[y * 4 + x] = columns[y];
        Presenter.Scale(rows, 4, 4, target, 6, 6);
        for (var y = 0; y < 6; y++)
            Assert.All(target.Skip(y * 6).Take(6), p => Assert.Equal(expected[y], p));
    }

    [Fact]
    public void AStripeOfTwoColoursMixesEachChannelByItsWeights()
    {
        // (200, 60, 40) and (20, 60, 128), opaque, half and half as above: each
        // channel (a x 128 + b x 128 + 128) >> 8, i.e. the mean rounded half up.
        var a = PixelBuffer.Colour(200, 60, 40);
        var b = PixelBuffer.Colour(20, 60, 128);
        var source = new[] { a, b, a, b };
        var target = new uint[6];
        Presenter.Scale(source, 4, 1, target, 6, 1);
        Assert.Equal(new[] { a, PixelBuffer.Colour(110, 60, 84), b, a, PixelBuffer.Colour(110, 60, 84), b }, target);
    }

    [Theory]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    [InlineData(0.8)]
    [InlineData(0.6)]
    public void TransparentAndOpaqueKeepEveryChannelWithinItsAlpha(double scale)
    {
        // Fully transparent pixels beside opaque and half-transparent ones, the
        // edges where a sprite meets the ground: a premultiplied channel must
        // never come out above its alpha.
        const int w = 64, h = 48;
        var rng = new Rng(7);
        var source = new uint[w * h];
        for (var i = 0; i < source.Length; i++)
            source[i] = rng.Next(3) switch
            {
                0 => 0u,
                1 => PixelBuffer.Colour((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256)),
                _ => PixelBuffer.Colour((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256)),
            };
        var (dw, dh) = Presenter.DeviceSize(w, h, scale, scale);
        var target = new uint[dw * dh];
        Presenter.Scale(source, w, h, target, dw, dh);

        Assert.All(target, p =>
        {
            var alpha = p >> 24;
            Assert.True(((p >> 16) & 0xFF) <= alpha && ((p >> 8) & 0xFF) <= alpha && (p & 0xFF) <= alpha, $"0x{p:X8} has a channel above its alpha");
        });
    }

    [Fact]
    public void AFractionalScaleKeepsASolidCoreAndSoftensOnlyOneDevicePixel()
    {
        // One white logical pixel in black, at 1.75: its device span is
        // [7 x 1.75, 8 x 1.75) = [12.25, 14) along each axis, so device pixel 12
        // takes three quarters of it, 13 all of it, and everything else none.
        var source = Enumerable.Repeat(Black, 16 * 16).ToArray();
        source[7 * 16 + 7] = White;
        var target = new uint[28 * 28];
        Presenter.Scale(source, 16, 16, target, 28, 28);

        Assert.Equal(White, target[13 * 28 + 13]);
        for (var y = 0; y < 28; y++)
            for (var x = 0; x < 28; x++)
                if (x is < 12 or > 13 || y is < 12 or > 13)
                    Assert.Equal(Black, target[y * 28 + x]);
        Assert.NotEqual(Black, target[12 * 28 + 13]);
        Assert.NotEqual(White, target[12 * 28 + 13]);
    }

    [Theory]
    [InlineData(532, 665)]
    [InlineData(532, 798)]
    [InlineData(532, 931)]
    [InlineData(456, 365)]
    [InlineData(17, 5)]
    [InlineData(5, 17)]
    [InlineData(7, 7)]
    [InlineData(1, 9)]
    [InlineData(9, 1)]
    public void EveryDevicePixelsWeightsSumToExactly256AndCoverItsSpan(int logical, int device)
    {
        var axis = new AxisWeights(logical, device);
        for (var i = 0; i < device; i++)
        {
            var start = axis.Offset[i];
            var end = axis.Offset[i + 1];
            Assert.True(end > start);
            Assert.Equal(Presenter.WeightTotal, axis.Weight.Skip(start).Take(end - start).Sum());
            Assert.All(axis.Weight.Skip(start).Take(end - start), weight => Assert.InRange(weight, 0, 256));
            // The first logical pixel is the one the span starts in, the last the one it ends in.
            Assert.Equal((int)((long)i * logical / device), axis.First[i]);
            Assert.Equal((int)(((long)(i + 1) * logical - 1) / device), axis.First[i] + end - start - 1);
        }
        Assert.Equal(axis.Weight.Length, axis.Offset[device]);
    }

    [Fact]
    public void TheRemainderGoesToTheLargestWeight()
    {
        // 3 logical to 2 device: device 0 covers [0, 1.5): all of logical 0
        // (256 x 2 / 3 = 170 rounded down) and half of logical 1 (85); the lost
        // unit goes to the larger, 171 + 85.
        var axis = new AxisWeights(3, 2);
        Assert.Equal(new[] { 171, 85, 85, 171 }, axis.Weight);
    }

    [Fact]
    public void APlanGivesWhatTheOneOffScaleGives()
    {
        const int w = 40, h = 24;
        var source = Pattern(w, h, seed: 4);
        foreach (var (dw, dh) in new[] { (50, 30), (60, 36), (70, 42), (32, 19), (80, 48), (40, 24) })
        {
            var once = new uint[dw * dh];
            Presenter.Scale(source, w, h, once, dw, dh);
            var plan = new ScalePlan(w, h, dw, dh);
            var kept = new uint[dw * dh];
            Presenter.Scale(source, kept, plan);
            Presenter.Scale(source, kept, plan); // and again with the same plan: nothing carried over
            Assert.Equal(once, kept);
        }
    }

    [Fact]
    public void WrongSizesAreRefused()
    {
        var plan = new ScalePlan(4, 4, 6, 6);
        Assert.Throws<ArgumentException>(() => Presenter.Scale(new uint[15], new uint[36], plan));
        Assert.Throws<ArgumentException>(() => Presenter.Scale(new uint[16], new uint[35], plan));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScalePlan(0, 4, 6, 6));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScalePlan(4, 4, 6, 0));
    }

    [Fact]
    public void TheLanesHoldFourChannelsApart()
    {
        Assert.Equal(0x00AA_00BB_00CC_00DDUL, Presenter.Spread(0xAABBCCDD));
        Assert.Equal(0xAABBCCDDu, Presenter.Pack(Presenter.Spread(0xAABBCCDD) * 256));
        // The largest lane a sum can hold, 255 x 256, does not reach the next.
        Assert.Equal(0xFFFFFFFFu, Presenter.Pack(Presenter.Spread(0xFFFFFFFF) * 256));
        // Rounding to the nearest: 127.5 x 256 is 32640, plus 128, over 256 is 128.
        Assert.Equal(0x80808080u, Presenter.Pack(Presenter.Spread(0xFFFFFFFF) * 128));
    }

    // ---- Place: whole device pixels ---------------------------------------------------

    [Fact]
    public void AtOneHundredPercentOnAWholePixelNothingMoves()
    {
        var placed = Presenter.Place(532, 456, new Matrix(1, 0, 0, 1, 134, 48), 1, 1);
        Assert.Equal(new Placement(532, 456, 1, 1, 0, 0, 532, 456), placed);
    }

    [Theory]
    [InlineData(1.25, 134, 48)]
    [InlineData(1.25, 134.3, 48.7)]
    [InlineData(1.5, 133.5, 47.25)]
    [InlineData(1.75, 134, 48.1)]
    [InlineData(2.0, 133.25, 48.75)]
    public void TheBitmapsOriginLandsOnAWholeDevicePixel(double dpi, double left, double top)
    {
        var toRoot = new Matrix(1, 0, 0, 1, left, top);
        var placed = Presenter.Place(532, 456, toRoot, dpi, dpi);

        // Where the drawn origin lands on the device: the surface's (offset)
        // through its transform, times the display scale.
        var deviceX = (left + placed.OffsetX) * dpi;
        var deviceY = (top + placed.OffsetY) * dpi;
        Assert.Equal(Math.Round(deviceX), deviceX, 9);
        Assert.Equal(Math.Round(deviceY), deviceY, 9);
        Assert.InRange(Math.Abs(placed.OffsetX * dpi), 0, 0.5);
        Assert.InRange(Math.Abs(placed.OffsetY * dpi), 0, 0.5);

        // Drawn at its natural size, which is exactly the device size on the device: 1:1.
        Assert.Equal(Presenter.DeviceSize(532, 456, dpi, dpi), (placed.DeviceWidth, placed.DeviceHeight));
        Assert.Equal(placed.DeviceWidth, placed.DrawWidth * dpi, 9);
        Assert.Equal(placed.DeviceHeight, placed.DrawHeight * dpi, 9);
    }

    [Fact]
    public void ALayoutScaleAboveIsPartOfTheRealScale()
    {
        // The Games window drawn smaller on a small screen (A11): a layout scale
        // of 0.9 at 150% is a real scale of 1.35, and the window's content
        // origin at (12.4, 7.9) in the root.
        var toRoot = new Matrix(0.9, 0, 0, 0.9, 12.4, 7.9);
        var placed = Presenter.Place(532, 456, toRoot, 1.5, 1.5);

        Assert.Equal(1.35, placed.ScaleX, 12);
        Assert.Equal(1.35, placed.ScaleY, 12);
        Assert.Equal((718, 616), (placed.DeviceWidth, placed.DeviceHeight)); // 718.2 and 615.6 rounded

        // The nudge is in the surface's own units, so it is scaled by the
        // layout scale on its way to the root: offset x 0.9 in the root.
        var deviceX = (12.4 + placed.OffsetX * 0.9) * 1.5;
        var deviceY = (7.9 + placed.OffsetY * 0.9) * 1.5;
        Assert.Equal(Math.Round(deviceX), deviceX, 9);
        Assert.Equal(Math.Round(deviceY), deviceY, 9);
        Assert.Equal(placed.DeviceWidth, placed.DrawWidth * 1.35, 9);
    }

    [Fact]
    public void AMirrorOrATurnDoesNotGiveANegativeScale()
    {
        var mirrored = Presenter.Place(8, 8, new Matrix(-1, 0, 0, 1, 20, 0), 1.5, 1.5);
        Assert.Equal(1.5, mirrored.ScaleX, 12);
        var turned = Presenter.Place(8, 8, new Matrix(0, 1, -1, 0, 0, 0), 1, 1);
        Assert.Equal(1, turned.ScaleX, 12);
        Assert.Equal(1, turned.ScaleY, 12);
    }

    [Fact]
    public void ACollapsedLayoutFallsBackToTheDisplayScale()
    {
        var placed = Presenter.Place(8, 8, new Matrix(0, 0, 0, 0, 3.3, 1.1), 1.25, 1.25);
        Assert.Equal((10, 10), (placed.DeviceWidth, placed.DeviceHeight));
        Assert.True(double.IsFinite(placed.OffsetX) && double.IsFinite(placed.OffsetY));
    }

    [Fact]
    public void UnplacedIsScaleOneAtTheOrigin() =>
        Assert.Equal(new Placement(16, 8, 1, 1, 0, 0, 16, 8), Presenter.Unplaced(16, 8));

    /// <summary>Premultiplied pixels with every kind in them: transparent, half and opaque, from a seed.</summary>
    private static uint[] Pattern(int w, int h, uint seed)
    {
        var rng = new Rng(seed);
        var pixels = new uint[w * h];
        for (var i = 0; i < pixels.Length; i++)
            pixels[i] = PixelBuffer.Colour((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256), (byte)(rng.Next(4) * 85));
        return pixels;
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PandoraOverlay.Games.Engine;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The playfield on screen (A17): its sizes, its picture rendered off-screen
/// pixel for pixel, the pointer's arithmetic, and its placement through a real
/// presentation source (a hidden <see cref="HwndSource"/>, never shown, never
/// activated: the test project may name Interop, the engine may not). What
/// only a shown window on a real display at a fractional scale can show is on
/// the owner's list.
/// </summary>
public class PixelSurfaceTests
{
    [Theory]
    [InlineData(0, 8)]
    [InlineData(8, 0)]
    [InlineData(-4, 8)]
    [InlineData(3, 8)]
    [InlineData(8, 6)]
    [InlineData(530, 456)]
    public void ASideThatIsNotAPositiveMultipleOfFourIsRefused(int width, int height) => Sta.Run(() =>
        Assert.Throws<ArgumentException>(() => new PixelSurface(width, height)));

    [Fact]
    public void ItsBufferIsItsLogicalSize() => Sta.Run(() =>
    {
        var surface = new PixelSurface(532, 456);
        Assert.Equal(532, surface.Buffer.Width);
        Assert.Equal(456, surface.Buffer.Height);
        Assert.NotNull(new PixelSurface(4, 4).Buffer);
    });

    [Fact]
    public void ItMeasuresExactlyItsLogicalSize() => Sta.Run(() =>
    {
        var surface = new PixelSurface(532, 456);
        surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.Equal(new Size(532, 456), surface.DesiredSize);
        surface.Measure(new Size(800, 600));
        Assert.Equal(new Size(532, 456), surface.DesiredSize);
    });

    [Fact]
    public void ItIsSnappedAndNearestNeighbourAsBelts() => Sta.Run(() =>
    {
        var surface = new PixelSurface(8, 8);
        Assert.True(surface.SnapsToDevicePixels);
        Assert.True(surface.UseLayoutRounding);
        Assert.Equal(BitmapScalingMode.NearestNeighbor, RenderOptions.GetBitmapScalingMode(surface));
        Assert.Null(surface.Effect);
    });

    [Fact]
    public void RenderedOffScreenAfterPresentItsPixelsAreTheBuffers() => Sta.Run(() =>
    {
        var surface = new PixelSurface(48, 32);
        Paint(surface.Buffer, seed: 1);
        surface.Present();

        Assert.Equal(surface.Buffer.Pixels, Render(surface));
    });

    [Fact]
    public void APresentAfterTheBufferChangedShowsTheNewPicture() => Sta.Run(() =>
    {
        var surface = new PixelSurface(48, 32);
        Paint(surface.Buffer, seed: 2);
        surface.Present();
        Paint(surface.Buffer, seed: 3);
        Assert.NotEqual(surface.Buffer.Pixels, Render(surface)); // the screen keeps the last presented picture
        surface.Present();

        Assert.Equal(surface.Buffer.Pixels, Render(surface));
    });

    [Fact]
    public void NothingIsDrawnBeforeTheFirstPresent() => Sta.Run(() =>
    {
        var surface = new PixelSurface(16, 8);
        Paint(surface.Buffer, seed: 4);
        Assert.All(Render(surface), p => Assert.Equal(0u, p));
    });

    [Fact]
    public void WithNoPresentationSourceTheScaleIsOne() => Sta.Run(() =>
    {
        var surface = new PixelSurface(16, 8);
        surface.Present();
        Assert.Equal(Presenter.Unplaced(16, 8), surface.Placed);
        Assert.Equal(new Point(0, 0), surface.ToDip(0, 0));
        Assert.Equal(new Point(5, 3), surface.ToDip(5, 3));
        Assert.Equal(new Point(16, 8), surface.ToDip(16, 8));
    });

    // ---- ToPixel -----------------------------------------------------------------------

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(0.999, 0.5, 0, 0)]
    [InlineData(1, 1, 1, 1)]
    [InlineData(531.999, 455.999, 531, 455)]   // the far corner
    [InlineData(531.2, 0, 531, 0)]             // the top-right corner
    [InlineData(0, 455.9, 0, 455)]             // the bottom-left corner
    [InlineData(266.5, 455.0, 266, 455)]       // on the bottom row
    public void APointInsideIsThePixelItFallsIn(double x, double y, int px, int py) =>
        Assert.Equal((px, py), PixelSurface.ToPixel(new Point(x, y), 532, 456));

    [Theory]
    [InlineData(532, 0)]          // the right edge is outside
    [InlineData(0, 456)]          // and the bottom edge
    [InlineData(532, 456)]
    [InlineData(-0.001, 10)]
    [InlineData(10, -0.5)]
    [InlineData(-1, -1)]
    [InlineData(600, 600)]
    [InlineData(double.NaN, 10)]
    [InlineData(10, double.NaN)]
    [InlineData(double.PositiveInfinity, 10)]
    public void APointOutsideIsNone(double x, double y) =>
        Assert.Null(PixelSurface.ToPixel(new Point(x, y), 532, 456));

    // ---- Through a real presentation source -------------------------------------------

    [Fact]
    public void OnARealSourceItTakesTheDisplayAndTheLayoutScaleAndLandsOnWholeDevicePixels() => Sta.Run(() =>
    {
        // A layout scale of 0.9 above it (the Games window drawn smaller, A11)
        // and a left edge at a fraction of a unit.
        var surface = new PixelSurface(64, 48);
        var canvas = new Canvas { Width = 200, Height = 150 };
        Canvas.SetLeft(surface, 10.3);
        Canvas.SetTop(surface, 7.7);
        canvas.Children.Add(surface);
        var scaled = new Border { LayoutTransform = new ScaleTransform(0.9, 0.9), Child = canvas };

        using var source = HiddenSource(scaled);
        Pump(source);
        var dpi = VisualTreeHelper.GetDpi(surface);
        Paint(surface.Buffer, seed: 5);
        surface.Present();

        var placed = surface.Placed;
        AssertOnWholeDevicePixels(surface, placed, dpi, 0.9);

        // Moved by a fraction: the next layout pass nudges it again, same device size.
        Canvas.SetLeft(surface, 31.15);
        Pump(source);
        var moved = surface.Placed;
        Assert.NotEqual(placed.OffsetX, moved.OffsetX); // nudged again by the layout pass, with no Present
        Assert.Equal((placed.DeviceWidth, placed.DeviceHeight), (moved.DeviceWidth, moved.DeviceHeight));
        AssertOnWholeDevicePixels(surface, moved, dpi, 0.9);

        // Another layout scale: another device size, presented again from the buffer.
        scaled.LayoutTransform = new ScaleTransform(0.5, 0.5);
        Pump(source);
        AssertOnWholeDevicePixels(surface, surface.Placed, dpi, 0.5);
        Assert.Equal(Presenter.DeviceSize(64, 48, dpi.DpiScaleX * 0.5, dpi.DpiScaleY * 0.5),
            (surface.Placed.DeviceWidth, surface.Placed.DeviceHeight));
    });

    [Fact]
    public void ToDipFollowsTheBitmapWhereItIsDrawn() => Sta.Run(() =>
    {
        var surface = new PixelSurface(64, 48);
        var canvas = new Canvas { Width = 200, Height = 150 };
        Canvas.SetLeft(surface, 10.3);
        canvas.Children.Add(surface);
        using var source = HiddenSource(new Border { LayoutTransform = new ScaleTransform(0.9, 0.9), Child = canvas });
        Pump(source);
        surface.Present();

        var placed = surface.Placed;
        Assert.Equal(new Point(placed.OffsetX, placed.OffsetY), surface.ToDip(0, 0));
        var far = surface.ToDip(64, 48);
        Assert.Equal(placed.OffsetX + placed.DrawWidth, far.X, 9);
        Assert.Equal(placed.OffsetY + placed.DrawHeight, far.Y, 9);
    });

    /// <summary>
    /// The drawn origin is on a whole device pixel, the device size is the
    /// logical size at the real scale, and the drawn size is exactly the
    /// device size on the device.
    /// </summary>
    private static void AssertOnWholeDevicePixels(PixelSurface surface, Placement placed, DpiScale dpi, double layoutScale)
    {
        var root = PresentationSource.FromVisual(surface)!.RootVisual;
        var origin = surface.TransformToAncestor(root).Transform(new Point(placed.OffsetX, placed.OffsetY));
        var deviceX = origin.X * dpi.DpiScaleX;
        var deviceY = origin.Y * dpi.DpiScaleY;
        Assert.Equal(Math.Round(deviceX), deviceX, 6);
        Assert.Equal(Math.Round(deviceY), deviceY, 6);

        Assert.Equal(dpi.DpiScaleX * layoutScale, placed.ScaleX, 9);
        Assert.Equal(dpi.DpiScaleY * layoutScale, placed.ScaleY, 9);
        Assert.Equal(Presenter.DeviceSize(64, 48, placed.ScaleX, placed.ScaleY), (placed.DeviceWidth, placed.DeviceHeight));
        Assert.Equal(placed.DeviceWidth, placed.DrawWidth * placed.ScaleX, 6);
        Assert.Equal(placed.DeviceHeight, placed.DrawHeight * placed.ScaleY, 6);
    }

    /// <summary>
    /// A presentation source that is never shown: an HWND created without
    /// WS_VISIBLE, so nothing appears, nothing is activated and no focus moves.
    /// The content goes under a plain root, as a window's content goes under
    /// the window: a transform to the root never includes the root's own
    /// transform, and a Window can't carry one, so the Games window's layout
    /// scale (A11) sits on its content, as here.
    /// </summary>
    private static HwndSource HiddenSource(UIElement content)
    {
        var source = new HwndSource(new HwndSourceParameters("PixelSurfaceTests") { WindowStyle = 0, Width = 300, Height = 200 });
        var root = new Grid();
        root.Children.Add(content);
        source.RootVisual = root;
        return source;
    }

    /// <summary>Lets the dispatcher run everything queued above idle (layout, Loaded, LayoutUpdated).</summary>
    private static void Pump(HwndSource source)
    {
        if (source.RootVisual is UIElement root)
            root.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
    }

    /// <summary>The surface laid out and rendered off-screen at 96 DPI, its pixels as 0xAARRGGBB.</summary>
    private static uint[] Render(PixelSurface surface)
    {
        var width = surface.Buffer.Width;
        var height = surface.Buffer.Height;
        surface.Measure(new Size(width, height));
        surface.Arrange(new Rect(0, 0, width, height));
        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(surface);
        var pixels = new uint[width * height];
        target.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    /// <summary>A picture with opaque, half-transparent and transparent pixels, from a seed.</summary>
    private static void Paint(PixelBuffer buffer, uint seed)
    {
        var rng = new Rng(seed);
        for (var i = 0; i < buffer.Pixels.Length; i++)
            buffer.Pixels[i] = PixelBuffer.Colour((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256),
                rng.Next(4) switch { 0 => (byte)0, 1 => (byte)128, _ => (byte)255 });
    }
}

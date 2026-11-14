using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PandoraOverlay.Games.Engine;

/// <summary>
/// A playfield: a logical pixel buffer shown sharp at any display scale (A17).
/// One logical pixel is one WPF unit at 100%, so a surface takes the same room
/// as any other element at every display scale and the Games window's fixed
/// frame is never letterboxed; the art's sprites were drawn for exactly that
/// size.
///
/// <para><b>How it is shown.</b> A game composes its picture in
/// <see cref="Buffer"/> and calls <see cref="Present"/>. The surface works out
/// its real scale on screen (the display's, times any layout or render scale
/// above it), makes the picture at the device's own resolution with the pure
/// <see cref="Presenter"/> (an exact k x k copy at a whole-number scale, an
/// area average at a fractional one), writes it into a
/// <see cref="WriteableBitmap"/> of exactly that size, and draws the bitmap
/// 1:1 with its origin nudged onto a whole device pixel. WPF never scales it:
/// <see cref="RenderOptions.BitmapScalingModeProperty"/> NearestNeighbor,
/// device-pixel snapping and layout rounding are belts; the 1:1 bitmap is the
/// braces.</para>
///
/// <para><b>Whole frames.</b> Every <see cref="Present"/> scales and writes the
/// whole buffer: measured on the owner's PC (A05's first step), a 532 x 456
/// board composed, scaled and written costs about 0.6 ms a frame at 200% and
/// about 2.3 ms at 150% in a Release build, under the 8 ms the plan set
/// before a dirty-row scheme would be worth its complexity.</para>
///
/// <para><b>Nothing runs here.</b> No timer, no animation, no
/// <c>Effect</c>, no brush but the bitmap: the surface changes only when the
/// view's loop presents it (A19). The pointer is reported in logical pixels
/// (<see cref="PointerMoved"/>, <see cref="PointerPressed"/>,
/// <see cref="PointerLeft"/>), with no capture, no keyboard and no cursor
/// logic (A33). A view subscribes to them through its Guard, like every other
/// handler (A10).</para>
///
/// <para><b>Where the real scale comes from.</b> The display scale is
/// <see cref="VisualTreeHelper.GetDpi"/> of the element; any layout scale above
/// it (the Games window drawn smaller on a small screen, A11) is in the
/// transform to the root visual of its presentation source. With no
/// presentation source (a test, an off-screen render) the scale is 1. The
/// window, the application and the composition target are never asked: the
/// forbidden list bans all three (A29), and none is needed.</para>
/// </summary>
public sealed class PixelSurface : FrameworkElement
{
    /// <summary>The side a logical size must be a multiple of: 4 keeps the device size whole at 125%, 150% and 175% (4 x 1.25 = 5, 4 x 1.5 = 6, 4 x 1.75 = 7).</summary>
    internal const int SizeStep = 4;

    private readonly int _width;
    private readonly int _height;

    /// <summary>Where the bitmap is drawn now, and at what device size it was made.</summary>
    private Placement _placement;

    // Kept between frames, remade only when the device size changes: the scale's
    // tables, the device-size pixels and the bitmap.
    private ScalePlan? _plan;
    private uint[]? _device;
    private WriteableBitmap? _bitmap;

    private bool _listening;

    /// <summary>
    /// A surface of <paramref name="width"/> x <paramref name="height"/>
    /// logical pixels, each a positive multiple of 4
    /// (<see cref="ArgumentException"/> otherwise), with a transparent
    /// <see cref="Buffer"/> of that size. Nothing is on screen until the first
    /// <see cref="Present"/>.
    /// </summary>
    public PixelSurface(int width, int height)
    {
        CheckSide(width, nameof(width));
        CheckSide(height, nameof(height));
        _width = width;
        _height = height;
        Buffer = new PixelBuffer(width, height);
        _placement = Presenter.Unplaced(width, height);

        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;

        // LayoutUpdated is raised for every layout pass of the whole dispatcher,
        // not only this element's: listened to only while the surface is in a
        // live tree, so a surface taken off screen is not kept by it.
        Loaded += (_, _) => Listen(true);
        Unloaded += (_, _) => Listen(false);
    }

    /// <summary>The picture: a game composes into it, then calls <see cref="Present"/>.</summary>
    public PixelBuffer Buffer { get; }

    /// <summary>The pointer moved over a logical pixel (the button is <see cref="PointerButton.None"/>).</summary>
    public event Action<PixelPointer>? PointerMoved;

    /// <summary>The left or the right button went down over a logical pixel.</summary>
    public event Action<PixelPointer>? PointerPressed;

    /// <summary>The pointer left the surface.</summary>
    public event Action? PointerLeft;

    /// <summary>
    /// Puts the buffer's contents on screen: scaled to the device size at the
    /// surface's real scale now, written into the bitmap, drawn at the next
    /// render. The bitmap and its arrays are remade only when the device size
    /// has changed since the last time.
    /// </summary>
    public void Present() => Present(CurrentPlacement());

    /// <summary>
    /// The point (<paramref name="x"/>, <paramref name="y"/>) of the buffer
    /// (the top-left corner of that pixel) in the surface's own coordinates,
    /// for a view to <c>TranslatePoint</c> a WPF callout from (A18). It
    /// follows the bitmap where it is drawn, nudge included, so a callout
    /// points at the pixel as it is seen; before the first
    /// <see cref="Present"/> it is simply (x, y).
    /// </summary>
    public Point ToDip(int x, int y) => new(
        _placement.OffsetX + x * _placement.DrawWidth / _width,
        _placement.OffsetY + y * _placement.DrawHeight / _height);

    /// <summary>Where the bitmap is drawn now and at what device size, for the tests of the placement through a real presentation source.</summary>
    internal Placement Placed => _placement;

    /// <summary>
    /// The logical pixel a point of the buffer's own space falls in: each
    /// coordinate floored, null outside 0 .. size (the right and bottom edges
    /// are outside, as is not-a-number).
    /// </summary>
    internal static (int X, int Y)? ToPixel(Point p, int width, int height)
    {
        // Written as "not at least 0" so that a NaN is outside too.
        if (!(p.X >= 0) || !(p.Y >= 0))
            return null;
        var x = Math.Floor(p.X);
        var y = Math.Floor(p.Y);
        if (x >= width || y >= height)
            return null;
        return ((int)x, (int)y);
    }

    /// <summary>Exactly the logical size, in WPF units: one logical pixel is one unit at 100% (A17), and the surface never asks for more or less.</summary>
    protected override Size MeasureOverride(Size availableSize) => new(_width, _height);

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (_bitmap is null)
            return;
        drawingContext.DrawImage(_bitmap, new Rect(
            _placement.OffsetX, _placement.OffsetY, _placement.DrawWidth, _placement.DrawHeight));
    }

    /// <summary>
    /// The whole surface takes the pointer, not only where the bitmap is drawn:
    /// before the first <see cref="Present"/> nothing is drawn, and the nudge
    /// can leave a sliver of the box uncovered.
    /// </summary>
    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) =>
        new Rect(RenderSize).Contains(hitTestParameters.HitPoint)
            ? new PointHitTestResult(this, hitTestParameters.HitPoint)
            : null;

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Recheck();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (PixelAt(e.GetPosition(this)) is { } pixel)
            PointerMoved?.Invoke(new PixelPointer(pixel.X, pixel.Y, PointerButton.None));
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        var button = e.ChangedButton switch
        {
            MouseButton.Left => PointerButton.Left,
            MouseButton.Right => PointerButton.Right,
            _ => PointerButton.None,
        };
        if (button == PointerButton.None)
            return;
        if (PixelAt(e.GetPosition(this)) is { } pixel)
            PointerPressed?.Invoke(new PixelPointer(pixel.X, pixel.Y, button));
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        PointerLeft?.Invoke();
    }

    // ---- Placement ------------------------------------------------------------------

    private void Present(Placement placement)
    {
        if (_bitmap is null || _plan is null || _device is null
            || placement.DeviceWidth != _plan.DeviceWidth || placement.DeviceHeight != _plan.DeviceHeight)
        {
            _plan = new ScalePlan(_width, _height, placement.DeviceWidth, placement.DeviceHeight);
            _device = new uint[placement.DeviceWidth * placement.DeviceHeight];
            // Its DPI is 96 times the real scale, so its natural size is the
            // logical size (to well under a device pixel): it is drawn at that
            // size, 1:1 on the device.
            _bitmap = new WriteableBitmap(placement.DeviceWidth, placement.DeviceHeight,
                96 * placement.ScaleX, 96 * placement.ScaleY, PixelFormats.Pbgra32, null);
        }
        _placement = placement;

        // The buffer's 0xAARRGGBB, read as little-endian ints, is exactly
        // Pbgra32 (the bytes B, G, R, A), premultiplied as WPF expects: no
        // conversion, only the scale.
        Presenter.Scale(Buffer.Pixels, _device, _plan);
        _bitmap.WritePixels(new Int32Rect(0, 0, placement.DeviceWidth, placement.DeviceHeight),
            _device, placement.DeviceWidth * 4, 0);
        InvalidateVisual();
    }

    /// <summary>
    /// After a layout pass or a display change: where the surface now sits.
    /// The same device size moved by a fraction is only drawn again (the
    /// nudge changes); another device size is presented again from the buffer
    /// as it is. Cheap when nothing changed: one transform and a comparison.
    /// Nothing until the first <see cref="Present"/>, which places it.
    /// </summary>
    private void Recheck()
    {
        if (_bitmap is null)
            return;
        var placement = CurrentPlacement();
        if (placement == _placement)
            return;
        if (placement.DeviceWidth != _placement.DeviceWidth || placement.DeviceHeight != _placement.DeviceHeight)
        {
            Present(placement);
            return;
        }
        _placement = placement;
        InvalidateVisual();
    }

    /// <summary>The surface's real scale and its nudge now, from its presentation source; unplaced (scale 1) with none.</summary>
    private Placement CurrentPlacement()
    {
        if (PresentationSource.FromVisual(this)?.RootVisual is not { } root)
            return Presenter.Unplaced(_width, _height);

        Matrix toRoot;
        if (ReferenceEquals(root, this))
            toRoot = Matrix.Identity;
        else if (root.IsAncestorOf(this))
            toRoot = MatrixOf(TransformToAncestor(root));
        else
            return Presenter.Unplaced(_width, _height);

        var dpi = VisualTreeHelper.GetDpi(this);
        return Presenter.Place(_width, _height, toRoot, dpi.DpiScaleX, dpi.DpiScaleY);
    }

    /// <summary>
    /// A 2D transform's matrix. TransformToAncestor gives a plain Transform in
    /// a 2D tree; any other kind is read off three points, which is the same
    /// matrix for anything affine.
    /// </summary>
    private static Matrix MatrixOf(GeneralTransform transform)
    {
        if (transform is Transform plain)
            return plain.Value;
        if (transform.TryTransform(new Point(0, 0), out var o)
            && transform.TryTransform(new Point(1, 0), out var x)
            && transform.TryTransform(new Point(0, 1), out var y))
            return new Matrix(x.X - o.X, x.Y - o.Y, y.X - o.X, y.Y - o.Y, o.X, o.Y);
        return Matrix.Identity;
    }

    /// <summary>The logical pixel under a point of the surface, through the bitmap as it is drawn (its nudge and its natural size).</summary>
    private (int X, int Y)? PixelAt(Point p) => ToPixel(
        new Point((p.X - _placement.OffsetX) * _width / _placement.DrawWidth,
                  (p.Y - _placement.OffsetY) * _height / _placement.DrawHeight),
        _width, _height);

    private void Listen(bool on)
    {
        if (on == _listening)
            return;
        _listening = on;
        if (on)
        {
            LayoutUpdated += OnLayoutUpdated;
            Recheck();
        }
        else
        {
            LayoutUpdated -= OnLayoutUpdated;
        }
    }

    private void OnLayoutUpdated(object? sender, EventArgs e) => Recheck();

    private static void CheckSide(int side, string name)
    {
        if (side < SizeStep || side % SizeStep != 0)
            throw new ArgumentException(
                $"A pixel surface's sides are positive multiples of {SizeStep}, so its device size stays whole at 125%, 150% and 175%; {side} is not.", name);
    }
}

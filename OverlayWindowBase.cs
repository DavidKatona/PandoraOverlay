using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace PandoraOverlay;

/// <summary>
/// Shared behaviour for the overlay's always-on-top windows: the click-through
/// / no-activate / no-Alt-Tab window styles (x64 SetWindowLongPtr), edit-mode
/// state with banner-height position compensation (the content you position
/// stays put between modes — the banner grows upward instead of pushing the
/// panel down), a manual edit-mode drag with magnetic snapping (work-area
/// edges, comfort inset, the other overlay window; hold Alt to bypass), and
/// shared appearance settings. The global hotkeys are registered once, by
/// MainWindow.
/// </summary>
public abstract class OverlayWindowBase : Window
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x00000020; // clicks fall through to the game
    private const long WS_EX_TOOLWINDOW = 0x00000080;  // no Alt-Tab entry
    private const long WS_EX_NOACTIVATE = 0x08000000;  // never steals focus

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    // Shared edit-mode border styling, so every overlay window looks the same.
    protected static readonly Brush BorderLocked = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
    protected static readonly Brush BorderEdit = new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x64));

    // Live overlay windows, so dragging one can snap against the others.
    private static readonly List<OverlayWindowBase> Instances = new();

    private double _bannerShift;
    private bool _dragging;
    private Point _dragStartCursor;
    private Point _dragStartWindow;

    /// <summary>True while the window is interactive (draggable, buttons usable).</summary>
    public bool EditMode { get; private set; }

    /// <summary>The edit banner, measured for position compensation when it appears.</summary>
    protected abstract FrameworkElement? BannerElement { get; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyClickThrough(clickThrough: true);
        Instances.Add(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        Instances.Remove(this);
        base.OnClosed(e);
    }

    /// <summary>Enters/leaves edit mode; subclasses restyle in OnEditModeChanged.</summary>
    public void SetEditMode(bool on)
    {
        if (on == EditMode) return;
        EditMode = on;
        ApplyClickThrough(clickThrough: !on);
        OnEditModeChanged(on);
        CompensateForBanner(on);
    }

    protected abstract void OnEditModeChanged(bool editMode);

    /// <summary>
    /// Keeps the CONTENT stationary across the mode switch: the banner grows
    /// upward into empty space instead of pushing the panel down, so what you
    /// position in edit mode is exactly where the panel sits once locked.
    /// Clamped at the top of the work area so the banner stays on-screen.
    /// </summary>
    private void CompensateForBanner(bool entering)
    {
        if (entering)
        {
            UpdateLayout(); // the banner just became visible — measure it
            var banner = BannerElement;
            var scale = (Content as FrameworkElement)?.LayoutTransform is ScaleTransform s ? s.ScaleY : 1.0;
            var height = banner is null
                ? 0
                : (banner.ActualHeight + banner.Margin.Top + banner.Margin.Bottom) * scale;
            var workTop = GetWorkAreaDips().Top;
            _bannerShift = Math.Clamp(height, 0, Math.Max(0, Top - workTop));
            Top -= _bannerShift;
        }
        else
        {
            Top += _bannerShift;
            _bannerShift = 0;
        }
    }

    /// <summary>
    /// Applies the shared appearance settings: UI scale as a LayoutTransform
    /// on the content root (the window resizes with it — a RenderTransform
    /// would clip), and the panel-glass opacity as the root Border's
    /// background alpha, leaving text and content fully crisp.
    /// </summary>
    protected void ApplyAppearance(OverlayConfig config)
    {
        if (Content is not Border panel) return;

        var scale = Math.Clamp(config.UiScale, 0.75, 1.5);
        panel.LayoutTransform = scale == 1.0 ? null : new ScaleTransform(scale, scale);

        var alpha = (byte)Math.Round(Math.Clamp(config.BackgroundOpacity, 0.3, 1.0) * 255);
        panel.Background = new SolidColorBrush(Color.FromArgb(alpha, 0x10, 0x15, 0x1B));
    }

    private void ApplyClickThrough(bool clickThrough)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        style |= WS_EX_TOOLWINDOW;
        if (clickThrough)
        {
            style |= WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
        }
        else
        {
            style &= ~(WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);
        }
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style));
    }

    // ---- Edit-mode drag with snapping --------------------------------------

    /// <summary>Wire to MouseLeftButtonDown: dragging is an edit-mode-only affair.</summary>
    protected void DragIfEditing(MouseButtonEventArgs e)
    {
        if (!EditMode || e.ButtonState != MouseButtonState.Pressed) return;
        _dragging = true;
        _dragStartCursor = CursorInDips(e);
        _dragStartWindow = new Point(Left, Top);
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;

        var cursor = CursorInDips(e);
        var x = _dragStartWindow.X + (cursor.X - _dragStartCursor.X);
        var y = _dragStartWindow.Y + (cursor.Y - _dragStartCursor.Y);

        if ((Keyboard.Modifiers & ModifierKeys.Alt) == 0)
        {
            // Snap in CONTENT space, banner excluded — "flush to the edge"
            // means the panel as it will sit once locked.
            var snapped = SnapResolver.Snap(
                new Point(x, y + _bannerShift),
                new Size(ActualWidth, Math.Max(0, ActualHeight - _bannerShift)),
                GetWorkAreaDips(),
                Instances.Where(w => w != this && w.IsVisible).Select(w => w.ContentBounds));
            x = snapped.X;
            y = snapped.Y - _bannerShift;
        }

        Left = x;
        Top = y;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
    }

    /// <summary>The window's bounds minus the edit banner — what the panel occupies when locked.</summary>
    private Rect ContentBounds =>
        new(Left, Top + _bannerShift, ActualWidth, Math.Max(0, ActualHeight - _bannerShift));

    private Point CursorInDips(MouseEventArgs e)
    {
        var device = PointToScreen(e.GetPosition(this));
        return PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice.Transform(device) ?? device;
    }

    /// <summary>The current monitor's work area (taskbar excluded), in DIPs.</summary>
    private Rect GetWorkAreaDips()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var wa = System.Windows.Forms.Screen.FromHandle(hwnd).WorkingArea;
        if (PresentationSource.FromVisual(this)?.CompositionTarget is not { } target)
        {
            return new Rect(wa.X, wa.Y, wa.Width, wa.Height);
        }
        var fromDevice = target.TransformFromDevice;
        var topLeft = fromDevice.Transform(new Point(wa.Left, wa.Top));
        var bottomRight = fromDevice.Transform(new Point(wa.Right, wa.Bottom));
        return new Rect(topLeft, bottomRight);
    }
}

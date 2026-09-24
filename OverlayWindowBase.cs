using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PandoraOverlay;

/// <summary>
/// Shared behaviour for the overlay's always-on-top windows: the click-through
/// / no-activate / no-Alt-Tab window styles (x64 SetWindowLongPtr), edit-mode
/// state (border-only indication — windows are static-size in both modes, so
/// position fidelity is inherent), shared appearance settings, a manual
/// edit-mode drag with magnetic snapping (screen edges, comfort inset, the
/// other panels; hold Alt to bypass) plus guide lines, and on-screen clamping
/// on lock and at startup. The global hotkeys are registered once, by
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

    // One guide window for the whole app, created on the first snapping drag.
    private static SnapGuideWindow? _guides;

    private bool _dragging;
    private Point _dragStartCursor;
    private Point _dragStartWindow;

    // ---- Attention fade ----------------------------------------------------
    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(300);
    private bool _attention = true;   // lit until the window says otherwise
    private double _idleOpacity = 1;  // 1 = fade off (or a window that never fades)

    /// <summary>True while the window is interactive (draggable, buttons usable).</summary>
    public bool EditMode { get; private set; }

    /// <summary>False for transient chrome (the control panel): it snaps when dragged, but is never a target.</summary>
    protected virtual bool IsSnapTarget => true;

    /// <summary>
    /// True for windows that take part in the attention fade (stats panel,
    /// Prime tracker). The minimap does not: nothing on it can wake it, and
    /// it is consulted, not watched.
    /// </summary>
    protected virtual bool Fades => false;

    protected OverlayWindowBase()
    {
        // Startup safety: saved positions can reference a monitor that no
        // longer exists (or a changed resolution) — pull the window into view.
        Loaded += (_, _) => ClampIntoScreen();
    }

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
        UpdateFade(animate: true); // editing always shows every panel in full
        if (!on) ClampIntoScreen(); // a locked panel is always fully on-screen
    }

    protected abstract void OnEditModeChanged(bool editMode);

    /// <summary>
    /// The window's own verdict on whether it needs eyes right now. With the
    /// fade on, false eases the whole window (text, bars and glass alike) to
    /// the idle opacity and true brings it back; edit mode overrides to full.
    /// </summary>
    protected void SetAttention(bool lit)
    {
        if (lit == _attention) return;
        _attention = lit;
        UpdateFade(animate: true);
    }

    private void UpdateFade(bool animate)
    {
        var target = EditMode || _attention ? 1.0 : _idleOpacity;
        if (animate && IsVisible)
        {
            BeginAnimation(OpacityProperty, new DoubleAnimation(target, FadeDuration));
        }
        else
        {
            BeginAnimation(OpacityProperty, null); // release any running animation before setting
            Opacity = target;
        }
    }

    /// <summary>
    /// Applies the shared appearance settings: UI scale as a LayoutTransform
    /// on the content root (the window resizes with it — a RenderTransform
    /// would clip), and the panel-glass opacity as the root Border's
    /// background alpha, leaving text and content fully crisp.
    /// </summary>
    /// <summary>Scale used by ApplyAppearance; the minimap overrides to 1 (it has a native size setting instead).</summary>
    protected virtual double AppearanceScale(OverlayConfig config) => config.UiScale;

    protected void ApplyAppearance(OverlayConfig config)
    {
        if (Content is not Border panel) return;

        var scale = Math.Clamp(AppearanceScale(config), 0.75, 1.5);
        panel.LayoutTransform = scale == 1.0 ? null : new ScaleTransform(scale, scale);

        var alpha = (byte)Math.Round(Math.Clamp(config.BackgroundOpacity, 0.3, 1.0) * 255);
        panel.Background = new SolidColorBrush(Color.FromArgb(alpha, 0x10, 0x15, 0x1B));

        _idleOpacity = Fades && config.FadeEnabled ? Math.Clamp(config.FadeIdleOpacity, 0.2, 0.8) : 1.0;
        UpdateFade(animate: false);
    }

    private void ApplyClickThrough(bool clickThrough) =>
        ApplyClickThroughStyles(new WindowInteropHelper(this).Handle, clickThrough);

    /// <summary>Shared with SnapGuideWindow, which is always click-through.</summary>
    internal static void ApplyClickThroughStyles(IntPtr hwnd, bool clickThrough)
    {
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
            var snapped = SnapResolver.Snap(
                new Point(x, y),
                new Size(ActualWidth, ActualHeight),
                GetScreenBoundsDips(),
                Instances.Where(w => w != this && w.IsVisible && w.IsSnapTarget)
                         .Select(w => new Rect(w.Left, w.Top, w.ActualWidth, w.ActualHeight)));
            x = snapped.Position.X;
            y = snapped.Position.Y;

            _guides ??= new SnapGuideWindow();
            _guides.ShowGuides(snapped.GuideX, snapped.GuideY);
        }
        else
        {
            _guides?.HideGuides();
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
        _guides?.HideGuides();
    }

    /// <summary>Moves the window fully into its monitor's bounds.</summary>
    private void ClampIntoScreen()
    {
        UpdateLayout(); // settle any pending size change before measuring
        var clamped = SnapResolver.ClampIntoRect(
            new Rect(Left, Top, ActualWidth, ActualHeight), GetScreenBoundsDips());
        Left = clamped.X;
        Top = clamped.Y;
    }

    private Point CursorInDips(MouseEventArgs e)
    {
        var device = PointToScreen(e.GetPosition(this));
        return PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice.Transform(device) ?? device;
    }

    /// <summary>
    /// The current monitor's FULL bounds in DIPs — deliberately not the work
    /// area: the game runs borderless-fullscreen over the taskbar, so "screen
    /// bottom" must mean the true bottom (the overlay is topmost anyway).
    /// </summary>
    protected Rect GetScreenBoundsDips()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var wa = System.Windows.Forms.Screen.FromHandle(hwnd).Bounds;
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

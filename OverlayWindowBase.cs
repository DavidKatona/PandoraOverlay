using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace PandoraOverlay;

/// <summary>
/// Shared behaviour for the overlay's always-on-top windows: the click-through
/// / no-activate / no-Alt-Tab window styles (x64 SetWindowLongPtr) and the
/// drag-to-move rule while edit mode is on. The global Ctrl+F8 hotkey itself is
/// registered once, by MainWindow, which toggles every open overlay window.
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

    /// <summary>True while the window is interactive (draggable, buttons usable).</summary>
    public bool EditMode { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyClickThrough(clickThrough: true);
    }

    /// <summary>Enters/leaves edit mode; subclasses restyle in OnEditModeChanged.</summary>
    public void SetEditMode(bool on)
    {
        EditMode = on;
        ApplyClickThrough(clickThrough: !on);
        OnEditModeChanged(on);
    }

    protected abstract void OnEditModeChanged(bool editMode);

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

    /// <summary>Wire to MouseLeftButtonDown: dragging is an edit-mode-only affair.</summary>
    protected void DragIfEditing(MouseButtonEventArgs e)
    {
        if (EditMode && e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}

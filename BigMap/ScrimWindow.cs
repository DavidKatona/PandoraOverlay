using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace PandoraOverlay;

/// <summary>
/// The map's dimmed backdrop: a full-monitor window of 70% black over the
/// game, the game showing through. A WPF window with AllowsTransparency and
/// a translucent background — WPF makes it a layered window itself. It
/// never changes after its first frame, so it costs nothing per frame; the
/// map's moving content lives in its own opaque window (the perf spike's
/// lesson: one full-monitor per-pixel-transparent window that also redrew
/// the map could not keep its frame rate). The first build instead marked
/// a plain WPF window layered with SetLayeredWindowAttributes: WPF ignores
/// that for its own rendering and the owner saw the game go pitch black
/// (Oct 9 2026). It never takes focus (the map keeps it); a click on it —
/// outside the map — asks for the map to close.
/// The pitch-black build got past the spike because its brightness check
/// ran over a dark screen and could not tell 70% from 100% black: check a
/// dim over a mid-grey screen (there the broken build read 0 of 128, this
/// one 38). Cost: 2–5% of one core in the spike, plus about 10% of one
/// core of the compositor while the map is open.
/// </summary>
internal sealed class ScrimWindow : Window
{
    private static readonly Brush Dim = MakeDim();

    public ScrimWindow(Rect monitor)
    {
        Title = "Pandora Map Backdrop";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = Dim;
        Left = monitor.Left;
        Top = monitor.Top;
        Width = monitor.Width;
        Height = monitor.Height;
    }

    private static Brush MakeDim()
    {
        var brush = new SolidColorBrush(Color.FromArgb(0xB3, 0, 0, 0)); // 70% black
        brush.Freeze();
        return brush;
    }

    /// <summary>A click on the backdrop, outside the map (on the release, so the game never sees half a click).</summary>
    public event Action? Clicked;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style));
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        Clicked?.Invoke();
    }

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);
}

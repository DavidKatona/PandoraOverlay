using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PandoraOverlay;

/// <summary>
/// Full-virtual-screen, click-through, no-activate window that draws the
/// snap guide lines while an overlay window is being dragged. One vertical
/// and one horizontal line: orange for screen-edge/inset targets, blue for
/// peer-window targets (matching the arrow and waypoint colours). Created
/// lazily by OverlayWindowBase, shown only mid-drag, hidden on release —
/// costs nothing in normal use.
/// </summary>
public sealed class SnapGuideWindow : Window
{
    private static readonly Brush ScreenGuide = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xC8, 0x64));
    private static readonly Brush PeerGuide = new SolidColorBrush(Color.FromArgb(0xE6, 0x4F, 0xC3, 0xF7));

    private readonly Line _vertical = new() { StrokeThickness = 1, Visibility = Visibility.Collapsed };
    private readonly Line _horizontal = new() { StrokeThickness = 1, Visibility = Visibility.Collapsed };

    public SnapGuideWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        IsHitTestVisible = false;

        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        var canvas = new Canvas();
        canvas.Children.Add(_vertical);
        canvas.Children.Add(_horizontal);
        Content = canvas;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        OverlayWindowBase.ApplyClickThroughStyles(new WindowInteropHelper(this).Handle, clickThrough: true);
    }

    /// <summary>Shows/updates the guide lines; positions are in screen DIPs.</summary>
    public void ShowGuides(SnapGuide? vertical, SnapGuide? horizontal)
    {
        if (vertical is null && horizontal is null)
        {
            HideGuides();
            return;
        }
        if (!IsVisible) Show();

        if (vertical is { } v)
        {
            _vertical.Stroke = v.FromPeer ? PeerGuide : ScreenGuide;
            _vertical.X1 = _vertical.X2 = v.Position - Left;
            _vertical.Y1 = 0;
            _vertical.Y2 = Height;
            _vertical.Visibility = Visibility.Visible;
        }
        else
        {
            _vertical.Visibility = Visibility.Collapsed;
        }

        if (horizontal is { } h)
        {
            _horizontal.Stroke = h.FromPeer ? PeerGuide : ScreenGuide;
            _horizontal.Y1 = _horizontal.Y2 = h.Position - Top;
            _horizontal.X1 = 0;
            _horizontal.X2 = Width;
            _horizontal.Visibility = Visibility.Visible;
        }
        else
        {
            _horizontal.Visibility = Visibility.Collapsed;
        }
    }

    public void HideGuides()
    {
        _vertical.Visibility = Visibility.Collapsed;
        _horizontal.Visibility = Visibility.Collapsed;
        if (IsVisible) Hide();
    }
}

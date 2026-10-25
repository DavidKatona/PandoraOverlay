using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PandoraOverlay;

/// <summary>
/// Full-virtual-screen, click-through, no-activate window that draws the
/// snap guide lines while an overlay window is being dragged. One vertical
/// and one horizontal line, in the theme's two guide colours (Map.SnapGuide):
/// one for screen-edge/inset targets, one for peer-window targets (in
/// Default orange and blue, matching the arrow and waypoint colours; read
/// at each drag, so a theme switch needs nothing here). Created lazily by
/// OverlayWindowBase, shown only mid-drag, hidden on release — costs
/// nothing in normal use.
/// </summary>
public sealed class SnapGuideWindow : Window
{
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
            _vertical.Stroke = Guide(v);
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
            _horizontal.Stroke = Guide(h);
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

    /// <summary>A guide's colour: to another panel, or to the screen.</summary>
    private static Brush Guide(SnapGuide guide) =>
        (guide.FromPeer ? Theme.Current.Map.SnapGuide.Peer : Theme.Current.Map.SnapGuide.Screen).ToBrush();

    public void HideGuides()
    {
        _vertical.Visibility = Visibility.Collapsed;
        _horizontal.Visibility = Visibility.Collapsed;
        if (IsVisible) Hide();
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace PandoraOverlay;

/// <summary>What the control panel's Layers menu marks: the minimap's view and layers, and the two hotkeys' labels (empty = none shown).</summary>
public sealed record PanelLayers(bool MinimapShown, bool Centered, bool Heatmap, bool Areas, string? ViewKey, string? HeatmapKey);

/// <summary>
/// ControlPanelWindow, Layers part (the Pack, Nov 2026): the minimap's Map
/// view, Heatmap and Areas buttons became ONE Layers button with a menu, to
/// make room for the PACK group without growing the panel's fixed frame (the
/// owner, Nov 8 2026: "Yes, Layers menu"). It is built like the map's own
/// Layers menu (BigMapWindow.Layers.cs), in the maps' menu look (MenuTick): a
/// VIEW caption with the minimap-view hotkey on its right, Island view and
/// Centered view (the current one ticked), a separator, a LAYERS caption,
/// Heatmap (its hotkey on the right) and Areas, ticked when on. A row calls
/// the existing callback (the same as Ctrl+F6, Ctrl+F7 and the old Areas
/// button) and the menu stays open, marked afresh; a click away closes it,
/// and Esc closes it on its RELEASE (.claude/rules/shell.md), the press and
/// the release both handled, so the next Esc is edit mode's. With the minimap
/// hidden the rows are greyed: there is nothing to switch. Same class as
/// ControlPanelWindow.xaml.cs.
/// </summary>
public partial class ControlPanelWindow
{
    private const double MenuGap = 6; // between the menu's bottom and the button
    private bool _menuEscapeDown;

    /// <summary>Above the button (the panel sits at the screen's bottom), its left edges aligned; WPF flips it below where the screen's top would cut it.</summary>
    private static CustomPopupPlacement[] PlaceLayersMenu(Size popup, Size target, Point offset) =>
        new[]
        {
            new CustomPopupPlacement(new Point(0, -popup.Height - MenuGap), PopupPrimaryAxis.Horizontal),
            new CustomPopupPlacement(new Point(0, target.Height + MenuGap), PopupPrimaryAxis.Horizontal),
        };

    private void LayersButton_Click(object sender, RoutedEventArgs e)
    {
        if (LayersButton.IsChecked == true) BuildLayersMenu();
        LayersMenu.IsOpen = LayersButton.IsChecked == true;
    }

    /// <summary>
    /// Lit while open and out of the mouse's way (the click away that closes it
    /// would otherwise land on the button and open it again); the menu takes
    /// the keyboard, so Esc reaches it.
    /// </summary>
    private void LayersMenu_Opened(object? sender, EventArgs e)
    {
        LayersButton.IsChecked = true;
        LayersButton.IsHitTestVisible = false;
        _menuEscapeDown = false;
        Dispatcher.BeginInvoke(() => LayersMenuPanel.Focus(), DispatcherPriority.Input);
    }

    private void LayersMenu_Closed(object? sender, EventArgs e)
    {
        LayersButton.IsChecked = false;
        LayersButton.IsHitTestVisible = true;
        _menuEscapeDown = false;
    }

    /// <summary>Esc's press is the menu's: marked, and acted on at the release.</summary>
    private void LayersMenu_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (!e.IsRepeat) _menuEscapeDown = true;
        e.Handled = true;
    }

    private void LayersMenu_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !_menuEscapeDown) return;
        _menuEscapeDown = false;
        e.Handled = true;
        LayersMenu.IsOpen = false;
    }

    // The menu's four rows, kept so a click marks them afresh in place: a row
    // taken out of the tree inside its own Click would take the popup's mouse
    // capture with it.
    private ToggleButton? _islandRow, _centeredRow, _heatmapRow, _areasRow;

    /// <summary>The menu's rows, built as it opens (in the theme on screen), ticked as the minimap stands now.</summary>
    private void BuildLayersMenu()
    {
        LayersItems.Children.Clear();
        var now = _layers();
        var tick = (Style)FindResource("MenuTick");

        Caption("VIEW", now.ViewKey);
        _islandRow = Row("Island view", () => { if (_layers().Centered) _toggleMinimapView(); });
        _centeredRow = Row("Centered view", () => { if (!_layers().Centered) _toggleMinimapView(); });
        LayersItems.Children.Add(new Border { Height = 1, Background = Theme.Current.Separator.ToBrush(), Margin = new Thickness(4, 3, 4, 3) });
        Caption("LAYERS", null);
        _heatmapRow = Row("Heatmap", _toggleHeatmap, now.HeatmapKey);
        _areasRow = Row("Areas", _toggleAreaBorders);
        MarkLayersMenu();

        void Caption(string text, string? key)
        {
            var line = new DockPanel { Margin = new Thickness(12, 3, 10, 2) };
            if (key is { Length: > 0 })
            {
                var hotkey = Small(key, Theme.Current.Text.Dim, Theme.Current.Font.BodyWeight);
                DockPanel.SetDock(hotkey, Dock.Right);
                line.Children.Add(hotkey);
            }
            line.Children.Add(Small(text, Theme.Current.Text.Caption, Theme.Current.Font.CaptionWeight));
            LayersItems.Children.Add(line);
        }

        ToggleButton Row(string label, Action act, string? key = null)
        {
            // The label and its hotkey across what the tick's padding and ✓ column leave.
            var content = new DockPanel { Width = FrameContract.LayersRowWidth, LastChildFill = true };
            if (key is { Length: > 0 })
            {
                var hotkey = Small(key, Theme.Current.Text.Dim, Theme.Current.Font.BodyWeight);
                hotkey.Margin = new Thickness(FrameContract.LayersKeyGap, 0, 0, 0);
                DockPanel.SetDock(hotkey, Dock.Right);
                content.Children.Add(hotkey);
            }
            content.Children.Add(new TextBlock { Text = label, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            var row = new ToggleButton { Content = content, Style = tick };
            row.Click += (_, _) =>
            {
                act();
                MarkLayersMenu(); // the ticks as the minimap now is; the menu stays open
            };
            LayersItems.Children.Add(row);
            return row;
        }
    }

    /// <summary>Each row ticked as the minimap stands; greyed while it is hidden (nothing to switch).</summary>
    private void MarkLayersMenu()
    {
        var now = _layers();
        foreach (var (row, on) in new[] { (_islandRow, !now.Centered), (_centeredRow, now.Centered), (_heatmapRow, now.Heatmap), (_areasRow, now.Areas) })
        {
            if (row is null) continue;
            row.IsChecked = on;
            row.IsEnabled = now.MinimapShown;
        }
    }

    private static TextBlock Small(string text, ThemeColor colour, ThemeWeight weight) => new()
    {
        Text = text, FontSize = Theme.Current.Size(10), Foreground = colour.ToBrush(), FontWeight = weight.ToFontWeight(),
        VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
    };
}

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.Windows.Shapes.Path;

namespace PandoraOverlay;

/// <summary>
/// MinimapWindow, menu part: the edit-mode right-click map menu, marker hit
/// testing and hover, clipboard share codes and the footer notice. Same
/// class as MinimapWindow.xaml.cs, split for reading; see CLAUDE.md.
/// </summary>
public partial class MinimapWindow
{
    // ---- Map menu (edit mode, right-click) ------------------------------------

    /// <summary>
    /// Opens on the button RELEASE, like a Windows context menu: opened on
    /// the press, the popup's own "click outside closes me" logic took the
    /// matching release as that outside click, so the menu only survived if
    /// the button was held until the cursor reached it. Right-click always
    /// means "menu here": with the menu open, a right-click elsewhere moves
    /// it, as Explorer does; left click, Escape or an entry closes it. (A
    /// timing rule that swallowed the dismissing click was tried and felt
    /// worse — whether the menu closed or moved depended on how long the
    /// button was held.)
    /// </summary>
    private void Window_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!EditMode || _poll.Calibration is not { } cal) return;
        var pos = e.GetPosition(MapHost);
        var size = MapHost.Width;
        if (pos.X < 0 || pos.Y < 0 || pos.X > size || pos.Y > size) return; // footer, not the map

        // Inverse of the calibration transform: panel point → map fraction → world cm.
        double fx, fy;
        if (_centered)
        {
            var mapSize = size * _zoom;
            fx = (pos.X - _mapTranslate.X) / mapSize;
            fy = (pos.Y - _mapTranslate.Y) / mapSize;
        }
        else
        {
            fx = pos.X / size;
            fy = pos.Y / size;
        }
        fx = Math.Clamp(fx, 0, 1);
        fy = Math.Clamp(fy, 0, 1);
        _menuWorld = ((fx * cal.MapSize - cal.OffsetX - cal.PinOffsetX) / cal.ScaleX,
                      ((1 - fy) * cal.MapSize - cal.OffsetY - cal.PinOffsetY) / cal.ScaleY);

        // On or near a marker, the spot IS that waypoint (or friend — they sit
        // above the waypoints, so they win) — the menu gains its own entries,
        // and "Copy this spot" shares it exactly, no aiming.
        _menuFriend = FriendHitTest(pos);
        _menuHit = _menuFriend is null ? HitTest(pos) : null;
        if (_menuFriend is { X: { } fx0, Y: { } fy0 }) _menuWorld = (fx0, fy0);
        else if (_menuHit is { } hit) _menuWorld = (hit.X, hit.Y);

        BuildMapMenu();
        MapMenu.IsOpen = true;
        // Focus the panel once the popup's window exists, so Escape reaches it
        // (a plain Popup doesn't close on Escape by itself, unlike ContextMenu).
        Dispatcher.BeginInvoke(() => MapMenuPanel.Focus(), DispatcherPriority.Input);
        e.Handled = true;
    }

    private void MapMenu_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        MapMenu.IsOpen = false;
        e.Handled = true;
    }

    /// <summary>In edit mode the footer names the marker under the cursor.</summary>
    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (!EditMode) return;
        var pos = e.GetPosition(MapHost);
        var friend = FriendHitTest(pos);
        var hit = friend is null ? HitTest(pos) : null;
        if (ReferenceEquals(hit, _hover) && ReferenceEquals(friend, _hoverFriend)) return;
        _hover = hit;
        _hoverFriend = friend;
        UpdateFooter();
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_hover is null && _hoverFriend is null) return;
        _hover = null;
        _hoverFriend = null;
        UpdateFooter();
    }

    /// <summary>The drawn waypoint within SnapRadius of a panel point, nearest first; the tracked one wins ties.</summary>
    private Waypoint? HitTest(Point pos)
    {
        Marker? best = null;
        var bestDistance = double.MaxValue;
        foreach (var m in _markers.Values)
        {
            if (m.Shape.Visibility != Visibility.Visible) continue;
            var d = Math.Max(Math.Abs(pos.X - m.Translate.X), Math.Abs(pos.Y - m.Translate.Y));
            if (d >= SnapRadius) continue;
            if (d < bestDistance || (d == bestDistance && m.Tracked))
            {
                best = m;
                bestDistance = d;
            }
        }
        return best?.Waypoint;
    }

    /// <summary>
    /// Rebuilds the menu for the current state. On a friend: track them, or
    /// drop a waypoint where they stand. On a marker: track / copy /
    /// remove that waypoint first. Always: "Waypoint here" (one click, the
    /// new one is auto-named and tracked), then share-a-spot: the clicked
    /// spot ("meet here"), my position ("come to me"), and Paste, which
    /// creates a waypoint named from the code.
    /// </summary>
    private void BuildMapMenu()
    {
        MapMenuItems.Children.Clear();
        var full = _library.IsFull;

        if (_menuFriend is { SteamId: { } friendId } friend)
        {
            var brush = FriendBrush(friend);
            var name = Short(FriendName(friend));
            var tracked = friendId == _config.TrackedFriendSteamId;
            AddMenuItem(tracked ? $"Untrack {name}" : $"Track {name}", brush, () => SetTrackedFriend(tracked ? null : friendId));
            AddMenuItem($"Waypoint at {name}", brush, () => AddWaypointAtFriend(friend), enabled: !full);
            AddMenuSeparator();
        }

        if (_menuHit is { } hit)
        {
            var brush = PaletteBrushes[WaypointPalette.Wrap(hit.Colour)];
            var name = Short(hit.Name);
            var tracked = hit.Id == _config.TrackedWaypointId;
            AddMenuItem(tracked ? $"Untrack {name}" : $"Track {name}", brush, () => SetTracked(tracked ? null : hit.Id));
            AddMenuItem($"Copy {name}", brush, () => CopyCode(ShareCode.Format(hit.X, hit.Y, hit.Name), "spot copied"));
            AddMenuItem($"Remove {name}", brush, () => RemoveWaypoint(hit));
            AddMenuSeparator();
        }

        AddMenuItem(full ? $"Library full ({WaypointLibrary.Capacity})" : "Waypoint here", null, AddWaypointHere, enabled: !full);
        AddMenuSeparator();
        AddMenuItem("Copy this spot", null, () =>
        {
            if (_menuWorld is { } spot) CopyCode(ShareCode.Format(spot.X, spot.Y), "spot copied");
        });
        AddMenuItem("Copy my position", null, () =>
        {
            if (_lastWorld is { } p) CopyCode(ShareCode.Format(p.X, p.Y), "position copied");
        }, enabled: _lastWorld is not null);
        AddMenuItem("Paste waypoint", null, PasteWaypoint, enabled: !full && ClipboardHasCode());
    }

    private void AddMenuItem(string text, Brush? dot, Action onClick, bool enabled = true)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        if (dot is not null)
        {
            content.Children.Add(new Ellipse
            {
                Width = 8, Height = 8, Fill = dot, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center
            });
        }
        content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });

        var button = new Button { Content = content, Style = (Style)FindResource("MenuButton"), IsEnabled = enabled };
        button.Click += (_, _) =>
        {
            MapMenu.IsOpen = false;
            onClick();
        };
        MapMenuItems.Children.Add(button);
    }

    private void AddMenuSeparator() =>
        MapMenuItems.Children.Add(new Border
        {
            Height = 1, Background = BorderLocked, Margin = new Thickness(4, 3, 4, 3)
        });

    private void AddWaypointHere()
    {
        if (_menuWorld is not { } spot) return;
        var wp = _library.Add(_library.NextName(), spot.X, spot.Y, _library.NextColour());
        if (wp is null)
        {
            ShowNotice("library full");
            return;
        }
        SetTracked(wp.Id);
        ShowNotice($"{Short(wp.Name)} added");
    }

    private void RemoveWaypoint(Waypoint wp)
    {
        if (wp.Id == _config.TrackedWaypointId) _config.TrackedWaypointId = null;
        _library.Remove(wp.Id); // Changed → OnLibraryChanged redraws
        ShowNotice($"{Short(wp.Name)} removed");
    }

    /// <summary>Tracks (or untracks with null); the tracked waypoint is what the footer follows and the ring marks.</summary>
    private void SetTracked(Guid? id)
    {
        _config.TrackedWaypointId = id; // persisted with the next Save()
        RebuildMarkers();
        UpdateWaypointVisual(_mapTranslate.X, _mapTranslate.Y, glide: null);
        UpdateFooter();
    }

    private static bool ClipboardHasCode()
    {
        try
        {
            return Clipboard.ContainsText() && ShareCode.TryParse(Clipboard.GetText(), out _, out _);
        }
        catch
        {
            return false; // the clipboard is a shared resource; another app can hold it briefly
        }
    }

    private void CopyCode(string code, string notice)
    {
        try
        {
            Clipboard.SetText(code);
            ShowNotice(notice);
        }
        catch
        {
            ShowNotice("couldn't reach the clipboard");
        }
    }

    /// <summary>A pasted code becomes a new, tracked waypoint, named from the code when it carries a name.</summary>
    private void PasteWaypoint()
    {
        string text;
        try
        {
            text = Clipboard.GetText();
        }
        catch
        {
            ShowNotice("couldn't reach the clipboard");
            return;
        }
        if (!ShareCode.TryParse(text, out var x, out var y, out var name))
        {
            ShowNotice("no position in the clipboard");
            return;
        }
        var wp = _library.Add(name.Length > 0 ? name : _library.NextName(), x, y, _library.NextColour());
        if (wp is null)
        {
            ShowNotice("library full");
            return;
        }
        SetTracked(wp.Id);
        ShowNotice($"{Short(wp.Name)} added");
    }

    /// <summary>A short footer message in place of the usual line; the next poll or the timer restores it.</summary>
    private void ShowNotice(string text)
    {
        _notice = text;
        _noticeTimer.Stop();
        _noticeTimer.Start();
        UpdateFooter();
    }

}

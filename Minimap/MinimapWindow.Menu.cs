using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace PandoraOverlay;

/// <summary>
/// MinimapWindow, menu part: the edit-mode right-click map menu (its entries
/// are MapMenuBuilder's, their actions MapActions', both shared with every
/// map), marker hit testing and hover, and the footer notice. Same class as
/// MinimapWindow.xaml.cs, split for reading; see CLAUDE.md.
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
        if (FractionAt(pos) is not { } spot) return; // footer, not the map
        var (fx, fy) = spot;
        (double X, double Y)? world = cal.ToWorld(fx, fy); // map fraction → world cm

        // On or near a marker, the spot IS that waypoint (or friend — they sit
        // above the waypoints, so they win) — the menu gains its own entries,
        // and "Copy this spot" shares it exactly, no aiming.
        var friend = FriendHitTest(pos);
        var hit = friend is null ? HitTest(pos) : null;
        if (friend is { X: { } friendX, Y: { } friendY }) world = (friendX, friendY);
        else if (hit is not null) world = (hit.X, hit.Y);

        _menuBuilder.Build(new MapMenuSpot(world, hit, friend));
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

    /// <summary>
    /// The map fraction (0–1) under a point of the map square, in either
    /// view; null outside the square (the footer). Past the map image's own
    /// edge in the centered view it clamps to that edge.
    /// </summary>
    private (double Fx, double Fy)? FractionAt(Point pos)
    {
        var view = _centered ? View(Map.MapOffset.X, Map.MapOffset.Y) : View();
        if (!view.InPanel(pos)) return null;
        var (fx, fy) = view.FractionAt(pos);
        return (Math.Clamp(fx, 0, 1), Math.Clamp(fy, 0, 1));
    }

    /// <summary>In edit mode the footer names the marker under the cursor, and the area pill the area under it.</summary>
    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (!EditMode) return;
        var pos = e.GetPosition(MapHost);
        SetHoverSpot(FractionAt(pos));
        var friend = FriendHitTest(pos);
        var hit = friend is null ? HitTest(pos) : null;
        if (ReferenceEquals(hit, _hover) && ReferenceEquals(friend, _hoverFriend)) return;
        _hover = hit;
        _hoverFriend = friend;
        UpdateFooter();
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        SetHoverSpot(null);
        if (_hover is null && _hoverFriend is null) return;
        _hover = null;
        _hoverFriend = null;
        UpdateFooter();
    }

    /// <summary>The drawn waypoint within SnapRadius of a panel point, nearest first; the tracked one wins ties.</summary>
    private Waypoint? HitTest(Point pos) => Map.WaypointAt(pos, SnapRadius);

    /// <summary>A short footer message in place of the usual line; the next poll or the timer restores it.</summary>
    private void ShowNotice(string text)
    {
        _notice = text;
        _noticeTimer.Stop();
        _noticeTimer.Start();
        UpdateFooter();
    }
}

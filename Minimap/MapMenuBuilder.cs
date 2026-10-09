using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PandoraOverlay;

/// <summary>What is under the cursor when a map's menu opens: the world spot (a marker's exact spot when the cursor is on one), and the friend or waypoint there.</summary>
public sealed record MapMenuSpot((double X, double Y)? World, Waypoint? Waypoint, FriendState? Friend);

/// <summary>
/// Fills a map's right-click menu, the same on every map; the map owns the
/// popup and says what is under the cursor. On a friend: track them, or
/// drop a waypoint where they stand. On a waypoint: track, copy or remove
/// it. Always: "Waypoint here" (auto-named and tracked), then share-a-spot:
/// this spot ("meet here"), my position ("come to me") and Paste, which
/// makes a waypoint named from the code. An entry closes the menu, then
/// acts through MapActions; its notice goes back to the map.
/// No name box in the popup (owner's call): a new waypoint is auto-named,
/// and names are typed on the Settings Waypoints page.
/// </summary>
public sealed class MapMenuBuilder
{
    private readonly Panel _items;
    private readonly Style _button;
    private readonly Brush _separator;
    private readonly MapActions _actions;
    private readonly Func<FriendState, Brush> _friendBrush;
    private readonly Func<(double X, double Y)?> _me;
    private readonly Action _close;
    private readonly Action<string> _notice;

    /// <param name="me">Your position when an entry is CLICKED (null: not in game), so "Copy my position" copies where you are then.</param>
    public MapMenuBuilder(Panel items, Style button, Brush separator, MapActions actions, Func<FriendState, Brush> friendBrush,
                          Func<(double X, double Y)?> me, Action close, Action<string> notice)
    {
        _items = items;
        _button = button;
        _separator = separator;
        _actions = actions;
        _friendBrush = friendBrush;
        _me = me;
        _close = close;
        _notice = notice;
    }

    public void Build(MapMenuSpot spot)
    {
        _items.Children.Clear();
        var full = _actions.LibraryFull;

        if (spot.Friend is { SteamId: { } friendId } friend)
        {
            var brush = _friendBrush(friend);
            var name = _actions.Short(_actions.FriendName(friend));
            var tracked = friendId == _actions.TrackedFriendSteamId;
            Item(tracked ? $"Untrack {name}" : $"Track {name}", brush, () => _actions.TrackFriend(tracked ? null : friendId));
            Item($"Waypoint at {name}", brush, () => Say(_actions.AddWaypointAtFriend(friend)), enabled: !full);
            Separator();
        }

        if (spot.Waypoint is { } hit)
        {
            var brush = MapBrushes.Of(hit.Colour);
            var name = _actions.Short(hit.Name);
            var tracked = hit.Id == _actions.TrackedWaypointId;
            Item(tracked ? $"Untrack {name}" : $"Track {name}", brush, () => _actions.Track(tracked ? null : hit.Id));
            Item($"Copy {name}", brush, () => Say(_actions.Copy(ShareCode.Format(hit.X, hit.Y, hit.Name), "spot copied")));
            Item($"Remove {name}", brush, () => Say(_actions.Remove(hit)));
            Separator();
        }

        Item(full ? $"Library full ({WaypointLibrary.Capacity})" : "Waypoint here", null, () =>
        {
            if (spot.World is { } at) Say(_actions.AddWaypoint(at.X, at.Y));
        }, enabled: !full);
        Separator();
        Item("Copy this spot", null, () =>
        {
            if (spot.World is { } at) Say(_actions.Copy(ShareCode.Format(at.X, at.Y), "spot copied"));
        });
        Item("Copy my position", null, () =>
        {
            if (_me() is { } p) Say(_actions.Copy(ShareCode.Format(p.X, p.Y), "position copied"));
        }, enabled: _me() is not null);
        Item("Paste waypoint", null, () => Say(_actions.Paste()), enabled: !full && MapActions.ClipboardHasCode());
    }

    private void Item(string text, Brush? dot, Action onClick, bool enabled = true)
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

        var button = new Button { Content = content, Style = _button, IsEnabled = enabled };
        button.Click += (_, _) =>
        {
            _close();
            onClick();
        };
        _items.Children.Add(button);
    }

    private void Separator() =>
        _items.Children.Add(new Border { Height = 1, Background = _separator, Margin = new Thickness(4, 3, 4, 3) });

    private void Say(string? notice)
    {
        if (notice is not null) _notice(notice);
    }
}

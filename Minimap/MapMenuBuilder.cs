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
/// it, and put it on the route. Always: "Waypoint here" (auto-named and
/// tracked) and "Add stop here", then with a route its own entries, then
/// share-a-spot: this spot ("meet here"), my position ("come to me") and
/// Paste, which makes a waypoint named from the code, and a route's paste when
/// the clipboard holds one. An entry closes the menu, then acts through
/// MapActions; its notice goes back to the map. The one exception is pasting
/// over a route you have, which asks first and keeps the menu open for its
/// answer: no Cancel brings the old route back.
/// No name box in the popup (owner's call): a new waypoint is auto-named,
/// and names are typed on the Settings Waypoints page.
/// </summary>
public sealed class MapMenuBuilder
{
    private readonly Panel _items;
    private readonly Style _button;
    private readonly Func<Brush> _separator; // read at each build, so a theme switch shows at the next opening
    private readonly MapActions _actions;
    private readonly Func<FriendState, Brush> _friendBrush;
    private readonly Func<(double X, double Y)?> _me;
    private readonly Action _close;
    private readonly Action<string> _notice;

    /// <param name="me">Your position when an entry is CLICKED (null: not in game), so "Copy my position" copies where you are then.</param>
    public MapMenuBuilder(Panel items, Style button, Func<Brush> separator, MapActions actions, Func<FriendState, Brush> friendBrush,
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
        var route = _actions.Route;

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

            // The stop this waypoint means: "Go to" it unless it already is the current one
            // (a paused route's current stop has Resume, below), "Remove from route" if it has one.
            var stop = _actions.StopOf(hit.Id);
            if (stop >= 0 && stop != route.Current) Item($"Go to {name}", brush, () => _actions.GoToStop(stop));
            Item("Add to route", brush, () => Say(_actions.AddToRoute(hit)), enabled: !route.IsFull);
            Item("Add as next stop", brush, () => Say(_actions.AddAsNextStop(hit)), enabled: !route.IsFull);
            if (stop >= 0) Item("Remove from route", brush, () => Say(_actions.RemoveFromRoute(hit)));
            Separator();
        }

        Item(full ? $"Library full ({WaypointLibrary.Capacity})" : "Waypoint here", null, () =>
        {
            if (spot.World is { } at) Say(_actions.AddWaypoint(at.X, at.Y));
        }, enabled: !full);
        Item("Add stop here", null, () =>
        {
            if (spot.World is { } at) Say(_actions.AddStopHere(at.X, at.Y));
        }, enabled: !full && !route.IsFull);
        Separator();
        if (!route.IsEmpty)
        {
            if (_actions.CurrentStopWaypoint is { } current)
            {
                if (_actions.RoutePaused) Item("Resume route", null, _actions.ResumeRoute);
                Item($"Skip {_actions.Short(current.Name)}", null, () => Say(_actions.SkipStop()));
            }
            Item("Copy route", null, () => Say(_actions.CopyRoute()));
            Separator();
        }
        Item("Copy this spot", null, () =>
        {
            if (spot.World is { } at) Say(_actions.Copy(ShareCode.Format(at.X, at.Y), "spot copied"));
        });
        Item("Copy my position", null, () =>
        {
            if (_me() is { } p) Say(_actions.Copy(ShareCode.Format(p.X, p.Y), "position copied"));
        }, enabled: _me() is not null);
        Item("Paste waypoint", null, () => Say(_actions.Paste()), enabled: !full && MapActions.ClipboardHasCode());
        if (_actions.ClipboardHasRoute())
        {
            void Paste() => Say(_actions.PasteRoute().Notice);
            if (route.IsEmpty) Item("Paste route", null, Paste);
            else ArmedItem("Paste route", "Replace your route?", Paste);
        }
    }

    /// <summary>An entry that asks first: its first click changes its words and leaves the menu open, its second closes the menu and acts.</summary>
    private void ArmedItem(string text, string armedText, Action onClick)
    {
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(label);
        var button = new Button { Content = content, Style = _button };
        var armed = false;
        button.Click += (_, _) =>
        {
            if (!armed)
            {
                armed = true;
                label.Text = armedText;
                return;
            }
            _close();
            onClick();
        };
        _items.Children.Add(button);
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
        _items.Children.Add(new Border { Height = 1, Background = _separator(), Margin = new Thickness(4, 3, 4, 3) });

    private void Say(string? notice)
    {
        if (notice is not null) _notice(notice);
    }
}

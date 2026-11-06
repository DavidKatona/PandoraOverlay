using System.Windows;
using System.Windows.Documents;

namespace PandoraOverlay;

/// <summary>
/// MinimapWindow, markers part: which library waypoints are drawn (the
/// WaypointVisibility policy) and against which view — the MapCanvas draws
/// them — then the footer and the heading/speed pill. Same class as
/// MinimapWindow.xaml.cs, split for reading; see CLAUDE.md.
/// </summary>
public partial class MinimapWindow
{
    // ---- Waypoint markers ---------------------------------------------------

    private Waypoint? TrackedWaypoint => _library.Find(_config.TrackedWaypointId);

    /// <summary>
    /// Which library entries are drawn under the WaypointVisibility policy:
    /// every visible one, only the tracked one, or the NearestCount visible
    /// ones closest to the player. The tracked one is always drawn — you
    /// asked to follow it — and so is every stop of your route you have not
    /// passed, hidden waypoints included.
    /// </summary>
    private List<Waypoint> ShownWaypoints()
    {
        var tracked = TrackedWaypoint;
        List<Waypoint> shown;
        switch (_config.WaypointVisibility)
        {
            case "tracked":
                shown = new List<Waypoint>();
                break;
            case "nearest" when _lastWorld is { } p:
                shown = _library.Items.Where(w => w.Visible)
                    .OrderBy(w => WaypointLibrary.Distance(w.X, w.Y, p.X, p.Y))
                    .Take(NearestCount).ToList();
                break;
            case "nearest":
                shown = _library.Items.Where(w => w.Visible).Take(NearestCount).ToList();
                break;
            default:
                shown = _library.Items.Where(w => w.Visible).ToList();
                break;
        }
        if (tracked is not null && !shown.Contains(tracked)) shown.Add(tracked);
        foreach (var stop in RouteDrawing.UpcomingWaypoints(_actions.Route, id => _library.Find(id)))
        {
            if (!shown.Contains(stop)) shown.Add(stop);
        }
        return shown;
    }

    private void OnLibraryChanged()
    {
        if (TrackedWaypoint is null && _config.TrackedWaypointId is not null) _config.TrackedWaypointId = null; // deleted elsewhere
        RebuildMarkers();
        UpdateWaypointVisual(Map.MapOffset.X, Map.MapOffset.Y, glide: null);
        RenderRouteLegs(); // a stop's colour or place may have changed
        UpdateFooter();
    }

    /// <summary>The route changed: its stops are drawn whatever the policy says, and the legs and the footer's count follow.</summary>
    private void OnRouteChanged()
    {
        RebuildMarkers();
        UpdateWaypointVisual(Map.MapOffset.X, Map.MapOffset.Y, glide: null);
        RenderRouteLegs();
        UpdateFooter();
    }

    /// <summary>The route's later legs, in map pixels: whenever the route, a stop or the map's rendered size changes. They need no position of yours, so they stay drawn when you are not in game.</summary>
    private void RenderRouteLegs() =>
        Map.ShowRouteLegs(_poll.Calibration is { } cal
            ? RouteDrawing.Legs(_actions.Route, id => _library.Find(id), cal, Map.MapSize)
            : Array.Empty<RouteLeg>());

    /// <summary>The tracked waypoint changed (MapActions): the ring, the edge indicator and the footer follow it.</summary>
    private void OnWaypointTracked()
    {
        RebuildMarkers();
        UpdateWaypointVisual(Map.MapOffset.X, Map.MapOffset.Y, glide: null);
        UpdateFooter();
    }

    /// <summary>Under the "nearest" policy the set follows the player: rebuild only when it actually changed.</summary>
    private void RebuildMarkersIfSetChanged()
    {
        if (Map.ShowsWaypoints(ShownWaypoints())) return;
        RebuildMarkers();
    }

    /// <summary>Recreates the markers for the policy's set: a dot per waypoint, the tracked one a ringed diamond (MapCanvas draws them).</summary>
    private void RebuildMarkers() => Map.ShowWaypoints(ShownWaypoints(), _config.TrackedWaypointId);

    /// <summary>
    /// Positions every marker for the given map translation (targets during
    /// a glide, current values otherwise). In the centered view the tracked
    /// marker clamps to the panel edge as a direction indicator; the others
    /// simply leave the panel.
    /// </summary>
    private void UpdateWaypointVisual(double mapTx, double mapTy, TimeSpan? glide)
    {
        _mapTargetX = mapTx; // the friend layer places against the same targets, now and when its roster changes
        _mapTargetY = mapTy;
        UpdateFriendVisual(mapTx, mapTy, glide);
        if (_poll.Calibration is not { } cal) return;
        Map.PlaceWaypoints(cal, _centered ? View(mapTx, mapTy) : View(), edgeIndicator: _centered, glide); // the island view's map never moves
    }

    /// <summary>The footer's subject: the tracked waypoint, else the nearest visible one; with metres.</summary>
    private (Waypoint Waypoint, double Meters)? FooterTarget()
    {
        if (_lastWorld is not { } p) return null;
        if (TrackedWaypoint is { } t) return (t, WaypointLibrary.Distance(t.X, t.Y, p.X, p.Y) / 100);
        return _library.Nearest(p.X, p.Y, w => w.Visible);
    }

    /// <summary>A name shortened for the footer and the maps' notices: 14 characters.</summary>
    internal static string Short(string name) =>
        name.Length <= FooterNameLength ? name : name[..(FooterNameLength - 1)] + "…";

    /// <summary>
    /// View mode (+ zoom when centered), then the tracked (else nearest)
    /// waypoint in its colour with name, distance and, while actually
    /// closing on it, the ETA at the current pace. While a route runs, its
    /// current stop also says which stop of how many ("Delta · 3/7 · 1.2 km").
    /// Hovering a marker in edit mode names that one instead; a notice
    /// (copied / pasted) replaces the line briefly.
    /// </summary>
    private void UpdateFooter()
    {
        ModeFooter.Inlines.Clear();
        if (_notice is not null)
        {
            ModeFooter.Inlines.Add(_notice);
            return;
        }

        ModeFooter.Inlines.Add(_centered ? $"centered · {_zoom:0.##}×" : "island view");

        // A friend first: the one under the cursor, else the tracked one while
        // they are on the map. Navigation only — name, distance, ETA — never
        // their stats; that line has one job.
        // (A stats line for the tracked friend was proposed and turned down by the owner, Sep 27 2026.)
        if ((_hoverFriend ?? TrackedFriend) is { X: { } fx, Y: { } fy } friend)
        {
            ModeFooter.Inlines.Add(" · ");
            ModeFooter.Inlines.Add(new Run("▲ " + Short(FriendName(friend))) { Foreground = FriendBrush(friend) });
            if (_lastWorld is { } me) AppendDistance(fx, fy, WaypointLibrary.Distance(fx, fy, me.X, me.Y) / 100);
            return;
        }

        Waypoint subject;
        double? meters;
        if (_hover is { } h)
        {
            subject = h;
            meters = _lastWorld is { } p ? WaypointLibrary.Distance(h.X, h.Y, p.X, p.Y) / 100 : null;
        }
        else if (FooterTarget() is { } target)
        {
            subject = target.Waypoint;
            meters = target.Meters;
        }
        else
        {
            return;
        }

        // The diamond AND the name in the waypoint's colour, then a separator
        // before the distance: "Waypoint 6 150 m" read as one number.
        ModeFooter.Inlines.Add(" · ");
        ModeFooter.Inlines.Add(new Run("◆ " + Short(subject.Name))
        {
            Foreground = MapBrushes.Of(subject.Colour)
        });
        if (_hover is null && _actions.RouteRunning && subject.Id == _actions.Route.CurrentStop)
        {
            ModeFooter.Inlines.Add($" · {_actions.Route.Current + 1}/{_actions.Route.Count}");
        }
        if (meters is not { } m) return;
        AppendDistance(subject.X, subject.Y, m);
    }

    /// <summary>" · 150 m" (or km) and, while actually closing on the point, " · ~4 min" at the current pace.</summary>
    private void AppendDistance(double targetX, double targetY, double meters)
    {
        var text = meters >= 1000 ? $" · {meters / 1000:0.0} km" : $" · {meters:0} m";
        if (_speed.ClosingMps(targetX, targetY) is { } closing && closing >= MinClosingMps)
        {
            text += $" · ~{FormatEta(TimeSpan.FromSeconds(meters / closing))}";
        }
        ModeFooter.Inlines.Add(text);
    }

    private static string FormatEta(TimeSpan eta) =>
        eta.TotalMinutes < 1 ? "<1 min"
        : eta.TotalHours < 1 ? $"{eta.TotalMinutes:0} min"
        : $"{(int)eta.TotalHours} h {eta.Minutes:00} min";

    /// <summary>Bottom-right pill: compass letter from the arrow's heading, and km/h once the speed is known.</summary>
    /// <remarks>
    /// The arrow's heading is body yaw plus MinimapYawOffsetDegrees, so the
    /// letter says where the dino's BODY faces, not the free-look camera.
    /// </remarks>
    private void UpdateSpeedPill()
    {
        if (!_config.MinimapSpeedEnabled || _lastFix is not { } fix)
        {
            SpeedPanel.Visibility = Visibility.Collapsed;
            return;
        }
        var heading = Compass.Letter(fix.Yaw);
        SpeedText.Text = _speed.SpeedMps is { } mps ? $"{heading} · {mps * 3.6:0} km/h" : heading;
        SpeedPanel.Visibility = Visibility.Visible;
    }

}

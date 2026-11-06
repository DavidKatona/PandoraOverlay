namespace PandoraOverlay;

/// <summary>
/// MainWindow, route part: it owns the route, as it owns what else must outlive
/// a widget being hidden, so a route moves on with the minimap switched off or
/// the overlay hidden. Fed from the poll stream, no request of its own. What a
/// stop reached does: a line in the Activity feed (always; your own events are
/// yours), a chime if ticked, and a notice in the maps' footers. Same class as
/// MainWindow.xaml.cs, split for reading; see CLAUDE.md.
/// </summary>
public partial class MainWindow
{
    private Route _route = null!; // loaded in the constructor, right after the library it refers to

    /// <summary>Loads the route and drops stops whose waypoint is gone (deleted while the overlay was older, or a hand-edited file). User content: saved on every change.</summary>
    private void LoadRoute()
    {
        _route = Route.Load();
        _route.KeepOnly(id => _library.Find(id) is not null);
        _route.Changed += () => _route.Save();
    }

    /// <summary>Per poll, before any map draws it: the route and the tracking move on from your position.</summary>
    private void UpdateRoute()
    {
        if (_me is not { } me)
        {
            MapActions.RouteOutOfGame();
            return;
        }
        var arrivals = MapActions.RouteAt(me.X, me.Y);
        if (arrivals.Count == 0) return;

        var now = DateTime.UtcNow;
        foreach (var arrival in arrivals)
        {
            AppLog.Maps.Log($"Route: stop {arrival.StopNumber} of {arrival.StopCount} reached");
            if (_library.Find(arrival.Waypoint) is { } waypoint)
            {
                _log.Post(SelfActivity.RouteLine(waypoint.Name, arrival.StopNumber, arrival.StopCount, arrival.Last, now));
            }
        }
        // One chime however many stops one position reached.
        if (_config.RouteChimeEnabled) Chime();
        MapActions.SayReached(arrivals);
    }
}

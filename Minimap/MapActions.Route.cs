using System.Windows;

namespace PandoraOverlay;

/// <summary>
/// MapActions, route part: what the route's menu entries and its Settings page
/// DO, and the one place that keeps the tracking in step with the route. The
/// current stop IS the tracked waypoint, so the ring, the edge indicator, the
/// footer and the line from your arrow all follow it with nothing new, and the
/// route runs exactly while that is so (Route.IsRunning): track something else
/// and it waits, track the stop again and it goes on. MainWindow owns the route
/// and feeds this part each in-game position; the maps redraw on Route.Changed
/// and on WaypointTracked. Same class as MapActions.cs, split for reading.
/// </summary>
public sealed partial class MapActions
{
    private readonly Route _route;
    private readonly RouteTracker _tracker = new();

    // Whether the route was running before the latest change. Read when a stop is deleted:
    // by then the tracking has already been cleared (Remove, Settings' Delete), so asking
    // now would call a running route paused, and the next stop would not take over. The
    // tracking's own changes keep it fresh, and so does every poll, for the ways Settings
    // sets the tracking directly.
    private bool _routeWasRunning;

    // Where you were at the last in-game poll, for the Route page's distances.
    private (double X, double Y)? _position;

    /// <summary>The player's route.</summary>
    public Route Route => _route;

    /// <summary>A short message for the maps' footers from outside a menu: a stop reached.</summary>
    public event Action<string>? Notice;

    /// <summary>The current stop's waypoint, for the menu's and the page's words.</summary>
    public Waypoint? CurrentStopWaypoint => _library.Find(_route.CurrentStop);

    public bool RouteRunning => _route.IsRunning(_config.TrackedWaypointId);

    public bool RoutePaused => _route.IsPaused(_config.TrackedWaypointId);

    /// <summary>
    /// Which stop of the route a waypoint means, for the menu: its first
    /// upcoming stop, else the last one you passed (a repeated waypoint has
    /// several, and the next one is the one you mean); -1 if it isn't on the route.
    /// </summary>
    public int StopOf(Guid waypoint)
    {
        for (var i = _route.Current; i < _route.Count; i++)
        {
            if (_route.Stops[i] == waypoint) return i;
        }
        for (var i = Math.Min(_route.Current, _route.Count) - 1; i >= 0; i--)
        {
            if (_route.Stops[i] == waypoint) return i;
        }
        return -1;
    }

    // ---- The position -------------------------------------------------------------

    /// <summary>One in-game position: moves the route and the tracking on, and returns the stops it reached.</summary>
    public IReadOnlyList<RouteArrival> RouteAt(double x, double y)
    {
        _position = (x, y);
        _routeWasRunning = RouteRunning;
        var arrivals = _tracker.Step(_route, Place, x, y, _routeWasRunning);
        if (arrivals.Count > 0) TrackRouteStop();
        return arrivals;
    }

    /// <summary>Not in game: the next position has no path from the last one.</summary>
    public void RouteOutOfGame()
    {
        _position = null;
        _tracker.Forget();
    }

    /// <summary>Metres from where you are to the current stop, in a straight line; null without a position or a current stop.</summary>
    public double? RouteMetersToStop() =>
        _position is { } p && CurrentStopWaypoint is { } stop ? WaypointLibrary.Distance(p.X, p.Y, stop.X, stop.Y) / 100 : null;

    /// <summary>Metres from where you are through every stop left, in straight lines, the same way; null without a position or a current stop.</summary>
    public double? RouteMetersLeft()
    {
        if (_position is not { } from || !_route.HasCurrent) return null;
        var cm = 0.0;
        foreach (var id in _route.Upcoming)
        {
            if (_library.Find(id) is not { } stop) continue;
            cm += WaypointLibrary.Distance(from.X, from.Y, stop.X, stop.Y);
            from = (stop.X, stop.Y);
        }
        return cm / 100;
    }

    /// <summary>The footer's words for the stops one position reached.</summary>
    public void SayReached(IReadOnlyList<RouteArrival> arrivals)
    {
        if (arrivals.Count == 0) return;
        var last = arrivals[^1];
        Notice?.Invoke(last.Last ? "Route finished" : $"{_short(_library.Find(last.Waypoint)?.Name ?? "Stop")} reached");
    }

    private (double X, double Y)? Place(Guid id) => _library.Find(id) is { } w ? (w.X, w.Y) : null;

    // ---- Building -----------------------------------------------------------------

    /// <summary>"Add to route": at the end. The first stop, or one after the last, becomes current and is tracked.</summary>
    public string AddToRoute(Waypoint waypoint)
    {
        if (_route.IsFull) return "route full";
        RouteEdit(() => _route.Add(waypoint.Id));
        return $"{_short(waypoint.Name)} is stop {_route.Count}";
    }

    /// <summary>"Add as next stop": straight after the current one.</summary>
    public string AddAsNextStop(Waypoint waypoint)
    {
        if (_route.IsFull) return "route full";
        var number = _route.HasCurrent ? _route.Current + 2 : _route.Count + 1;
        RouteEdit(() => _route.AddNext(waypoint.Id));
        return $"{_short(waypoint.Name)} is stop {number}";
    }

    /// <summary>"Add stop here": a normal waypoint, named after its area, which stays in the library afterwards (the owner's call).</summary>
    public string AddStopHere(double x, double y)
    {
        if (_route.IsFull) return "route full";
        var waypoint = _library.Add(NewName(x, y), x, y, _library.NextColour());
        return waypoint is null ? "library full" : AddToRoute(waypoint);
    }

    /// <summary>Takes one stop off the route, never the waypoint off the library. The next stop takes over if it was the current one.</summary>
    public void RemoveStop(int index) => RouteEdit(() => _route.RemoveAt(index));

    /// <summary>"Remove from route": the stop that waypoint means (<see cref="StopOf"/>).</summary>
    public string RemoveFromRoute(Waypoint waypoint)
    {
        RemoveStop(StopOf(waypoint.Id));
        return $"{_short(waypoint.Name)} left the route";
    }

    /// <summary>A stop dragged to a new place. Landing on the current stop's place makes it the current one.</summary>
    public void MoveStop(int from, int to) => RouteEdit(() => _route.Move(from, to));

    /// <summary>"Clear route": the waypoints stay; the tracking goes only if it was the route's.</summary>
    public string ClearRoute()
    {
        RouteEdit(() => _route.Clear());
        return "route cleared";
    }

    // ---- Walking ------------------------------------------------------------------

    /// <summary>
    /// A stop picked by hand: "Go to", a row on the page. Those above it read
    /// as passed, those below as upcoming, and it counts only once you have
    /// been outside it, so this also undoes an accidental pass (RouteTracker).
    /// </summary>
    public void GoToStop(int index)
    {
        if (index < 0 || index >= _route.Count) return;
        if (index == _route.Current)
        {
            ResumeRoute();
            return;
        }
        _route.GoTo(index);
        _tracker.HandPicked();
        TrackRouteStop();
    }

    /// <summary>"Skip": the current stop passes with no cue. A paused route stays paused.</summary>
    public string? SkipStop()
    {
        if (CurrentStopWaypoint is not { } skipped) return null;
        RouteEdit(() => _route.Skip());
        return $"{_short(skipped.Name)} skipped";
    }

    /// <summary>"Resume route": tracking the current stop is all a resume is. A resume is no hand pick: already standing in the stop, it counts.</summary>
    public void ResumeRoute() => TrackRouteStop();

    /// <summary>"Restart": walk it again from the first stop, running whatever it was doing before.</summary>
    public void RestartRoute()
    {
        _route.Restart();
        _tracker.StartOver();
        TrackRouteStop();
    }

    // ---- Sharing ------------------------------------------------------------------

    /// <summary>The notice for a route copied: the page says more in its own words when it sees this one.</summary>
    public const string RouteCopiedNotice = "route copied";

    /// <summary>Where a paste reads from. A property so the tests never touch the real clipboard, which is the player's.</summary>
    internal Func<string?> ReadClipboard { get; set; } = () => Clipboard.ContainsText() ? Clipboard.GetText() : null;

    /// <summary>The whole list as text, passed stops too: the receiver starts at stop 1 (PLAN.md, D8).</summary>
    internal string RouteCodes() => RouteCode.Format(_route.Stops.Select(id => _library.Find(id)).OfType<Waypoint>());

    /// <summary>"Copy route".</summary>
    public string CopyRoute() => Copy(RouteCodes(), RouteCopiedNotice);

    /// <summary>Whether the clipboard holds a route: the menu shows "Paste route" only then. The clipboard is a shared resource another app can hold.</summary>
    public bool ClipboardHasRoute()
    {
        try
        {
            return RouteCode.IsRoute(ReadClipboard());
        }
        catch
        {
            return false;
        }
    }

    /// <summary>"Paste route": a route from the clipboard takes your route's place. Asking first is the caller's: its second click.</summary>
    public RoutePaste PasteRoute()
    {
        string? text;
        try
        {
            text = ReadClipboard();
        }
        catch
        {
            return new RoutePaste(false, "couldn't reach the clipboard", "Couldn't reach the clipboard. Try again in a moment.");
        }
        return PasteRouteFrom(text);
    }

    /// <summary>
    /// A shared route becomes your route, from its first stop, and is tracked:
    /// pasting one is asking to walk it. Its stops arrive like a pack import (the
    /// owner's call): a waypoint of the same name within the packs' duplicate
    /// distance is reused, so a route you already have the places for adds nothing;
    /// the rest arrive hidden, under one band the Waypoints page can delete at once,
    /// taking the library's next colours since the code carries none. A code with no
    /// name has no name to compare, so any waypoint on that spot is the one it means.
    /// <para>
    /// All or nothing: the arrivals are worked out on a copy, so a library with no
    /// room for them changes nothing, and half a route never arrives. And one
    /// Changed for them all: a save and a redraw per stop would be 64 of each.
    /// </para>
    /// </summary>
    internal RoutePaste PasteRouteFrom(string? text)
    {
        var parsed = RouteCode.Parse(text);
        if (parsed.Count < 2)
        {
            return new RoutePaste(false, "no route in the clipboard", "There's no route in the clipboard. A route is two or more spot codes, one per line.");
        }

        var stops = parsed.Take(Route.Capacity).ToList();
        var scratch = new WaypointLibrary();
        scratch.ReplaceWith(_library.Clone());
        var had = _library.Items.Select(w => w.Id).ToHashSet();
        var ids = new List<Guid>();
        var noRoom = 0;
        foreach (var stop in stops)
        {
            var waypoint = FindPastedStop(scratch, stop);
            if (waypoint is null)
            {
                var name = stop.Name.Length > 0 ? stop.Name : scratch.NextName(_areaAt?.Invoke(stop.X, stop.Y));
                waypoint = scratch.Add(name, stop.X, stop.Y, scratch.NextColour());
                if (waypoint is null)
                {
                    noRoom++;
                    continue;
                }
                waypoint.Visible = false;
                waypoint.Pack = RouteCode.SharedPack;
            }
            ids.Add(waypoint.Id);
        }
        var added = scratch.Count - _library.Count;
        if (noRoom > 0)
        {
            return new RoutePaste(false, "library full",
                $"Your waypoint library can't hold it: {added + noRoom} new waypoints, room for {WaypointLibrary.Capacity - _library.Count}. Nothing changed.");
        }

        var reused = ids.Distinct().Count(had.Contains);
        _library.ReplaceWith(scratch.Items.ToList());
        _route.ReplaceWith(ids);
        _tracker.StartOver();
        TrackRouteStop();
        return new RoutePaste(true,
            parsed.Count > stops.Count ? $"first {stops.Count} stops pasted" : $"route pasted · {stops.Count} stops",
            RouteCode.PastedWords(stops.Count, added, reused, parsed.Count));
    }

    private static Waypoint? FindPastedStop(WaypointLibrary library, RouteCode.Stop stop)
    {
        bool Near(Waypoint w) => WaypointLibrary.Distance(w.X, w.Y, stop.X, stop.Y) / 100 <= WaypointPacks.DuplicateMeters;
        if (stop.Name.Length == 0) return library.Items.Where(Near).MinBy(w => WaypointLibrary.Distance(w.X, w.Y, stop.X, stop.Y));
        var name = WaypointLibrary.SanitizeName(stop.Name);
        return library.Items.FirstOrDefault(w => Near(w) && string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    // ---- Keeping the tracking in step ----------------------------------------------

    /// <summary>
    /// A change to the route. A running route keeps running, so the tracking
    /// follows whichever stop is current now, and is dropped if none is. A route
    /// that had no current stop and has one now starts running, replacing
    /// whatever was tracked, as "Waypoint here" does. A paused route is left
    /// paused: the tracking is yours until you resume.
    /// </summary>
    private void RouteEdit(Action edit)
    {
        var wasRunning = RouteRunning;
        var hadCurrent = _route.HasCurrent;
        edit();
        if (wasRunning || (!hadCurrent && _route.HasCurrent)) TrackRouteStop();
    }

    private void TrackRouteStop()
    {
        if (_config.TrackedWaypointId != _route.CurrentStop) Track(_route.CurrentStop);
        _routeWasRunning = RouteRunning;
    }

    /// <summary>A waypoint deleted from the library leaves the route, every repeat of it; if it was the running route's current stop, the next one takes over.</summary>
    private void OnLibraryChanged()
    {
        var before = _route.CurrentStop;
        _route.KeepOnly(id => _library.Find(id) is not null);
        if (_routeWasRunning && _route.CurrentStop != before) TrackRouteStop();
        // Settings writes the tracking directly, just before this: read it now, or a stop
        // deleted after a Track click there (with no poll since, out of game) would take the line.
        _routeWasRunning = RouteRunning;
    }
}

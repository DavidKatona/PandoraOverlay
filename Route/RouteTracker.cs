namespace PandoraOverlay;

/// <summary>One stop reached: which stop of how many, and whether it was the last.</summary>
public sealed record RouteArrival(int StopNumber, int StopCount, Guid Waypoint, bool Last);

/// <summary>
/// Decides, from the position the poll already gives, whether the route moves
/// on: pure, fed by MainWindow, with no timer and no request of its own.
/// <para>
/// <b>Reached</b> means within <see cref="ReachCm"/> on the map (X and Y only),
/// at a position or anywhere on the straight path between two positions: a
/// fast dino covers tens of metres between 3 s polls and could run through a
/// circle without ever being seen in it. A path counts only between two
/// consecutive in-game positions a dino could have travelled
/// (<see cref="BreadcrumbTrail.CouldTravel"/>), so a respawn or a teleport
/// never passes the stops along its line. It still counts if you land in the
/// circle: you are there.
/// </para>
/// <para>
/// <b>The current stop</b> counts whenever you are in its circle, even if you
/// already were when it became current (standing at a stop you just added, or
/// at the first stop of a route you restarted). <b>The one exception is a stop
/// you picked by hand</b>: it counts only once you have been outside its
/// circle. A hand pick is the undo for an accidental pass, and undo would undo
/// nothing if the stop passed again while you still stood in it.
/// </para>
/// <para>
/// <b>Skipping:</b> reaching the next-in-line passes the current stop too
/// (the owner's rule; the stop after that does nothing). The next-in-line
/// counts only on ENTERING its circle, never for standing in it, and that is
/// not a nicety. On water, mud, water, reaching the first water makes mud
/// current and the second water next-in-line, and you are standing in it: mud
/// would be skipped and the route finished on the spot. Adding a stop where you
/// stand would skip the current stop the same way. So a stop must be seen
/// outside its circle after it becomes next-in-line before it can count.
/// </para>
/// <para>
/// Paused, finished or empty: nothing is checked, though the position is still
/// kept, so that a resume has a path to work from.
/// </para>
/// </summary>
public sealed class RouteTracker
{
    /// <summary>About 50 m, in world cm. A fixed number (the owner's call): no setting, adjusted after trying it in game if it feels wrong.</summary>
    public const double ReachCm = 5000;

    private (DateTime At, double X, double Y)? _last;
    private bool _currentMustLeave;
    private Guid? _watchedNext;
    private bool _nextSeenOutside;

    /// <summary>The player picked the current stop by hand: it counts only after they have been outside it, and so does the one after it.</summary>
    public void HandPicked()
    {
        _currentMustLeave = true;
        _watchedNext = null;
    }

    /// <summary>Not in game: the next position has no path from this one.</summary>
    public void Forget() => _last = null;

    public IReadOnlyList<RouteArrival> Step(Route route, Func<Guid, (double X, double Y)?> place, double x, double y, bool running) =>
        Step(route, place, x, y, running, DateTime.UtcNow);

    /// <summary>
    /// One in-game position. Marks what it reached on the route (one Changed,
    /// however many stops passed) and returns them in order, usually none.
    /// </summary>
    /// <param name="place">Where a waypoint is; null for one no longer in the library.</param>
    /// <param name="running">Whether the route is running (<see cref="Route.IsRunning"/>).</param>
    internal IReadOnlyList<RouteArrival> Step(Route route, Func<Guid, (double X, double Y)?> place, double x, double y, bool running, DateTime now)
    {
        var from = PathStart(x, y, now);
        _last = (now, x, y);

        if (!running)
        {
            // A resume is not a hand pick, and what you walked into meanwhile must be entered again to skip anything.
            _currentMustLeave = false;
            _watchedNext = null;
            return Array.Empty<RouteArrival>();
        }

        var arrivals = new List<RouteArrival>();
        var stops = route.Stops;
        var index = route.Current;
        while (index < stops.Count && place(stops[index]) is { } current)
        {
            if (CurrentReached(current, from, x, y))
            {
                arrivals.Add(new RouteArrival(index + 1, stops.Count, stops[index], index == stops.Count - 1));
                index++;
                continue; // close stops can pass together, on the same path
            }
            if (!NextReached(index + 1 < stops.Count ? stops[index + 1] : null, place, from, x, y)) break;

            // The next-in-line: the current stop passes with no cue of its own, the one reached with one.
            arrivals.Add(new RouteArrival(index + 2, stops.Count, stops[index + 1], index + 1 == stops.Count - 1));
            index += 2;
            _currentMustLeave = false;
        }
        route.PassUpTo(index);
        return arrivals;
    }

    /// <summary>Where this step's path starts: the last position if a dino could have come from there, else here (only this position counts).</summary>
    private (double X, double Y) PathStart(double x, double y, DateTime now)
    {
        if (_last is not { } seen) return (x, y);
        var travelled = Distance(seen.X, seen.Y, x, y);
        return BreadcrumbTrail.CouldTravel(travelled, (now - seen.At).TotalSeconds) ? (seen.X, seen.Y) : (x, y);
    }

    private bool CurrentReached((double X, double Y) stop, (double X, double Y) from, double x, double y)
    {
        if (_currentMustLeave)
        {
            // Not this step, even on the way out: the path leaving the circle starts inside it.
            if (Distance(stop.X, stop.Y, x, y) > ReachCm) _currentMustLeave = false;
            return false;
        }
        return DistanceToPath(stop, from, x, y) <= ReachCm;
    }

    private bool NextReached(Guid? next, Func<Guid, (double X, double Y)?> place, (double X, double Y) from, double x, double y)
    {
        if (next != _watchedNext)
        {
            _watchedNext = next;
            _nextSeenOutside = false;
        }
        if (next is not { } id || place(id) is not { } stop) return false;
        if (!_nextSeenOutside)
        {
            _nextSeenOutside = Distance(stop.X, stop.Y, x, y) > ReachCm;
            return false;
        }
        return DistanceToPath(stop, from, x, y) <= ReachCm;
    }

    /// <summary>The shortest distance from a stop to the straight path between two positions.</summary>
    internal static double DistanceToPath((double X, double Y) stop, (double X, double Y) from, double toX, double toY)
    {
        var (dx, dy) = (toX - from.X, toY - from.Y);
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= 0) return Distance(stop.X, stop.Y, toX, toY);
        var along = Math.Clamp(((stop.X - from.X) * dx + (stop.Y - from.Y) * dy) / lengthSquared, 0, 1);
        return Distance(stop.X, stop.Y, from.X + along * dx, from.Y + along * dy);
    }

    private static double Distance(double x1, double y1, double x2, double y2) => WaypointLibrary.Distance(x1, y1, x2, y2);
}

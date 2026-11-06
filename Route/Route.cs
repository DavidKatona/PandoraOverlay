using System.IO;
using System.Text.Json;

namespace PandoraOverlay;

/// <summary>
/// The player's one route (the owner's feature, Nov 2026; the plan and every
/// decision: Desktop\Pandora Overlay Files\pandora-route\PLAN.md): an ordered
/// list of library waypoints, walked like stops on a GPS, and the index of the
/// current stop. Everything before the index is passed, an index past the end
/// means finished. An index and not a waypoint id, because the same waypoint
/// may be on a route twice (water, mud, water; the owner's call).
/// <para>
/// A reached stop is marked passed, never removed (the owner's call: a route
/// can be walked again, and nothing is deleted behind your back), and the
/// waypoint stays in the library whatever happens here. Reordering touches
/// upcoming stops only: what is behind you stays behind you.
/// </para>
/// <para>
/// The current stop IS the tracked waypoint, so this class holds no running or
/// paused state: <see cref="IsRunning"/> reads it from the tracking. Nothing
/// here knows about tracking otherwise, the maps, or the library: kept in
/// route.json beside waypoints.json, user content apart from config and the
/// cookie vault. Every mutation validates, then raises Changed; the owner
/// (MainWindow) saves on it. Fail-soft like the library: nothing in this class
/// throws, except the internal FromJson on text that isn't JSON (Load guards it).
/// </para>
/// </summary>
public sealed class Route
{
    // Far more than a real trip, and it keeps the page's list and the maps' lines light (the owner's call).
    // A limit is needed whatever its size: repeats count, so the library's 256 would not bound it.
    public const int Capacity = 64;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string FilePath { get; } = DataFolder.FileIn("route.json");

    private readonly List<Guid> _stops = new();

    /// <summary>The waypoint of each stop, in walking order.</summary>
    public IReadOnlyList<Guid> Stops => _stops;

    public int Count => _stops.Count;
    public bool IsEmpty => _stops.Count == 0;
    public bool IsFull => _stops.Count >= Capacity;

    /// <summary>The index of the current stop; <see cref="Count"/> when the route is finished or empty.</summary>
    public int Current { get; private set; }

    public bool HasCurrent => Current < _stops.Count;
    public bool IsFinished => _stops.Count > 0 && Current >= _stops.Count;

    public Guid? CurrentStop => HasCurrent ? _stops[Current] : null;
    public Guid? NextStop => Current + 1 < _stops.Count ? _stops[Current + 1] : null;

    /// <summary>The stops not yet passed, the current one first: what the maps draw.</summary>
    public IEnumerable<Guid> Upcoming => _stops.Skip(Current);

    /// <summary>Raised after any change to the list or the current stop.</summary>
    public event Action? Changed;

    /// <summary>The route runs only while its current stop is the tracked waypoint (the owner's rule): no state of its own to keep in step.</summary>
    public bool IsRunning(Guid? tracked) => HasCurrent && CurrentStop == tracked;

    /// <summary>There is a current stop, and something else (or nothing) is tracked. A finished or empty route is not paused.</summary>
    public bool IsPaused(Guid? tracked) => HasCurrent && CurrentStop != tracked;

    public bool Contains(Guid waypoint) => _stops.Contains(waypoint);

    // ---- Persistence --------------------------------------------------------

    public static Route Load()
    {
        try
        {
            if (File.Exists(FilePath)) return FromJson(File.ReadAllText(FilePath));
        }
        catch (Exception ex)
        {
            // Corrupt or unreadable: no route rather than a crash, and only the next change rewrites the file.
            AppLog.Config.Warning($"route.json could not be read ({AppLog.Short(ex)}): started with no route; it is replaced at the next change");
        }
        return new Route();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath, ToJson());
        }
        catch (Exception ex)
        {
            // Non-fatal: the route in memory keeps working.
            AppLog.Config.Warning($"route.json could not be saved: {AppLog.Short(ex)}");
        }
    }

    /// <summary>Parses a route file, dropping anything invalid. Throws on text that isn't JSON at all; Load guards it.</summary>
    internal static Route FromJson(string json)
    {
        var route = new Route();
        var file = JsonSerializer.Deserialize<RouteFile>(json);
        if (file?.Stops is null) return route;

        foreach (var id in file.Stops)
        {
            if (route.IsFull) break;
            if (id != Guid.Empty) route._stops.Add(id);
        }
        route.Current = Math.Clamp(file.Current, 0, route._stops.Count);
        return route;
    }

    internal string ToJson() =>
        JsonSerializer.Serialize(new RouteFile { Format = 1, Current = Current, Stops = _stops }, JsonOpts);

    private sealed class RouteFile
    {
        public int Format { get; set; } = 1;
        public int Current { get; set; }
        public List<Guid>? Stops { get; set; }
    }

    // ---- Mutations (each validates, then raises Changed) ---------------------

    /// <summary>
    /// A stop at the end. On an empty or finished route the index already
    /// points one past the last stop, so the new stop is the current one: a
    /// finished route simply carries on. False when the route is full.
    /// </summary>
    public bool Add(Guid waypoint)
    {
        if (IsFull || waypoint == Guid.Empty) return false;
        _stops.Add(waypoint);
        Notify();
        return true;
    }

    /// <summary>A stop straight after the current one; on an empty or finished route the same as <see cref="Add"/>.</summary>
    public bool AddNext(Guid waypoint)
    {
        if (!HasCurrent) return Add(waypoint);
        if (IsFull || waypoint == Guid.Empty) return false;
        _stops.Insert(Current + 1, waypoint);
        Notify();
        return true;
    }

    /// <summary>Takes one stop off the route (never off the library). If it was the current stop, the next one takes over.</summary>
    public void RemoveAt(int index)
    {
        if (index < 0 || index >= _stops.Count) return;
        _stops.RemoveAt(index);
        if (index < Current) Current--;
        Notify();
    }

    /// <summary>Keeps only the stops whose waypoint passes the test: a waypoint deleted from the library leaves the route, every repeat of it, and the next stop takes over.</summary>
    public void KeepOnly(Func<Guid, bool> exists)
    {
        var removed = false;
        for (var i = _stops.Count - 1; i >= 0; i--)
        {
            if (exists(_stops[i])) continue;
            _stops.RemoveAt(i);
            if (i < Current) Current--;
            removed = true;
        }
        if (removed) Notify();
    }

    /// <summary>
    /// Moves an upcoming stop among the upcoming ones, the current one
    /// included: the index stays, so whichever stop lands on it is the current
    /// stop (drag one to the top of the upcoming and you go there next).
    /// Anything touching a passed stop is ignored.
    /// </summary>
    public void Move(int from, int to)
    {
        if (from == to || from < Current || to < Current || from >= _stops.Count || to >= _stops.Count) return;
        var stop = _stops[from];
        _stops.RemoveAt(from);
        _stops.Insert(to, stop);
        Notify();
    }

    /// <summary>Makes any stop the current one, so those above it read as passed and those below as upcoming: the undo for an accidental pass or a wrong skip.</summary>
    public void GoTo(int index)
    {
        if (index < 0 || index >= _stops.Count || index == Current) return;
        Current = index;
        Notify();
    }

    /// <summary>Passes the current stop by hand.</summary>
    public void Skip()
    {
        if (!HasCurrent) return;
        Current++;
        Notify();
    }

    /// <summary>Walk it again from the first stop.</summary>
    public void Restart()
    {
        if (Current == 0) return;
        Current = 0;
        Notify();
    }

    public void Clear()
    {
        if (_stops.Count == 0 && Current == 0) return;
        _stops.Clear();
        Current = 0;
        Notify();
    }

    /// <summary>A pasted route: these stops, from the first; the first <see cref="Capacity"/> if there are more.</summary>
    public void ReplaceWith(IEnumerable<Guid> stops)
    {
        _stops.Clear();
        _stops.AddRange(stops.Where(id => id != Guid.Empty).Take(Capacity));
        Current = 0;
        Notify();
    }

    /// <summary>RouteTracker's step: every stop before this index is passed. One Changed however many stops passed together.</summary>
    internal void PassUpTo(int index)
    {
        var clamped = Math.Clamp(index, 0, _stops.Count);
        if (clamped == Current) return;
        Current = clamped;
        Notify();
    }

    private void Notify() => Changed?.Invoke();
}

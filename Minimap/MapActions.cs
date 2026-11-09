using System.Windows;

namespace PandoraOverlay;

/// <summary>
/// What the map menu's entries DO, shared by every map: add, remove and
/// track waypoints, track a friend, copy and paste share codes. An action
/// returns the short notice its map shows in the footer (null: nothing to
/// say). Redrawing is the map's business: it hears the library's Changed,
/// and WaypointTracked / FriendTracked for the tracking (persisted with the
/// next config Save, as before). The clipboard is a shared resource another
/// app can hold, so every touch of it is guarded.
/// </summary>
public sealed partial class MapActions
{
    private readonly OverlayConfig _config;
    private readonly WaypointLibrary _library;
    private readonly FriendBook _book;
    private readonly Func<string, string> _short;
    private readonly Func<double, double, string?>? _areaAt;

    /// <param name="shortName">How the map shortens a name for its notices ("Forks added").</param>
    /// <param name="areaAt">World cm → the named area there, or null (out at sea, or nothing to place it with yet). New waypoints are named after it; without it they are "Waypoint N".</param>
    /// <param name="route">The player's route (MainWindow's); null = an empty one of its own, for a map built alone.</param>
    public MapActions(OverlayConfig config, WaypointLibrary library, FriendBook book, Func<string, string> shortName,
                      Func<double, double, string?>? areaAt = null, Route? route = null)
    {
        _config = config;
        _library = library;
        _book = book;
        _short = shortName;
        _areaAt = areaAt;
        _route = route ?? new Route();
        _routeWasRunning = RouteRunning;
        _library.Changed += OnLibraryChanged;
    }

    /// <summary>The tracked waypoint changed: the ring, the edge indicator and the footer follow it.</summary>
    /// <remarks>Separate from FriendTracked on purpose: tracking one must not redraw — and so snap — the other's markers.</remarks>
    public event Action? WaypointTracked;

    /// <summary>The tracked friend changed: the ring, the edge indicator and the footer's first claim follow them.</summary>
    public event Action? FriendTracked;

    public Guid? TrackedWaypointId => _config.TrackedWaypointId;

    public string? TrackedFriendSteamId => _config.TrackedFriendSteamId;

    public bool LibraryFull => _library.IsFull;

    public string Short(string name) => _short(name);

    /// <summary>The name you see for a friend: your nickname for them, else the site's.</summary>
    public string FriendName(FriendState friend) => _book.DisplayName(friend.SteamId, friend.Name);

    /// <summary>Tracks a waypoint, or untracks with null.</summary>
    public void Track(Guid? id)
    {
        _config.TrackedWaypointId = id;
        _routeWasRunning = RouteRunning;
        WaypointTracked?.Invoke();
    }

    /// <summary>Tracks a friend, or untracks with null.</summary>
    public void TrackFriend(string? steamId)
    {
        _config.TrackedFriendSteamId = steamId;
        FriendTracked?.Invoke();
    }

    /// <summary>True when this friend is in the Pack.</summary>
    public bool Watches(string? steamId) => _book.Watches(steamId);

    /// <summary>True when this friend is watched or one more fits (the cap, FriendBook.MaxWatched): unwatching is never refused.</summary>
    public bool CanWatch(string? steamId) => _book.CanWatch(steamId);

    /// <summary>
    /// The maps' "Watch …" / "Unwatch …" (the Pack, Nov 2026): sets the book's
    /// entry, which raises its Changed (MainWindow saves the file and brings
    /// the Pack up at the first, or closes it at the last). No request: a
    /// Pack already shown just redraws.
    /// </summary>
    public void WatchFriend(string steamId, bool on) => _book.SetWatch(steamId, on);

    /// <summary>"Waypoint here": a new waypoint at a world spot, named after its area, coloured, and tracked.</summary>
    public string AddWaypoint(double x, double y) => Added(_library.Add(NewName(x, y), x, y, _library.NextColour()));

    /// <summary>"Waypoint at &lt;friend&gt;": where they stand, in their name and colour, tracked like any new one. Null for a friend with no position.</summary>
    public string? AddWaypointAtFriend(FriendState friend) =>
        friend is { X: { } x, Y: { } y } ? Added(_library.Add(FriendName(friend), x, y, _book.ColourOf(friend.SteamId!))) : null;

    public string Remove(Waypoint waypoint)
    {
        _routeWasRunning = RouteRunning; // asked now: the line below would make a running route look paused
        if (waypoint.Id == _config.TrackedWaypointId) _config.TrackedWaypointId = null;
        _library.Remove(waypoint.Id); // Changed → the maps redraw
        return $"{_short(waypoint.Name)} removed";
    }

    /// <summary>Where a copy goes. A property so the tests never touch the real clipboard, which is the player's.</summary>
    internal Action<string> WriteClipboard { get; set; } = text => Clipboard.SetText(text);

    /// <summary>A share code onto the clipboard; the notice says what was copied, or that the clipboard was busy.</summary>
    public string Copy(string code, string notice)
    {
        try
        {
            WriteClipboard(code);
            return notice;
        }
        catch
        {
            return "couldn't reach the clipboard";
        }
    }

    /// <summary>A pasted code becomes a new, tracked waypoint (AddFromCode).</summary>
    public string Paste()
    {
        string text;
        try
        {
            text = Clipboard.GetText();
        }
        catch
        {
            return "couldn't reach the clipboard";
        }
        return AddFromCode(text);
    }

    /// <summary>
    /// A share code's spot as a new, tracked waypoint: named from the code when
    /// it carries a name, else after the area it lands in like "Waypoint here"
    /// (owner, Oct 23 2026: a bare "pandora:62,-3168" pasted in Delta is "Delta").
    /// </summary>
    internal string AddFromCode(string? text)
    {
        if (!ShareCode.TryParse(text, out var x, out var y, out var name)) return "no position in the clipboard";
        return Added(_library.Add(name.Length > 0 ? name : NewName(x, y), x, y, _library.NextColour()));
    }

    /// <summary>A new waypoint's name: its area's ("Delta", "Delta 2" …), else "Waypoint N".</summary>
    private string NewName(double x, double y) => _library.NextName(_areaAt?.Invoke(x, y));

    /// <summary>Whether Paste has anything to work with: the menu greys its entry otherwise.</summary>
    public static bool ClipboardHasCode()
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

    /// <summary>A new waypoint is tracked at once, unless a route is running: marking a spot you pass must not interrupt the route (the owner's call).</summary>
    private string Added(Waypoint? waypoint)
    {
        if (waypoint is null) return "library full";
        if (!RouteRunning) Track(waypoint.Id);
        return $"{_short(waypoint.Name)} added";
    }
}

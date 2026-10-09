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
public sealed class MapActions
{
    private readonly OverlayConfig _config;
    private readonly WaypointLibrary _library;
    private readonly FriendBook _book;
    private readonly Func<string, string> _short;

    /// <param name="shortName">How the map shortens a name for its notices ("Forks added").</param>
    public MapActions(OverlayConfig config, WaypointLibrary library, FriendBook book, Func<string, string> shortName)
    {
        _config = config;
        _library = library;
        _book = book;
        _short = shortName;
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
        WaypointTracked?.Invoke();
    }

    /// <summary>Tracks a friend, or untracks with null.</summary>
    public void TrackFriend(string? steamId)
    {
        _config.TrackedFriendSteamId = steamId;
        FriendTracked?.Invoke();
    }

    /// <summary>"Waypoint here": a new waypoint at a world spot, auto-named and coloured, and tracked.</summary>
    public string AddWaypoint(double x, double y) => Added(_library.Add(_library.NextName(), x, y, _library.NextColour()));

    /// <summary>"Waypoint at &lt;friend&gt;": where they stand, in their name and colour, tracked like any new one. Null for a friend with no position.</summary>
    public string? AddWaypointAtFriend(FriendState friend) =>
        friend is { X: { } x, Y: { } y } ? Added(_library.Add(FriendName(friend), x, y, _book.ColourOf(friend.SteamId!))) : null;

    public string Remove(Waypoint waypoint)
    {
        if (waypoint.Id == _config.TrackedWaypointId) _config.TrackedWaypointId = null;
        _library.Remove(waypoint.Id); // Changed → the maps redraw
        return $"{_short(waypoint.Name)} removed";
    }

    /// <summary>A share code onto the clipboard; the notice says what was copied, or that the clipboard was busy.</summary>
    public string Copy(string code, string notice)
    {
        try
        {
            Clipboard.SetText(code);
            return notice;
        }
        catch
        {
            return "couldn't reach the clipboard";
        }
    }

    /// <summary>A pasted code becomes a new, tracked waypoint, named from the code when it carries a name.</summary>
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
        if (!ShareCode.TryParse(text, out var x, out var y, out var name)) return "no position in the clipboard";
        return Added(_library.Add(name.Length > 0 ? name : _library.NextName(), x, y, _library.NextColour()));
    }

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

    private string Added(Waypoint? waypoint)
    {
        if (waypoint is null) return "library full";
        Track(waypoint.Id);
        return $"{_short(waypoint.Name)} added";
    }
}

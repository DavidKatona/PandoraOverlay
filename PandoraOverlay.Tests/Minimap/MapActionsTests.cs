using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>The map menu's actions, shared by every map. The clipboard entries (copy / paste) are left to the in-game check: tests can't own the clipboard.</summary>
public sealed class MapActionsTests
{
    private readonly OverlayConfig _config = new();
    private readonly WaypointLibrary _library = new();
    private readonly FriendBook _book = new();

    private MapActions Actions() => new(_config, _library, _book, name => name.Length <= 5 ? name : name[..4] + "…");

    private static FriendState Friend(string id, string name, double? x, double? y) =>
        new(id, name, "Deinosuchus", "Male", 0.5, 1, 1, 1, 1, 0, false, false, false, x, y, true, false);

    [Fact]
    public void WaypointHereAddsANamedWaypointAndTracksIt()
    {
        var actions = Actions();
        var tracked = 0;
        actions.WaypointTracked += () => tracked++;

        var notice = actions.AddWaypoint(1000, 2000);

        var added = Assert.Single(_library.Items);
        Assert.Equal((1000.0, 2000.0), (added.X, added.Y));
        Assert.Equal(added.Id, _config.TrackedWaypointId);
        Assert.Equal(1, tracked);
        Assert.Equal($"{actions.Short(added.Name)} added", notice);
    }

    [Fact]
    public void AFullLibraryAddsNothingAndSaysSo()
    {
        for (var i = 0; i < WaypointLibrary.Capacity; i++) _library.Add($"w{i}", i * 100, 0, 0);
        var actions = Actions();
        var tracked = 0;
        actions.WaypointTracked += () => tracked++;

        Assert.True(actions.LibraryFull);
        Assert.Equal("library full", actions.AddWaypoint(5, 5));
        Assert.Equal(WaypointLibrary.Capacity, _library.Items.Count);
        Assert.Null(_config.TrackedWaypointId);
        Assert.Equal(0, tracked);
    }

    [Fact]
    public void RemovingTheTrackedWaypointUntracksIt()
    {
        var actions = Actions();
        actions.AddWaypoint(1, 1);
        var waypoint = _library.Items[0];

        var notice = actions.Remove(waypoint);

        Assert.Empty(_library.Items);
        Assert.Null(_config.TrackedWaypointId);
        Assert.EndsWith(" removed", notice);
    }

    [Fact]
    public void RemovingAnotherWaypointKeepsTheTrackedOne()
    {
        var actions = Actions();
        actions.AddWaypoint(1, 1);
        var other = _library.Add("other", 9, 9, 3)!;
        var tracked = _config.TrackedWaypointId;

        actions.Remove(other);

        Assert.Equal(tracked, _config.TrackedWaypointId);
    }

    [Fact]
    public void AWaypointAtAFriendTakesTheirNameAndColour()
    {
        var friend = Friend("76561190000000002", "Zoro", 300, 400);
        _book.Sync(new[] { friend }, DateTime.UtcNow);
        _book.Find(friend.SteamId)!.Nickname = "Zorrito";
        var actions = Actions();

        var notice = actions.AddWaypointAtFriend(friend);

        var added = Assert.Single(_library.Items);
        Assert.Equal("Zorrito", added.Name);                        // the name you see, your nickname
        Assert.Equal(_book.ColourOf(friend.SteamId!), added.Colour);
        Assert.Equal((300.0, 400.0), (added.X, added.Y));
        Assert.Equal(added.Id, _config.TrackedWaypointId);
        Assert.Equal("Zorr… added", notice);                        // shortened the map's way
    }

    [Fact]
    public void AFriendWithNoPositionGivesNoWaypointAndNoNotice()
    {
        Assert.Null(Actions().AddWaypointAtFriend(Friend("1", "x", null, null)));
        Assert.Empty(_library.Items);
    }

    [Fact]
    public void TrackingSetsTheConfigAndTellsTheMaps()
    {
        var actions = Actions();
        int waypoints = 0, friends = 0;
        actions.WaypointTracked += () => waypoints++;
        actions.FriendTracked += () => friends++;
        var id = Guid.NewGuid();

        actions.Track(id);
        actions.TrackFriend("76561190000000009");

        Assert.Equal(id, actions.TrackedWaypointId);
        Assert.Equal("76561190000000009", actions.TrackedFriendSteamId);
        Assert.Equal((1, 1), (waypoints, friends));                // each its own event: tracking one never redraws the other

        actions.Track(null);
        actions.TrackFriend(null);
        Assert.Null(_config.TrackedWaypointId);
        Assert.Null(_config.TrackedFriendSteamId);
    }
}

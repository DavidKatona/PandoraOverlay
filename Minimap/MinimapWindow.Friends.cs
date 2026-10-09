using System.Windows;
using System.Windows.Media;

namespace PandoraOverlay;

/// <summary>
/// MinimapWindow, friends part: the in-game friends from PollService's
/// roster stream, drawn by the MapCanvas as small arrows in their FriendBook
/// colours, rotated by their yaw like your own; the tracked friend wears a
/// ring and, in the centered view, edge-clamps as a direction indicator.
/// Only friends the server marks as sharing their location are ever drawn
/// (FriendState.OnMap) — consent is the server's call. Same class as
/// MinimapWindow.xaml.cs, split for reading; see CLAUDE.md.
/// Friends stay drawn while YOU are not in game: their positions are still
/// true (OnSnapshot's not-in-game path leaves this layer alone).
/// </summary>
public partial class MinimapWindow
{
    private IReadOnlyList<FriendState>? _friends;  // the last roster (you filtered out), null = none / cleared
    private FriendState? _hoverFriend;             // the friend under the cursor in edit mode
    private double _mapTargetX, _mapTargetY;       // where the map translate is heading — markers are placed against it

    private FriendState? TrackedFriend =>
        _config.TrackedFriendSteamId is { } id ? _friends?.FirstOrDefault(f => f.SteamId == id && f.OnMap) : null;

    private string FriendName(FriendState f) => _book.DisplayName(f.SteamId, f.Name);

    private Brush FriendBrush(FriendState f) => MapBrushes.Palette[_book.ColourOf(f.SteamId!)];

    /// <summary>A new roster from PollService (after your own snapshot); null clears the layer.</summary>
    private void OnFriends(IReadOnlyList<FriendState>? roster)
    {
        _friends = roster;
        if (_hoverFriend is not null && (roster is null || !roster.Any(f => f.SteamId == _hoverFriend.SteamId)))
        {
            _hoverFriend = null;
        }
        RebuildFriendMarkers();
        var glide = TimeSpan.FromSeconds(Math.Max(2, _config.PollIntervalSeconds) * 0.9);
        UpdateFriendVisual(_mapTargetX, _mapTargetY, glide);
        UpdateFooter();
    }

    /// <summary>Nicknames, colours, map visibility changed in Settings: redraw with the same roster.</summary>
    private void OnBookChanged()
    {
        RebuildFriendMarkers();
        UpdateFriendVisual(_mapTargetX, _mapTargetY, glide: null);
        UpdateFooter();
    }

    /// <summary>
    /// Recreates the arrows for the drawn set — in-game friends sharing their
    /// location, not hidden per friend, with the layer on. The canvas keeps
    /// each existing arrow's current place, so a rebuild never makes one jump.
    /// </summary>
    private void RebuildFriendMarkers()
    {
        var drawn = _friends is null || !_config.FriendsOnMinimap
            ? Array.Empty<FriendState>()
            : _friends.Where(f => f.OnMap && f.SteamId is not null && _book.ShowsOnMap(f.SteamId));
        Map.ShowFriends(drawn, _config.TrackedFriendSteamId, FriendBrush);
    }

    /// <summary>
    /// Positions and turns every friend arrow for the given map translation
    /// (targets during a glide, current values otherwise) — called from
    /// UpdateWaypointVisual so friends move with the map exactly as the
    /// waypoints do. The tracked friend clamps to the panel edge in the
    /// centered view; the others simply leave the panel.
    /// </summary>
    private void UpdateFriendVisual(double mapTx, double mapTy, TimeSpan? glide)
    {
        if (_poll.Calibration is not { } cal) return;
        Map.PlaceFriends(cal, _centered ? View(mapTx, mapTy) : View(), edgeIndicator: _centered, glide, _config.MinimapYawOffsetDegrees); // the island view's map never moves
    }

    /// <summary>The drawn friend under a panel point; friends sit above waypoints, so they are tested first.</summary>
    private FriendState? FriendHitTest(Point pos) => Map.FriendAt(pos, SnapRadius);

    /// <summary>The tracked friend changed (MapActions): ring, edge indicator and first claim on the footer.</summary>
    private void OnFriendTracked()
    {
        RebuildFriendMarkers();
        UpdateFriendVisual(_mapTargetX, _mapTargetY, glide: null);
        UpdateFooter();
    }
}

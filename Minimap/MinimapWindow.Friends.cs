using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace PandoraOverlay;

/// <summary>
/// MinimapWindow, friends part: the in-game friends from PollService's
/// roster stream drawn as small arrows in their FriendBook colours, rotated
/// by their yaw like your own; the tracked friend wears a ring and, in the
/// centered view, edge-clamps as a direction indicator. Only friends the
/// server marks as sharing their location are ever drawn (FriendState.OnMap)
/// — consent is the server's call. Same class as MinimapWindow.xaml.cs,
/// split for reading; see CLAUDE.md.
/// </summary>
public partial class MinimapWindow
{
    private const string FriendArrowGeometry = "M 0,-6 L 4.5,5 L 0,2.5 L -4.5,5 Z"; // your arrow at three quarters: you stay the anchor

    /// <summary>One drawn friend: the arrow (+ ring when tracked) and the transforms that move and turn it.</summary>
    private sealed class FriendMarker
    {
        public required FriendState Friend { get; set; }
        public required Path Arrow { get; init; }
        public Ellipse? Ring { get; init; }
        public RotateTransform Rotate { get; } = new();
        public TranslateTransform Translate { get; } = new();
        public bool Tracked { get; init; }
        public bool Fresh { get; set; } = true; // first placement snaps, later ones glide
    }

    private readonly Dictionary<string, FriendMarker> _friendMarkers = new();
    private IReadOnlyList<FriendState>? _friends;  // the last roster (you filtered out), null = none / cleared
    private FriendState? _menuFriend;              // the friend under the cursor when the menu opened
    private FriendState? _hoverFriend;             // the friend under the cursor in edit mode
    private double _mapTargetX, _mapTargetY;       // where the map translate is heading — markers are placed against it

    private FriendState? TrackedFriend =>
        _config.TrackedFriendSteamId is { } id ? _friends?.FirstOrDefault(f => f.SteamId == id && f.OnMap) : null;

    private string FriendName(FriendState f) => _book.DisplayName(f.SteamId, f.Name);

    private Brush FriendBrush(FriendState f) => PaletteBrushes[_book.ColourOf(f.SteamId!)];

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
    /// location, not hidden per friend, with the layer on — keeping each
    /// existing marker's current place so a rebuild never makes one jump.
    /// </summary>
    private void RebuildFriendMarkers()
    {
        var old = new Dictionary<string, FriendMarker>(_friendMarkers);
        FriendLayer.Children.Clear();
        _friendMarkers.Clear();
        if (_friends is null || !_config.FriendsOnMinimap) return;

        var trackedId = _config.TrackedFriendSteamId;
        foreach (var f in _friends)
        {
            if (!f.OnMap || f.SteamId is null || !_book.ShowsOnMap(f.SteamId) || _friendMarkers.ContainsKey(f.SteamId)) continue;
            var brush = FriendBrush(f);
            var tracked = f.SteamId == trackedId;
            var arrow = new Path
            {
                Data = Geometry.Parse(FriendArrowGeometry), Fill = brush, Stroke = MarkerOutline, StrokeThickness = 1
            };
            Ellipse? ring = null;
            if (tracked)
            {
                ring = new Ellipse
                {
                    Width = RingSize, Height = RingSize, Stroke = brush, StrokeThickness = 1.5, Fill = Brushes.Transparent
                };
                Canvas.SetLeft(ring, -RingSize / 2);
                Canvas.SetTop(ring, -RingSize / 2);
            }
            var marker = new FriendMarker { Friend = f, Arrow = arrow, Ring = ring, Tracked = tracked };
            arrow.RenderTransform = new TransformGroup { Children = { marker.Rotate, marker.Translate } };
            if (ring is not null) ring.RenderTransform = marker.Translate;

            if (old.TryGetValue(f.SteamId, out var was))
            {
                // Same friend, new shapes: start where the old marker stands, so only the data moves it.
                marker.Translate.X = was.Translate.X;
                marker.Translate.Y = was.Translate.Y;
                marker.Rotate.Angle = was.Rotate.Angle;
                marker.Fresh = was.Fresh;
            }
            if (ring is not null) FriendLayer.Children.Add(ring);
            FriendLayer.Children.Add(arrow);
            _friendMarkers[f.SteamId] = marker;
        }
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
        var size = MapHost.Width;
        foreach (var m in _friendMarkers.Values)
        {
            if (m.Friend is not { X: { } wx, Y: { } wy }) continue;
            var f = ToFraction(cal, wx, wy);
            double x, y;
            var onScreen = true;
            if (_centered)
            {
                var mapSize = size * _zoom;
                x = f.Fx * mapSize + mapTx;
                y = f.Fy * mapSize + mapTy;
                if (m.Tracked)
                {
                    x = Math.Clamp(x, WaypointMargin, size - WaypointMargin);
                    y = Math.Clamp(y, WaypointMargin, size - WaypointMargin);
                }
                else
                {
                    onScreen = x >= -RingSize && x <= size + RingSize && y >= -RingSize && y <= size + RingSize;
                }
            }
            else
            {
                x = f.Fx * size;
                y = f.Fy * size;
            }

            var visibility = onScreen ? Visibility.Visible : Visibility.Hidden;
            m.Arrow.Visibility = visibility;
            if (m.Ring is not null) m.Ring.Visibility = visibility;

            var angle = m.Friend.Yaw + _config.MinimapYawOffsetDegrees;
            if (glide is { } d && !m.Fresh)
            {
                Animate(m.Translate, TranslateTransform.XProperty, x, d);
                Animate(m.Translate, TranslateTransform.YProperty, y, d);
                var delta = ((angle - m.Rotate.Angle) % 360 + 540) % 360 - 180;
                Animate(m.Rotate, RotateTransform.AngleProperty, m.Rotate.Angle + delta, d);
            }
            else
            {
                m.Translate.BeginAnimation(TranslateTransform.XProperty, null);
                m.Translate.BeginAnimation(TranslateTransform.YProperty, null);
                m.Rotate.BeginAnimation(RotateTransform.AngleProperty, null);
                m.Translate.X = x;
                m.Translate.Y = y;
                m.Rotate.Angle = angle;
            }
            m.Fresh = false;
        }
    }

    private void ClearFriendAnimations()
    {
        foreach (var m in _friendMarkers.Values)
        {
            m.Translate.BeginAnimation(TranslateTransform.XProperty, null);
            m.Translate.BeginAnimation(TranslateTransform.YProperty, null);
            m.Rotate.BeginAnimation(RotateTransform.AngleProperty, null);
        }
    }

    /// <summary>The drawn friend within SnapRadius of a panel point, nearest first; the tracked one wins ties. Friends sit above waypoints, so they are tested first.</summary>
    private FriendState? FriendHitTest(Point pos)
    {
        FriendMarker? best = null;
        var bestDistance = double.MaxValue;
        foreach (var m in _friendMarkers.Values)
        {
            if (m.Arrow.Visibility != Visibility.Visible) continue;
            var d = Math.Max(Math.Abs(pos.X - m.Translate.X), Math.Abs(pos.Y - m.Translate.Y));
            if (d >= SnapRadius) continue;
            if (d < bestDistance || (d == bestDistance && m.Tracked))
            {
                best = m;
                bestDistance = d;
            }
        }
        return best?.Friend;
    }

    /// <summary>Tracks a friend (or untracks with null): ring, edge indicator and first claim on the footer.</summary>
    private void SetTrackedFriend(string? steamId)
    {
        _config.TrackedFriendSteamId = steamId; // persisted with the next Save()
        RebuildFriendMarkers();
        UpdateFriendVisual(_mapTargetX, _mapTargetY, glide: null);
        UpdateFooter();
    }

    /// <summary>"Waypoint at &lt;friend&gt;": a library waypoint where they stand, in their colour and name, tracked like any new one.</summary>
    private void AddWaypointAtFriend(FriendState friend)
    {
        if (friend is not { X: { } x, Y: { } y }) return;
        var wp = _library.Add(FriendName(friend), x, y, _book.ColourOf(friend.SteamId!));
        if (wp is null)
        {
            ShowNotice("library full");
            return;
        }
        SetTracked(wp.Id);
        ShowNotice($"{Short(wp.Name)} added");
    }
}

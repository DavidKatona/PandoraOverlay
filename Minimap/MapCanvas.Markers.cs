using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace PandoraOverlay;

/// <summary>
/// MapCanvas, markers part: the waypoint markers (dots in their colours,
/// the tracked one a ringed diamond), the friends' arrows (your arrow at
/// three quarters, turned by their yaw, the tracked one ringed), their
/// placing and gliding, and hit testing. Which waypoints and friends are
/// drawn is the map's call; it passes the drawn set. Same class as
/// MapCanvas.xaml.cs, split for reading.
/// </summary>
public partial class MapCanvas
{
    private const double DotSize = 6;            // an untracked waypoint
    private const double DiamondRadius = 5;      // the tracked one (10 px, like the v1.20 slots)
    private const double RingSize = 16;          // the ring around a tracked waypoint or friend
    private const double EdgeMargin = 8;         // inset of a tracked marker's off-panel indicator
    private const string FriendArrowGeometry = "M 0,-6 L 4.5,5 L 0,2.5 L -4.5,5 Z"; // your arrow at three quarters: you stay the anchor

    private static readonly Brush MarkerOutline = new SolidColorBrush(Color.FromArgb(0x99, 0x0A, 0x15, 0x20));

    /// <summary>One drawn waypoint: its shape(s) and the transform that moves them.</summary>
    private sealed class Marker
    {
        public required Waypoint Waypoint { get; init; }
        public required Shape Shape { get; init; }
        public Ellipse? Ring { get; init; }
        public TranslateTransform Translate { get; } = new();
        public bool Tracked { get; init; }
    }

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

    private readonly Dictionary<Guid, Marker> _markers = new();
    private readonly Dictionary<string, FriendMarker> _friendMarkers = new();

    // ---- Waypoints --------------------------------------------------------------

    /// <summary>
    /// Recreates the waypoint shapes: a small dot per waypoint in its colour,
    /// the tracked one a diamond with a ring. Each gets its own translate so
    /// it can glide with the map. Place them after (PlaceWaypoints).
    /// </summary>
    public void ShowWaypoints(IEnumerable<Waypoint> shown, Guid? trackedId)
    {
        MarkerLayer.Children.Clear();
        _markers.Clear();
        foreach (var w in shown)
        {
            var brush = MapBrushes.Of(w.Colour);
            var tracked = w.Id == trackedId;
            Marker marker;
            if (tracked)
            {
                var ring = new Ellipse
                {
                    Width = RingSize, Height = RingSize, Stroke = brush, StrokeThickness = 1.5,
                    Fill = Brushes.Transparent
                };
                Canvas.SetLeft(ring, -RingSize / 2);
                Canvas.SetTop(ring, -RingSize / 2);
                var diamond = new Path
                {
                    Data = Geometry.Parse($"M 0,-{DiamondRadius} L {DiamondRadius},0 L 0,{DiamondRadius} L -{DiamondRadius},0 Z"),
                    Fill = brush, Stroke = MarkerOutline, StrokeThickness = 1.25
                };
                marker = new Marker { Waypoint = w, Shape = diamond, Ring = ring, Tracked = true };
                ring.RenderTransform = marker.Translate;
                diamond.RenderTransform = marker.Translate;
                MarkerLayer.Children.Add(ring);
                MarkerLayer.Children.Add(diamond);
            }
            else
            {
                var dot = new Ellipse
                {
                    Width = DotSize, Height = DotSize, Fill = brush, Stroke = MarkerOutline, StrokeThickness = 1
                };
                Canvas.SetLeft(dot, -DotSize / 2);
                Canvas.SetTop(dot, -DotSize / 2);
                marker = new Marker { Waypoint = w, Shape = dot };
                dot.RenderTransform = marker.Translate;
                MarkerLayer.Children.Add(dot);
            }
            _markers[w.Id] = marker;
        }
    }

    /// <summary>Whether exactly these waypoints are drawn: a policy whose set follows the player rebuilds only on a change.</summary>
    public bool ShowsWaypoints(IReadOnlyCollection<Waypoint> wanted) =>
        wanted.Count == _markers.Count && wanted.All(w => _markers.ContainsKey(w.Id));

    /// <summary>
    /// Places every waypoint for a view (its offset: the map's target during
    /// a glide, the current one otherwise). With an edge indicator, the
    /// tracked marker clamps to the panel's edge as a direction indicator and
    /// the others simply leave the panel; without, every marker sits where it is.
    /// </summary>
    public void PlaceWaypoints(MapCalibration cal, MapViewport view, bool edgeIndicator, TimeSpan? glide)
    {
        foreach (var m in _markers.Values)
        {
            var f = cal.ToFraction(m.Waypoint.X, m.Waypoint.Y);
            var at = view.ToPanel(f.Fx, f.Fy);
            var onScreen = true;
            if (edgeIndicator)
            {
                if (m.Tracked) at = view.ClampIntoPanel(at, EdgeMargin);
                else onScreen = view.InPanel(at, DotSize);
            }
            var (x, y) = (at.X, at.Y);

            var visibility = onScreen ? Visibility.Visible : Visibility.Hidden;
            m.Shape.Visibility = visibility;
            if (m.Ring is not null) m.Ring.Visibility = visibility;

            if (glide is { } d)
            {
                Animate(m.Translate, TranslateTransform.XProperty, x, d);
                Animate(m.Translate, TranslateTransform.YProperty, y, d);
            }
            else
            {
                m.Translate.BeginAnimation(TranslateTransform.XProperty, null);
                m.Translate.BeginAnimation(TranslateTransform.YProperty, null);
                m.Translate.X = x;
                m.Translate.Y = y;
            }
        }
    }

    /// <summary>The drawn waypoint within a radius of a panel point, nearest first; the tracked one wins ties.</summary>
    public Waypoint? WaypointAt(Point pos, double radius)
    {
        Marker? best = null;
        var bestDistance = double.MaxValue;
        foreach (var m in _markers.Values)
        {
            if (m.Shape.Visibility != Visibility.Visible) continue;
            var d = Math.Max(Math.Abs(pos.X - m.Translate.X), Math.Abs(pos.Y - m.Translate.Y));
            if (d >= radius) continue;
            if (d < bestDistance || (d == bestDistance && m.Tracked))
            {
                best = m;
                bestDistance = d;
            }
        }
        return best?.Waypoint;
    }

    // ---- Friends ----------------------------------------------------------------

    /// <summary>
    /// Recreates the friends' arrows for the drawn set the map passes (it
    /// filters: in game, sharing their location, the Map tick). A friend
    /// drawn before keeps their arrow's current place and turn, so a rebuild
    /// never makes one jump; a new one is placed at once, later ones glide.
    /// </summary>
    public void ShowFriends(IEnumerable<FriendState> drawn, string? trackedId, Func<FriendState, Brush> brushOf)
    {
        var old = new Dictionary<string, FriendMarker>(_friendMarkers);
        FriendLayer.Children.Clear();
        _friendMarkers.Clear();
        foreach (var f in drawn)
        {
            if (f.SteamId is null || _friendMarkers.ContainsKey(f.SteamId)) continue;
            var brush = brushOf(f);
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
    /// Places and turns every friend's arrow for a view, exactly as the
    /// waypoints are placed (same offset, same glide), turned by their yaw
    /// plus the yaw offset your own arrow uses. A tracked friend clamps to the
    /// panel's edge with an edge indicator.
    /// </summary>
    public void PlaceFriends(MapCalibration cal, MapViewport view, bool edgeIndicator, TimeSpan? glide, double yawOffset)
    {
        foreach (var m in _friendMarkers.Values)
        {
            if (m.Friend is not { X: { } wx, Y: { } wy }) continue;
            var f = cal.ToFraction(wx, wy);
            var at = view.ToPanel(f.Fx, f.Fy);
            var onScreen = true;
            if (edgeIndicator)
            {
                if (m.Tracked) at = view.ClampIntoPanel(at, EdgeMargin);
                else onScreen = view.InPanel(at, RingSize);
            }
            var (x, y) = (at.X, at.Y);

            var visibility = onScreen ? Visibility.Visible : Visibility.Hidden;
            m.Arrow.Visibility = visibility;
            if (m.Ring is not null) m.Ring.Visibility = visibility;

            var angle = m.Friend.Yaw + yawOffset;
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

    /// <summary>The drawn friend within a radius of a panel point, nearest first; the tracked one wins ties. Friends sit above waypoints, so a map tests them first.</summary>
    public FriendState? FriendAt(Point pos, double radius)
    {
        FriendMarker? best = null;
        var bestDistance = double.MaxValue;
        foreach (var m in _friendMarkers.Values)
        {
            if (m.Arrow.Visibility != Visibility.Visible) continue;
            var d = Math.Max(Math.Abs(pos.X - m.Translate.X), Math.Abs(pos.Y - m.Translate.Y));
            if (d >= radius) continue;
            if (d < bestDistance || (d == bestDistance && m.Tracked))
            {
                best = m;
                bestDistance = d;
            }
        }
        return best?.Friend;
    }
}

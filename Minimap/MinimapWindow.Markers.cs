using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.Windows.Shapes.Path;

namespace PandoraOverlay;

/// <summary>
/// MinimapWindow, markers part: which library waypoints are drawn and how
/// (dots, the tracked diamond and ring), their positioning, the footer and
/// the heading/speed pill. Same class as MinimapWindow.xaml.cs, split for
/// reading; see CLAUDE.md.
/// </summary>
public partial class MinimapWindow
{
    // ---- Waypoint markers ---------------------------------------------------

    private Waypoint? TrackedWaypoint => _library.Find(_config.TrackedWaypointId);

    /// <summary>
    /// Which library entries are drawn under the WaypointVisibility policy:
    /// every visible one, only the tracked one, or the NearestCount visible
    /// ones closest to the player. The tracked one is always drawn — you
    /// asked to follow it.
    /// </summary>
    private List<Waypoint> ShownWaypoints()
    {
        var tracked = TrackedWaypoint;
        List<Waypoint> shown;
        switch (_config.WaypointVisibility)
        {
            case "tracked":
                shown = new List<Waypoint>();
                break;
            case "nearest" when _lastWorld is { } p:
                shown = _library.Items.Where(w => w.Visible)
                    .OrderBy(w => WaypointLibrary.Distance(w.X, w.Y, p.X, p.Y))
                    .Take(NearestCount).ToList();
                break;
            case "nearest":
                shown = _library.Items.Where(w => w.Visible).Take(NearestCount).ToList();
                break;
            default:
                shown = _library.Items.Where(w => w.Visible).ToList();
                break;
        }
        if (tracked is not null && !shown.Contains(tracked)) shown.Add(tracked);
        return shown;
    }

    private void OnLibraryChanged()
    {
        if (TrackedWaypoint is null && _config.TrackedWaypointId is not null) _config.TrackedWaypointId = null; // deleted elsewhere
        RebuildMarkers();
        UpdateWaypointVisual(_mapTranslate.X, _mapTranslate.Y, glide: null);
        UpdateFooter();
    }

    /// <summary>Under the "nearest" policy the set follows the player: rebuild only when it actually changed.</summary>
    private void RebuildMarkersIfSetChanged()
    {
        var wanted = ShownWaypoints();
        if (wanted.Count == _markers.Count && wanted.All(w => _markers.ContainsKey(w.Id))) return;
        RebuildMarkers();
    }

    /// <summary>
    /// Recreates the marker shapes from the library: a small dot per shown
    /// waypoint in its colour, the tracked one a diamond with a ring. Each
    /// gets its own translate so positions can glide with the map.
    /// </summary>
    private void RebuildMarkers()
    {
        MarkerLayer.Children.Clear();
        _markers.Clear();
        var trackedId = _config.TrackedWaypointId;
        foreach (var w in ShownWaypoints())
        {
            var brush = PaletteBrushes[WaypointPalette.Wrap(w.Colour)];
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

    private static readonly Brush MarkerOutline = new SolidColorBrush(Color.FromArgb(0x99, 0x0A, 0x15, 0x20));

    /// <summary>
    /// Positions every marker for the given map translation (targets during
    /// a glide, current values otherwise). In the centered view the tracked
    /// marker clamps to the panel edge as a direction indicator; the others
    /// simply leave the panel.
    /// </summary>
    private void UpdateWaypointVisual(double mapTx, double mapTy, TimeSpan? glide)
    {
        if (_poll.Calibration is not { } cal) return;
        var size = MapHost.Width;
        foreach (var m in _markers.Values)
        {
            var f = ToFraction(cal, m.Waypoint.X, m.Waypoint.Y);
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
                    onScreen = x >= -DotSize && x <= size + DotSize && y >= -DotSize && y <= size + DotSize;
                }
            }
            else
            {
                x = f.Fx * size;
                y = f.Fy * size;
            }

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

    /// <summary>The footer's subject: the tracked waypoint, else the nearest visible one; with metres.</summary>
    private (Waypoint Waypoint, double Meters)? FooterTarget()
    {
        if (_lastWorld is not { } p) return null;
        if (TrackedWaypoint is { } t) return (t, WaypointLibrary.Distance(t.X, t.Y, p.X, p.Y) / 100);
        return _library.Nearest(p.X, p.Y, w => w.Visible);
    }

    private static string Short(string name) =>
        name.Length <= FooterNameLength ? name : name[..(FooterNameLength - 1)] + "…";

    /// <summary>
    /// View mode (+ zoom when centered), then the tracked (else nearest)
    /// waypoint in its colour with name, distance and, while actually
    /// closing on it, the ETA at the current pace. Hovering a marker in
    /// edit mode names that one instead; a notice (copied / pasted)
    /// replaces the line briefly.
    /// </summary>
    private void UpdateFooter()
    {
        ModeFooter.Inlines.Clear();
        if (_notice is not null)
        {
            ModeFooter.Inlines.Add(_notice);
            return;
        }

        ModeFooter.Inlines.Add(_centered ? $"centered · {_zoom:0.##}×" : "island view");

        Waypoint subject;
        double? meters;
        if (_hover is { } h)
        {
            subject = h;
            meters = _lastWorld is { } p ? WaypointLibrary.Distance(h.X, h.Y, p.X, p.Y) / 100 : null;
        }
        else if (FooterTarget() is { } target)
        {
            subject = target.Waypoint;
            meters = target.Meters;
        }
        else
        {
            return;
        }

        // The diamond AND the name in the waypoint's colour, then a separator
        // before the distance: "Waypoint 6 150 m" read as one number.
        ModeFooter.Inlines.Add(" · ");
        ModeFooter.Inlines.Add(new Run("◆ " + Short(subject.Name))
        {
            Foreground = PaletteBrushes[WaypointPalette.Wrap(subject.Colour)]
        });
        if (meters is not { } m) return;

        var text = m >= 1000 ? $" · {m / 1000:0.0} km" : $" · {m:0} m";
        if (_speed.ClosingMps(subject.X, subject.Y) is { } closing && closing >= MinClosingMps)
        {
            text += $" · ~{FormatEta(TimeSpan.FromSeconds(m / closing))}";
        }
        ModeFooter.Inlines.Add(text);
    }

    private static string FormatEta(TimeSpan eta) =>
        eta.TotalMinutes < 1 ? "<1 min"
        : eta.TotalHours < 1 ? $"{eta.TotalMinutes:0} min"
        : $"{(int)eta.TotalHours} h {eta.Minutes:00} min";

    /// <summary>Bottom-right pill: compass letter from the arrow's heading, and km/h once the speed is known.</summary>
    private void UpdateSpeedPill()
    {
        if (!_config.MinimapSpeedEnabled || _lastFix is not { } fix)
        {
            SpeedPanel.Visibility = Visibility.Collapsed;
            return;
        }
        var heading = Compass.Letter(fix.Yaw);
        SpeedText.Text = _speed.SpeedMps is { } mps ? $"{heading} · {mps * 3.6:0} km/h" : heading;
        SpeedPanel.Visibility = Visibility.Visible;
    }

}

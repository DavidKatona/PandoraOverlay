using System.Windows;
using System.Windows.Media;

namespace PandoraOverlay;

/// <summary>
/// MinimapWindow, area part (v1.29): the top-right pill that names the area
/// you are in, and the optional border layer. The area itself is
/// MainWindow's (AreaJournal — it also feeds the Activity lines, so it must
/// not depend on this window being shown); it arrives through SetArea. In
/// edit mode the pill follows the CURSOR instead and names the area under
/// it, in the edit colour, so the borders can be looked over without
/// walking the island. Same class as MinimapWindow.xaml.cs, split for
/// reading; see CLAUDE.md.
/// </summary>
public partial class MinimapWindow
{
    /// <summary>How wide a border line should come out on screen, whatever the view: thin, but not lost.</summary>
    private const double BorderScreenPixels = 1.2;

    private static readonly Brush AreaOwn = new SolidColorBrush(Color.FromRgb(0xEC, 0xF2, 0xF8));
    private static readonly Brush AreaHover = new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x64)); // the edit-mode accent

    /// <summary>
    /// What the pill says where there is no named area (owner's word, Oct 2
    /// 2026: neutral on purpose — it must not claim water, a future map
    /// could leave land unnamed). Shown dimmer than a real name, so it
    /// reads as "nothing is named here" and not as an area called that.
    /// </summary>
    private const string Uncharted = "Uncharted";
    private const double UnchartedOpacity = 0.6;
    private static readonly TimeSpan AreaPulse = TimeSpan.FromSeconds(3); // three blinks; the growth readout gets ten seconds, but a crossing is far more frequent than a stage

    private string? _area;                       // where you are, settled (null = no named area / not in game / unknown)
    private (double Fx, double Fy)? _hoverSpot;  // edit mode: the map point under the cursor

    /// <summary>The bundled map for the hover lookup, decoded on first use — never, with the pill switched off.</summary>
    private AreaMap? Areas => _config.MinimapAreaEnabled ? AreaMapAsset.Shared : null;

    /// <summary>
    /// MainWindow's word on where you are, per poll and when this window is
    /// created. A CROSSING blinks the pill briefly (the growth readout's
    /// blink), so it is noticed without reading the feed — not the pill's
    /// first appearance after a spawn (no position yet when the word
    /// arrives), and not while the pill is showing the cursor's area.
    /// </summary>
    public void SetArea(string? name)
    {
        if (name == _area) return;
        var crossing = _lastFix is not null;
        _area = name;
        UpdateAreaPill();
        if (crossing && AreaPanel.Visibility == Visibility.Visible && !(EditMode && _hoverSpot is not null))
        {
            PulseBriefly(AreaPanel, AreaPulse);
        }
    }

    private void SetHoverSpot((double Fx, double Fy)? spot)
    {
        if (spot == _hoverSpot) return;
        _hoverSpot = spot;
        UpdateAreaPill();
    }

    /// <summary>
    /// Your area — or, while the cursor is over the map in edit mode, the
    /// area under it and only that (falling back to yours there would read
    /// as "this spot is in my area"). Where no area is named — open sea,
    /// off the map's edge — it says "Uncharted", dimmed. Hidden only when
    /// there is nothing to place: not in game, no map, or switched off.
    /// </summary>
    private void UpdateAreaPill()
    {
        UpdateAreaOutlines(force: false); // everything that can change the pill can change the two outlines
        var hovering = EditMode && _hoverSpot is not null;
        if (Areas is not { } map || (!hovering && _lastFix is null))
        {
            AreaPanel.Visibility = Visibility.Collapsed;
            AreaPanel.BeginAnimation(OpacityProperty, null); // a blink in progress ends with the pill, so it can't reappear mid-blink
            return;
        }
        var name = hovering && _hoverSpot is { } spot ? map.NameAt(spot.Fx, spot.Fy) : _area;
        AreaText.Text = name ?? Uncharted;
        AreaText.Opacity = name is null ? UnchartedOpacity : 1;
        AreaText.Foreground = hovering ? AreaHover : AreaOwn;
        AreaPanel.Visibility = Visibility.Visible;
    }

    /// <summary>The control panel's Areas button: flips the border layer (persisted with the next Save).</summary>
    public void ToggleAreaBorders()
    {
        _config.MinimapAreaBordersEnabled = !_config.MinimapAreaBordersEnabled;
        UpdateAreaBorders();
    }

    /// <summary>
    /// The border layer: a dark outline round every area, over the map (and
    /// the heatmap) and panning with it. Lines, not the area colours — those
    /// are picked to be told apart and would bury the terrain. VECTOR lines
    /// (AreaBorders): the geometry is in map pixels and scaled to the map's
    /// rendered size through the GEOMETRY's transform, which moves the
    /// points but leaves the stroke alone — so the line is the same thin
    /// width at every zoom and stays sharp. (A first build drew a picture of
    /// the borders, stretched with the map; at 5–6× its pixels showed.) The
    /// width undoes the widget's own scale, so it is about a screen pixel at
    /// any MinimapScale. Follows the view, the zoom and the scale
    /// (ApplyViewMode).
    /// </summary>
    private void UpdateAreaBorders()
    {
        if (!_config.MinimapAreaBordersEnabled || AreaMapAsset.Shared is not { } map || AreaMapAsset.Borders is not { } lines)
        {
            AreaBordersPath.Visibility = Visibility.Collapsed;
            AreaBordersPath.Data = null;
            UpdateAreaOutlines(force: true);
            return;
        }
        var scale = MapImage.Width / map.Size;
        AreaBordersPath.Data = new GeometryGroup { Children = { lines }, Transform = new ScaleTransform(scale, scale) };
        AreaBordersPath.StrokeThickness = BorderScreenPixels / Math.Max(0.1, AppearanceScale(_config));
        AreaBordersPath.Visibility = Visibility.Visible;
        UpdateAreaOutlines(force: true); // same scale, same widget scale
    }

    /// <summary>A highlighted outline is a little wider than the borders it lies on.</summary>
    private const double OutlineScreenPixels = 1.5;

    private int _ownOutline = AreaMap.None;   // the area each highlight path draws now
    private int _hoverOutline = AreaMap.None;

    /// <summary>
    /// Two highlights on the border layer, mirroring the pill's two colours:
    /// YOUR area's outline in a soft light line (where you are and how far
    /// it reaches — the owner picked it from renders over a bolder navy,
    /// which vanished on forest, and over orange), and in edit mode the
    /// outline of the area under the CURSOR in the edit orange (the owner's
    /// idea). Pointing at your own area, orange wins. Each is the very
    /// lines of the border layer that have that area on a side
    /// (AreaMapAsset.OutlineOf), so it sits exactly on them. Only with the
    /// Areas layer on: the button keeps one meaning. Nothing at open sea.
    /// </summary>
    private void UpdateAreaOutlines(bool force)
    {
        var map = _config.MinimapAreaBordersEnabled ? AreaMapAsset.Shared : null;
        var hover = map is not null && EditMode && _hoverSpot is { } spot ? map.IndexAt(spot.Fx, spot.Fy) : AreaMap.None;
        var own = map is not null && _lastFix is not null ? map.IndexOf(_area) : AreaMap.None;
        if (own == hover) own = AreaMap.None;
        var scale = map is null ? 0 : MapImage.Width / map.Size;
        DrawOutline(AreaOwnPath, own, ref _ownOutline, scale, force);
        DrawOutline(AreaHoverPath, hover, ref _hoverOutline, scale, force);
    }

    private void DrawOutline(System.Windows.Shapes.Path path, int area, ref int shown, double scale, bool force)
    {
        if (area == shown && !force) return;
        shown = area;
        if (AreaMapAsset.OutlineOf(area) is not { } outline)
        {
            path.Visibility = Visibility.Collapsed;
            path.Data = null;
            return;
        }
        path.Data = new GeometryGroup { Children = { outline }, Transform = new ScaleTransform(scale, scale) };
        path.StrokeThickness = OutlineScreenPixels / Math.Max(0.1, AppearanceScale(_config));
        path.Visibility = Visibility.Visible;
    }
}

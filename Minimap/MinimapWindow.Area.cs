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

    private string? _area;                       // where you are, settled (null = no named area / not in game / unknown)
    private (double Fx, double Fy)? _hoverSpot;  // edit mode: the map point under the cursor

    /// <summary>The bundled map for the hover lookup, decoded on first use — never, with the pill switched off.</summary>
    private AreaMap? Areas => _config.MinimapAreaEnabled ? AreaMapAsset.Shared : null;

    /// <summary>MainWindow's word on where you are, per poll and when this window is created.</summary>
    public void SetArea(string? name)
    {
        if (name == _area) return;
        _area = name;
        UpdateAreaPill();
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
        var hovering = EditMode && _hoverSpot is not null;
        if (Areas is not { } map || (!hovering && _lastFix is null))
        {
            AreaPanel.Visibility = Visibility.Collapsed;
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
    /// The border layer: a dark outline round every area, over the
    /// map (and the heatmap) and panning with it. Lines, not the area
    /// colours — those are picked to be told apart and would bury the
    /// terrain. The line is drawn in map pixels, so its thickness is chosen
    /// for the view: four map pixels in the island view, one when zoomed
    /// right in, each coming out about a screen pixel wide. Follows the
    /// view, the zoom and the widget's scale (ApplyViewMode).
    /// </summary>
    private void UpdateAreaBorders()
    {
        if (!_config.MinimapAreaBordersEnabled || AreaMapAsset.Shared is not { } map)
        {
            AreaBordersImage.Visibility = Visibility.Collapsed;
            AreaBordersImage.Source = null;
            return;
        }
        var screenPerMapPixel = MapImage.Width * AppearanceScale(_config) / map.Size;
        var thickness = Math.Clamp((int)Math.Round(BorderScreenPixels / screenPerMapPixel), 1, 4);
        AreaBordersImage.Source = AreaMapAsset.Borders(thickness);
        AreaBordersImage.Width = AreaBordersImage.Height = MapImage.Width;
        AreaBordersImage.Visibility = Visibility.Visible;
    }
}

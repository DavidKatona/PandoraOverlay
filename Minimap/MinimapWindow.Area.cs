using System.Windows;
using System.Windows.Media;

namespace PandoraOverlay;

/// <summary>
/// MinimapWindow, area part (v1.29): the top-right pill that names the area
/// you are in, from the bundled area map (AreaMap) — your own position, no
/// request. In edit mode the pill follows the CURSOR instead and names the
/// area under it, in the edit colour, so the borders can be looked over
/// without walking the island. Same class as MinimapWindow.xaml.cs, split
/// for reading; see CLAUDE.md.
/// </summary>
public partial class MinimapWindow
{
    private static readonly Brush AreaOwn = new SolidColorBrush(Color.FromRgb(0xEC, 0xF2, 0xF8));
    private static readonly Brush AreaHover = new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x64)); // the edit-mode accent

    private readonly AreaReadout _areaReadout = new();
    private string? _area;                       // where you are, settled (null = open sea / unknown)
    private (double Fx, double Fy)? _hoverSpot;  // edit mode: the map point under the cursor

    /// <summary>The bundled map, decoded on first use (~25 ms, a megabyte of grid) — never, with the setting off.</summary>
    private AreaMap? Areas => _config.MinimapAreaEnabled ? AreaMapAsset.Shared : null;

    /// <summary>A new position: the readout decides whether you are clearly in another area yet.</summary>
    private void NoteAreaFix(double fx, double fy)
    {
        _area = Areas is { } map ? map.NameOf(_areaReadout.Update(map, fx, fy)) : null;
        UpdateAreaPill();
    }

    /// <summary>Not in game: nowhere, and the next spawn starts afresh.</summary>
    private void ClearAreaFix()
    {
        _areaReadout.Reset();
        _area = null;
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
    /// as "this spot is in my area"). Hidden where there is no area.
    /// </summary>
    private void UpdateAreaPill()
    {
        var hovering = EditMode && _hoverSpot is not null;
        string? name = null;
        if (Areas is { } map)
        {
            name = hovering && _hoverSpot is { } spot ? map.NameAt(spot.Fx, spot.Fy) : _area;
        }
        if (name is null)
        {
            AreaPanel.Visibility = Visibility.Collapsed;
            return;
        }
        AreaText.Text = name;
        AreaText.Foreground = hovering ? AreaHover : AreaOwn;
        AreaPanel.Visibility = Visibility.Visible;
    }
}

using System.Windows.Media;

namespace PandoraOverlay;

/// <summary>The maps' shared brushes: the waypoint palette, frozen once for every map, the menus and the footers.</summary>
/// <remarks>The Settings Waypoints page and the Activity widget still build their own copies of the palette; moving them here was left for later.</remarks>
public static class MapBrushes
{
    public static readonly Brush[] Palette = WaypointPalette.Colours
        .Select(c =>
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(c.Hex));
            brush.Freeze();
            return (Brush)brush;
        })
        .ToArray();

    /// <summary>A waypoint's colour, any index wrapped onto the palette.</summary>
    public static Brush Of(int colour) => Palette[WaypointPalette.Wrap(colour)];
}

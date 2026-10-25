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

    // ---- The zone layers (the map's Layers menu) ---------------------------------
    // The website's own hues, sampled from its live-map overlays (owner, Oct 12
    // 2026: "same colors as the website"), so a player who knows its map reads
    // ours at once. Only the hue is the website's: how strongly a zone is tinted
    // is MapCanvas's, since the website's half-opaque fill buries the terrain.

    public static readonly Color MigrationHue = Color.FromRgb(0x79, 0xD9, 0x63);
    public static readonly Color PatrolHue = Color.FromRgb(0xA9, 0x0F, 0x33);
    public static readonly Color SanctuaryHue = Color.FromRgb(0xF0, 0x6E, 0xAA);

    public static Color HueOf(ZoneKind kind) => kind switch
    {
        ZoneKind.Migration => MigrationHue,
        ZoneKind.Patrol => PatrolHue,
        _ => SanctuaryHue,
    };

    private static readonly Brush[] ZoneBrushes = Enum.GetValues<ZoneKind>()
        .Select(k =>
        {
            var brush = new SolidColorBrush(HueOf(k));
            brush.Freeze();
            return (Brush)brush;
        })
        .ToArray();

    /// <summary>A zone kind's hue, solid: the Layers menu's swatches and the readout's ■.</summary>
    public static Brush OfZone(ZoneKind kind) => ZoneBrushes[(int)kind];

    private static readonly Brush[] SwatchBrushes = Enum.GetValues<ZoneKind>()
        .Select(k => Frozen(Color.FromArgb(0x99, HueOf(k).R, HueOf(k).G, HueOf(k).B)))
        .ToArray();

    /// <summary>A zone kind's swatch in the Layers menu: its hue at 60%, inside a solid edge (<see cref="OfZone"/>).</summary>
    public static Brush SwatchOf(ZoneKind kind) => SwatchBrushes[(int)kind];

    // ---- The map itself (1.39: moved here from the maps' XAML, so the chrome
    // there holds only theme tokens). DATA, never a theme's (PLAN.md section
    // 11): the picture's own sea and the area borders' dark navy look the same
    // in every theme (the owner's rule for the borders, Oct 2 2026).

    /// <summary>The map picture's own sea: the map's background where a view runs past the picture's edge.</summary>
    public static readonly Brush Sea = Frozen(Color.FromRgb(0x0A, 0x1D, 0x45));

    /// <summary>The area borders' dark navy (MapCanvas draws them at 70%).</summary>
    public static readonly Brush AreaBorders = Frozen(Color.FromRgb(0x00, 0x14, 0x30));

    private static Brush Frozen(Color colour)
    {
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        return brush;
    }
}

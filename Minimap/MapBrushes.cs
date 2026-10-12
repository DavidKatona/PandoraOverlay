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
}

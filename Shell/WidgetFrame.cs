namespace PandoraOverlay;

/// <summary>
/// The two fixed frame sizes every widget is built on (v1.25, owner's
/// design): the minimap and the Prime tracker share the LARGE frame, the
/// stats panel and the Activity feed the SMALL one, all one width. Each
/// window's root Border takes these as its explicit size at 100% and the
/// content lays out INSIDE them with stretching rows — so the "a widget
/// never changes size" rule is structural, not a discipline, and twin
/// widgets line up in the two-column default layout. Per-widget scale
/// sliders multiply the frame as a whole. Outer sizes, border included.
/// </summary>
public static class WidgetFrame
{
    /// <summary>Every widget's outer width. Set by the stats panel's bar layout (58 + 170 + 44 content, 12 px padding, 1 px border).</summary>
    public const double Width = 298;

    /// <summary>Minimap + Prime tracker. Set by the map: a 284 px square (Width minus the minimap's 6 px padding and border) plus its footer.</summary>
    public const double LargeHeight = 318;

    /// <summary>Stats panel + Activity feed. Set by the stats panel with its fracture row always present.</summary>
    public const double SmallHeight = 168;
}

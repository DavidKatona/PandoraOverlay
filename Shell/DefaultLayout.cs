using System.Windows;

namespace PandoraOverlay;

/// <summary>
/// Where the widgets go when they have no saved position — pure and tested.
/// One preset for now, "Columns": the Prime tracker in the top-left corner
/// with the Activity feed under it, the minimap in the top-right corner
/// with the stats panel under it, right edges aligned; the control panel
/// stays bottom-centre (it places itself). With the twin frames of
/// WidgetFrame both columns come out the same height. Sizes are passed in
/// as the windows' ACTUAL (scaled) sizes, so a 120% Prime tracker still
/// gets its Activity feed docked right under it. Written as a preset so a
/// second layout is a new method here, nothing else (owner, Sep 29 2026:
/// no presets shipped yet — positions are freely draggable).
/// </summary>
public static class DefaultLayout
{
    /// <summary>Distance from the screen edges — SnapResolver's comfort inset, so a default layout is exactly where a snap would put it.</summary>
    public const double Inset = 16;

    /// <summary>Gap between the two widgets of a column.</summary>
    public const double Gap = 8;

    /// <summary>The four top-left corners of one arrangement.</summary>
    public sealed record Placement(Point Prime, Point Activity, Point Minimap, Point Stats);

    /// <summary>The "Columns" preset for a screen of the given bounds (DIPs) and the widgets' actual sizes.</summary>
    public static Placement Columns(Rect screen, Size prime, Size activity, Size minimap, Size stats)
    {
        var top = screen.Top + Inset;
        var left = screen.Left + Inset;
        var right = screen.Right - Inset;

        var primeAt = new Point(left, top);
        var activityAt = new Point(left, top + prime.Height + Gap);
        var minimapAt = new Point(right - minimap.Width, top);
        var statsAt = new Point(right - stats.Width, top + minimap.Height + Gap);
        return new Placement(primeAt, activityAt, minimapAt, statsAt);
    }
}

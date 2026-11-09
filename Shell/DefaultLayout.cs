using System.Windows;

namespace PandoraOverlay;

/// <summary>
/// Where the widgets go when they have no saved position — pure and tested.
/// One preset for now, "Columns": the Prime tracker in the top-left corner
/// with the Activity feed under it, the minimap in the top-right corner
/// with the stats panel under it, right edges aligned, all starting under
/// the game's top HUD strip; the control panel
/// stays bottom-centre (it places itself). With the twin frames of
/// WidgetFrame both columns come out the same height. Sizes are passed in
/// as the windows' ACTUAL (scaled) sizes, so a 120% Prime tracker still
/// gets its Activity feed docked right under it. Written as a preset so a
/// second layout is a new method here, nothing else (owner, Sep 29 2026:
/// no presets shipped yet — positions are freely draggable).
/// </summary>
public static class DefaultLayout
{
    /// <summary>Distance from the screen's side edges — SnapResolver's comfort inset, so a default layout is exactly where a snap would put it.</summary>
    public const double Inset = 16;

    /// <summary>
    /// The top inset as a fraction of the screen height: the game's own HUD
    /// owns the top strip (version number, FPS and the recording camera on
    /// the left, ping and FPS on the right — about 85 px at 1080p, and it
    /// scales with the resolution), so the columns start under it. 10% is
    /// 108 px at 1080p; Inset is the floor.
    /// (Owner, Sep 29 2026, measured from a screenshot.) Centring the columns
    /// vertically was rejected: on a short screen they would run into the
    /// game's stat hexes bottom-right.
    /// </summary>
    public const double TopFraction = 0.10;

    /// <summary>Gap between the two widgets of a column.</summary>
    public const double Gap = 8;

    /// <summary>The five top-left corners of one arrangement.</summary>
    public sealed record Placement(Point Prime, Point Activity, Point Minimap, Point Stats, Point Pack);

    /// <summary>
    /// The "Columns" preset for a screen of the given bounds (DIPs) and the
    /// widgets' actual sizes. The Pack (Nov 2026) goes under the stats panel,
    /// right edges aligned, <see cref="Gap"/> between, from its own size (its
    /// frame, small or large, times its scale). Nothing flows toward the
    /// screen's centre (the owner, Nov 8 2026); a Pack that runs off the
    /// bottom is pulled up onto the screen by the widgets' own clamp when it
    /// appears and when edit mode locks.
    /// </summary>
    public static Placement Columns(Rect screen, Size prime, Size activity, Size minimap, Size stats, Size pack = default)
    {
        var top = screen.Top + Math.Max(Inset, screen.Height * TopFraction);
        var left = screen.Left + Inset;
        var right = screen.Right - Inset;

        var primeAt = new Point(left, top);
        var activityAt = new Point(left, top + prime.Height + Gap);
        var minimapAt = new Point(right - minimap.Width, top);
        var statsAt = new Point(right - stats.Width, top + minimap.Height + Gap);
        var packAt = new Point(right - pack.Width, statsAt.Y + stats.Height + Gap);
        return new Placement(primeAt, activityAt, minimapAt, statsAt, packAt);
    }
}

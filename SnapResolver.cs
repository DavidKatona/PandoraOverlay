using System.Windows;

namespace PandoraOverlay;

/// <summary>A snap target that engaged: where its guide line sits, and whether it came from the peer window (vs the screen).</summary>
public readonly record struct SnapGuide(double Position, bool FromPeer);

/// <summary>The adjusted position plus the guide that engaged per axis (null = that axis moved freely).</summary>
public readonly record struct SnapResult(Point Position, SnapGuide? GuideX, SnapGuide? GuideY);

/// <summary>
/// Pure snapping math for edit-mode dragging: given a proposed position,
/// magnetically prefers the screen edges, a comfort inset from them, and
/// the other overlay window's edges (align or abut — both fall out of the
/// same two candidates per target). Axes snap independently; the caller
/// decides when to bypass entirely (Alt held). The result reports which
/// targets engaged so the caller can draw guide lines.
/// </summary>
public static class SnapResolver
{
    /// <summary>Magnet range in DIPs. Kept smaller than Inset so the edge and
    /// inset magnets read as two distinct stops instead of a jitter.</summary>
    public const double Threshold = 12;

    /// <summary>The "comfortably away from the edge" secondary target.</summary>
    public const double Inset = 16;

    public static SnapResult Snap(Point pos, Size size, Rect bounds, IEnumerable<Rect> peers)
    {
        var xTargets = new List<(double Value, bool FromPeer)>
        {
            (bounds.Left, false), (bounds.Left + Inset, false),
            (bounds.Right, false), (bounds.Right - Inset, false)
        };
        var yTargets = new List<(double Value, bool FromPeer)>
        {
            (bounds.Top, false), (bounds.Top + Inset, false),
            (bounds.Bottom, false), (bounds.Bottom - Inset, false)
        };
        foreach (var peer in peers)
        {
            xTargets.Add((peer.Left, true));
            xTargets.Add((peer.Right, true));
            yTargets.Add((peer.Top, true));
            yTargets.Add((peer.Bottom, true));
        }

        var (x, guideX) = SnapAxis(pos.X, size.Width, xTargets);
        var (y, guideY) = SnapAxis(pos.Y, size.Height, yTargets);
        return new SnapResult(new Point(x, y), guideX, guideY);
    }

    /// <summary>
    /// The nearest position that puts the window fully inside bounds —
    /// top-left priority if the window is somehow larger than bounds. Used
    /// when locking edit mode and at startup, so a panel can never be lost
    /// off-screen (dragging itself stays free for cross-monitor moves).
    /// </summary>
    public static Point ClampIntoRect(Rect window, Rect bounds)
    {
        var x = Math.Max(bounds.Left, Math.Min(window.X, bounds.Right - window.Width));
        var y = Math.Max(bounds.Top, Math.Min(window.Y, bounds.Bottom - window.Height));
        return new Point(x, y);
    }

    private static (double Pos, SnapGuide? Guide) SnapAxis(
        double pos, double extent, List<(double Value, bool FromPeer)> targets)
    {
        var best = pos;
        SnapGuide? guide = null;
        var bestDistance = Threshold;
        foreach (var (value, fromPeer) in targets)
        {
            Consider(value, value, fromPeer);          // leading edge lands on the target
            Consider(value - extent, value, fromPeer); // trailing edge lands on the target
        }
        return (best, guide);

        void Consider(double candidate, double target, bool fromPeer)
        {
            var distance = Math.Abs(pos - candidate);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
                guide = new SnapGuide(target, fromPeer);
            }
        }
    }
}

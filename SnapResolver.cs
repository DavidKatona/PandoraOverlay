using System.Windows;

namespace PandoraOverlay;

/// <summary>
/// Pure snapping math for edit-mode dragging: given a proposed position,
/// magnetically prefers the work-area edges, a comfort inset from them, and
/// the other overlay window's edges (align or abut — both fall out of the
/// same two candidates per target). Axes snap independently; the caller
/// decides when to bypass entirely (Alt held).
/// </summary>
public static class SnapResolver
{
    /// <summary>Magnet range in DIPs.</summary>
    public const double Threshold = 14;

    /// <summary>The "comfortably away from the edge" secondary target.</summary>
    public const double Inset = 12;

    public static Point Snap(Point pos, Size size, Rect workArea, IEnumerable<Rect> peers)
    {
        var xTargets = new List<double>
        {
            workArea.Left, workArea.Left + Inset, workArea.Right, workArea.Right - Inset
        };
        var yTargets = new List<double>
        {
            workArea.Top, workArea.Top + Inset, workArea.Bottom, workArea.Bottom - Inset
        };
        foreach (var peer in peers)
        {
            xTargets.Add(peer.Left);
            xTargets.Add(peer.Right);
            yTargets.Add(peer.Top);
            yTargets.Add(peer.Bottom);
        }

        return new Point(
            SnapAxis(pos.X, size.Width, xTargets),
            SnapAxis(pos.Y, size.Height, yTargets));
    }

    private static double SnapAxis(double pos, double extent, List<double> targets)
    {
        var best = pos;
        var bestDistance = Threshold;
        foreach (var target in targets)
        {
            Consider(target);          // leading edge lands on the target
            Consider(target - extent); // trailing edge lands on the target
        }
        return best;

        void Consider(double candidate)
        {
            var distance = Math.Abs(pos - candidate);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }
    }
}

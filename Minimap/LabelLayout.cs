using System.Windows;

namespace PandoraOverlay;

/// <summary>One name to place: the caller's id for it, how much it matters (higher wins) and where it may go, the best spot first.</summary>
public sealed record LabelRequest(int Id, double Priority, IReadOnlyList<Rect> Spots);

/// <summary>A name that got a place: its id and the spot it took.</summary>
public readonly record struct PlacedLabel(int Id, Rect Box);

/// <summary>
/// The big map's declutter: names must not sit on each other. Greedy, by
/// priority — highest first, equal priorities in the caller's order. Each
/// name takes the first of its spots that keeps a gap from every name
/// already placed, or is left out; a name left out blocks nothing. Boxes
/// that only touch don't overlap. Generic on purpose: it sees rectangles,
/// never what they name, so the caller decides the priorities, and a marker
/// passed as a top-priority box keeps names off it. Placement depends only
/// on the boxes, so a pan (every box moving together) needs no new layout;
/// a zoom does. Pure, tested.
/// </summary>
public static class LabelLayout
{
    public static IReadOnlyList<PlacedLabel> Place(IEnumerable<LabelRequest> labels, double gap = 0)
    {
        var placed = new List<PlacedLabel>();
        foreach (var label in labels.OrderByDescending(l => l.Priority)) // a stable sort: ties keep their order
        {
            foreach (var spot in label.Spots)
            {
                if (spot.IsEmpty || placed.Any(p => Overlaps(p.Box, spot, gap))) continue;
                placed.Add(new PlacedLabel(label.Id, spot));
                break;
            }
        }
        return placed;
    }

    /// <summary>Two boxes closer than the gap overlap; with no gap, boxes that only touch do not.</summary>
    internal static bool Overlaps(Rect a, Rect b, double gap) =>
        a.Left < b.Right + gap && b.Left < a.Right + gap && a.Top < b.Bottom + gap && b.Top < a.Bottom + gap;
}

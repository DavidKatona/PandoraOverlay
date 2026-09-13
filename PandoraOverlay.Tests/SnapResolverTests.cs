using System.Windows;
using Xunit;

namespace PandoraOverlay.Tests;

public class SnapResolverTests
{
    private static readonly Rect WorkArea = new(0, 0, 1920, 1040);
    private static readonly Size Window = new(200, 100);
    private static readonly Rect[] NoPeers = [];

    [Fact]
    public void FarFromEverythingStaysPut()
    {
        var snapped = SnapResolver.Snap(new Point(500, 500), Window, WorkArea, NoPeers);
        Assert.Equal(new Point(500, 500), snapped);
    }

    [Fact]
    public void SnapsToTheNearestOfEdgeAndInset()
    {
        // 4 is closer to the edge (0) than to the inset (12).
        Assert.Equal(0, SnapResolver.Snap(new Point(4, 500), Window, WorkArea, NoPeers).X);
        // 9 is closer to the inset.
        Assert.Equal(SnapResolver.Inset, SnapResolver.Snap(new Point(9, 500), Window, WorkArea, NoPeers).X);
    }

    [Fact]
    public void SnapsTrailingEdgeToTheRightInset()
    {
        // Right edge at 1900 → inset target 1908 (distance 8) beats the edge 1920 (distance 20).
        var snapped = SnapResolver.Snap(new Point(1700, 500), Window, WorkArea, NoPeers);
        Assert.Equal(1908 - Window.Width, snapped.X);
    }

    [Fact]
    public void SnapsBottomEdgeToTheWorkAreaBottom()
    {
        // Bottom edge at 1045 → work-area bottom 1040 (distance 5) wins.
        var snapped = SnapResolver.Snap(new Point(500, 945), Window, WorkArea, NoPeers);
        Assert.Equal(1040 - Window.Height, snapped.Y);
    }

    [Fact]
    public void SnapsAgainstPeerEdges()
    {
        var peer = new Rect(300, 300, 200, 150);
        // Left edge near the peer's right edge (abut), top edge near the peer's top (align).
        var snapped = SnapResolver.Snap(new Point(505, 297), new Size(100, 50), WorkArea, [peer]);
        Assert.Equal(new Point(500, 300), snapped);
    }

    [Fact]
    public void OutsideTheThresholdNothingHappens()
    {
        // Past both the edge (0) and the inset (12) by more than the threshold.
        var x = SnapResolver.Inset + SnapResolver.Threshold + 1;
        var snapped = SnapResolver.Snap(new Point(x, 500), Window, WorkArea, NoPeers);
        Assert.Equal(x, snapped.X);
    }
}

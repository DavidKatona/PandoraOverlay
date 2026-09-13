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
        // Right edge at 1900 → the inset target (1920 − Inset) beats the edge itself.
        var snapped = SnapResolver.Snap(new Point(1700, 500), Window, WorkArea, NoPeers);
        Assert.Equal(WorkArea.Right - SnapResolver.Inset - Window.Width, snapped.X);
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

    [Theory]
    [InlineData(500, 500, 500, 500)]    // already inside — untouched
    [InlineData(-50, 500, 0, 500)]      // off the left
    [InlineData(1800, 500, 1720, 500)]  // off the right (right edge would be 2000)
    [InlineData(500, -30, 500, 0)]      // off the top
    [InlineData(500, 1000, 500, 940)]   // off the bottom (bottom edge would be 1100)
    public void ClampPullsTheWindowFullyIntoBounds(double x, double y, double expectedX, double expectedY)
    {
        var clamped = SnapResolver.ClampIntoRect(new Rect(x, y, Window.Width, Window.Height), WorkArea);
        Assert.Equal(new Point(expectedX, expectedY), clamped);
    }

    [Fact]
    public void OversizedWindowClampsToTopLeft()
    {
        var clamped = SnapResolver.ClampIntoRect(new Rect(100, 100, 2500, 1200), WorkArea);
        Assert.Equal(new Point(0, 0), clamped);
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

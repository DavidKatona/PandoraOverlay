using System.Windows;
using Xunit;

namespace PandoraOverlay.Tests;

public class SnapResolverTests
{
    private static readonly Rect WorkArea = new(0, 0, 1920, 1040);
    private static readonly Size Window = new(200, 100);
    private static readonly Rect[] NoPeers = [];

    [Fact]
    public void FarFromEverythingStaysPutWithNoGuides()
    {
        var result = SnapResolver.Snap(new Point(500, 500), Window, WorkArea, NoPeers);
        Assert.Equal(new Point(500, 500), result.Position);
        Assert.Null(result.GuideX);
        Assert.Null(result.GuideY);
    }

    [Fact]
    public void SnapsToTheNearestOfEdgeAndInset()
    {
        // 4 is closer to the edge (0) than to the inset.
        var atEdge = SnapResolver.Snap(new Point(4, 500), Window, WorkArea, NoPeers);
        Assert.Equal(0, atEdge.Position.X);
        Assert.Equal(new SnapGuide(0, FromPeer: false), atEdge.GuideX);

        // 9 is closer to the inset.
        var atInset = SnapResolver.Snap(new Point(9, 500), Window, WorkArea, NoPeers);
        Assert.Equal(SnapResolver.Inset, atInset.Position.X);
        Assert.Equal(new SnapGuide(SnapResolver.Inset, FromPeer: false), atInset.GuideX);
    }

    [Fact]
    public void SnapsTrailingEdgeToTheRightInsetAndReportsTheTargetAsGuide()
    {
        // Right edge at 1900 → the inset target (1920 − Inset) beats the edge itself.
        var result = SnapResolver.Snap(new Point(1700, 500), Window, WorkArea, NoPeers);
        Assert.Equal(WorkArea.Right - SnapResolver.Inset - Window.Width, result.Position.X);
        // The guide line sits at the TARGET, not at the window position.
        Assert.Equal(new SnapGuide(WorkArea.Right - SnapResolver.Inset, FromPeer: false), result.GuideX);
    }

    [Fact]
    public void SnapsBottomEdgeToTheScreenBottom()
    {
        // Bottom edge at 1045 → screen bottom 1040 (distance 5) wins.
        var result = SnapResolver.Snap(new Point(500, 945), Window, WorkArea, NoPeers);
        Assert.Equal(WorkArea.Bottom - Window.Height, result.Position.Y);
        Assert.Equal(new SnapGuide(WorkArea.Bottom, FromPeer: false), result.GuideY);
    }

    [Fact]
    public void SnapsAgainstPeerEdgesAndMarksGuidesAsPeer()
    {
        var peer = new Rect(300, 300, 200, 150);
        // Left edge near the peer's right edge (abut), top edge near the peer's top (align).
        var result = SnapResolver.Snap(new Point(505, 297), new Size(100, 50), WorkArea, [peer]);
        Assert.Equal(new Point(500, 300), result.Position);
        Assert.Equal(new SnapGuide(500, FromPeer: true), result.GuideX);
        Assert.Equal(new SnapGuide(300, FromPeer: true), result.GuideY);
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
        // Past both the edge (0) and the inset by more than the threshold.
        var x = SnapResolver.Inset + SnapResolver.Threshold + 1;
        var result = SnapResolver.Snap(new Point(x, 500), Window, WorkArea, NoPeers);
        Assert.Equal(x, result.Position.X);
        Assert.Null(result.GuideX);
    }
}

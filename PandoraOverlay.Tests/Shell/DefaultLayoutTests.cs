using System.Windows;
using Xunit;

namespace PandoraOverlay.Tests;

public class DefaultLayoutTests
{
    private static readonly Rect Screen = new(0, 0, 1920, 1080);
    private static readonly Size Large = new(WidgetFrame.Width, WidgetFrame.LargeHeight);
    private static readonly Size Small = new(WidgetFrame.Width, WidgetFrame.SmallHeight);

    [Fact]
    public void ColumnsPutPrimeAndActivityLeftMinimapAndStatsRightUnderTheHudStrip()
    {
        var p = DefaultLayout.Columns(Screen, Large, Small, Large, Small);

        const double top = 108; // 10% of 1080: clear of the game's version/FPS/camera and ping/FPS readouts
        Assert.Equal(new Point(16, top), p.Prime);
        Assert.Equal(new Point(16, top + WidgetFrame.LargeHeight + 8), p.Activity);
        Assert.Equal(new Point(1920 - 16 - WidgetFrame.Width, top), p.Minimap);
        Assert.Equal(new Point(1920 - 16 - WidgetFrame.Width, top + WidgetFrame.LargeHeight + 8), p.Stats);
    }

    [Fact]
    public void TopInsetScalesWithTheScreenAndNeverDropsUnderTheSideInset()
    {
        var p1440 = DefaultLayout.Columns(new Rect(0, 0, 2560, 1440), Large, Small, Large, Small);
        Assert.Equal(144, p1440.Prime.Y);

        var tiny = DefaultLayout.Columns(new Rect(0, 0, 800, 100), Large, Small, Large, Small);
        Assert.Equal(16, tiny.Prime.Y); // 10% of 100 is under the floor
    }

    [Fact]
    public void TwinFramesGiveEqualColumns()
    {
        var p = DefaultLayout.Columns(Screen, Large, Small, Large, Small);
        Assert.Equal(p.Activity.Y, p.Stats.Y);
        Assert.Equal(p.Activity.Y + Small.Height, p.Stats.Y + Small.Height);
    }

    [Fact]
    public void RightColumnAlignsRightEdgesWhenWidthsDiffer()
    {
        var bigMap = new Size(WidgetFrame.Width * 1.25, WidgetFrame.LargeHeight * 1.25); // minimap at 125%
        var p = DefaultLayout.Columns(Screen, Large, Small, bigMap, Small);

        Assert.Equal(1920 - 16, p.Minimap.X + bigMap.Width);
        Assert.Equal(1920 - 16, p.Stats.X + Small.Width);
        Assert.Equal(108 + bigMap.Height + 8, p.Stats.Y); // docked under the bigger map
    }

    [Fact]
    public void FollowsTheScreenOrigin()
    {
        var second = new Rect(1920, -200, 2560, 1440); // a monitor to the right, higher up
        var p = DefaultLayout.Columns(second, Large, Small, Large, Small);

        Assert.Equal(new Point(1936, -200 + 144), p.Prime);
        Assert.Equal(1920 + 2560 - 16 - WidgetFrame.Width, p.Minimap.X);
        Assert.Equal(-200 + 144, p.Minimap.Y);
    }
}

/// <summary>The Pack's place in the default layout: under the stats panel, right edges aligned, 8 apart, at either frame and any scale.</summary>
public class DefaultLayoutPackTests
{
    private static readonly Rect Screen = new(0, 0, 1920, 1080);
    private static readonly Size Large = new(WidgetFrame.Width, WidgetFrame.LargeHeight);
    private static readonly Size Small = new(WidgetFrame.Width, WidgetFrame.SmallHeight);

    public static IEnumerable<object[]> FramesAndScales() =>
        from frame in new[] { PackFrame.Small, PackFrame.Large }
        from scale in new[] { 0.75, 1.0, 1.5 }
        select new object[] { frame, scale };

    [Theory]
    [MemberData(nameof(FramesAndScales))]
    public void ThePackGoesUnderTheStatsPanel(PackFrame frame, double scale)
    {
        var pack = new Size(WidgetFrame.Width * scale, PackRows.FrameHeight(frame) * scale);
        var p = DefaultLayout.Columns(Screen, Large, Small, Large, Small, pack);
        Assert.Equal(1920 - 16, p.Pack.X + pack.Width);                 // right edges aligned
        Assert.Equal(p.Stats.X + Small.Width, p.Pack.X + pack.Width);
        Assert.Equal(p.Stats.Y + Small.Height + DefaultLayout.Gap, p.Pack.Y); // 8 under the stats panel
    }

    /// <summary>The Pack moves nothing else: the four widgets are where they were without it.</summary>
    [Fact]
    public void ThePackMovesNoOtherWidget()
    {
        var without = DefaultLayout.Columns(Screen, Large, Small, Large, Small);
        var with = DefaultLayout.Columns(Screen, Large, Small, Large, Small, Large);
        Assert.Equal((without.Prime, without.Activity, without.Minimap, without.Stats), (with.Prime, with.Activity, with.Minimap, with.Stats));
    }

    /// <summary>A bigger stats panel pushes the Pack down with it.</summary>
    [Fact]
    public void ThePackFollowsAScaledStatsPanel()
    {
        var stats = new Size(WidgetFrame.Width * 1.25, WidgetFrame.SmallHeight * 1.25);
        var p = DefaultLayout.Columns(Screen, Large, Small, Large, stats, Small);
        Assert.Equal(p.Stats.Y + stats.Height + 8, p.Pack.Y);
    }
}

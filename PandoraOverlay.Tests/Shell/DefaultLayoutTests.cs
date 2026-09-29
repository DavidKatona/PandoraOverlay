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

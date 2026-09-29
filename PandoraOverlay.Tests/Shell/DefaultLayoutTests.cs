using System.Windows;
using Xunit;

namespace PandoraOverlay.Tests;

public class DefaultLayoutTests
{
    private static readonly Rect Screen = new(0, 0, 1920, 1080);
    private static readonly Size Large = new(WidgetFrame.Width, WidgetFrame.LargeHeight);
    private static readonly Size Small = new(WidgetFrame.Width, WidgetFrame.SmallHeight);

    [Fact]
    public void ColumnsPutPrimeAndActivityLeftMinimapAndStatsRight()
    {
        var p = DefaultLayout.Columns(Screen, Large, Small, Large, Small);

        Assert.Equal(new Point(16, 16), p.Prime);
        Assert.Equal(new Point(16, 16 + WidgetFrame.LargeHeight + 8), p.Activity);
        Assert.Equal(new Point(1920 - 16 - WidgetFrame.Width, 16), p.Minimap);
        Assert.Equal(new Point(1920 - 16 - WidgetFrame.Width, 16 + WidgetFrame.LargeHeight + 8), p.Stats);
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
        Assert.Equal(16 + bigMap.Height + 8, p.Stats.Y); // docked under the bigger map
    }

    [Fact]
    public void FollowsTheScreenOrigin()
    {
        var second = new Rect(1920, -200, 2560, 1440); // a monitor to the right, higher up
        var p = DefaultLayout.Columns(second, Large, Small, Large, Small);

        Assert.Equal(new Point(1936, -184), p.Prime);
        Assert.Equal(1920 + 2560 - 16 - WidgetFrame.Width, p.Minimap.X);
        Assert.Equal(-184, p.Minimap.Y);
    }
}

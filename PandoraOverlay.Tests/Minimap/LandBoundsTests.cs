using System.Windows;
using Xunit;

namespace PandoraOverlay.Tests;

public sealed class LandBoundsTests
{
    private const string Red = "#FF0000", Green = "#00FF00";

    private static readonly AreaLegend TwoAreas = new("test", "2026-10-09", new[]
    {
        new AreaEntry("Alpha", Red, 0, 0), new AreaEntry("Beta", Green, 0, 0)
    });

    /// <summary>A 100 px area map from a per-pixel "#RRGGBB" (null = transparent, no area).</summary>
    private static AreaMap Map(Func<int, int, string?> colourAt)
    {
        const int size = 100;
        var bgra = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                if (colourAt(x, y) is not { } hex || !AreaMap.TryParseColour(hex, out var rgb)) continue;
                var o = (y * size + x) * 4;
                bgra[o] = (byte)rgb;
                bgra[o + 1] = (byte)(rgb >> 8);
                bgra[o + 2] = (byte)(rgb >> 16);
                bgra[o + 3] = 255;
            }
        }
        return AreaMap.FromPixels(size, size, bgra, TwoAreas)!;
    }

    private static void AssertRect(Rect expected, Rect? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.X, actual!.Value.X, 12);
        Assert.Equal(expected.Y, actual.Value.Y, 12);
        Assert.Equal(expected.Width, actual.Value.Width, 12);
        Assert.Equal(expected.Height, actual.Value.Height, 12);
    }

    [Fact]
    public void TheBoxHoldsEveryPixelOfAnAreaEdgesIncluded()
    {
        var map = Map((x, y) => x is >= 20 and < 40 && y is >= 30 and < 60 ? Red : null);

        AssertRect(new Rect(0.2, 0.3, 0.2, 0.3), LandBounds.Of(map));
    }

    [Fact]
    public void SeparateAreasShareOneBox()
    {
        var map = Map((x, y) => (x, y) switch
        {
            ( >= 10 and < 20, >= 10 and < 20) => Red,
            ( >= 70 and < 75, >= 80 and < 90) => Green,
            _ => null
        });

        AssertRect(new Rect(0.1, 0.1, 0.65, 0.8), LandBounds.Of(map));
    }

    [Fact]
    public void OnePixelIsAOnePixelBox()
    {
        AssertRect(new Rect(0.42, 0.07, 0.01, 0.01), LandBounds.Of(Map((x, y) => x == 42 && y == 7 ? Green : null)));
    }

    [Fact]
    public void AMapWithNoAreaHasNoBox()
    {
        Assert.Null(LandBounds.Of(Map((_, _) => null)));
    }

    [Fact]
    public void AColourOutsideTheLegendIsNotLand()
    {
        // It reads as no area everywhere else, so it doesn't stretch the box either.
        var map = Map((x, y) => x < 50 ? (y < 50 ? Red : null) : (y > 90 ? "#123456" : null));

        AssertRect(new Rect(0, 0, 0.5, 0.5), LandBounds.Of(map));
    }

    [Fact]
    public void AMapThatIsAllAreaFillsTheWholeBox()
    {
        AssertRect(new Rect(0, 0, 1, 1), LandBounds.Of(Map((_, _) => Red)));
    }

    [Fact]
    public void EachAreasPixelsAreCounted()
    {
        var map = Map((x, y) => x < 30 ? Red : x < 40 ? Green : y < 50 ? "#123456" : null); // a colour outside the legend counts for nobody

        Assert.Equal(new[] { 3000, 1000 }, map.PixelCounts());
        Assert.Equal(new[] { 0, 0 }, Map((_, _) => null).PixelCounts());
    }

    [Fact]
    public void TheBundledMapsBoxIsOnTheMapAndHoldsEveryLabelPoint()
    {
        // Holds for any generated map: names no area, counts on nothing about this island.
        var map = AreaMapAsset.LoadBundled()!;
        var calibration = map.Legend.Calibration!;

        var box = LandBounds.Of(map);

        Assert.NotNull(box);
        Assert.True(box!.Value.Left >= 0 && box.Value.Top >= 0 && box.Value.Right <= 1 && box.Value.Bottom <= 1);
        foreach (var area in map.Areas)
        {
            var (fx, fy) = calibration.ToFraction(area.X, area.Y);
            Assert.True(box.Value.Contains(fx, fy));
        }
    }
}

using PandoraOverlay.Games.Engine;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The games' growth lines are the overlay's own (A07). The engine can't
/// reference the app (A2), so it keeps a copy of <see cref="GrowthMilestones"/>'
/// lines in <see cref="GrowthStages"/>; this test, the one place that sees
/// both, holds the copy equal, so neither side can move a line alone and a
/// game never says a dino reached a stage the overlay's stats panel and
/// Activity feed would not.
/// </summary>
public sealed class GrowthLinesInStepTests
{
    /// <summary>The engine's four lines, in order.</summary>
    private static readonly double[] EngineLines =
    {
        GrowthStages.Juvenile, GrowthStages.Subadult, GrowthStages.Adult, GrowthStages.Elder
    };

    /// <summary>The overlay's lines, read by walking its own NextLine from 0 to the end.</summary>
    private static List<double> OverlayLines()
    {
        var lines = new List<double>();
        var at = 0.0;
        while (GrowthMilestones.NextLine(at) is { } next)
        {
            lines.Add(next.Threshold);
            at = next.Threshold;
            Assert.True(lines.Count <= 10, "NextLine never ran out of lines.");
        }
        return lines;
    }

    [Fact]
    public void TheFullGrownLineIsTheOverlays()
    {
        Assert.Equal(GrowthMilestones.FullyGrown, GrowthStages.Elder);
    }

    [Fact]
    public void TheLinesAreTheOverlaysOneForOne()
    {
        Assert.Equal(EngineLines, OverlayLines());
    }

    [Fact]
    public void JustUnderEachLineTheOverlayCountsToThatLine()
    {
        foreach (var line in EngineLines)
        {
            var under = Math.BitDecrement(line);
            Assert.Equal(line, GrowthMilestones.NextLine(under)?.Threshold);
            Assert.Equal(GrowthStages.IndexOf(under) + 1, GrowthStages.IndexOf(line));
        }
    }

    [Fact]
    public void OnEachLineTheOverlayIsPastIt()
    {
        for (var i = 0; i < EngineLines.Length; i++)
        {
            var next = GrowthMilestones.NextLine(EngineLines[i]);
            if (i + 1 < EngineLines.Length) Assert.Equal(EngineLines[i + 1], next?.Threshold);
            else Assert.Null(next);   // from the full-grown line on there is no line left
            Assert.Equal(i + 1, GrowthStages.IndexOf(EngineLines[i]));
        }
    }

    [Fact]
    public void EveryThousandthSitsInTheSameStageOnBothSides()
    {
        // The overlay's StageAt names the stage a growth sits in; the engine
        // numbers it. Over the website's three decimals they agree.
        var names = new[] { "hatchling", "juvenile", "subadult", "adult", "elder" };
        for (var k = 0; k <= 1000; k++)
        {
            var g = k / 1000.0;
            Assert.Equal(GrowthMilestones.StageAt(g), names[GrowthStages.IndexOf(g)]);
        }
        Assert.Equal(GrowthMilestones.StageAt(GrowthStages.Elder), names[GrowthStages.IndexOf(GrowthStages.Elder)]);
        Assert.Equal(GrowthMilestones.StageAt(0.9994), names[GrowthStages.IndexOf(0.9994)]);
    }
}

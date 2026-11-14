using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// What the balance work may turn, and how far (H33): a species' threat between 0.70
/// and 1.40; a danger's threat by at most 0.15 and its outlast by at most 2 turns from
/// the plan's first pass. Anything beyond is the planner's decision, so a value past
/// these limits fails here before it can pass a balance test.
/// </summary>
public class KnobTests
{
    private const double SpeciesLeast = 0.70;
    private const double SpeciesMost = 1.40;
    private const double DangerThreatStep = 0.15;
    private const int OutlastStep = 2;

    // The plan's first pass, danger 1 to 5 (H15).
    private static readonly double[] FirstThreat = { 1.00, 1.20, 1.40, 1.60, 1.85 };
    private static readonly int[] FirstOutlast = { 15, 15, 17, 19, 21 };

    [Fact]
    public void EverySpeciesThreatIsInItsRange() =>
        Assert.All(SpeciesTable.All, s => Assert.InRange(s.Threat, SpeciesLeast, SpeciesMost));

    [Fact]
    public void EveryDangerMovedNoFurtherThanAllowed()
    {
        for (var d = 0; d < Dangers.Highest; d++)
        {
            var row = Dangers.All[d];
            Assert.InRange(row.Threat, FirstThreat[d] - DangerThreatStep - 1e-9, FirstThreat[d] + DangerThreatStep + 1e-9);
            Assert.InRange(row.Outlast, FirstOutlast[d] - OutlastStep, FirstOutlast[d] + OutlastStep);
        }
    }

    [Fact]
    public void EachDangerIsStillTougherThanTheOneBelow()
    {
        for (var d = 1; d < Dangers.Highest; d++)
        {
            Assert.True(Dangers.All[d].Threat > Dangers.All[d - 1].Threat);
            Assert.True(Dangers.All[d].Outlast >= Dangers.All[d - 1].Outlast);
        }
    }
}

using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// What the balance's knobs must keep (H33): every species' threat between 0.70 and
/// 1.40, and each danger tougher than the one below it. The limit on how far a
/// danger's threat and turns may move from the first pass is gone (H69): it was the
/// balance task's, not the game's, and <c>TablesTests</c> pins every danger's row
/// exactly, so a danger that moves is a decision there, not a drift here.
/// </summary>
public class KnobTests
{
    private const double SpeciesLeast = 0.70;
    private const double SpeciesMost = 1.40;

    [Fact]
    public void EverySpeciesThreatIsInItsRange() =>
        Assert.All(SpeciesTable.All, s => Assert.InRange(s.Threat, SpeciesLeast, SpeciesMost));

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

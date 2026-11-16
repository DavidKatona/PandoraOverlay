using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The game's numbers: the one rounding rule (H24) and the scan that keeps it the
/// only one, and the few numbers whose sums other numbers depend on.
/// </summary>
public class TuningTests
{
    // ---- Round: halves up (H24) -------------------------------------------------------

    [Theory]
    [InlineData(0.5, 1)]
    [InlineData(1.5, 2)]
    [InlineData(2.5, 3)]
    [InlineData(-0.5, 0)]
    [InlineData(-1.5, -1)]
    [InlineData(0.49, 0)]
    [InlineData(7, 7)]
    [InlineData(-2.6, -3)]
    public void RoundSendsHalvesUpAsThePrototypesMathRoundDoes(double x, double expected)
    {
        Assert.Equal(expected, Tuning.Round(x));
    }

    [Theory]
    [InlineData(1.25, 1.3)]
    [InlineData(1.35, 1.4)]
    [InlineData(2.04, 2.0)]
    [InlineData(2.96, 3.0)]
    [InlineData(-0.05, 0.0)]
    public void Round1IsRoundToOneDecimal(double x, double expected)
    {
        Assert.Equal(expected, Tuning.Round1(x), 12);
    }

    /// <summary>
    /// The scan's word, built here so that this file itself says it only once and
    /// the scan's own source can't be mistaken for game code.
    /// </summary>
    private static readonly string NetRounding = "Math" + ".Round";

    /// <summary>
    /// .NET's rounding sends halves to the even neighbour; the prototype's sends them
    /// up, and a port that mixed the two would drift (H24). So nothing in the game's
    /// project names it, in code or in a comment: every rounding is Tuning's.
    /// </summary>
    [Fact]
    public void NoFileOfTheGameNamesDotNetsRounding()
    {
        var files = GameSource.Files();
        Assert.NotEmpty(files);
        Assert.Contains(files, f => f.Path == "Data/Tuning.cs");   // the scan reaches the game's source
        Assert.Empty(GameSource.LinesHolding(files, NetRounding));
    }

    [Fact]
    public void TheRoundingScanFindsABadLine()
    {
        var bad = new[]
        {
            ("Rules/Fine.cs", "var x = Tuning.Round(y);\n"),
            ("Rules/Bad.cs", "var a = 1;\nvar b = " + NetRounding + "(y, MidpointRounding.AwayFromZero);\n"),
        };
        Assert.Equal(new[] { "Rules/Bad.cs:2" }, GameSource.LinesHolding(bad, NetRounding));
    }

    [Fact]
    public void TheScanSkipsTheBuildsOutputAtAnyDepth()
    {
        Assert.True(GameSource.IsBuildOutput("bin/Debug/x.cs"));
        Assert.True(GameSource.IsBuildOutput("obj/x.cs"));
        Assert.True(GameSource.IsBuildOutput("Art/obj/x.json"));
        Assert.False(GameSource.IsBuildOutput("Data/Tuning.cs"));
        Assert.False(GameSource.IsBuildOutput("Data/obj.cs"));
    }

    // ---- Numbers other numbers depend on ----------------------------------------------

    [Fact]
    public void TheBoardIsElevenByEleven()
    {
        Assert.Equal(11, Tuning.Columns);
        Assert.Equal(11, Tuning.Rows);
        Assert.Equal(121, Tuning.Cells);
    }

    [Fact]
    public void TheStagesStarsAndPayAreThePlans()
    {
        Assert.Equal(new[] { 25.0, 50.0, 75.0, 100.0 }, Tuning.StageLines);
        Assert.Equal(new[] { 50.0, 150.0, 300.0 }, Tuning.StarsAt);
        Assert.Equal(new[] { 5, 10, 20, 40 }, Tuning.StagePay);
        Assert.Equal(new[] { 60, 200, 800, 4000 }, Tuning.TierPrice);
        Assert.Equal(new[] { 500, 1500, 3000, 6000 }, Tuning.InstinctTierPrice);
        Assert.Equal(0x9E3779B9u, Tuning.SeedStep);
    }

    /// <summary>The plan's own worked cases for a run's pay (H9), from the named numbers.</summary>
    [Fact]
    public void ThePaysWorkedExamplesFollowFromTheNumbers()
    {
        // A fresh run that ends at 55% as a Subadult with one predator defeated: 27 + 5 + 10 + 3.
        var subadult = Math.Floor(55 * Tuning.GrowthPay) + Tuning.StagePay[0] + Tuning.StagePay[1] + Tuning.KillPay;
        Assert.Equal(45, subadult);
        // A fresh win at danger 1 with four defeated: 50 + 75 + 12 + 60.
        var win = Math.Floor(100 * Tuning.GrowthPay) + Tuning.StagePay.Sum() + 4 * Tuning.KillPay + Tuning.WinPay;
        Assert.Equal(197, win);
    }
}

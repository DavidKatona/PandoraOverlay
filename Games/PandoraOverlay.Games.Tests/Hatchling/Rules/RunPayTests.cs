using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// What a run pays (<see cref="RunPay"/>, H8, H9): only growth gained and lines
/// crossed in the run, the predators defeated without the apex, the win, then the
/// danger's and Legacy's shares rounded halves up (H24). The design's two worked
/// examples, a run left at once (with and without Strong start), each line alone,
/// and the total as the lines' sum.
/// </summary>
public class RunPayTests
{
    /// <summary>A run that began at <paramref name="start"/> and reached <paramref name="best"/>, with <paramref name="kills"/> defeated, ended as given (no Over when <paramref name="reason"/> is null).</summary>
    private static RunState Ended(double start, double best, int kills = 0, EndReason? reason = EndReason.Caught,
        int danger = 1, int legacy = 0)
    {
        var run = Turns.Board("Dryosaurus", growth: best).With(Runs.Ranks((Trait.Legacy, legacy)), 0);
        run.You.Start = start;
        run.Danger = danger;
        run.Kills = kills;
        if (reason is { } why)
            run.Over = new RunOver { Won = why is EndReason.Outlasted or EndReason.DefeatedApex, Reason = why };
        return run;
    }

    private static int Total(RunState run) => RunPay.Total(RunPay.Of(run));

    [Fact]
    public void TheDesignsSubadultExamplePays45()
    {
        var lines = RunPay.Of(Ended(0, 55, kills: 1));
        Assert.Equal(new[]
        {
            new PayLine(PayKind.Grew, 55, 27),
            new PayLine(PayKind.Reached, Stages.Juvenile, 5),
            new PayLine(PayKind.Reached, Stages.Subadult, 10),
            new PayLine(PayKind.Defeated, 1, 3),
        }, lines);
        Assert.Equal(45, RunPay.Total(lines));
    }

    [Fact]
    public void TheDesignsWinPays197()
    {
        var lines = RunPay.Of(Ended(0, 100, kills: 4, reason: EndReason.Outlasted));
        Assert.Equal(new[]
        {
            new PayLine(PayKind.Grew, 100, 50),
            new PayLine(PayKind.Reached, Stages.Juvenile, 5),
            new PayLine(PayKind.Reached, Stages.Subadult, 10),
            new PayLine(PayKind.Reached, Stages.Adult, 20),
            new PayLine(PayKind.Reached, Stages.Elder, 40),
            new PayLine(PayKind.Defeated, 4, 12),
            new PayLine(PayKind.Outlasted, 0, 60),
        }, lines);
        Assert.Equal(197, RunPay.Total(lines));
    }

    [Fact]
    public void ARunLeftAtTurnZeroPaysNothing()
    {
        var left = Turns.Must(Runs.Fresh("Dryosaurus", 7), new PlayerAction.Leave()).After;
        Assert.Empty(left.Over!.Lines);
        Assert.Equal(0, left.Over.Total);
    }

    [Fact]
    public void ARunLeftAtTurnZeroWithStrongStartPaysNothing()
    {
        // H8: it began at 30, past Juvenile, and crossed nothing.
        var fresh = Runs.Fresh("Dryosaurus", 7, Runs.Ranks((Trait.StrongStart, 3)), 0);
        Assert.Equal(30, fresh.You.Start);
        var left = Turns.Must(fresh, new PlayerAction.Leave()).After;
        Assert.Empty(left.Over!.Lines);
        Assert.Equal(0, left.Over.Total);
    }

    [Fact]
    public void GrowthPaysOnlyWhatWasGainedInTheRun()
    {
        Assert.Equal(new[] { new PayLine(PayKind.Grew, 15, 7), new PayLine(PayKind.Reached, Stages.Subadult, 10) },
            RunPay.Of(Ended(40, 55.9)));                                   // 15.9 gained: 15%, 7 floored
        Assert.Equal(new[] { new PayLine(PayKind.Grew, 1, 0) }, RunPay.Of(Ended(30, 31.4)));   // a percent, worth less than one
        Assert.Empty(RunPay.Of(Ended(30, 30.9)));                          // under a percent: no line
    }

    /// <summary>Growth from 0 after <paramref name="turns"/> base turns, added one turn at a time as the world adds it.</summary>
    private static double Summed(int turns)
    {
        var growth = 0.0;
        for (var k = 0; k < turns; k++) growth += Tuning.GrowthPerTurn;
        return growth;
    }

    [Theory]
    [InlineData(20, 28, 14)]
    [InlineData(15, 21, 10)]
    public void GrowthSummedTurnByTurnPaysItsWholePercents(int turns, int percent, int pay)
    {
        // H58: twenty base turns sum to 27.999999999999989, which is 28.
        var best = Summed(turns);
        Assert.True(best < percent, "the test's premise: the sum sits under the whole number");
        Assert.Equal(new PayLine(PayKind.Grew, percent, pay), RunPay.Of(Ended(0, best))[0]);
    }

    [Theory]
    [InlineData(27.5)]
    [InlineData(27.999)]
    public void TheSlackRoundsNothingUp(double best)
    {
        Assert.Equal(new PayLine(PayKind.Grew, 27, 13), RunPay.Of(Ended(0, best))[0]);
    }

    [Fact]
    public void AHairUnderALineCrossesNothingButItsPercentCounts()
    {
        // The stage lines take no slack: they agree with Stages.Of, which said Hatchling.
        var best = 25 - 1e-12;
        Assert.Equal(Stages.Hatchling, Stages.Of(best));
        Assert.Equal(new[] { new PayLine(PayKind.Grew, 25, 12) }, RunPay.Of(Ended(0, best)));
    }

    [Fact]
    public void ALineIsPaidOnlyWhenCrossedInTheRun()
    {
        // Begun on a line is past it: nothing for it.
        Assert.DoesNotContain(RunPay.Of(Ended(25, 30)), l => l.Kind == PayKind.Reached);
        // Reached exactly is crossed.
        Assert.Contains(new PayLine(PayKind.Reached, Stages.Subadult, 10), RunPay.Of(Ended(30, 50)));
        Assert.Equal(new[] { Stages.Adult, Stages.Elder },
            RunPay.Of(Ended(70, 100)).Where(l => l.Kind == PayKind.Reached).Select(l => l.Value));
    }

    [Fact]
    public void PredatorsDefeatedPayThreeEachAndTheApexIsNotOne()
    {
        Assert.Equal(new[] { new PayLine(PayKind.Defeated, 5, 15) }, RunPay.Of(Ended(30, 30, kills: 5)));
        var slain = RunPay.Of(Ended(100, 100, kills: 0, reason: EndReason.DefeatedApex));
        Assert.Equal(new[] { new PayLine(PayKind.DefeatedApex, 0, 60) }, slain);
    }

    [Fact]
    public void OnlyAWinHasAWinsLine()
    {
        foreach (var reason in new[] { EndReason.Caught, EndReason.Hunger, EndReason.Thirst, EndReason.Left })
            Assert.Empty(RunPay.Of(Ended(30, 30, reason: reason)));
        Assert.Empty(RunPay.Of(Ended(30, 30, reason: null)));   // a run still going
        Assert.Equal(new[] { new PayLine(PayKind.Outlasted, 0, 60) }, RunPay.Of(Ended(100, 100, reason: EndReason.Outlasted)));
    }

    [Theory]
    [InlineData(2, 45, 11)]   // 56.25
    [InlineData(3, 3, 2)]     // 4.5: halves up
    [InlineData(3, 45, 23)]   // 67.5
    [InlineData(4, 45, 34)]   // 78.75
    [InlineData(5, 45, 45)]
    public void TheDangersShareIsItsPayOnTheSumRoundedHalvesUp(int danger, int sum, int share)
    {
        var run = SumOf(sum, danger: danger);
        var lines = RunPay.Of(run);
        Assert.Equal(new PayLine(PayKind.Danger, danger, share), lines[^1]);
        Assert.Equal(sum + share, RunPay.Total(lines));
    }

    [Fact]
    public void DangerOneHasNoLineAndNothingHasNoShare()
    {
        Assert.DoesNotContain(RunPay.Of(SumOf(45, danger: 1)), l => l.Kind == PayKind.Danger);
        Assert.Empty(RunPay.Of(Ended(30, 30, danger: 5)));
    }

    [Theory]
    [InlineData(1, 15, 1, 2)]    // 16.5: halves up
    [InlineData(2, 45, 1, 9)]    // 54
    [InlineData(3, 45, 1, 14)]   // 58.5
    [InlineData(1, 45, 3, 7)]    // danger 3: 68, then 74.8, so 75
    public void LegacysShareIsOnTheSumWithTheDangersInIt(int legacy, int sum, int danger, int share)
    {
        var lines = RunPay.Of(SumOf(sum, danger: danger, legacy: legacy));
        Assert.Equal(new PayLine(PayKind.Legacy, 10 * legacy, share), lines[^1]);
        var before = RunPay.Total(lines) - share;
        Assert.Equal((int)Tuning.Round(before * (1 + 0.1 * legacy)), RunPay.Total(lines));
    }

    [Fact]
    public void NoLegacyNoLine()
    {
        Assert.DoesNotContain(RunPay.Of(SumOf(45)), l => l.Kind == PayKind.Legacy);
        Assert.Empty(RunPay.Of(Ended(30, 30, legacy: 3)));
    }

    [Fact]
    public void NoLineIsEverNegativeAndTheTotalIsTheirSum()
    {
        foreach (var start in new[] { 0.0, 10, 20, 30 })
            foreach (var best in new[] { 0.0, 12.3, 49.99, 50, 74.5, 100 })
                foreach (var danger in new[] { 1, 2, 3, 4, 5 })
                    foreach (var legacy in new[] { 0, 1, 3 })
                        foreach (var reason in new EndReason?[] { null, EndReason.Caught, EndReason.Outlasted, EndReason.DefeatedApex })
                        {
                            // Best below Start can't happen in a run; the rule still pays nothing negative for it.
                            var lines = RunPay.Of(Ended(start, best, kills: (int)best % 4, reason: reason, danger: danger, legacy: legacy));
                            Assert.All(lines, l => Assert.True(l.Amount >= 0, l.ToString()));
                            Assert.Equal(lines.Sum(l => l.Amount), RunPay.Total(lines));
                        }
    }

    [Fact]
    public void TheLinesComeInTheTablesOrder()
    {
        var kinds = RunPay.Of(Ended(0, 100, kills: 2, reason: EndReason.DefeatedApex, danger: 4, legacy: 2)).Select(l => l.Kind).ToList();
        Assert.Equal(new[]
        {
            PayKind.Grew, PayKind.Reached, PayKind.Reached, PayKind.Reached, PayKind.Reached,
            PayKind.Defeated, PayKind.DefeatedApex, PayKind.Danger, PayKind.Legacy,
        }, kinds);
    }

    [Fact]
    public void PayingChangesNothing()
    {
        var run = Ended(0, 55, kills: 1);
        var before = Runs.Fingerprint(run);
        RunPay.Of(run);
        Assert.Equal(before, Runs.Fingerprint(run));
    }

    /// <summary>A caught run whose only line before the shares is its kills, summing to <paramref name="sum"/> (a multiple of 3).</summary>
    private static RunState SumOf(int sum, int danger = 1, int legacy = 0)
    {
        Assert.True(sum % Tuning.KillPay == 0, "the test's sums are kills");
        var run = Ended(30, 30, kills: sum / Tuning.KillPay, danger: danger, legacy: legacy);
        Assert.Equal(sum, RunPay.Of(run).Where(l => l.Kind == PayKind.Defeated).Sum(l => l.Amount));
        return run;
    }
}

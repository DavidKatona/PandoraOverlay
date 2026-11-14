namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// What a run pays when it ends, line by line (H8, H9). A run pays only for what it
/// gained: the growth from where it began (<see cref="You.Start"/>, which Strong start
/// raises) to the most it reached, and the stage lines crossed between the two. So a
/// run left at once pays nothing, whatever the lineage (H8: with Strong start the
/// prototype paid 40 for two clicks).
///
/// <para>The lines, in the summary's order: Grew N%; Reached each stage crossed;
/// Predators defeated (the apex is a win, never a kill); Outlasted or Defeated the
/// apex; Danger d, the danger's share on top of those; Legacy +N%, the lineage's
/// share on top of all of it. Each line is there only when it counts something:
/// a percent grown, a line crossed, a predator defeated, a win, and the two shares
/// only on a sum above 0 (above danger 1, and with a rank of Legacy). No line is
/// ever negative. The total is the lines' sum, so the summary's column always adds up.</para>
///
/// <para>Pure: it reads the run and changes nothing. It reads how the run ended from
/// <see cref="RunState.Over"/> (the win's line); a run still going is paid as if it
/// ended with no win.</para>
/// </summary>
internal static class RunPay
{
    /// <summary>The run's pay lines, in the table's order (see the class).</summary>
    public static IReadOnlyList<PayLine> Of(RunState run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var lines = new List<PayLine>();
        var you = run.You;

        // Grew N%: the whole percents gained, at GrowthPay each, both floored.
        var gained = Math.Max(0, you.Best - you.Start);
        var percent = (int)Math.Floor(gained);
        if (percent > 0) lines.Add(new PayLine(PayKind.Grew, percent, (int)Math.Floor(gained * Tuning.GrowthPay)));

        // A line crossed IN this run: begun under it, reached it (a value on a line is past it).
        for (var line = 0; line < Tuning.StageLines.Count; line++)
        {
            var at = Tuning.StageLines[line];
            if (you.Start < at && at <= you.Best)
                lines.Add(new PayLine(PayKind.Reached, line + 1, Tuning.StagePay[line]));
        }

        if (run.Kills > 0) lines.Add(new PayLine(PayKind.Defeated, run.Kills, run.Kills * Tuning.KillPay));

        if (run.Over is { Won: true } over)
            lines.Add(new PayLine(over.Reason == EndReason.DefeatedApex ? PayKind.DefeatedApex : PayKind.Outlasted, 0, Tuning.WinPay));

        // The danger's share, then Legacy's on the sum with the danger's in it; each
        // rounded halves up (H24), as the difference the factor makes.
        var sum = Total(lines);
        if (run.Danger > Dangers.Lowest && sum > 0)
        {
            var amount = (int)Tuning.Round(sum * Dangers.Of(run.Danger).Pay) - sum;
            lines.Add(new PayLine(PayKind.Danger, run.Danger, Math.Max(0, amount)));
            sum = Total(lines);
        }
        var legacy = run.Rank(Trait.Legacy);
        if (legacy > 0 && sum > 0)
        {
            var amount = (int)Tuning.Round(sum * (1 + Tuning.LegacyStep * legacy)) - sum;
            lines.Add(new PayLine(PayKind.Legacy, (int)Tuning.Round(Tuning.LegacyStep * 100 * legacy), Math.Max(0, amount)));
        }
        return lines;
    }

    /// <summary>The lines' sum: what the run banks.</summary>
    public static int Total(IReadOnlyList<PayLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var total = 0;
        foreach (var line in lines) total += line.Amount;
        return total;
    }
}

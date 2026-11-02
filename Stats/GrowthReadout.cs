namespace PandoraOverlay;

/// <summary>
/// The stats panel header's growth text, pure and tested; MainWindow paints
/// it and colours it by the tracker's status. The owner's forms (Nov 2 2026):
/// "64% · ~1h 20m to Adult" while growing (the time to the NEXT stage at the
/// measured rate, GrowthTracker's, and the stage it reaches); "64%" while the
/// rate is still measured (the first five minutes of a life); "64% · paused"
/// on a full baseline with no growth; "Elder" from the full-grown line. The
/// word "Growth" and the decimal are gone: the percent is floored like Dino
/// storage's (<see cref="Percent"/>), so 99.96% reads "99% · ~2m to Elder"
/// and only the full-grown line reads as Elder.
/// </summary>
public static class GrowthReadout
{
    /// <summary>The header's growth text for the tracker's status, the growth and, while growing, its Eta and NextStage.</summary>
    public static string Text(GrowthTracker.GrowthStatus status, double growth, TimeSpan? eta, int? nextStage) => status switch
    {
        GrowthTracker.GrowthStatus.Full => GrowthMilestones.StageTitle(100),
        GrowthTracker.GrowthStatus.Growing when eta is { } time && nextStage is { } next =>
            $"{Percent(growth)}% · ~{FormatEta(time)} to {GrowthMilestones.StageTitle(next)}",
        GrowthTracker.GrowthStatus.Paused => $"{Percent(growth)}% · paused",
        _ => $"{Percent(growth)}%"
    };

    /// <summary>
    /// The growth as a whole percent, rounded down so 99.6% never reads as
    /// full (100 only from the full-grown line), with a hair of slack because
    /// 0.58 × 100 is 57.999… in floating point. Dino storage's cards read it too.
    /// </summary>
    public static int Percent(double growth) =>
        growth >= GrowthMilestones.FullyGrown ? 100 : (int)Math.Floor(Math.Clamp(growth, 0, 1) * 100 + 1e-9);

    /// <summary>"~"-worthy in-game time: "1m" under a minute, "45m", "3h 10m", "1d 4h".</summary>
    public static string FormatEta(TimeSpan eta)
    {
        if (eta.TotalMinutes < 1) return "1m";
        if (eta.TotalHours < 1) return $"{eta.TotalMinutes:0}m";
        if (eta.TotalDays < 1) return $"{(int)eta.TotalHours}h {eta.Minutes:00}m";
        return $"{(int)eta.TotalDays}d {eta.Hours}h";
    }
}

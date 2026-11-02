namespace PandoraOverlay;

/// <summary>
/// Spots the moment growth crosses one of the game's stage lines — 25%
/// juvenile, 50% subadult, 75% adult, 100% elder (the owner's word for the
/// last stage, Nov 2 2026, everywhere a player reads it) — pure and
/// tested. Only a crossing seen during this life counts: the first sample of
/// a life sets the baseline and never alerts (spawning in at 60% is not
/// "reaching subadult"). A poll gap that jumps over two lines reports the
/// higher one. Resets on death/dino swap so the next life crosses them anew.
/// </summary>
public sealed class GrowthMilestones
{
    /// <summary>GrowthTracker's full-grown line: the server reports a grown dino a hair under 1.</summary>
    public const double FullyGrown = 0.9995;

    private static readonly (double Threshold, int Percent)[] Lines =
    {
        (0.25, 25), (0.50, 50), (0.75, 75), (FullyGrown, 100)
    };

    /// <summary>
    /// The first stage line above this growth, with the milestone it marks:
    /// 64% → (0.75, 75). A value on a line is past it (75% → the full-grown
    /// line, 100); null from the full-grown line on. What GrowthTracker counts to.
    /// </summary>
    public static (double Threshold, int Percent)? NextLine(double growth)
    {
        foreach (var line in Lines)
        {
            if (growth < line.Threshold) return line;
        }
        return null;
    }

    private string? _identity;
    private double? _last;

    public void Reset()
    {
        _identity = null;
        _last = null;
    }

    /// <summary>The milestone (25/50/75/100) crossed by this sample, or null.</summary>
    public int? Update(PlayerState p) => Update($"{p.SteamId}|{p.Dino}|{p.Gender}", p.Growth);

    /// <summary>The same rule for any life identified by a string — the friends feed runs one per friend.</summary>
    public int? Update(string identity, double growth)
    {
        if (identity != _identity || (_last is { } l && growth < l - 0.001))
        {
            _identity = identity;
            _last = null;
        }

        int? crossed = null;
        if (_last is { } prev)
        {
            foreach (var (threshold, percent) in Lines)
            {
                if (prev < threshold && growth >= threshold) crossed = percent;
            }
        }
        _last = growth;
        return crossed;
    }

    /// <summary>"a juvenile", "a subadult", "an adult", "an elder" — the stage a milestone marks, for the feed's sentences.</summary>
    public static string StageName(int percent) => percent switch
    {
        25 => "a juvenile",
        50 => "a subadult",
        75 => "an adult",
        _ => "an elder"
    };

    /// <summary>"Juvenile", "Subadult", "Adult", "Elder" — the same stages as titles: the stats header's target ("to Adult") and its word at 100%.</summary>
    public static string StageTitle(int percent) => percent switch
    {
        25 => "Juvenile",
        50 => "Subadult",
        75 => "Adult",
        _ => "Elder"
    };

    /// <summary>
    /// The stage a growth value sits in, by the same lines: "hatchling" under
    /// 25%, then "juvenile", "subadult", "adult", and "elder" from the
    /// full-grown line (the Dino storage page's cards).
    /// </summary>
    public static string StageAt(double growth) => growth switch
    {
        >= FullyGrown => "elder",
        >= 0.75 => "adult",
        >= 0.50 => "subadult",
        >= 0.25 => "juvenile",
        _ => "hatchling"
    };
}

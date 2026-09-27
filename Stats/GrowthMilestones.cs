namespace PandoraOverlay;

/// <summary>
/// Spots the moment growth crosses one of the game's stage lines — 25%
/// juvenile, 50% subadult, 75% adult, 100% elder / fully grown — pure and
/// tested. Only a crossing seen during this life counts: the first sample of
/// a life sets the baseline and never alerts (spawning in at 60% is not
/// "reaching subadult"). A poll gap that jumps over two lines reports the
/// higher one. Resets on death/dino swap so the next life crosses them anew.
/// </summary>
public sealed class GrowthMilestones
{
    private static readonly (double Threshold, int Percent)[] Lines =
    {
        (0.25, 25), (0.50, 50), (0.75, 75), (0.9995, 100) // 100 uses GrowthTracker's full-grown line
    };

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

    /// <summary>"a juvenile", "a subadult", "an adult", "fully grown" — the stage a milestone marks.</summary>
    public static string StageName(int percent) => percent switch
    {
        25 => "a juvenile",
        50 => "a subadult",
        75 => "an adult",
        _ => "fully grown"
    };
}

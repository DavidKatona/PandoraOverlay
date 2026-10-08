namespace PandoraOverlay;

/// <summary>
/// When the update card shows (1.32, the owner's rules of Oct 8 2026). Pure
/// and tested; MainWindow asks these once per launch.
/// <list type="bullet">
/// <item>AVAILABLE: only from the launch check (so only at launch — opening the
/// overlay is a conscious act, no in-game rule needed), never on a first run
/// (Settings opens on Account then), and once per version: the version a card
/// was shown for is remembered, and "Later" means not again for it.</item>
/// <item>UPDATED: the running version is compared with the one that ran last,
/// so an update by the tray, by Setup.exe over an old install or by a zip
/// unpacked over an old folder all count. A first run, the same version or a
/// lower one shows nothing; a config from before 1.32 (no version recorded)
/// shows the card without "you were on".</item>
/// <item>One card per launch: an Updated card wins over an Available one —
/// the tray entry still offers the newer version.</item>
/// </list>
/// </summary>
public static class UpdateCardPolicy
{
    public static bool ShowAvailable(string? found, string? shownFor, bool firstRun, bool updatedCardShown) =>
        !string.IsNullOrWhiteSpace(found)
        && !firstRun
        && !updatedCardShown
        && Compare(found, shownFor ?? "") != 0;

    /// <summary>Show the Updated card? <paramref name="previous"/> is the version it replaced, when known.</summary>
    public static bool ShowUpdated(string running, string? lastRun, bool firstRun, out string? previous)
    {
        previous = null;
        if (firstRun || string.IsNullOrWhiteSpace(running)) return false;
        if (string.IsNullOrWhiteSpace(lastRun)) return true; // a config from before the card: updated from something older
        if (Compare(running, lastRun) <= 0) return false;
        previous = Bare(lastRun);
        return true;
    }

    /// <summary>"1.31.2+sha" → "1.31.2", "v1.32.0-rc.1" → "1.32.0-rc.1".</summary>
    public static string Bare(string version) => version.Split('+')[0].Trim().TrimStart('v', 'V');

    /// <summary>
    /// SemVer order: major.minor.patch numerically, a release above its own
    /// pre-releases, pre-release labels part by part (numbers numerically,
    /// words by text, numbers before words), build metadata ignored. Anything
    /// unparseable compares as text, so two equal strings are always equal.
    /// </summary>
    public static int Compare(string a, string b)
    {
        var (coreA, preA) = Split(Bare(a));
        var (coreB, preB) = Split(Bare(b));
        if (!TryCore(coreA, out var na) || !TryCore(coreB, out var nb))
            return string.CompareOrdinal(Bare(a), Bare(b));
        for (var i = 0; i < 3; i++)
            if (na[i] != nb[i]) return na[i].CompareTo(nb[i]);
        if (preA.Length == 0 && preB.Length == 0) return 0;
        if (preA.Length == 0) return 1;  // 1.32.0 is above 1.32.0-rc.2
        if (preB.Length == 0) return -1;

        var pa = preA.Split('.');
        var pb = preB.Split('.');
        for (var i = 0; i < Math.Min(pa.Length, pb.Length); i++)
        {
            var aNum = int.TryParse(pa[i], out var ia);
            var bNum = int.TryParse(pb[i], out var ib);
            var c = (aNum, bNum) switch
            {
                (true, true) => ia.CompareTo(ib),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(pa[i], pb[i])
            };
            if (c != 0) return c;
        }
        return pa.Length.CompareTo(pb.Length);
    }

    private static (string Core, string Pre) Split(string v)
    {
        var dash = v.IndexOf('-');
        return dash < 0 ? (v, "") : (v[..dash], v[(dash + 1)..]);
    }

    private static bool TryCore(string core, out int[] parts)
    {
        parts = new int[3];
        var bits = core.Split('.');
        if (bits.Length is < 1 or > 3) return false;
        for (var i = 0; i < bits.Length; i++)
            if (!int.TryParse(bits[i], out parts[i]) || parts[i] < 0) return false;
        return true;
    }
}

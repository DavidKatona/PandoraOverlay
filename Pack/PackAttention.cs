namespace PandoraOverlay;

/// <summary>
/// Whether the Pack needs your eyes: the wake rule behind its fade, pure and
/// tested (BRIEF.md, Nov 2026). It reads only the rows on screen that have
/// bars (a held row, a friend who just left, never wakes it), with the
/// stats panel's own lines, reused by name:
/// <list type="bullet">
/// <item>lit while a shown friend's health is under half (the bars' half,
/// <see cref="BarStates.UnderHalf"/>, the Survival view's wake line), calm
/// again only once every one is above <see cref="StatsAttention.CalmAbove"/>
/// (55%), so a bar hovering at the line can't make the widget blink;</item>
/// <item>lit while a shown friend has a fracture;</item>
/// <item>lit for <see cref="StatsAttention.DamageHold"/> (10 s) after a shown
/// friend's health drops into a lower quarter (under 75, 50 or 25).</item>
/// </list>
/// Nothing else wakes it: no chime, no per-kind rules. The first sample of a
/// friend is never a drop; a friend who left or changed dino starts over.
/// </summary>
public sealed class PackAttention
{
    private readonly Dictionary<string, (string? Dino, BarState State)> _last = new();
    private DateTime _dropUntil;

    /// <summary>True while the Pack should show at full opacity.</summary>
    public bool Lit { get; private set; }

    /// <summary>The rows on screen now (only those with bars count): the verdict.</summary>
    public bool Update(IEnumerable<PackRow> shown, DateTime now)
    {
        var live = shown.Where(r => r.Kind == PackRowKind.Live).Select(r => r.Friend).ToList();
        var seen = new HashSet<string>();
        foreach (var f in live)
        {
            var id = f.SteamId!;
            seen.Add(id);
            var state = BarStates.Of(f.Health);
            if (_last.TryGetValue(id, out var before) && before.Dino == f.Dino && state > before.State) _dropUntil = now + StatsAttention.DamageHold;
            _last[id] = (f.Dino, state);
        }
        foreach (var gone in _last.Keys.Where(k => !seen.Contains(k)).ToList()) _last.Remove(gone);

        var fractured = live.Any(f => f.HeadFractured || f.BodyFractured || f.LegsFractured);
        var dropped = now < _dropUntil;
        var wake = live.Any(f => BarStates.Of(f.Health).UnderHalf()) || fractured || dropped;
        var calm = live.All(f => f.Health > StatsAttention.CalmAbove) && !fractured && !dropped;
        if (wake) Lit = true;
        else if (calm) Lit = false;
        return Lit;
    }

    /// <summary>When the drop's hold ends (the window re-checks then); MinValue = none running.</summary>
    public DateTime HoldUntil => _dropUntil;
}

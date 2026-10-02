namespace PandoraOverlay;

/// <summary>
/// Your own events for the Activity feed — pure and tested. Every cue the
/// overlay already gives you is momentary (a blink, a chime, a status line
/// the next poll overwrites); these turn them into lines you can read ten
/// minutes later. Per poll: spawned in (or started a fresh dino of the same
/// species — growth lower than your last life, a fact, never "died"), a
/// fracture taken, damage taken (a real drop, at most one line per half
/// minute, and only when asked for — a long fight would otherwise fill the
/// feed). Plus builders for the events MainWindow's existing rules detect:
/// a growth stage, a low stat, a Prime check. The first poll of a session
/// is a baseline: being in game at launch is not "spawning".
/// </summary>
public sealed class SelfActivity
{
    private const double DamageStep = 0.05; // a real hit — the fade's 0.005 "damage" cue is far too fine for a line
    private static readonly TimeSpan DamageQuiet = TimeSpan.FromSeconds(30);

    private bool? _inGame;         // null before the first poll
    private PlayerState? _lastLive; // your last in-game state
    private DateTime _damageUntil;

    /// <summary>Per poll: spawn / fresh life, fractures taken, damage taken. Oldest first.</summary>
    public IReadOnlyList<FeedLine> Update(MyLocationResponse result, bool damageLines, DateTime now)
    {
        var lines = new List<FeedLine>();
        var inGame = result.InGame && result.Player is { } p;
        if (inGame)
        {
            var me = result.Player!;
            if (_inGame == false)
            {
                lines.Add(SpawnLine(me, now));
                _damageUntil = default;
            }
            else if (_inGame == true && _lastLive is { } was && SameLife(was, me))
            {
                if (!was.HeadFractured && me.HeadFractured) lines.Add(Mine(now, "You fractured your head", FeedKind.Fracture));
                if (!was.BodyFractured && me.BodyFractured) lines.Add(Mine(now, "You fractured your body", FeedKind.Fracture));
                if (!was.LegsFractured && me.LegsFractured) lines.Add(Mine(now, "You fractured your legs", FeedKind.Fracture));
                if (damageLines && was.Health - me.Health >= DamageStep && now >= _damageUntil)
                {
                    lines.Add(Mine(now, $"Took damage · HP {Pct(me.Health)}", FeedKind.Damage));
                    _damageUntil = now + DamageQuiet;
                }
            }
            _lastLive = me;
        }
        _inGame = inGame;
        return lines;
    }

    /// <summary>"You are now a subadult" — for the stage GrowthMilestones reports.</summary>
    public static FeedLine GrowthLine(int percent, DateTime now) =>
        Mine(now, $"You are now {GrowthMilestones.StageName(percent)}", FeedKind.Growth);

    /// <summary>"Hunger under 20% · ~40m left" — for the moment LowStatAlert fires; the label is DrainTracker's, null when unknown.</summary>
    public static FeedLine LowStatLine(string stat, string? timeLeftLabel, DateTime now) =>
        Mine(now, timeLeftLabel is null ? $"{stat} under 20%" : $"{stat} under 20% · {timeLeftLabel} left", FeedKind.LowStat);

    /// <summary>"Skin applied · Ember (pattern C)" — a skin you applied from the Skins page.</summary>
    public static FeedLine SkinLine(string skinName, int pattern, DateTime now) =>
        Mine(now, $"Skin applied · {skinName} (pattern {PatreonSkins.PatternLetter(pattern)})", FeedKind.Skin);

    /// <summary>"Entered Highland" — for the moment AreaJournal says you have clearly crossed into another area.</summary>
    public static FeedLine AreaLine(string area, DateTime now) => Mine(now, $"Entered {area}", FeedKind.Area);

    /// <summary>
    /// A Prime check: the CHANGE, not the state — the widget shows the state.
    /// One line per condition that flipped since the previous result; with
    /// nothing to compare against, or nothing flipped, one summary line.
    /// </summary>
    public static IReadOnlyList<FeedLine> PrimeLines(PrimeSnapshot? before, PrimeSnapshot fresh, DateTime now)
    {
        var lines = new List<FeedLine>();
        if (before is not null)
        {
            for (var i = 0; i < PrimeConditions.Texts.Length; i++)
            {
                var was = i < before.Conditions.Length && before.Conditions[i];
                var isNow = i < fresh.Conditions.Length && fresh.Conditions[i];
                if (was == isNow) continue;
                lines.Add(Mine(now, isNow ? $"Prime · now met: {PrimeConditions.Texts[i]}" : $"Prime · lost: {PrimeConditions.Texts[i]}", FeedKind.Prime));
            }
        }
        if (lines.Count == 0)
        {
            var count = fresh.Conditions.Count(c => c);
            var verdict = fresh.IsPrime ? "Prime Elder" : fresh.IsEligible ? "ready" : "not ready";
            lines.Add(Mine(now, $"Prime check · {count}/10 · {verdict}", FeedKind.Prime));
        }
        return lines;
    }

    private FeedLine SpawnLine(PlayerState me, DateTime now)
    {
        if (_lastLive is { } live && me.Dino is not null &&
            string.Equals(live.Dino, me.Dino, StringComparison.OrdinalIgnoreCase) && me.Growth < live.Growth - 0.001)
        {
            return Mine(now, $"You started a fresh {me.Dino} {Pct(me.Growth)}", FeedKind.NewLife);
        }
        return Mine(now, me.Dino is null ? "You spawned in" : $"You spawned as {me.Dino} {Pct(me.Growth)}", FeedKind.Spawned);
    }

    /// <summary>Same dino and gender, growth not lower — the trackers' rule; anything else is a new life and compares nothing.</summary>
    private static bool SameLife(PlayerState was, PlayerState now) =>
        was.Dino == now.Dino && was.Gender == now.Gender && now.Growth >= was.Growth - 0.001;

    private static FeedLine Mine(DateTime now, string text, FeedKind kind) => new(now, text, null, kind, Mine: true);

    private static string Pct(double value) => $"{Math.Clamp(value, 0, 1) * 100:0}%";
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PandoraOverlay;

/// <summary>
/// MainWindow, stats panel part: the panel's drawing — the bars and their
/// time labels, the two views, the fracture badges, the critical-stat pulse
/// and the status line — fed by OnSnapshot. Same class as
/// MainWindow.xaml.cs, split for reading; see CLAUDE.md.
/// <para>
/// A poll's answer goes through <see cref="UpdateUi"/>: <see cref="IngestStats"/>
/// feeds it to the trackers and runs the alert rules, once per poll;
/// <see cref="PaintStats"/> draws the panel from the trackers' state and the
/// kept snapshot, and feeds nothing, so a theme switch can repaint it
/// (REVIEW.md objection 1: replaying the snapshot through the ingest would
/// give every tracker a duplicate sample, the speed would read 0 and the
/// drain slopes flatten). The colours are the theme's: the code names each
/// bar's state (<see cref="BarStates"/>, the lines are behaviour) and asks
/// <c>Theme.Current.Bar(kind, state)</c> for its look.
/// </para>
/// </summary>
public partial class MainWindow
{
    // ---- Layout constants -------------------------------------------------
    private const double TrackWidth = 170; // must match bar track width in XAML
    private const double ContentWidth = WidgetFrame.Width - 26; // the frame minus RootPanel's padding (12 a side) and border
    private const double FooterGap = 10; // px the status line and the view name keep between them
    private const double TimeLeftGap = 5; // px between the fill's tip and its label

    private static readonly TimeSpan HeaderPulse = TimeSpan.FromSeconds(10);

    /// <summary>The last answer the panel drew (null before the first): what PaintStats paints from.</summary>
    private MyLocationResponse? _lastSnapshot;

    // ---- A poll's answer ----------------------------------------------------

    /// <summary>One poll's answer: into the trackers and the alerts, onto the panel, and the status line says when.</summary>
    private void UpdateUi(MyLocationResponse result)
    {
        IngestStats(result);
        if (!result.InGame || result.Player is null) DinoText.Text = "Not in-game"; // not PaintStats': "Not signed in" may stand there instead
        PaintStats();
        SetStatus(result.InGame && result.Player is not null
            ? $"Live · updated {DateTime.Now:HH:mm:ss}"
            // Not in-game the poll idles; say so, or a slow reaction to a spawn reads as frozen.
            : _poll.IsIdling
                ? $"Connected · checking every {_poll.Interval.TotalSeconds:0}s · {DateTime.Now:HH:mm:ss}"
                : $"Connected · waiting for spawn · {DateTime.Now:HH:mm:ss}");
    }

    /// <summary>
    /// Once per poll: the answer into the growth, drain, stamina, damage and
    /// speed trackers, the alert rules (a growth stage, a low stat: a blink,
    /// a chime if wanted, a feed line) and the fade's verdict. Draws nothing
    /// but the growth header's blink; PaintStats draws.
    /// </summary>
    private void IngestStats(MyLocationResponse result)
    {
        _lastSnapshot = result;
        if (!result.InGame || result.Player is null)
        {
            _growth.Reset(); // a wall-clock gap would flatten the measured slope
            _hungerDrain.Reset();
            _thirstDrain.Reset();
            _stamina.Reset();
            _damage.Reset();
            _combatSpeed.Reset();
            _attention.Reset();
            _lowStat.Reset();
            _milestones.Reset();
            SetAttention(_attention.FlipHeld); // "Not in-game" is nothing to watch — bar a view flip's few seconds
            return;
        }

        var p = result.Player;
        _growth.Add(p);
        _hungerDrain.Add(p);
        _thirstDrain.Add(p);
        _stamina.Add(p);
        _damage.Add(p); // the combat view's damage taken in this fight
        _combatSpeed.Add(p.X, p.Y);

        // Alerts: the rules are pure and always run; the sounds are opt-in,
        // and each moment also becomes a line in the Activity feed.
        var now = DateTime.UtcNow;
        if (_milestones.Update(p) is { } stage)
        {
            PulseBriefly(GrowthText, HeaderPulse); // a stage reached is always worth a blink
            _attention.NoteEvent();
            if (_config.GrowthChimeEnabled) Chime();
            _log.Post(SelfActivity.GrowthLine(stage, now));
        }
        if (_lowStat.Update(p))
        {
            if (_config.LowStatChimeEnabled) Chime();
            if (_lowStat.HungerFired) _log.Post(SelfActivity.LowStatLine("Hunger", _hungerDrain.Label, now));
            if (_lowStat.ThirstFired) _log.Post(SelfActivity.LowStatLine("Thirst", _thirstDrain.Label, now));
        }

        SetAttention(_attention.Update(p, _hungerDrain.TimeLeft, _thirstDrain.TimeLeft, CombatViewOn, _damage.InFight));
    }

    /// <summary>
    /// The panel from the kept snapshot and the trackers' state: the header,
    /// both views' bars (kept up to date, so flipping between them is
    /// instant), the time labels, the damage and the speed, the pulse and
    /// the badges, every colour from the theme by the bar's state. Feeds no
    /// tracker and runs no alert, so it can run again at any time (a theme
    /// switch). Before the first answer there is nothing to paint over the
    /// XAML's own look ("Connecting…").
    /// </summary>
    private void PaintStats()
    {
        if (_lastSnapshot is null) return;
        if (_lastSnapshot is not { InGame: true, Player: { } p })
        {
            RenderTimeLeft();
            GrowthText.Text = "";
            GrowthText.Foreground = Theme.Current.Growth.Growing.ToBrush();
            ConditionText.Text = "";
            SetBar(HealthFill, HealthPct, 0);
            SetBar(StaminaFill, StaminaPct, 0);
            SetBar(HungerFill, HungerPct, 0);
            SetBar(ThirstFill, ThirstPct, 0);
            SetBar(CombatHealthFill, CombatHealthPct, 0);
            SetBar(CombatStaminaFill, CombatStaminaPct, 0);
            SetBar(DamageFill, DamagePct, 0);
            SpeedValue.Text = "—";
            SetCritical(HealthFill, false);
            SetCritical(CombatHealthFill, false);
            SetCritical(HungerFill, false);
            SetCritical(ThirstFill, false);
            SetFractures(false, false, false);
            return;
        }

        var gender = p.Gender ?? "";
        var symbol = gender.StartsWith("M", StringComparison.OrdinalIgnoreCase) ? "♂"
                   : gender.StartsWith("F", StringComparison.OrdinalIgnoreCase) ? "♀"
                   : "";
        DinoText.Text = $"{p.Dino} {symbol}".Trim();

        GrowthText.Text = _growth.Status switch
        {
            GrowthTracker.GrowthStatus.Growing when _growth.Eta is { } eta =>
                $"Growth {p.Growth * 100:0.#}% · ~{FormatEta(eta)}",
            GrowthTracker.GrowthStatus.Paused => $"Growth {p.Growth * 100:0.#}% · paused",
            GrowthTracker.GrowthStatus.Full => "Fully grown",
            _ => $"Growth {p.Growth * 100:0.#}%"
        };
        var growth = Theme.Current.Growth;
        GrowthText.Foreground = (_growth.Status switch
        {
            GrowthTracker.GrowthStatus.Paused => growth.Paused,
            GrowthTracker.GrowthStatus.Full => growth.FullyGrown,
            _ => growth.Growing
        }).ToBrush();

        // The combat view's corner: the game's own word for health under 50%
        // (the health bar's own line, BarStates), coloured by the health
        // state; "Healthy" (quiet) above it, so the slot always answers the
        // question. "Healthy" is our word for not-wounded; only "Wounded" is the game's.
        var health = BarStates.Of(p.Health);
        ConditionText.Text = health.UnderHalf() ? "Wounded" : "Healthy";
        ConditionText.Foreground = Theme.Current.Readout(health).ToBrush();

        // Both views are kept up to date, so flipping between them is instant.
        PaintBar(HealthFill, HealthPct, BarKind.Health, p.Health);
        PaintBar(CombatHealthFill, CombatHealthPct, BarKind.Health, p.Health);
        PaintBar(StaminaFill, StaminaPct, BarKind.Stamina, p.Stamina);
        PaintBar(CombatStaminaFill, CombatStaminaPct, BarKind.Stamina, p.Stamina);
        PaintBar(HungerFill, HungerPct, BarKind.Hunger, p.Hunger);
        PaintBar(ThirstFill, ThirstPct, BarKind.Thirst, p.Thirst);
        RenderTimeLeft();

        // The combat view's two own rows: damage taken in this fight (a bar
        // that grows, capped at the full track) and the current speed.
        PaintBar(DamageFill, DamagePct, BarKind.Damage, _damage.Total);
        DamagePct.Text = $"{_damage.Total * 100:0}%"; // the bar stops at the track's end; a long fight's number does not
        SpeedValue.Text = _combatSpeed.SpeedMps is { } mps ? $"{mps * 3.6:0} km/h" : "—";

        // Critical-stat pulse (and a theme's halo), on the bars' own Below25
        // state (the line lives once, in BarStates). Stamina is deliberately
        // excluded — it drains to zero every sprint by design and would train
        // the eye to ignore it; damage is a bar that grows, so its low end is
        // the good one.
        SetCritical(HealthFill, health.UnderQuarter());
        SetCritical(CombatHealthFill, health.UnderQuarter());
        SetCritical(HungerFill, BarStates.Of(p.Hunger).UnderQuarter());
        SetCritical(ThirstFill, BarStates.Of(p.Thirst).UnderQuarter());

        SetFractures(p.HeadFractured, p.BodyFractured, p.LegsFractured);
    }

    /// <summary>One bar in its state: the fill's length and the percent, both coloured as the theme colours that bar's state.</summary>
    private static void PaintBar(Border fill, TextBlock pct, BarKind kind, double value)
    {
        SetBar(fill, pct, value);
        var look = Theme.Current.Bar(kind, BarStates.Of(value));
        fill.Background = look.Fill.ToBrush();
        pct.Foreground = look.Percent.ToBrush();
    }

    /// <summary>
    /// The footer's left half. Every status goes through here so the view
    /// name at the right end can step aside for one that needs the whole
    /// line (the first-run hint, a hotkey conflict) instead of overlapping
    /// it or cutting it short.
    /// </summary>
    private void SetStatus(string text)
    {
        StatusText.Text = text;
        FitViewName();
    }

    /// <summary>Shows the view name only while the status line leaves room for it (hidden, not collapsed: the row keeps its height).</summary>
    private void FitViewName()
    {
        var unbounded = new Size(double.PositiveInfinity, double.PositiveInfinity);
        StatusText.Measure(unbounded);
        ViewText.Measure(unbounded);
        var fits = StatusText.DesiredSize.Width + FooterGap + ViewText.DesiredSize.Width <= ContentWidth;
        ViewText.Visibility = fits ? Visibility.Visible : Visibility.Hidden;
    }

    // Fracture badges: always drawn (the row is part of the fixed frame), lit
    // only while that part is fractured — like the site's own fracture icons.

    /// <summary>The three badges are always there; a fractured part lights up, the rest stay dim. Never changes the panel's size.</summary>
    private void SetFractures(bool head, bool body, bool legs)
    {
        SetBadge(FracHead, FracHeadText, head);
        SetBadge(FracBody, FracBodyText, body);
        SetBadge(FracLegs, FracLegsText, legs);
    }

    /// <summary>A badge's shape and text in its state, as the theme colours it.</summary>
    private static void SetBadge(ThemeShape badge, TextBlock text, bool fractured)
    {
        var look = fractured ? Theme.Current.Badge.Fractured : Theme.Current.Badge.Intact;
        badge.Fill = look.Fill.ToBrush();
        text.Foreground = look.Text.ToBrush();
    }

    // ---- Stats views --------------------------------------------------------

    // Anything but "combat" reads as survival, so a config from the first
    // build (which said "full") still loads; FullView in the XAML is the
    // Survival view under that old name.
    private bool CombatViewOn => string.Equals(_config.StatsView, "combat", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Shows the Survival view (health, stamina, hunger, thirst, growth) or
    /// the Combat view (health, stamina, damage taken in this fight, speed;
    /// no growth readout) — two layouts of the SAME four rows inside the
    /// same fixed frame, at the same sizes, so flipping moves nothing. A
    /// player's request (Sep 30 2026: "only health, stam and body breaks ...
    /// to focus and help with pvp"). A shorter panel — the literal ask — was
    /// turned down for the frame's sake, and a first build that drew health
    /// and stamina LARGE was rejected on sight by the owner: size is not how
    /// this overlay emphasises anything. The combat view shows different
    /// information instead of bigger information. The header's corner
    /// follows the view (growth ↔ Wounded/Healthy) and the footer names it,
    /// like the minimap's does. Each view also has its own fade ruleset
    /// (StatsAttention).
    /// The status line stays in both views: staleness matters most in a
    /// fight. The view changes only when the player flips it — switching to
    /// Combat automatically on damage was rejected (content changing by
    /// itself is a surprise).
    /// </summary>
    private void ApplyStatsView()
    {
        var combat = CombatViewOn;
        FullView.Visibility = combat ? Visibility.Collapsed : Visibility.Visible;
        CombatView.Visibility = combat ? Visibility.Visible : Visibility.Collapsed;
        GrowthText.Visibility = combat ? Visibility.Collapsed : Visibility.Visible;
        ConditionText.Visibility = combat ? Visibility.Visible : Visibility.Collapsed;
        ViewText.Text = combat ? "combat view" : "survival view";
        FitViewName();
        RenderTimeLeft();

        // A flip — by hotkey, the control panel or a Settings save — always
        // lights the panel for a few seconds: it is a change to the panel
        // itself (owner's call). Then the new view's ruleset decides.
        if (_shownCombat is { } was && was != combat)
        {
            _attention.NoteViewFlip();
            SetAttention(true);
        }
        _shownCombat = combat;
    }

    private bool? _shownCombat; // the view on screen, to tell a flip from a re-apply

    /// <summary>The stats-view hotkey and the control panel's View button: flips Survival ↔ Combat (persisted with the next Save).</summary>
    private void ToggleStatsView()
    {
        _config.StatsView = CombatViewOn ? "survival" : "combat";
        ApplyStatsView();
    }

    /// <summary>
    /// The time labels on the bars, in the colour the theme gives the bar's
    /// state; the trackers run either way, so the Settings checkbox applies
    /// at once. Without a player the trackers are reset and show nothing.
    /// </summary>
    private void RenderTimeLeft()
    {
        var on = _config.StatTimeLeftEnabled;
        var p = _lastSnapshot is { InGame: true } shown ? shown.Player : null;
        SetBarLabel(HungerLeft, on ? _hungerDrain.Label : null, HungerFill.Width, BarKind.Hunger, p?.Hunger ?? 1);
        SetBarLabel(ThirstLeft, on ? _thirstDrain.Label : null, ThirstFill.Width, BarKind.Thirst, p?.Thirst ?? 1);
        // Stamina reads both ways: "~25s" until empty, "full ~40s" until recovered.
        SetBarLabel(StaminaLeft, on ? _stamina.Label : null, StaminaFill.Width, BarKind.Stamina, p?.Stamina ?? 1);
        SetBarLabel(CombatStaminaLeft, on ? _stamina.Label : null, CombatStaminaFill.Width, BarKind.Stamina, p?.Stamina ?? 1);
    }

    /// <summary>
    /// A bar-chart data label: it rides just past the fill's tip in the bar's
    /// own (lightened) colour, so it reads as part of that bar rather than a
    /// second number next to the percent. With no room left on the track it
    /// flips inside the fill's end, dark on the bright colour (the theme's
    /// OnFill for that bar; Default's is the panel's glass).
    /// Rejected before this: widening the 44 px percent column for the label
    /// (an update must never resize a panel), and white text in a dark pill
    /// at the track's right end (rejected on sight: the track is nearly
    /// invisible, so it floated beside the percent like a second, unrelated
    /// number).
    /// </summary>
    private static void SetBarLabel(TextBlock label, string? text, double fillWidth, BarKind kind, double value)
    {
        if (text is null)
        {
            label.Visibility = Visibility.Collapsed;
            return;
        }

        label.Text = text;
        label.Visibility = Visibility.Visible; // before measuring — a collapsed element measures as 0
        label.Margin = default;
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = label.DesiredSize.Width;
        var outside = fillWidth + TimeLeftGap + width <= TrackWidth;

        label.Foreground = (outside ? Theme.Current.Bar(kind, BarStates.Of(value)).TimeLeft : Theme.Current.BarOf(kind).OnFill).ToBrush();
        label.Margin = new Thickness(outside ? fillWidth + TimeLeftGap : fillWidth - TimeLeftGap - width, 0, 0, 0);
    }

    /// <summary>The Windows "Exclamation" sound — no bundled audio, and it follows the user's sound scheme.</summary>
    // Owner's call: a custom sound only if players ask for one.
    private static void Chime()
    {
        try
        {
            System.Media.SystemSounds.Exclamation.Play();
        }
        catch
        {
            // No sound device / scheme: silence is the right fallback.
        }
    }

    // ---- Critical-stat pulse ------------------------------------------------
    private readonly HashSet<Border> _pulsing = new();

    /// <summary>
    /// A bar's critical signal: the pulse, and the theme's Below25 halo behind
    /// its fill (one number per theme, REVIEW.md decision 16; 0 in Default, so
    /// none). The halo is read here at every paint, so a theme switch on a bar
    /// already pulsing gives it the new theme's at once.
    /// </summary>
    private void SetCritical(BarFill fill, bool on)
    {
        SetPulse(fill, on);
        fill.Halo = on ? Theme.Current.Bars.Below25Halo : 0;
    }

    private void SetPulse(Border fill, bool on)
    {
        if (on == _pulsing.Contains(fill)) return;
        if (on)
        {
            _pulsing.Add(fill);
            fill.BeginAnimation(OpacityProperty, new DoubleAnimation(1.0, 0.35, TimeSpan.FromMilliseconds(600))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            });
        }
        else
        {
            _pulsing.Remove(fill);
            fill.BeginAnimation(OpacityProperty, null);
            fill.Opacity = 1;
        }
    }

    /// <summary>"~"-worthy in-game time: "45m", "3h 10m", "1d 4h".</summary>
    private static string FormatEta(TimeSpan eta)
    {
        if (eta.TotalMinutes < 1) return "1m";
        if (eta.TotalHours < 1) return $"{eta.TotalMinutes:0}m";
        if (eta.TotalDays < 1) return $"{(int)eta.TotalHours}h {eta.Minutes:00}m";
        return $"{(int)eta.TotalDays}d {eta.Hours}h";
    }

    private static void SetBar(System.Windows.Controls.Border fill,
                               System.Windows.Controls.TextBlock pct,
                               double value)
    {
        var v = Math.Clamp(value, 0, 1);
        fill.Width = v * TrackWidth;
        pct.Text = $"{v * 100:0}%";
    }
}

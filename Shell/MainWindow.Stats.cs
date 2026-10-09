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
/// </summary>
public partial class MainWindow
{
    // ---- Layout constants -------------------------------------------------
    private const double TrackWidth = 170; // must match bar track width in XAML
    private const double ContentWidth = WidgetFrame.Width - 26; // the frame minus RootPanel's padding (12 a side) and border
    private const double FooterGap = 10; // px the status line and the view name keep between them

    private static readonly TimeSpan HeaderPulse = TimeSpan.FromSeconds(10);
    private const double WoundedBelow = 0.50; // the game calls you Wounded under half health; the health bar turns amber on the same line

    // ---- Brushes ----------------------------------------------------------
    private static readonly Brush HealthGood = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly Brush HealthWarn = new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00));
    private static readonly Brush HealthCrit = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
    private static readonly Brush GrowthNormal = new SolidColorBrush(Color.FromRgb(0x9A, 0xA7, 0xB0));
    private static readonly Brush HungerTint = new SolidColorBrush(Color.FromRgb(0xFF, 0xB7, 0x4D));     // the bar's orange, lightened for text
    private static readonly Brush ThirstTint = new SolidColorBrush(Color.FromRgb(0x81, 0xD4, 0xFA));     // the bar's blue, lightened for text
    private static readonly Brush StaminaTint = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x82));    // the bar's yellow, lightened for text
    private static readonly Brush TimeLeftOnFill = new SolidColorBrush(Color.FromRgb(0x10, 0x15, 0x1B)); // panel-glass dark, for a label inside the fill
    private const double TimeLeftGap = 5; // px between the fill's tip and its label

    // Fracture badges: always drawn (the row is part of the fixed frame), lit
    // only while that part is fractured — like the site's own fracture icons.
    private static readonly Brush FracturedBadge = new SolidColorBrush(Color.FromArgb(0xB3, 0x40, 0x20, 0x20));
    private static readonly Brush FracturedText = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x80));
    private static readonly Brush IntactBadge = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
    private static readonly Brush IntactText = new SolidColorBrush(Color.FromRgb(0x4A, 0x55, 0x60));

    // ---- Rendering ----------------------------------------------------------
    private void UpdateUi(MyLocationResponse result)
    {
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
            RenderTimeLeft();
            DinoText.Text = "Not in-game";
            GrowthText.Text = "";
            GrowthText.Foreground = GrowthNormal;
            ConditionText.Text = "";
            SetBar(HealthFill, HealthPct, 0);
            SetBar(StaminaFill, StaminaPct, 0);
            SetBar(HungerFill, HungerPct, 0);
            SetBar(ThirstFill, ThirstPct, 0);
            SetBar(CombatHealthFill, CombatHealthPct, 0);
            SetBar(CombatStaminaFill, CombatStaminaPct, 0);
            SetBar(DamageFill, DamagePct, 0);
            SpeedValue.Text = "—";
            SetPulse(HealthFill, false);
            SetPulse(CombatHealthFill, false);
            SetPulse(HungerFill, false);
            SetPulse(ThirstFill, false);
            SetFractures(false, false, false);
            // Not in-game the poll idles; say so, or a slow reaction to a spawn reads as frozen.
            SetStatus(_poll.IsIdling
                ? $"Connected · checking every {_poll.Interval.TotalSeconds:0}s · {DateTime.Now:HH:mm:ss}"
                : $"Connected · waiting for spawn · {DateTime.Now:HH:mm:ss}");
            return;
        }

        var p = result.Player;

        var gender = p.Gender ?? "";
        var symbol = gender.StartsWith("M", StringComparison.OrdinalIgnoreCase) ? "♂"
                   : gender.StartsWith("F", StringComparison.OrdinalIgnoreCase) ? "♀"
                   : "";
        DinoText.Text = $"{p.Dino} {symbol}".Trim();

        _growth.Add(p);
        GrowthText.Text = _growth.Status switch
        {
            GrowthTracker.GrowthStatus.Growing when _growth.Eta is { } eta =>
                $"Growth {p.Growth * 100:0.#}% · ~{FormatEta(eta)}",
            GrowthTracker.GrowthStatus.Paused => $"Growth {p.Growth * 100:0.#}% · paused",
            GrowthTracker.GrowthStatus.Full => "Fully grown",
            _ => $"Growth {p.Growth * 100:0.#}%"
        };
        GrowthText.Foreground = _growth.Status == GrowthTracker.GrowthStatus.Paused
            ? HealthWarn
            : GrowthNormal;

        // The combat view's corner: the game's own word for health under 50%,
        // in the health bar's colour; "Healthy" (quiet) above it, so the slot
        // always answers the question.
        // "Healthy" is our word for not-wounded; only "Wounded" is the game's.
        ConditionText.Text = p.Health < WoundedBelow ? "Wounded" : "Healthy";
        ConditionText.Foreground = p.Health switch
        {
            < 0.25 => HealthCrit,
            < WoundedBelow => HealthWarn,
            _ => GrowthNormal
        };

        // Both views are kept up to date, so flipping between them is instant.
        SetBar(HealthFill, HealthPct, p.Health);
        SetBar(CombatHealthFill, CombatHealthPct, p.Health);
        HealthFill.Background = CombatHealthFill.Background = p.Health switch
        {
            < 0.25 => HealthCrit,
            < WoundedBelow => HealthWarn,
            _ => HealthGood
        };
        SetBar(StaminaFill, StaminaPct, p.Stamina);
        SetBar(CombatStaminaFill, CombatStaminaPct, p.Stamina);
        SetBar(HungerFill, HungerPct, p.Hunger);
        SetBar(ThirstFill, ThirstPct, p.Thirst);
        _hungerDrain.Add(p);
        _thirstDrain.Add(p);
        _stamina.Add(p);
        RenderTimeLeft();

        // The combat view's two own rows: damage taken in this fight (a bar
        // that grows, capped at the full track) and the current speed.
        _damage.Add(p);
        SetBar(DamageFill, DamagePct, _damage.Total);
        DamagePct.Text = $"{_damage.Total * 100:0}%"; // the bar stops at the track's end; a long fight's number does not
        _combatSpeed.Add(p.X, p.Y);
        SpeedValue.Text = _combatSpeed.SpeedMps is { } mps ? $"{mps * 3.6:0} km/h" : "—";

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

        // Critical-stat pulse. Stamina is deliberately excluded — it drains to
        // zero every sprint by design and would train the eye to ignore it.
        SetPulse(HealthFill, p.Health < 0.25);
        SetPulse(CombatHealthFill, p.Health < 0.25);
        SetPulse(HungerFill, p.Hunger < 0.25);
        SetPulse(ThirstFill, p.Thirst < 0.25);

        SetFractures(p.HeadFractured, p.BodyFractured, p.LegsFractured);

        SetStatus($"Live · updated {DateTime.Now:HH:mm:ss}");
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

    /// <summary>The three badges are always there; a fractured part lights up, the rest stay dim. Never changes the panel's size.</summary>
    private void SetFractures(bool head, bool body, bool legs)
    {
        SetBadge(FracHead, FracHeadText, head);
        SetBadge(FracBody, FracBodyText, body);
        SetBadge(FracLegs, FracLegsText, legs);
    }

    private static void SetBadge(Border badge, TextBlock text, bool fractured)
    {
        badge.Background = fractured ? FracturedBadge : IntactBadge;
        text.Foreground = fractured ? FracturedText : IntactText;
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

    /// <summary>The time labels on the bars; the trackers run either way, so the Settings checkbox applies at once.</summary>
    private void RenderTimeLeft()
    {
        var on = _config.StatTimeLeftEnabled;
        SetBarLabel(HungerLeft, on ? _hungerDrain.Label : null, HungerFill.Width, HungerTint);
        SetBarLabel(ThirstLeft, on ? _thirstDrain.Label : null, ThirstFill.Width, ThirstTint);
        // Stamina reads both ways: "~25s" until empty, "full ~40s" until recovered.
        SetBarLabel(StaminaLeft, on ? _stamina.Label : null, StaminaFill.Width, StaminaTint);
        SetBarLabel(CombatStaminaLeft, on ? _stamina.Label : null, CombatStaminaFill.Width, StaminaTint);
    }

    /// <summary>
    /// A bar-chart data label: it rides just past the fill's tip in the bar's
    /// own (lightened) colour, so it reads as part of that bar rather than a
    /// second number next to the percent. With no room left on the track it
    /// flips inside the fill's end, dark on the bright colour.
    /// Rejected before this: widening the 44 px percent column for the label
    /// (an update must never resize a panel), and white text in a dark pill
    /// at the track's right end (rejected on sight: the track is nearly
    /// invisible, so it floated beside the percent like a second, unrelated
    /// number).
    /// </summary>
    private static void SetBarLabel(System.Windows.Controls.TextBlock label, string? text, double fillWidth, Brush tint)
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

        label.Foreground = outside ? tint : TimeLeftOnFill;
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

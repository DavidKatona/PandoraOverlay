using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PandoraOverlay;

/// <summary>
/// The Prime tracker widget: overall Prime status plus the ten condition rows
/// from the site's "Prime Check" — display-only and click-through when locked.
/// The check itself is triggered from the control panel or the tray and runs
/// through PollService (user-clicked only, never polled, server cooldown
/// honored); this window just renders the last result, which is cached in
/// config so it survives restarts, and a live cooldown countdown. The
/// condition texts are baked in because the site bakes them into its frontend
/// too — the API only returns flags. First show docks to the left screen
/// edge, vertically centered; the position persists via config.
/// </summary>
public partial class PrimeWindow : OverlayWindowBase
{
    private const double EdgeInset = 16; // matches SnapResolver's comfort inset
    private const int NeededForPrime = 5;

    private static readonly string[] ConditionTexts =
    {
        "Visit a Sanctuary as a juvenile",
        "Get nested in",
        "Get perfect diet (1% of each)",
        "Visit Mass Migration zone",
        "Visit 2 Migration zones",
        "Visit 4 Patrol zones",
        "Never be Infertile",
        "Never get Muscle spasms",
        "Raise children to Subadult",
        "Be a Hypsi, Troodon, Beipi, Dryo or Deino"
    };

    private static readonly Brush MetIcon = new SolidColorBrush(Color.FromRgb(0x7C, 0xC8, 0x84));
    private static readonly Brush MetText = new SolidColorBrush(Color.FromRgb(0xC7, 0xD1, 0xDA));
    private static readonly Brush UnmetIcon = new SolidColorBrush(Color.FromRgb(0x9A, 0x5F, 0x5F));
    private static readonly Brush Dim = new SolidColorBrush(Color.FromRgb(0x7B, 0x87, 0x90));
    private static readonly Brush StatusNeutral = new SolidColorBrush(Color.FromRgb(0x9A, 0xA7, 0xB0));
    private static readonly Brush StatusPrime = new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x64));
    private static readonly Brush Warn = new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00));

    private readonly OverlayConfig _config;
    private readonly PollService _poll;
    private readonly TextBlock[] _icons = new TextBlock[ConditionTexts.Length];
    private readonly TextBlock[] _labels = new TextBlock[ConditionTexts.Length];
    private readonly DispatcherTimer _cooldownTimer;
    private string? _notice;   // sticky explanation of the last non-Ok outcome
    private string? _liveDino; // null while not in-game
    private bool _checking;

    public PrimeWindow(OverlayConfig config, PollService poll)
    {
        InitializeComponent();

        _config = config;
        _poll = poll;
        ApplyAppearance(config);
        BuildRows();

        if (config.PrimeX is { } x && config.PrimeY is { } y)
        {
            Left = x;
            Top = y;
        }
        else
        {
            Loaded += (_, _) => PlaceLeftCenter();
        }

        _cooldownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _cooldownTimer.Tick += (_, _) => RenderNotice();

        _poll.PrimeCheckStarted += OnCheckStarted;
        _poll.PrimeChecked += OnPrimeChecked;
        _poll.SnapshotReceived += OnSnapshot;
        Closed += (_, _) =>
        {
            _cooldownTimer.Stop();
            _poll.PrimeCheckStarted -= OnCheckStarted;
            _poll.PrimeChecked -= OnPrimeChecked;
            _poll.SnapshotReceived -= OnSnapshot;
        };

        RenderSnapshot();
        RenderNotice();
    }

    private void BuildRows()
    {
        for (var i = 0; i < ConditionTexts.Length; i++)
        {
            _icons[i] = new TextBlock { Width = 16, FontSize = 11, FontWeight = FontWeights.Bold };
            _labels[i] = new TextBlock
            {
                Text = ConditionTexts[i],
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var row = new DockPanel { Margin = new Thickness(0, i == 0 ? 0 : 3, 0, 0) };
            DockPanel.SetDock(_icons[i], Dock.Left);
            row.Children.Add(_icons[i]);
            row.Children.Add(_labels[i]);
            ConditionRows.Children.Add(row);
        }
    }

    private void PlaceLeftCenter()
    {
        var bounds = GetScreenBoundsDips();
        Left = bounds.Left + EdgeInset;
        Top = bounds.Top + (bounds.Height - ActualHeight) / 2;
    }

    /// <summary>Its own size control, like every widget; UiScale is only the pre-seed fallback.</summary>
    protected override double AppearanceScale(OverlayConfig config) => config.PrimeScale ?? config.UiScale;

    /// <summary>Reapplies scale/opacity after a settings save.</summary>
    public void ApplySettingsFromConfig() => ApplyAppearance(_config);

    protected override void OnEditModeChanged(bool editMode)
    {
        RootPanel.BorderBrush = editMode ? BorderEdit : BorderLocked;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragIfEditing(e);

    // ---- Poll stream ---------------------------------------------------------
    private void OnSnapshot(MyLocationResponse result)
    {
        if (!EditMode && !Topmost) Topmost = true;

        var dino = result.InGame ? result.Player?.Dino : null;
        if (dino == _liveDino) return;
        _liveDino = dino;

        if (dino is not null && _notice == NotInGameNotice)
        {
            _notice = null;
            RenderNotice();
        }
        RenderInfo(); // the stale cue depends on the live dino
    }

    // ---- Prime checks --------------------------------------------------------
    private const string NotInGameNotice = "join the server first";

    private void OnCheckStarted()
    {
        _checking = true;
        RenderNotice();
    }

    private void OnPrimeChecked(PrimeCheckResult result)
    {
        _checking = false;
        _notice = result.Outcome switch
        {
            PrimeCheckOutcome.Ok => null,
            PrimeCheckOutcome.Cooldown => _notice, // the countdown already says it
            PrimeCheckOutcome.NotInGame => NotInGameNotice,
            _ => $"check failed ({result.Reason})"
        };
        if (result.Outcome == PrimeCheckOutcome.Ok) RenderSnapshot();
        RenderNotice();
    }

    // ---- Rendering -----------------------------------------------------------
    private void RenderSnapshot()
    {
        var snap = _config.Prime;
        for (var i = 0; i < ConditionTexts.Length; i++)
        {
            if (snap is null)
            {
                _icons[i].Text = "·";
                _icons[i].Foreground = Dim;
                _labels[i].Foreground = Dim;
                continue;
            }
            var met = i < snap.Conditions.Length && snap.Conditions[i];
            _icons[i].Text = met ? "✓" : "✗";
            _icons[i].Foreground = met ? MetIcon : UnmetIcon;
            _labels[i].Foreground = met ? MetText : Dim;
        }

        if (snap is null)
        {
            StatusText.Text = "—";
            StatusText.Foreground = StatusNeutral;
        }
        else
        {
            var count = snap.Conditions.Count(c => c);
            (StatusText.Text, StatusText.Foreground) = snap switch
            {
                { IsPrime: true } => ("Prime Elder", StatusPrime),
                { IsEligible: true } => ($"Ready · {count}/10", MetIcon),
                _ => ($"Not ready · {count}/10 ({NeededForPrime} needed)", StatusNeutral)
            };
        }
        RenderInfo();
    }

    /// <summary>What the rows reflect: when and as which dino — amber once the live dino differs.</summary>
    private void RenderInfo()
    {
        if (_config.Prime is not { } snap)
        {
            InfoText.Text = "Not checked yet — Check Prime: control panel or tray";
            InfoText.Foreground = Dim;
            return;
        }

        var local = snap.CheckedAtUtc.ToLocalTime();
        var when = local.Date == DateTime.Today ? $"{local:HH:mm}" : $"{local:MMM d HH:mm}";
        var stale = snap.Dino is not null && _liveDino is not null &&
                    !string.Equals(snap.Dino, _liveDino, StringComparison.OrdinalIgnoreCase);

        InfoText.Text = snap.Dino is null
            ? $"Checked {when}"
            : stale ? $"Checked {when} as {snap.Dino} — now {_liveDino}" : $"Checked {when} · {snap.Dino}";
        InfoText.Foreground = stale ? Warn : Dim;
    }

    /// <summary>Second footer line: checking / sticky notice / live cooldown. Runs the 1 s timer only while cooling down.</summary>
    private void RenderNotice()
    {
        var remaining = _poll.PrimeCooldownUntilUtc - DateTime.UtcNow;
        var cooling = remaining > TimeSpan.Zero;

        if (cooling && !_cooldownTimer.IsEnabled) _cooldownTimer.Start();
        else if (!cooling && _cooldownTimer.IsEnabled) _cooldownTimer.Stop();

        NoticeText.Text =
            _checking ? "checking…"
            : _notice is not null ? (cooling ? $"{_notice} · retry in {Format(remaining)}" : _notice)
            : cooling ? $"next check in {Format(remaining)}"
            : "check available";
        NoticeText.Foreground = _notice is not null && !_checking ? Warn : Dim;
    }

    private static string Format(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";
}

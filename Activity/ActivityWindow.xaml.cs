using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PandoraOverlay;

/// <summary>
/// The Activity widget, built on the SMALL frame (WidgetFrame) it shares with
/// the stats panel: a header (plus, with friends' events included, how
/// many friends you have and how many are in game) over FeedLines
/// one-line slots showing the last ten minutes of the ActivityLog — your
/// own events (SelfActivity: spawned, fresh life, stage, low stat,
/// fracture, Prime check, damage), always, and your friends' (FriendFeed:
/// spawned, left, changed dino, reached a stage, took a fracture, came
/// near) as the extra, newest at the top, fading with age. Every other
/// cue on the overlay is momentary; this is the one you can read late. With
/// no recent lines the first slot names who is in game, so it is never
/// blank. A pure renderer: MainWindow owns the log and the feed, posts
/// into them and plays the chimes, so the history — and the friend-spawn
/// chime — survive the widget being hidden. Display-only
/// and click-through when locked. First show docks under the Prime tracker
/// (left column); the position persists via config.
/// </summary>
public partial class ActivityWindow : OverlayWindowBase
{
    private const double EdgeInset = 16;

    /// <summary>
    /// As many one-line slots as fit the SMALL frame under the header at
    /// 100% (v1.25, owner's call — a 3/5/8 setting was dropped: inside a
    /// fixed frame three lines floated and eight didn't fit). The Grid
    /// shares the frame's remaining height between them.
    /// </summary>
    private const int FeedLines = 6;

    private static readonly TimeSpan AttentionHold = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ExpiryTick = TimeSpan.FromSeconds(20);

    private static readonly Brush Dim = new SolidColorBrush(Color.FromRgb(0x7B, 0x87, 0x90));
    private static readonly Brush Text = new SolidColorBrush(Color.FromRgb(0xC7, 0xD1, 0xDA));
    private static readonly Brush Warn = new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00));
    private static readonly Brush Self = new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x64)); // your arrow's orange: you, everywhere on the overlay
    private static readonly Brush[] PaletteBrushes = WaypointPalette.Colours
        .Select(c => { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(c.Hex)); b.Freeze(); return (Brush)b; })
        .ToArray();

    private readonly OverlayConfig _config;
    private readonly PollService _poll;
    private readonly FriendBook _book;
    private readonly FriendFeed _feed;
    private readonly ActivityLog _log;
    private readonly DispatcherTimer _expiryTimer;    // ages the lines; runs only while there are lines
    private readonly DispatcherTimer _attentionTimer; // one-shot: lets a woken widget fade again
    private readonly List<TextBlock> _slots = new();

    public ActivityWindow(OverlayConfig config, PollService poll, FriendBook book, FriendFeed feed, ActivityLog log, Point? suggested)
    {
        InitializeComponent();

        _config = config;
        _poll = poll;
        _book = book;
        _feed = feed;
        _log = log;
        ApplyAppearance(config);
        BuildSlots();

        if (config.ActivityX is { } x && config.ActivityY is { } y)
        {
            Left = x;
            Top = y;
        }
        else if (suggested is { } s)
        {
            Left = s.X;
            Top = s.Y; // Loaded's ClampIntoScreen pulls it back if the column runs off the bottom
        }
        else
        {
            Loaded += (_, _) => PlaceLeftCenter();
        }

        _expiryTimer = new DispatcherTimer { Interval = ExpiryTick };
        _expiryTimer.Tick += (_, _) => Render();
        _attentionTimer = new DispatcherTimer { Interval = AttentionHold };
        _attentionTimer.Tick += (_, _) =>
        {
            _attentionTimer.Stop();
            SetAttention(false);
        };

        _poll.SnapshotReceived += OnSnapshot;
        _poll.FriendsChanged += OnFriends;
        _log.Posted += OnPosted;
        Closed += (_, _) =>
        {
            _expiryTimer.Stop();
            _attentionTimer.Stop();
            _poll.SnapshotReceived -= OnSnapshot;
            _poll.FriendsChanged -= OnFriends;
            _log.Posted -= OnPosted;
        };

        Render();
        SetAttention(false); // whatever is in the log is old news to a window that just opened
    }

    /// <summary>The widget takes part in the attention fade: a new line lights it for a while.</summary>
    protected override bool Fades => true;

    /// <summary>Its own size control, like every widget; seeded from the Prime tracker it docks under.</summary>
    protected override double AppearanceScale(OverlayConfig config) => config.ActivityScale ?? config.PrimeScale ?? config.UiScale;

    /// <summary>Reapplies scale/opacity and the feed choices after a settings save.</summary>
    public void ApplySettingsFromConfig()
    {
        ApplyAppearance(_config);
        Render();
    }

    /// <summary>FeedLines equal-height rows sharing the frame under the header.</summary>
    private void BuildSlots()
    {
        Lines.Children.Clear();
        Lines.RowDefinitions.Clear();
        _slots.Clear();
        for (var i = 0; i < FeedLines; i++)
        {
            Lines.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var slot = new TextBlock
            {
                FontSize = 11, Foreground = Text,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(slot, i);
            _slots.Add(slot);
            Lines.Children.Add(slot);
        }
    }

    private void PlaceLeftCenter()
    {
        var bounds = GetScreenBoundsDips();
        Left = bounds.Left + EdgeInset;
        Top = bounds.Top + (bounds.Height - ActualHeight) / 2;
    }

    protected override void OnEditModeChanged(bool editMode)
    {
        RootPanel.BorderBrush = editMode ? BorderEdit : BorderLocked;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragIfEditing(e);

    // ---- Streams ---------------------------------------------------------------
    private void OnSnapshot(MyLocationResponse result)
    {
        if (!EditMode && !Topmost) Topmost = true;
    }

    /// <summary>The roster changed (or was cleared): the header's counts and the quiet line follow.</summary>
    private void OnFriends(IReadOnlyList<FriendState>? roster) => Render();

    /// <summary>New lines landed in the log: show them and light the widget. (The friend-spawn chime is MainWindow's, like the stats chimes — it must not depend on this widget being shown.)</summary>
    private void OnPosted(IReadOnlyList<FeedLine> fresh)
    {
        Render();
        Wake();
    }

    private void Wake()
    {
        SetAttention(true);
        _attentionTimer.Stop();
        _attentionTimer.Start();
    }

    // ---- Rendering -----------------------------------------------------------
    private void Render()
    {
        var now = DateTime.UtcNow;
        _log.Expire(now); // a window shown after a long hide must not display lines that should be gone

        // The friends count is part of the friends extra: without it the header is just the title.
        CountText.Text = !_config.ActivityIncludeFriends ? ""
            : !_feed.HasRoster ? "—"
            : _feed.Total == 0 ? "no friends yet"
            : $"{_feed.InGame} of {_feed.Total} friends in game";

        var lines = _log.Lines;
        if (lines.Count > 0 && !_expiryTimer.IsEnabled) _expiryTimer.Start();
        else if (lines.Count == 0 && _expiryTimer.IsEnabled) _expiryTimer.Stop();

        for (var i = 0; i < _slots.Count; i++)
        {
            var slot = _slots[i];
            slot.Inlines.Clear();
            slot.Opacity = 1;
            if (i < lines.Count)
            {
                var line = lines[i];
                if (line.Mine) slot.Inlines.Add(new Run("● ") { Foreground = Self });
                else if (line.SteamId is { } id) slot.Inlines.Add(new Run("● ") { Foreground = PaletteBrushes[_book.ColourOf(id)] });
                var warn = line.Kind is FeedKind.Fracture or FeedKind.LowStat or FeedKind.Damage;
                slot.Inlines.Add(new Run(line.Text) { Foreground = warn ? Warn : Text });
                slot.Opacity = ActivityLog.AgeOpacity(line, now);
            }
            else if (i == 0)
            {
                slot.Inlines.Add(new Run(QuietLine()) { Foreground = Dim });
            }
        }
    }

    /// <summary>What the first slot says when nothing recent happened: who is on, or why nothing shows.</summary>
    private string QuietLine()
    {
        if (!_config.ActivityIncludeFriends) return "no recent activity";
        if (_feed.HasRoster && _poll.Friends is null) return "friends unavailable · retrying";
        if (!_feed.HasRoster) return "waiting for the friends list…";
        if (_feed.Total == 0) return "no recent activity";
        if (_feed.InGame == 0) return "no friends in game right now";
        return "In game: " + string.Join(", ", _feed.InGameNames);
    }
}

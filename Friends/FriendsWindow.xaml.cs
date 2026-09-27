using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PandoraOverlay;

/// <summary>
/// The Friends widget: a header with how many friends you have and how many
/// are in game, over a fixed number of one-line slots carrying the friends
/// feed — who spawned, left, changed dino, reached a stage, took a fracture,
/// came near — newest at the top, fading with age and gone after ten
/// minutes (FriendFeed). Who is on and where lives on the minimap; this
/// widget says what changed while you were watching the game. With no
/// recent lines the first slot names who is in game, so it is never blank.
/// Display-only and click-through when locked; a pure consumer of
/// PollService's roster stream. First show docks under the Prime tracker
/// (left column); the position persists via config.
/// </summary>
public partial class FriendsWindow : OverlayWindowBase
{
    private const double EdgeInset = 16;
    private const double LineHeight = 16;
    private static readonly TimeSpan AttentionHold = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ExpiryTick = TimeSpan.FromSeconds(20);

    private static readonly Brush Dim = new SolidColorBrush(Color.FromRgb(0x7B, 0x87, 0x90));
    private static readonly Brush Text = new SolidColorBrush(Color.FromRgb(0xC7, 0xD1, 0xDA));
    private static readonly Brush Warn = new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00));
    private static readonly Brush[] PaletteBrushes = WaypointPalette.Colours
        .Select(c => { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(c.Hex)); b.Freeze(); return (Brush)b; })
        .ToArray();

    private readonly OverlayConfig _config;
    private readonly PollService _poll;
    private readonly FriendBook _book;
    private readonly FriendFeed _feed = new();
    private readonly DispatcherTimer _expiryTimer;    // ages the lines; runs only while there are lines
    private readonly DispatcherTimer _attentionTimer; // one-shot: lets a woken widget fade again
    private readonly List<TextBlock> _slots = new();
    private PlayerState? _me;
    private bool _hasRoster;   // at least one roster has arrived this session
    private bool _unavailable; // the roster fetch keeps failing

    public FriendsWindow(OverlayConfig config, PollService poll, FriendBook book, Point? suggested)
    {
        InitializeComponent();

        _config = config;
        _poll = poll;
        _book = book;
        ApplyAppearance(config);
        BuildSlots();

        if (config.FriendsX is { } x && config.FriendsY is { } y)
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
        _expiryTimer.Tick += (_, _) =>
        {
            _feed.Expire(DateTime.UtcNow);
            Render();
        };
        _attentionTimer = new DispatcherTimer { Interval = AttentionHold };
        _attentionTimer.Tick += (_, _) =>
        {
            _attentionTimer.Stop();
            SetAttention(false);
        };

        _poll.SnapshotReceived += OnSnapshot;
        _poll.FriendsChanged += OnFriends;
        Closed += (_, _) =>
        {
            _expiryTimer.Stop();
            _attentionTimer.Stop();
            _poll.SnapshotReceived -= OnSnapshot;
            _poll.FriendsChanged -= OnFriends;
        };

        if (_poll.Friends is { } roster) OnFriends(roster); // shown mid-session: start from the current roster
        Render();
        SetAttention(false); // nothing has happened yet
    }

    /// <summary>The widget takes part in the attention fade: a new line lights it for a while.</summary>
    protected override bool Fades => true;

    /// <summary>Its own size control, like every widget; seeded from the Prime tracker it docks under.</summary>
    protected override double AppearanceScale(OverlayConfig config) => config.FriendsScale ?? config.PrimeScale ?? config.UiScale;

    /// <summary>Reapplies scale/opacity and the slot count after a settings save.</summary>
    public void ApplySettingsFromConfig()
    {
        ApplyAppearance(_config);
        if (_slots.Count != Rows) BuildSlots();
        Render();
    }

    private int Rows => Math.Clamp(_config.FriendsRows, 3, 8);

    private void BuildSlots()
    {
        Lines.Children.Clear();
        _slots.Clear();
        for (var i = 0; i < Rows; i++)
        {
            var slot = new TextBlock
            {
                Height = LineHeight, FontSize = 11, Foreground = Text,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, i == 0 ? 0 : 2, 0, 0)
            };
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

    // ---- Poll stream ---------------------------------------------------------
    private void OnSnapshot(MyLocationResponse result)
    {
        if (!EditMode && !Topmost) Topmost = true;
        _me = result.InGame ? result.Player : null;
    }

    private void OnFriends(IReadOnlyList<FriendState>? roster)
    {
        if (roster is null)
        {
            // Cleared: the fetch keeps failing (or no surface wants it — then
            // we are hidden anyway). Keep the lines, drop the baseline so the
            // roster's return seeds again instead of reporting everyone as new.
            _unavailable = true;
            _feed.ResetBaseline();
            Render();
            return;
        }
        _unavailable = false;
        _hasRoster = true;

        var fresh = _feed.Update(roster, _me,
            nameOf: (id, siteName) => _book.DisplayName(id, siteName),
            notify: _book.Notifies,
            DateTime.UtcNow);
        Render();
        if (fresh.Count == 0) return;

        Wake();
        if (_config.FriendsChimeEnabled && fresh.Any(l => l.Kind is FeedKind.Spawned or FeedKind.NewLife)) Chime();
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
        CountText.Text = !_hasRoster ? "—"
            : _feed.Total == 0 ? "no friends yet"
            : $"{_feed.InGame} of {_feed.Total} in game";

        var now = DateTime.UtcNow;
        var lines = _feed.Lines;
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
                if (line.SteamId is { } id)
                {
                    slot.Inlines.Add(new Run("● ") { Foreground = PaletteBrushes[_book.ColourOf(id)] });
                }
                slot.Inlines.Add(new Run(line.Text) { Foreground = line.Kind == FeedKind.Fracture ? Warn : Text });
                slot.Opacity = FriendFeed.AgeOpacity(line, now);
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
        if (_unavailable) return "friends unavailable · retrying";
        if (!_hasRoster) return "waiting for the friends list…";
        if (_feed.Total == 0) return "add friends on islapandora.eu";
        if (_feed.InGame == 0) return "nobody in game right now";
        return "In game: " + string.Join(", ", _feed.InGameNames);
    }

    /// <summary>The Windows "Exclamation" sound, like the stats panel's chimes.</summary>
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
}

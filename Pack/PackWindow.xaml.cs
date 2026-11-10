using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace PandoraOverlay;

/// <summary>
/// The Pack widget (BRIEF.md, Nov 2026; reworked Nov 10 after the first
/// testers): your friends on your exact dino, a row each, with their health,
/// stamina, hunger and thirst, by the website's own Friends stats rule
/// (PackRows): all of them by default, or only those ticked Watch (Settings →
/// Friends, or a map menu's "Watch …"). On one of the two widget frames, the
/// player's choice (Small, the Activity feed's, by default; Large, the
/// minimap's), with Full or Compact rows; the frame never changes size: the
/// nearest fill it, in name order, and the last line says "+N more".
/// Click-through and never scrolling, dragged, snapped, scaled
/// (<see cref="OverlayConfig.PackScale"/>) and faded like the other widgets;
/// it wakes by <see cref="PackAttention"/> (no chime).
/// <para>
/// A pure renderer of what the stream hands out: the roster
/// (<see cref="PollService.FriendsChanged"/>, after MainWindow has synced the
/// book) and your own state (<see cref="PollService.SnapshotReceived"/>), plus
/// the book's Watch ticks, names and colours. It holds the leavers who left in
/// the last five minutes (<see cref="PackLeavers"/>), and redraws from what it
/// holds: a theme switch or a setting repaints and sends nothing. It adds no
/// request and no pace: MainWindow tells the poll it is shown
/// (<see cref="PollService.PackShown"/>), and the roster keeps its cadence.
/// MainWindow owns its lifetime: the window exists only while it is shown
/// (in the ticked mode, with a friend ticked). Nothing positional is shown here
/// (distance, area, "nearby"): distance only picks who fills the frame.
/// </para>
/// <para>
/// Every colour, corner and font is a theme token, read by meaning as it
/// paints: the bars' quarter states (<see cref="ThemeTokens.Bar"/>), the
/// health readout's quarter colours for the percent, the fracture badges,
/// the title, label and dim text. A friend's dot is data (their colour). The
/// heights are the code's (PackRows), never a theme's. The bars step between
/// rosters, like the stats panel's; they don't glide.
/// </para>
/// </summary>
public partial class PackWindow : OverlayWindowBase
{
    private const double EdgeInset = 16;
    private const double ThinBarHeight = 4;      // a full row's four bars
    private const double CompactBarHeight = 7;   // a compact row's health bar
    private const double NameLineHeight = 18;    // a full row's name line
    // The parts the frame contract measures the Pack's strings with
    // (FrameContract, "The Pack"), named there once.
    private const double CaptionHeight = FrameContract.PackCaptionHeight; // a full row's captions over their bars
    private const double CaptionGap = 2;                                  // between the captions and the bars
    private const double ColumnGap = FrameContract.PackColumnGap;         // between the four captioned bars
    private const double FullNameMax = 120, CompactNameMax = FrameContract.PackCompactNameMax;
    private const double BadgeWidth = FrameContract.PackBadgeWidth, BadgeHeight = FrameContract.PackBadgeHeight, BadgeGap = 3;
    private static readonly TimeSpan HoldTick = TimeSpan.FromSeconds(5);

    /// <summary>The four stats of a full row, in their order, with their captions.</summary>
    private static readonly (BarKind Kind, string Caption, Func<FriendState, double> Value)[] FourStats =
    {
        (BarKind.Health, "Health", f => f.Health),
        (BarKind.Stamina, "Stamina", f => f.Stamina),
        (BarKind.Hunger, "Hunger", f => f.Hunger),
        (BarKind.Thirst, "Thirst", f => f.Thirst),
    };

    private static readonly Brush[] PaletteBrushes = WaypointPalette.Colours
        .Select(c => { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(c.Hex)); b.Freeze(); return (Brush)b; })
        .ToArray();

    private readonly OverlayConfig _config;
    private readonly PollService _poll;
    private readonly FriendBook _book;
    private readonly PackLeavers _leavers = new();
    private readonly PackAttention _attention = new();
    private readonly DispatcherTimer _holdTimer; // ages the held rows and the drop's hold; runs only while one is running
    private IReadOnlyList<FriendState>? _roster;
    private PlayerState? _me;
    private IReadOnlySet<string> _shown = new HashSet<string>();

    public PackWindow(OverlayConfig config, PollService poll, FriendBook book)
    {
        InitializeComponent();
        _config = config;
        _poll = poll;
        _book = book;
        _me = poll.LastPlayer;
        _roster = poll.Friends;
        _leavers.Update(_roster, _me?.Dino, DateTime.UtcNow); // the baseline: whoever is in game now can be held when they leave
        ApplyFrame();
        ApplyAppearance(config);

        // MainWindow fills a missing position from the default layout before
        // creating this window; the fallback only guards a hand-edited config.
        if (config.PackX is { } x && config.PackY is { } y)
        {
            Left = x;
            Top = y;
        }
        else
        {
            Loaded += (_, _) => PlaceRightCenter();
        }

        _holdTimer = new DispatcherTimer { Interval = HoldTick };
        _holdTimer.Tick += (_, _) => Render();

        _poll.SnapshotReceived += OnSnapshot;
        _poll.FriendsChanged += OnFriends;
        _book.Changed += OnBookChanged;
        Closed += (_, _) =>
        {
            _holdTimer.Stop();
            _poll.SnapshotReceived -= OnSnapshot;
            _poll.FriendsChanged -= OnFriends;
            _book.Changed -= OnBookChanged;
        };

        Render();
    }

    /// <summary>The widget takes part in the attention fade: PackAttention lights it.</summary>
    protected override bool Fades => true;

    /// <summary>Its own size control, like every widget: a plain number defaulting to 1.0.</summary>
    protected override double AppearanceScale(OverlayConfig config) => config.PackScale;

    private PackFrame Frame => PackRows.FrameOf(_config.PackFrame);

    private PackRowSize Size => PackRows.SizeOf(_config.PackRows);

    /// <summary>A Settings change (the frame, the rows, the scale, the opacity, the fade): re-applied from config and redrawn from what it holds.</summary>
    public void ApplySettingsFromConfig()
    {
        ApplyFrame();
        ApplyAppearance(_config);
        Render();
    }

    /// <summary>A theme switch (MainWindow.ApplyTheme): the glass, then the rows painted again from what it holds. No request, no placement.</summary>
    public void ApplyTheme()
    {
        ApplyAppearance(_config);
        Render();
    }

    private void ApplyFrame() => RootPanel.Height = PackRows.FrameHeight(Frame);

    private void PlaceRightCenter()
    {
        var bounds = GetScreenBoundsDips();
        Left = bounds.Right - EdgeInset - ActualWidth;
        Top = bounds.Top + (bounds.Height - ActualHeight) / 2;
    }

    protected override void OnEditModeChanged(bool editMode)
    {
        RootPanel.Lit = editMode; // the theme's edit look on the frame (ThemedBorder)
        Render();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragIfEditing(e);

    // ---- Streams ---------------------------------------------------------------

    /// <summary>Your own state: redrawn only when it changes what the rows can show (in game or not, your dino).</summary>
    private void OnSnapshot(MyLocationResponse result)
    {
        if (!EditMode && !Topmost) Topmost = true;
        var me = result.InGame ? result.Player : null;
        var changed = (me is null) != (_me is null) || me?.Dino != _me?.Dino;
        _me = me;
        if (changed) Render();
    }

    /// <summary>A new roster (or none): the leavers are updated, the rows redrawn, and the wake rule asked.</summary>
    private void OnFriends(IReadOnlyList<FriendState>? roster)
    {
        _roster = roster;
        _leavers.Update(roster, _me?.Dino, DateTime.UtcNow);
        Render();
    }

    /// <summary>A Watch tick, a nickname or a colour: redrawn (MainWindow decides whether the window stays).</summary>
    private void OnBookChanged() => Render();

    // ---- Rendering -----------------------------------------------------------

    /// <summary>Everything drawn again from what the window holds: the header's count, the rows (or the body's line), the closing line, the verdict.</summary>
    private void Render()
    {
        var now = DateTime.UtcNow;
        var view = PackRows.Build(_roster, _book, _me, PackRows.TickedOnly(_config.PackShows), _leavers, _shown, now, Frame, Size);
        _shown = view.Shown; // the margin: who is on screen keeps their row until another is clearly nearer
        CountText.Text = view.Count;

        Rows.Children.Clear();
        EmptyText.Visibility = view.Empty is null ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Text = view.Empty ?? "";
        var size = Size;
        for (var i = 0; i < view.Rows.Count; i++)
        {
            FrameworkElement row = size == PackRowSize.Compact ? CompactRow(view.Rows[i]) : FullRow(view.Rows[i]);
            row.Height = PackRows.RowHeight(size);
            if (i > 0) row.Margin = new Thickness(0, PackRows.RowGap(size), 0, 0);
            Rows.Children.Add(row);
        }
        if (view.MoreLine is { } more)
        {
            Rows.Children.Add(new TextBlock
            {
                Text = more, Height = PackRows.MoreLineHeight, Margin = new Thickness(0, PackRows.RowGap(size), 0, 0),
                LineHeight = PackRows.MoreLineHeight, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Theme.Current.Text.Dim.ToBrush(), FontSize = Theme.Current.Size(11),
                FontWeight = Theme.Current.Font.BodyWeight.ToFontWeight(),
            });
        }

        // The wake rule on the rows now on screen. Idempotent for the same
        // rows (a repaint, edit mode, a hold's tick): a drop needs a lower
        // quarter than the last sample, so only a new roster can bring one.
        SetAttention(_attention.Update(view.Rows, now));

        var running = _leavers.Any(now) || now < _attention.HoldUntil;
        if (running && !_holdTimer.IsEnabled) _holdTimer.Start();
        else if (!running && _holdTimer.IsEnabled) _holdTimer.Stop();
    }

    /// <summary>A full row: the name line (dot, name, dino and growth, sex, and with bars the badges and the health percent), then four captioned thin bars, or a note.</summary>
    private Grid FullRow(PackRow row)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(NameLineHeight) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(CaptionHeight) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(CaptionGap) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(ThinBarHeight) });

        var line = new DockPanel { LastChildFill = true };
        line.Children.Add(Dot(row));
        var dim = row.Kind == PackRowKind.Held;
        var name = NameText(row.Name, FullNameMax, dim);
        line.Children.Add(name);
        if (row.Kind == PackRowKind.Live && row.Friend is var live)
        {
            var pct = Percent(live.Health);
            DockPanel.SetDock(pct, Dock.Right);
            line.Children.Add(pct);
            var badges = Badges(live);
            DockPanel.SetDock(badges, Dock.Right);
            badges.Margin = new Thickness(6, 0, 4, 0);
            line.Children.Add(badges);
        }
        if (Sex(row.Friend.Gender) is { Length: > 0 } sex)
        {
            var symbol = Text(sex, 12, Theme.Current.Text.Label, Theme.Current.Font.BodyWeight);
            symbol.Margin = new Thickness(4, 0, 0, 0);
            DockPanel.SetDock(symbol, Dock.Right);
            line.Children.Add(symbol);
        }
        var meta = Text(Meta(row), 11, dim ? Theme.Current.Text.Dim : Theme.Current.Text.Label, Theme.Current.Font.BodyWeight);
        meta.Margin = new Thickness(6, 0, 0, 0);
        line.Children.Add(meta);
        grid.Children.Add(line);

        if (row.Kind == PackRowKind.Live && row.Friend is var f)
        {
            var captions = Quarters();
            var bars = Quarters();
            for (var i = 0; i < FourStats.Length; i++)
            {
                var (kind, caption, value) = FourStats[i];
                var label = Text(caption, 10, Theme.Current.Bars.Label, Theme.Current.Font.BodyWeight);
                Grid.SetColumn(label, i * 2);
                captions.Children.Add(label);
                var bar = Bar(kind, value(f), ThinBarHeight);
                Grid.SetColumn(bar, i * 2);
                bars.Children.Add(bar);
            }
            Grid.SetRow(captions, 2);
            Grid.SetRow(bars, 4);
            grid.Children.Add(captions);
            grid.Children.Add(bars);
        }
        else
        {
            var note = Text(HeldNote, 11, Theme.Current.Text.Dim, Theme.Current.Font.BodyWeight);
            note.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(note, 1);
            Grid.SetRowSpan(note, 4);
            grid.Children.Add(note);
        }
        return grid;
    }

    /// <summary>A compact row: one line, health only (the dot, the name, the bar, the badges and the percent), or a note.</summary>
    private DockPanel CompactRow(PackRow row)
    {
        var line = new DockPanel { LastChildFill = true };
        line.Children.Add(Dot(row));
        var dim = row.Kind == PackRowKind.Held;
        var name = NameText(row.Name, CompactNameMax, dim);
        name.Width = CompactNameMax; // one name column, so the bars start together down the list
        line.Children.Add(name);
        if (row.Kind == PackRowKind.Live && row.Friend is var live)
        {
            var pct = Percent(live.Health);
            DockPanel.SetDock(pct, Dock.Right);
            line.Children.Add(pct);
            var badges = Badges(live);
            badges.Margin = new Thickness(4, 0, 4, 0);
            DockPanel.SetDock(badges, Dock.Right);
            line.Children.Add(badges);
            var bar = Bar(BarKind.Health, live.Health, CompactBarHeight);
            bar.Margin = new Thickness(6, 0, 2, 0);
            bar.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(bar);
        }
        else
        {
            var note = Text(HeldNote, 11, Theme.Current.Text.Dim, Theme.Current.Font.BodyWeight);
            note.TextAlignment = TextAlignment.Right;
            note.Margin = new Thickness(6, 0, 0, 0);
            line.Children.Add(note);
        }
        return line;
    }

    /// <summary>The one row without bars: a friend who just left, held in place for five minutes.</summary>
    private const string HeldNote = "Not in game";

    /// <summary>The name line's middle: the dino and growth, as the Activity feed names a spawn.</summary>
    private static string Meta(PackRow row)
    {
        var dino = row.Dino ?? "";
        return dino.Length > 0 ? $"{dino} · {GrowthReadout.Percent(row.Growth)}%" : dino;
    }

    private static string Sex(string? gender) =>
        gender is null ? ""
        : gender.StartsWith("M", StringComparison.OrdinalIgnoreCase) ? "♂"
        : gender.StartsWith("F", StringComparison.OrdinalIgnoreCase) ? "♀"
        : "";

    // ---- The parts ---------------------------------------------------------------

    /// <summary>Four equal columns with the gap between them (columns 0, 2, 4, 6).</summary>
    private static Grid Quarters()
    {
        var grid = new Grid();
        for (var i = 0; i < 4; i++)
        {
            if (i > 0) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ColumnGap) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        return grid;
    }

    private Ellipse Dot(PackRow row) => new()
    {
        Width = 8, Height = 8, Fill = PaletteBrushes[WaypointPalette.Wrap(row.Colour)], Margin = new Thickness(0, 0, 6, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static TextBlock NameText(string name, double max, bool dim)
    {
        var text = Text(name, 13, dim ? Theme.Current.Text.Dim : Theme.Current.Text.Title, Theme.Current.Font.CaptionWeight);
        text.MaxWidth = max;
        return text;
    }

    /// <summary>The health percent at the line's right end, in the health readout's quarter colour.</summary>
    private static TextBlock Percent(double health)
    {
        var v = Math.Clamp(health, 0, 1);
        var text = Text($"{v * 100:0}%", 13, Theme.Current.Readout(BarStates.Of(v)), Theme.Current.Font.CaptionWeight);
        text.Margin = new Thickness(FrameContract.PackPercentMargin, 0, 0, 0);
        text.Width = FrameContract.PackPercentWidth; // one column, so "100%" moves no bar or badge
        text.TextAlignment = TextAlignment.Right;
        return text;
    }

    private static TextBlock Text(string text, double size, ThemeColor colour, ThemeWeight weight) => new()
    {
        Text = text, FontSize = Theme.Current.Size(size), Foreground = colour.ToBrush(), FontWeight = weight.ToFontWeight(),
        TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>The three fracture badges, H / B / L, each the theme's badge shape behind its letter, lit while fractured.</summary>
    private static StackPanel Badges(FriendState f)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (letter, fractured, last) in new[] { ("H", f.HeadFractured, false), ("B", f.BodyFractured, false), ("L", f.LegsFractured, true) })
        {
            var look = fractured ? Theme.Current.Badge.Fractured : Theme.Current.Badge.Intact;
            var cell = new Grid { Width = BadgeWidth, Height = BadgeHeight, Margin = new Thickness(0, 0, last ? 0 : BadgeGap, 0) };
            var shape = new ThemeShape { Slot = ShapeSlot.Badge, Fill = look.Fill.ToBrush() };
            shape.SetResourceReference(ThemeShape.KindProperty, "Theme.Shapes.Badge");
            shape.SetResourceReference(ThemeShape.CornerRadiusProperty, "Theme.Corner.Badge");
            cell.Children.Add(shape);
            cell.Children.Add(new TextBlock
            {
                Text = letter, FontSize = Theme.Current.Size(Theme.Current.Font.BadgeSize), FontWeight = FontWeights.Bold,
                Foreground = look.Text.ToBrush(), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            });
            panel.Children.Add(cell);
        }
        return panel;
    }

    /// <summary>
    /// One bar in its quarter state: the theme's track (and its inset line)
    /// holding the fill, its length the value's share of the track (two star
    /// columns, so no width is measured), its corner the theme's thin-bar
    /// corner, at most half its height.
    /// </summary>
    private static BarTrack Bar(BarKind kind, double value, double height)
    {
        var v = Math.Clamp(value, 0, 1);
        var look = Theme.Current.BarOf(kind);
        var radius = new CornerRadius(Math.Min(Theme.Current.Corner.Progress.Value, height / 2));
        var track = new BarTrack
        {
            Height = height, CornerRadius = radius, Background = look.Track.ToBrush(),
            LineBrush = look.TrackLine.ToBrush(), LineWidth = Theme.Current.Bars.TrackLineWidth,
        };
        var split = new Grid();
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(v, GridUnitType.Star) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - v, GridUnitType.Star) });
        if (v > 0) split.Children.Add(new BarFill { Height = height, CornerRadius = radius, Background = Theme.Current.Bar(kind, BarStates.Of(v)).Fill.ToBrush() });
        track.Child = split;
        return track;
    }
}

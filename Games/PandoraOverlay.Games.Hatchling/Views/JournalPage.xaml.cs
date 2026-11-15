using System.Windows;
using System.Windows.Controls;
using PandoraOverlay.Games.Engine;

namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// The Journal (H12, H18, H27): where a player learns that growing a different
/// species on the server pays (G6), the game's second purpose. The field journal's
/// three entries, each with its progress and an armed Swap; what server growth pays
/// for the real dino's species and the most grown; what server play paid lately; the
/// stars; and the overlay's last reading of the real dino.
///
/// <para><b>Never a wait (G4).</b> The page shows what a species pays NOW, as a
/// multiplier, and how far an entry has come, as a percent. It never shows a
/// countdown, a date, a time or anything that reads as "come back": freshness comes
/// back by itself and the journal has no deadline (H18, H12). A test scans every
/// text of the busiest state for a number of days, hours or minutes.</para>
///
/// <para><b>Only what the overlay saw (A6).</b> Every percent here is seen growth;
/// the last line reads the game's last reading (H71: in game, not in game, or none
/// yet), held in memory only. A reading that pays while the page is shown refreshes
/// it (the view hands <c>game.Changed</c> on as <see cref="Refresh"/>).</para>
///
/// <para><b>The page never changes the game's state</b> (A20): a swap is
/// <c>game.Swap</c>, which spends from the game's one wallet (G3) and saves before it
/// returns. Nothing ticks here.</para>
/// </summary>
internal sealed partial class JournalPage : Grid, IHatchlingPage
{
    /// <summary>An entry's card (H27): 400 x 70.</summary>
    internal const double EntryWidth = 400, EntryHeight = 70;

    /// <summary>An entry's bar (H27): 6 high.</summary>
    internal const double BarHeight = 6;

    /// <summary>The small Swap button (H27): 110 x 22.</summary>
    internal const double SwapWidth = 110, SwapHeight = 22;

    /// <summary>The rows of what server growth pays (H27): five at most, 17 each.</summary>
    internal const int MostPays = 5;

    /// <summary>A row of what server growth pays.</summary>
    internal const double PaysHeight = 17;

    /// <summary>A line of what server play paid lately: 18 each, six at most (<see cref="ServerPlay.LatelyMost"/>).</summary>
    internal const double LatelyHeight = 18;

    /// <summary>No Swap is armed.</summary>
    private const int NoEntry = -1;

    private readonly HatchlingView _view;
    private readonly HatchlingGame _game;
    private readonly EntryCard[] _entries;
    private readonly (TextBlock Left, TextBlock Right)[] _pays = new (TextBlock, TextBlock)[MostPays];
    private readonly (Grid Row, TextBlock Left, TextBlock Right)[] _lately = new (Grid, TextBlock, TextBlock)[ServerPlay.LatelyMost];
    private readonly string[] _payKeys = new string[MostPays];

    /// <summary>The entry whose Swap is armed, or <see cref="NoEntry"/>; and the species it would swap away.</summary>
    private int _armed = NoEntry;
    private string? _armedSpecies;

    public JournalPage(HatchlingView view, HatchlingGame game)
    {
        _view = view;
        _game = game;
        InitializeComponent();
        _entries = new[] { MakeEntry(Entry0, 0), MakeEntry(Entry1, 1), MakeEntry(Entry2, 2) };
        for (var i = 0; i < MostPays; i++) _pays[i] = MakePaysRow(i);
        for (var i = 0; i < _lately.Length; i++) _lately[i] = MakeLatelyRow(i);

        // A click anywhere but on the armed Swap disarms it ("armed": a stray click never acts).
        PreviewMouseLeftButtonDown += (_, e) => _view.Guarded(() =>
        {
            if (_armed == NoEntry || !PageParts.IsInside(e.OriginalSource as DependencyObject, _entries[_armed].Swap)) Disarm();
        });
    }

    /// <inheritdoc />
    public FrameworkElement Root => this;

    /// <summary>
    /// The real dino's reading a layout state shows (A26), set by the view before it
    /// shows the sample: null is "no reading yet". Read only while the page shows a
    /// sample; the game's own state is shown with the game's own last reading.
    /// </summary>
    internal DinoReading? SampleReading { get; set; }

    /// <summary>The entry whose Swap is armed, or -1.</summary>
    internal int ArmedEntry => _armed;

    // ===== IHatchlingPage ==================================================================

    /// <inheritdoc />
    public void Show() => Draw();

    /// <inheritdoc />
    public void Refresh() => Draw();

    /// <inheritdoc />
    public void Hide() => Disarm();

    // ===== Drawing ===========================================================================

    private void Draw()
    {
        var state = _view.State;
        var now = PageParts.Now(_view, _game);
        var (hasReading, reading) = Reading();
        var playing = hasReading && reading.InGame && SpeciesTable.TryGet(reading.Species, out var info) ? info.Name : null;

        // A Swap stays armed only while it would still swap the same entry and the wallet pays it.
        if (_armed != NoEntry && (_armed >= state.Journal.Open.Count
                || !string.Equals(state.Journal.Open[_armed].Species, _armedSpecies, StringComparison.Ordinal)
                || state.Wallet.Balance < Tuning.SwapPrice))
            Disarm();

        for (var i = 0; i < _entries.Length; i++)
            DrawEntry(_entries[i], i < state.Journal.Open.Count ? state.Journal.Open[i] : null, state, playing, reading);
        DrawPays(state, now, playing);
        DrawLately(state);
        StarsCount.Text = HatchlingText.StarsCount(SpeciesTable.All.Sum(s => state.Stars(s.Name)));
        RealDino.Text = !hasReading ? HatchlingText.RealDinoWaiting
            : reading.InGame ? HatchlingText.RealDino(playing ?? reading.Species ?? "", PageParts.WholePercent(reading.Growth * 100))
            : HatchlingText.RealDinoOut;
    }

    /// <summary>
    /// An entry (H12, H27): "Grow a Maiasaura by 75%" and what it pays; its bar of
    /// seen growth and "32 of 75%"; "Your Maiasaura now: 42%" (Highlight) when it is
    /// the species the real dino is, in game, else "Not the dino you are playing now"
    /// (Dim); and Swap, armed, disabled when the wallet is short of its price.
    /// </summary>
    private void DrawEntry(EntryCard card, JournalEntry? entry, GameState state, string? playing, DinoReading reading)
    {
        card.Card.Visibility = entry is null ? Visibility.Hidden : Visibility.Visible;
        if (entry is null) return;   // the game fills the journal at every load: three are always open

        var target = (int)entry.Target;
        card.Title.Text = HatchlingText.Entry(entry.Species, target);
        card.Reward.Text = HatchlingText.Plus(entry.Reward);
        var seen = Math.Clamp(entry.Seen, 0, entry.Target);
        card.Progress.Text = HatchlingText.EntryProgress(Math.Min(target, PageParts.WholePercent(seen)), target);
        var share = entry.Target > 0 ? seen / entry.Target : 0;
        card.Done.Width = new GridLength(share, GridUnitType.Star);
        card.Left.Width = new GridLength(1 - share, GridUnitType.Star);

        var mine = playing is not null && string.Equals(playing, entry.Species, StringComparison.OrdinalIgnoreCase);
        card.Line.Text = mine ? HatchlingText.EntryPlaying(entry.Species, PageParts.WholePercent(reading.Growth * 100)) : HatchlingText.EntryNotPlaying;
        card.LineKey = mine ? "Theme.Text.Highlight" : "Theme.Text.Dim";
        card.Line.SetResourceReference(TextBlock.ForegroundProperty, card.LineKey);

        card.Swap.IsEnabled = state.Wallet.Balance >= Tuning.SwapPrice;
        SetSwap(card, armed: _armed == card.Index);
    }

    private static void SetSwap(EntryCard card, bool armed)
    {
        card.SwapWords.Text = armed ? HatchlingText.SwapArmed : HatchlingText.Swap(Tuning.SwapPrice);
        var style = armed ? "SaveButtonStyle" : "NeutralButtonStyle";
        if (card.SwapStyle == style) return;
        card.SwapStyle = style;
        card.Swap.SetResourceReference(StyleProperty, style);
    }

    /// <summary>
    /// What server growth pays (H27): the real dino's species first (Highlight), when
    /// it is in game and the table knows it, then the most grown, five rows at most:
    /// "Maiasaura · 62% grown" and the multiplier growing one pays now (H18). A species
    /// never grown is not listed: the note under the rows says what it pays.
    /// </summary>
    private void DrawPays(GameState state, DateTime now, string? playing)
    {
        var rows = new List<string>(MostPays);
        if (playing is not null) rows.Add(playing);
        rows.AddRange(SpeciesTable.All
            .Select((s, order) => (s.Name, order, state.Record(s.Name).Grown))
            .Where(s => s.Grown > 0 && !string.Equals(s.Name, playing, StringComparison.Ordinal))
            .OrderByDescending(s => s.Grown).ThenBy(s => s.order)
            .Select(s => s.Name)
            .Take(MostPays - rows.Count));

        for (var i = 0; i < MostPays; i++)
        {
            var (left, right) = _pays[i];
            var shown = i < rows.Count;
            left.Visibility = right.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            if (!shown) continue;
            var name = rows[i];
            left.Text = HatchlingText.PaysRow(name, PageParts.WholePercent(state.Record(name).Grown));
            right.Text = HatchlingText.Times(Freshness.At(state, name, now));
            var yours = string.Equals(name, playing, StringComparison.Ordinal);
            _payKeys[i] = yours ? "Theme.Text.Highlight" : "Theme.Text.Text";
            left.SetResourceReference(TextBlock.ForegroundProperty, _payKeys[i]);
            right.SetResourceReference(TextBlock.ForegroundProperty, yours ? "Theme.Text.Highlight" : "Theme.Text.Bright");
        }
    }

    /// <summary>
    /// What server play paid lately (H27, task H06's lines), newest first, six at most,
    /// each with its amount at the right: a growth line and its pay so far, a star, an
    /// unlock, a journal entry finished; with none, the line that says how.
    /// </summary>
    private void DrawLately(GameState state)
    {
        var lines = state.Lately;
        LatelyNone.Visibility = lines.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        for (var i = 0; i < _lately.Length; i++)
        {
            var (row, left, right) = _lately[i];
            row.Visibility = i < lines.Count ? Visibility.Visible : Visibility.Collapsed;
            if (i >= lines.Count) continue;
            var (words, amount) = LatelyText(lines[i]);
            left.Text = words;
            right.Text = amount;
        }
    }

    /// <summary>A line of what server play paid lately, as its words and its amount.</summary>
    internal static (string Words, string Amount) LatelyText(LatelyLine line) => line.Kind switch
    {
        LatelyKind.Grew => (HatchlingText.LatelyGrew(line.Species, line.Percent, line.Fresh), HatchlingText.Plus(line.Amount)),
        LatelyKind.Star => (HatchlingText.LatelyStar(line.Species), HatchlingText.LatelyStarMark),
        LatelyKind.Unlocked => (HatchlingText.LatelyUnlocked(line.Species), HatchlingText.LatelyNew),
        LatelyKind.Journal => (HatchlingText.LatelyJournal(line.Species, line.Target), HatchlingText.Plus(line.Amount)),
        _ => ("", ""),
    };

    /// <summary>The reading the last line and the entries read (H71): the game's, or a sample's.</summary>
    private (bool Has, DinoReading Reading) Reading() =>
        PageParts.Sampled(_view, _game)
            ? (SampleReading is not null, SampleReading ?? DinoReading.NotInGame)
            : (_game.HasReading, _game.LastReading);

    // ===== What a click does ===================================================================

    /// <summary>
    /// An entry's Swap, armed (H12): the first click changes its words and its look
    /// and does nothing else; the second replaces the entry with a new draw for its
    /// price, the wallet in the bar following. Refused (and disarmed) while the
    /// wallet is short.
    /// </summary>
    internal void SwapClicked(int index)
    {
        if (PageParts.Sampled(_view, _game)) return;
        var state = _view.State;
        if (index < 0 || index >= state.Journal.Open.Count || state.Wallet.Balance < Tuning.SwapPrice)
        {
            Disarm();
            return;
        }
        if (_armed != index)
        {
            Disarm();
            _armed = index;
            _armedSpecies = state.Journal.Open[index].Species;
            SetSwap(_entries[index], armed: true);
            return;
        }
        Disarm();
        var earned = _view.Earned;
        if (_game.Swap(index))
        {
            _view.Did(Cue.Shuffle, earned);   // H99: the armed swap's second click; its first is silent
            _view.ShowWallet();
        }
        Draw();
    }

    /// <summary>The armed Swap back to its first look: the mouse leaving it, a click anywhere else, or the page left.</summary>
    internal void Disarm()
    {
        if (_armed == NoEntry) return;
        var was = _entries[_armed];
        _armed = NoEntry;
        _armedSpecies = null;
        SetSwap(was, armed: false);
    }

    // ===== Made once ============================================================================

    /// <summary>
    /// An entry's card in its row: the title and the reward (16); the bar and its
    /// count (15); what is left; the line about the real dino and the Swap (22).
    /// </summary>
    private EntryCard MakeEntry(Grid host, int index)
    {
        var title = PageParts.Line("Theme.Text.Bright", "Theme.Size.12");
        var reward = PageParts.Line("Theme.Text.Highlight", "Theme.Size.12");

        var done = new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) };
        var left = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
        var bar = new Grid();
        bar.ColumnDefinitions.Add(done);
        bar.ColumnDefinitions.Add(left);
        var fill = new Border();
        fill.SetResourceReference(Border.BackgroundProperty, "Theme.Storage.Growth");
        fill.SetResourceReference(Border.CornerRadiusProperty, "Theme.Corner.Progress");
        bar.Children.Add(fill);
        var track = new Border { Height = BarHeight, VerticalAlignment = VerticalAlignment.Center, Child = bar };
        track.SetResourceReference(Border.BackgroundProperty, "Theme.Storage.BarTrack");
        track.SetResourceReference(Border.CornerRadiusProperty, "Theme.Corner.Progress");
        var progress = PageParts.Line("Theme.Text.Dim", "Theme.Size.11");

        var line = PageParts.Line("Theme.Text.Dim", "Theme.Size.11");
        var words = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        words.SetResourceReference(TextBlock.FontSizeProperty, "Theme.Size.11");
        var swap = new Button
        {
            Focusable = false,
            Width = SwapWidth,
            Height = SwapHeight,
            Padding = new Thickness(6, 1, 6, 1),
            Content = words,
        };
        swap.SetResourceReference(StyleProperty, "NeutralButtonStyle");
        swap.Click += (_, _) => _view.Guarded(() => SwapClicked(index));
        swap.MouseLeave += (_, _) => _view.Guarded(() =>
        {
            if (_armed == index) Disarm();
        });

        var lines = PageParts.Rows(16, 15, 0, SwapHeight);
        PageParts.In(lines, 0, PageParts.LeftAndRight(title, reward));
        PageParts.In(lines, 1, PageParts.LeftAndRight(new Grid { Children = { track } }, progress, gap: 8));
        PageParts.In(lines, 3, PageParts.LeftAndRight(line, swap, gap: 8));

        var card = PageParts.Card(EntryWidth, EntryHeight);
        card.Child = lines;
        host.Children.Add(card);
        return new EntryCard(index, card, title, reward, done, left, progress, line, swap, words);
    }

    private (TextBlock, TextBlock) MakePaysRow(int index)
    {
        PaysRows.RowDefinitions.Add(new RowDefinition { Height = new GridLength(PaysHeight) });
        var left = PageParts.Line("Theme.Text.Text", "Theme.Size.11");
        var right = PageParts.Line("Theme.Text.Bright", "Theme.Size.11");
        PageParts.In(PaysRows, index, PageParts.LeftAndRight(left, right, gap: 8));
        return (left, right);
    }

    private (Grid, TextBlock, TextBlock) MakeLatelyRow(int index)
    {
        LatelyRows.RowDefinitions.Add(new RowDefinition { Height = new GridLength(LatelyHeight) });
        var left = PageParts.Line("Theme.Text.Text", "Theme.Size.11");
        var right = PageParts.Line("Theme.Text.Highlight", "Theme.Size.11");
        var row = PageParts.In(LatelyRows, index, PageParts.LeftAndRight(left, right, gap: 8));
        return (row, left, right);
    }

    /// <summary>An entry's card and its parts, with the keys last set on them (what a test reads).</summary>
    private sealed class EntryCard
    {
        public EntryCard(int index, Border card, TextBlock title, TextBlock reward, ColumnDefinition done, ColumnDefinition left,
            TextBlock progress, TextBlock line, Button swap, TextBlock swapWords)
        {
            Index = index;
            Card = card;
            Title = title;
            Reward = reward;
            Done = done;
            Left = left;
            Progress = progress;
            Line = line;
            Swap = swap;
            SwapWords = swapWords;
        }

        public int Index { get; }
        public Border Card { get; }
        public TextBlock Title { get; }
        public TextBlock Reward { get; }
        public ColumnDefinition Done { get; }
        public ColumnDefinition Left { get; }
        public TextBlock Progress { get; }
        public TextBlock Line { get; }
        public Button Swap { get; }
        public TextBlock SwapWords { get; }
        public string LineKey { get; set; } = "";
        public string SwapStyle { get; set; } = "NeutralButtonStyle";
    }

    // ===== What a test reads ===================================================================

    /// <summary>The entries as shown: their words, the bar's share, the line's key, and the Swap's words, whether it is enabled and its style.</summary>
    internal IReadOnlyList<(string Title, string Reward, string Progress, double Share, string Line, string LineKey, string Swap, bool SwapEnabled, string SwapStyle)> EntriesShown =>
        _entries.Where(e => e.Card.Visibility == Visibility.Visible)
            .Select(e => (e.Title.Text, e.Reward.Text, e.Progress.Text, e.Done.Width.Value, e.Line.Text, e.LineKey, e.SwapWords.Text, e.Swap.IsEnabled, e.SwapStyle))
            .ToList();

    /// <summary>The rows of what server growth pays, as shown, with the key of each row's words.</summary>
    internal IReadOnlyList<(string Row, string Times, string Key)> PaysShown =>
        _pays.Select((p, i) => (p.Left, p.Right, Key: _payKeys[i])).Where(p => p.Left.Visibility == Visibility.Visible)
            .Select(p => (p.Left.Text, p.Right.Text, p.Key)).ToList();

    /// <summary>The lines of what server play paid lately, as shown, and whether the "nothing yet" line shows.</summary>
    internal (IReadOnlyList<(string Words, string Amount)> Lines, bool None) LatelyShown =>
        (_lately.Where(l => l.Row.Visibility == Visibility.Visible).Select(l => (l.Left.Text, l.Right.Text)).ToList(),
            LatelyNone.Visibility == Visibility.Visible);

    /// <summary>The stars' count and the real dino's line, as shown.</summary>
    internal (string Stars, string RealDino) FootShown => (StarsCount.Text, RealDino.Text);
}

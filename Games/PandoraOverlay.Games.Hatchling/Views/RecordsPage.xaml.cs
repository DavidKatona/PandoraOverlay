using System.Windows;
using System.Windows.Controls;
using PandoraOverlay.Games.Engine;

namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// Records (H26, H27): the 24 achievements, earned or not, with what each pays,
/// and the lineage's totals. Shown here and in a run's summary only, never in the
/// Activity feed or the tray (G22). An achievement pays a little of the game's
/// currency or nothing, never power, and the page says only what it pays.
///
/// <para>Nothing on it is a click, and nothing ticks: it is drawn from
/// <c>view.State</c> on Show and in place on Refresh (a reading that earned one).</para>
/// </summary>
internal sealed partial class RecordsPage : Grid, IHatchlingPage
{
    /// <summary>An achievement's cell (H27): 255 x 34, in two columns of twelve, 10 apart.</summary>
    internal const double CellWidth = 255, CellHeight = 34, CellGap = 10;

    /// <summary>Cells in a column.</summary>
    internal const int PerColumn = 12;

    /// <summary>A total's row (H27): 17 high.</summary>
    internal const double TotalHeight = 17;

    /// <summary>The mark's column at a cell's left.</summary>
    private const double MarkWidth = 18;

    private readonly HatchlingView _view;
    private readonly Cell[] _cells;
    private readonly TextBlock[] _totals;

    public RecordsPage(HatchlingView view)
    {
        _view = view;
        InitializeComponent();
        _cells = HatchlingText.AchievementIds.Select(MakeCell).ToArray();
        _totals = new[]
        {
            HatchlingText.TotalRuns, HatchlingText.TotalWins, HatchlingText.TotalKills, HatchlingText.TotalEarned,
            HatchlingText.TotalSpecies, HatchlingText.TotalStars, HatchlingText.TotalJournal, HatchlingText.TotalDanger,
        }.Select(MakeTotal).ToArray();
    }

    /// <inheritdoc />
    public FrameworkElement Root => this;

    /// <inheritdoc />
    public void Show() => Draw();

    /// <inheritdoc />
    public void Refresh() => Draw();

    /// <inheritdoc />
    public void Hide() { }

    // ===== Drawing ===========================================================================

    private void Draw()
    {
        var state = _view.State;
        var achievements = state.Achievements;
        foreach (var cell in _cells) DrawCell(cell, achievements);
        EarnedCount.Text = HatchlingText.AchievementsCount(
            achievements.All.Count(a => achievements.Has(a.Id)), achievements.All.Count);

        var values = TotalsOf(state);
        for (var i = 0; i < _totals.Length; i++) _totals[i].Text = values[i];

        if (SpeciesTable.TryGet(state.Selected, out var chosen))
        {
            var record = state.Record(chosen.Name);
            ChosenLine.Text = HatchlingText.ChosenLine(chosen.Name, record.Runs, PageParts.WholePercent(record.BestGrowth), record.WonAt);
        }
    }

    /// <summary>
    /// An achievement's cell (H27): the mark ("✓" Good earned, "○" Off not), the name
    /// (Bright earned, Text2 not) over its text (Dim), and at the right what it pays
    /// (Highlight, or Good once earned; nothing for one that pays nothing).
    /// </summary>
    private static void DrawCell(Cell cell, Achievements achievements)
    {
        var earned = achievements.Has(cell.Id);
        var definition = achievements.All.FirstOrDefault(a => a.Id == cell.Id);
        cell.Mark.Text = earned ? HatchlingText.Earned : HatchlingText.NotEarned;
        cell.MarkKey = earned ? "Theme.Text.Good" : "Theme.Text.Off";
        cell.Mark.SetResourceReference(TextBlock.ForegroundProperty, cell.MarkKey);
        cell.Name.Text = HatchlingText.AchievementName(cell.Id);
        cell.NameKey = earned ? "Theme.Text.Bright" : "Theme.Text.Text2";
        cell.Name.SetResourceReference(TextBlock.ForegroundProperty, cell.NameKey);
        cell.Text.Text = HatchlingText.AchievementText(cell.Id);
        cell.Reward.Text = definition?.Reward is AchievementReward.Currency pay ? HatchlingText.Plus(pay.Amount) : "";
        cell.RewardKey = earned ? "Theme.Text.Good" : "Theme.Text.Highlight";
        cell.Reward.SetResourceReference(TextBlock.ForegroundProperty, cell.RewardKey);
    }

    /// <summary>
    /// The lineage's totals (H27), in the rows' order: runs, wins, predators
    /// defeated, what was earned in all, species unlocked of 21, stars of 63, journal
    /// entries finished, the highest danger won with any species.
    /// </summary>
    internal static string[] TotalsOf(GameState state)
    {
        var stars = SpeciesTable.All.Sum(s => state.Stars(s.Name));
        var unlocked = SpeciesTable.All.Count(s => Unlocks.IsUnlocked(state, s.Name));
        var highest = SpeciesTable.All.Select(s => state.Record(s.Name).WonAt).DefaultIfEmpty(0).Max();
        return new[]
        {
            HatchlingText.Count(state.Totals.Runs),
            HatchlingText.Count(state.Totals.Wins),
            HatchlingText.Count(state.Totals.Kills),
            HatchlingText.Count(state.Wallet.Earned),
            HatchlingText.SpeciesCount(unlocked),
            HatchlingText.StarsOf(stars),
            HatchlingText.Count(state.Journal.Done),
            HatchlingText.Count(highest),
        };
    }

    // ===== Made once ============================================================================

    /// <summary>An achievement's cell, in the table's order: the first twelve down the left column, the rest down the right.</summary>
    private Cell MakeCell(string id, int index)
    {
        var mark = PageParts.Line("Theme.Text.Off", "Theme.Size.12");
        var name = PageParts.Line("Theme.Text.Text2", "Theme.Size.12");
        var text = PageParts.Line("Theme.Text.Dim", "Theme.Size.10.5");
        var reward = PageParts.Line("Theme.Text.Highlight", "Theme.Size.12");

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, text } };
        var grid = new Grid
        {
            Width = CellWidth,
            Height = CellHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(index / PerColumn * (CellWidth + CellGap), index % PerColumn * CellHeight, 0, 0),
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(MarkWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(words, 1);
        reward.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(reward, 2);
        grid.Children.Add(mark);
        grid.Children.Add(words);
        grid.Children.Add(reward);
        Cells.Children.Add(grid);
        return new Cell(id, mark, name, text, reward);
    }

    /// <summary>A total's row: its label (Text2) and, at the right, its value (Bright).</summary>
    private TextBlock MakeTotal(string label, int index)
    {
        TotalRows.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TotalHeight) });
        var value = PageParts.Line("Theme.Text.Bright", "Theme.Size.11");
        var row = PageParts.LeftAndRight(PageParts.Line("Theme.Text.Text2", "Theme.Size.11", label), value, gap: 8);
        PageParts.In(TotalRows, index, row);
        return value;
    }

    /// <summary>An achievement's cell and its parts, with the keys last set on them (what a test reads).</summary>
    private sealed class Cell
    {
        public Cell(string id, TextBlock mark, TextBlock name, TextBlock text, TextBlock reward)
        {
            Id = id;
            Mark = mark;
            Name = name;
            Text = text;
            Reward = reward;
        }

        public string Id { get; }
        public TextBlock Mark { get; }
        public TextBlock Name { get; }
        public TextBlock Text { get; }
        public TextBlock Reward { get; }
        public string MarkKey { get; set; } = "";
        public string NameKey { get; set; } = "";
        public string RewardKey { get; set; } = "";
    }

    // ===== What a test reads ===================================================================

    /// <summary>The cells as shown, in the table's order: where each stands, its words, and its keys.</summary>
    internal IReadOnlyList<(string Id, Thickness Place, string Mark, string Name, string Text, string Reward, string MarkKey, string NameKey, string RewardKey)> CellsShown =>
        _cells.Select(c => (c.Id, ((FrameworkElement)c.Mark.Parent).Margin, c.Mark.Text, c.Name.Text, c.Text.Text, c.Reward.Text, c.MarkKey, c.NameKey, c.RewardKey)).ToList();

    /// <summary>"5 of 24", the totals' values in their order, and the chosen species' line.</summary>
    internal (string Count, IReadOnlyList<string> Totals, string Chosen) SummaryShown =>
        (EarnedCount.Text, _totals.Select(t => t.Text).ToList(), ChosenLine.Text);
}

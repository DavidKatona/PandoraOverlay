using System.Windows;
using System.Windows.Controls;
using PandoraOverlay.Games.Engine;

namespace PandoraOverlay.Games.Hatchling;

// The Run page: the side panel (your dino, your move, the turn list), the offer, the
// summary and the context line (H27, H23, H28). Every colour, size and
// corner set here is a theme key, by SetResourceReference, written whole (A25: the
// app's key scan reads each literal); every word is HatchlingText's (A24).
internal sealed partial class RunPage
{
    /// <summary>The width a bar's fill can take: its column (the panel's 234 inside, less the label's 46 and the value's 48) less the track's margins.</summary>
    internal const double BarWidth = 132;

    /// <summary>
    /// A turn list row's height, its marker's and its name's widths (H28, H80): the
    /// widest name, "Herrerasaurus 11", is 88.5 in the widest font, so the name takes
    /// 90 and what the row did the rest (134), which holds "spotted you · bit you: 12"
    /// in every theme.
    /// </summary>
    internal const double RowHeight = 16, MarkerWidth = 10, NameWidth = 90;

    /// <summary>A card of the offer (H27): 112 x 132, 8 apart.</summary>
    internal const double OfferCardWidth = 112, OfferCardHeight = 132, OfferCardGap = 8;

    /// <summary>A line of the summary's pay or achievements: 18 high.</summary>
    internal const double SummaryLineHeight = 18;

    /// <summary>The most pay lines the summary shows (H27: nine at most).</summary>
    internal const int MostPayLines = 9;

    /// <summary>The most achievements named in the summary; more are "and N more".</summary>
    internal const int MostEarnedLines = 3;

    private readonly (Grid Row, TextBlock Marker, TextBlock Name, TextBlock Text)[] _rows = new (Grid, TextBlock, TextBlock, TextBlock)[TurnScript.MaxRows];

    // ---- the turn list ---------------------------------------------------------------------

    /// <summary>The turn list's eleven rows (H44: never more), made once: a marker, the name, what it did; all size 11, trimmed.</summary>
    private void BuildRows()
    {
        for (var i = 0; i < _rows.Length; i++)
        {
            var row = new Grid { Height = RowHeight, Visibility = Visibility.Collapsed };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(MarkerWidth) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(NameWidth) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var marker = Line(0, "Theme.Text.Marker");
            var name = Line(1, "Theme.Text.Text2");
            var text = Line(2, "Theme.Text.Text");
            row.Children.Add(marker);
            row.Children.Add(name);
            row.Children.Add(text);
            TurnRows.Children.Add(row);
            _rows[i] = (row, marker, name, text);
        }

        static TextBlock Line(int column, string foreground)
        {
            var block = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(block, column);
            block.SetResourceReference(TextBlock.ForegroundProperty, foreground);
            block.SetResourceReference(TextBlock.FontSizeProperty, "Theme.Size.11");
            return block;
        }
    }

    /// <summary>
    /// The turn list (H28): a row per actor in the order of the turn (H1),
    /// the marker on the row acting, its name Bright while it acts; at rest every row
    /// says what it did and the marker is on You. <paramref name="texts"/> null means
    /// every row's words from its codes; otherwise a null entry is a row still to act.
    /// </summary>
    private void ShowRows(IReadOnlyList<TurnRow> rows, string?[]? texts, int marker, bool playing)
    {
        for (var i = 0; i < _rows.Length; i++)
        {
            var (grid, mark, name, text) = _rows[i];
            if (i >= rows.Count)
            {
                grid.Visibility = Visibility.Collapsed;
                continue;
            }
            var row = rows[i];
            grid.Visibility = Visibility.Visible;
            mark.Text = i == marker ? HatchlingText.RowMarker : "";
            name.Text = HatchlingText.RowName(row);
            text.Text = texts is null ? HatchlingText.RowText(row) : texts[i] ?? "";
            name.SetResourceReference(TextBlock.ForegroundProperty, playing && i == marker ? "Theme.Text.Bright" : NameKey(row));
        }
    }

    /// <summary>A row's name colour by who it is and its alert (H28).</summary>
    internal static string NameKey(TurnRow row) => row.Actor switch
    {
        RowActor.You => "Theme.Text.Highlight",
        RowActor.Predator when row.Alert >= Tuning.AlertSeen || row.Type == Tuning.Apex => "Theme.Text.Danger",
        RowActor.Predator when row.Alert > 0 => "Theme.Text.Caution",
        RowActor.Predator => "Theme.Text.Text2",
        _ => "Theme.Text.Dim",
    };

    /// <summary>What the list's rows read now, the marker's row, and each name's colour key: what a test reads off the screen.</summary>
    internal IReadOnlyList<(string Marker, string Name, string Text)> RowsShown =>
        _rows.Where(r => r.Row.Visibility == Visibility.Visible).Select(r => (r.Marker.Text, r.Name.Text, r.Text.Text)).ToList();

    /// <summary>The banner as it reads now.</summary>
    internal string BannerShown => Banner.Text;

    /// <summary>The context line as it reads now.</summary>
    internal string ContextShown => Context.Text;

    // ---- your dino ---------------------------------------------------------------------------

    /// <summary>
    /// The info card of your dino: its species and stage, the five stage segments, the
    /// three bars (the theme's own colour for the quarter each value is in, as the
    /// stats panel's, by <see cref="Quarters"/>), the status line, the instincts. The
    /// values are <paramref name="values"/>: the run's at rest, the beat's during a
    /// playback (the panel follows the beats, G9).
    /// </summary>
    private void ShowYourDino(RunState run, PanelValues values)
    {
        // The whole percent shown takes H58's slack (growth sits a hair under a whole
        // number now and then); the stage is the rules' own, with none: a stage never
        // means two things, and a hair under 25 the board draws a hatchling.
        var growth = (int)Math.Floor(values.Growth + RunPay.GrowthSlack);
        var stage = Stages.Of(values.Growth);
        SpeciesText.Text = run.Species;
        StageText.Text = HatchlingText.StageLine(stage, growth);
        var segments = new[] { Segment0, Segment1, Segment2, Segment3, Segment4 };
        for (var i = 0; i < segments.Length; i++)
            segments[i].SetResourceReference(Border.BackgroundProperty, i <= stage ? "Theme.Storage.Growth" : "Theme.Storage.BarTrack");

        var max = SpeciesTable.TryGet(run.Species, out var species)
            ? Stats.Of(species, run.Ranks, run.Stars, run.Instincts, values.Growth).MaxHealth
            : 1;
        var health = Math.Clamp(values.Health, 0, 1);
        var food = Math.Clamp(values.Food / Tuning.FoodMost, 0, 1);
        var water = Math.Clamp(values.Water / Tuning.WaterMost, 0, 1);
        Bar(HealthFill, health, Quarters.HealthKey(Quarters.Of(health)));
        Bar(FoodFill, food, Quarters.FoodKey(Quarters.Of(food)));
        Bar(WaterFill, water, Quarters.WaterKey(Quarters.Of(water)));
        HealthValue.Text = HatchlingText.HealthValue((int)Tuning.Round(health * max), max);
        FoodValue.Text = HatchlingText.Count((long)Tuning.Round(values.Food));
        WaterValue.Text = HatchlingText.Count((long)Tuning.Round(values.Water));

        var (status, key) = Status(run);
        StatusText.Text = status;
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, key);
        InstinctsText.Text = HatchlingText.InstinctList(run.Instincts);
    }

    private static void Bar(Border fill, double fraction, string key)
    {
        fill.Width = BarWidth * fraction;
        fill.SetResourceReference(Border.BackgroundProperty, key);
    }

    /// <summary>
    /// The status line (H27): the apex's count once it has come; else hidden, wading,
    /// or in the open; empty once the run is over (H78: an ended run has no state to
    /// tell; the row keeps its height).
    /// </summary>
    internal static (string Text, string Key) Status(RunState run)
    {
        if (run.Over is not null) return ("", "Theme.Text.Text2");
        var stats = Stats.Of(run);
        if (run.ApexAt != Tuning.ApexNotYet && run.Creatures.Any(c => c.Kind == CreatureKind.Predator && c.Type == Tuning.Apex))
            return (HatchlingText.StatusApex(Math.Max(0, World.OutlastTurns(run) - (run.Turn - run.ApexAt))), "Theme.Text.Danger");
        if (Sight.InCover(run, stats))
            return (run.Tiles[run.You.At] == Terrain.Water ? HatchlingText.StatusWater : HatchlingText.StatusForest, "Theme.Text.Good");
        if (Sight.Hidden(run, stats)) return (HatchlingText.StatusStill, "Theme.Text.Good");
        if (Sight.Wading(run, stats)) return (HatchlingText.StatusWading, "Theme.Text.Caution");
        return (HatchlingText.StatusOpen, "Theme.Text.Text2");
    }

    /// <summary>The panel's values as shown now: health 0 to 1, food and water as written.</summary>
    internal (string Health, string Food, string Water, string Stage) PanelShown =>
        (HealthValue.Text, FoodValue.Text, WaterValue.Text, StageText.Text);

    /// <summary>The status line as shown.</summary>
    internal string StatusShown => StatusText.Text;

    // ---- your move ---------------------------------------------------------------------------

    /// <summary>
    /// The paces with this dino's own numbers (the pace's noise with its build and
    /// traits, never under 0; the sprint's range), and Eat, Drink and Rest with what
    /// the move would add NOW (H49: the same functions the move's event takes its
    /// number from); Eat and Drink disabled where the rules would refuse them, and
    /// every move and the paces while the run takes none (<paramref name="open"/>
    /// false: an offer is open or the run is over, H5). During a playback they keep
    /// their look (H5): a press on them then only skips. Leave stays enabled with an
    /// offer open (the plan's 1.9: a run can always be left) and goes only once the
    /// run is over, whose way out is "Back to camp".
    /// </summary>
    private void ShowMoves(RunState run, Stats stats, bool open)
    {
        WalkPace.IsEnabled = SneakPace.IsEnabled = SprintPace.IsEnabled = open;
        RestButton.IsEnabled = open;
        LeaveButton.IsEnabled = run.Over is null;
        WalkLine.Text = HatchlingText.Noise(Moves.NoiseOf(stats, Tuning.WalkNoise));
        SneakLine.Text = HatchlingText.Noise(Moves.NoiseOf(stats, Tuning.SneakNoise));
        SprintLine.Text = HatchlingText.SprintLine(stats.SprintRange, Moves.NoiseOf(stats, Tuning.SprintNoise));

        var food = Moves.FoodHere(run, stats);
        var canEat = food is not null && run.You.Food < Tuning.FoodMost;
        EatLine.Text = food is null ? HatchlingText.EatNothing : HatchlingText.EatGain(Moves.EatGain(run, stats));
        EatButton.IsEnabled = open && canEat;

        var near = Moves.WaterNear(run);
        DrinkLine.Text = near ? HatchlingText.DrinkGain(Moves.DrinkGain(run)) : HatchlingText.DrinkNothing;
        DrinkButton.IsEnabled = open && near && run.You.Water < Tuning.WaterMost;

        RestLine.Text = HatchlingText.RestGain(Moves.RestGain(run, stats));
    }

    /// <summary>The moves' second lines as shown: Eat, Drink, Rest, and whether Eat and Drink are enabled.</summary>
    internal (string Eat, bool CanEat, string Drink, bool CanDrink, string RestLine) MovesShown =>
        (EatLine.Text, EatButton.IsEnabled, DrinkLine.Text, DrinkButton.IsEnabled, RestLine.Text);

    // ---- the offer -----------------------------------------------------------------------------

    /// <summary>
    /// The offer (H27): open once the playback has finished, over the board:
    /// its title ("You grew to Juvenile", or "A new instinct" for one owed at the start),
    /// the three or four cards, each a click, and Redraw with its price, disabled when
    /// the wallet is short.
    /// </summary>
    private void ShowOffer(GameState state)
    {
        _pressedCard = NoCard;   // the cards are rebuilt or gone: no press began on any of them (H79)
        var run = state.Run;
        if (run is not { Over: null, Offer: { } offer })
        {
            OfferCover.Visibility = Visibility.Collapsed;
            return;
        }
        OfferCover.Visibility = Visibility.Visible;
        OfferTitle.Text = run.Turn == 0 ? HatchlingText.OfferStart : HatchlingText.OfferGrew(Math.Max(Stages.Juvenile, Stages.Of(run.You.Growth)));
        OfferCards.Children.Clear();
        for (var i = 0; i < offer.Count; i++)
            OfferCards.Children.Add(OfferCard(offer[i], i, last: i == offer.Count - 1));
        var price = Offers.RedrawPrice(run);
        RedrawButton.Content = price == 0 ? HatchlingText.RedrawFree : HatchlingText.Redraw(price);
        RedrawButton.IsEnabled = state.Wallet.Balance >= price;
    }

    private Border OfferCard(Instinct instinct, int index, bool last)
    {
        var name = new TextBlock
        {
            Text = HatchlingText.InstinctName(instinct),
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 34,
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Text.Bright");
        name.SetResourceReference(TextBlock.FontSizeProperty, "Theme.Size.12");
        name.SetResourceReference(TextBlock.FontWeightProperty, "Theme.Font.ValueWeight");
        var rule = new TextBlock
        {
            Text = HatchlingText.InstinctText(instinct),
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 4, 0, 0),
            MaxHeight = 76,
        };
        rule.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Text.Text");
        rule.SetResourceReference(TextBlock.FontSizeProperty, "Theme.Size.11");

        var card = new Border
        {
            Width = OfferCardWidth,
            Height = OfferCardHeight,
            Margin = new Thickness(0, 0, last ? 0 : OfferCardGap, 0),
            Padding = new Thickness(8, 6, 8, 6),
            BorderThickness = new Thickness(1),
            Child = new StackPanel { Children = { name, rule } },
        };
        card.SetResourceReference(Border.BackgroundProperty, "Theme.Card.Surface");
        card.SetResourceReference(Border.BorderBrushProperty, "Theme.Card.Line");
        card.SetResourceReference(Border.CornerRadiusProperty, "Theme.Corner.Card");
        card.MouseEnter += (_, _) => _view.Guarded(() => card.SetResourceReference(Border.BorderBrushProperty, "Theme.Card.HoverLine"));
        card.MouseLeave += (_, _) => _view.Guarded(() =>
        {
            card.SetResourceReference(Border.BorderBrushProperty, "Theme.Card.Line");
            CardLeft(index);
        });
        // A pick is a press AND a release on this card (H79): the release of the press
        // that skipped the playback, when the offer opened under the pointer, is not one.
        card.MouseLeftButtonDown += (_, _) => _view.Guarded(() => CardPressed(index));
        card.MouseLeftButtonUp += (_, _) => _view.Guarded(() => CardReleased(index));
        return card;
    }

    /// <summary>The offer as shown: open or not, its title, its cards' names, Redraw's label and whether it is enabled.</summary>
    internal (bool Open, string Title, IReadOnlyList<string> Cards, string Redraw, bool CanRedraw) OfferShown =>
        (OfferCover.Visibility == Visibility.Visible, OfferTitle.Text,
            OfferCards.Children.OfType<Border>().Select(b => ((TextBlock)((StackPanel)b.Child).Children[0]).Text).ToList(),
            RedrawButton.Content as string ?? "", RedrawButton.IsEnabled);

    // ---- the summary ---------------------------------------------------------------------------

    /// <summary>
    /// The summary (H27): open once the run is over and the playback has
    /// finished: the title (a win in Good), the reason, the pay lines as given (H58: an
    /// amount may be 0), the run's total, up to three achievements and "and N more",
    /// a danger unlocked, and "Back to camp".
    /// </summary>
    private void ShowSummary(GameState state)
    {
        if (state.Run?.Over is not { } over)
        {
            SummaryCover.Visibility = Visibility.Collapsed;
            return;
        }
        SummaryCover.Visibility = Visibility.Visible;
        SummaryTitle.Text = HatchlingText.OverTitle(over);
        SummaryTitle.SetResourceReference(TextBlock.ForegroundProperty, over.Won ? "Theme.Text.Good" : "Theme.Text.Bright");
        SummaryReason.Text = HatchlingText.OverReason(over);

        PayLines.Children.Clear();
        foreach (var line in over.Lines.Take(MostPayLines))
            PayLines.Children.Add(SummaryLine(HatchlingText.PayLineText(line), HatchlingText.Plus(line.Amount)));
        PayTotal.Text = HatchlingText.Plus(over.Total);

        EarnedLines.Children.Clear();
        foreach (var id in over.Earned.Take(MostEarnedLines))
        {
            var definition = state.Achievements.All.FirstOrDefault(a => a.Id == id);
            if (definition is null) continue;
            var reward = definition.Reward is AchievementReward.Currency pay ? HatchlingText.Plus(pay.Amount) : "";
            EarnedLines.Children.Add(SummaryLine(HatchlingText.PayAchievement(definition.Title), reward));
        }
        if (over.Earned.Count > MostEarnedLines)
            EarnedLines.Children.Add(SummaryLine(HatchlingText.PayMore(over.Earned.Count - MostEarnedLines), ""));

        DangerLine.Text = over.DangerUnlocked > 0 ? HatchlingText.DangerUnlocked(over.DangerUnlocked) : "";
        DangerLine.Visibility = over.DangerUnlocked > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Grid SummaryLine(string left, string right)
    {
        var grid = new Grid { Height = SummaryLineHeight };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var words = new TextBlock { Text = left, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        words.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Text.Text");
        words.SetResourceReference(TextBlock.FontSizeProperty, "Theme.Size.12");
        var amount = new TextBlock { Text = right, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        amount.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Text.Highlight");
        amount.SetResourceReference(TextBlock.FontSizeProperty, "Theme.Size.12");
        Grid.SetColumn(amount, 1);
        grid.Children.Add(words);
        grid.Children.Add(amount);
        return grid;
    }

    /// <summary>The summary as shown: open or not, its title, its lines (pay, then achievements).</summary>
    internal (bool Open, string Title, IReadOnlyList<string> Lines, string Total) SummaryShown =>
        (SummaryCover.Visibility == Visibility.Visible, SummaryTitle.Text,
            PayLines.Children.OfType<Grid>().Concat(EarnedLines.Children.OfType<Grid>())
                .Select(g => ((TextBlock)g.Children[0]).Text).ToList(), PayTotal.Text);

    // ---- the context line (H23) ------------------------------------------------------

    /// <summary>
    /// The context line, by the first rule that applies: during a playback, that a
    /// click skips; while no move is awaited (an offer open, the run over), nothing
    /// (H78: no hint and no cell's words over a board that takes no click); the mouse
    /// over a cell, what is there (<see cref="Describe.Cell"/>); fewer than three runs
    /// finished, a hint (<see cref="Hints.For"/>); otherwise nothing.
    /// </summary>
    private string ContextLine(GameState state)
    {
        if (_play is not null) return HatchlingText.Skip;
        if (state.Run is not { } run || !AwaitsMove(run)) return "";
        var stats = Stats.Of(run);
        if (_hover >= 0) return Describe.Cell(run, _hover, Facts(run, stats, _hover));
        if (state.Totals.Runs >= Tuning.HintRuns) return "";
        var percent = (int)Tuning.Round(run.You.Health * 100);
        return Hints.Text(Hints.For(run, stats.Hides, stats.Eats.HasFlag(Diet.Plants), percent, SightKnows));
    }

    /// <summary>
    /// What the line for <paramref name="cell"/> needs that the run alone does not say,
    /// from the very answers the board and the moves give (H23, H39): a move there at
    /// the run's pace and its noise, the move's own (the wading's +1 added to the
    /// pace's noise BEFORE the build's floor at 0, as the rules add it, so a quiet dino
    /// sneaking into water is told the 0 its move makes);
    /// the creature only if the player may know it; whether a click attacks it; the
    /// build's facts; a meal's worth of each food; the food the board shows.
    /// </summary>
    internal static CellFacts Facts(RunState run, Stats stats, int cell)
    {
        var paceNoise = run.Pace switch { Pace.Sneak => Tuning.SneakNoise, Pace.Sprint => Tuning.SprintNoise, _ => Tuning.WalkNoise };
        var wades = Sight.WouldWade(run, stats, cell);
        // A sprint never wades (the rules': it enters water only as a swimmer or a flyer).
        var noise = Moves.NoiseOf(stats, paceNoise + (run.Pace != Pace.Sprint && wades ? Tuning.WadeNoise : 0));
        var creature = Island.CreatureAt(run, cell) is { } c && Sight.Knows(run, stats, c) ? c : null;
        var canAttack = creature is not null && Moves.Attackable(run, stats).Any(a => a.Id == creature.Id);
        return new CellFacts(Moves.Targets(run, stats).Contains(cell), noise, creature, canAttack,
            stats.Eats, stats.Hides, stats.Flies, wades,
            Moves.MealGain(stats, Food.Plants), Moves.MealGain(stats, Food.Meat), Moves.MealGain(stats, Food.Fish),
            Sight.ShowsFood(run, stats, cell, Food.Plants), Sight.ShowsFood(run, stats, cell, Food.Meat));
    }
}

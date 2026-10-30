using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PandoraOverlay;

/// <summary>
/// SettingsWindow, Rules part: the Server rules page — a reference, not
/// preferences (nothing here is saved). Renders the bundled RulesDocument:
/// a pack-limits card with one column per diet category and your current
/// species highlighted, then the numbered rules as striped rows. Reached
/// from the nav and from the tray's "Server rules…" entry, which opens the
/// dialog straight on this page. Same class as SettingsWindow.xaml.cs,
/// split for reading; see CLAUDE.md.
/// </summary>
public partial class SettingsWindow
{
    private const string RulesPageUrl = "https://islapandora.eu/rules";
    private const string DiscordUrl = "https://discord.gg/islapandora";

    // Read from the theme as a page builds (Skins and Dino storage use them
    // too); the diet colours are the website's, never a theme's (SiteBrushes).
    private static Brush RuleNumber => Theme.Current.Text.Off.ToBrush();
    private static Brush RuleText => Theme.Current.Text.Text.ToBrush();
    private static Brush Highlight => Theme.Current.Text.Highlight.ToBrush(); // yours: your species here, gold like your arrow in Default

    private readonly string? _currentDino; // the live dino when the dialog opened, for the pack-limit highlight

    private RulesDocument? _rulesDocument;
    private bool _rulesBuilt;

    /// <summary>
    /// What the page needs when the dialog opens: the stamp and the note.
    /// The note wraps, so it is part of the page's HEIGHT — and the tallest
    /// page sets the dialog's — which is why it can't wait for the first
    /// look like the cards do.
    /// </summary>
    private void PrepareRulesPage()
    {
        _rulesDocument = ServerRules.LoadBundled();
        RulesStamp.Text = _rulesDocument is null ? "" : $"copied {_rulesDocument.CopiedOn}";
        RulesNote.Text = _rulesDocument?.Note ?? "The bundled rules could not be read — see the website.";
    }

    /// <summary>The cards, built the first time the page is looked at (they sit in a fixed-height list, so nothing shifts).</summary>
    private void OpenRulesPage()
    {
        if (_rulesBuilt || _rulesDocument is null) return;
        _rulesBuilt = true;
        RenderRules();
    }

    /// <summary>The two cards from the loaded copy; again after a theme switch (RedrawIfStale), from the same copy.</summary>
    private void RenderRules()
    {
        RulesContent.Children.Clear();
        Drawn("Rules");
        if (_rulesDocument is not { } doc) return;
        if (doc.PackLimits.Count > 0) RulesContent.Children.Add(BuildPackLimitsCard(doc));
        if (doc.Rules.Count > 0) RulesContent.Children.Add(BuildRulesCard(doc));
    }

    /// <summary>One card: a caption band, then a column per category with "Name ...... limit" rows; your species highlighted.</summary>
    private Border BuildPackLimitsCard(RulesDocument doc)
    {
        var body = new StackPanel();
        var limit = doc.LimitFor(_currentDino);
        body.Children.Add(BuildCaptionBand(limit is { } l
            ? $"PACK LIMITS · maximum group size per species · yours ({_currentDino}): {l}"
            : "PACK LIMITS · maximum group size per species"));

        var columns = new Grid { Margin = new Thickness(6, 2, 6, 4) };
        for (var i = 0; i < doc.PackLimits.Count; i++)
        {
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var category = doc.PackLimits[i];
            var column = new StackPanel { Margin = new Thickness(i == 0 ? 0 : 10, 0, 0, 0) };
            column.Children.Add(new TextBlock
            {
                Text = category.Category, FontSize = Theme.Current.Size(11), FontWeight = FontWeights.SemiBold,
                Foreground = CategoryBrush(category.Category), Margin = new Thickness(0, 0, 0, 4)
            });
            foreach (var s in category.Species)
            {
                var mine = string.Equals(s.Name, _currentDino, StringComparison.OrdinalIgnoreCase);
                var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                var value = new TextBlock
                {
                    Text = s.Limit.ToString(), FontSize = Theme.Current.Size(11), Foreground = mine ? Highlight : RuleText,
                    FontWeight = mine ? FontWeights.Bold : FontWeights.Normal
                };
                DockPanel.SetDock(value, Dock.Right);
                row.Children.Add(value);
                row.Children.Add(new TextBlock
                {
                    Text = s.Name, FontSize = Theme.Current.Size(11), Foreground = mine ? Highlight : RuleText,
                    FontWeight = mine ? FontWeights.Bold : FontWeights.Normal,
                    TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 6, 0)
                });
                column.Children.Add(row);
            }
            Grid.SetColumn(column, i);
            columns.Children.Add(column);
        }
        body.Children.Add(columns);
        return WrapCard(body);
    }

    /// <summary>One card: a caption band, then "01  rule text" rows, zebra-striped, the text wrapping.</summary>
    private Border BuildRulesCard(RulesDocument doc)
    {
        var body = new StackPanel();
        body.Children.Add(BuildCaptionBand("RULES"));
        for (var i = 0; i < doc.Rules.Count; i++)
        {
            var row = new DockPanel
            {
                Margin = new Thickness(0, 1, 0, 1),
                Background = i % 2 == 1 ? RowStripe : Brushes.Transparent
            };
            var number = new TextBlock
            {
                Text = (i + 1).ToString("00"), Width = 30, FontSize = Theme.Current.Size(11), Foreground = RuleNumber,
                FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 3, 0, 3), VerticalAlignment = VerticalAlignment.Top
            };
            DockPanel.SetDock(number, Dock.Left);
            row.Children.Add(number);
            row.Children.Add(new TextBlock
            {
                Text = doc.Rules[i], FontSize = Theme.Current.Size(11), Foreground = RuleText, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 8, 3)
            });
            body.Children.Add(row);
        }
        return WrapCard(body);
    }

    private Border BuildCaptionBand(string text) => new()
    {
        Child = new TextBlock
        {
            Text = text, Foreground = HintNeutral, FontSize = Theme.Current.Size(10), FontWeight = CaptionWeight,
            Margin = new Thickness(8, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis
        },
        Background = BandSurface, CornerRadius = BandCorner,
        Padding = new Thickness(0, 3, 0, 3), Margin = new Thickness(0, 0, 0, 4)
    };

    private static Border WrapCard(UIElement body) => new()
    {
        Child = body, CornerRadius = CardCorner, BorderBrush = CardBorder, BorderThickness = new Thickness(1),
        Background = CardSurface, Padding = new Thickness(6, 6, 6, 4), Margin = new Thickness(0, 0, 0, 8)
    };

    private static Brush CategoryBrush(string category) => category.ToLowerInvariant() switch
    {
        "herbivore" => SiteBrushes.Herbivore,
        "carnivore" => SiteBrushes.Carnivore,
        "omnivore" => SiteBrushes.Omnivore,
        _ => HintNeutral
    };

    private void OpenRulesPage_Click(object sender, RoutedEventArgs e) => OpenUrl(RulesPageUrl);

    private void OpenDiscord_Click(object sender, RoutedEventArgs e) => OpenUrl(DiscordUrl); // OpenUrl: SettingsWindow.Apply.cs
}

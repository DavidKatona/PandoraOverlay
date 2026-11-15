using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace PandoraOverlay.Games.Hatchling;

/// <summary>A line of a Guide section: a short lead at the left, its sentence beside it.</summary>
internal sealed record GuideEntry(string Lead, string Text);

/// <summary>
/// A Guide section's table: its header and its rows, every cell one short line. A
/// header whose first cell is empty is a table of labelled rows (the first column the
/// labels, the width of an entry's lead); otherwise every column is one width.
/// </summary>
internal sealed record GuideTable(IReadOnlyList<string> Header, IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>One section of the Guide (H94): its id (the layout state is "guide-" and the id), its name in the list and on its card, an intro, a table, then its entries.</summary>
internal sealed record GuideSection(string Id, string Name, string? Intro, GuideTable? Table, IReadOnlyList<GuideEntry> Entries);

/// <summary>
/// The Guide (H94, the owner's ask of Nov 15 2026: every game explains its goal, its
/// rules and its terms; he had not known Walk from Sneak). A fifth tab, reachable from
/// every page, in Camp and during a run: like the Lineage, the Journal and Records it
/// is a page between the run's turns and changes nothing in a run (a playback is
/// skipped by the tab's press, as every tab's, H5).
///
/// <para><b>No scrolling</b> (H27): the sections are a list at the left, one
/// NavButton each, and a click shows that one section on the card at the right. Each
/// section is short enough to fit its card whole in every theme at its fit factor, no
/// sentence cut by an ellipsis (<c>HatchlingInTheWindowTests</c> measures every one).
/// Fewer words were chosen over cut words.</para>
///
/// <para><b>Its words are the rules' own numbers</b>: every sentence is
/// <see cref="HatchlingText.Guide"/>'s, built from <see cref="Tuning"/> and the
/// tables, so a retune changes the Guide with the rules (<c>GuideTests</c>). Nothing
/// here reads the game's state: the page is the same for every save, and the section
/// shown is the view's to remember while it lives, never saved.</para>
/// </summary>
internal sealed partial class GuidePage : Grid, IHatchlingPage
{
    /// <summary>An entry's lead and a labelled table's first column: 120 wide.</summary>
    internal const double LeadWidth = 120;

    /// <summary>A table's row: 20 high.</summary>
    internal const double TableRowHeight = 20;

    /// <summary>The air under an entry, the intro and the table.</summary>
    private const double EntryGap = 7;

    private readonly HatchlingView _view;
    private readonly ToggleButton[] _buttons;
    private int _shown;

    public GuidePage(HatchlingView view)
    {
        _view = view;
        InitializeComponent();
        _buttons = HatchlingText.Guide.Select(MakeButton).ToArray();
    }

    /// <inheritdoc />
    public FrameworkElement Root => this;

    /// <inheritdoc />
    public void Show() => Draw();

    /// <inheritdoc />
    public void Refresh() => Draw();

    /// <inheritdoc />
    public void Hide() { }

    /// <summary>The section shown when the page is next drawn, by its id; an id that is not one throws.</summary>
    internal void Choose(string id)
    {
        var index = HatchlingText.Guide.Select(s => s.Id).ToList().IndexOf(id);
        if (index < 0) throw new ArgumentException($"The Guide has no section '{id}'.", nameof(id));
        _shown = index;
    }

    /// <summary>A section's button: shows that section (a click on the one shown keeps it).</summary>
    internal void SectionClicked(int index) => _view.Guarded(() =>
    {
        _shown = Math.Clamp(index, 0, _buttons.Length - 1);
        Draw();
    });

    // ===== Drawing ===========================================================================

    /// <summary>The list as the section shown has it: exactly one checked (a toggle the guard refused is put back).</summary>
    private void SyncButtons()
    {
        for (var i = 0; i < _buttons.Length; i++) _buttons[i].IsChecked = i == _shown;
    }

    private void Draw()
    {
        var section = HatchlingText.Guide[_shown];
        SyncButtons();
        SectionTitle.Text = section.Name;
        SectionContent.Children.Clear();
        if (section.Intro is { } intro)
        {
            var block = Wrapping("Theme.Text.Text2");
            block.Text = intro;
            block.Margin = new Thickness(0, 0, 0, EntryGap);
            SectionContent.Children.Add(block);
        }
        if (section.Table is { } table) SectionContent.Children.Add(Table(table));
        SectionContent.Children.Add(Entries(section.Entries));
    }

    /// <summary>The entries: a lead (Bright, value weight) in its column of 120, the sentence (Text) wrapping beside it.</summary>
    private static Grid Entries(IReadOnlyList<GuideEntry> entries)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LeadWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < entries.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var gap = new Thickness(0, 0, 0, i == entries.Count - 1 ? 0 : EntryGap);
            var lead = PageParts.Line("Theme.Text.Bright", "Theme.Size.12", entries[i].Lead);
            lead.SetResourceReference(TextBlock.FontWeightProperty, "Theme.Font.ValueWeight");
            lead.VerticalAlignment = VerticalAlignment.Top;
            lead.Margin = new Thickness(0, 0, 8, 0);
            var text = Wrapping("Theme.Text.Text");
            text.Text = entries[i].Text;
            text.Margin = gap;
            Grid.SetColumn(text, 1);
            PageParts.In(grid, i, lead);
            PageParts.In(grid, i, text);
        }
        return grid;
    }

    /// <summary>A table: its header (Text2, 11) over its rows (Bright, 12), each 20 high; a labelled table's labels in Text2.</summary>
    private static Grid Table(GuideTable table)
    {
        var labelled = table.Header.Count > 0 && table.Header[0].Length == 0;
        var grid = new Grid { Margin = new Thickness(0, 0, 0, EntryGap) };
        for (var c = 0; c < table.Header.Count; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = labelled && c == 0 ? new GridLength(LeadWidth) : new GridLength(1, GridUnitType.Star),
            });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TableRowHeight) });
        for (var c = 0; c < table.Header.Count; c++)
            Cell(grid, 0, c, PageParts.Line("Theme.Text.Text2", "Theme.Size.11", table.Header[c]));
        for (var r = 0; r < table.Rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TableRowHeight) });
            var row = table.Rows[r];
            for (var c = 0; c < row.Count; c++)
            {
                var cell = labelled && c == 0
                    ? PageParts.Line("Theme.Text.Text2", "Theme.Size.12", row[c])
                    : PageParts.Line("Theme.Text.Bright", "Theme.Size.12", row[c]);
                Cell(grid, r + 1, c, cell);
            }
        }
        return grid;
    }

    private static void Cell(Grid grid, int row, int column, TextBlock cell)
    {
        cell.Margin = new Thickness(0, 0, 6, 0);
        Grid.SetColumn(cell, column);
        PageParts.In(grid, row, cell);
    }

    /// <summary>A sentence that wraps freely (size 12): never held to a height, so it is never cut; the card's size is what holds it (a test).</summary>
    private static TextBlock Wrapping(string foregroundKey)
    {
        var block = PageParts.Line(foregroundKey, "Theme.Size.12");
        block.TextWrapping = TextWrapping.Wrap;
        block.TextTrimming = TextTrimming.None;
        block.VerticalAlignment = VerticalAlignment.Top;
        return block;
    }

    // ===== Made once ============================================================================

    /// <summary>A section's button in the list: a NavButton, stretched, its words trimmed (a one-line text, A25), the keyboard never its.</summary>
    private ToggleButton MakeButton(GuideSection section, int index)
    {
        var words = new TextBlock { Text = section.Name, TextTrimming = TextTrimming.CharacterEllipsis };
        var button = new ToggleButton { Content = words, Focusable = false };
        button.SetResourceReference(FrameworkElement.StyleProperty, "NavButton");
        button.Click += (_, _) =>
        {
            SectionClicked(index);
            _view.Settled(SyncButtons);
        };
        SectionList.Children.Add(button);
        return button;
    }

    // ===== What a test reads ===================================================================

    /// <summary>The list's buttons' words, in order, and which is checked.</summary>
    internal (IReadOnlyList<string> Sections, int Checked) ListShown =>
        (_buttons.Select(b => ((TextBlock)b.Content).Text).ToList(), Array.FindIndex(_buttons, b => b.IsChecked == true));

    /// <summary>The section shown: its title, and every text on its card in order (the table's cells row by row, then each entry's lead and sentence).</summary>
    internal (string Title, IReadOnlyList<string> Texts) SectionShown =>
        (SectionTitle.Text, Texts(SectionContent).ToList());

    /// <summary>The list's buttons, for a test that clicks one.</summary>
    internal IReadOnlyList<ToggleButton> Buttons => _buttons;

    private static IEnumerable<string> Texts(Panel panel)
    {
        foreach (UIElement child in panel.Children)
        {
            if (child is TextBlock text) yield return text.Text;
            else if (child is Grid grid)
                foreach (var t in grid.Children.OfType<TextBlock>()
                             .OrderBy(Grid.GetRow).ThenBy(Grid.GetColumn))
                    yield return t.Text;
        }
    }
}

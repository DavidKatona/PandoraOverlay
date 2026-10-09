using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace PandoraOverlay;

/// <summary>
/// SettingsWindow, Waypoints part: the library page — the draft list, its
/// grouped rows, pack import/export and Delete all. Same class as
/// SettingsWindow.xaml.cs, split for reading; see CLAUDE.md.
/// </summary>
public partial class SettingsWindow
{
    // ---- Waypoints page -----------------------------------------------------
    private readonly WaypointLibrary _library;
    private readonly List<Waypoint> _draft;   // edited in place; committed on Save, dropped on Cancel
    private Guid? _draftTracked;
    private bool _waypointsDirty;
    private bool _deleteAllArmed;
    // The Dino storage page's "Waypoint here" also writes the draft and its
    // tracking (it acts at once, SettingsWindow.StorageDetail.cs) and clears
    // this, so the rows are rebuilt from the draft on the page's next look.
    private bool _waypointRowsBuilt;

    /// <summary>
    /// Builds the list the first time the page is looked at, not when the
    /// dialog opens: with 122 waypoints (the five packs) the rows more than
    /// doubled the time every Settings opening took (measured Oct 2026:
    /// ~0.25 s empty, ~0.6 s with the packs, ~0.9 s with a full library),
    /// whichever page was shown — hidden pages are still laid out. Saving
    /// never needed the rows: it works on the draft.
    /// </summary>
    private void OpenWaypointsPage()
    {
        if (_waypointRowsBuilt) return;
        _waypointRowsBuilt = true;
        BuildWaypointRows();
    }

    private static readonly Brush[] PaletteBrushes = WaypointPalette.Colours
        .Select(c => { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(c.Hex)); b.Freeze(); return (Brush)b; })
        .ToArray();

    /// <summary>
    /// The list, grouped: your own waypoints first, then one group per
    /// imported pack with its own band checkbox (show / hide all) and ✕. Each
    /// row: colour dot (click cycles the palette), name box, Show, Track,
    /// delete.
    /// </summary>
    private void BuildWaypointRows()
    {
        WaypointRows.Children.Clear();
        _rowShowChecks.Clear();
        UpdateWaypointTotals();

        if (_draft.Count == 0)
        {
            WaypointRows.Children.Add(new TextBlock
            {
                Text = "No waypoints yet.",
                Foreground = HintNeutral, FontSize = 12, Margin = new Thickness(0, 6, 0, 0)
            });
            return;
        }

        // The cards and their bands at once; the rows in batches — the first
        // screenful now, the rest a batch per idle moment. A row is five
        // templated controls, and a full rebuild (it happens on every Show
        // all, delete or import too) held the dialog for a third of a second
        // with the five packs in the library.
        // (Since then Show all and the deletes change the list IN PLACE —
        // SetGroupVisible, DeleteWaypointRow, DeletePack; only Import, Delete
        // all and the last delete still come here, as they change its shape.)
        var version = ++_waypointRenderVersion;
        var rows = new Queue<Action>();
        AddGroupCard("Your waypoints", pack: null, rows);
        foreach (var pack in _draft.Where(w => w.Pack is not null).Select(w => w.Pack!).Distinct())
        {
            AddGroupCard(pack, pack, rows);
        }
        AddWaypointRowBatch(rows, FirstRowBatch, version, rows.Count);
    }

    private const int FirstRowBatch = 12; // more than the list shows without scrolling
    private const int RowBatch = 24;
    private int _waypointRenderVersion;   // a newer rebuild stops an older one's batches

    /// <summary>
    /// How long the batches after the first screenful may take before a
    /// list's count line says "building… 48 of 122". Normally they are done
    /// well inside it and nothing is shown — a notice that lived a fifth of
    /// a second would only flicker; a slow PC or a full library gets the
    /// words instead of a list that silently keeps growing. Counted from the
    /// first idle batch, not from the build's start, so the dialog's own
    /// opening doesn't use the time up. Words in the count line on purpose:
    /// a cover over the list was considered and dropped (owner, Oct 2 2026)
    /// — the first screenful is ready at once, and a cover would hide it.
    /// Measured: the later batches normally take ~0.1–0.2 s. The skin tiles
    /// share this notice, where a cover would also flash on every search.
    /// </summary>
    private static readonly TimeSpan BuildNoticeAfter = TimeSpan.FromMilliseconds(300);

    private static bool BuildIsSlow(long waitingSince) =>
        waitingSince != 0 && Stopwatch.GetElapsedTime(waitingSince) > BuildNoticeAfter;

    private string WaypointCountText() => $"{_draft.Count} / {WaypointLibrary.Capacity}";

    /// <summary>The Show box of every row built so far, so a pack's Show all can tick them where they stand.</summary>
    private readonly Dictionary<Guid, CheckBox> _rowShowChecks = new();

    /// <summary>The count and the three buttons under the list, after anything that changes how many waypoints there are.</summary>
    private void UpdateWaypointTotals()
    {
        WaypointCount.Text = WaypointCountText();
        DeleteAllButton.Content = "Delete all";
        DeleteAllButton.IsEnabled = _draft.Count > 0;
        ExportButton.IsEnabled = _draft.Count > 0;
        ImportButton.IsEnabled = _draft.Count < WaypointLibrary.Capacity;
        _deleteAllArmed = false;
    }

    private void AddWaypointRowBatch(Queue<Action> rows, int count, int version, int total, long waitingSince = 0)
    {
        if (version != _waypointRenderVersion) return;
        for (var i = 0; i < count && rows.Count > 0; i++) rows.Dequeue()();
        if (rows.Count == 0)
        {
            WaypointCount.Text = WaypointCountText();
            return;
        }
        if (BuildIsSlow(waitingSince)) WaypointCount.Text = $"building… {total - rows.Count} of {total}";
        Dispatcher.BeginInvoke(DispatcherPriority.Background,
            () => AddWaypointRowBatch(rows, RowBatch, version, total, waitingSince != 0 ? waitingSince : Stopwatch.GetTimestamp()));
    }

    private static readonly Brush CardBorder = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
    private static readonly Brush CardSurface = new SolidColorBrush(Color.FromRgb(0x16, 0x1C, 0x23));
    private static readonly Brush BandSurface = new SolidColorBrush(Color.FromRgb(0x1F, 0x26, 0x2E));
    private static readonly Brush RowStripe = new SolidColorBrush(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF));
    private static readonly Brush WellSurface = new SolidColorBrush(Color.FromRgb(0x1A, 0x21, 0x29));   // matches the Check template's box
    private static readonly Brush WellBorder = new SolidColorBrush(Color.FromRgb(0x5E, 0x6B, 0x76));
    private static readonly Brush RowDeleteGlyph = new SolidColorBrush(Color.FromRgb(0xFF, 0xB4, 0xAE));

    /// <summary>
    /// One group as a card: a bordered surface holding a caption band and
    /// zebra-striped rows, so every checkbox, radio and ✕ sits on a visible
    /// strip that runs from its name, and the group's own controls have a
    /// home. Stock white glyphs on bare page were tried first and floated.
    /// So did loose Show all / Hide all / Delete pack buttons, then bare
    /// caption rows (owner, Sep 26 2026: the controls "floated").
    /// </summary>
    private void AddGroupCard(string title, string? pack, Queue<Action> rows)
    {
        var members = _draft.Where(w => w.Pack == pack).ToList();
        if (members.Count == 0) return;

        var body = new StackPanel();
        var card = new Border
        {
            Child = body, CornerRadius = new CornerRadius(4), BorderBrush = CardBorder, BorderThickness = new Thickness(1),
            Background = CardSurface, Padding = new Thickness(6, 6, 6, 4), Margin = new Thickness(0, 0, 0, 8)
        };
        var (band, groupCheck) = BuildGroupBand(title, pack, members, card);
        body.Children.Add(band);
        foreach (var wp in members)
        {
            rows.Enqueue(() =>
            {
                if (!_draft.Contains(wp)) return; // deleted (alone or with its pack) while its row was still waiting
                body.Children.Add(BuildWaypointRow(wp, new GroupCard(card, body, members, groupCheck)));
            });
        }
        WaypointRows.Children.Add(card);
    }

    /// <summary>What a row needs of the group it sits in, to change things IN PLACE: its card, the rows' panel, the members and the band's checkbox.</summary>
    private sealed record GroupCard(Border Card, StackPanel Body, List<Waypoint> Members, CheckBox Check);

    /// <summary>Zebra stripes by position (the band is child 0), so they stay right after a row is removed.</summary>
    private static void Restripe(StackPanel body)
    {
        for (var i = 1; i < body.Children.Count; i++)
        {
            if (body.Children[i] is Grid row) row.Background = i % 2 == 0 ? RowStripe : Brushes.Transparent;
        }
    }

    /// <summary>
    /// Keeps the column labels exactly as wide as the list's content: the
    /// header sits outside the scroll area, so it can't share the layout,
    /// and a guessed scrollbar width was wrong both with and without a bar.
    /// Measured instead: the bar's space is the difference between the
    /// viewer's width and its viewport; 6 px is the rows' right margin, 7 px
    /// each side is the cards' border + padding.
    /// </summary>
    private void AlignWaypointHeader() => AlignHeader(WaypointHeader, WaypointList, top: 0);

    /// <summary>The same measured alignment for any column header sitting over one of the list viewers (the Friends page shares it).</summary>
    private static void AlignHeader(Grid header, ScrollViewer list, double top)
    {
        var bar = Math.Max(0, list.ActualWidth - list.ViewportWidth);
        header.Margin = new Thickness(7, top, 7 + 6 + bar, 4);
    }

    /// <summary>True = every member shown, false = none, null = mixed (the checkbox draws a square).</summary>
    private static bool? GroupState(List<Waypoint> members)
    {
        var shown = members.Count(w => w.Visible);
        return shown == members.Count ? true : shown == 0 ? false : null;
    }

    /// <summary>
    /// The card's caption band, laid out on the same grid as the rows so its
    /// controls sit in the columns they govern: a checkbox in the Show
    /// column (ticked = all shown, empty = none, a square = mixed; a click
    /// shows all, the next hides all) and, for a pack, a ✕ in the delete
    /// column. Three loose buttons were tried first and looked bolted on.
    /// </summary>
    private (Border Band, CheckBox GroupCheck) BuildGroupBand(string text, string? pack, List<Waypoint> members, Border card)
    {
        var row = NewRowGrid();
        row.Margin = new Thickness(0);

        var caption = new TextBlock
        {
            Text = text.ToUpperInvariant(), Foreground = HintNeutral, FontSize = 10,
            FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(caption, 1);
        row.Children.Add(caption);

        var all = new CheckBox
        {
            Style = (Style)FindResource("Check"), HorizontalAlignment = HorizontalAlignment.Center,
            IsChecked = GroupState(members),
            ToolTip = pack is null ? "Show or hide all of your waypoints" : "Show or hide the whole pack"
        };
        all.Click += (_, _) => SetGroupVisible(pack, all.IsChecked == true); // mixed → click → checked → show all
        Grid.SetColumn(all, 2);
        row.Children.Add(all);

        if (pack is not null)
        {
            var delete = new Button
            {
                Content = "✕", Width = 22, Height = 22, Padding = new Thickness(0), FontSize = 10,
                Style = (Style)FindResource("DangerButtonStyle"),
                HorizontalAlignment = HorizontalAlignment.Center, ToolTip = "Delete this pack"
            };
            delete.Click += (_, _) => DeletePack(pack, card);
            Grid.SetColumn(delete, 4);
            row.Children.Add(delete);
        }
        var band = new Border
        {
            Child = row, Background = BandSurface, CornerRadius = new CornerRadius(3),
            Padding = new Thickness(0, 3, 0, 3), Margin = new Thickness(0, 0, 0, 4)
        };
        return (band, all);
    }

    /// <summary>The five-column grid every list row and caption shares: dot | name | Show | Track | delete.</summary>
    private static Grid NewRowGrid(double topMargin = 0)
    {
        var row = new Grid { Margin = new Thickness(0, topMargin, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        return row;
    }

    /// <summary>
    /// A pack's Show all / Hide all, IN PLACE: the draft changes and the rows
    /// already on screen get their boxes ticked where they stand (rows still
    /// waiting to be built read the draft when their turn comes). The list
    /// used to be rebuilt for this, which threw you back to the top and
    /// re-drew every row in front of you — the owner lost their place in a
    /// long list each time (Oct 2026). Nothing here may rebuild the list.
    /// </summary>
    private void SetGroupVisible(string? pack, bool visible)
    {
        foreach (var w in _draft.Where(w => w.Pack == pack))
        {
            w.Visible = visible;
            if (_rowShowChecks.TryGetValue(w.Id, out var box)) box.IsChecked = visible;
        }
        _waypointsDirty = true;
    }

    /// <summary>Removes a whole pack — no confirmation: it is a draft, Cancel still reverts it. Only its card leaves the list; the rest stays where it is.</summary>
    private void DeletePack(string pack, Border card)
    {
        if (_draft.Any(w => w.Pack == pack && w.Id == _draftTracked)) _draftTracked = null;
        foreach (var w in _draft.Where(w => w.Pack == pack)) _rowShowChecks.Remove(w.Id);
        _draft.RemoveAll(w => w.Pack == pack);
        _waypointsDirty = true;
        if (_draft.Count == 0)
        {
            BuildWaypointRows(); // nothing left: back to "No waypoints yet"
            return;
        }
        WaypointRows.Children.Remove(card);
        UpdateWaypointTotals();
    }

    /// <summary>One waypoint's ✕, IN PLACE: its row leaves, the rows below close up and are re-striped, an emptied card goes too.</summary>
    private void DeleteWaypointRow(Waypoint wp, Grid row, GroupCard group)
    {
        _draft.Remove(wp);
        group.Members.Remove(wp);
        _rowShowChecks.Remove(wp.Id);
        if (_draftTracked == wp.Id) _draftTracked = null;
        _waypointsDirty = true;
        if (_draft.Count == 0)
        {
            BuildWaypointRows();
            return;
        }

        group.Body.Children.Remove(row);
        if (group.Members.Count == 0)
        {
            WaypointRows.Children.Remove(group.Card);
        }
        else
        {
            Restripe(group.Body);
            group.Check.IsChecked = GroupState(group.Members);
        }
        UpdateWaypointTotals();
    }

    /// <summary>One list row; alternate rows carry a faint stripe so the controls read as part of the row.</summary>
    private Grid BuildWaypointRow(Waypoint wp, GroupCard group)
    {
        var row = NewRowGrid();
        row.Margin = new Thickness(0, 1, 0, 1);
        row.Background = group.Body.Children.Count % 2 == 0 ? RowStripe : Brushes.Transparent; // by position: the band is child 0

        // The colour disc sits in the same 15 px dark well as the checkboxes,
        // so it reads as a control in the table rather than a loose dot.
        var dot = new Ellipse { Width = 9, Height = 9, Fill = PaletteBrushes[WaypointPalette.Wrap(wp.Colour)] };
        var well = new Border
        {
            Child = dot, Width = 15, Height = 15, CornerRadius = new CornerRadius(2),
            Background = WellSurface, BorderBrush = WellBorder, BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
            ToolTip = $"{WaypointPalette.Colours[WaypointPalette.Wrap(wp.Colour)].Name} — click to change"
        };
        well.MouseLeftButtonDown += (_, _) =>
        {
            wp.Colour = WaypointPalette.Wrap(wp.Colour + 1);
            dot.Fill = PaletteBrushes[wp.Colour];
            well.ToolTip = $"{WaypointPalette.Colours[wp.Colour].Name} — click to change";
            _waypointsDirty = true;
        };
        row.Children.Add(well);

        var name = new TextBox { Text = wp.Name, Style = (Style)FindResource("NameBox"), Margin = new Thickness(8, 0, 8, 0) };
        name.TextChanged += (_, _) =>
        {
            wp.Name = name.Text;
            _waypointsDirty = true;
        };
        Grid.SetColumn(name, 1);
        row.Children.Add(name);

        var show = new CheckBox { IsChecked = wp.Visible, Style = (Style)FindResource("Check"), HorizontalAlignment = HorizontalAlignment.Center };
        show.Click += (_, _) =>
        {
            wp.Visible = show.IsChecked == true;
            _waypointsDirty = true;
            group.Check.IsChecked = GroupState(group.Members); // the band's box shows all / none / mixed live
        };
        _rowShowChecks[wp.Id] = show;
        Grid.SetColumn(show, 2);
        row.Children.Add(show);

        var track = new RadioButton
        {
            GroupName = "TrackWaypoint", IsChecked = wp.Id == _draftTracked,
            Style = (Style)FindResource("Radio"), HorizontalAlignment = HorizontalAlignment.Center
        };
        track.Click += (_, _) =>
        {
            // A radio can't be un-clicked, so clicking the tracked one untracks.
            if (_draftTracked == wp.Id)
            {
                _draftTracked = null;
                track.IsChecked = false;
            }
            else
            {
                _draftTracked = wp.Id;
            }
            _waypointsDirty = true;
        };
        Grid.SetColumn(track, 3);
        row.Children.Add(track);

        var delete = new Button
        {
            Content = "✕", Width = 22, Height = 22, Padding = new Thickness(0), FontSize = 10,
            // Grey button, soft red glyph: still "remove", but not the only alarm on every row.
            // The band's pack delete and Delete all stay red — they remove many at once.
            Style = (Style)FindResource("NeutralButtonStyle"), Foreground = RowDeleteGlyph,
            HorizontalAlignment = HorizontalAlignment.Center, ToolTip = "Delete this waypoint"
        };
        delete.Click += (_, _) => DeleteWaypointRow(wp, row, group);
        Grid.SetColumn(delete, 4);
        row.Children.Add(delete);

        return row;
    }

    // ---- Packs: import / export ------------------------------------------------
    private const string PackFilter = "Waypoint packs (*.json)|*.json|All files (*.*)|*.*";

    /// <summary>Merges a pack file into the draft; the outcome goes to the status line. Nothing persists until Save.</summary>
    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Import waypoints", Filter = PackFilter };
        if (dialog.ShowDialog(this) != true) return;

        string json;
        try
        {
            json = File.ReadAllText(dialog.FileName);
        }
        catch
        {
            SetWaypointStatus("Couldn't read that file.", HintWarn);
            return;
        }

        var pack = WaypointPacks.Parse(json, System.IO.Path.GetFileNameWithoutExtension(dialog.FileName));
        if (pack is null)
        {
            SetWaypointStatus("That file isn't a waypoint pack.", HintWarn);
            return;
        }

        var result = WaypointPacks.Merge(_draft, pack);
        if (result.Added > 0) _waypointsDirty = true;
        BuildWaypointRows();

        var text = $"Imported {result.Added} from \"{pack.Name}\"";
        if (result.Duplicates > 0) text += $" · {result.Duplicates} already here";
        if (result.Overflow > 0) text += $" · {result.Overflow} didn't fit ({WaypointLibrary.Capacity} max)";
        if (result.Added > 0) text += ". They start hidden: Show all on the pack, or tick Show per waypoint. Save to keep them.";
        SetWaypointStatus(text, result.Added > 0 ? HintGood : HintNeutral);
    }

    /// <summary>Writes the draft as a pack named after the chosen file.</summary>
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export waypoints", Filter = PackFilter, FileName = "pandora-waypoints.json",
            DefaultExt = ".json", AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;

        var name = WaypointLibrary.SanitizeName(System.IO.Path.GetFileNameWithoutExtension(dialog.FileName));
        try
        {
            File.WriteAllText(dialog.FileName, WaypointPacks.Export(_draft, name));
            SetWaypointStatus($"Exported {_draft.Count} waypoints as \"{name}\". Send the file to a friend; they import it from this page.", HintGood);
        }
        catch
        {
            SetWaypointStatus("Couldn't write that file.", HintWarn);
        }
    }

    private void SetWaypointStatus(string text, Brush brush)
    {
        WaypointStatus.Text = text;
        WaypointStatus.Foreground = brush;
        WaypointStatus.Visibility = Visibility.Visible;
    }

    /// <summary>First click arms ("Really delete all?"), second click deletes — no modal box on a game overlay.</summary>
    private void DeleteAll_Click(object sender, RoutedEventArgs e)
    {
        if (!_deleteAllArmed)
        {
            _deleteAllArmed = true;
            DeleteAllButton.Content = "Really delete all?";
            return;
        }
        _draft.Clear();
        _draftTracked = null;
        _waypointsDirty = true;
        BuildWaypointRows();
    }
}

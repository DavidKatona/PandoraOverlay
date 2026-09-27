using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

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

    private static readonly Brush[] PaletteBrushes = WaypointPalette.Colours
        .Select(c => { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(c.Hex)); b.Freeze(); return (Brush)b; })
        .ToArray();

    /// <summary>
    /// The list, grouped: your own waypoints first, then one group per
    /// imported pack with its own Show all / Hide all / Delete pack. Each
    /// row: colour dot (click cycles the palette), name box, Show, Track,
    /// delete.
    /// </summary>
    private void BuildWaypointRows()
    {
        WaypointRows.Children.Clear();
        WaypointCount.Text = $"{_draft.Count} / {WaypointLibrary.Capacity}";
        DeleteAllButton.Content = "Delete all";
        DeleteAllButton.IsEnabled = _draft.Count > 0;
        ExportButton.IsEnabled = _draft.Count > 0;
        ImportButton.IsEnabled = _draft.Count < WaypointLibrary.Capacity;
        _deleteAllArmed = false;

        if (_draft.Count == 0)
        {
            WaypointRows.Children.Add(new TextBlock
            {
                Text = "No waypoints yet.",
                Foreground = HintNeutral, FontSize = 12, Margin = new Thickness(0, 6, 0, 0)
            });
            return;
        }

        AddGroupCard("Your waypoints", pack: null);
        foreach (var pack in _draft.Where(w => w.Pack is not null).Select(w => w.Pack!).Distinct())
        {
            AddGroupCard(pack, pack);
        }
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
    /// </summary>
    private void AddGroupCard(string title, string? pack)
    {
        var members = _draft.Where(w => w.Pack == pack).ToList();
        if (members.Count == 0) return;

        var body = new StackPanel();
        var (band, groupCheck) = BuildGroupBand(title, pack, members);
        body.Children.Add(band);
        var stripe = false;
        foreach (var wp in members)
        {
            // A row's Show click must reach the band: the group box shows all / none / mixed live.
            body.Children.Add(BuildWaypointRow(wp, stripe, () => groupCheck.IsChecked = GroupState(members)));
            stripe = !stripe;
        }
        WaypointRows.Children.Add(new Border
        {
            Child = body, CornerRadius = new CornerRadius(4), BorderBrush = CardBorder, BorderThickness = new Thickness(1),
            Background = CardSurface, Padding = new Thickness(6, 6, 6, 4), Margin = new Thickness(0, 0, 0, 8)
        });
    }

    /// <summary>
    /// The card's caption band, laid out on the same grid as the rows so its
    /// controls sit in the columns they govern: a checkbox in the Show
    /// column (ticked = all shown, empty = none, a square = mixed; a click
    /// shows all, the next hides all) and, for a pack, a ✕ in the delete
    /// column. Three loose buttons were tried first and looked bolted on.
    /// </summary>
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

    private (Border Band, CheckBox GroupCheck) BuildGroupBand(string text, string? pack, List<Waypoint> members)
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
            delete.Click += (_, _) => DeletePack(pack);
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

    private void SetGroupVisible(string? pack, bool visible)
    {
        foreach (var w in _draft.Where(w => w.Pack == pack)) w.Visible = visible;
        _waypointsDirty = true;
        BuildWaypointRows();
    }

    /// <summary>Removes a whole pack — no confirmation: it is a draft, Cancel still reverts it.</summary>
    private void DeletePack(string pack)
    {
        if (_draft.Any(w => w.Pack == pack && w.Id == _draftTracked)) _draftTracked = null;
        _draft.RemoveAll(w => w.Pack == pack);
        _waypointsDirty = true;
        BuildWaypointRows();
    }

    /// <summary>One list row; alternate rows carry a faint stripe so the controls read as part of the row.</summary>
    private Grid BuildWaypointRow(Waypoint wp, bool stripe, Action onVisibilityChanged)
    {
        var row = NewRowGrid();
        row.Margin = new Thickness(0, 1, 0, 1);
        row.Background = stripe ? RowStripe : Brushes.Transparent;

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
            onVisibilityChanged();
        };
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
        delete.Click += (_, _) =>
        {
            _draft.Remove(wp);
            if (_draftTracked == wp.Id) _draftTracked = null;
            _waypointsDirty = true;
            BuildWaypointRows();
        };
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

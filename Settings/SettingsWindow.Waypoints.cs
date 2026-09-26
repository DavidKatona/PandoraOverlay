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

        var own = _draft.Where(w => w.Pack is null).ToList();
        var packs = _draft.Where(w => w.Pack is not null).Select(w => w.Pack!).Distinct().ToList();
        if (packs.Count > 0 && own.Count > 0) AddGroupCaption("Your waypoints", pack: null);
        AddWaypointRows(own);
        foreach (var pack in packs)
        {
            AddGroupCaption(pack, pack);
            AddWaypointRows(_draft.Where(w => w.Pack == pack));
        }
    }

    /// <summary>
    /// A caption row that acts on its group, laid out on the same grid as
    /// the rows so its controls sit in the columns they govern: a checkbox
    /// in the Show column (ticked = all shown, empty = none, a square =
    /// mixed; a click shows all, the next hides all) and, for a pack, a ✕
    /// in the delete column. Three loose buttons were tried first and looked
    /// bolted on.
    /// </summary>
    private void AddGroupCaption(string text, string? pack)
    {
        var members = _draft.Where(w => w.Pack == pack).ToList();
        var row = NewRowGrid(topMargin: WaypointRows.Children.Count == 0 ? 0 : 8);

        var caption = new TextBlock
        {
            Text = text.ToUpperInvariant(), Foreground = HintNeutral, FontSize = 10,
            FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(caption, 1);
        row.Children.Add(caption);

        var shown = members.Count(w => w.Visible);
        var all = new CheckBox
        {
            Style = (Style)FindResource("Check"), HorizontalAlignment = HorizontalAlignment.Center,
            IsChecked = shown == members.Count ? true : shown == 0 ? false : null, // null draws the "mixed" square
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
                HorizontalAlignment = HorizontalAlignment.Right, ToolTip = "Delete this pack"
            };
            delete.Click += (_, _) => DeletePack(pack);
            Grid.SetColumn(delete, 4);
            row.Children.Add(delete);
        }
        WaypointRows.Children.Add(row);
    }

    /// <summary>The five-column grid every list row and caption shares: dot | name | Show | Track | delete.</summary>
    private static Grid NewRowGrid(double topMargin = 0)
    {
        var row = new Grid { Margin = new Thickness(0, topMargin, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
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

    private void AddWaypointRows(IEnumerable<Waypoint> items)
    {
        foreach (var wp in items)
        {
            var row = NewRowGrid();

            var dot = new Ellipse
            {
                Width = 12, Height = 12, Fill = PaletteBrushes[WaypointPalette.Wrap(wp.Colour)],
                Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
                ToolTip = $"{WaypointPalette.Colours[WaypointPalette.Wrap(wp.Colour)].Name} — click to change"
            };
            dot.MouseLeftButtonDown += (_, _) =>
            {
                wp.Colour = WaypointPalette.Wrap(wp.Colour + 1);
                dot.Fill = PaletteBrushes[wp.Colour];
                dot.ToolTip = $"{WaypointPalette.Colours[wp.Colour].Name} — click to change";
                _waypointsDirty = true;
            };
            row.Children.Add(dot);

            var name = new TextBox { Text = wp.Name, Style = (Style)FindResource("NameBox"), Margin = new Thickness(0, 0, 8, 0) };
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
                Style = (Style)FindResource("DangerButtonStyle"),
                HorizontalAlignment = HorizontalAlignment.Right, ToolTip = "Delete"
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

            WaypointRows.Children.Add(row);
        }
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

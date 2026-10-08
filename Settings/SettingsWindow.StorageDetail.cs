using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace PandoraOverlay;

/// <summary>
/// SettingsWindow, Dino storage part two (1.33): an OPEN card — the
/// vitals and mutations, where the dino was stored on our own map with the
/// area and the distance from you, "Waypoint here", and the two writes,
/// Rename (in place, Save / Cancel) and Delete (armed in place). The page,
/// the list and the closed cards are SettingsWindow.Storage.cs. Same class
/// as SettingsWindow.xaml.cs, split for reading; see CLAUDE.md.
/// </summary>
public partial class SettingsWindow
{
    /// <summary>
    /// The open part: a rule, then the vitals and mutations on the left;
    /// where it was stored, "Waypoint here", Rename… and Delete… on the
    /// right. The actions sit under the map, not under the mutations: the
    /// left column is the long one, and there an open card outgrew the list.
    /// </summary>
    private FrameworkElement BuildStorageDetail(StorageCard card)
    {
        var detail = new StackPanel();
        detail.Children.Add(new Border { Height = 1, Background = CardBorder, Margin = new Thickness(0, 8, 0, 8) });

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(StoredMapSize + 14) });
        var left = new Grid(); // the plain view, or the rename form in its place
        var view = BuildStorageView(card.Dino);
        left.Children.Add(view);
        columns.Children.Add(left);
        var right = BuildStoredAt(card.Dino);
        right.Children.Add(BuildStorageActions(card, left, view));
        Grid.SetColumn(right, 1);
        columns.Children.Add(right);
        detail.Children.Add(columns);

        card.Body.Children.Add(detail);
        return detail;
    }

    /// <summary>Rename… and Delete… side by side; an armed Delete swaps in "Can't be undone." over the real Delete and Keep.</summary>
    private StackPanel BuildStorageActions(StorageCard card, Grid left, StackPanel view)
    {
        var d = card.Dino;
        var block = new StackPanel { Width = StoredMapSize, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        var rename = SmallButton("Rename…", "NeutralButtonStyle");
        rename.ToolTip = "Change its name and description on the website";
        var delete = SmallButton("Delete…", "NeutralButtonStyle");
        delete.Foreground = RowDeleteGlyph;
        delete.ToolTip = "Remove it from your storage on the website";
        var plain = Pair(rename, delete);
        block.Children.Add(plain);

        var warning = new TextBlock
        {
            Text = "Can't be undone.", Foreground = HintWarn, FontSize = 11, TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4), Visibility = Visibility.Collapsed
        };
        var really = SmallButton("Delete", "DangerButtonStyle");
        really.ToolTip = $"Delete {d.DisplayName} from your storage on the website";
        var keep = SmallButton("Keep", "NeutralButtonStyle");
        var armed = Pair(really, keep);
        armed.Visibility = Visibility.Collapsed;
        block.Children.Add(warning);
        block.Children.Add(armed);

        void Disarm()
        {
            warning.Visibility = armed.Visibility = Visibility.Collapsed;
            plain.Visibility = Visibility.Visible;
            card.Disarm = null;
        }
        delete.Click += (_, _) =>
        {
            plain.Visibility = Visibility.Collapsed;
            warning.Visibility = armed.Visibility = Visibility.Visible;
            card.Disarm = Disarm;
            SetStorageStatus($"Delete {d.DisplayName} from your storage? It goes for good, on the website too.", HintWarn);
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => armed.BringIntoView());
        };
        keep.Click += (_, _) =>
        {
            Disarm();
            ShowStorageHint();
        };
        really.Click += async (_, _) => await DeleteStoredDinoAsync(card, really, keep);
        rename.Click += (_, _) => ShowRenameForm(card, left, view, block);
        return block;
    }

    /// <summary>Two buttons sharing a row equally, a small gap between.</summary>
    private static Grid Pair(Button first, Button second)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        first.Padding = second.Padding = new Thickness(0, 3, 0, 3);
        Grid.SetColumn(second, 2);
        row.Children.Add(first);
        row.Children.Add(second);
        return row;
    }

    /// <summary>The plain view of an open card's left column: the description, the vitals and the mutations.</summary>
    private static StackPanel BuildStorageView(StoredDino d)
    {
        var view = new StackPanel();
        if (d.Description is { } description)
        {
            view.Children.Add(new TextBlock
            {
                Text = description, Foreground = CaptionBrush, FontSize = 11, FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 7)
            });
        }

        view.Children.Add(Caption("VITALS"));
        var vitals = new[] { d.Health, d.Stamina, d.Hunger, d.Thirst, d.Blood };
        for (var i = 0; i < vitals.Length; i++) view.Children.Add(VitalRow(VitalLooks[i].Label, vitals[i], VitalLooks[i].Fill));

        view.Children.Add(Caption("MUTATIONS", top: 8));
        view.Children.Add(MutationRow("Regular", d.Mutations));
        view.Children.Add(MutationRow("Parent", d.ParentMutations));
        view.Children.Add(MutationRow("Elder", d.ElderMutations));
        return view;
    }

    private static TextBlock Caption(string text, double top = 0) => new()
    {
        Text = text, Foreground = CaptionBrush, FontSize = 10, FontWeight = FontWeights.Bold, Margin = new Thickness(0, top, 0, 3)
    };

    private Button SmallButton(string text, string style) => new()
    {
        Content = text, Style = (Style)FindResource(style), FontSize = 11, Padding = new Thickness(12, 3, 12, 3)
    };

    private static Grid VitalRow(string label, StoredVital vital, Brush fill)
    {
        var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
        row.Children.Add(new TextBlock { Text = label, Foreground = RuleText, FontSize = 11 });
        var bar = Bar(vital.Fraction, fill, 5);
        Grid.SetColumn(bar, 1);
        row.Children.Add(bar);
        var value = new TextBlock { Text = vital.Text, Foreground = HintNeutral, FontSize = 10.5, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(value, 2);
        row.Children.Add(value);
        return row;
    }

    private static Grid MutationRow(string label, IReadOnlyList<string> mutations)
    {
        var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock { Text = label, Foreground = HintNeutral, FontSize = 11 });
        var text = new TextBlock
        {
            Text = mutations.Count == 0 ? "none" : string.Join(" · ", mutations),
            Foreground = mutations.Count == 0 ? HintNeutral : TileName, FontSize = 11, TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return row;
    }

    // ---- Where it was stored ------------------------------------------------------

    /// <summary>
    /// The whole island with the stored spot as a diamond and, while you are
    /// in game, your own arrow; under it the area there and how far it is
    /// from you; then "Waypoint here". All local: the bundled map, the
    /// bundled area map, your last position from the poll.
    /// </summary>
    private StackPanel BuildStoredAt(StoredDino d)
    {
        var panel = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
        panel.Children.Add(Caption("STORED AT"));

        var canvas = new Canvas { Width = StoredMapSize, Height = StoredMapSize };
        if (StoredMapPicture.Value is { } map)
        {
            canvas.Children.Add(new Image { Source = map, Width = StoredMapSize, Height = StoredMapSize, Stretch = Stretch.Fill });
        }
        var well = new Border
        {
            Width = StoredMapSize, Height = StoredMapSize, CornerRadius = new CornerRadius(3), Background = PictureWell,
            ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left, Child = canvas
        };
        panel.Children.Add(well);

        var line = new TextBlock
        {
            FontSize = 11, Width = StoredMapSize, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 0)
        };
        panel.Children.Add(line);

        var waypoint = SmallButton("Waypoint here", "NeutralButtonStyle");
        waypoint.Width = StoredMapSize;
        waypoint.HorizontalAlignment = HorizontalAlignment.Left;
        waypoint.Margin = new Thickness(0, 6, 0, 0);
        waypoint.ToolTip = "A waypoint named after this dino where it was stored, tracked on the minimap";
        panel.Children.Add(waypoint);

        var cal = _poll.Calibration;
        if (d is not { X: { } x, Y: { } y } || cal is null)
        {
            var none = new TextBlock
            {
                Text = "no position stored", Foreground = TileName, FontSize = 10.5, Width = StoredMapSize,
                TextAlignment = TextAlignment.Center, Padding = new Thickness(0, 3, 0, 3), Background = NoPositionBand
            };
            Canvas.SetTop(none, StoredMapSize / 2 - 10);
            canvas.Children.Add(none);
            line.Text = " ";
            waypoint.IsEnabled = false;
            return panel;
        }

        var (fx, fy) = cal.ToFraction(x, y);
        if (_poll.LastPlayer is { } me)
        {
            var (mx, my) = cal.ToFraction(me.X, me.Y);
            canvas.Children.Add(YouArrow(mx * StoredMapSize, my * StoredMapSize, me.Yaw + _config.MinimapYawOffsetDegrees));
        }
        canvas.Children.Add(StoredSpot(fx * StoredMapSize, fy * StoredMapSize));

        var area = AreaMapAsset.Shared?.NameAt(fx, fy);
        line.Inlines.Add(new Run(area ?? "Uncharted") { Foreground = area is null ? HintNeutral : TileName });
        if (_poll.LastPlayer is { } you)
        {
            var meters = WaypointLibrary.Distance(you.X, you.Y, x, y) / 100;
            line.Inlines.Add(new Run(meters >= 1000 ? $" · {meters / 1000:0.0} km away" : $" · {meters:0} m away") { Foreground = HintNeutral });
        }
        waypoint.Click += (_, _) => AddStorageWaypoint(d, x, y, waypoint);
        return panel;
    }

    /// <summary>A 10 px diamond in the waypoint blue with a white rim, centred on the spot.</summary>
    private static Path StoredSpot(double cx, double cy)
    {
        var spot = new Path
        {
            Data = Geometry.Parse("M 0,-5 L 5,0 L 0,5 L -5,0 Z"), Fill = StoredSpotFill, Stroke = MarkerRim, StrokeThickness = 1.2,
            RenderTransform = new TranslateTransform(cx, cy), ToolTip = "where it was stored"
        };
        return spot;
    }

    /// <summary>Your minimap arrow, smaller, turned the way the minimap turns it.</summary>
    private static Path YouArrow(double cx, double cy, double degrees)
    {
        var turn = new TransformGroup();
        turn.Children.Add(new ScaleTransform(0.75, 0.75));
        turn.Children.Add(new RotateTransform(degrees));
        turn.Children.Add(new TranslateTransform(cx, cy));
        return new Path
        {
            Data = Geometry.Parse("M 0,-8 L 6,7 L 0,3.5 L -6,7 Z"), Fill = YouFill, Stroke = ArrowRim, StrokeThickness = 1,
            RenderTransform = turn, ToolTip = "you"
        };
    }

    private static BitmapImage? LoadStoredMapPicture()
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri("pack://application:,,,/Assets/map.png");
            bmp.DecodePixelWidth = (int)(StoredMapSize * 2);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null; // fail soft: the well stays dark, the diamond still says where
        }
    }

    /// <summary>
    /// "Waypoint here" ACTS AT ONCE, like the rest of this page: the waypoint
    /// goes into the library (the minimap draws it, MainWindow saves the
    /// file) and into the Waypoints page's draft, so Save keeps it and Cancel
    /// doesn't lose it; it is tracked in both. A waypoint of the same name
    /// within 20 m (the packs' duplicate rule) is tracked instead of twinned.
    /// </summary>
    private void AddStorageWaypoint(StoredDino d, double x, double y, Button button)
    {
        var name = WaypointLibrary.SanitizeName(d.DisplayName);
        var existing = _library.Items.FirstOrDefault(w =>
            string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase) &&
            WaypointLibrary.Distance(w.X, w.Y, x, y) / 100 <= WaypointPacks.DuplicateMeters);
        var added = existing is null;
        if (existing is null)
        {
            if (_library.IsFull)
            {
                SetStorageStatus($"The waypoint library is full ({WaypointLibrary.Capacity}). Delete one on the Waypoints page first.", HintWarn);
                return;
            }
            existing = _library.Add(name, x, y, _library.NextColour());
            if (existing is null)
            {
                SetStorageStatus("That spot isn't on the island, so it can't hold a waypoint.", HintWarn);
                return;
            }
        }

        if (!_draft.Any(w => w.Id == existing.Id)) _draft.Add(existing.Clone());
        _config.TrackedWaypointId = existing.Id;
        _draftTracked = existing.Id;
        if (!added) _library.Notify(); // only the tracking changed: the minimap redraws all the same
        _waypointRowsBuilt = false;    // the Waypoints page rebuilds from the draft on its next look

        button.Content = added ? "Waypoint added ✓" : "Tracked ✓";
        button.IsEnabled = false;
        SetStorageStatus(added
            ? $"Waypoint \"{name}\" added and tracked: it's on the minimap now."
            : $"\"{name}\" was already a waypoint there: it's tracked now.", HintGood);
    }

    // ---- Rename -------------------------------------------------------------------

    /// <summary>The name and description boxes in the view's place, the website's limits on both; Save sends them at once.</summary>
    private void ShowRenameForm(StorageCard card, Grid left, StackPanel view, FrameworkElement actions)
    {
        var d = card.Dino;
        var form = new StackPanel();
        var nameBox = new TextBox
        {
            Text = d.Name ?? "", MaxLength = StoredDinos.MaxNameLength, Style = (Style)FindResource("TextBoxStyle"), Height = 26
        };
        var descriptionBox = new TextBox
        {
            Text = d.Description ?? "", MaxLength = StoredDinos.MaxDescriptionLength, Style = (Style)FindResource("TextBoxStyle"),
            Height = 54, TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Top
        };
        form.Children.Add(FieldHeader("Name", nameBox, StoredDinos.MaxNameLength, top: 0));
        form.Children.Add(nameBox);
        form.Children.Add(FieldHeader("Description", descriptionBox, StoredDinos.MaxDescriptionLength, top: 8));
        form.Children.Add(descriptionBox);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var save = SmallButton("Save", "SaveButtonStyle");
        var cancel = SmallButton("Cancel", "NeutralButtonStyle");
        cancel.Margin = new Thickness(6, 0, 0, 0);
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        form.Children.Add(buttons);

        void Close()
        {
            left.Children.Remove(form);
            view.Visibility = actions.Visibility = Visibility.Visible;
            card.Disarm = null;
        }
        async Task SaveAsync()
        {
            if (_storageBusy) return;
            var name = StoredDinos.CleanName(nameBox.Text);
            var description = StoredDinos.CleanDescription(descriptionBox.Text);
            if (name == (d.Name ?? "") && description == (d.Description ?? ""))
            {
                Close();
                SetStorageStatus("Nothing changed, so nothing was sent.", HintNeutral);
                return;
            }
            _storageBusy = true;
            save.IsEnabled = cancel.IsEnabled = false;
            SetStorageStatus($"Saving {d.DisplayName}…", HintNeutral);
            try
            {
                var result = await _poll.RenameStoredDinoAsync(d, name, description);
                if (_storageClosed) return;
                if (result is { Outcome: StorageEditOutcome.Ok, Dino: { } renamed })
                {
                    try
                    {
                        ReplaceStorageCard(card, renamed);
                        SetStorageStatus($"Saved: {renamed.DisplayName} carries the new name on the website too.", HintGood);
                    }
                    catch (Exception ex)
                    {
                        RedrawFailed(ex, "Saved on the website");
                    }
                    return;
                }
                ReportStorageEdit(result, "save the name");
            }
            finally
            {
                _storageBusy = false;
                save.IsEnabled = cancel.IsEnabled = true;
            }
        }

        save.Click += async (_, _) => await SaveAsync();
        cancel.Click += (_, _) =>
        {
            Close();
            ShowStorageHint();
        };
        nameBox.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter) await SaveAsync();
        };
        foreach (var box in new[] { nameBox, descriptionBox })
        {
            box.PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape) return;
                e.Handled = true;
                Close();
                ShowStorageHint();
            };
        }

        card.Disarm?.Invoke(); // an armed Delete goes back first
        view.Visibility = actions.Visibility = Visibility.Collapsed;
        left.Children.Add(form);
        card.Disarm = Close;
        SetStorageStatus($"Renaming {d.DisplayName}: Save sends the name and description to the website at once.", HintNeutral);
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            nameBox.Focus();
            nameBox.SelectAll();
        });
    }

    /// <summary>"Name" on the left, "5 / 40" on the right, the count following the box as you type.</summary>
    private static Grid FieldHeader(string label, TextBox box, int max, double top)
    {
        var header = new Grid { Margin = new Thickness(0, top, 0, 3) };
        header.Children.Add(new TextBlock { Text = label, Foreground = HintNeutral, FontSize = 11 });
        var count = new TextBlock { Foreground = HintNeutral, FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Right };
        void Update() => count.Text = $"{box.Text.Length} / {max}";
        Update();
        box.TextChanged += (_, _) => Update();
        header.Children.Add(count);
        return header;
    }

    /// <summary>A renamed dino's card, rebuilt where it stood and open again, so its header, description and form all start fresh.</summary>
    private void ReplaceStorageCard(StorageCard old, StoredDino renamed)
    {
        _storage = _storage?.With(renamed);
        var index = StorageCards.Children.IndexOf(old.Root);
        var slot = _storageCards.IndexOf(old);
        if (index < 0 || slot < 0) return;
        var fresh = BuildStorageCard(renamed);
        // Out, then in: WPF refuses to assign into an occupied slot of the panel
        // ("Specified index is already in use") — that crashed the first build
        // right after a rename had gone through (Oct 8 2026).
        StorageCards.Children.RemoveAt(index);
        StorageCards.Children.Insert(index, fresh.Root);
        _storageCards[slot] = fresh;
        OpenStorageCard(fresh, scroll: false);
    }

    // ---- Delete -------------------------------------------------------------------

    private async Task DeleteStoredDinoAsync(StorageCard card, Button really, Button keep)
    {
        if (_storageBusy) return;
        _storageBusy = true;
        really.IsEnabled = keep.IsEnabled = false;
        var name = card.Dino.DisplayName;
        SetStorageStatus($"Deleting {name}…", HintNeutral);
        try
        {
            var result = await _poll.DeleteStoredDinoAsync(card.Dino);
            if (_storageClosed) return;
            if (result.Outcome != StorageEditOutcome.Ok)
            {
                ReportStorageEdit(result, "delete it");
                return;
            }
            try
            {
                _storage = _storage?.Without(card.Dino.Id);
                StorageCards.Children.Remove(card.Root);
                _storageCards.Remove(card);
                if (_openDinoId == card.Dino.Id) _openDinoId = null;
                RenderStorageCount();
                UpdateFreeSlots();
                SetStorageStatus($"Deleted {name} from your storage.", HintGood);
            }
            catch (Exception ex)
            {
                RedrawFailed(ex, $"Deleted {name} on the website");
            }
        }
        finally
        {
            _storageBusy = false;
            really.IsEnabled = keep.IsEnabled = true;
        }
    }

    /// <summary>
    /// A change the website took, but the page failed to draw: say so instead
    /// of letting the exception take the whole overlay down (these handlers
    /// are async void, where nothing above would catch it). Opening the page
    /// again reloads the list.
    /// </summary>
    private void RedrawFailed(Exception ex, string done) =>
        SetStorageStatus($"{done}, but the page couldn't redraw ({ex.GetType().Name}). Open the page again to see it.", HintWarn);
}

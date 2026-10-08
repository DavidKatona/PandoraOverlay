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
/// SettingsWindow, Dino storage part (1.33, a few players' request): the
/// website's /extras Dino Storage page as cards. A card shows the species,
/// sex, name, when it was stored, growth and the website's badges; a click
/// OPENS IT IN PLACE (one at a time, scrolled to the top of the list) with
/// the vitals, the mutations and, beyond what the website shows, where it
/// was stored on our own map with the area's name and the distance from you,
/// and a "Waypoint here" button. Rename and Delete act AT ONCE like the
/// website's buttons (the third page that does, after Skins and Account):
/// rename in place with Save / Cancel, delete armed in place with a "can't be
/// undone" line. Storing and retrieving are not on the website, so not here
/// either. All requests go through PollService.Storage, which holds the
/// gating; these files only draw. This part is the page, the list and the
/// closed cards; an open card is SettingsWindow.StorageDetail.cs. Same class
/// as SettingsWindow.xaml.cs, split for reading; see CLAUDE.md.
/// </summary>
public partial class SettingsWindow
{
    private const string ExtrasPageUrl = "https://islapandora.eu/extras";
    private const double StoredMapSize = 130; // the "stored at" map in an open card: the whole island
    private const double SlotBarWidth = 90;   // matches the XAML's StorageSlotBar

    private static readonly Brush MaleBrush = Frozen(Color.FromRgb(0x7F, 0xB3, 0xFF));
    private static readonly Brush FemaleBrush = Frozen(Color.FromRgb(0xFF, 0x8F, 0xB8));
    private static readonly Brush CaptionBrush = Frozen(Color.FromRgb(0x9A, 0xA7, 0xB0)); // the page titles' grey
    private static readonly Brush BarTrack = Frozen(Color.FromRgb(0x0E, 0x13, 0x18));
    private static readonly Brush GrowthFill = Frozen(Color.FromRgb(0x7C, 0xC8, 0x84));
    private static readonly Brush CardHoverBorder = Frozen(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
    private static readonly Brush StoredSpotFill = Frozen(Color.FromRgb(0x4F, 0xC3, 0xF7)); // the waypoint blue
    private static readonly Brush MarkerRim = Frozen(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
    private static readonly Brush ArrowRim = Frozen(Color.FromArgb(0xC8, 0x1A, 0x13, 0x0A));
    private static readonly Brush NoPositionBand = Frozen(Color.FromArgb(0xB0, 0x10, 0x15, 0x1B));
    private static readonly Brush YouFill = Frozen(Color.FromRgb(0xFF, 0xC8, 0x64)); // the minimap arrow's orange

    /// <summary>The stats panel's bar colours, so a stored dino's vitals read like your live ones; blood has no bar there.</summary>
    private static readonly (string Label, Brush Fill)[] VitalLooks =
    {
        ("Health", Frozen(Color.FromRgb(0x4C, 0xAF, 0x50))),
        ("Stamina", Frozen(Color.FromRgb(0xFF, 0xD5, 0x4F))),
        ("Hunger", Frozen(Color.FromRgb(0xFF, 0x98, 0x00))),
        ("Thirst", Frozen(Color.FromRgb(0x4F, 0xC3, 0xF7))),
        ("Blood", Frozen(Color.FromRgb(0xE0, 0x5A, 0x5A)))
    };

    /// <summary>
    /// The website's badges in our colours: (text colour, background). Own
    /// brushes on purpose: a static initializer must not read another part's
    /// static fields, whose order across partial files C# leaves open.
    /// </summary>
    private static readonly (Brush Fore, Brush Back) PrimeBadge = (Frozen(Color.FromRgb(0xFF, 0xC8, 0x64)), Frozen(Color.FromRgb(0x3A, 0x2E, 0x14)));
    private static readonly (Brush Fore, Brush Back) ElderBadge = (Frozen(Color.FromRgb(0xC7, 0xD1, 0xDA)), Frozen(Color.FromRgb(0x2A, 0x30, 0x38)));
    private static readonly (Brush Fore, Brush Back) MutationBadge = (Frozen(Color.FromRgb(0xC8, 0xA0, 0xFF)), Frozen(Color.FromRgb(0x2B, 0x23, 0x40)));
    private static readonly (Brush Fore, Brush Back) CompensatedBadge = (Frozen(Color.FromRgb(0x7F, 0xD6, 0xC9)), Frozen(Color.FromRgb(0x16, 0x35, 0x31)));

    /// <summary>The island map for the open cards, decoded once at twice the well's size (200 % display scaling).</summary>
    private static readonly Lazy<BitmapImage?> StoredMapPicture = new(LoadStoredMapPicture);

    /// <summary>One card on the page and what opening, closing and rebuilding it need.</summary>
    private sealed class StorageCard
    {
        public StorageCard(StoredDino dino, Border root, StackPanel body, TextBlock chevron)
        {
            Dino = dino;
            Root = root;
            Body = body;
            Chevron = chevron;
        }

        public StoredDino Dino { get; }
        public Border Root { get; }
        public StackPanel Body { get; }
        public TextBlock Chevron { get; }
        public FrameworkElement? Detail { get; set; } // built on the card's first opening
        public bool IsOpen { get; set; }
        public Action? Disarm { get; set; }          // back from Rename or an armed Delete to the plain view
    }

    private StoredDinoList? _storage;
    private bool _storageOpened;
    private bool _storageClosed;
    private bool _storageBusy;          // a rename or delete in flight: the page takes one change at a time
    private string? _openDinoId;        // the open card, kept across a Refresh
    private FrameworkElement? _freeSlots;
    private readonly List<StorageCard> _storageCards = new();

    // ---- Opening the page ---------------------------------------------------

    /// <summary>First look at the page this dialog: one list request (or the session's copy from moments ago).</summary>
    private async void OpenStoragePage()
    {
        if (_storageOpened) return;
        _storageOpened = true;
        Closed += (_, _) => _storageClosed = true;

        if (StorageBlocked() is { } why)
        {
            SetStorageStatus(why, HintWarn);
            StorageRefreshButton.IsEnabled = false;
            return;
        }
        await LoadStorageAsync();
    }

    /// <summary>Why the page can't ask the website right now, or null.</summary>
    private string? StorageBlocked() =>
        !_signedIn ? "Sign in on the Account page first: the storage belongs to your islapandora.eu account."
        : string.IsNullOrEmpty(_poll.CurrentCookie) ? "Signed in just now: close Settings and open it again to see your storage."
        : _poll.IsSignedOut ? "The website session has ended. Sign in again on the Account page."
        : null;

    private async Task LoadStorageAsync()
    {
        SetStorageStatus(_storage is null ? "Loading your storage…" : "Refreshing…", HintNeutral);
        StorageRefreshButton.IsEnabled = false;
        var result = await _poll.GetStorageAsync();
        if (_storageClosed) return;
        StorageRefreshButton.IsEnabled = true;

        if (result.List is null)
        {
            SetStorageStatus($"Couldn't load your storage ({result.Problem}). Press Refresh to try again.", HintBad);
            return;
        }
        ShowStorage(result.List);
        if (result.Problem is not null) SetStorageStatus($"Showing the earlier list: the refresh failed ({result.Problem}).", HintWarn);
    }

    private void ShowStorage(StoredDinoList list)
    {
        _storage = list;
        RenderStorage();
        ShowStorageHint();
    }

    private void ShowStorageHint() => SetStorageStatus(
        _storage is { Dinos.Count: > 0 }
            ? "Click a dino for its vitals, its mutations and where it was stored."
            : "Nothing in storage. Store a dino in game, then press Refresh.",
        HintNeutral);

    private async void StorageRefresh_Click(object sender, RoutedEventArgs e) => await LoadStorageAsync();

    private void OpenExtrasPage_Click(object sender, RoutedEventArgs e) => OpenUrl(ExtrasPageUrl);

    private void SetStorageStatus(string text, Brush brush)
    {
        StorageStatus.Text = text;
        StorageStatus.Foreground = brush;
    }

    // ---- The list -----------------------------------------------------------

    /// <summary>
    /// All cards at once: the list is as long as the account's slots (ten
    /// seen), and a closed card is a dozen elements — the open part is built
    /// on a card's first opening. A Refresh keeps the card you had open.
    /// </summary>
    private void RenderStorage()
    {
        StorageCards.Children.Clear();
        _storageCards.Clear();
        _freeSlots = null;
        RenderStorageCount();
        if (_storage is null) return;

        foreach (var dino in _storage.Dinos)
        {
            var card = BuildStorageCard(dino);
            _storageCards.Add(card);
            StorageCards.Children.Add(card.Root);
        }
        UpdateFreeSlots();
        if (_storageCards.FirstOrDefault(c => c.Dino.Id == _openDinoId) is { } open) OpenStorageCard(open, scroll: true);
    }

    /// <summary>"3 of 10 slots used" and its thin bar, in the title row.</summary>
    private void RenderStorageCount()
    {
        if (_storage is null)
        {
            StorageCount.Text = "";
            StorageSlotBar.Visibility = Visibility.Collapsed;
            return;
        }
        var count = _storage.Dinos.Count;
        StorageCount.Text = count <= _storage.Limit ? $"{count} of {_storage.Limit} slots used" : $"{count} stored · {_storage.Limit} slots";
        StorageSlotFill.Width = SlotBarWidth * Math.Min(1, count / (double)_storage.Limit);
        StorageSlotBar.Visibility = Visibility.Visible;
    }

    /// <summary>The dashed box after the cards: how many slots are free, or that storage is empty.</summary>
    private void UpdateFreeSlots()
    {
        if (_freeSlots is not null) StorageCards.Children.Remove(_freeSlots);
        _freeSlots = null;
        if (_storage is null) return;

        var free = _storage.Free;
        var empty = _storage.Dinos.Count == 0;
        if (free == 0 && !empty) return;
        var text = empty
            ? "No dinos in storage. Store one in game to see it here."
            : $"{free} {(free == 1 ? "slot" : "slots")} free · store a dino in game to fill one";
        _freeSlots = new Grid
        {
            Height = 40,
            Children =
            {
                new Rectangle
                {
                    Stroke = CardBorder, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 },
                    RadiusX = 4, RadiusY = 4, SnapsToDevicePixels = true
                },
                new TextBlock
                {
                    Text = text, Foreground = HintNeutral, FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            }
        };
        StorageCards.Children.Add(_freeSlots);
    }

    // ---- A card, closed -------------------------------------------------------

    /// <summary>
    /// The closed card: species, sex and name over the stored date, the
    /// growth bar with its stage, and the website's badges. The whole of it
    /// is the click that opens the card.
    /// </summary>
    private StorageCard BuildStorageCard(StoredDino d)
    {
        var body = new StackPanel();
        var root = new Border
        {
            Child = body, CornerRadius = new CornerRadius(4), BorderBrush = CardBorder, BorderThickness = new Thickness(1),
            Background = CardSurface, Padding = new Thickness(10, 7, 10, 8), Margin = new Thickness(0, 0, 0, 6)
        };
        var summary = new StackPanel { Background = Brushes.Transparent, Cursor = Cursors.Hand };

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = d.Species, Foreground = TileName, FontSize = 13, FontWeight = FontWeights.SemiBold });
        if (d.Gender is "Male" or "Female")
        {
            var sex = new TextBlock
            {
                Text = d.Gender == "Male" ? "♂" : "♀", Foreground = d.Gender == "Male" ? MaleBrush : FemaleBrush,
                FontSize = 13, Margin = new Thickness(5, 0, 0, 0), ToolTip = d.Gender
            };
            Grid.SetColumn(sex, 1);
            header.Children.Add(sex);
        }
        var name = new TextBlock { FontSize = 12.5, Margin = new Thickness(7, 0, 10, 0), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Bottom };
        name.Inlines.Add(new Run("· ") { Foreground = HintNeutral });
        name.Inlines.Add(d.Name is { } given ? new Run(given) { Foreground = Highlight } : new Run("unnamed") { Foreground = HintNeutral });
        Grid.SetColumn(name, 2);
        header.Children.Add(name);
        var chevron = new TextBlock { Text = "▾", Foreground = RuleText, FontSize = 11, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (d.StoredAtUtc is { } stored)
        {
            var local = stored.ToLocalTime();
            right.Children.Add(new TextBlock
            {
                Text = "stored " + StoredDinos.StoredText(local, DateTime.Now.Year), Foreground = HintNeutral, FontSize = 10.5,
                VerticalAlignment = VerticalAlignment.Center, ToolTip = local.ToString("d MMM yyyy, HH:mm", System.Globalization.CultureInfo.InvariantCulture)
            });
        }
        right.Children.Add(chevron);
        Grid.SetColumn(right, 3);
        header.Children.Add(right);
        summary.Children.Add(header);

        var growth = new Grid { Margin = new Thickness(0, 5, 0, 0) };
        growth.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        growth.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        growth.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(124) });
        growth.Children.Add(new TextBlock { Text = "Growth", Foreground = HintNeutral, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
        var bar = Bar(d.Growth, GrowthFill, 6);
        Grid.SetColumn(bar, 1);
        growth.Children.Add(bar);
        var growthText = new TextBlock
        {
            Text = StoredDinos.GrowthText(d.Growth), Foreground = RuleText, FontSize = 11,
            Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(growthText, 2);
        growth.Children.Add(growthText);
        summary.Children.Add(growth);

        var badges = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        if (d.Prime) badges.Children.Add(Badge("Prime", PrimeBadge));
        if (d.ElderStacks > 0) badges.Children.Add(Badge($"Elder ×{d.ElderStacks}", ElderBadge));
        if (d.MutationCount > 0) badges.Children.Add(Badge(d.MutationCount == 1 ? "1 mutation" : $"{d.MutationCount} mutations", MutationBadge));
        if (d.Compensated) badges.Children.Add(Badge("Compensated", CompensatedBadge));
        if (badges.Children.Count > 0) summary.Children.Add(badges);

        body.Children.Add(summary);
        var card = new StorageCard(d, root, body, chevron);
        summary.MouseLeftButtonUp += (_, _) => ToggleStorageCard(card);
        summary.MouseEnter += (_, _) => root.BorderBrush = CardHoverBorder;
        summary.MouseLeave += (_, _) => root.BorderBrush = CardBorder;
        return card;
    }

    private static Border Badge(string text, (Brush Fore, Brush Back) look) => new()
    {
        Background = look.Back, CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 1, 7, 2), Margin = new Thickness(0, 0, 4, 0),
        Child = new TextBlock { Text = text, Foreground = look.Fore, FontSize = 10.5 }
    };

    /// <summary>A rounded bar on a dark track, filled to a fraction; the fill is a star column, so it follows the card's width.</summary>
    private static Grid Bar(double fraction, Brush fill, double height)
    {
        fraction = double.IsFinite(fraction) ? Math.Clamp(fraction, 0, 1) : 0;
        var grid = new Grid { Height = height, VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fraction, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - fraction, GridUnitType.Star) });
        var track = new Border { Background = BarTrack, CornerRadius = new CornerRadius(height / 2) };
        Grid.SetColumnSpan(track, 2);
        grid.Children.Add(track);
        grid.Children.Add(new Border { Background = fill, CornerRadius = new CornerRadius(height / 2) });
        return grid;
    }

    // ---- Opening and closing ----------------------------------------------------

    /// <summary>One card open at a time: opening one closes the other.</summary>
    private void ToggleStorageCard(StorageCard card)
    {
        if (card.IsOpen)
        {
            CloseStorageCard(card);
            _openDinoId = null;
            return;
        }
        foreach (var other in _storageCards.Where(c => c.IsOpen)) CloseStorageCard(other);
        OpenStorageCard(card, scroll: true);
    }

    private void OpenStorageCard(StorageCard card, bool scroll)
    {
        card.Detail ??= BuildStorageDetail(card);
        card.Detail.Visibility = Visibility.Visible;
        card.IsOpen = true;
        card.Chevron.Text = "▴";
        _openDinoId = card.Dino.Id;
        // After the layout pass, so the card above that just closed is already out of the way.
        if (scroll) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => ScrollStorageTo(card));
    }

    private static void CloseStorageCard(StorageCard card)
    {
        card.Disarm?.Invoke();
        card.Disarm = null;
        if (card.Detail is not null) card.Detail.Visibility = Visibility.Collapsed;
        card.IsOpen = false;
        card.Chevron.Text = "▾";
    }

    /// <summary>The open card's top at the top of the list, so all of it is in view.</summary>
    private void ScrollStorageTo(StorageCard card)
    {
        if (!card.IsOpen || !StorageCards.Children.Contains(card.Root)) return;
        StorageList.ScrollToVerticalOffset(card.Root.TranslatePoint(new Point(0, 0), StorageCards).Y);
    }

    /// <summary>A rename or delete that didn't happen, in words: nothing changed on the website in any of these.</summary>
    private void ReportStorageEdit(StorageEditResult result, string what)
    {
        switch (result.Outcome)
        {
            case StorageEditOutcome.TooSoon:
                SetStorageStatus("One change at a time: try again in a moment. Nothing was sent.", HintWarn);
                break;
            case StorageEditOutcome.SignedOut:
                SetStorageStatus("The website session has ended. Sign in again on the Account page; nothing was changed.", HintWarn);
                break;
            case StorageEditOutcome.Refused:
                SetStorageStatus(result.Message is null ? "The website declined, without saying why." : $"The website said: {result.Message}", HintWarn);
                break;
            default:
                SetStorageStatus($"Couldn't {what} ({result.Message}).", HintBad);
                break;
        }
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

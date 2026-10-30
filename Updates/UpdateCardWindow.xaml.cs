using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PandoraOverlay;

/// <summary>
/// The update card (1.32, the owner's design of Oct 8 2026 — sketches in
/// Desktop\Pandora Overlay Files\pandora-update-card): the overlay's own
/// centred card instead of a Windows notification, which can be snoozed or
/// swallowed. Three modes:
/// <list type="bullet">
/// <item>AVAILABLE — at launch, once per version (UpdateCardPolicy): the new
/// version, What's new from the release's notes, "Later" and "Install and
/// restart", which does exactly what the tray entry does; the card then shows
/// the download's progress until the overlay exits for the update. ✕ and
/// Escape mean Later. A failed download puts the buttons back.</item>
/// <item>UPDATED — the first start on a higher version: "Updated ✓", the
/// version, the one it replaced when known, the notes, OK.</item>
/// <item>NOTES — Settings → About's "What's new": the running version's notes, OK.</item>
/// </list>
/// The tray entry and the About page stay the update's permanent home: the
/// card is an extra and never the only way. Topmost, not modal (MainWindow
/// carries on), not in the taskbar, not part of edit mode.
/// Since 1.38 a click on the game closes it, as Later or OK would
/// (OverlayFocus), except while the update downloads. A card Windows never
/// let have the focus (at a start with Windows, say) can't notice a click
/// elsewhere, and waits for its own buttons as before.
/// </summary>
public partial class UpdateCardWindow : Window
{
    // The theme's, read as the card is built and filled: a card is never up
    // while a theme is clicked (the Themes page is in Settings, under it).
    private static Brush Soft => Theme.Current.Text.Dim.ToBrush();
    private static Brush Good => Theme.Current.UpdateCard.Good.ToBrush();
    private static Brush Warn => Theme.Current.Text.Caution.ToBrush();
    private static Brush Bullet => Theme.Current.UpdateCard.Bullet.ToBrush();
    private static Brush NoteText => Theme.Current.Text.Text.ToBrush();
    private static Brush NoteTitle => Theme.Current.Text.Label.ToBrush();

    private Func<Action<int>, Task<bool>>? _install; // AVAILABLE only
    private bool _installing;

    /// <summary>True when a click elsewhere closed it: What's new then closes Settings too (SettingsWindow.WhatsNew_Click).</summary>
    public bool ClosedByFocus { get; private set; }

    private bool _escapeDown;

    private UpdateCardWindow()
    {
        Theme.EnsureLoaded(); // the shared templates (Themes/Controls.xaml), read at parse time: a harness has no App
        InitializeComponent();
        // Esc closes on its RELEASE and keeps the keystroke (1.38). Closed on
        // the press and left unhandled, What's new handed the same keystroke on
        // to Settings, which had just got the focus back, and Settings closed
        // on its release too (the owner, Oct 24 2026): one Esc, two windows.
        // And over the game, a press acted on at once leaves the release to it.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            if (!_installing) _escapeDown = true;
        };
        PreviewKeyUp += (_, e) =>
        {
            if (e.Key != Key.Escape || !_escapeDown) return;
            _escapeDown = false;
            e.Handled = true;
            if (!_installing) Close();
        };
        Deactivated += (_, _) => _escapeDown = false;
        Loaded += (_, _) => Activate();
        OverlayFocus.Watch(this);
        OverlayFocus.Left += OnFocusLeft;
        Closed += (_, _) => OverlayFocus.Left -= OnFocusLeft;
    }

    private void OnFocusLeft()
    {
        if (_installing) return; // the download runs on; the card goes when the overlay exits for the update
        ClosedByFocus = true;
        Close();
    }

    /// <summary>A newer version is out. <paramref name="install"/> downloads (reporting 0–100) and returns false on failure; on success the overlay exits for the update.</summary>
    public static UpdateCardWindow Available(string version, string current, string? notes, Func<Action<int>, Task<bool>> install)
    {
        var card = new UpdateCardWindow { _install = install };
        card.HeaderText.Text = "Update available";
        card.VersionText.Text = $"Pandora Overlay v{version}";
        card.SubText.Text = $"You have v{current}";
        card.SubText.Foreground = Soft;
        card.HintText.Text = "Later keeps it in the tray menu.";
        card.PrimaryButton.Content = "Install and restart";
        card.FillNotes(notes);
        return card;
    }

    /// <summary>This copy was just updated. <paramref name="previous"/> is the version it replaced, when known.</summary>
    public static UpdateCardWindow Updated(string version, string? previous, string? notes)
    {
        var card = new UpdateCardWindow();
        card.HeaderText.Text = "Updated";
        card.HeaderCheck.Visibility = Visibility.Visible;
        card.VersionText.Text = $"Pandora Overlay v{version}";
        card.SubText.Text = previous is null ? "Installed just now" : $"Installed just now · you were on v{previous}";
        card.SubText.Foreground = Good;
        card.HintText.Text = "Settings › About shows your version any time.";
        card.LaterButton.Visibility = Visibility.Collapsed;
        card.PrimaryButton.Content = "OK";
        card.FillNotes(notes);
        return card;
    }

    /// <summary>Settings → About's "What's new": the running version's notes.</summary>
    public static UpdateCardWindow Notes(string version, string? notes)
    {
        var card = new UpdateCardWindow();
        card.HeaderText.Text = "What's new";
        card.VersionText.Text = $"Pandora Overlay v{version}";
        card.SubText.Visibility = Visibility.Collapsed;
        card.HintText.Text = "";
        card.LaterButton.Visibility = Visibility.Collapsed;
        card.PrimaryButton.Content = "OK";
        card.FillNotes(notes);
        return card;
    }

    private void FillNotes(string? markdown)
    {
        NotesList.Children.Clear();
        var sections = ReleaseNotes.Parse(markdown);
        if (sections.Count == 0)
        {
            NotesList.Children.Add(new TextBlock
            {
                Text = "No notes came with this version. The Releases page on GitHub lists every change.",
                Foreground = Soft, FontSize = Theme.Current.Size(12), TextWrapping = TextWrapping.Wrap
            });
            return;
        }
        var first = true;
        foreach (var section in sections)
        {
            if (section.Title.Length > 0)
            {
                NotesList.Children.Add(new TextBlock
                {
                    Text = section.Title, Foreground = NoteTitle, FontSize = Theme.Current.Size(10.5), FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, first ? 0 : 8, 0, 4)
                });
            }
            first = false;
            foreach (var item in section.Items) NotesList.Children.Add(BulletRow(item));
        }
    }

    private static UIElement BulletRow(string text)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
        var dot = new Ellipse { Width = 4.5, Height = 4.5, Fill = Bullet, Margin = new Thickness(1, 6, 9, 0), VerticalAlignment = VerticalAlignment.Top };
        DockPanel.SetDock(dot, Dock.Left);
        row.Children.Add(dot);
        row.Children.Add(new TextBlock { Text = text, Foreground = NoteText, FontSize = Theme.Current.Size(12), TextWrapping = TextWrapping.Wrap });
        return row;
    }

    private async void Primary_Click(object sender, RoutedEventArgs e)
    {
        if (_install is null)
        {
            Close();
            return;
        }
        if (_installing) return;
        _installing = true;
        CloseButton.IsEnabled = false;
        ButtonRow.Visibility = Visibility.Collapsed;
        ProgressRow.Visibility = Visibility.Visible;
        ShowProgress(0);

        bool ok;
        try
        {
            ok = await _install(percent => Dispatcher.BeginInvoke(() => ShowProgress(percent)));
        }
        catch
        {
            ok = false;
        }
        if (ok) return; // the overlay is on its way out; the card goes with it

        _installing = false;
        CloseButton.IsEnabled = true;
        ProgressRow.Visibility = Visibility.Collapsed;
        ButtonRow.Visibility = Visibility.Visible;
        PrimaryButton.Content = "Try again";
        HintText.Text = "The download failed. Try again, or use the tray menu later.";
        HintText.Foreground = Warn;
    }

    private void ShowProgress(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        ProgressText.Text = $"Downloading the update…  {percent}%";
        var track = ((FrameworkElement)ProgressFill.Parent).ActualWidth;
        ProgressFill.Width = track * percent / 100.0;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (!_installing) Close();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}

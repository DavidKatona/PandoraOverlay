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
/// </summary>
public partial class UpdateCardWindow : Window
{
    private static readonly Brush Soft = Frozen(0x7B, 0x87, 0x90);
    private static readonly Brush Good = Frozen(0x7C, 0xC8, 0x84);
    private static readonly Brush Warn = Frozen(0xFF, 0xC8, 0x64);
    private static readonly Brush Bullet = Frozen(0xFF, 0xAA, 0x00);
    private static readonly Brush NoteText = Frozen(0xC7, 0xD1, 0xDA);
    private static readonly Brush NoteTitle = Frozen(0x9A, 0xA7, 0xB0);

    private Func<Action<int>, Task<bool>>? _install; // AVAILABLE only
    private bool _installing;

    private UpdateCardWindow()
    {
        InitializeComponent();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !_installing) Close();
        };
        Loaded += (_, _) => Activate();
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
                Foreground = Soft, FontSize = 12, TextWrapping = TextWrapping.Wrap
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
                    Text = section.Title, Foreground = NoteTitle, FontSize = 10.5, FontWeight = FontWeights.Bold,
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
        row.Children.Add(new TextBlock { Text = text, Foreground = NoteText, FontSize = 12, TextWrapping = TextWrapping.Wrap });
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

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

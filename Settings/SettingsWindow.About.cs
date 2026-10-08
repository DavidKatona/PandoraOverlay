using System.Windows;
using System.Windows.Input;

namespace PandoraOverlay;

/// <summary>
/// SettingsWindow, About page (v1.30): everything about THIS COPY of the
/// overlay in one place — the version and how it got onto the PC (installed /
/// zip / plain folder), the update check and its button, where the settings
/// live with an Open folder and an import from an older copy's folder, and
/// the links (releases, issues, Discord). The owner's choice over an
/// "Updates" page: three controls would float, "this copy" fills a page.
/// A fresh install still lands on Account, so that page carries one pointer
/// line to here while no cookie is stored.
/// </summary>
public partial class SettingsWindow
{
    private const string IssuesUrl = "https://github.com/DavidKatona/PandoraOverlay/issues";

    private readonly UpdateHooks? _updates;
    private string? _offeredUpdate;

    /// <summary>Fills the page from what MainWindow handed over; the controls themselves are static XAML.</summary>
    private void InitAboutPage(OverlayConfig config)
    {
        AboutVersion.Text = _updates is null ? "Pandora Overlay" : $"Pandora Overlay {_updates.Version} · {_updates.Flavour}";
        UpdateCheck.IsChecked = config.UpdateCheckEnabled;
        UpdateButton.IsEnabled = _updates is not null;
        DataFolderText.Text = DataFolder.Path;
        ImportPointer.Visibility = _firstRun ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// "Check for updates now": one request through MainWindow's updater. Found
    /// one, the button becomes the install (or, for a plain folder, "open the
    /// download page") and a second click hands over to MainWindow.
    /// </summary>
    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_updates is null) return;
        if (_offeredUpdate is { } offered)
        {
            _updates.Apply(offered);
            return;
        }
        UpdateButton.IsEnabled = false;
        UpdateText.Text = "Checking…";
        var found = await _updates.Check();
        UpdateButton.IsEnabled = true;
        if (found is null)
        {
            UpdateText.Text = "No newer version found.";
            return;
        }
        _offeredUpdate = found;
        UpdateText.Text = $"Update available: v{found}";
        UpdateButton.Content = _updates.CanUpdate ? $"Update to v{found} and restart" : "Open download page";
    }

    /// <summary>
    /// Copy an older copy's files into the data folder. The overlay keeps
    /// running on what it loaded at start, so the page asks for a restart —
    /// and switches Save off, which would otherwise write the old values
    /// straight back over the import.
    /// </summary>
    private void ImportSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Pick the folder of your older Pandora Overlay copy" };
        if (dialog.ShowDialog(this) != true) return;
        var result = DataFolder.ImportFrom(dialog.FolderName);
        if (!result.Ok)
        {
            ImportSettingsText.Text = result.Message;
            return;
        }
        ImportSettingsText.Text = result.Message + " Exit the overlay from its tray menu and start it again to use them. Save is off now, so nothing here can overwrite them.";
        SaveButton.IsEnabled = false;
    }

    /// <summary>
    /// "What's new" (1.32): the running version's notes, read from the CHANGELOG
    /// bundled with the app (no request), in the update card's notes mode.
    /// </summary>
    private void WhatsNew_Click(object sender, RoutedEventArgs e)
    {
        var version = UpdateCardPolicy.Bare(_updates?.Version ?? Updater.InformationalVersion ?? "");
        var notes = ReleaseNotes.SectionFor(ReleaseNotes.LoadBundledChangelog(), version);
        var card = UpdateCardWindow.Notes(version, notes);
        card.Owner = this;
        card.ShowDialog();
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e) => OpenUrl(DataFolder.Path);

    private void OpenReleases_Click(object sender, RoutedEventArgs e) => OpenUrl(UpdateChecker.ReleasesPage);

    private void OpenIssues_Click(object sender, RoutedEventArgs e) => OpenUrl(IssuesUrl);

    /// <summary>The Account page's pointer for a fresh install: the import lives here.</summary>
    private void ImportPointer_Click(object sender, MouseButtonEventArgs e) => SetPage("About");
}

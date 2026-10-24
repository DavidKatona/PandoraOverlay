using System.IO;
using System.Windows;

namespace PandoraOverlay;

/// <summary>
/// SettingsWindow, About page (v1.30): everything about THIS COPY of the
/// overlay in one place — the version and how it got onto the PC (installed /
/// zip / plain folder), the update check and its button, where the settings
/// live with an Open folder, the log (1.36: Detailed log and its folder, for
/// a problem report) and the links (releases, issues, Discord). The owner's
/// choice over an "Updates" page: three controls would float, "this copy"
/// fills a page. Its "Import from an older copy…" (1.30–1.37, with a pointer
/// to it on a fresh install's Account page) was cut in 1.38, the owner's
/// call: it served the move to %AppData%, copies that old can't connect
/// since Oct 12 2026, the first start still moves an older copy's files by
/// itself (DataFolder.MigrateLegacy), and the README says how by hand.
/// </summary>
public partial class SettingsWindow
{
    // GitHub's form chooser (1.36): the bug-report form, which asks for the version and the log, or a blank issue for anything else.
    private const string IssuesUrl = "https://github.com/DavidKatona/PandoraOverlay/issues/new/choose";

    private readonly UpdateHooks? _updates;
    private string? _offeredUpdate;

    /// <summary>Fills the page from what MainWindow handed over; the controls themselves are static XAML.</summary>
    private void InitAboutPage(OverlayConfig config)
    {
        AboutVersion.Text = _updates is null ? "Pandora Overlay" : $"Pandora Overlay {_updates.Version} · {_updates.Flavour}";
        UpdateCheck.IsChecked = config.UpdateCheckEnabled;
        UpdateButton.IsEnabled = _updates is not null;
        DataFolderText.Text = DataFolder.Path;
        DetailedLogCheck.IsChecked = config.DetailedLog;
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
    /// "What's new" (1.32): the running version's notes, read from the CHANGELOG
    /// bundled with the app (no request), in the update card's notes mode. A
    /// click on the game closes the card, and the dialog with it: the card
    /// says so, as the dialog may already think itself in front again.
    /// </summary>
    private void WhatsNew_Click(object sender, RoutedEventArgs e)
    {
        var version = UpdateCardPolicy.Bare(_updates?.Version ?? Updater.InformationalVersion ?? "");
        var notes = ReleaseNotes.SectionFor(ReleaseNotes.LoadBundledChangelog(), version);
        var card = UpdateCardWindow.Notes(version, notes);
        card.Owner = this;
        OwnWindow(card.ShowDialog);
        if (card.ClosedByFocus) CloseForFocus();
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e) => OpenUrl(DataFolder.Path);

    /// <summary>The log's folder (1.36), for a problem report. Created first, so the click opens it even if the log could never be written.</summary>
    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        try { Directory.CreateDirectory(AppLog.Folder); } catch { /* the shell says so if it is missing */ }
        OpenUrl(AppLog.Folder);
    }

    private void OpenReleases_Click(object sender, RoutedEventArgs e) => OpenUrl(UpdateChecker.ReleasesPage);

    private void OpenIssues_Click(object sender, RoutedEventArgs e) => OpenUrl(IssuesUrl);
}

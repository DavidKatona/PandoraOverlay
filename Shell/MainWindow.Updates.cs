using System.Diagnostics;
using System.Windows;

namespace PandoraOverlay;

/// <summary>What the Settings page needs from MainWindow for its update button (v1.30).</summary>
public sealed record UpdateHooks(string Version, string Flavour, bool CanUpdate, Func<Task<string?>> Check, Action<string> Apply);

/// <summary>
/// MainWindow, updates part (v1.30): the launch check and the one click that
/// updates. A copy Velopack put here (Setup.exe, or the self-updating zip)
/// goes through <see cref="Updater"/>: check, then — on the tray entry or the
/// Settings button — download and leave through the normal exit path, so the
/// positions and the rolled cookie are saved before Velopack swaps the files
/// and starts the overlay again. A plain folder keeps the old notice that
/// opens the download page. Never on a timer, never without a click.
/// Since 1.32 the launch also brings the update card (UpdateCardWindow,
/// rules in UpdateCardPolicy): "updated" once after a version change, and
/// "update available" once per version, with its notes and the same install.
/// </summary>
public partial class MainWindow
{
    private readonly Updater _updater = new();
    private Task<bool>? _applying; // the one download + hand-over in progress, shared by the tray, the card and Settings

    /// <summary>
    /// At launch (1.32): the "updated" card if this copy runs a higher version
    /// than last time, then the quiet check, if the setting allows it — one
    /// GitHub request, then an offer (status line + tray) and, once per
    /// version, the "update available" card. See UpdateCardPolicy.
    /// </summary>
    private async Task CheckForUpdateAsync()
    {
        var firstRun = string.IsNullOrWhiteSpace(_config.GetCookie()); // Settings opens on Account then
        var updatedShown = ShowUpdatedCard(firstRun);
        if (!_config.UpdateCheckEnabled) return;
        if (await CheckForUpdateNowAsync() is not { } version) return;
        OfferUpdate(version);
        if (!_updater.CanUpdate || !UpdateCardPolicy.ShowAvailable(version, _config.UpdateCardShownFor, firstRun, updatedShown)) return;
        _config.UpdateCardShownFor = UpdateCardPolicy.Bare(version); // once per version, whatever the button
        _config.Save();
        UpdateCardWindow.Available(UpdateCardPolicy.Bare(version), UpdateCardPolicy.Bare(_updater.CurrentVersion), _updater.PendingNotes, ApplyUpdateAsync).Show();
    }

    /// <summary>Records the running version and shows the "updated" card when it is higher than the one that ran last. True when shown.</summary>
    private bool ShowUpdatedCard(bool firstRun)
    {
        var running = UpdateCardPolicy.Bare(_updater.CurrentVersion);
        var show = UpdateCardPolicy.ShowUpdated(running, _config.LastRunVersion, firstRun, out var previous);
        if (_config.LastRunVersion != running)
        {
            _config.LastRunVersion = running;
            _config.Save();
        }
        if (show) UpdateCardWindow.Updated(running, previous, ReleaseNotes.SectionFor(ReleaseNotes.LoadBundledChangelog(), running)).Show();
        return show;
    }

    /// <summary>One check now (launch, or the Settings button): the newer version's number, or null for none / unknown / offline.</summary>
    private async Task<string?> CheckForUpdateNowAsync()
    {
        if (_updater.CanUpdate) return await _updater.CheckAsync();
        var tag = await UpdateChecker.CheckAsync(); // a plain folder: GitHub's latest release tag, "v1.31.0"
        return tag?.TrimStart('v', 'V');
    }

    /// <summary>The offer: on the status line (the next poll overwrites it) and in the tray for the session — install for a click where this copy can, the download page where it can't.</summary>
    private void OfferUpdate(string version)
    {
        SetStatus($"Update available: v{version}");
        if (_updater.CanUpdate) _tray.ShowUpdateAvailable($"v{version}", $"Update to v{version} and restart", () => _ = ApplyUpdateAsync(null));
        else _tray.ShowUpdateAvailable($"v{version}", $"Update available (v{version}) — open download page", OpenReleasesPage);
    }

    /// <summary>
    /// The click (tray, card or Settings): download, hand the package to
    /// Velopack's updater and exit normally; it swaps the files and starts the
    /// overlay again. A second click while one runs joins it instead of
    /// starting another. True when the overlay is on its way out.
    /// </summary>
    private Task<bool> ApplyUpdateAsync(Action<int>? progress)
    {
        if (!_updater.CanUpdate) return Task.FromResult(false);
        return _applying ??= RunUpdateAsync(progress);
    }

    private async Task<bool> RunUpdateAsync(Action<int>? progress)
    {
        SetStatus("Downloading the update…");
        _tray.SetUpdateBusy("Downloading the update…");
        var ready = (_updater.Pending is not null || await _updater.CheckAsync() is not null)
                    && await _updater.DownloadAsync(progress)
                    && _updater.ApplyOnExit();
        if (ready)
        {
            Application.Current.Shutdown();
            return true;
        }
        _applying = null;
        SetStatus($"Update failed ({_updater.Note}) · try again later");
        if (_updater.Pending is { } version) OfferUpdate(version);
        return false;
    }

    private UpdateHooks UpdateHooksForSettings() => new(
        _updater.CurrentVersion, _updater.Flavour, _updater.CanUpdate, CheckForUpdateNowAsync,
        version =>
        {
            if (_updater.CanUpdate) _ = ApplyUpdateAsync(null);
            else OpenReleasesPage();
        });

    private static void OpenReleasesPage()
    {
        try { Process.Start(new ProcessStartInfo(UpdateChecker.ReleasesPage) { UseShellExecute = true }); }
        catch { /* fail soft — worst case the user browses to the repo manually */ }
    }
}

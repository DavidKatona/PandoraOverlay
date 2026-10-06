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
/// </summary>
public partial class MainWindow
{
    private readonly Updater _updater = new();
    private bool _updating;

    /// <summary>The quiet launch check, if the setting allows it: one GitHub request, then an offer or nothing.</summary>
    private async Task CheckForUpdateAsync()
    {
        if (!_config.UpdateCheckEnabled) return;
        if (await CheckForUpdateNowAsync() is { } version) OfferUpdate(version);
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
        if (_updater.CanUpdate) _tray.ShowUpdateAvailable($"v{version}", $"Update to v{version} and restart", () => _ = ApplyUpdateAsync());
        else _tray.ShowUpdateAvailable($"v{version}", $"Update available (v{version}) — open download page", OpenReleasesPage);
    }

    /// <summary>The click: download, hand the package to Velopack's updater and exit normally; it swaps the files and starts the overlay again.</summary>
    private async Task ApplyUpdateAsync()
    {
        if (_updating || !_updater.CanUpdate) return;
        _updating = true;
        SetStatus("Downloading the update…");
        _tray.SetUpdateBusy("Downloading the update…");
        var ready = (_updater.Pending is not null || await _updater.CheckAsync() is not null)
                    && await _updater.DownloadAsync()
                    && _updater.ApplyOnExit();
        if (ready)
        {
            Application.Current.Shutdown();
            return;
        }
        _updating = false;
        SetStatus($"Update failed ({_updater.Note}) · try again later");
        if (_updater.Pending is { } version) OfferUpdate(version);
    }

    private UpdateHooks UpdateHooksForSettings() => new(
        _updater.CurrentVersion, _updater.Flavour, _updater.CanUpdate, CheckForUpdateNowAsync,
        version =>
        {
            if (_updater.CanUpdate) _ = ApplyUpdateAsync();
            else OpenReleasesPage();
        });

    private static void OpenReleasesPage()
    {
        try { Process.Start(new ProcessStartInfo(UpdateChecker.ReleasesPage) { UseShellExecute = true }); }
        catch { /* fail soft — worst case the user browses to the repo manually */ }
    }
}

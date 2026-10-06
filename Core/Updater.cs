using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace PandoraOverlay;

/// <summary>
/// One-click updates (v1.30): Velopack against this repository's GitHub
/// releases, for a copy Velopack put on the PC — Setup.exe's install or the
/// self-updating zip. A plain folder (the plain zip, or a build from the IDE)
/// cannot update itself; MainWindow falls back to <see cref="UpdateChecker"/>'s
/// notice there. Nothing here runs on a timer: the launch check is one GitHub
/// request, gated by the "Check for updates at launch" setting, and the
/// download and the restart happen only for a click. Fail soft: a problem ends
/// in <see cref="Note"/> as an exception's type name, never as an exception.
/// A pre-release build (a "-rc.1" version) also sees pre-releases, so testers
/// on rc.1 are offered rc.2 while everyone else sees only full releases.
/// </summary>
public sealed class Updater
{
    public const string Repository = "https://github.com/DavidKatona/PandoraOverlay";

    private readonly UpdateManager? _manager;
    private UpdateInfo? _found;

    /// <summary>Why the last step found or did nothing, in a few words (an exception's type at most).</summary>
    public string Note { get; private set; } = "";

    public Updater()
    {
        try { _manager = new UpdateManager(new GithubSource(Repository, null, AcceptsPreReleases(InformationalVersion))); }
        catch (Exception e) { Note = e.GetType().Name; }
    }

    /// <summary>The build's own version string, "1.30.0-rc.1+sha" style.</summary>
    public static string? InformationalVersion =>
        typeof(Updater).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    /// <summary>"1.30.0-rc.1" and "1.30.0-rc.1+sha" are pre-releases; "1.30.0" and "1.30.0+sha" are not.</summary>
    internal static bool AcceptsPreReleases(string? version)
    {
        if (string.IsNullOrEmpty(version)) return false;
        var plus = version.IndexOf('+');
        return (plus < 0 ? version : version[..plus]).Contains('-');
    }

    /// <summary>True for a copy Velopack put here (installed or the self-updating zip): it can download and apply updates.</summary>
    public bool CanUpdate
    {
        get { try { return _manager?.IsInstalled == true; } catch { return false; } }
    }

    /// <summary>"installed", "zip" or "plain folder" — how this copy got onto the PC, for the Settings page.</summary>
    public string Flavour
    {
        get
        {
            try { return _manager is null || !_manager.IsInstalled ? "plain folder" : _manager.IsPortable ? "zip" : "installed"; }
            catch { return "plain folder"; }
        }
    }

    /// <summary>The version Velopack says is running (installed / zip), else the build's own without its build metadata.</summary>
    public string CurrentVersion
    {
        get
        {
            try { return _manager?.CurrentVersion?.ToString() ?? InformationalVersion?.Split('+')[0] ?? "?"; }
            catch { return InformationalVersion?.Split('+')[0] ?? "?"; }
        }
    }

    /// <summary>The newer version found by the last check and not yet applied.</summary>
    public string? Pending => _found?.TargetFullRelease.Version.ToString();

    /// <summary>One request to GitHub: the newer version's number, or null (none, or <see cref="Note"/> says why).</summary>
    public async Task<string?> CheckAsync()
    {
        _found = null;
        if (_manager is null) { Note = "Updater unavailable"; return null; }
        try
        {
            _found = await _manager.CheckForUpdatesAsync().ConfigureAwait(true);
            if (_found is null) { Note = "No newer version"; return null; }
            return _found.TargetFullRelease.Version.ToString();
        }
        catch (Exception e)
        {
            Note = e.GetType().Name;
            return null;
        }
    }

    /// <summary>Downloads the found update (the small delta where one exists) into Velopack's packages folder.</summary>
    public async Task<bool> DownloadAsync()
    {
        if (_manager is null || _found is null) { Note = "Nothing to download"; return false; }
        try
        {
            await _manager.DownloadUpdatesAsync(_found).ConfigureAwait(true);
            return true;
        }
        catch (Exception e)
        {
            Note = e.GetType().Name;
            return false;
        }
    }

    /// <summary>
    /// Hands the downloaded update to Velopack's updater, which waits for this
    /// process to exit, swaps the files and starts the overlay again — so the
    /// overlay leaves through its normal exit path and saves its state first.
    /// </summary>
    public bool ApplyOnExit()
    {
        if (_manager is null || _found is null) { Note = "Nothing to apply"; return false; }
        try
        {
            _manager.WaitExitThenApplyUpdates(_found.TargetFullRelease, silent: false, restart: true);
            return true;
        }
        catch (Exception e)
        {
            Note = e.GetType().Name;
            return false;
        }
    }
}

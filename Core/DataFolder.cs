using System.IO;

namespace PandoraOverlay;

/// <summary>
/// Where the overlay keeps what is YOURS: config.json (with the cookie vault),
/// waypoints.json, friends.json and the skin-picture cache. Since v1.30 that is
/// %AppData%\PandoraOverlay, outside the app folder — an update replaces the
/// app folder whole, so these could no longer live next to the exe (they did
/// until 1.29). The first launch after the move COPIES an older copy's files
/// over — never moves or deletes them — and Settings → About can import
/// them from any folder later. Pure IO, zero WPF, fail soft: a problem is a
/// message, never an exception, and never the cookie's value.
/// Roaming, as Velopack's docs suggest for files that must outlive an
/// uninstall: an uninstall removes the app (under %LocalAppData%\PandoraOverlay)
/// and leaves this folder behind, as well as Velopack's own log folder,
/// %LocalAppData%\Velopack (seen in the updater trial, Oct 2026). Every copy
/// — installed, zip or plain folder — shares this one folder.
/// </summary>
public static class DataFolder
{
    public const string Name = "PandoraOverlay";

    /// <summary>The files that are yours, in the order they are copied; the first is the one that says "settings live here".</summary>
    public static readonly string[] Files = { "config.json", "waypoints.json", "friends.json" };

    /// <summary>The skin-picture cache's folder inside the data folder — re-downloadable, but copied so a move doesn't cost a reload.</summary>
    public const string Cache = "cache";

    public static string Path { get; } = Resolve();

    public static string FileIn(string name) => System.IO.Path.Combine(Path, name);

    /// <summary>
    /// The sign-in window's browser folder (v1.31): under %LocalAppData%, not
    /// the roaming data folder — a browser's cache is nobody's content and
    /// should not travel with a profile. Nothing of the sign-in itself lands
    /// here: the window browses in private, so Discord's login lives in
    /// memory only. Left to the default, WebView2 would create this folder
    /// next to the exe, inside the app folder an update replaces.
    /// </summary>
    public static string BrowserFolder { get; } =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Name, "WebView2");

    /// <summary>
    /// The Account card's saved avatar (Oct 8 2026): a small copy beside the skin
    /// pictures' cache, in its own folder so a Sign out can drop it without
    /// touching the skins. Re-downloadable; emptied on Sign out.
    /// </summary>
    public static string AvatarFolder { get; } = System.IO.Path.Combine(Path, Cache, "account");

    /// <summary>Sign out: drops the browser folder; anything in it is re-creatable. Fail soft.</summary>
    public static void ClearBrowserFolder()
    {
        try { if (Directory.Exists(BrowserFolder)) Directory.Delete(BrowserFolder, recursive: true); } catch { }
    }

    /// <summary>What <see cref="MigrateLegacy"/> did on this launch, if anything.</summary>
    public static ImportResult? Migration { get; private set; }

    private static string Resolve()
    {
        var folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Name);
        try { Directory.CreateDirectory(folder); } catch { /* fail soft: saves won't stick, the overlay still runs */ }
        return folder;
    }

    /// <summary>
    /// Where an older copy may have left its files, nearest first: next to this
    /// exe (the plain zip unpacked over the old folder), one level up (this exe
    /// inside the "current" folder of the self-updating zip unpacked over the
    /// old folder), and the folder of the exe "Start with Windows" points at
    /// (an old copy elsewhere on the disk, when the installer put this one
    /// under AppData).
    /// </summary>
    public static IEnumerable<string> LegacyCandidates()
    {
        var here = AppContext.BaseDirectory.TrimEnd(System.IO.Path.DirectorySeparatorChar);
        yield return here;
        if (System.IO.Path.GetDirectoryName(here) is { } parent) yield return parent;
        if (StartupRegistration.RegisteredExePath() is { } exe && System.IO.Path.GetDirectoryName(exe) is { } folder) yield return folder;
    }

    /// <summary>The first launch after the move: fills an EMPTY data folder from the nearest older copy. Never overwrites.</summary>
    public static void MigrateLegacy() => Migration = ImportIfEmpty(Path, LegacyCandidates());

    /// <summary>Settings → About: bring the files over from a folder the user picked, replacing what is here.</summary>
    public static ImportResult ImportFrom(string source) => Import(Path, source);

    internal static ImportResult? ImportIfEmpty(string target, IEnumerable<string> candidates)
    {
        if (File.Exists(System.IO.Path.Combine(target, Files[0]))) return null;
        foreach (var candidate in candidates)
        {
            if (SameFolder(candidate, target)) continue;
            if (File.Exists(System.IO.Path.Combine(candidate, Files[0]))) return Import(target, candidate);
        }
        return null;
    }

    internal static ImportResult Import(string target, string source)
    {
        try
        {
            if (!File.Exists(System.IO.Path.Combine(source, Files[0]))) return new ImportResult(false, 0, "No settings found there (no config.json).");
            if (SameFolder(source, target)) return new ImportResult(false, 0, "That is already the settings folder.");
            Directory.CreateDirectory(target);
            var copied = new List<string>();
            foreach (var name in Files)
            {
                var from = System.IO.Path.Combine(source, name);
                if (!File.Exists(from)) continue;
                File.Copy(from, System.IO.Path.Combine(target, name), overwrite: true);
                copied.Add(name switch { "config.json" => "settings", "waypoints.json" => "waypoints", _ => "friends" });
            }
            CopyTree(System.IO.Path.Combine(source, Cache), System.IO.Path.Combine(target, Cache));
            return new ImportResult(true, copied.Count, $"Copied your {string.Join(", ", copied)}.");
        }
        catch (Exception e)
        {
            return new ImportResult(false, 0, $"Could not copy ({e.GetType().Name}).");
        }
    }

    private static bool SameFolder(string a, string b)
    {
        try { return string.Equals(System.IO.Path.GetFullPath(a).TrimEnd('\\'), System.IO.Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    /// <summary>Copies a folder tree, file by file, skipping anything that fails: the cache is a convenience, not content.</summary>
    private static void CopyTree(string from, string to)
    {
        if (!Directory.Exists(from)) return;
        try
        {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.EnumerateFiles(from))
            {
                try { File.Copy(file, System.IO.Path.Combine(to, System.IO.Path.GetFileName(file)), overwrite: true); } catch { }
            }
            foreach (var dir in Directory.EnumerateDirectories(from)) CopyTree(dir, System.IO.Path.Combine(to, System.IO.Path.GetFileName(dir)));
        }
        catch { }
    }
}

/// <summary>What an import did, in words fit for the status line or the Settings page.</summary>
public sealed record ImportResult(bool Ok, int Files, string Message);

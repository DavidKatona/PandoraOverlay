using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;

namespace PandoraOverlay;

/// <summary>
/// How much a log line matters: Unreal Engine's ELogVerbosity, with the same
/// names, order and numbers (a lower number is more severe). A category writes
/// a line when the line's level is at or below the category's own.
/// Fatal records a crash; unlike Unreal's, it doesn't cause one. Display is
/// Unreal's "to the console too": in a Debug build, Display and above also
/// reach the debugger's Output window. Log and below go to the file only.
/// </summary>
public enum LogVerbosity
{
    NoLogging = 0,
    Fatal = 1,
    Error = 2,
    Warning = 3,
    Display = 4,
    Log = 5,
    Verbose = 6,
    VeryVerbose = 7,
}

/// <summary>
/// One named log, Unreal's DECLARE_LOG_CATEGORY: a name ("LogNet"), the level
/// it starts at, and the level it runs at now (set by AppLog.Configure from
/// config.json's LogLevels and Settings → About's Detailed log). Every
/// category is declared in AppLog.
/// A line states a fact: what happened, an outcome, a count, an exception's
/// type. It NEVER holds a credential, a header's value, a body, server text,
/// a name, a Steam ID or a position (CLAUDE.md, hard constraint 4).
/// </summary>
public sealed class LogCategory
{
    private volatile LogVerbosity _verbosity; // read by the request threads, set on the UI thread

    internal LogCategory(string name, LogVerbosity defaultVerbosity)
    {
        Name = name;
        DefaultVerbosity = defaultVerbosity;
        _verbosity = defaultVerbosity;
    }

    /// <summary>As written in the file and in config.json's LogLevels: "LogNet".</summary>
    public string Name { get; }

    public LogVerbosity DefaultVerbosity { get; }

    /// <summary>The level this category runs at now.</summary>
    public LogVerbosity Verbosity
    {
        get => _verbosity;
        internal set => _verbosity = value;
    }

    /// <summary>Unreal's UE_LOG_ACTIVE: would a line at this level be written? A Fatal line always is.</summary>
    public bool IsActive(LogVerbosity verbosity) =>
        verbosity == LogVerbosity.Fatal || (verbosity > LogVerbosity.NoLogging && verbosity <= _verbosity);

    public void Write(LogVerbosity verbosity, string message)
    {
        if (IsActive(verbosity)) AppLog.Write(this, verbosity, message);
    }

    public void Fatal(string message) => Write(LogVerbosity.Fatal, message);

    public void Error(string message) => Write(LogVerbosity.Error, message);

    public void Warning(string message) => Write(LogVerbosity.Warning, message);

    public void Display(string message) => Write(LogVerbosity.Display, message);

    public void Log(string message) => Write(LogVerbosity.Log, message);

    public void Verbose(string message) => Write(LogVerbosity.Verbose, message);

    public void VeryVerbose(string message) => Write(LogVerbosity.VeryVerbose, message);

    /// <summary>What was going on, then the exception as <see cref="AppLog.Describe"/> gives it: its type, its codes and its stack trace, never its message.</summary>
    public void Exception(LogVerbosity verbosity, string what, System.Exception ex)
    {
        if (IsActive(verbosity)) AppLog.Write(this, verbosity, $"{what}: {AppLog.Describe(ex)}");
    }
}

/// <summary>
/// The overlay's log (1.36), modelled on Unreal Engine's logging. It has named
/// categories (below, Unreal's DECLARE_LOG_CATEGORY_EXTERN), each with its own
/// <see cref="LogVerbosity"/>. A line is written only when its category is at
/// least that verbose, and a line that is switched off costs one comparison.
/// Lines go to one file per run, %LocalAppData%\PandoraOverlay\Logs\PandoraOverlay.log,
/// in Unreal's shape:
///   [2026.10.21-14.03.12:345] LogNet: Warning: POST /api/map/mylocation 403 html 212 ms
/// (the level is left out of Log lines, as Unreal does). The files are kept as
/// Unreal's Saved/Logs keeps them; see <see cref="LogFile"/>.
/// The levels: Unreal's [Core.Log] ini section is config.json's LogLevels
/// ({"LogNet": "Verbose"}, with "global" for every category), and its "Log
/// global Verbose" is Settings → About's Detailed log.
/// The owner's choices (Oct 21 2026): on for everyone at these quiet defaults;
/// ten files of 2 MB at most, kept on the PC; About's "Open log folder" to
/// hand one over; an exception as its type and stack trace only.
/// What never goes in (CLAUDE.md, hard constraint 4): the cookie, the access
/// header, any header's value or body, server text, names, Steam IDs,
/// positions. Nothing here sends anything anywhere. Fail soft: a log that
/// can't be written goes quiet and the overlay runs on. Zero WPF.
/// Until <see cref="Start"/> (App.OnStartup, after the single-instance check,
/// so a second copy never rotates the first one's file) nothing is written,
/// so the tests and the render harnesses run without a log.
/// </summary>
public static class AppLog
{
    // ---- Categories (Unreal's DECLARE_LOG_CATEGORY_EXTERN) ---------------------
    // A new feature with something to say gets its own category here, at its
    // default level, and joins Categories so config.json can set it.

    /// <summary>Start and exit, the overlay hidden and shown, edit mode, Settings saved; a crash (Fatal).</summary>
    public static readonly LogCategory App = new("LogApp", LogVerbosity.Log);

    /// <summary>One line per request (RequestLogHandler): the timed ones at Verbose, the rest at Log, failures as Warnings.</summary>
    public static readonly LogCategory Net = new("LogNet", LogVerbosity.Log);

    /// <summary>The poll stream: connected or not, in game or not, the cadence, runs of failures, the session ending.</summary>
    public static readonly LogCategory Poll = new("LogPoll", LogVerbosity.Log);

    /// <summary>The Prime check: answered here or by the website, and the cooldown.</summary>
    public static readonly LogCategory Prime = new("LogPrime", LogVerbosity.Log);

    /// <summary>The friends roster: kept, missed, cleared. Counts only, never a name.</summary>
    public static readonly LogCategory Friends = new("LogFriends", LogVerbosity.Log);

    /// <summary>The calibration, the heatmap picture, the map opened and closed.</summary>
    public static readonly LogCategory Maps = new("LogMaps", LogVerbosity.Log);

    /// <summary>Settings → Skins: the list and the applies.</summary>
    public static readonly LogCategory Skins = new("LogSkins", LogVerbosity.Log);

    /// <summary>Settings → Dino storage: the list, the renames and the deletes.</summary>
    public static readonly LogCategory Storage = new("LogStorage", LogVerbosity.Log);

    /// <summary>Signing in and out.</summary>
    public static readonly LogCategory Account = new("LogAccount", LogVerbosity.Log);

    /// <summary>This copy's version and kind, the update check, the download and the hand-over.</summary>
    public static readonly LogCategory Update = new("LogUpdate", LogVerbosity.Log);

    /// <summary>Your files: a config.json, waypoints.json or friends.json that can't be read or saved, a session that can't be decrypted.</summary>
    public static readonly LogCategory Config = new("LogConfig", LogVerbosity.Log);

    /// <summary>Hotkeys another program holds, and the replacements chosen.</summary>
    public static readonly LogCategory Hotkeys = new("LogHotkeys", LogVerbosity.Log);

    /// <summary>Every category, in the order above.</summary>
    public static IReadOnlyList<LogCategory> Categories { get; } =
        new[] { App, Net, Poll, Prime, Friends, Maps, Skins, Storage, Account, Update, Config, Hotkeys };

    // ---- Levels ------------------------------------------------------------------

    /// <summary>
    /// Sets every category's level from config.json (at launch and after each
    /// Settings save) and notes in the log what isn't at its default, as
    /// <see cref="LevelsNote"/> words it. The rule is in <see cref="LevelFor"/>.
    /// </summary>
    public static void Configure(bool detailed, IReadOnlyDictionary<string, string>? levels)
    {
        foreach (var category in Categories) category.Verbosity = LevelFor(category, detailed, levels);
        if (LevelsNote(Categories, detailed) is { } note) App.Log($"Log levels: {note}");
    }

    /// <summary>
    /// What isn't at its default, in a few words, or null when nothing is.
    /// Detailed log is named once ("Detailed log (every category at Verbose or
    /// more)"), not as twelve categories at Verbose. Then come the categories
    /// that config.json's LogLevels moved away from that: "every category at
    /// Warning" when "global" moved them all alike, else each one by name.
    /// </summary>
    internal static string? LevelsNote(IReadOnlyList<LogCategory> categories, bool detailed)
    {
        var moved = categories.Where(c => c.Verbosity != LevelFor(c, detailed, null)).ToList();
        var parts = new List<string>();
        if (detailed) parts.Add("Detailed log (every category at Verbose or more)");
        if (moved.Count > 1 && moved.Count == categories.Count && moved.All(c => c.Verbosity == moved[0].Verbosity))
        {
            parts.Add($"every category at {moved[0].Verbosity}");
        }
        else if (moved.Count > 0)
        {
            parts.Add(string.Join(", ", moved.Select(c => $"{c.Name} {c.Verbosity}")));
        }
        return parts.Count == 0 ? null : string.Join("; ", parts);
    }

    /// <summary>
    /// A category's level: its default, raised to Verbose by Detailed log, then
    /// LogLevels' "global" entry, then the category's own entry ("LogNet" or
    /// "Net", any case). The most specific word wins, so an entry can also
    /// lower a category. Unknown names and levels are ignored.
    /// </summary>
    internal static LogVerbosity LevelFor(LogCategory category, bool detailed, IReadOnlyDictionary<string, string>? levels)
    {
        var level = detailed && category.DefaultVerbosity < LogVerbosity.Verbose ? LogVerbosity.Verbose : category.DefaultVerbosity;
        if (levels is null) return level;
        foreach (var (name, value) in levels)
        {
            if (string.Equals(name, "global", StringComparison.OrdinalIgnoreCase) && TryParseVerbosity(value, out var all)) level = all;
        }
        foreach (var (name, value) in levels)
        {
            if (Names(category, name) && TryParseVerbosity(value, out var own)) level = own;
        }
        return level;
    }

    private static bool Names(LogCategory category, string name) =>
        string.Equals(name, category.Name, StringComparison.OrdinalIgnoreCase) ||
        string.Equals("Log" + name, category.Name, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A level as Unreal spells it, in any case ("verbose", "VeryVerbose"),
    /// plus Unreal's "All" (= VeryVerbose) and "None" (= NoLogging). Numbers
    /// and lists are refused.
    /// </summary>
    internal static bool TryParseVerbosity(string? text, out LogVerbosity verbosity)
    {
        verbosity = default;
        var word = text?.Trim();
        if (string.IsNullOrEmpty(word)) return false;
        if (string.Equals(word, "All", StringComparison.OrdinalIgnoreCase))
        {
            verbosity = LogVerbosity.VeryVerbose;
            return true;
        }
        if (string.Equals(word, "None", StringComparison.OrdinalIgnoreCase))
        {
            verbosity = LogVerbosity.NoLogging;
            return true;
        }
        foreach (var level in Enum.GetValues<LogVerbosity>())
        {
            if (string.Equals(word, level.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                verbosity = level;
                return true;
            }
        }
        return false;
    }

    // ---- The file ----------------------------------------------------------------

    public const string FileName = "PandoraOverlay.log";

    /// <summary>A file that would grow past this rolls over to a backup mid-run.</summary>
    internal const long MaxFileBytes = 2 * 1024 * 1024;

    /// <summary>The current file plus nine backups: at most about 20 MB.</summary>
    internal const int MaxFiles = 10;

    /// <summary>A backup older than this goes, whatever the count.</summary>
    internal static readonly TimeSpan KeepFor = TimeSpan.FromDays(14);

    /// <summary>
    /// Where the logs live: under %LocalAppData%, beside the sign-in browser's
    /// folder (DataFolder.BrowserFolder), not in the roaming data folder. A log
    /// is nobody's content, so it shouldn't travel with a profile, and an
    /// uninstall takes it.
    /// </summary>
    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DataFolder.Name, "Logs");

    private static LogFile? _file;

    /// <summary>Opens this run's file (App.OnStartup). Fail soft: a folder that can't be written leaves the log off.</summary>
    public static void Start()
    {
        if (LogFile.Open(Folder, DateTime.Now) is { } file) Interlocked.Exchange(ref _file, file)?.Dispose();
    }

    /// <summary>Closes the file (App.OnExit); lines after it go nowhere.</summary>
    public static void Stop() => Interlocked.Exchange(ref _file, null)?.Dispose();

    internal static void Write(LogCategory category, LogVerbosity verbosity, string message)
    {
        var line = FormatLine(DateTime.Now, category.Name, verbosity, Scrub(message));
        if (verbosity <= LogVerbosity.Display) Debug.WriteLine(line); // Debug builds only: Unreal's console
        Volatile.Read(ref _file)?.Write(line);
    }

    /// <summary>
    /// Unreal's line: "[time] Category: Level: message", with the level left out
    /// for Log. A message's further lines (a stack trace) are indented under it.
    /// </summary>
    internal static string FormatLine(DateTime local, string category, LogVerbosity verbosity, string message)
    {
        var line = new StringBuilder(48 + message.Length);
        line.Append('[').Append(local.ToString("yyyy.MM.dd-HH.mm.ss:fff", CultureInfo.InvariantCulture)).Append("] ");
        line.Append(category).Append(": ");
        if (verbosity != LogVerbosity.Log) line.Append(verbosity).Append(": ");
        var parts = message.ReplaceLineEndings("\n").Split('\n');
        line.Append(parts[0]);
        for (var i = 1; i < parts.Length; i++)
        {
            if (parts[i].Length > 0) line.Append(Environment.NewLine).Append("    ").Append(parts[i].TrimStart());
        }
        return line.ToString();
    }

    /// <summary>
    /// The last guard, like PatreonSkins.Clean: a message that names a cookie
    /// is left out whole. No line of ours names one; this is here in case one
    /// ever does.
    /// </summary>
    internal static string Scrub(string message) =>
        message.Contains("connect.sid", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("cf_clearance", StringComparison.OrdinalIgnoreCase)
            ? "(a line that named a cookie was left out)"
            : message;

    // ---- Exceptions ----------------------------------------------------------------

    /// <summary>
    /// An exception in a few words: its type, then the codes .NET attaches (the
    /// request error, the HTTP status, the socket error, a Windows error
    /// number), and the same for each exception inside it:
    /// "HttpRequestException (ConnectionError) &lt;- SocketException (ConnectionRefused)".
    /// Never the message, which can carry server words or an address (CLAUDE.md,
    /// constraint 4; the status line shows the type alone for the same reason).
    /// </summary>
    public static string Short(Exception ex)
    {
        var text = new StringBuilder();
        var depth = 0;
        for (var e = ex; e is not null && depth < 4; e = e.InnerException, depth++)
        {
            if (depth > 0) text.Append(" <- ");
            text.Append(e.GetType().Name);
            var codes = Codes(e);
            if (codes.Length > 0) text.Append(" (").Append(codes).Append(')');
        }
        return text.ToString();
    }

    private static string Codes(Exception e) => e switch
    {
        HttpRequestException h => string.Join(", ", new[]
        {
            h.HttpRequestError != HttpRequestError.Unknown ? h.HttpRequestError.ToString() : null,
            h.StatusCode is { } status ? ((int)status).ToString(CultureInfo.InvariantCulture) : null,
        }.OfType<string>()),
        SocketException s => s.SocketErrorCode.ToString(), // before Win32Exception, which it derives from
        Win32Exception w => w.NativeErrorCode.ToString(CultureInfo.InvariantCulture),
        _ => "",
    };

    /// <summary>
    /// <see cref="Short"/>, then where it happened: the stack trace of the
    /// exception and of each one inside it (method names and line numbers,
    /// never a message). For a crash or an error; a failed request needs only
    /// Short.
    /// </summary>
    public static string Describe(Exception ex)
    {
        var text = new StringBuilder(Short(ex));
        var depth = 0;
        for (var e = ex; e is not null && depth < 4; e = e.InnerException, depth++)
        {
            if (depth > 0) text.Append("\n--- inside it: ").Append(e.GetType().Name);
            if (e.StackTrace is { } trace) text.Append('\n').Append(trace);
        }
        return text.ToString();
    }
}

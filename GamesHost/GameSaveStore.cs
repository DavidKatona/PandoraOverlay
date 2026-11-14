using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using PandoraOverlay.Games;

namespace PandoraOverlay;

/// <summary>
/// Every byte of every game's save (A7). Losing a player's progress is the
/// worst bug the games can have, so the host owns the disk and no game is
/// trusted to get this right by itself: a game hands over text and a version
/// (A10's <c>IGameStorage</c> sits on top of this class) and the store does
/// the rest.
/// <para>
/// One folder per game, <c>&lt;root&gt;\&lt;id&gt;\</c> (so no file is shared
/// between two games, G3, and one game's bad save can't touch another's), with
/// these files and no others:
/// <c>save.json</c> the current save; <c>save.bak</c> the good save before the
/// last write; <c>save.json.tmp</c> a write in progress;
/// <c>save.v&lt;n&gt;.json</c> the last good save of version n, copied before
/// the first write of a higher one; <c>save.before-reset.json</c> the save a
/// Reset replaced; <c>unreadable-&lt;n&gt;.json</c> and
/// <c>unreadable-&lt;n&gt;.bak</c>, damaged files set aside, by the player's
/// "Start fresh" (<see cref="SetAside"/>) or by a write that found a damaged
/// save in its way (A36), never deleted and never read again.
/// </para>
/// <para>
/// The envelope round the game's text: one JSON object, UTF-8 without a BOM,
/// <c>Game</c> (the id), <c>Version</c> (from 1), <c>SavedUtc</c>,
/// <c>Check</c> (the SHA-256 of the payload's UTF-8 bytes, lower-case hex) and
/// <c>Payload</c> (the game's text, as a JSON string). A file is good when it
/// parses, names this game, holds a version of 1 or more, and its check
/// matches its payload. The check is what tells a torn or damaged file from a
/// good one: a file can parse and still be wrong.
/// </para>
/// <para>
/// A write can't be torn: the envelope goes to <c>save.json.tmp</c>, is
/// flushed to the disk, and is swapped in by <c>File.Replace</c>, which makes
/// the old save the backup in the same step. A crash at any point
/// leaves either the old save or the new one under <c>save.json</c>, and a
/// leftover temp file is never read. A damaged save never becomes the backup
/// (A36): a write that finds one in its way sets it aside first, so a good
/// backup stays where it is, and after any write the last good save before it
/// is still on disk. A save that could not be read is HELD
/// (<see cref="Hold"/>): every write is refused until the player's armed
/// "Start fresh" sets the files aside, so it is never silently replaced.
/// </para>
/// <para>
/// Nothing here throws: a failure is <c>false</c> and a Warning, and the host
/// keeps the pending text for its next flush (A8). The one exception is an id
/// that fails <see cref="GameIds.IsValid"/>, which throws
/// <see cref="ArgumentException"/> from every member: that is a build defect
/// (A10's catalog test catches it), and nothing a game names may become a path
/// outside its folder.
/// </para>
/// <para>
/// The log (<see cref="AppLog.Games"/>) gets facts only: the game's id, what
/// happened, an exception as <see cref="AppLog.Short"/>. Never a payload, never
/// a path (a path holds the Windows user's name). Plain IO, zero WPF, and the
/// store knows no game and none of the Contract's interfaces: ids, versions
/// and text only. The constructor touches nothing on disk, and nothing creates
/// the root until a game first writes. Not thread-safe: the host calls it on
/// the UI thread, as everything in the games does.
/// </para>
/// </summary>
internal sealed class GameSaveStore
{
    // ---- The files (A7) ----------------------------------------------------------

    internal const string SaveFile = "save.json";
    internal const string BackupFile = "save.bak";
    internal const string TempFile = "save.json.tmp";
    internal const string BeforeResetFile = "save.before-reset.json";

    /// <summary>The last save of a version, kept for ever: what a later, fixed build can recover from if an upgrade step was wrong.</summary>
    internal static string VersionFile(int version) => $"save.v{version.ToString(CultureInfo.InvariantCulture)}.json";

    internal static string UnreadableSave(int n) => $"unreadable-{n.ToString(CultureInfo.InvariantCulture)}.json";

    internal static string UnreadableBackup(int n) => $"unreadable-{n.ToString(CultureInfo.InvariantCulture)}.bak";

    /// <summary>
    /// The highest number a set-aside pair is given. Only a bound, so a disk
    /// that answers "it exists" to every name can't hang the overlay: a player
    /// clicks "Start fresh" once per save that broke, never thousands of times.
    /// </summary>
    internal const int MaxSetAside = 10_000;

    private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };

    /// <summary>
    /// Indented, so a person recovering a save by hand reads one field a line.
    /// The relaxed escaper leaves non-ASCII text and HTML's characters as they
    /// are and writes a payload's quotes as \" rather than " (the payload
    /// is usually JSON itself, full of quotes). It is "unsafe" only for text
    /// put inside a web page, which a save never is.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _root;
    private readonly Func<DateTime> _utcNow;
    private readonly HashSet<string> _held = new(StringComparer.Ordinal);

    /// <param name="root">The folder that holds one folder per game: the app passes the user's games folder (A7), a test a temporary one.</param>
    /// <param name="utcNow">The calendar, for the envelope's <c>SavedUtc</c> only; never read back.</param>
    public GameSaveStore(string root, Func<DateTime> utcNow)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(utcNow);
        _root = root;
        _utcNow = utcNow;
    }

    // ---- Reading -----------------------------------------------------------------

    /// <summary>
    /// The save, with the backup's fallback: <c>save.json</c> good is
    /// <see cref="SaveReadState.Ok"/>; else <c>save.bak</c> good is
    /// <see cref="SaveReadState.FromBackup"/> (and a Warning); else, when
    /// either file is there, <see cref="SaveReadState.Unreadable"/>; neither is
    /// <see cref="SaveReadState.None"/>, exactly <c>default(SaveRead)</c>.
    /// None and Unreadable carry version 0 and no payload, whatever a damaged
    /// file still showed (A34): a version from a file that failed its check is
    /// not a fact, and a game must not answer "from a newer version" on it.
    /// A leftover <c>save.json.tmp</c> is ignored: it is a write that never
    /// finished, and the save it would have replaced is still in place.
    /// </summary>
    public SaveRead Read(string gameId)
    {
        var folder = FolderOf(gameId);
        var save = Look(gameId, Path.Combine(folder, SaveFile));
        if (save.Good is { } current)
        {
            AppLog.Games.Log($"{gameId}: save read (version {current.Version})");
            return new SaveRead(SaveReadState.Ok, current.Version, current.Payload);
        }
        var backup = Look(gameId, Path.Combine(folder, BackupFile));
        if (backup.Good is { } previous)
        {
            AppLog.Games.Warning($"{gameId}: the save could not be read ({save.Why}), its backup was (version {previous.Version})");
            return new SaveRead(SaveReadState.FromBackup, previous.Version, previous.Payload);
        }
        if (save.There || backup.There)
        {
            AppLog.Games.Warning($"{gameId}: neither the save ({save.Why}) nor its backup ({backup.Why}) could be read");
            return new SaveRead(SaveReadState.Unreadable, 0, null);
        }
        AppLog.Games.Log($"{gameId}: no save yet");
        return default;
    }

    /// <summary>
    /// What one file turned out to be: there or not; when there, opened and
    /// read or not; and its envelope when it is good. Three cases matter to a
    /// write (A36): missing; there but not opened (it can't be judged, so
    /// nothing is moved); read and damaged (<see cref="Damaged"/>: set aside).
    /// </summary>
    private readonly record struct Found(bool There, bool Opened, Envelope? Good, string Why)
    {
        public bool Damaged => Opened && Good is null;
    }

    /// <summary>A good file's content.</summary>
    private sealed record Envelope(int Version, string Payload);

    /// <summary>
    /// One file, read whole. Any exception makes it not good. A file that is
    /// not found (nor its folder) is not there; one that failed to open for any
    /// other reason (access refused, locked, a disk error) counts as THERE, so
    /// the read says Unreadable and the save is held, rather than None, which
    /// would let a fresh game's first writes push the real save out through
    /// the backup. When in doubt, hold.
    /// </summary>
    private static Found Look(string gameId, string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new Found(false, false, null, "missing");
        }
        catch (Exception ex)
        {
            return new Found(true, false, null, $"not opened: {AppLog.Short(ex)}");
        }
        var (good, why) = Parse(gameId, bytes);
        return new Found(true, true, good, why);
    }

    /// <summary>
    /// The envelope, when the bytes are a good file of this game; else null and
    /// why, in a few words for the log (never the content). A leading UTF-8 BOM
    /// is skipped: the store never writes one, but a file saved again by an
    /// editor may carry one and is otherwise the same file.
    /// </summary>
    private static (Envelope? Good, string Why) Parse(string gameId, ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(Utf8Bom)) bytes = bytes[Utf8Bom.Length..];
        if (bytes.IsEmpty) return (null, "empty");
        EnvelopeFile? file;
        try
        {
            file = JsonSerializer.Deserialize<EnvelopeFile>(bytes, JsonOptions);
        }
        catch (Exception)
        {
            return (null, "not an envelope"); // cut short, not JSON, a field of the wrong kind
        }
        if (file is null) return (null, "not an envelope");
        if (!string.Equals(file.Game, gameId, StringComparison.Ordinal)) return (null, "not this game's");
        if (file.Version < 1) return (null, "a version under 1"); // versions start at 1 (A34)
        if (file.Payload is null || file.Check is null) return (null, "no payload");
        if (!string.Equals(file.Check, CheckOf(file.Payload), StringComparison.Ordinal)) return (null, "the check failed");
        return (new Envelope(file.Version, file.Payload), "good");
    }

    /// <summary>The envelope as it is on disk. The names are the file's, case and all.</summary>
    private sealed class EnvelopeFile
    {
        public string? Game { get; set; }
        public int Version { get; set; }
        public string? SavedUtc { get; set; }
        public string? Check { get; set; }
        public string? Payload { get; set; }
    }

    /// <summary>The SHA-256 of the payload's UTF-8 bytes, lower-case hex.</summary>
    internal static string CheckOf(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

    // ---- Writing -----------------------------------------------------------------

    /// <summary>
    /// The game's text on disk before this returns, or <c>false</c> and a
    /// Warning. In this order (A7), any exception ending it:
    /// 0. a version under 1 (versions start at 1, A34) or no text at all:
    ///    refused, nothing touched;
    /// 1. a held save: refused, nothing touched;
    /// 2. the game's folder is created;
    /// 3. what is in the way (<see cref="ClearTheWay"/>, A36): a
    ///    <c>save.json</c> that is there but can't be opened ends the write
    ///    with nothing moved, since it can't be judged; on a version rise the
    ///    newest GOOD file (<c>save.json</c>, else <c>save.bak</c>) is copied
    ///    to <c>save.v&lt;its version&gt;.json</c> (a rise happens once, so
    ///    the copy is the last good save before it); a damaged
    ///    <c>save.json</c> is set aside as <c>unreadable-&lt;n&gt;.json</c>
    ///    (with a damaged <c>save.bak</c> as <c>unreadable-&lt;n&gt;.bak</c>)
    ///    instead of becoming the backup, so a good backup stays where it is;
    /// 4. the envelope to <c>save.json.tmp</c>, flushed to the disk itself,
    ///    not only to Windows' cache, so a power cut can't leave the swap
    ///    pointing at a file that was never written;
    /// 5. swapped in: <c>File.Replace</c> over <c>save.json</c>, the old
    ///    (good) save becoming <c>save.bak</c> in the same step; or, with no
    ///    <c>save.json</c> (the first save, or one set aside in step 3), a
    ///    plain move, <c>save.bak</c> left as it was. If Windows fails half-way
    ///    through a replace, the old save is left as <c>save.bak</c> and the
    ///    read falls back to it.
    /// </summary>
    public bool Write(string gameId, int version, string payload)
    {
        var folder = FolderOf(gameId);
        if (version < 1)
        {
            AppLog.Games.Warning($"{gameId}: write refused: version {version} is under 1");
            return false;
        }
        if (payload is null)
        {
            AppLog.Games.Warning($"{gameId}: write refused: no text");
            return false;
        }
        if (_held.Contains(gameId))
        {
            AppLog.Games.Warning($"{gameId}: write refused: the save is held");
            return false;
        }
        try
        {
            Directory.CreateDirectory(folder);
            var save = Path.Combine(folder, SaveFile);
            var temp = Path.Combine(folder, TempFile);
            if (!ClearTheWay(gameId, folder, version)) return false;
            var bytes = EnvelopeBytes(gameId, version, payload);
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(save)) File.Replace(temp, save, Path.Combine(folder, BackupFile), ignoreMetadataErrors: true);
            else File.Move(temp, save);
            AppLog.Games.Verbose($"{gameId}: saved (version {version}, {bytes.Length} bytes)");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Games.Warning($"{gameId}: write failed: {AppLog.Short(ex)}");
            return false;
        }
    }

    /// <summary>
    /// Step 3 of <see cref="Write"/> (A36), before the temp file is written:
    /// what is on disk is looked at with the reader <see cref="Read"/> uses,
    /// and the way is cleared so that nothing good is ever pushed out.
    /// <c>false</c> (with a Warning, nothing moved) when <c>save.json</c> is
    /// there and can't be opened. Why each part:
    /// the version copy comes from the newest good file, so a rise run on a
    /// save read from its backup (with <c>save.json</c> missing or damaged)
    /// keeps its copy too: that is the case a wrong upgrade step must be
    /// undone from. A damaged <c>save.json</c> is moved aside rather than
    /// becoming the backup over a good one, which would leave only the
    /// damaged bytes and the new save. A <c>save.bak</c> that can't be opened
    /// is left alone and the write goes on: the new save is good either way,
    /// and nothing is lost by not touching what can't be judged. Throws on an
    /// IO failure, which ends the write (<see cref="Write"/>'s catch).
    /// </summary>
    private static bool ClearTheWay(string gameId, string folder, int version)
    {
        var savePath = Path.Combine(folder, SaveFile);
        var backupPath = Path.Combine(folder, BackupFile);
        var save = Look(gameId, savePath);
        if (save.There && !save.Opened)
        {
            AppLog.Games.Warning($"{gameId}: write refused: the save is there but was {save.Why}");
            return false;
        }

        // The backup matters only when save.json is not good: as the version
        // copy's source, or as a damaged file to set aside with it.
        var backup = save.Good is null ? Look(gameId, backupPath) : default;

        string? source = null;
        Envelope? kept = null;
        if (save.Good is { } current) (source, kept) = (savePath, current);
        else if (backup.Good is { } previous) (source, kept) = (backupPath, previous);
        if (source is not null && kept is not null && kept.Version < version)
        {
            File.Copy(source, Path.Combine(folder, VersionFile(kept.Version)), overwrite: true);
            AppLog.Games.Log($"{gameId}: kept the version {kept.Version} save before the first version {version} write");
        }

        if (save.Damaged)
        {
            var n = FreeNumber(folder);
            File.Move(savePath, Path.Combine(folder, UnreadableSave(n)));
            if (backup.Damaged) File.Move(backupPath, Path.Combine(folder, UnreadableBackup(n)));
            AppLog.Games.Warning($"{gameId}: a damaged save was set aside as unreadable-{n} before the write");
        }
        return true;
    }

    private byte[] EnvelopeBytes(string gameId, int version, string payload) =>
        JsonSerializer.SerializeToUtf8Bytes(new EnvelopeFile
        {
            Game = gameId,
            Version = version,
            SavedUtc = Stamp(_utcNow()),
            Check = CheckOf(payload),
            Payload = payload,
        }, JsonOptions);

    /// <summary>ISO 8601 in UTC, to the second ("2026-11-14T10:00:00Z"). A local time is converted; any other is taken as UTC.</summary>
    internal static string Stamp(DateTime utc) =>
        (utc.Kind == DateTimeKind.Local ? utc.ToUniversalTime() : utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    // ---- Held saves (A7) ---------------------------------------------------------

    /// <summary>
    /// Refuse every write for this game until <see cref="SetAside"/>: the host
    /// holds a save the game could not take (it could not be read, or it is
    /// from a newer version), so nothing replaces it before the player has
    /// chosen "Start fresh". In memory only: at the next launch the read finds
    /// the same file and the host holds it again.
    /// </summary>
    public void Hold(string gameId)
    {
        FolderOf(gameId);
        if (_held.Add(gameId)) AppLog.Games.Log($"{gameId}: save held: writes refused until it is set aside");
    }

    public bool IsHeld(string gameId)
    {
        FolderOf(gameId);
        return _held.Contains(gameId);
    }

    /// <summary>
    /// The player's armed "Start fresh": <c>save.json</c> and <c>save.bak</c>
    /// (whichever are there) move to <c>unreadable-&lt;n&gt;.json</c> and
    /// <c>unreadable-&lt;n&gt;.bak</c>, n the first from 1 for which neither
    /// name is taken, so a set-aside file is never overwritten; then the hold
    /// is released. <c>true</c> when neither file is left in the way. The hold
    /// is released ONLY then: a damaged save still in place must stay held,
    /// or the fresh game's first writes would push it out through the backup.
    /// Set-aside files are never deleted and never read again by the overlay;
    /// they are there for a person, or a later build, to recover.
    /// </summary>
    public bool SetAside(string gameId)
    {
        var folder = FolderOf(gameId);
        try
        {
            var save = Path.Combine(folder, SaveFile);
            var backup = Path.Combine(folder, BackupFile);
            if (File.Exists(save) || File.Exists(backup))
            {
                var n = FreeNumber(folder);
                if (File.Exists(save)) File.Move(save, Path.Combine(folder, UnreadableSave(n)));
                if (File.Exists(backup)) File.Move(backup, Path.Combine(folder, UnreadableBackup(n)));
                AppLog.Games.Log($"{gameId}: the save was set aside as unreadable-{n}");
            }
            if (File.Exists(save) || File.Exists(backup))
            {
                AppLog.Games.Warning($"{gameId}: the save could not be set aside");
                return false;
            }
            if (_held.Remove(gameId)) AppLog.Games.Log($"{gameId}: the hold is released");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Games.Warning($"{gameId}: the save could not be set aside: {AppLog.Short(ex)}");
            return false;
        }
    }

    /// <summary>The first n from 1 for which neither set-aside name is taken. Throws past <see cref="MaxSetAside"/>.</summary>
    private static int FreeNumber(string folder)
    {
        for (var n = 1; n <= MaxSetAside; n++)
        {
            if (!File.Exists(Path.Combine(folder, UnreadableSave(n))) && !File.Exists(Path.Combine(folder, UnreadableBackup(n)))) return n;
        }
        throw new IOException("no free number to set the save aside under");
    }

    /// <summary>
    /// Before a Ready game's Reset (the tile's armed second click): a copy of
    /// <c>save.json</c> as <c>save.before-reset.json</c>, over the last one,
    /// so a Reset by mistake can be undone by hand. <c>true</c> when there was
    /// nothing to copy or the copy worked.
    /// </summary>
    public bool KeepBeforeReset(string gameId)
    {
        var folder = FolderOf(gameId);
        try
        {
            var save = Path.Combine(folder, SaveFile);
            if (!File.Exists(save)) return true;
            File.Copy(save, Path.Combine(folder, BeforeResetFile), overwrite: true);
            AppLog.Games.Log($"{gameId}: the save was kept before a reset");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Games.Warning($"{gameId}: the save could not be kept before a reset: {AppLog.Short(ex)}");
            return false;
        }
    }

    // ---- The id ------------------------------------------------------------------

    /// <summary>The game's folder; an id that could leave it is a build defect and throws.</summary>
    private string FolderOf(string gameId)
    {
        if (!GameIds.IsValid(gameId))
        {
            throw new ArgumentException("Not a game id: 1 to 32 characters of a-z, 0-9 and '-', not starting or ending with '-', and not a Windows device name (A7, A37).", nameof(gameId));
        }
        return Path.Combine(_root, gameId);
    }
}

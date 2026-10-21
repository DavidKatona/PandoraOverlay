using System.Globalization;
using System.IO;
using System.Text;

namespace PandoraOverlay;

/// <summary>
/// AppLog's file, kept the way Unreal Engine's Saved/Logs keeps its own. This
/// run writes PandoraOverlay.log. At launch the last run's file is renamed
/// PandoraOverlay-backup-&lt;when it was last written&gt;.log; a file that
/// would grow past its size limit rolls over the same way mid-run (stamped
/// with the moment it rolled) and the new one says which backup it continues.
/// Only the newest backups are kept (the limit counts this run's file too),
/// and none older than the age limit.
/// Written from any thread (the requests finish on the thread pool), one line
/// at a time under a lock and flushed per line, so a crash loses nothing. The
/// file stays readable while it is written, so Notepad can open it live.
/// Fail soft: an IO error closes the file and the log goes quiet for the rest
/// of the run. Zero WPF.
/// </summary>
internal sealed class LogFile : IDisposable
{
    private const string BackupPrefix = "PandoraOverlay-backup-";
    private const string Stamp = "yyyy.MM.dd-HH.mm.ss";
    private static readonly int NewLineBytes = Encoding.UTF8.GetByteCount(Environment.NewLine);

    private readonly object _gate = new();
    private readonly string _folder;
    private readonly long _maxBytes;
    private readonly int _maxFiles;
    private readonly TimeSpan _keepFor;
    private StreamWriter? _writer;
    private long _bytes;

    private LogFile(string folder, long maxBytes, int maxFiles, TimeSpan keepFor)
    {
        _folder = folder;
        _maxBytes = maxBytes;
        _maxFiles = maxFiles;
        _keepFor = keepFor;
    }

    private string CurrentPath => Path.Combine(_folder, AppLog.FileName);

    /// <summary>
    /// Opens this run's file in the folder: the last run's file becomes a
    /// backup, backups past the limits are deleted, and the header goes in.
    /// Null when the folder can't be written.
    /// </summary>
    internal static LogFile? Open(string folder, DateTime now, long maxBytes = AppLog.MaxFileBytes, int maxFiles = AppLog.MaxFiles, TimeSpan? keepFor = null)
    {
        var file = new LogFile(folder, maxBytes, maxFiles, keepFor ?? AppLog.KeepFor);
        lock (file._gate)
        {
            try
            {
                Directory.CreateDirectory(folder);
                if (File.Exists(file.CurrentPath)) file.MoveToBackup(File.GetLastWriteTime(file.CurrentPath));
                file.Begin(now, continues: null);
                return file;
            }
            catch (Exception)
            {
                file.Close();
                return null;
            }
        }
    }

    /// <summary>One finished line; it rolls the file over first when the line would take it past the size limit.</summary>
    internal void Write(string line)
    {
        lock (_gate)
        {
            if (_writer is null) return;
            try
            {
                var bytes = Encoding.UTF8.GetByteCount(line) + NewLineBytes;
                if (_bytes > 0 && _bytes + bytes > _maxBytes) Roll(DateTime.Now);
                WriteRaw(line, bytes);
            }
            catch (Exception)
            {
                Close();
            }
        }
    }

    /// <summary>The closing line, then the file is closed. Later lines are dropped.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_writer is null) return;
            try { WriteRaw($"Log file closed, {DateTime.Now.ToString(Stamp, CultureInfo.InvariantCulture)}", null); }
            catch (Exception) { /* closing anyway */ }
            Close();
        }
    }

    private void Roll(DateTime now)
    {
        WriteRaw("Log file full, continued in a new file", null);
        Close();
        var backup = MoveToBackup(now);
        Begin(now, continues: backup);
    }

    /// <summary>A fresh current file with Unreal's opening line ("Log file open, …"), and, after a roll, the backup it continues.</summary>
    private void Begin(DateTime now, string? continues)
    {
        Purge(DateTime.UtcNow);
        var stream = new FileStream(CurrentPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
        _bytes = 0;
        var offset = TimeZoneInfo.Local.GetUtcOffset(now);
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        WriteRaw($"Log file open, {now.ToString(Stamp, CultureInfo.InvariantCulture)} (UTC{sign}{offset:hh\\:mm})", null);
        if (continues is not null) WriteRaw($"Continued from {continues}", null);
    }

    private void WriteRaw(string line, int? bytes)
    {
        _writer!.WriteLine(line);
        _bytes += bytes ?? Encoding.UTF8.GetByteCount(line) + NewLineBytes;
    }

    /// <summary>Renames the current file to a backup stamped with the given time; a second backup within the same second gets "-2", "-3", ….</summary>
    private string MoveToBackup(DateTime stamp)
    {
        var name = BackupName(stamp);
        for (var n = 2; File.Exists(Path.Combine(_folder, name)); n++) name = BackupName(stamp, n);
        File.Move(CurrentPath, Path.Combine(_folder, name));
        return name;
    }

    /// <summary>"PandoraOverlay-backup-2026.10.21-14.03.12.log", Unreal's own naming; the n-th of a second gets "-n".</summary>
    internal static string BackupName(DateTime stamp, int n = 1) =>
        BackupPrefix + stamp.ToString(Stamp, CultureInfo.InvariantCulture) + (n > 1 ? $"-{n}" : "") + ".log";

    /// <summary>Deletes the backups past the limits. Fail soft per file: one that can't go stays.</summary>
    private void Purge(DateTime nowUtc)
    {
        var backups = new DirectoryInfo(_folder).EnumerateFiles(BackupPrefix + "*.log").Select(f => (f.FullName, f.LastWriteTimeUtc));
        foreach (var path in ToDelete(backups, nowUtc, _maxFiles, _keepFor))
        {
            try { File.Delete(path); } catch (Exception) { /* kept, then */ }
        }
    }

    /// <summary>
    /// The purge rule, pure for the tests: keep the newest backups that leave
    /// room for the current file within the file limit, and none older than
    /// the age limit.
    /// </summary>
    internal static IEnumerable<string> ToDelete(IEnumerable<(string Path, DateTime WrittenUtc)> backups, DateTime nowUtc, int maxFiles, TimeSpan keepFor)
    {
        var keep = Math.Max(0, maxFiles - 1);
        return backups
            .OrderByDescending(b => b.WrittenUtc)
            .Where((b, i) => i >= keep || nowUtc - b.WrittenUtc > keepFor)
            .Select(b => b.Path)
            .ToList();
    }

    private void Close()
    {
        try { _writer?.Dispose(); } catch (Exception) { /* nothing more to do */ }
        _writer = null;
    }
}

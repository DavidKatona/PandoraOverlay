using System.IO;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The log's file: the last run's file becoming a backup, the roll-over at the
/// size limit, the purge and the fail-soft open. Each test works in a folder
/// of its own under the temp folder, never in the real log folder.
/// </summary>
public sealed class LogFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "PandoraOverlayLogTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* temp */ }
    }

    private string Current => Path.Combine(_folder, AppLog.FileName);

    private string[] Backups() =>
        Directory.GetFiles(_folder, "PandoraOverlay-backup-*.log").Select(Path.GetFileName).OrderBy(n => n).ToArray()!;

    /// <summary>The file as Notepad would read it while the log still has it open.</summary>
    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void ANewFileOpensWithUnrealsOpeningLine_AndIsReadableWhileWritten()
    {
        using var file = LogFile.Open(_folder, new DateTime(2026, 10, 21, 14, 3, 12))!;
        file.Write("[2026.10.21-14.03.12:345] LogApp: Pandora Overlay 1.36.0");
        var text = ReadShared(Current);
        Assert.StartsWith("Log file open, 2026.10.21-14.03.12 (UTC", text);
        Assert.Contains("LogApp: Pandora Overlay 1.36.0", text);
        Assert.Empty(Backups());
    }

    [Fact]
    public void TheLastRunsFileBecomesABackupNamedForWhenItWasLastWritten()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Current, "last run");
        var lastWritten = DateTime.Now.AddHours(-3); // within the age limit whatever the clock says
        lastWritten = new DateTime(lastWritten.Year, lastWritten.Month, lastWritten.Day, lastWritten.Hour, lastWritten.Minute, lastWritten.Second);
        File.SetLastWriteTime(Current, lastWritten);

        using (LogFile.Open(_folder, new DateTime(2026, 10, 21, 9, 0, 0))) { }

        Assert.Equal(new[] { $"PandoraOverlay-backup-{lastWritten:yyyy.MM.dd-HH.mm.ss}.log" }, Backups());
        Assert.Equal("last run", File.ReadAllText(Path.Combine(_folder, Backups()[0])));
        Assert.StartsWith("Log file open, 2026.10.21-09.00.00", File.ReadAllText(Current));
    }

    [Fact]
    public void ClosingWritesUnrealsClosingLine_AndLaterLinesGoNowhere()
    {
        var file = LogFile.Open(_folder, DateTime.Now)!;
        file.Dispose();
        file.Write("after the close");
        var text = File.ReadAllText(Current);
        Assert.Contains("Log file closed, ", text);
        Assert.DoesNotContain("after the close", text);
    }

    [Fact]
    public void AFullFileRollsOverToABackup_AndTheNewOneSaysWhichItContinues()
    {
        using var file = LogFile.Open(_folder, DateTime.Now, maxBytes: 400, maxFiles: 3)!;
        for (var i = 0; i < 40; i++) file.Write($"line {i:00} " + new string('x', 40));

        var backups = Backups();
        Assert.Equal(2, backups.Length); // maxFiles counts the current file too
        var current = ReadShared(Current);
        Assert.Contains("Continued from PandoraOverlay-backup-", current);
        Assert.Contains("line 39", current);
        Assert.True(new FileInfo(Current).Length <= 400 + 100, "the current file stays near its limit");
        Assert.Contains("Log file full, continued in a new file", File.ReadAllText(Path.Combine(_folder, backups[^1])));
    }

    [Fact]
    public void TheSecondBackupOfASecondGetsANumber()
    {
        var at = new DateTime(2026, 10, 21, 14, 3, 12);
        Assert.Equal("PandoraOverlay-backup-2026.10.21-14.03.12.log", LogFile.BackupName(at));
        Assert.Equal("PandoraOverlay-backup-2026.10.21-14.03.12-2.log", LogFile.BackupName(at, 2));
    }

    [Fact]
    public void ThePurgeKeepsTheNewestBackupsThatFit_AndNoneTooOld()
    {
        var now = new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
        var backups = Enumerable.Range(1, 12).Select(i => ($"b{i:00}", now - TimeSpan.FromHours(i))).ToList(); // b01 newest
        var gone = LogFile.ToDelete(backups, now, maxFiles: 10, keepFor: TimeSpan.FromDays(14)).OrderBy(p => p).ToList();
        Assert.Equal(new[] { "b10", "b11", "b12" }, gone); // nine kept, beside the current file

        var old = new[] { ("fresh", now - TimeSpan.FromDays(1)), ("stale", now - TimeSpan.FromDays(15)) };
        Assert.Equal(new[] { "stale" }, LogFile.ToDelete(old, now, maxFiles: 10, keepFor: TimeSpan.FromDays(14)));
    }

    [Fact]
    public void OpeningPurgesTheFolder()
    {
        Directory.CreateDirectory(_folder);
        for (var i = 1; i <= 5; i++)
        {
            var path = Path.Combine(_folder, $"PandoraOverlay-backup-2026.10.0{i}-10.00.00.log");
            File.WriteAllText(path, "old");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow - TimeSpan.FromHours(i));
        }
        File.WriteAllText(Path.Combine(_folder, "notes.txt"), "not ours"); // anything else in the folder is left alone

        using (LogFile.Open(_folder, DateTime.Now, maxFiles: 3)) { }

        Assert.Equal(2, Backups().Length);
        Assert.True(File.Exists(Path.Combine(_folder, "notes.txt")));
    }

    [Fact]
    public void AFolderThatCantBeWrittenLeavesTheLogOff()
    {
        Directory.CreateDirectory(_folder);
        var blocker = Path.Combine(_folder, "blocker");
        File.WriteAllText(blocker, "a file where the folder should be");
        Assert.Null(LogFile.Open(Path.Combine(blocker, "Logs"), DateTime.Now));
    }
}

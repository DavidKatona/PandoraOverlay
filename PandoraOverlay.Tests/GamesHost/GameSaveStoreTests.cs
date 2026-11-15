using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PandoraOverlay.Games;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The games' saves on disk (A7, A34): the envelope and its check, the write
/// that can't be torn, the backup, the copy at a version rise, the held save
/// that nothing overwrites, and the files set aside by "Start fresh".
/// Every test works in a fresh temporary folder of its own, deleted after it,
/// and never in the real data folder: the store is only ever handed a root,
/// and a test below holds that its source never names the real one. After
/// every test the folder is walked: nothing the store made may lie outside
/// the root it was given.
/// </summary>
public sealed class GameSaveStoreTests : IDisposable
{
    private const string Id = "sample-game";
    private const string OtherId = "other-game";

    private static readonly DateTime At = new(2026, 11, 14, 10, 0, 0, DateTimeKind.Utc);

    private readonly string _base = Path.Combine(Path.GetTempPath(), "PandoraOverlay.Tests", Guid.NewGuid().ToString("N"));

    /// <summary>The store's root, one level under the test's folder, so a file that escaped the root would still be found by the walk.</summary>
    private string Root => Path.Combine(_base, "games");

    private string Folder => Path.Combine(Root, Id);

    private string FileOf(string name) => Path.Combine(Folder, name);

    private GameSaveStore Store() => new(Root, () => At);

    public void Dispose()
    {
        var strays = Directory.Exists(_base)
            ? Directory.EnumerateFileSystemEntries(_base, "*", SearchOption.AllDirectories)
                .Where(entry => entry != Root && !entry.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                .ToList()
            : new List<string>();
        try { Directory.Delete(_base, recursive: true); } catch { /* a temp folder; the next run uses another */ }
        Assert.Empty(strays);
    }

    // ---- Helpers ---------------------------------------------------------------

    /// <summary>Every file under the root, by its path from the root, with its bytes.</summary>
    private Dictionary<string, byte[]> Snapshot() =>
        Directory.Exists(Root)
            ? Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(Root, f), File.ReadAllBytes)
            : new Dictionary<string, byte[]>();

    private static void AssertSameFiles(Dictionary<string, byte[]> before, Dictionary<string, byte[]> after)
    {
        Assert.Equal(before.Keys.OrderBy(k => k, StringComparer.Ordinal), after.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var (name, bytes) in before) Assert.True(bytes.AsSpan().SequenceEqual(after[name]), $"{name} changed");
    }

    /// <summary>An envelope written by hand, its check right unless told otherwise.</summary>
    private static string Envelope(string game, int version, string payload, string? check = null) =>
        new JsonObject
        {
            ["Game"] = game,
            ["Version"] = version,
            ["SavedUtc"] = "2026-11-14T10:00:00Z",
            ["Check"] = check ?? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant(),
            ["Payload"] = payload,
        }.ToJsonString();

    private void Plant(string name, string text)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FileOf(name), text, new UTF8Encoding(false));
    }

    /// <summary>A good save and a good backup: save.json holds "second", save.bak "first".</summary>
    private GameSaveStore TwoWrites(int version = 1)
    {
        var store = Store();
        Assert.True(store.Write(Id, version, "first"));
        Assert.True(store.Write(Id, version, "second-save"));
        return store;
    }

    // ---- Nothing, then a save ----------------------------------------------------

    [Fact]
    public void NothingThereIsNoneAndAWriteReadsBack()
    {
        var store = Store();

        var nothing = store.Read(Id);
        Assert.Equal(default, nothing);
        Assert.Equal(new SaveRead(SaveReadState.None, 0, null), nothing);
        Assert.False(Directory.Exists(Root), "a read creates nothing");

        Assert.True(store.Write(Id, 3, "hello"));

        Assert.Equal(new SaveRead(SaveReadState.Ok, 3, "hello"), store.Read(Id));
        Assert.Equal(new SaveRead(SaveReadState.Ok, 3, "hello"), Store().Read(Id)); // from the disk, not from memory
        Assert.False(File.Exists(FileOf(GameSaveStore.TempFile)), "the temp file is swapped in, not left behind");
        Assert.False(File.Exists(FileOf(GameSaveStore.BackupFile)), "the first save has nothing to back up");
    }

    [Fact]
    public void TheEnvelopeIsOneObjectInUtf8WithoutABom()
    {
        Assert.True(Store().Write(Id, 2, "{\"Amber\":5}"));

        var bytes = File.ReadAllBytes(FileOf(GameSaveStore.SaveFile));
        Assert.NotEqual(0xEF, bytes[0]);
        using var json = JsonDocument.Parse(bytes);
        var root = json.RootElement;
        Assert.Equal(new[] { "Game", "Version", "SavedUtc", "Check", "Payload" }, root.EnumerateObject().Select(p => p.Name));
        Assert.Equal(Id, root.GetProperty("Game").GetString());
        Assert.Equal(2, root.GetProperty("Version").GetInt32());
        Assert.Equal("2026-11-14T10:00:00Z", root.GetProperty("SavedUtc").GetString());
        Assert.Equal("{\"Amber\":5}", root.GetProperty("Payload").GetString());
        var check = root.GetProperty("Check").GetString()!;
        Assert.Matches("^[0-9a-f]{64}$", check);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("{\"Amber\":5}"))).ToLowerInvariant(), check);
    }

    [Fact]
    public void TheStampIsUtcToTheSecond()
    {
        Assert.Equal("2026-11-14T10:00:00Z", GameSaveStore.Stamp(new DateTime(2026, 11, 14, 10, 0, 0, 999, DateTimeKind.Utc)));
        Assert.Equal("2026-11-14T10:00:00Z", GameSaveStore.Stamp(new DateTime(2026, 11, 14, 10, 0, 0, DateTimeKind.Unspecified)));
        var local = new DateTime(2026, 11, 14, 10, 0, 0, DateTimeKind.Local);
        Assert.Equal(GameSaveStore.Stamp(local.ToUniversalTime()), GameSaveStore.Stamp(local));
    }

    [Fact]
    public void AwkwardTextComesBackEqual()
    {
        var store = Store();
        var megabyte = new StringBuilder();
        var accented = new string((char)0x00E9, 1);
        var chinese = new string(new[] { (char)0x4E2D, (char)0x6587 });
        while (megabyte.Length < 1024 * 1024) megabyte.Append("{\"turn\":12,\"note\":\"a \\\"quoted\\\" line\\nnext\"} " + accented + "\U0001F996 ");
        var payloads = new[]
        {
            "",
            "plain",
            "quotes \" and 'single' and \"\"",
            "lines\nand\r\nmore\rand\ttabs",
            "a backslash \\ and two \\\\ and a \\u0022 that is text",
            "emoji \U0001F996\U0001F995 and " + accented + " and " + chinese,
            "<html> & 'script' +",
            "{\"Gold\":5,\"Name\":\"x\\\"y\"}",
            megabyte.ToString(),
        };
        var version = 1;
        foreach (var payload in payloads)
        {
            Assert.True(store.Write(Id, version, payload));
            var read = Store().Read(Id);
            Assert.Equal(SaveReadState.Ok, read.State);
            Assert.Equal(version, read.Version);
            Assert.Equal(payload, read.Payload);
            version++;
        }
    }

    [Fact]
    public void TextThatIsNotWholeUnicodeStillSaves()
    {
        // A lone surrogate can't be written as UTF-8: it becomes U+FFFD, in the
        // check and in the file alike, so the save still reads back as good.
        // What matters is that the write is not refused for ever.
        Assert.True(Store().Write(Id, 1, "half a pair \ud83e here"));
        var read = Store().Read(Id);
        Assert.Equal(SaveReadState.Ok, read.State);
        Assert.StartsWith("half a pair ", read.Payload);
        Assert.EndsWith(" here", read.Payload);
    }

    // ---- The backup --------------------------------------------------------------

    [Fact]
    public void ASecondWriteKeepsTheFirstAsTheBackup()
    {
        var store = TwoWrites();
        Assert.Equal(new SaveRead(SaveReadState.Ok, 1, "second-save"), store.Read(Id));

        File.WriteAllText(FileOf(GameSaveStore.SaveFile), "{ \"Game\": \"sample-", new UTF8Encoding(false));

        Assert.Equal(new SaveRead(SaveReadState.FromBackup, 1, "first"), store.Read(Id));
    }

    [Fact]
    public void AThirdWriteMovesTheBackupOn()
    {
        var store = TwoWrites();
        Assert.True(store.Write(Id, 1, "third"));
        File.Delete(FileOf(GameSaveStore.SaveFile));

        Assert.Equal(new SaveRead(SaveReadState.FromBackup, 1, "second-save"), store.Read(Id));
    }

    [Theory]
    [InlineData("cut in half")]
    [InlineData("one character of the payload changed")]
    [InlineData("not JSON")]
    [InlineData("another game's id inside")]
    [InlineData("empty")]
    [InlineData("a version of 0")]
    [InlineData("no check")]
    [InlineData("no payload")]
    [InlineData("a JSON array")]
    [InlineData("the version as text")]
    public void EachWayOfBeingBadFallsBackToTheBackup(string how)
    {
        var store = TwoWrites();
        var path = FileOf(GameSaveStore.SaveFile);
        var bytes = File.ReadAllBytes(path);
        var text = Encoding.UTF8.GetString(bytes);
        JsonObject Node() => JsonNode.Parse(text)!.AsObject();
        string Without(string name) { var node = Node(); node.Remove(name); return node.ToJsonString(); }
        string With(string name, JsonNode value) { var node = Node(); node[name] = value; return node.ToJsonString(); }

        switch (how)
        {
            case "cut in half": File.WriteAllBytes(path, bytes[..(bytes.Length / 2)]); break;
            case "one character of the payload changed":
                Assert.Contains("second-save", text);
                File.WriteAllText(path, text.Replace("second-save", "secand-save"), new UTF8Encoding(false));
                break;
            case "not JSON": File.WriteAllText(path, "this is not a save at all"); break;
            case "another game's id inside": File.WriteAllText(path, With("Game", OtherId)); break; // its check still matches
            case "empty": File.WriteAllBytes(path, Array.Empty<byte>()); break;
            case "a version of 0": File.WriteAllText(path, With("Version", 0)); break;
            case "no check": File.WriteAllText(path, Without("Check")); break;
            case "no payload": File.WriteAllText(path, Without("Payload")); break;
            case "a JSON array": File.WriteAllText(path, "[" + text + "]"); break;
            case "the version as text": File.WriteAllText(path, With("Version", "1")); break;
            default: throw new ArgumentOutOfRangeException(nameof(how));
        }

        Assert.Equal(new SaveRead(SaveReadState.FromBackup, 1, "first"), store.Read(Id));
    }

    [Fact]
    public void AByteOrderMarkInFrontOfAGoodFileIsStillGood()
    {
        Plant(GameSaveStore.SaveFile, Envelope(Id, 1, "marked"));
        var path = FileOf(GameSaveStore.SaveFile);
        File.WriteAllBytes(path, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(File.ReadAllBytes(path)).ToArray());

        Assert.Equal(new SaveRead(SaveReadState.Ok, 1, "marked"), Store().Read(Id));
    }

    // ---- Unreadable and None (A34) -----------------------------------------------

    [Fact]
    public void BothBadIsUnreadableWithNoVersionAndNoPayload()
    {
        var store = TwoWrites(version: 7);
        var path = FileOf(GameSaveStore.SaveFile);
        var text = File.ReadAllText(path);
        File.WriteAllText(path, text.Replace("second-save", "secand-save"), new UTF8Encoding(false));
        File.WriteAllText(FileOf(GameSaveStore.BackupFile), "");
        Assert.Contains("\"Version\": 7", File.ReadAllText(path)); // the damaged file still shows a version

        var read = store.Read(Id);

        Assert.Equal(new SaveRead(SaveReadState.Unreadable, 0, null), read);
    }

    [Fact]
    public void ABadSaveAloneIsUnreadable()
    {
        Plant(GameSaveStore.SaveFile, "torn");
        Assert.Equal(new SaveRead(SaveReadState.Unreadable, 0, null), Store().Read(Id));
    }

    [Fact]
    public void ABadBackupAloneIsUnreadable()
    {
        Plant(GameSaveStore.BackupFile, "torn");
        Assert.Equal(new SaveRead(SaveReadState.Unreadable, 0, null), Store().Read(Id));
    }

    [Fact]
    public void AGoodBackupAloneIsFromBackup()
    {
        Plant(GameSaveStore.BackupFile, Envelope(Id, 4, "kept"));
        Assert.Equal(new SaveRead(SaveReadState.FromBackup, 4, "kept"), Store().Read(Id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void AWellFormedEnvelopeWithAVersionUnderOneIsNotGood(int version)
    {
        Plant(GameSaveStore.SaveFile, Envelope(Id, version, "fine text"));
        Assert.Equal(new SaveRead(SaveReadState.Unreadable, 0, null), Store().Read(Id));
    }

    [Fact]
    public void AWriteOfAVersionUnderOneIsRefusedAndTouchesNothing()
    {
        var store = Store();
        Assert.False(store.Write(Id, 0, "zero"));
        Assert.False(Directory.Exists(Root), "a refused write creates no folder");

        TwoWrites();
        var before = Snapshot();
        Assert.False(store.Write(Id, 0, "zero"));
        Assert.False(store.Write(Id, -1, "minus one"));
        AssertSameFiles(before, Snapshot());
        Assert.Equal(new SaveRead(SaveReadState.Ok, 1, "second-save"), store.Read(Id));
    }

    [Fact]
    public void AWriteWithNoTextIsRefusedAndTouchesNothing()
    {
        Assert.False(Store().Write(Id, 1, null!));
        Assert.False(Directory.Exists(Root));
    }

    [Fact]
    public void OnlyATempFileIsNone()
    {
        Plant(GameSaveStore.TempFile, Envelope(Id, 1, "a write that never finished"));
        Assert.Equal(default, Store().Read(Id));
    }

    // ---- A damaged save in a write's way (A36) -------------------------------------

    /// <summary>A save.json that parses but fails its check, at the version given, and a good save.bak ("A") at version 1.</summary>
    private (byte[] Damaged, byte[] Backup) DamagedSaveGoodBackup(int damagedVersion = 1)
    {
        Plant(GameSaveStore.SaveFile, Envelope(Id, damagedVersion, "newer progress", check: new string('0', 64)));
        Plant(GameSaveStore.BackupFile, Envelope(Id, 1, "A"));
        return (File.ReadAllBytes(FileOf(GameSaveStore.SaveFile)), File.ReadAllBytes(FileOf(GameSaveStore.BackupFile)));
    }

    private string[] Unreadables() =>
        Directory.GetFiles(Folder, "unreadable-*").Select(f => Path.GetFileName(f)).OrderBy(f => f, StringComparer.Ordinal).ToArray();

    [Fact]
    public void ADamagedSaveIsSetAsideAndTheGoodBackupStays()
    {
        var (damaged, backup) = DamagedSaveGoodBackup();
        var store = Store();
        Assert.Equal(new SaveRead(SaveReadState.FromBackup, 1, "A"), store.Read(Id));

        Assert.True(store.Write(Id, 1, "B"));

        Assert.Equal(new SaveRead(SaveReadState.Ok, 1, "B"), store.Read(Id));
        Assert.Equal(backup, File.ReadAllBytes(FileOf(GameSaveStore.BackupFile)));
        Assert.Equal(damaged, File.ReadAllBytes(FileOf(GameSaveStore.UnreadableSave(1))));
        Assert.False(File.Exists(FileOf(GameSaveStore.UnreadableBackup(1))), "a good backup is not set aside");

        // The next write is an ordinary one: the good "B" becomes the backup.
        Assert.True(store.Write(Id, 1, "C"));
        Assert.Equal(new SaveRead(SaveReadState.Ok, 1, "B"), ReadAsSave(FileOf(GameSaveStore.BackupFile)));
        Assert.Equal(damaged, File.ReadAllBytes(FileOf(GameSaveStore.UnreadableSave(1))));
        Assert.Equal(new SaveRead(SaveReadState.Ok, 1, "C"), store.Read(Id));
    }

    [Fact]
    public void AVersionRiseOverADamagedSaveKeepsTheGoodBackupAsItsCopy()
    {
        var (damaged, backup) = DamagedSaveGoodBackup(damagedVersion: 1);
        var store = Store();

        Assert.True(store.Write(Id, 2, "U"));

        Assert.Equal(backup, File.ReadAllBytes(FileOf(GameSaveStore.VersionFile(1))));
        Assert.Equal(backup, File.ReadAllBytes(FileOf(GameSaveStore.BackupFile)));
        Assert.Equal(damaged, File.ReadAllBytes(FileOf(GameSaveStore.UnreadableSave(1))));
        Assert.Equal(new SaveRead(SaveReadState.Ok, 2, "U"), store.Read(Id));
    }

    [Fact]
    public void AVersionRiseWithOnlyABackupKeepsItsCopy()
    {
        Plant(GameSaveStore.BackupFile, Envelope(Id, 1, "A"));
        var backup = File.ReadAllBytes(FileOf(GameSaveStore.BackupFile));
        var store = Store();
        Assert.Equal(new SaveRead(SaveReadState.FromBackup, 1, "A"), store.Read(Id));

        Assert.True(store.Write(Id, 2, "U"));

        Assert.Equal(backup, File.ReadAllBytes(FileOf(GameSaveStore.VersionFile(1))));
        Assert.Equal(backup, File.ReadAllBytes(FileOf(GameSaveStore.BackupFile)));
        Assert.Empty(Unreadables());
        Assert.Equal(new SaveRead(SaveReadState.Ok, 2, "U"), store.Read(Id));
    }

    [Fact]
    public void BothDamagedAndNotHeldAreSetAsideTogether()
    {
        Plant(GameSaveStore.SaveFile, "torn save");
        Plant(GameSaveStore.BackupFile, Envelope(OtherId, 1, "another game's backup"));
        var damagedSave = File.ReadAllBytes(FileOf(GameSaveStore.SaveFile));
        var damagedBackup = File.ReadAllBytes(FileOf(GameSaveStore.BackupFile));
        var store = Store();

        Assert.True(store.Write(Id, 1, "fresh"));

        Assert.Equal(damagedSave, File.ReadAllBytes(FileOf(GameSaveStore.UnreadableSave(1))));
        Assert.Equal(damagedBackup, File.ReadAllBytes(FileOf(GameSaveStore.UnreadableBackup(1))));
        Assert.False(File.Exists(FileOf(GameSaveStore.BackupFile)));
        Assert.Equal(new SaveRead(SaveReadState.Ok, 1, "fresh"), store.Read(Id));
    }

    [Fact]
    public void ADamagedSaveTakesTheNextFreeNumber()
    {
        Plant(GameSaveStore.UnreadableSave(1), "set aside before");
        Plant(GameSaveStore.SaveFile, "torn save");
        var before = File.ReadAllBytes(FileOf(GameSaveStore.UnreadableSave(1)));
        var damaged = File.ReadAllBytes(FileOf(GameSaveStore.SaveFile));

        Assert.True(Store().Write(Id, 1, "fresh"));

        Assert.Equal(before, File.ReadAllBytes(FileOf(GameSaveStore.UnreadableSave(1))));
        Assert.Equal(damaged, File.ReadAllBytes(FileOf(GameSaveStore.UnreadableSave(2))));
        Assert.Equal(new[] { "unreadable-1.json", "unreadable-2.json" }, Unreadables());
    }

    [Fact]
    public void AFolderInTheWayOfADamagedSaveIsPassedOver()
    {
        // Counted free, the folder would fail this write and every write of the game after it (A65).
        Plant(GameSaveStore.SaveFile, "torn save");
        var damaged = File.ReadAllBytes(FileOf(GameSaveStore.SaveFile));
        Directory.CreateDirectory(FileOf(GameSaveStore.UnreadableSave(1)));

        Assert.True(Store().Write(Id, 1, "fresh"));

        Assert.Equal(damaged, File.ReadAllBytes(FileOf(GameSaveStore.UnreadableSave(2))));
        Assert.True(Directory.Exists(FileOf(GameSaveStore.UnreadableSave(1))));
        Assert.Empty(Directory.EnumerateFileSystemEntries(FileOf(GameSaveStore.UnreadableSave(1))));
        Assert.Equal(new[] { "unreadable-2.json" }, Unreadables()); // files only: the folder is not listed
        Assert.Equal(new SaveRead(SaveReadState.Ok, 1, "fresh"), Store().Read(Id));
    }

    [Fact]
    public void ASaveThatCantBeOpenedEndsTheWriteWithNothingMoved()
    {
        var store = TwoWrites();
        var before = Snapshot();

        using (new FileStream(FileOf(GameSaveStore.SaveFile), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(store.Write(Id, 1, "can't judge what is there"));
            Assert.False(store.Write(Id, 2, "nor at a higher version"));
        }

        AssertSameFiles(before, Snapshot());
        Assert.False(File.Exists(FileOf(GameSaveStore.TempFile)));
        Assert.Equal(new SaveRead(SaveReadState.Ok, 1, "second-save"), store.Read(Id));
    }

    [Fact]
    public void ABackupThatCantBeOpenedIsLeftAloneAndTheWriteGoesOn()
    {
        Plant(GameSaveStore.SaveFile, "torn save");
        Plant(GameSaveStore.BackupFile, "can't be judged");
        var damaged = File.ReadAllBytes(FileOf(GameSaveStore.SaveFile));
        var store = Store();

        using (new FileStream(FileOf(GameSaveStore.BackupFile), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.True(store.Write(Id, 1, "fresh"));
        }

        Assert.Equal("can't be judged", File.ReadAllText(FileOf(GameSaveStore.BackupFile)));
        Assert.Equal(damaged, File.ReadAllBytes(FileOf(GameSaveStore.UnreadableSave(1))));
        Assert.Equal(new[] { "unreadable-1.json" }, Unreadables());
        Assert.Equal(new SaveRead(SaveReadState.Ok, 1, "fresh"), store.Read(Id));
    }

    // ---- A torn write ------------------------------------------------------------

    [Fact]
    public void ATornWriteLeavesTheOldSave()
    {
        var store = Store();
        Assert.True(store.Write(Id, 1, "old"));
        var half = Envelope(Id, 1, "new");
        Plant(GameSaveStore.TempFile, half[..(half.Length / 2)]); // the overlay stopped mid-write

        Assert.Equal(new SaveRead(SaveReadState.Ok, 1, "old"), Store().Read(Id));

        // The next write starts the temp file over and goes through.
        Assert.True(store.Write(Id, 1, "newer"));
        Assert.Equal(new SaveRead(SaveReadState.Ok, 1, "newer"), Store().Read(Id));
        Assert.False(File.Exists(FileOf(GameSaveStore.TempFile)));
        Assert.Equal(new SaveRead(SaveReadState.Ok, 1, "old"), ReadAsSave(FileOf(GameSaveStore.BackupFile)));
    }

    /// <summary>Any file of the store's, read as if it were a game's save.json, through a second store on a root of its own.</summary>
    private SaveRead ReadAsSave(string file)
    {
        var root = Path.Combine(Root, "zz-read-as-save", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, Id));
        File.Copy(file, Path.Combine(root, Id, GameSaveStore.SaveFile));
        return new GameSaveStore(root, () => At).Read(Id);
    }

    // ---- A version rise ----------------------------------------------------------

    [Fact]
    public void AVersionRiseKeepsTheLastSaveOfTheOldVersion()
    {
        var store = TwoWrites(version: 1);
        var lastOfOne = File.ReadAllBytes(FileOf(GameSaveStore.SaveFile));

        Assert.True(store.Write(Id, 2, "two-a"));
        var kept = FileOf(GameSaveStore.VersionFile(1));
        Assert.Equal("save.v1.json", Path.GetFileName(kept));
        Assert.Equal(lastOfOne, File.ReadAllBytes(kept));
        Assert.Equal(new SaveRead(SaveReadState.Ok, 1, "second-save"), ReadAsSave(kept));

        Assert.True(store.Write(Id, 2, "two-b"));
        Assert.Equal(lastOfOne, File.ReadAllBytes(kept));
        Assert.False(File.Exists(FileOf(GameSaveStore.VersionFile(2))), "no rise, no copy");

        var lastOfTwo = File.ReadAllBytes(FileOf(GameSaveStore.SaveFile));
        Assert.True(store.Write(Id, 3, "three"));
        Assert.Equal(lastOfTwo, File.ReadAllBytes(FileOf(GameSaveStore.VersionFile(2))));
        Assert.Equal(lastOfOne, File.ReadAllBytes(kept));
        Assert.Equal(new SaveRead(SaveReadState.Ok, 3, "three"), store.Read(Id));
    }

    [Fact]
    public void AFirstSaveAtAHigherVersionHasNothingToKeep()
    {
        Assert.True(Store().Write(Id, 5, "fresh"));
        Assert.Equal(new[] { GameSaveStore.SaveFile }, Directory.GetFiles(Folder).Select(f => Path.GetFileName(f)));
    }

    // ---- Held saves --------------------------------------------------------------

    [Fact]
    public void AHeldSaveRefusesEveryWriteUntilItIsSetAside()
    {
        var store = TwoWrites(version: 2);
        File.WriteAllText(FileOf(GameSaveStore.SaveFile), "damaged");
        File.WriteAllText(FileOf(GameSaveStore.BackupFile), "damaged too");
        Assert.Equal(SaveReadState.Unreadable, store.Read(Id).State);

        store.Hold(Id);
        Assert.True(store.IsHeld(Id));
        Assert.False(store.IsHeld(OtherId), "a hold is per game");
        Assert.False(Store().IsHeld(Id), "a hold is in memory, per store");
        var before = Snapshot();

        Assert.False(store.Write(Id, 2, "fresh"));
        Assert.False(store.Write(Id, 3, "fresh at a higher version"));
        AssertSameFiles(before, Snapshot());

        Assert.True(store.SetAside(Id));
        Assert.False(store.IsHeld(Id));
        Assert.Equal("damaged", File.ReadAllText(FileOf(GameSaveStore.UnreadableSave(1))));
        Assert.Equal("damaged too", File.ReadAllText(FileOf(GameSaveStore.UnreadableBackup(1))));
        Assert.Equal("unreadable-1.json", GameSaveStore.UnreadableSave(1));
        Assert.Equal("unreadable-1.bak", GameSaveStore.UnreadableBackup(1));
        Assert.False(File.Exists(FileOf(GameSaveStore.SaveFile)));
        Assert.False(File.Exists(FileOf(GameSaveStore.BackupFile)));
        Assert.Equal(default, store.Read(Id));

        Assert.True(store.Write(Id, 2, "a fresh start"));
        Assert.Equal(new SaveRead(SaveReadState.Ok, 2, "a fresh start"), store.Read(Id));

        // A second time: the next free number, and the first pair untouched.
        Assert.True(store.Write(Id, 2, "again"));
        File.WriteAllText(FileOf(GameSaveStore.SaveFile), "damaged again");
        File.WriteAllText(FileOf(GameSaveStore.BackupFile), "and its backup");
        store.Hold(Id);
        Assert.False(store.Write(Id, 2, "refused"));
        Assert.True(store.SetAside(Id));
        Assert.Equal("damaged again", File.ReadAllText(FileOf(GameSaveStore.UnreadableSave(2))));
        Assert.Equal("and its backup", File.ReadAllText(FileOf(GameSaveStore.UnreadableBackup(2))));
        Assert.Equal("damaged", File.ReadAllText(FileOf(GameSaveStore.UnreadableSave(1))));
        Assert.Equal("damaged too", File.ReadAllText(FileOf(GameSaveStore.UnreadableBackup(1))));
        Assert.True(store.Write(Id, 2, "the newest"));
        Assert.Equal(new SaveRead(SaveReadState.Ok, 2, "the newest"), store.Read(Id));
    }

    [Fact]
    public void ANumberIsFreeOnlyWhenNeitherNameIsTaken()
    {
        // A save without a backup sets aside alone; the next pair skips its number.
        Plant(GameSaveStore.SaveFile, "first damage");
        var store = Store();
        Assert.True(store.SetAside(Id));
        Assert.True(File.Exists(FileOf(GameSaveStore.UnreadableSave(1))));
        Assert.False(File.Exists(FileOf(GameSaveStore.UnreadableBackup(1))));

        Plant(GameSaveStore.BackupFile, "a backup alone");
        Plant(GameSaveStore.UnreadableBackup(2), "left by hand");
        Assert.True(store.SetAside(Id));
        Assert.Equal("a backup alone", File.ReadAllText(FileOf(GameSaveStore.UnreadableBackup(3))));
        Assert.Equal("left by hand", File.ReadAllText(FileOf(GameSaveStore.UnreadableBackup(2))));
        Assert.Equal("first damage", File.ReadAllText(FileOf(GameSaveStore.UnreadableSave(1))));
    }

    [Fact]
    public void AFolderInTheWayOfASetAsideIsPassedOver()
    {
        // Counted free, the folder would fail the move of the backup onto it, and so every "Start fresh" (A65).
        Plant(GameSaveStore.SaveFile, "damaged");
        Plant(GameSaveStore.BackupFile, "damaged too");
        Directory.CreateDirectory(FileOf(GameSaveStore.UnreadableBackup(1)));
        var store = Store();
        store.Hold(Id);

        Assert.True(store.SetAside(Id));
        Assert.False(store.IsHeld(Id));

        Assert.Equal("damaged", File.ReadAllText(FileOf(GameSaveStore.UnreadableSave(2))));
        Assert.Equal("damaged too", File.ReadAllText(FileOf(GameSaveStore.UnreadableBackup(2))));
        Assert.False(File.Exists(FileOf(GameSaveStore.UnreadableSave(1))));
        Assert.True(Directory.Exists(FileOf(GameSaveStore.UnreadableBackup(1))));
        Assert.Empty(Directory.EnumerateFileSystemEntries(FileOf(GameSaveStore.UnreadableBackup(1))));
        Assert.False(File.Exists(FileOf(GameSaveStore.SaveFile)));
        Assert.False(File.Exists(FileOf(GameSaveStore.BackupFile)));
    }

    [Fact]
    public void SettingAsideNothingReleasesTheHold()
    {
        var store = Store();
        store.Hold(Id);
        Assert.True(store.SetAside(Id));
        Assert.False(store.IsHeld(Id));
        Assert.False(Directory.Exists(Root), "nothing to move, nothing made");
    }

    [Fact]
    public void AHeldWriteCreatesNoFolder()
    {
        var store = Store();
        store.Hold(Id);
        Assert.False(store.Write(Id, 1, "refused"));
        Assert.False(Directory.Exists(Root));
    }

    // ---- Before a reset ----------------------------------------------------------

    /// <summary>The name a Reset's copy had before A62 (one file, overwritten at every Reset), which a build of the branch may have left.</summary>
    private const string UnnumberedCopy = "save.before-reset.json";

    /// <summary>The Reset copies in the game's folder, by name.</summary>
    private string[] ResetCopies() =>
        Directory.GetFileSystemEntries(Folder, "save.before-reset*").Select(f => Path.GetFileName(f)).OrderBy(f => f, StringComparer.Ordinal).ToArray();

    [Fact]
    public void KeepBeforeResetCopiesTheSave()
    {
        // A62 reversed what this pinned (one copy, the last Reset's only): a
        // Reset by mistake, a little play, and a second Reset must still leave
        // the first Reset's copy, whole.
        var store = TwoWrites();
        var first = File.ReadAllBytes(FileOf(GameSaveStore.SaveFile));
        Assert.True(store.KeepBeforeReset(Id));
        Assert.Equal(first, File.ReadAllBytes(FileOf(GameSaveStore.BeforeReset(1))));
        Assert.Equal(new SaveRead(SaveReadState.Ok, 1, "second-save"), ReadAsSave(FileOf(GameSaveStore.BeforeReset(1))));

        // The reset's fresh save, then another reset: a second copy, the first untouched.
        Assert.True(store.Write(Id, 1, "after the reset"));
        var second = File.ReadAllBytes(FileOf(GameSaveStore.SaveFile));
        Assert.True(store.KeepBeforeReset(Id));
        Assert.Equal(first, File.ReadAllBytes(FileOf(GameSaveStore.BeforeReset(1))));
        Assert.Equal(new SaveRead(SaveReadState.Ok, 1, "second-save"), ReadAsSave(FileOf(GameSaveStore.BeforeReset(1))));
        Assert.Equal(second, File.ReadAllBytes(FileOf(GameSaveStore.BeforeReset(2))));
        Assert.Equal(new SaveRead(SaveReadState.Ok, 1, "after the reset"), ReadAsSave(FileOf(GameSaveStore.BeforeReset(2))));
        Assert.Equal(new SaveRead(SaveReadState.Ok, 1, "after the reset"), store.Read(Id));
        Assert.Equal(new[] { "save.before-reset-1.json", "save.before-reset-2.json" }, ResetCopies());
    }

    [Fact]
    public void KeepBeforeResetWithNoSaveHasNothingToDo()
    {
        Assert.True(Store().KeepBeforeReset(Id));
        Assert.False(Directory.Exists(Root));
    }

    [Fact]
    public void KeepBeforeResetWithOnlyOtherFilesMakesNothing()
    {
        // The folder is there, with an earlier copy and a backup, but no
        // save.json: nothing to copy (a backup is not what a Reset replaces).
        var store = TwoWrites();
        Assert.True(store.KeepBeforeReset(Id));
        File.Delete(FileOf(GameSaveStore.SaveFile));
        var before = Snapshot();

        Assert.True(store.KeepBeforeReset(Id));

        AssertSameFiles(before, Snapshot());
        Assert.Equal(new[] { "save.before-reset-1.json" }, ResetCopies());
    }

    [Fact]
    public void AResetCopyTakesTheFirstFreeNumber()
    {
        var store = TwoWrites();
        Plant(GameSaveStore.BeforeReset(1), "an earlier copy");
        Plant(GameSaveStore.BeforeReset(3), "a later copy");
        var save = File.ReadAllBytes(FileOf(GameSaveStore.SaveFile));

        Assert.True(store.KeepBeforeReset(Id));

        Assert.Equal(save, File.ReadAllBytes(FileOf(GameSaveStore.BeforeReset(2))));
        Assert.Equal("an earlier copy", File.ReadAllText(FileOf(GameSaveStore.BeforeReset(1))));
        Assert.Equal("a later copy", File.ReadAllText(FileOf(GameSaveStore.BeforeReset(3))));
        Assert.Equal(new[] { "save.before-reset-1.json", "save.before-reset-2.json", "save.before-reset-3.json" }, ResetCopies());

        // And the next one skips every taken number.
        Assert.True(store.KeepBeforeReset(Id));
        Assert.Equal(save, File.ReadAllBytes(FileOf(GameSaveStore.BeforeReset(4))));
        Assert.Equal("a later copy", File.ReadAllText(FileOf(GameSaveStore.BeforeReset(3))));
    }

    [Fact]
    public void AFolderInTheWayOfAResetCopyIsPassedOver()
    {
        // Counted free, a folder of the name would fail the copy, and every
        // Reset after it, for ever.
        var store = TwoWrites();
        Directory.CreateDirectory(FileOf(GameSaveStore.BeforeReset(1)));
        var save = File.ReadAllBytes(FileOf(GameSaveStore.SaveFile));

        Assert.True(store.KeepBeforeReset(Id));

        Assert.Equal(save, File.ReadAllBytes(FileOf(GameSaveStore.BeforeReset(2))));
        Assert.True(Directory.Exists(FileOf(GameSaveStore.BeforeReset(1))));
        Assert.Empty(Directory.EnumerateFileSystemEntries(FileOf(GameSaveStore.BeforeReset(1))));
    }

    [Fact]
    public void AnUnnumberedCopyFromBeforeA62IsLeftAsItIs()
    {
        // It may be the only copy of what an earlier Reset replaced: never
        // read, moved, overwritten or deleted, and never counted as number 1.
        var store = TwoWrites();
        Plant(UnnumberedCopy, Envelope(Id, 1, "kept by an older build"));
        var old = File.ReadAllBytes(FileOf(UnnumberedCopy));
        var save = File.ReadAllBytes(FileOf(GameSaveStore.SaveFile));

        Assert.True(store.KeepBeforeReset(Id));
        Assert.True(store.Write(Id, 1, "after the reset"));
        Assert.True(store.KeepBeforeReset(Id));

        Assert.Equal(old, File.ReadAllBytes(FileOf(UnnumberedCopy)));
        Assert.Equal(save, File.ReadAllBytes(FileOf(GameSaveStore.BeforeReset(1))));
        Assert.Equal(new SaveRead(SaveReadState.Ok, 1, "after the reset"), ReadAsSave(FileOf(GameSaveStore.BeforeReset(2))));
        Assert.Equal(new[] { "save.before-reset-1.json", "save.before-reset-2.json", UnnumberedCopy }, ResetCopies()); // ordinal: '-' before '.'
        Assert.Equal(new SaveRead(SaveReadState.Ok, 1, "after the reset"), store.Read(Id));
    }

    [Fact]
    public void AResetCopyThatCantReadTheSaveFailsAndMakesNoCopy()
    {
        // The copy's failure is false (the host then resets nothing, A38), and
        // it leaves no file behind, so no number is used up by a torn copy.
        var store = TwoWrites();
        var before = Snapshot();

        using (new FileStream(FileOf(GameSaveStore.SaveFile), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(store.KeepBeforeReset(Id));
        }

        AssertSameFiles(before, Snapshot());
        Assert.Empty(ResetCopies());
    }

    // ---- A root that can't be made ----------------------------------------------

    [Fact]
    public void ARootThatCantBeCreatedFailsSoft()
    {
        Directory.CreateDirectory(_base);
        File.WriteAllText(Root, "a file where the games folder should be");
        var store = Store();

        Assert.False(store.Write(Id, 1, "nowhere to go"));
        Assert.Equal(default, store.Read(Id));
        Assert.True(store.KeepBeforeReset(Id));
        store.Hold(Id);
        Assert.True(store.SetAside(Id));
        Assert.Equal("a file where the games folder should be", File.ReadAllText(Root));
    }

    // ---- Ids ---------------------------------------------------------------------

    public static TheoryData<string?> BadIds => new()
    {
        "", "Hatchling", "a/b", "..", "-a", new string('a', 33), null, "a\\b", "c:", "x/../..",
    };

    [Theory]
    [MemberData(nameof(BadIds))]
    public void ABadIdThrowsFromEveryMember(string? id)
    {
        var store = Store();
        Assert.ThrowsAny<ArgumentException>(() => store.Read(id!));
        Assert.ThrowsAny<ArgumentException>(() => store.Write(id!, 1, "text"));
        Assert.ThrowsAny<ArgumentException>(() => store.Hold(id!));
        Assert.ThrowsAny<ArgumentException>(() => store.IsHeld(id!));
        Assert.ThrowsAny<ArgumentException>(() => store.SetAside(id!));
        Assert.ThrowsAny<ArgumentException>(() => store.KeepBeforeReset(id!));
        Assert.False(Directory.Exists(Root));
    }

    // ---- Never the real folder ---------------------------------------------------

    [Fact]
    public void TheStoreNeverNamesTheRealDataFolder()
    {
        // The store is handed its root, so no test can reach the real saves
        // through it: only the app's start-up names the real folder.
        var source = File.ReadAllText(Path.Combine(ThemeFileTests.RepoRoot(), "GamesHost", "GameSaveStore.cs"));
        Assert.DoesNotContain("DataFolder", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SpecialFolder", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetFolderPath", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EverythingTheStoreMakesIsInItsGamesFolder()
    {
        var store = Store();
        foreach (var id in new[] { Id, OtherId })
        {
            Assert.Equal(default, store.Read(id));
            Assert.True(store.Write(id, 1, $"{id} one"));
            Assert.True(store.Write(id, 1, $"{id} two"));
            Assert.True(store.Write(id, 2, $"{id} three"));
            Assert.True(store.KeepBeforeReset(id));
            File.WriteAllText(Path.Combine(Root, id, GameSaveStore.SaveFile), "damaged");
            Assert.Equal(SaveReadState.FromBackup, store.Read(id).State);
            File.WriteAllText(Path.Combine(Root, id, GameSaveStore.BackupFile), "damaged");
            Assert.Equal(SaveReadState.Unreadable, store.Read(id).State);
            store.Hold(id);
            Assert.False(store.Write(id, 2, "refused"));
            Assert.True(store.SetAside(id));
            Assert.True(store.Write(id, 2, $"{id} fresh"));
        }

        // Each game's files are its own: no file shared, none read across (G3).
        Assert.Equal(new SaveRead(SaveReadState.Ok, 2, $"{Id} fresh"), store.Read(Id));
        Assert.Equal(new SaveRead(SaveReadState.Ok, 2, $"{OtherId} fresh"), store.Read(OtherId));

        var names = new Regex(@"^(save\.json|save\.bak|save\.json\.tmp|save\.v[0-9]+\.json|save\.before-reset-[0-9]+\.json|unreadable-[0-9]+\.(json|bak))$");
        foreach (var file in Directory.EnumerateFileSystemEntries(_base, "*", SearchOption.AllDirectories))
        {
            if (file == Root) continue;
            var relative = Path.GetRelativePath(Root, file).Split(Path.DirectorySeparatorChar);
            if (relative.Length == 1)
            {
                Assert.True(relative[0] is Id or OtherId, $"{relative[0]} is not a game's folder");
                continue;
            }
            Assert.Equal(2, relative.Length);
            Assert.True(relative[0] is Id or OtherId, $"{relative[0]} is not a game's folder");
            Assert.Matches(names, relative[1]);
        }
    }
}

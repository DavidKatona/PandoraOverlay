using System.IO;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The settings move (v1.30): the first launch copies an older copy's files
/// into the new data folder, never moving or deleting anything, and only while
/// the new folder is still empty; an import from Settings replaces what is there.
/// </summary>
public sealed class DataFolderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PandoraOverlay.Tests", Guid.NewGuid().ToString("N"));

    private string Folder(string name, params string[] files)
    {
        var folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        foreach (var file in files) File.WriteAllText(Path.Combine(folder, file), $"{name}:{file}");
        return folder;
    }

    [Fact]
    public void TheFirstLaunchCopiesFromTheNearestOlderCopyThatHasSettings()
    {
        var target = Folder("new");
        var empty = Folder("empty-neighbour");
        var old = Folder("old", "config.json", "waypoints.json");
        var older = Folder("older", "config.json", "friends.json");

        var result = DataFolder.ImportIfEmpty(target, new[] { empty, old, older });

        Assert.NotNull(result);
        Assert.True(result!.Ok);
        Assert.Equal(2, result.Files);
        Assert.Equal("old:config.json", File.ReadAllText(Path.Combine(target, "config.json")));
        Assert.Equal("old:waypoints.json", File.ReadAllText(Path.Combine(target, "waypoints.json")));
        Assert.False(File.Exists(Path.Combine(target, "friends.json")), "the farther copy is not mixed in");
        Assert.True(File.Exists(Path.Combine(old, "config.json")), "the source is left where it is");
    }

    [Fact]
    public void TheFirstLaunchLeavesANewFolderThatAlreadyHasSettingsAlone()
    {
        var target = Folder("new", "config.json");
        var old = Folder("old", "config.json");

        Assert.Null(DataFolder.ImportIfEmpty(target, new[] { old }));
        Assert.Equal("new:config.json", File.ReadAllText(Path.Combine(target, "config.json")));
    }

    [Fact]
    public void TheNewFolderItselfIsNeverACandidate()
    {
        var target = Folder("new");
        Assert.Null(DataFolder.ImportIfEmpty(target, new[] { target, target + Path.DirectorySeparatorChar }));
    }

    [Fact]
    public void AnImportReplacesWhatIsThereAndBringsThePictureCache()
    {
        var target = Folder("new", "config.json");
        var old = Folder("old", "config.json", "friends.json");
        Directory.CreateDirectory(Path.Combine(old, "cache", "skins"));
        File.WriteAllText(Path.Combine(old, "cache", "skins", "a.jpg"), "jpg");

        var result = DataFolder.Import(target, old);

        Assert.True(result.Ok);
        Assert.Equal("old:config.json", File.ReadAllText(Path.Combine(target, "config.json")));
        Assert.Equal("old:friends.json", File.ReadAllText(Path.Combine(target, "friends.json")));
        Assert.Equal("jpg", File.ReadAllText(Path.Combine(target, "cache", "skins", "a.jpg")));
        Assert.Contains("settings", result.Message);
        Assert.Contains("friends", result.Message);
        Assert.DoesNotContain("waypoints", result.Message);
    }

    [Fact]
    public void AnImportFromAFolderWithoutSettingsSaysSoAndChangesNothing()
    {
        var target = Folder("new", "config.json");
        var elsewhere = Folder("elsewhere", "waypoints.json");

        var result = DataFolder.Import(target, elsewhere);

        Assert.False(result.Ok);
        Assert.Equal(0, result.Files);
        Assert.False(File.Exists(Path.Combine(target, "waypoints.json")));
    }

    [Fact]
    public void AnImportFromTheFolderItselfIsRefused()
    {
        var target = Folder("new", "config.json");
        Assert.False(DataFolder.Import(target, target).Ok);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}

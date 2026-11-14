using System.IO;
using System.Windows;
using System.Windows.Controls;
using PandoraOverlay.Games;

namespace PandoraOverlay.Tests;

/// <summary>The members a <see cref="FakeGame"/> can be told to throw from.</summary>
[Flags]
public enum FakeThrows
{
    None = 0,
    Title = 1 << 0,
    Pitch = 1 << 1,
    Attach = 1 << 2,
    OnReading = 1 << 3,
    Tile = 1 << 4,
    CreateTileArt = 1 << 5,
    CreateView = 1 << 6,
    Reset = 1 << 7,
}

/// <summary>
/// A game for the host's tests (A10): it records every call, can be told to
/// throw from any member or to answer any <see cref="AttachResult"/>, and
/// otherwise behaves as a real game should (it reads its save at Attach, its
/// Reset writes a fresh save Now). A reading is counted, not listed, so a
/// hundred thousand of them cost the fake nothing (the cost test measures the
/// host). Its view's root is made on first use, so a test that only asks for
/// a view needs no STA thread; its tile art is a real element and does.
/// </summary>
internal sealed class FakeGame : IGame
{
    public FakeGame(string id = "fake-game") => Id = id;

    public string Id { get; }

    public string TitleText { get; set; } = "Fake game";

    public string PitchText { get; set; } = "A game that records what it is asked.";

    public FakeThrows Throws { get; set; }

    public AttachResult Answer { get; set; } = AttachResult.Ready;

    /// <summary>Whether Attach reads the save first, as the Contract asks (a test turns it off to see the host read it itself).</summary>
    public bool ReadsAtAttach { get; set; } = true;

    /// <summary>Run inside Attach, after the read: a write, a fault, a tile change.</summary>
    public Action<IGameHost>? DuringAttach { get; set; }

    /// <summary>Run on each reading, after it is counted.</summary>
    public Action<IGameHost, DinoReading>? WhenRead { get; set; }

    /// <summary>Run inside CreateView, after its view is made: a game reporting a fault of its own (through its host) and still handing the view back.</summary>
    public Action<IGameHost>? DuringCreateView { get; set; }

    /// <summary>When set, CreateTileArt hands back this one element every time, as a game that keeps its picture would (the Contract allows it).</summary>
    public FrameworkElement? KeptTileArt { get; set; }

    /// <summary>What Reset writes, Now, at version 1.</summary>
    public string ResetPayload { get; set; } = "fresh";

    public GameTile TileToShow { get; set; } = new("Ready to play", GameTileTone.Neutral, "No runs yet", "New", CanReset: true);

    /// <summary>Every call but OnReading, in order, by member name.</summary>
    public List<string> Calls { get; } = new();

    public IGameHost? Host { get; private set; }

    public SaveRead ReadAtAttach { get; private set; }

    public int Readings { get; private set; }

    public DinoReading LastReading { get; private set; }

    public List<FakeGameView> Views { get; } = new();

    public string Title
    {
        get
        {
            Calls.Add(nameof(Title));
            ThrowIf(FakeThrows.Title);
            return TitleText;
        }
    }

    public string Pitch
    {
        get
        {
            Calls.Add(nameof(Pitch));
            ThrowIf(FakeThrows.Pitch);
            return PitchText;
        }
    }

    public AttachResult Attach(IGameHost host)
    {
        Calls.Add(nameof(Attach));
        Host = host;
        if (ReadsAtAttach) ReadAtAttach = host.Storage.Read();
        ThrowIf(FakeThrows.Attach);
        DuringAttach?.Invoke(host);
        return Answer;
    }

    public void OnReading(DinoReading reading)
    {
        Readings++;
        LastReading = reading;
        ThrowIf(FakeThrows.OnReading);
        WhenRead?.Invoke(Host!, reading);
    }

    public GameTile Tile()
    {
        Calls.Add(nameof(Tile));
        ThrowIf(FakeThrows.Tile);
        return TileToShow;
    }

    public FrameworkElement CreateTileArt()
    {
        Calls.Add(nameof(CreateTileArt));
        ThrowIf(FakeThrows.CreateTileArt);
        return KeptTileArt ?? new Border { Width = GameFrame.TileArtWidth, Height = GameFrame.TileArtHeight };
    }

    public IGameView CreateView()
    {
        Calls.Add(nameof(CreateView));
        ThrowIf(FakeThrows.CreateView);
        var view = new FakeGameView();
        Views.Add(view);
        DuringCreateView?.Invoke(Host!);
        return view;
    }

    public void Reset()
    {
        Calls.Add(nameof(Reset));
        ThrowIf(FakeThrows.Reset);
        Host?.Storage.Write(1, ResetPayload, SaveUrgency.Now);
    }

    private void ThrowIf(FakeThrows member)
    {
        // Not HasFlag: unoptimised (Debug) code can box for it, and the cost test
        // counts every byte a reading allocates.
        if ((Throws & member) != 0) throw new InvalidOperationException($"FakeGame was told to throw from {member}.");
    }
}

/// <summary>A view that records Pause, Resume and Dispose; its root, a plain Grid, is made on first use.</summary>
internal sealed class FakeGameView : IGameView
{
    private FrameworkElement? _root;

    public List<string> Calls { get; } = new();

    public FrameworkElement Root => _root ??= new Grid { Width = GameFrame.ViewWidth, Height = GameFrame.ViewHeight };

    public IReadOnlyList<string> LayoutStates { get; } = new[] { "only" };

    public void Pause() => Calls.Add(nameof(Pause));

    public void Resume() => Calls.Add(nameof(Resume));

    public void ShowLayoutState(string state) => Calls.Add($"{nameof(ShowLayoutState)} {state}");

    public void Dispose() => Calls.Add(nameof(Dispose));
}

/// <summary>A clock the test moves by hand.</summary>
internal sealed class TestGameClock : IGameClock
{
    public DateTime UtcNow { get; set; } = new(2026, 11, 14, 10, 0, 0, DateTimeKind.Utc);

    public TimeSpan Elapsed { get; set; }

    public void Advance(TimeSpan by)
    {
        if (by < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(by), "A monotonic clock never goes back.");
        Elapsed += by;
        UtcNow += by;
    }
}

/// <summary>A game's log lines, collected instead of written.</summary>
internal sealed class TestGameLog : IGameLog
{
    public List<string> Lines { get; } = new();

    public List<string> Warnings { get; } = new();

    public void Log(string fact) => Lines.Add(fact);

    public void Warning(string fact) => Warnings.Add(fact);
}

/// <summary>
/// A temporary games folder of the test's own, never the real one
/// (%AppData%\PandoraOverlay\games): the store is only ever handed this root.
/// Deleted after the test; a file the store made outside the root fails it.
/// </summary>
internal sealed class TempGamesRoot : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "PandoraOverlay.Tests", Guid.NewGuid().ToString("N"));

    /// <summary>The store's root, one level under the test's folder, so a file that escaped it would still be found.</summary>
    public string Root => Path.Combine(_base, "games");

    public string FolderOf(string id) => Path.Combine(Root, id);

    public string FileOf(string id, string name) => Path.Combine(Root, id, name);

    /// <summary>A file of the game's, written by hand (a damaged save, for one).</summary>
    public void Plant(string id, string name, string text)
    {
        Directory.CreateDirectory(FolderOf(id));
        File.WriteAllText(FileOf(id, name), text);
    }

    /// <summary>The payload of a good save.json, read with a store of its own; null when there is none.</summary>
    public string? SavedPayload(string id) => new GameSaveStore(Root, () => DateTime.UtcNow).Read(id) is { State: SaveReadState.Ok } read ? read.Payload : null;

    public void Dispose()
    {
        var strays = Directory.Exists(_base)
            ? Directory.EnumerateFileSystemEntries(_base, "*", SearchOption.AllDirectories)
                .Where(entry => entry != Root && !entry.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                .ToList()
            : new List<string>();
        try { Directory.Delete(_base, recursive: true); } catch { /* a temp folder; the next run uses another */ }
        if (strays.Count > 0) throw new InvalidOperationException("The store wrote outside its root: " + string.Join(", ", strays));
    }
}

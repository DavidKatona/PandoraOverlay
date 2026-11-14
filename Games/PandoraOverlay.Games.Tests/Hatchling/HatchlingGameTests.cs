using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// <see cref="HatchlingGame"/>, the one object the Games window talks to (task H08), on the
/// tests' host (a manual clock, the save in memory, A41's rethrowing Fault): Attach
/// against every kind of save, every action committed and saved Now before it returns
/// (A20), the readings that pay and those that must not (A6), the tile, Reset, and a
/// whole run played by the simplest player.
/// </summary>
public class HatchlingGameTests
{
    private static readonly Known SightKnows = (run, c) => Sight.Knows(run, Stats.Of(run), c);

    /// <summary>A game attached to <paramref name="saved"/> (or to no save), Ready.</summary>
    private static (HatchlingGame Game, FakeHost Host) Attached(GameState? saved = null, SaveReadState how = SaveReadState.Ok)
    {
        var host = new FakeHost();
        if (saved is not null) host.Memory.Stored = new SaveRead(how, HatchlingSave.Version, SaveFiles.PayloadOf(saved));
        var game = new HatchlingGame();
        Assert.Equal(AttachResult.Ready, game.Attach(host));
        return (game, host);
    }

    /// <summary>The state the storage's last write holds, read back as the next launch would.</summary>
    private static GameState LastWritten(FakeHost host)
    {
        var (version, payload, _) = host.Memory.Writes[^1];
        var loaded = HatchlingSave.Schema.Load(version, payload);
        Assert.Equal(SaveOutcome.Ok, loaded.Outcome);
        return HatchlingSave.To(loaded.State!);
    }

    /// <summary>One more write than before, with Now, and it holds exactly the game's state: saved before shown (A20).</summary>
    private static void AssertSavedNow(HatchlingGame game, FakeHost host, int writesBefore)
    {
        Assert.Equal(writesBefore + 1, host.Memory.Writes.Count);
        Assert.Equal(SaveUrgency.Now, host.Memory.Writes[^1].Urgency);
        Assert.Equal(HatchlingSave.Version, host.Memory.Writes[^1].Version);
        var differences = Deep.Differences(game.State, LastWritten(host));
        Assert.True(differences.Count == 0, Deep.Show(differences));
    }

    private static DinoReading Reading(string species, int permille) => new(true, species, "Male", permille / 1000.0);

    private static void Feed(HatchlingGame game, FakeHost host, DinoReading reading)
    {
        host.ManualClock.Advance(TimeSpan.FromSeconds(3));
        game.OnReading(reading);
    }

    /// <summary>A Camp state with a run behind it and something to spend.</summary>
    private static GameState OnCamp()
    {
        var state = Settle.BackToCamp(SaveSamples.RunOver());
        state.Wallet.Earn(5000);
        return state;
    }

    // ===== The game as the Contract sees it ===================================================

    [Fact]
    public void ItsIdTitleAndPitch()
    {
        var game = new HatchlingGame();
        Assert.Equal("hatchling", game.Id);
        Assert.Matches(new Regex("^[a-z0-9][a-z0-9-]{0,30}[a-z0-9]$"), game.Id);
        Assert.Equal(HatchlingText.Title, game.Title);
        Assert.Equal(HatchlingText.Pitch, game.Pitch);
    }

    [Fact]
    public void TheConstructorSetsNoStateAndLoadsNoArt()
    {
        var game = new HatchlingGame();
        Assert.Throws<InvalidOperationException>(() => game.State);
        Assert.Null(ArtOf(game));
        Assert.False(game.HasReading);
        Assert.Equal(DinoReading.NotInGame, game.LastReading);
    }

    private static object? ArtOf(HatchlingGame game) =>
        typeof(HatchlingGame).GetField("_art", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(game);

    // ===== Attach (A7, A9, A34) ===============================================================

    [Fact]
    public void NoSaveIsAFreshStateAndWritesNothing()
    {
        var (game, host) = Attached();
        var differences = Deep.Differences(GameState.Fresh(ManualClock.Start), game.State);
        Assert.True(differences.Count == 0, Deep.Show(differences));
        Assert.Empty(host.Memory.Writes);
        Assert.Contains("attached: fresh", host.Collected.Lines);
    }

    [Theory]
    [InlineData("fresh", SaveReadState.Ok)]
    [InlineData("mid-run", SaveReadState.Ok)]
    [InlineData("run-over", SaveReadState.Ok)]
    [InlineData("everything", SaveReadState.Ok)]
    [InlineData("mid-run", SaveReadState.FromBackup)]
    public void ASaveIsItsStateAndWritesNothing(string sample, SaveReadState how)
    {
        var saved = SaveSamples.Named(sample);
        var (game, host) = Attached(saved, how);
        var differences = Deep.Differences(saved, game.State);
        Assert.True(differences.Count == 0, Deep.Show(differences));
        Assert.Empty(host.Memory.Writes);
    }

    [Fact]
    public void TheLogSaysWhatWasAttachedAndNothingOfThePlayers()
    {
        var (_, host) = Attached(SaveSamples.MidRun());
        Assert.Contains("attached: version 1, a run under way", host.Collected.Lines);
        var (_, backup) = Attached(SaveSamples.RunOver(), SaveReadState.FromBackup);
        Assert.Contains("attached: version 1, a run ended, from the backup", backup.Collected.Lines);
    }

    /// <summary>A save the host could not read is held: the schema is never asked, whatever a damaged read may carry with it (A34).</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void AnUnreadableSaveIsHeldAndTheSchemaNeverAsked(int version, bool payload)
    {
        var host = new FakeHost();
        host.Memory.Stored = new SaveRead(SaveReadState.Unreadable, version, payload ? SaveFiles.PayloadOf(SaveSamples.MidRun()) : null);
        var game = new HatchlingGame();
        Assert.Equal(AttachResult.SaveUnreadable, game.Attach(host));
        Assert.Throws<InvalidOperationException>(() => game.State);
        Assert.Empty(host.Memory.Writes);
    }

    [Fact]
    public void ASaveOfANewerVersionIsKeptForThatVersion()
    {
        var host = new FakeHost();
        host.Memory.Stored = new SaveRead(SaveReadState.Ok, HatchlingSave.Version + 1, "{\"Whatever\":\"a later build wrote\"}");
        var game = new HatchlingGame();
        Assert.Equal(AttachResult.SaveFromNewerVersion, game.Attach(host));
        Assert.Throws<InvalidOperationException>(() => game.State);
        Assert.Empty(host.Memory.Writes);
    }

    [Theory]
    [InlineData("not a save")]
    [InlineData("")]
    [InlineData("{\"Selected\":\"Velociraptor\"}")]
    public void AnUnreadableTextIsSaveUnreadable(string text)
    {
        var host = new FakeHost();
        host.Memory.Stored = new SaveRead(SaveReadState.Ok, HatchlingSave.Version, text);
        var game = new HatchlingGame();
        Assert.Equal(AttachResult.SaveUnreadable, game.Attach(host));
        Assert.Empty(host.Memory.Writes);
    }

    [Fact]
    public void ASaveItsOwnCheckRefusesIsSaveUnreadable()
    {
        var data = HatchlingSave.From(SaveSamples.MidRun());
        data.Run!.You.Health = 1.5;
        var host = new FakeHost();
        host.Memory.Stored = new SaveRead(SaveReadState.Ok, HatchlingSave.Version, HatchlingSave.Schema.Save(data));
        Assert.Equal(AttachResult.SaveUnreadable, new HatchlingGame().Attach(host));
        Assert.Empty(host.Memory.Writes);
    }

    /// <summary>Server play assumes three open entries and never tops the journal up, so Attach fills a short one, from its own generator, without writing.</summary>
    [Fact]
    public void AShortJournalIsFilledAtAttachAndNotWritten()
    {
        var saved = SaveSamples.MidRun();
        saved.Journal.Open.RemoveRange(1, 2);
        var (game, host) = Attached(saved);
        Assert.Equal(Tuning.JournalEntries, game.State.Journal.Open.Count);
        Assert.Equal("Maiasaura", game.State.Journal.Open[0].Species);
        Assert.Empty(host.Memory.Writes);

        var expected = HatchlingSave.To(HatchlingSave.From(saved));
        Hatchling.Journal.Fill(expected);
        var differences = Deep.Differences(expected, game.State);
        Assert.True(differences.Count == 0, Deep.Show(differences));
    }

    /// <summary>Every fixture attaches Ready and plays on: a save of every shape the format has is a game that runs.</summary>
    [Theory]
    [InlineData("fresh")]
    [InlineData("mid-run")]
    [InlineData("run-over")]
    [InlineData("everything")]
    public void EveryFixtureAttachesAndPlays(string name)
    {
        var path = SaveFiles.FixturePath(name);
        Assert.True(File.Exists(path), SaveFiles.Missing(path));
        var host = new FakeHost();
        host.Memory.Stored = new SaveRead(SaveReadState.Ok, HatchlingSave.Version, File.ReadAllText(path));
        var game = new HatchlingGame();
        Assert.Equal(AttachResult.Ready, game.Attach(host));
        _ = game.Tile();

        if (game.State.Run?.Over is not null) Assert.True(game.BackToCamp());
        if (game.State.Run is null) Assert.True(game.StartRun());
        for (var turn = 0; turn < 30 && game.State.Run!.Over is null; turn++)
        {
            var writes = host.Memory.Writes.Count;
            var result = game.Act(game.State.Run.Offer is not null ? new PlayerAction.Pick(0) : new PlayerAction.Rest());
            Assert.NotNull(result);
            AssertSavedNow(game, host, writes);
        }
    }

    // ===== Saved before shown (A20) =============================================================

    /// <summary>Each action a view can ask for, from the state it needs, and whether it was taken.</summary>
    private static readonly Dictionary<string, (Func<GameState> From, Func<HatchlingGame, bool> Do)> Actions = new()
    {
        ["redraw"] = (SaveSamples.MidRun, g => g.Act(new PlayerAction.Redraw()) is not null),
        ["pick"] = (SaveSamples.MidRun, g => g.Act(new PlayerAction.Pick(1)) is not null),
        ["leave with an offer open"] = (SaveSamples.MidRun, g => g.Act(new PlayerAction.Leave()) is not null),
        ["pace"] = (Picked, g => g.Act(new PlayerAction.SetPace(Pace.Sprint)) is not null),
        ["rest"] = (Picked, g => g.Act(new PlayerAction.Rest()) is not null),
        ["eat"] = (Picked, g => g.Act(new PlayerAction.Eat()) is not null),
        ["drink"] = (Picked, g => g.Act(new PlayerAction.Drink()) is not null),
        ["move"] = (Picked, g => g.Act(new PlayerAction.Move(Moves.Targets(g.State.Run!, Stats.Of(g.State.Run!))[0])) is not null),
        ["attack"] = (Picked, g => g.Act(new PlayerAction.Attack(5)) is not null),
        ["leave"] = (Picked, g => g.Act(new PlayerAction.Leave()) is not null),
        ["back to camp"] = (SaveSamples.RunOver, g => g.BackToCamp()),
        ["start a run"] = (OnCamp, g => g.StartRun()),
        ["select"] = (OnCamp, g => g.Select("Maiasaura")),
        ["danger"] = (OnCamp, g => g.SetDanger(3)),
        ["fast"] = (OnCamp, g => g.SetFast(true)),
        ["buy a trait"] = (OnCamp, g => g.BuyTrait(Trait.ThickHide)),
        ["unlock"] = (OnCamp, g => g.Unlock("Maiasaura")),
        ["swap"] = (OnCamp, g => g.Swap(2)),
        ["reset"] = (SaveSamples.MidRun, g => { g.Reset(); return true; }),
    };

    /// <summary>The mid-run sample with its offer answered: a run that takes every move.</summary>
    private static GameState Picked() => HatchlingGame.Advance(SaveSamples.MidRun(), new PlayerAction.Pick(0))!.Value.State;

    public static IEnumerable<object[]> ActionNames => Actions.Keys.Select(k => new object[] { k });

    /// <summary>
    /// After an action returns, the storage's last write holds the new state, with Now:
    /// what the view goes on to show is already on disk (A20, A8), and a click on the
    /// game, a closed window or a crash in the playback loses nothing (G17).
    /// </summary>
    [Theory]
    [MemberData(nameof(ActionNames))]
    public void EveryActionIsSavedNowBeforeItReturns(string name)
    {
        var (from, act) = Actions[name];
        var (game, host) = Attached(from());
        var writes = host.Memory.Writes.Count;
        Assert.True(act(game), $"the action '{name}' was refused");
        AssertSavedNow(game, host, writes);
    }

    [Fact]
    public void ARefusedActionWritesNothing()
    {
        var (game, host) = Attached(SaveSamples.MidRun());
        Assert.Null(game.Act(new PlayerAction.Rest()));                   // an offer is open: only a pick, a redraw or leaving
        Assert.Null(game.Act(new PlayerAction.Pick(4)));                   // there is no fifth choice
        Assert.False(game.StartRun());                                     // a run exists (H20)
        Assert.False(game.BackToCamp());                                   // it goes on
        Assert.False(game.Select("Velociraptor"));
        Assert.False(game.SetDanger(5));                                   // above the highest unlocked
        Assert.False(game.BuyTrait(Trait.SecondWind));                     // complete
        Assert.False(game.Unlock("Omniraptor"));                           // unlocked already
        Assert.False(game.Swap(3));
        Assert.Empty(host.Memory.Writes);
    }

    /// <summary>A redraw is paid from the wallet first, and refused when it is short (H14).</summary>
    [Fact]
    public void ARedrawIsPaidFirstAndRefusedWhenShort()
    {
        var (game, host) = Attached(SaveSamples.MidRun());
        var price = Offers.RedrawPrice(game.State.Run!);
        Assert.Equal(Tuning.RedrawPrice * 2, price);                       // the free one spent, one paid already
        var balance = game.State.Wallet.Balance;
        Assert.NotNull(game.Act(new PlayerAction.Redraw()));
        Assert.Equal(balance - price, game.State.Wallet.Balance);
        Assert.Equal(2, game.State.Run!.Redraws);

        var poor = SaveSamples.MidRun();
        poor.Wallet = new Wallet(price - 1, 5375);
        var (short_, shortHost) = Attached(poor);
        var offer = short_.State.Run!.Offer!.ToList();
        Assert.Null(short_.Act(new PlayerAction.Redraw()));
        Assert.Equal(offer, short_.State.Run!.Offer);
        Assert.Equal(price - 1, short_.State.Wallet.Balance);
        Assert.Empty(shortHost.Memory.Writes);
    }

    /// <summary>A run that just ended is settled in the same step, so its summary shows what is already banked (H20).</summary>
    [Fact]
    public void ARunThatEndsIsSettledInTheSameStep()
    {
        var (game, host) = Attached(SaveSamples.MidRun());
        var earned = game.State.Wallet.Earned;
        var runs = game.State.Totals.Runs;
        var result = game.Act(new PlayerAction.Leave());
        Assert.NotNull(result);
        var over = game.State.Run!.Over!;
        Assert.True(over.Settled);
        Assert.Equal(EndReason.Left, over.Reason);
        Assert.Equal(runs + 1, game.State.Totals.Runs);
        Assert.True(game.State.Wallet.Earned >= earned + over.Total);
        Assert.True(LastWritten(host).Run!.Over!.Settled);
        Assert.Contains(host.Collected.Lines, l => l.StartsWith("run ended: left, turn 74", StringComparison.Ordinal));
    }

    /// <summary>What the view plays back and shows is what the save holds: the result's After is the run as committed.</summary>
    [Fact]
    public void TheResultsAfterIsTheRunAsSaved()
    {
        var (game, _) = Attached(Picked());
        var result = game.Act(new PlayerAction.Rest())!;
        Assert.Same(game.State.Run, result.After);
        var (left, _) = Attached(Picked());
        var ended = left.Act(new PlayerAction.Leave())!;
        Assert.Same(left.State.Run, ended.After);
        Assert.True(ended.After.Over!.Settled);
    }

    /// <summary>A move that passed a turn stores the turn list the script reads from it (H28); a choice that takes none keeps the list as last shown.</summary>
    [Fact]
    public void TheTurnListIsTheScriptsAndKeptByAChoice()
    {
        var (game, _) = Attached(Picked());
        var before = game.State.Run!.LastTurn.ToList();
        game.Act(new PlayerAction.SetPace(Pace.Sprint));
        Assert.Equal(before, game.State.Run!.LastTurn);

        var result = game.Act(new PlayerAction.Rest())!;
        Assert.Equal(TurnScripts.Build(result, SightKnows).Rows, game.State.Run!.LastTurn);
        Assert.NotEqual(before, game.State.Run.LastTurn);
    }

    [Fact]
    public void ARunStartsWithTheStartsTurnList()
    {
        var (game, host) = Attached();
        Assert.True(game.StartRun());
        var run = game.State.Run!;
        Assert.Equal(0, run.Turn);
        Assert.Equal(TurnScripts.RowsAtStart(run, SightKnows), run.LastTurn);
        Assert.Equal(GameState.Fresh(ManualClock.Start).NextSeed, run.Seed);
        Assert.Contains("run started: danger 1", host.Collected.Lines);
    }

    /// <summary>A save the host refuses (held, A7) costs the game nothing: every action still answers, nothing throws.</summary>
    [Fact]
    public void AHeldSaveThrowsFromNoAction()
    {
        var host = new FakeHost();
        var game = new HatchlingGame();
        Assert.Equal(AttachResult.Ready, game.Attach(host));
        host.Memory.Held = true;
        Assert.True(game.SetFast(true));
        Assert.True(game.StartRun());
        for (var turn = 0; turn < 5 && game.State.Run!.Over is null; turn++)
            Assert.NotNull(game.Act(game.State.Run.Offer is not null ? new PlayerAction.Pick(0) : new PlayerAction.Rest()));
        Assert.NotNull(game.Act(new PlayerAction.Leave()));
        Assert.True(game.BackToCamp());
        game.OnReading(Reading("Maiasaura", 300));
        Feed(game, host, Reading("Maiasaura", 302));
        game.Reset();
        Assert.Equal(SaveReadState.None, host.Memory.Stored.State);       // every write refused
        Assert.NotEmpty(host.Memory.Writes);
    }

    /// <summary>A save the check would refuse is never written: Commit throws (a defect, which stops the game, A10), the last good save stays on disk, nothing is replaced.</summary>
    [Fact]
    public void AStateItsOwnCheckRefusesIsNeverWritten()
    {
        var (game, host) = Attached(OnCamp());
        var broken = game.State.Clone();
        broken.Selected = "Velociraptor";
        typeof(HatchlingGame).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(game, broken);
        Assert.Throws<InvalidOperationException>(() => game.SetFast(!broken.Fast));
        Assert.Empty(host.Memory.Writes);
        Assert.Same(broken, game.State);
    }

    // ===== Readings (A6, H10, H18) =============================================================

    /// <summary>
    /// A stream of readings 3 s apart growing 0.1% each pays, step by step, Soon; the
    /// step that earns a star (50% of seen growth) is written Now. A first reading
    /// pays nothing. (A Tyrannosaurus: the journal never asks for one, and it stays
    /// under Adult, so a star is the only news.)
    /// </summary>
    [Fact]
    public void APayingStreamWritesSoonAndNowAtAStar()
    {
        var (game, host) = Attached();
        game.OnReading(Reading("Tyrannosaurus", 100));
        Assert.Empty(host.Memory.Writes);
        Assert.Equal(0, game.State.Wallet.Balance);

        Feed(game, host, Reading("Tyrannosaurus", 101));
        Assert.True(game.State.Wallet.Balance > 0);
        Assert.Equal(SaveUrgency.Soon, Assert.Single(host.Memory.Writes).Urgency);

        for (var permille = 102; permille <= 600; permille++)
        {
            var before = game.State.Wallet.Balance;
            Feed(game, host, Reading("Tyrannosaurus", permille));
            Assert.True(game.State.Wallet.Balance >= before);
        }
        Assert.Equal(500, host.Memory.Writes.Count);
        Assert.All(host.Memory.Writes.Take(499), w => Assert.Equal(SaveUrgency.Soon, w.Urgency));
        Assert.Equal(SaveUrgency.Now, host.Memory.Writes[^1].Urgency);
        Assert.Equal(1, game.State.Stars("Tyrannosaurus"));
        var differences = Deep.Differences(game.State, LastWritten(host));
        Assert.True(differences.Count == 0, Deep.Show(differences));
    }

    [Fact]
    public void AJumpPaysNothing()
    {
        var (game, host) = Attached();
        game.OnReading(Reading("Maiasaura", 100));
        Feed(game, host, Reading("Maiasaura", 200));                       // a stored dino swapped in: no dino grows 10% in 3 s
        Assert.Empty(host.Memory.Writes);
        Assert.Equal(0, game.State.Wallet.Balance);
        Feed(game, host, Reading("Maiasaura", 201));                       // the new baseline goes on
        Assert.Single(host.Memory.Writes);
        Assert.Equal(0.1, game.State.Record("Maiasaura").Grown, 9);
    }

    [Fact]
    public void NotInGameClearsTheLife()
    {
        var (game, host) = Attached();
        game.OnReading(Reading("Maiasaura", 100));
        Feed(game, host, DinoReading.NotInGame);
        Feed(game, host, Reading("Maiasaura", 101));                       // a first sight again
        Assert.Empty(host.Memory.Writes);
        Feed(game, host, Reading("Maiasaura", 102));
        Assert.Single(host.Memory.Writes);
    }

    /// <summary>A restart is a new SeenGrowth: nothing bridges it, so nothing is paid for the time between (the public promise).</summary>
    [Fact]
    public void ARestartPaysNothingForTheGap()
    {
        var (first, host) = Attached();
        first.OnReading(Reading("Maiasaura", 100));
        Feed(first, host, Reading("Maiasaura", 101));
        var paid = first.State.Wallet.Balance;
        Assert.True(paid > 0);

        var second = new HatchlingGame();                                  // the next launch, on the same save
        Assert.Equal(AttachResult.Ready, second.Attach(host));
        Feed(second, host, Reading("Maiasaura", 150));                     // grown while the overlay was closed
        Assert.Equal(paid, second.State.Wallet.Balance);
        Assert.Equal(0.1, second.State.Record("Maiasaura").Grown, 9);
        Feed(second, host, Reading("Maiasaura", 151));
        Assert.Equal(0.2, second.State.Record("Maiasaura").Grown, 9);
    }

    [Fact]
    public void ASpeciesTheGameDoesNotKnowCostsNothing()
    {
        var (game, host) = Attached();
        game.OnReading(Reading("Spinosaurus", 100));
        for (var permille = 101; permille < 200; permille++) Feed(game, host, Reading("Spinosaurus", permille));
        Assert.Empty(host.Memory.Writes);
        Assert.Equal(0, game.State.Wallet.Balance);
    }

    /// <summary>100,000 readings that make no step: under a second, no write, and (warmed up) next to no allocation (A5: it costs every poll).</summary>
    [Fact]
    public void AHundredThousandReadingsWithNoStepAreCheap()
    {
        var (game, host) = Attached();
        var reading = Reading("Maiasaura", 420);
        var changes = 0;
        game.Changed += () => changes++;
        for (var i = 0; i < 1000; i++) Feed(game, host, reading);

        var watch = Stopwatch.StartNew();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100_000; i++) Feed(game, host, reading);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        watch.Stop();

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"100,000 readings took {watch.Elapsed.TotalMilliseconds:0} ms");
        Assert.Empty(host.Memory.Writes);
        Assert.True(allocated < 64 * 1024, $"100,000 readings allocated {allocated} bytes");
        Assert.Equal(1, changes);                                          // the first reading only: none since differed
    }

    [Fact]
    public void ChangedIsRaisedWhenAReadingChangedSomething()
    {
        var (game, host) = Attached();
        var changes = 0;
        game.Changed += () => changes++;
        Assert.False(game.HasReading);
        game.OnReading(DinoReading.NotInGame);
        Assert.True(game.HasReading);
        Assert.Equal(1, changes);                                          // the Journal's line: no reading yet, then not in game
        Feed(game, host, DinoReading.NotInGame);
        Assert.Equal(1, changes);
        Feed(game, host, Reading("Maiasaura", 300));
        Assert.Equal(2, changes);
        Assert.Equal(Reading("Maiasaura", 300), game.LastReading);
        Feed(game, host, Reading("Maiasaura", 300));
        Assert.Equal(2, changes);
        Feed(game, host, Reading("Maiasaura", 301));                       // it paid
        Assert.Equal(3, changes);
    }

    [Fact]
    public void ChangedIsNotRaisedByTheViewsOwnActions()
    {
        var (game, _) = Attached();
        var changes = 0;
        game.Changed += () => changes++;
        game.SetFast(true);
        game.StartRun();
        game.Act(new PlayerAction.Rest());
        Assert.Equal(0, changes);
        game.Reset();
        Assert.Equal(1, changes);                                          // the window's, not a view's
    }

    // ===== The tile =============================================================================

    [Fact]
    public void TheTileOfAFreshSave()
    {
        var (game, _) = Attached();
        var tile = game.Tile();
        Assert.Equal(HatchlingText.TileIdle(2, 0), tile.Status);
        Assert.Equal("2 of 21 species · 0 amber", tile.Status);
        Assert.Equal(GameTileTone.Neutral, tile.Tone);
        Assert.Equal(HatchlingText.TileDetail(0, AchievementList.All.Count), tile.Detail);
        Assert.Equal(HatchlingText.Tag, tile.Tag);
        Assert.False(tile.CanReset);
    }

    [Fact]
    public void TheTileOfARunUnderWay()
    {
        var (game, _) = Attached(SaveSamples.MidRun());
        var tile = game.Tile();
        Assert.Equal(HatchlingText.TileUnderWay(Stages.Elder, "Omniraptor"), tile.Status);
        Assert.Equal(GameTileTone.Good, tile.Tone);
        Assert.Equal(HatchlingText.TileDetail(7, 24), tile.Detail);
        Assert.True(tile.CanReset);
    }

    [Fact]
    public void TheTileOfARunEnded()
    {
        var (game, _) = Attached(SaveSamples.RunOver());
        Assert.Equal(HatchlingText.TileEnded, game.Tile().Status);
        Assert.Equal(GameTileTone.Neutral, game.Tile().Tone);
    }

    /// <summary>An achievement this build does not know is kept in the save, never counted: "24 of 24", not "25 of 24".</summary>
    [Fact]
    public void TheTileCountsOnlyTheAchievementsItKnows()
    {
        var (game, _) = Attached(SaveSamples.Everything());
        Assert.Equal(HatchlingText.TileDetail(24, 24), game.Tile().Detail);
        Assert.Contains("a-later-achievement", game.State.Achievements.Unlocked);
    }

    [Fact]
    public void TileChangedOnlyWhenWhatItSaysChanged()
    {
        var (game, host) = Attached();
        game.Select("Herrerasaurus");
        game.SetFast(true);
        game.SetDanger(1);
        Assert.Equal(0, host.TileChanges);                                 // Camp's choices: not on the tile, not progress

        Assert.True(game.StartRun());
        Assert.Equal(1, host.TileChanges);                                 // under way
        game.OnReading(Reading("Maiasaura", 300));
        Feed(game, host, Reading("Maiasaura", 301));                       // pays, but the tile shows the run
        Assert.Equal(1, host.TileChanges);
        game.Act(new PlayerAction.Leave());
        Assert.Equal(2, host.TileChanges);                                 // ended
        game.BackToCamp();
        Assert.Equal(3, host.TileChanges);                                 // the wallet's line again
        Feed(game, host, Reading("Maiasaura", 302));
        Assert.Equal(4, host.TileChanges);                                 // its amber rose
        Feed(game, host, Reading("Maiasaura", 302));
        Assert.Equal(4, host.TileChanges);
    }

    // ===== Reset =================================================================================

    [Fact]
    public void ResetWritesAFreshSaveNow()
    {
        var (game, host) = Attached(SaveSamples.Everything());
        Assert.True(game.Tile().CanReset);
        game.Reset();
        AssertSavedNow(game, host, 0);
        var differences = Deep.Differences(GameState.Fresh(host.Clock.UtcNow), LastWritten(host));
        Assert.True(differences.Count == 0, Deep.Show(differences));
        Assert.False(game.Tile().CanReset);
        Assert.Contains("reset: a fresh save", host.Collected.Lines);
    }

    /// <summary>"Start fresh" on a held save: the host sets the files aside, then calls Reset on the instance whose Attach could not read (A7, A38).</summary>
    [Fact]
    public void ResetWorksOnAnInstanceThatCouldNotRead()
    {
        var host = new FakeHost();
        host.Memory.Stored = new SaveRead(SaveReadState.Unreadable, 0, null);
        var game = new HatchlingGame();
        Assert.Equal(AttachResult.SaveUnreadable, game.Attach(host));
        game.Reset();
        AssertSavedNow(game, host, 0);
        Assert.False(game.Tile().CanReset);
    }

    [Fact]
    public void ResetBeforeAttachThrows() =>
        Assert.Throws<InvalidOperationException>(() => new HatchlingGame().Reset());

    /// <summary>Reset makes a new SeenGrowth: the life watched before it pays nothing after it.</summary>
    [Fact]
    public void ResetForgetsTheLifeItWasWatching()
    {
        var (game, host) = Attached();
        game.OnReading(Reading("Maiasaura", 100));
        Feed(game, host, Reading("Maiasaura", 101));
        game.Reset();
        var writes = host.Memory.Writes.Count;
        Feed(game, host, Reading("Maiasaura", 102));
        Assert.Equal(writes, host.Memory.Writes.Count);
        Assert.Equal(0, game.State.Wallet.Balance);
    }

    // ===== A whole run ===========================================================================

    /// <summary>
    /// The simplest player (rest every turn, take the first instinct offered) plays a
    /// run to its end through Act: it ends, it is settled once (its pay banked once),
    /// nothing more can be done in it, and Back to camp forgets it.
    /// </summary>
    [Fact]
    public void TheSimplestPlayerPlaysARunToItsEnd()
    {
        var (game, host) = Attached();
        Assert.True(game.StartRun());
        var turns = 0;
        TurnResult? last = null;
        while (game.State.Run!.Over is null && turns++ < 2000)
        {
            var writes = host.Memory.Writes.Count;
            last = game.Act(game.State.Run.Offer is not null ? new PlayerAction.Pick(0) : new PlayerAction.Rest());
            Assert.NotNull(last);
            AssertSavedNow(game, host, writes);
        }

        var state = game.State;
        var over = state.Run!.Over!;
        Assert.True(over.Settled);
        // The last turn's list as stored is the one the view builds from what Act returned.
        Assert.Equal(TurnScripts.Build(last!, SightKnows).Rows, state.Run.LastTurn);
        Assert.Equal(1, state.Totals.Runs);
        var achievementsPay = over.Earned.Sum(id => AchievementList.All.Single(a => a.Id == id).Reward is AchievementReward.Currency c ? c.Amount : 0);
        Assert.Equal(over.Total + achievementsPay, state.Wallet.Earned);
        Assert.Contains(host.Collected.Lines, l => l.StartsWith("run ended: ", StringComparison.Ordinal));

        var writesAtEnd = host.Memory.Writes.Count;
        Assert.Null(game.Act(new PlayerAction.Rest()));
        Assert.Null(game.Act(new PlayerAction.Leave()));
        Assert.Equal(writesAtEnd, host.Memory.Writes.Count);
        Assert.Equal(1, game.State.Totals.Runs);

        Assert.True(game.BackToCamp());
        Assert.Null(game.State.Run);
        Assert.Equal(1, game.State.Totals.Runs);
        Assert.Equal(over.Total + achievementsPay, game.State.Wallet.Earned);
        Assert.False(game.BackToCamp());
        Assert.Equal(HatchlingText.TileIdle(2, game.State.Wallet.Balance), game.Tile().Status);
    }

    // ===== The art and the view ==================================================================

    /// <summary>The art is loaded once, at the first picture: never in the constructor or in Attach (a game's constructor does no IO).</summary>
    [Fact]
    public void TheArtIsLoadedOnceAtTheFirstPicture() => Sta.Run(() =>
    {
        var (game, _) = Attached();
        Assert.Null(ArtOf(game));
        var art = game.CreateTileArt();
        art.Measure(new Size(1000, 1000));
        Assert.Equal(new Size(GameFrame.TileArtWidth, GameFrame.TileArtHeight), art.DesiredSize);
        var book = ArtOf(game);
        Assert.NotNull(book);
        game.CreateTileArt();
        Assert.Same(book, ArtOf(game));
    });

    [Fact]
    public void TheViewIsNotBuiltYet() =>
        Assert.Throws<NotSupportedException>(() => Attached().Game.CreateView());

    // ===== The log (constraint 4) ================================================================

    /// <summary>The log holds facts and counts only: never a species, nothing of the player's.</summary>
    [Fact]
    public void TheLogNamesNoSpecies()
    {
        var (game, host) = Attached(SaveSamples.Everything());
        game.BackToCamp();
        game.Select("Maiasaura");
        game.StartRun();
        for (var turn = 0; turn < 2000 && game.State.Run!.Over is null; turn++)
            game.Act(game.State.Run.Offer is not null ? new PlayerAction.Pick(0) : new PlayerAction.Rest());
        Assert.NotNull(game.State.Run!.Over);
        game.OnReading(Reading("Gallimimus", 300));
        Feed(game, host, Reading("Gallimimus", 305));
        game.Reset();
        var (_, other) = Attached(SaveSamples.MidRun(), SaveReadState.FromBackup);

        var lines = host.Collected.Lines.Concat(other.Collected.Lines).ToList();
        Assert.NotEmpty(lines);
        foreach (var line in lines)
            foreach (var species in SpeciesTable.All)
                Assert.DoesNotContain(species.Name, line, StringComparison.OrdinalIgnoreCase);
    }
}

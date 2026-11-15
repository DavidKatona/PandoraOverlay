using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;
using Xunit;
using Xunit.Abstractions;
using static PandoraOverlay.Games.Tests.Scenes;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// A turn's cues as the Run page plays them (H96 to H100, A79), on the playback tests'
/// machinery and their 200 seeded turns, the host a <see cref="FakeHost"/> whose
/// <see cref="FakeSound"/> records what the game handed it: played to its end at Normal
/// and at Fast, exactly each beat's cues in order; skipped at tick 0 and every tenth
/// tick, nothing of the beats it finished and then at most the skip's one; paused,
/// nothing until resumed; and nothing for a layout state, a view made over a run or a
/// summary, or a reading.
/// </summary>
[Collection(LoopTimers.Name)]
public class PlaybackSoundTests
{
    private readonly ITestOutputHelper _out;

    public PlaybackSoundTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// The beat's creature is the apex: told here from the turn's creatures (the type),
    /// apart from the planner's own way (its ring), so the test does not repeat the code.
    /// </summary>
    private static bool IsApex(TurnResult turn, Beat beat) =>
        beat.Actor == BeatActor.Creature && beat.Creature > 0
        && (turn.After.Creatures.Find(c => c.Id == beat.Creature) ?? turn.Before.Creatures.Find(c => c.Id == beat.Creature)) is { Kind: CreatureKind.Predator } c
        && c.Type == Tuning.Apex;

    /// <summary>
    /// Every cue of the turn, in order, with the playback's advance (1-based, at speed 1)
    /// that plays it: a beat begins on the advance after the one its last beat ended on
    /// (A35), and its <c>Begin</c> (tick 0) and its first tick come on that one advance.
    /// </summary>
    private static List<(int Advance, Cue Cue)> Expected(TurnResult turn, TurnScript script)
    {
        var cues = new List<(int, Cue)>();
        var start = 0;
        foreach (var beat in script.Beats)
        {
            foreach (var s in BeatSounds.Of(beat, IsApex(turn, beat)))
                cues.Add((start + Math.Max(s.Tick, 1), s.Cue));
            start += beat.Ticks;
        }
        return cues;
    }

    /// <summary>The last beat begun after <paramref name="advances"/> advances at speed 1, -1 for none.</summary>
    private static int LastBegun(TurnScript script, int advances)
    {
        var last = -1;
        var start = 0;
        for (var i = 0; i < script.Beats.Count; i++)
        {
            if (start <= advances - 1) last = i;
            start += script.Beats[i].Ticks;
        }
        return last;
    }

    private static List<string> Names(IEnumerable<Cue> cues) => cues.Select(Cues.FileOf).ToList();

    private static List<string> Since(FakeHost host, int from) => host.Sound.Played.Skip(from).ToList();

    // ===== The 200 seeded turns =====================================================================

    [Fact]
    public void RunToTheEndPlaysEachBeatsCuesInOrderAtNormalAndAtFast() => Sta.Run(() =>
    {
        var turns = TurnPlaybackTests.Seeded(200);
        var cues = 0;
        foreach (var (saved, turn, script) in turns)
        {
            var (game, host) = ViewHarness.Attached(saved);
            using var view = ViewHarness.ViewOf(game);
            var page = view.RunPage;
            var what = $"turn {turn.Before.Turn} of a {turn.Before.Species} at danger {turn.Before.Danger}";
            var expected = Names(Expected(turn, script).Select(e => e.Cue));
            Assert.Empty(host.Sound.Played);   // the view made over the run sounds nothing (H100)

            var from = host.Sound.Played.Count;
            page.Play(turn);
            var advances = 0;
            while (page.Playing && advances < 5000) { view.ThePlayback.Advance(); advances++; }
            Assert.True(expected.SequenceEqual(Since(host, from)), $"{what}: at Normal played {string.Join(",", Since(host, from))}, the beats' are {string.Join(",", expected)}");

            page.FastClicked(true);
            from = host.Sound.Played.Count;
            page.Play(turn);
            Assert.Equal(2, view.ThePlayback.Speed);
            while (page.Playing && advances < 10000) { view.ThePlayback.Advance(); advances++; }
            Assert.True(expected.SequenceEqual(Since(host, from)), $"{what}: at Fast played {string.Join(",", Since(host, from))}, the beats' are {string.Join(",", expected)}");
            page.FastClicked(false);
            cues += expected.Count;
        }
        _out.WriteLine($"200 turns, {cues} cues each played once at Normal and once at Fast, in order");
    });

    [Fact]
    public void ASkipPlaysNothingOfTheBeatsItFinishesAndThenAtMostItsOneCue() => Sta.Run(() =>
    {
        var turns = TurnPlaybackTests.Seeded(200);
        var skips = 0;
        var skipCues = new Dictionary<string, int>();
        foreach (var (saved, turn, script) in turns)
        {
            var (game, host) = ViewHarness.Attached(saved);
            using var view = ViewHarness.ViewOf(game);
            var page = view.RunPage;
            var expected = Expected(turn, script);
            for (var at = 0; at < script.TotalTicks; at += 10)
            {
                var from = host.Sound.Played.Count;
                page.Play(turn);
                for (var k = 0; k < at; k++) view.ThePlayback.Advance();
                var before = Since(host, from);
                Assert.Equal(Names(expected.Where(e => e.Advance <= at).Select(e => e.Cue)), before);

                if (page.Playing) page.BoardPressed(new PixelPointer(1, 1, PointerButton.Left));
                Assert.False(page.Playing);
                var after = Since(host, from).Skip(before.Count).ToList();
                var cue = BeatSounds.OnSkip(script.Beats, LastBegun(script, at) + 1);
                Assert.Equal(cue is { } c ? new[] { Cues.FileOf(c) } : Array.Empty<string>(), after);
                if (after.Count == 1) skipCues[after[0]] = skipCues.GetValueOrDefault(after[0]) + 1;
                skips++;
            }
        }
        Assert.Contains("stage", skipCues.Keys);
        Assert.Contains("lost", skipCues.Keys);
        _out.WriteLine($"{skips} skips; their one cue: {string.Join(", ", skipCues.Select(p => $"{p.Key} {p.Value}"))}");
    });

    // ===== Paused (A79) ===================================================================================

    [Fact]
    public void PausedNothingMoreUntilResumedThenTheRest() => Sta.Run(() =>
    {
        var (saved, turn, script) = TurnPlaybackTests.Seeded(200).First(t => Expected(t.Turn, t.Script).Count >= 6);
        var (game, host) = ViewHarness.Attached(saved);
        using var view = ViewHarness.ViewOf(game);
        var page = view.RunPage;
        var expected = Names(Expected(turn, script).Select(e => e.Cue));
        page.Play(turn);
        while (host.Sound.Played.Count < expected.Count / 2) ViewHarness.Tick(view, host);
        var heard = host.Sound.Played.Count;
        view.Pause();
        for (var k = 0; k < 120; k++) ViewHarness.Tick(view, host);
        Assert.Equal(heard, host.Sound.Played.Count);
        Assert.True(page.Playing);
        view.Resume();
        ViewHarness.PlayOut(view, host);
        Assert.Equal(expected, host.Sound.Played);
    });

    // ===== What never sounds (H100, A85) ====================================================================

    [Fact]
    public void LayoutStatesAFreshViewASummaryAndAReadingSoundNothing() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(ViewHarness.Running());
        using (var view = ViewHarness.ViewOf(game))
        {
            for (var k = 0; k < 30; k++) ViewHarness.Tick(view, host);
            foreach (var state in view.LayoutStates)
            {
                view.ShowLayoutState(state);
                for (var k = 0; k < 5; k++) ViewHarness.Tick(view, host);
            }
            Assert.Empty(host.Sound.Played);
        }

        // A view made over a run's summary.
        var over = HatchlingGame.Advance(ViewHarness.Running(), new PlayerAction.Leave())!.Value.State;
        Assert.NotNull(over.Run!.Over);
        var (ended, endedHost) = ViewHarness.Attached(over);
        using (var view = ViewHarness.ViewOf(ended))
        {
            for (var k = 0; k < 30; k++) ViewHarness.Tick(view, endedHost);
            Assert.Empty(endedHost.Sound.Played);
        }

        // A reading that pays, with the view on screen (A85: the overlay's own chime is the real dino's).
        var (paid, paidHost) = ViewHarness.Attached(ViewHarness.Running());
        using (var view = ViewHarness.ViewOf(paid))
        {
            var balance = paid.State.Wallet.Balance;
            paid.OnReading(new DinoReading(true, "Dryosaurus", "Male", 0.300));
            paidHost.ManualClock.Advance(TimeSpan.FromSeconds(30));
            paid.OnReading(new DinoReading(true, "Dryosaurus", "Male", 0.310));
            Assert.True(paid.State.Wallet.Balance > balance, "the reading paid nothing");
            for (var k = 0; k < 30; k++) ViewHarness.Tick(view, paidHost);
            Assert.Empty(paidHost.Sound.Played);
        }
    });

    // ===== The summary a move brings (H99) ===================================================================

    /// <summary>A run under way with nothing in either bar and almost no health: a Rest ends it out of food, and its first run earns "first".</summary>
    private static GameState Starving()
    {
        var state = ViewHarness.Running();
        state.Run!.You.Food = 0;
        state.Run.You.Water = 0;
        state.Run.You.Health = 0.01;
        return state;
    }

    [Fact]
    public void AMoveThatEndsTheRunWithAnAchievementPlaysRewardAfterItsBeats() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(Starving());
        using var view = ViewHarness.ViewOf(game);
        var earned = game.State.Achievements.Unlocked.Count;
        view.RunPage.RestClicked();
        Assert.True(view.RunPage.Playing);
        Assert.NotNull(game.State.Run!.Over);
        Assert.True(game.State.Achievements.Unlocked.Count > earned);
        var beats = host.Sound.Played.Count;
        Assert.DoesNotContain("reward", host.Sound.Played);
        ViewHarness.PlayOut(view, host);
        var played = host.Sound.Played;
        _out.WriteLine(string.Join(", ", played));
        Assert.Equal("rest", played[0]);
        Assert.Equal(new[] { "lost", "reward" }, played.TakeLast(2));   // the beats' last cue, then the summary's
        Assert.Single(played, p => p == "reward");
        Assert.True(played.Count > beats);
    });

    [Fact]
    public void ASkipThatPlaysItsCueOpensASummaryWithNoReward() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(Starving());
        using var view = ViewHarness.ViewOf(game);
        view.RunPage.RestClicked();
        view.RunPage.BoardPressed(new PixelPointer(1, 1, PointerButton.Left));   // the skip, before the first beat
        Assert.False(view.RunPage.Playing);
        Assert.Equal(new[] { "lost" }, host.Sound.Played);   // one cue a click (H98, H99)
    });

    // ===== The plan's two-hunter turn, as evidence (H97) ======================================================

    [Fact]
    public void TheTwoHunterTurnIsElevenCuesInSixAndAHalfSeconds() => Sta.Run(() =>
    {
        foreach (var fast in new[] { false, true })
        {
            var (game, host) = ViewHarness.Attached(ViewHarness.Running());
            using var view = ViewHarness.ViewOf(game);
            var page = view.RunPage;
            if (fast) page.FastClicked(true);
            var turn = TwoHunters();
            Assert.Equal(2, TurnScripts.Build(turn, RunPage.SightKnows).Beats.Count(b => b.Kind == BeatKind.Focus));
            page.Play(turn);
            var looks = 0;
            var track = new List<(int Look, string Cue)>();
            while (page.Playing)
            {
                var heard = host.Sound.Played.Count;
                ViewHarness.Tick(view, host);
                looks++;
                foreach (var cue in host.Sound.Played.Skip(heard)) track.Add((looks, cue));
            }
            Assert.Equal(11, track.Count);
            Assert.Equal(fast ? 98 : 195, looks);
            // The plan's ticks on the turn's clock (BeatSoundsTests.Track), and the look of
            // the loop (30 a second, speed ticks each) on which the host was handed each cue:
            // a beat's first look draws its ticks 0 and 1 together, so a cue on tick 0 is
            // heard one tick after its time, any other on its own tick (at Fast, the look
            // holding that tick).
            var plan = BeatSoundsTests.Track(TurnScripts.Build(turn, RunPage.SightKnows));
            Assert.Equal(plan.Select(p => Cues.FileOf(p.Cue)), track.Select(t => t.Cue));
            var speed = fast ? 2 : 1;
            for (var i = 0; i < plan.Count; i++)
                Assert.InRange(track[i].Look, (plan[i].Tick + speed - 1) / speed, (plan[i].Tick + 1 + speed - 1) / speed);
            _out.WriteLine($"{(fast ? "Fast" : "Normal")}: {track.Count} cues in {looks / 30.0:0.00} s ({looks} looks): "
                + string.Join(", ", plan.Select((p, i) => $"tick {p.Tick} = {p.Tick / (30.0 * speed):0.00} s {Cues.FileOf(p.Cue)} (look {track[i].Look})")));
        }
    });

    /// <summary>You walk a cell; a Troodon and Carnotaurus 2 each spot you, step two cells and bite; time passes (the plan's 3.3 turn, as <c>BeatTableTests</c> builds it).</summary>
    private static TurnResult TwoHunters()
    {
        var here = Row5(2);
        var to = Row5(3);
        int troodonFrom = Row5(6), carnoFrom = Grid.Index(3, 8);
        var island = Run(here,
            Predator(1, PredatorType.Troodon, troodonFrom),
            Predator(2, PredatorType.Carnotaurus, carnoFrom, ordinal: 2));
        island.Instincts.Add(Instinct.Wary);   // both known throughout, whatever the ground, as Wary makes them
        var after = With(With(YouAt(island, to), 1, Row5(4), alert: 4), 2, Grid.Index(3, 6), alert: 4);
        after.Turn = island.Turn + 1;
        return Turn(island, after, Walk(here, to),
            new Noticed(1, Notice.Spotted), Step(1, Gait.Hunt, troodonFrom, Row5(5), Row5(4)), new Bit(1, 9, false, false),
            new Noticed(2, Notice.Spotted), Step(2, Gait.Hunt, carnoFrom, Grid.Index(3, 7), Grid.Index(3, 6)), new Bit(2, 9, false, false),
            Time());
    }
}

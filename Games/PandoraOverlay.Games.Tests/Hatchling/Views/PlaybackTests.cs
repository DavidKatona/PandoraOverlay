using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;
using Xunit;
using Xunit.Abstractions;
using static PandoraOverlay.Games.Tests.Scenes;

namespace PandoraOverlay.Games.Tests;

// The engine's playback tests have a Scene of their own in this namespace: the board's is BoardScene here.
using BoardScene = PandoraOverlay.Games.Hatchling.Scene;

/// <summary>
/// A turn as it is played on the Run page (A20, G9, A19; G9; A20;
/// A35): skip equals run for 200 seeded turns of every kind; one creature moves at a
/// time, with a gap between two hunters; no beat is cut short; the banner, the marker
/// and the rows beat by beat; a walk's frames count on through its cells (H60); the
/// sprites a playback adds are made by the scene's own rule.
/// </summary>
[Collection(LoopTimers.Name)]
public class TurnPlaybackTests
{
    private readonly ITestOutputHelper _out;

    public TurnPlaybackTests(ITestOutputHelper output) => _out = output;

    // ===== The 200 seeded turns ===============================================================

    /// <summary>
    /// Turns of real play, picked so every beat of the table is in at least one: H09's
    /// bot plays every species through <see cref="HatchlingGame.Advance"/> (the very
    /// step the game commits), fresh at danger 1 and with the whole lineage at dangers 3
    /// and 5; the first turn showing each kind of beat is taken, then turns spread over
    /// the rest, to <paramref name="count"/>. Each is the state the game saved and the
    /// turn's result. Deterministic.
    /// </summary>
    internal static IReadOnlyList<(GameState Saved, TurnResult Turn, TurnScript Script)> Seeded(int count)
    {
        var picked = new List<(GameState, TurnResult, TurnScript)>();
        var covered = new HashSet<BeatKind>();
        var spare = new List<(GameState, TurnResult, TurnScript)>();
        var all = Enum.GetValues<BeatKind>().ToHashSet();
        var seen = 0;

        // The bot eats and drinks long before hunger or thirst take health (none of
        // its runs end by them, H16), so the one beat it never shows is played from a
        // real run left with nothing in either bar: a Rest, through the game's own step.
        var hungry = ViewHarness.Running("Dryosaurus", 1, false, Sweeps.SeedOf(0), runs: 5);
        hungry.Run!.You.Food = 0;
        hungry.Run.You.Water = 0;
        var starved = HatchlingGame.Advance(hungry, new PlayerAction.Rest())!.Value;
        var starvedScript = TurnScripts.Build(starved.Result, RunPage.SightKnows);
        covered.UnionWith(starvedScript.Beats.Select(b => b.Kind));
        picked.Add((starved.State, starved.Result, starvedScript));

        foreach (var (danger, whole) in new[] { (5, true), (1, false), (3, true) })
            foreach (var species in SpeciesTable.All)
                for (var k = 0; k < 2; k++)
                {
                    if (covered.SetEquals(all) && spare.Count >= 4 * count) goto done;
                    var state = HatchlingGame.Started(ViewHarness.Ready(species.Name, danger, whole, Sweeps.SeedOf(k), runs: 5))!;
                    for (var actions = 0; state.Run is { Over: null } run && run.Turn < Bot.TurnLimit && actions < 2000; actions++)
                    {
                        var choice = Bot.Choose(run);
                        if (choice.Pace is { } pace && pace != run.Pace)
                            state = HatchlingGame.Advance(state, new PlayerAction.SetPace(pace))!.Value.State;
                        var next = HatchlingGame.Advance(state, choice.Action) ?? HatchlingGame.Advance(state, new PlayerAction.Rest())!;
                        state = next.Value.State;
                        var script = TurnScripts.Build(next.Value.Result, RunPage.SightKnows);
                        if (script.Beats.Count == 0) continue;
                        seen++;
                        var kinds = script.Beats.Select(b => b.Kind).ToHashSet();
                        if (!kinds.IsSubsetOf(covered))
                        {
                            covered.UnionWith(kinds);
                            picked.Add((state, next.Value.Result, script));
                        }
                        else if (seen % 7 == 0 || kinds.Contains(BeatKind.End) || kinds.Contains(BeatKind.Apex))
                        {
                            spare.Add((state, next.Value.Result, script));
                        }
                    }
                }
        done:
        Assert.True(covered.SetEquals(all), "beats no seeded turn showed: " + string.Join(", ", all.Except(covered)));
        var step = Math.Max(1, spare.Count / Math.Max(1, count - picked.Count));
        for (var i = 0; picked.Count < count && i < spare.Count; i += step) picked.Add(spare[i]);
        return picked;
    }

    /// <summary>What the page shows once a turn's beats are done: the board the beats left, the rows they wrote, the panel, the banner.</summary>
    private static string Played(RunPage page) => string.Join("\n",
        ViewHarness.Key(page.PlayedScene!),
        string.Join("|", page.PlayedRows!.Select(r => r ?? "-")),
        page.PlayedPanel!.Value.ToString(),
        page.PlayedBanner);

    /// <summary>What the page shows at rest after it: the board, the rows, the banner.</summary>
    private static string Rested(RunPage page) => string.Join("\n",
        ViewHarness.Key(page.CurrentScene, flips: false),
        string.Join("|", page.RowsShown),
        page.BannerShown);

    [Fact]
    public void SkipEqualsRunForTwoHundredSeededTurns() => Sta.Run(() =>
    {
        var turns = Seeded(200);
        Assert.Equal(200, turns.Count);
        var kinds = new HashSet<BeatKind>();
        long ticksPlayed = 0;
        var variants = 0;
        var onTheClock = 0;
        for (var n = 0; n < turns.Count; n++)
        {
            var (saved, turn, script) = turns[n];
            kinds.UnionWith(script.Beats.Select(b => b.Kind));
            var (game, host) = ViewHarness.Attached(saved);
            using var view = ViewHarness.ViewOf(game);
            var page = view.RunPage;
            var what = $"turn {turn.Before.Turn} of a {turn.Before.Species} at danger {turn.Before.Danger}";

            // Run to its end: exactly the script's ticks (A35), no overrun. Every tenth
            // turn on the manual clock through the loop (each look steps, then paints
            // the board, as on screen); the others by the playback's own advance, one
            // engine tick each, as the loop calls it, without the painting that a debug
            // build makes the slow part.
            page.Play(turn);
            int ticks;
            if (n % 10 == 0)
            {
                ticks = ViewHarness.PlayOut(view, host);
                onTheClock++;
            }
            else
            {
                ticks = 0;
                while (page.Playing && ticks < 5000) { view.ThePlayback.Advance(); ticks++; }
            }
            Assert.True(script.TotalTicks == ticks, $"{what}: played {ticks} ticks, the script's are {script.TotalTicks}");
            ticksPlayed += ticks;
            Assert.DoesNotContain(host.Collected.Warnings, w => w.Contains("limit", StringComparison.Ordinal));
            var ran = Played(page);
            var rest = Rested(page);
            AgreesWithTheState(page.PlayedScene!, game.State.Run!, what);
            RowsAreTheScripts(page, script, what);

            // Skipped by a press at tick 0, and at every tenth tick: the same picture.
            for (var at = 0; at < script.TotalTicks; at += 10)
            {
                page.Play(turn);
                for (var k = 0; k < at; k++) view.ThePlayback.Advance();
                if (page.Playing) page.BoardPressed(new PixelPointer(1, 1, PointerButton.Left));
                Assert.False(page.Playing);
                Assert.True(ran == Played(page), $"{what}: skipped at tick {at}, the beats left another picture than played to the end");
                Assert.True(rest == Rested(page), $"{what}: skipped at tick {at}, the rest after it differs");
                variants++;
            }

            // And the rest is the scene and the rows built from the state alone (H53, H28).
            var run = game.State.Run!;
            Assert.Equal(ViewHarness.Key(BoardScene.Of(run, Stats.Of(run), yourTurn: true, -1), flips: false), ViewHarness.Key(page.CurrentScene, flips: false));
            Assert.Equal(ViewHarness.Words(run.LastTurn), page.RowsShown.Select(r => (r.Name, r.Text)).ToList());
            Assert.Empty(host.Memory.Writes);   // a playback writes nothing: the state was saved before it (A20)
        }
        Assert.Equal(Enum.GetValues<BeatKind>().ToHashSet(), kinds);
        _out.WriteLine($"200 turns, every one of the {kinds.Count} kinds of beat, {ticksPlayed} ticks played ({onTheClock} turns on the manual clock), {variants} skips compared");
    });

    /// <summary>
    /// The picture the beats left agrees with the state saved: every creature the rest
    /// draws stands where the beats left it, standing, whole, with the same size, ring
    /// and marks (the "your turn" mark aside, H52); anything else the beats left on the
    /// board is gone (faded to 0) or a creature the player no longer knows, which the
    /// rest takes away.
    /// </summary>
    private static void AgreesWithTheState(BoardScene played, RunState after, string what)
    {
        var rest = BoardScene.Of(after, Stats.Of(after), yourTurn: true, -1);
        foreach (var sprite in rest.Sprites)
        {
            var mine = played.Sprites.SingleOrDefault(s => s.Creature == sprite.Creature && s.Kind == sprite.Kind);
            Assert.True(mine is not null, $"{what}: creature {sprite.Creature} is at rest on the board, and the playback never drew it");
            Assert.True(ViewHarness.SpriteKey(sprite, flips: false, youMark: false) == ViewHarness.SpriteKey(mine!, flips: false, youMark: false),
                $"{what}: the beats left {ViewHarness.SpriteKey(mine!, false, false)} where the rest draws {ViewHarness.SpriteKey(sprite, false, false)}");
        }
        foreach (var extra in played.Sprites.Where(s => s.Opacity > 0 && !rest.Sprites.Any(r => r.Creature == s.Creature && r.Kind == s.Kind)))
        {
            var creature = after.Creatures.FirstOrDefault(c => c.Id == extra.Creature);
            Assert.True(creature is null || !Sight.Knows(after, Stats.Of(after), creature),
                $"{what}: the beats left creature {extra.Creature} on the board, known, and the rest does not draw it");
        }
        Assert.Empty(played.Numbers);
    }

    /// <summary>The rows the beats wrote are the script's, each once its last beat finished; the rows with no beat are written by the drain (H28).</summary>
    private static void RowsAreTheScripts(RunPage page, TurnScript script, string what)
    {
        for (var i = 0; i < script.Rows.Count; i++)
        {
            var hasBeat = script.Beats.Any(b => b.RowIndex == i);
            var written = page.PlayedRows![i];
            if (hasBeat) Assert.True(HatchlingText.RowText(script.Rows[i]) == written, $"{what}: row {i} read '{written}'");
            else Assert.Null(written);
        }
    }

    // ===== One at a time (G9, H4) ===================================================================

    /// <summary>
    /// A turn with three hunters: you walk; each of three predators steps twice and
    /// bites; small game moves; time passes. Recorded tick by tick, which creatures
    /// changed place: never two on one tick, small game aside; and between two hunters,
    /// at least 3 ticks with nothing moving.
    /// </summary>
    [Fact]
    public void OneCreatureMovesAtATimeWithAGapBetweenTwoHunters() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(ViewHarness.Running());
        using var view = ViewHarness.ViewOf(game);
        var page = view.RunPage;
        var turn = ThreeHunters();
        page.Play(turn);
        var movers = new List<HashSet<int>>();
        var last = Places(page.CurrentScene);
        while (page.Playing)
        {
            ViewHarness.Tick(view, host);
            if (!page.Playing) break;
            var now = Places(page.CurrentScene);
            movers.Add(now.Where(p => !last.TryGetValue(p.Key, out var was) || was != p.Value).Select(p => p.Key).ToHashSet());
            last = now;
        }
        var game3 = SmallGameIds(turn);
        Assert.All(movers, m => Assert.True(m.Count(id => !game3.Contains(id)) <= 1, "two creatures moved on one tick: " + string.Join(",", m)));

        var hunters = new[] { 1, 2, 3 };
        var lastHunter = -1;
        var stillSince = 0;
        foreach (var m in movers)
        {
            var hunter = m.FirstOrDefault(id => hunters.Contains(id), -1);
            if (m.Count == 0) { stillSince++; continue; }
            if (hunter >= 0 && lastHunter >= 0 && hunter != lastHunter)
                Assert.True(stillSince >= BeatTicks.Gap, $"hunter {hunter} moved {stillSince} ticks after hunter {lastHunter}");
            if (hunter >= 0) lastHunter = hunter;
            stillSince = 0;
        }
        Assert.Equal(3, movers.SelectMany(m => m).Distinct().Count(id => hunters.Contains(id)));
    });

    private static Dictionary<int, (int, int)> Places(BoardScene scene) =>
        scene.Sprites.Where(s => s.Opacity > 0).ToDictionary(s => s.Kind == SpriteKind.You ? 0 : s.Creature, s => (s.X, s.Y));

    private static HashSet<int> SmallGameIds(TurnResult turn) =>
        turn.Before.Creatures.Concat(turn.After.Creatures).Where(c => c.Kind == CreatureKind.SmallGame).Select(c => c.Id).ToHashSet();

    /// <summary>You at column 5 of row 5; three predators two cells off (all in sight), each walking up and biting; small game fleeing; time.</summary>
    private static TurnResult ThreeHunters()
    {
        var you = Grid.Index(5, 5);
        var to = Grid.Index(5, 6);
        var before = Run(you,
            Predator(1, PredatorType.Troodon, Grid.Index(3, 6), alert: 4),
            Predator(2, PredatorType.Carnotaurus, Grid.Index(7, 6), ordinal: 1, alert: 4),
            Predator(3, PredatorType.Carnotaurus, Grid.Index(5, 8), ordinal: 2, alert: 4),
            Game(4, Grid.Index(6, 4)));
        var after = YouAt(before, to);
        after = With(after, 1, Grid.Index(4, 7));
        after = With(after, 2, Grid.Index(6, 7));
        after = With(after, 3, Grid.Index(5, 7));
        after = With(after, 4, Grid.Index(7, 4));
        after.Turn = before.Turn + 1;
        return Turn(before, after,
            Walk(you, to),
            Step(1, Gait.Hunt, Grid.Index(3, 6), Grid.Index(4, 6), Grid.Index(4, 7)), new Bit(1, 6, false, false),
            Step(2, Gait.Hunt, Grid.Index(7, 6), Grid.Index(7, 7), Grid.Index(6, 7)), new Bit(2, 9, false, false),
            Step(3, Gait.Hunt, Grid.Index(5, 8), Grid.Index(5, 7)), new Bit(3, 9, false, false),
            Step(4, Gait.Flee, Grid.Index(6, 4), Grid.Index(7, 4)),
            new TimePassed(0, 0, 0), new Grew(10, 11.4));
    }

    // ===== No beat is cut short (A35) ===============================================================

    /// <summary>
    /// The longest turn the rules can produce (A20): danger 5, the apex and seven
    /// predators known, each noticing, stepping twice, biting, hurt by spikes and
    /// bleeding; a sprint of five cells; a stage reached; the run ending. At speed 1 it
    /// lasts exactly the script's ticks, no beat over the most, and the engine never
    /// finishes a beat for it.
    /// </summary>
    [Fact]
    public void TheWorstTurnLastsExactlyItsTicksAndNoBeatOverruns() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(ViewHarness.Running());
        using var view = ViewHarness.ViewOf(game);
        var overran = 0;
        view.ThePlayback.StepOverran += () => overran++;
        var turn = Worst();
        var script = TurnScripts.Build(turn, RunPage.SightKnows);
        Assert.Equal(8, script.Beats.Count(b => b.Kind == BeatKind.Focus));
        Assert.All(script.Beats, b => Assert.InRange(b.Ticks, 1, TurnScript.MaxBeatTicks));
        view.RunPage.Play(turn);
        Assert.Equal(LoopRate.Full, view.TheLoop.Rate);
        var ticks = ViewHarness.PlayOut(view, host);
        Assert.Equal(script.TotalTicks, ticks);
        Assert.Equal(0, overran);
        Assert.DoesNotContain(host.Collected.Warnings, w => w.Contains("limit", StringComparison.Ordinal));
        // Fast plays it in half (H5): the speed, never the beats.
        view.RunPage.FastClicked(true);
        view.RunPage.Play(turn);
        Assert.Equal((script.TotalTicks + 1) / 2, ViewHarness.PlayOut(view, host));
        Assert.Equal(0, overran);
    });

    private static TurnResult Worst()
    {
        var you = Grid.Index(5, 5);
        // A sprint of five cells, toward the predators' row, every cell in a line.
        var five = new[] { Grid.Index(5, 4), Grid.Index(5, 3), Grid.Index(5, 2), Grid.Index(5, 1), Grid.Index(5, 0) };
        var creatures = new List<Creature>();
        var events = new List<TurnEvent> { Sprint(you, five) };
        for (var id = 1; id <= 8; id++)
        {
            var type = id == 8 ? PredatorType.Tyrannosaurus : PredatorType.Carnotaurus;
            var from = Grid.Index(id, 2);
            creatures.Add(Predator(id, type, from, ordinal: id == 8 ? 1 : id, alert: 4));
            events.Add(new Noticed(id, Notice.Spotted));
            events.Add(Step(id, Gait.Hunt, from, Grid.Index(id, 1), Grid.Index(id - 1, 1)));
            events.Add(new Bit(id, 9, false, false));
            events.Add(new Hurt(id, Grid.Index(id - 1, 1), 3, HurtBy.Spikes, false));
            events.Add(new Hurt(id, Grid.Index(id - 1, 1), 2, HurtBy.Bleed, false));
        }
        events.Add(new TimePassed(5, 5, 0));
        events.Add(new Grew(98.8, 100));
        events.Add(new StageReached(4));
        events.Add(new RunEnded(false, EndReason.Hunger, 0));
        var before = Run(you, creatures.ToArray());
        before.Danger = 5;
        before.Instincts.Add(Instinct.Wary);   // every hunter known throughout, as Wary and the apex make them
        before.You.Growth = 98.8;
        var after = YouAt(before, five[^1]);
        after.You.Growth = 100;
        return Turn(before, after, events.ToArray());
    }

    // ===== The banner, the marker, the rows (G9, H28) ===================================================

    [Fact]
    public void TheBannerAndTheMarkerFollowTheBeatsAndARowIsWrittenWhenItsActorIsDone() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(ViewHarness.Running());
        using var view = ViewHarness.ViewOf(game);
        var page = view.RunPage;
        Assert.Equal(HatchlingText.YourTurn, page.BannerShown);
        Assert.Equal(HatchlingText.RowMarker, page.RowsShown[0].Marker);

        var turn = ThreeHunters();
        var script = TurnScripts.Build(turn, RunPage.SightKnows);
        page.Play(turn);
        Assert.Equal(HatchlingText.Skip, page.ContextShown);
        // Before the first beat: the rows are the script's, none written yet.
        Assert.All(page.RowsShown, r => Assert.Equal("", r.Text));

        var marker = 0;
        for (var b = 0; b < script.Beats.Count; b++)
        {
            var beat = script.Beats[b];
            ViewHarness.Tick(view, host);   // the beat begins on this tick
            if (!page.Playing) break;
            Assert.Equal(beat.Banner, page.BannerShown);
            if (beat.Kind != BeatKind.Gap) marker = beat.RowIndex;
            var rows = page.RowsShown;
            for (var r = 0; r < rows.Count; r++)
            {
                Assert.Equal(r == marker ? HatchlingText.RowMarker : "", rows[r].Marker);
                // A row is written once its last beat has finished, never at its Focus.
                var done = script.Beats.Take(b).Any(x => x.RowIndex == r) && !script.Beats.Skip(b).Any(x => x.RowIndex == r);
                Assert.Equal(done ? HatchlingText.RowText(script.Rows[r]) : "", rows[r].Text);
            }
            for (var k = 1; k < beat.Ticks; k++) ViewHarness.Tick(view, host);
        }
        ViewHarness.PlayOut(view, host);
        Assert.Equal(HatchlingText.YourTurn, page.BannerShown);
        Assert.Equal(HatchlingText.RowMarker, page.RowsShown[0].Marker);
    });

    // ===== A walk's frames (H60) ======================================================================

    [Fact]
    public void AWalksTickCountsOnThroughEveryCell() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(ViewHarness.Running());
        using var view = ViewHarness.ViewOf(game);
        var page = view.RunPage;
        var you = Row5(1);
        var before = Run(you);
        var path = new[] { Row5(2), Row5(3), Row5(4) };
        var after = YouAt(before, path[^1]);
        page.Play(Turn(before, after, Walk(you, path), Time()));
        var ticks = new List<int>();
        for (var k = 0; k < 3 * BeatTicks.WalkPerCell; k++)
        {
            ViewHarness.Tick(view, host);
            var sprite = page.CurrentScene.Sprites.Single(s => s.Kind == SpriteKind.You);
            if (sprite.Action == BeatStep.Walk) ticks.Add(sprite.Tick);
        }
        Assert.Equal(3 * BeatTicks.WalkPerCell - 1, ticks.Count);
        for (var i = 1; i < ticks.Count; i++) Assert.True(ticks[i] > ticks[i - 1], "the walk's tick fell back");
        Assert.True(ticks[^1] > 12, "a walk of three cells passes the whole cycle of 12");
        ViewHarness.PlayOut(view, host);
        var stands = page.PlayedScene!.Sprites.Single(s => s.Kind == SpriteKind.You);
        Assert.Equal(ArtNames.Stand, stands.Action);
        Assert.Equal(BoardLayout.Feet(path[^1]), (stands.X, stands.Y));
    });

    // ===== The sprites a playback adds (H22, H52) =====================================================

    [Fact]
    public void APlaybacksSpritesAreMadeByTheScenesOwnRule()
    {
        var checkedSprites = 0;
        foreach (var (saved, turn, _) in Seeded(60))
            foreach (var run in new[] { turn.Before, turn.After })
            {
                var stats = Stats.Of(run);
                var scene = BoardScene.Of(run, stats, yourTurn: false, -1);
                foreach (var creature in run.Creatures.Where(c => Sight.Knows(run, stats, c)))
                {
                    var made = PlaybackSprites.Of(creature, creature.At);
                    var theScenes = scene.Sprites.Single(s => s.Creature == creature.Id && s.Kind != SpriteKind.You);
                    Assert.Equal(ViewHarness.SpriteKey(theScenes), ViewHarness.SpriteKey(made));
                    checkedSprites++;
                }
            }
        Assert.True(checkedSprites > 100);
    }

    // ===== The quiet cue (H55, A19) ===========================================================

    [Fact]
    public void AtRestTheAmbientFrameFlipsEverySecondLook() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(ViewHarness.Running());
        using var view = ViewHarness.ViewOf(game);
        Assert.Equal(LoopRate.Ambient, view.TheLoop.Rate);
        var frames = new List<int>();
        for (var look = 0; look < 8; look++)
        {
            host.ManualClock.Advance(TimeSpan.FromSeconds(1.0 / EngineTime.AmbientPerSecond));
            view.TheLoop.Tick();
            frames.Add(view.RunPage.CurrentScene.AmbientFrame);
        }
        Assert.Equal(new[] { 0, 1, 1, 0, 0, 1, 1, 0 }, frames);
    });

    // ===== Pictures for the planner (PANDORA_GAMES_RENDERS) ============================================

    /// <summary>
    /// With <c>PANDORA_GAMES_RENDERS</c> naming a folder (the render tool's variable,
    /// A27), the board of one turn at five moments (your beat, a hunter's focus, its
    /// step, its bite, time passing), saved with <c>FrameGolden.Save</c>, and the Run
    /// page's layout states drawn unthemed (the app's theme is not in these tests: task
    /// H16 draws them in every theme). Compared with nothing; without the variable it
    /// returns at once.
    /// </summary>
    [Fact]
    public void RendersForTheReviewer() => Sta.Run(() =>
    {
        var folder = System.Environment.GetEnvironmentVariable("PANDORA_GAMES_RENDERS");
        if (string.IsNullOrWhiteSpace(folder)) return;
        folder = Path.Combine(folder, "hatchling-run");
        Directory.CreateDirectory(folder);

        var (game, host) = ViewHarness.Attached(ViewHarness.Running());
        using var view = ViewHarness.ViewOf(game);
        var page = view.RunPage;
        var turn = ThreeHunters();
        var script = TurnScripts.Build(turn, RunPage.SightKnows);
        var moments = new (string Name, Func<Beat, bool> Beat, int Tick)[]
        {
            ("1-your-walk", b => b.Kind == BeatKind.YouWalk, 2),
            ("2-hunter-focus", b => b.Kind == BeatKind.Focus && b.Creature == 2, 1),
            ("3-hunter-step", b => b.Kind == BeatKind.Steps && b.Creature == 2, 4),
            ("4-hunter-bite", b => b.Kind == BeatKind.Bites && b.Creature == 2, 5),
            ("5-time-passes", b => b.Kind == BeatKind.TimePasses, 2),
        };
        foreach (var (name, which, tick) in moments)
        {
            var index = script.Beats.ToList().FindIndex(b => which(b));
            Assert.True(index >= 0, name);
            var start = script.Beats.Take(index).Sum(b => b.Ticks);
            page.Play(turn);
            for (var k = 0; k < start + tick; k++) ViewHarness.Tick(view, host);
            FrameGolden.Save(page.Board.Buffer, Path.Combine(folder, $"board-{name}.png"));
            _out.WriteLine($"{name}: banner '{page.BannerShown}'");
            ViewHarness.PlayOut(view, host);
        }

        foreach (var state in view.LayoutStates.Where(s => s.StartsWith("run-", StringComparison.Ordinal)))
        {
            view.ShowLayoutState(state);
            var root = view.Root;
            root.Measure(new Size(GameFrame.ViewWidth, GameFrame.ViewHeight));
            root.Arrange(new Rect(0, 0, GameFrame.ViewWidth, GameFrame.ViewHeight));
            root.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)GameFrame.ViewWidth, (int)GameFrame.ViewHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(folder, $"unthemed-{state}.png"))) encoder.Save(file);
            FrameGolden.Save(page.Board.Buffer, Path.Combine(folder, $"board-{state}.png"));
        }
        _out.WriteLine("renders in " + folder);
    });
}

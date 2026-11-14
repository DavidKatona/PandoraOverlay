using System.IO;
using System.Reflection;
using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// Hatchling's save (task H08): a state saved reads back exactly, field for field and roll
/// for roll; the format is pinned; every fixture loads (A9); the engine's round trip
/// holds on a fresh, a full mid-run and a finished save (A51); and a save no run of
/// the game could have written is refused, kind by kind, while every state the game
/// does write passes. Losing a player's progress is the worst bug this feature can
/// have: these tests are where it would be found first.
/// </summary>
public class HatchlingSaveTests
{
    // ===== The shape's two rules (A51) ==========================================================

    /// <summary>Every class of the save's shape, from SaveData down.</summary>
    private static IReadOnlyList<Type> ShapeClasses()
    {
        var found = new List<Type>();
        var queue = new Queue<Type>(new[] { typeof(SaveData) });
        while (queue.Count > 0)
        {
            var type = queue.Dequeue();
            if (found.Contains(type)) continue;
            found.Add(type);
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var t = property.PropertyType;
                foreach (var part in new[] { t }.Concat(t.IsGenericType ? t.GetGenericArguments() : Type.EmptyTypes))
                    if (Deep.IsShapeClass(part)) queue.Enqueue(part);
            }
        }
        return found;
    }

    [Fact]
    public void EverySavedPropertyHasAPublicSetter()
    {
        var problems = ShapeClasses()
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.SetMethod is not { IsPublic: true })
                .Select(p => $"{t.Name}.{p.Name}"))
            .ToList();
        Assert.True(problems.Count == 0,
            "A saved property without a public setter is written and never read back (A51): " + string.Join(", ", problems));
    }

    [Fact]
    public void TheShapeHoldsOnlySimpleTypesAndNoField()
    {
        var simple = new[] { typeof(string), typeof(bool), typeof(int), typeof(uint), typeof(long), typeof(double) };
        bool Allowed(Type t) =>
            simple.Contains(t) || Deep.IsShapeClass(t)
            || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>) && Allowed(t.GetGenericArguments()[0]))
            || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>)
                && t.GetGenericArguments()[0] == typeof(string) && Allowed(t.GetGenericArguments()[1]));

        var classes = ShapeClasses();
        Assert.Contains(typeof(SavedRun), classes);
        Assert.Contains(typeof(SavedRow), classes);
        var problems = classes
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => !Allowed(p.PropertyType))
                .Select(p => $"{t.Name}.{p.Name} ({p.PropertyType.Name})"))
            .Concat(classes.SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => $"{t.Name}.{f.Name} (a field)")))
            .ToList();
        Assert.True(problems.Count == 0,
            "The save's shape is numbers, strings, booleans, lists, string-keyed dictionaries and its own classes (an Rng is its State, A51): "
            + string.Join(", ", problems));
    }

    // ===== The engine's round trip (A51) ========================================================

    [Theory]
    [InlineData("fresh")]
    [InlineData("mid-run")]
    [InlineData("run-over")]
    [InlineData("everything")]
    [InlineData("pinned")]
    public void TheEnginesRoundTripHolds(string sample) =>
        SaveFixtures.AssertRoundTrips(HatchlingSave.Schema, HatchlingSave.From(SaveSamples.Named(sample)));

    /// <summary>
    /// The mid-run sample holds something in every member of the save, so the round
    /// trip above proves every one: a member left at what a lost one reads back as
    /// proves nothing about itself. A field added to the shape later and not filled
    /// here fails this test. The one exception is the run's end, which a run under way
    /// never has; the next test holds the ended samples to it.
    /// </summary>
    [Fact]
    public void TheMidRunSaveHoldsSomethingInEveryMember()
    {
        var missing = Deep.DefaultMembers(typeof(SaveData), new object[] { HatchlingSave.From(SaveSamples.MidRun()) });
        Assert.True(missing.SequenceEqual(new[] { "Run.Over" }),
            "Every member of the mid-run sample must hold something (A51); at what a lost member reads back as: " + string.Join(", ", missing));
    }

    /// <summary>A won end and a caught one between them fill every member of a run's end (no one end fills both "won" and "caught by").</summary>
    [Fact]
    public void TheEndedSamplesHoldSomethingInEveryMemberOfTheirEnd()
    {
        var ends = new object[]
        {
            HatchlingSave.From(SaveSamples.RunOver()).Run!.Over!,
            HatchlingSave.From(SaveSamples.Everything()).Run!.Over!,
        };
        var missing = Deep.DefaultMembers(typeof(SavedOver), ends, "Run.Over.");
        Assert.True(missing.Count == 0, "Every member of a run's end must hold something in one of the ended samples: " + string.Join(", ", missing));
    }

    // ===== To(From(s)) is s =====================================================================

    [Theory]
    [InlineData("fresh")]
    [InlineData("mid-run")]
    [InlineData("run-over")]
    [InlineData("everything")]
    [InlineData("pinned")]
    public void ASampleReadsBackFieldForField(string sample)
    {
        var state = SaveSamples.Named(sample);
        var back = HatchlingSave.To(HatchlingSave.From(state));
        var differences = Deep.Differences(state, back);
        Assert.True(differences.Count == 0, Deep.Show(differences));

        // And through the text, as the next launch reads it.
        var loaded = HatchlingSave.Schema.Load(HatchlingSave.Version, SaveFiles.PayloadOf(state));
        Assert.Equal(SaveOutcome.Ok, loaded.Outcome);
        differences = Deep.Differences(state, HatchlingSave.To(loaded.State!));
        Assert.True(differences.Count == 0, Deep.Show(differences));
    }

    [Fact]
    public void TheComparerFindsAFieldLeftBehind()
    {
        var state = SaveSamples.MidRun();
        var other = state.Clone();
        other.Run!.Creatures[1].Bleed = 0;
        other.Run.You.AdultUnbitten = false;
        other.FreshAsOf = DateTime.SpecifyKind(other.FreshAsOf, DateTimeKind.Unspecified);
        var differences = Deep.Differences(state, other);
        Assert.Contains(differences, d => d.StartsWith("state.Run.Creatures[1].Bleed", StringComparison.Ordinal));
        Assert.Contains(differences, d => d.StartsWith("state.Run.You.AdultUnbitten", StringComparison.Ordinal));
        Assert.Contains(differences, d => d.StartsWith("state.FreshAsOf", StringComparison.Ordinal));
        Assert.Empty(Deep.Differences(state, state.Clone()));
    }

    /// <summary>The careers of <see cref="RandomPlay"/>: built once for the tests below.</summary>
    private static readonly Lazy<IReadOnlyList<GameState>> Careers =
        new(() => Enumerable.Range(0, 50).Select(RandomPlay.Career).ToList());

    /// <summary>
    /// Fifty states made by playing (A20's rules, server play's seen steps, the
    /// purchases), in every phase: each saved and loaded is itself, field for field,
    /// and the same twenty moves played on from it and from the loaded copy end in
    /// equal states: every generator restored (the run's, the journal's, the next
    /// seed) rolls exactly as the one saved would have.
    /// </summary>
    [Fact]
    public void FiftyPlayedStatesReadBackAndPlayOnAlike()
    {
        var careers = Careers.Value;
        for (var i = 0; i < careers.Count; i++)
        {
            var state = careers[i];
            var data = HatchlingSave.From(state);
            Assert.True(HatchlingSave.Problem(data) is null, $"career {i}: the game wrote a save its own check refuses: {HatchlingSave.Problem(data)}");
            var loaded = HatchlingSave.Schema.Load(HatchlingSave.Version, HatchlingSave.Schema.Save(data));
            Assert.Equal(SaveOutcome.Ok, loaded.Outcome);
            var back = HatchlingSave.To(loaded.State!);
            var differences = Deep.Differences(state, back);
            Assert.True(differences.Count == 0, $"career {i} does not read back:\n" + Deep.Show(differences));

            var dice1 = new Rng(Rng.Mix((uint)i, 20));
            var dice2 = new Rng(Rng.Mix((uint)i, 20));
            var played = state;
            var playedBack = back;
            for (var move = 0; move < 20; move++)
            {
                played = RandomPlay.Step(played, dice1);
                playedBack = RandomPlay.Step(playedBack, dice2);
            }
            differences = Deep.Differences(played, playedBack);
            Assert.True(differences.Count == 0, $"career {i}: twenty moves from the loaded copy part from the original's:\n" + Deep.Show(differences));
        }
    }

    [Fact]
    public void TheFiftyStatesHaveRunsInEveryPhase()
    {
        var reached = Careers.Value.SelectMany(RandomPlay.PhasesOf).ToHashSet();
        var missing = Enum.GetValues<RandomPlay.Phase>().Where(p => !reached.Contains(p)).ToList();
        Assert.True(missing.Count == 0, "No career reached: " + string.Join(", ", missing));
    }

    /// <summary>
    /// The other way round from refusing: the check must never refuse a state the
    /// game writes, or a real player's save would be held as unreadable at the next
    /// launch. Long careers, a check after every step.
    /// </summary>
    [Fact]
    public void EveryStateTheGameWritesPassesItsOwnCheck()
    {
        for (var career = 0; career < 12; career++)
        {
            var dice = new Rng(Rng.Mix((uint)career, 0x5AFE));
            var state = RandomPlay.Career(career * 3 + 1);
            for (var step = 0; step < 400; step++)
            {
                state = RandomPlay.Step(state, dice);
                var problem = HatchlingSave.Problem(HatchlingSave.From(state));
                Assert.True(problem is null, $"career {career}, step {step}: the check refuses a state the game made: {problem}");
            }
        }
    }

    // ===== The format is pinned ==================================================================

    /// <summary>
    /// The save of one fixed, hand-built state is a text checked in beside this test:
    /// a property renamed, retyped or reordered, an enum member renamed, a moment's
    /// form changed, each fails here before it strands a player's save (A9).
    /// </summary>
    [Fact]
    public void TheFormatIsPinned()
    {
        var text = SaveFiles.PayloadOf(SaveSamples.Pinned());
        var pinned = SaveFiles.ReadOrRecord(SaveFiles.PinnedPath, text);
        Assert.True(pinned is not null, SaveFiles.Missing(SaveFiles.PinnedPath));
        Assert.Equal(pinned, text);
    }

    [Fact]
    public void ThePinnedTextHoldsEveryNameOfTheFormat()
    {
        var text = SaveFiles.PayloadOf(SaveSamples.Pinned());
        var missing = ShapeClasses()
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name))
            .Distinct()
            .Where(name => !text.Contains($"\"{name}\":", StringComparison.Ordinal))
            .ToList();
        Assert.True(missing.Count == 0, "The pinned state leaves names of the format out: " + string.Join(", ", missing));
    }

    /// <summary>
    /// Every word the save writes for a value, pinned: the enum members it writes by
    /// name, the predators by their species, the ids of traits, instincts, species
    /// and achievements, the ground's letters. A word renamed strands every save that
    /// holds it (an upgrade step and a new version carry it over, A9); a member ADDED
    /// is fine, and is added here.
    /// </summary>
    [Fact]
    public void TheWordsTheSaveWritesArePinned()
    {
        static void Names<T>(params string[] expected) where T : struct, Enum =>
            Assert.Equal(expected.OrderBy(s => s, StringComparer.Ordinal), Enum.GetNames<T>().OrderBy(s => s, StringComparer.Ordinal));

        Names<CreatureKind>("Predator", "SmallGame");
        Names<Pace>("Walk", "Sneak", "Sprint");
        Names<MoveKind>("None", "Walk", "Sneak", "Sprint", "Eat", "Drink", "Rest", "Attack");
        Names<EndReason>("Caught", "Hunger", "Thirst", "Left", "Outlasted", "DefeatedApex");
        Names<PayKind>("Grew", "Reached", "Defeated", "Outlasted", "DefeatedApex", "Danger", "Legacy");
        Names<RowActor>("You", "Predator", "SmallGame", "Unseen");
        Names<RowCode>("None", "Hatched", "Walked", "Sneaked", "Sprinted", "Ate", "Drank", "Rested", "Hit", "Defeated",
            "Spotted", "Heard", "Lost", "Bit", "Missed", "Dazed", "Held", "Hunting", "Searching", "Wandered", "Waited", "Arrived",
            "WasDefeated", "Scattered", "Moved", "Still", "Quiet", "HeardYou");
        Names<Food>("Plants", "Meat", "Fish");
        Names<Build>("Runner", "Grazer", "Tank", "Hunter", "Stalker", "Apex", "Swimmer", "Flyer", "Forager");
        Names<LatelyKind>("Grew", "Star", "Unlocked", "Journal");

        Assert.Equal(new[] { "Troodon", "Herrerasaurus", "Carnotaurus", "Allosaurus", "Tyrannosaurus" },
            Enum.GetValues<PredatorType>().Select(Predators.NameOf));
        Assert.Equal(new[] { "stomach", "hide", "dry", "wind", "nose", "reflexes", "step", "escape",
                "metabolism", "start", "legacy", "patience", "sharp", "thoughts", "bloomer", "ready" },
            Lineage.All.Select(t => t.Id));
        Assert.Equal(new[] { "eyes", "feet", "stride", "skin", "ambush", "appetite", "healer", "quick", "nose", "fierce", "wary", "hardy" },
            Instincts.All.Select(Instincts.IdOf));
        Assert.Equal(new[] { "Dryosaurus", "Hypsilophodon", "Kentrosaurus", "Pachycephalosaurus", "Tenontosaurus", "Diabloceratops",
                "Maiasaura", "Stegosaurus", "Triceratops", "Pteranodon", "Troodon", "Herrerasaurus", "Omniraptor", "Carnotaurus",
                "Ceratosaurus", "Dilophosaurus", "Deinosuchus", "Allosaurus", "Tyrannosaurus", "Beipiaosaurus", "Gallimimus" },
            SpeciesTable.All.Select(s => s.Name));
        Assert.Equal(new[] { "first", "juvenile", "adult", "elder", "win", "slayer", "hunter", "underdog", "heavy", "shapes",
                "danger3", "danger5", "unseen", "gentle", "six", "island", "roots", "blood", "researcher", "naturalist", "diverse",
                "star21", "mastery", "all" },
            AchievementList.All.Select(a => a.Id));

        var run = SaveSamples.MidRun().Run!;
        run.Tiles[0] = Terrain.Grass;
        run.Tiles[1] = Terrain.Forest;
        run.Tiles[2] = Terrain.Water;
        run.Tiles[3] = Terrain.Rock;
        var state = SaveSamples.MidRun();
        state.Run = run;
        Assert.StartsWith("gfwr", HatchlingSave.From(state).Run!.Tiles, StringComparison.Ordinal);
        Assert.Equal("2026-11-14T12:00:00.0000000Z", HatchlingSave.MomentText(SaveSamples.Moment));
    }

    // ===== Fixtures (A9) ==========================================================================

    /// <summary>Every fixture of every version loads, mid-run included, in this build and every later one (A9).</summary>
    [Fact]
    public void EveryFixtureLoads()
    {
        RecordMissingFixtures();
        SaveFixtures.AssertAllLoad(HatchlingSave.Schema, SaveFiles.FixturesFolder);
    }

    [Fact]
    public void TheFourFixturesAreThere()
    {
        RecordMissingFixtures();
        foreach (var name in SaveSamples.FixtureNames)
            Assert.True(File.Exists(SaveFiles.FixturePath(name)), SaveFiles.Missing(SaveFiles.FixturePath(name)));
    }

    // No test compares a fixture with the sample it was recorded from: the samples may
    // change with the game, and a fixture never does. A test that failed when they
    // parted would invite recording the fixture again, the one thing never to do.

    /// <summary>Writes a fixture that is missing, from its sample, when recording (never one that is there).</summary>
    private static void RecordMissingFixtures()
    {
        foreach (var name in SaveSamples.FixtureNames)
            SaveFiles.ReadOrRecord(SaveFiles.FixturePath(name), SaveFiles.PayloadOf(SaveSamples.Named(name)));
    }

    // ===== Valid =================================================================================

    [Theory]
    [InlineData("fresh")]
    [InlineData("mid-run")]
    [InlineData("run-over")]
    [InlineData("everything")]
    [InlineData("pinned")]
    public void TheSamplesPassTheCheck(string sample) =>
        Assert.Null(HatchlingSave.Problem(HatchlingSave.From(SaveSamples.Named(sample))));

    /// <summary>One kind of bad value each, done to a good save (the mid-run sample's), and what it is.</summary>
    private static readonly Dictionary<string, Action<SaveData>> Spoilers = new()
    {
        // Lists of the wrong length.
        ["the ground one cell short"] = d => d.Run!.Tiles = d.Run.Tiles[..^1],
        ["the seen cells one too many"] = d => d.Run!.Seen += "0",
        ["the plants one short"] = d => d.Run!.Plants = d.Run.Plants[1..],
        ["the meat one short"] = d => d.Run!.Meat.RemoveAt(0),
        ["the journal with four entries"] = d => d.Journal.Open.Add(new SavedEntry { Species = "Triceratops", Target = 50, Reward = 500 }),
        ["lately with seven lines"] = d => d.Lately.AddRange(Enumerable.Range(0, 3).Select(_ => new SavedLine { Kind = "Star", Species = "Maiasaura" })),
        ["a turn list of twelve rows"] = d => d.Run!.LastTurn.AddRange(Enumerable.Range(0, 6).Select(_ => d.Run.LastTurn[^1])),
        ["an empty offer"] = d => d.Run!.Offer = new List<string>(),
        ["an offer of five"] = d => d.Run!.Offer!.Add("feet"),
        // Cells off the board.
        ["your cell off the board"] = d => d.Run!.You.At = Tuning.Cells,
        ["a creature off the board"] = d => d.Run!.Creatures[0].At = -1,
        ["a target off the board"] = d => d.Run!.Creatures[0].Target = Tuning.Cells,
        ["a target of minus two"] = d => d.Run!.Creatures[0].Target = -2,
        // Ids the tables do not know.
        ["an unknown trait"] = d => d.Ranks["wings"] = 1,
        ["an unknown trait in the run"] = d => d.Run!.Ranks["wings"] = 1,
        ["an unknown instinct picked"] = d => d.Run!.Instincts.Add("x-ray"),
        ["an unknown instinct offered"] = d => d.Run!.Offer![0] = "x-ray",
        ["an unknown species unlocked"] = d => d.Unlocked.Add("Velociraptor"),
        ["a species in another case"] = d => d.Selected = "omniraptor",
        ["an unknown species chosen"] = d => d.Selected = "Velociraptor",
        ["an unknown species' record"] = d => d.Species["Velociraptor"] = new SavedSpecies(),
        ["an unknown species in the journal"] = d => d.Journal.Open[0].Species = "Velociraptor",
        ["an unknown species in lately"] = d => d.Lately[0].Species = "Velociraptor",
        ["an unknown species won with"] = d => d.Totals.SpeciesWon.Add("Velociraptor"),
        ["an unknown build won with"] = d => d.Totals.BuildsWon.Add("Glider"),
        ["an unknown kind of line"] = d => d.Lately[0].Kind = "Rumour",
        ["the run's species unknown"] = d => d.Run!.Species = "Velociraptor",
        ["an unknown pace"] = d => d.Run!.Pace = "Fly",
        ["an unknown last move"] = d => d.Run!.You.LastMove = "Dance",
        ["an unknown creature kind"] = d => d.Run!.Creatures[0].Kind = "Bird",
        ["an unknown predator"] = d => d.Run!.Creatures[0].Type = "Spinosaurus",
        ["an unknown predator arrived"] = d => d.Run!.Arrived["Spinosaurus"] = 1,
        ["an unknown ground"] = d => d.Run!.Tiles = "x" + d.Run.Tiles[1..],
        ["a seen cell neither 0 nor 1"] = d => d.Run!.Seen = "2" + d.Run.Seen[1..],
        ["an unknown row code"] = d => d.Run!.LastTurn[0].First = "Danced",
        ["an unknown row actor"] = d => d.Run!.LastTurn[0].Actor = "Narrator",
        ["an enum member by its number"] = d => d.Run!.Pace = "1",
        // Health, food, water, growth.
        ["health above 1"] = d => d.Run!.You.Health = 1.01,
        ["health below 0"] = d => d.Run!.You.Health = -0.01,
        ["food above 100"] = d => d.Run!.You.Food = 100.5,
        ["water below 0"] = d => d.Run!.You.Water = -1,
        ["growth above 100"] = d => d.Run!.You.Growth = 100.01,
        ["a start below 0"] = d => d.Run!.You.Start = -5,
        ["a best above 100"] = d => d.Run!.You.Best = 101,
        ["a species' best above 100"] = d => d.Species["Omniraptor"].BestGrowth = 100.5,
        // Ranks and dangers.
        ["a rank above its trait's"] = d => d.Ranks["stomach"] = 6,
        ["a rank below 0"] = d => d.Ranks["stomach"] = -1,
        ["a run's rank above its trait's"] = d => d.Run!.Ranks["sharp"] = 2,
        ["danger 0"] = d => d.Danger = 0,
        ["a highest danger of 6"] = d => d.MaxDanger = 6,
        ["a run at danger 6"] = d => d.Run!.Danger = 6,
        ["four stars"] = d => d.Run!.Stars = 4,
        // Creatures and their numbers (H38).
        ["two creatures with one id"] = d => d.Run!.Creatures[1].Id = d.Run.Creatures[0].Id,
        ["a creature at the next id"] = d => d.Run!.Creatures[^1].Id = d.Run.NextId,
        ["a creature id of 0"] = d => d.Run!.Creatures[0].Id = 0,
        ["an alert of 5"] = d => d.Run!.Creatures[0].Alert = 5,
        ["a predator numbered 0"] = d => d.Run!.Creatures[0].Ordinal = 0,
        ["a number its species never reached"] = d => d.Run!.Creatures[0].Ordinal = 3,
        ["two living predators with one number"] = d =>
        {
            d.Run!.Creatures[1].Type = "Troodon";
            d.Run.Creatures[1].Ordinal = 2;
        },
        ["a predator whose species never arrived"] = d => d.Run!.Arrived.Remove("Carnotaurus"),
        // The instincts (H14).
        ["an offered instinct owned"] = d => d.Run!.Offer![0] = d.Run.Instincts[0],
        ["an instinct picked twice"] = d => d.Run!.Instincts.Add(d.Run.Instincts[0]),
        // What the rules would throw on.
        ["a wallet below zero"] = d => d.Balance = -1,
        ["earnings below zero"] = d => d.Earned = -1,
        ["a carry of 1"] = d => d.Carry = 1,
        ["a carry below 0"] = d => d.Carry = -0.1,
        ["a freshness above 3"] = d => d.Species["Omniraptor"].Fresh = 3.5,
        ["a moment with no zone"] = d => d.FreshAsOf = "2026-11-14T10:00:00.0000000",
        ["a moment that is not one"] = d => d.FreshAsOf = "yesterday",
        ["a journal reward below 0"] = d => d.Journal.Open[0].Reward = -400,
        ["a run's pay below 0"] = d => d.Run!.Over = new SavedOver { Reason = "Left", ByType = "Troodon", Total = -1 },
        ["a last stage of 5"] = d => d.Run!.Over = new SavedOver { Reason = "Left", ByType = "Troodon", Stage = 5 },
        ["an unknown end"] = d => d.Run!.Over = new SavedOver { Reason = "Vanished", ByType = "Troodon" },
        ["a species unlocked twice"] = d => d.Unlocked.Add("Omniraptor"),
        ["a negative count bought"] = d => d.Bought = -1,
        ["a negative turn"] = d => d.Run!.Turn = -1,
        ["negative meat"] = d => d.Run!.Meat[0] = -1,
        // Nulls the reader leaves where a file held one.
        ["no unlocked list"] = d => d.Unlocked = null!,
        ["no journal"] = d => d.Journal = null!,
        ["no totals"] = d => d.Totals = null!,
        ["no ranks"] = d => d.Ranks = null!,
        ["no records"] = d => d.Species = null!,
        ["no lately"] = d => d.Lately = null!,
        ["no achievements list"] = d => d.Achievements = null!,
        ["no dino of yours"] = d => d.Run!.You = null!,
        ["no creatures"] = d => d.Run!.Creatures = null!,
        ["no turn list"] = d => d.Run!.LastTurn = null!,
        ["no arrivals"] = d => d.Run!.Arrived = null!,
        ["no ground"] = d => d.Run!.Tiles = null!,
        ["a creature that is null"] = d => d.Run!.Creatures.Add(null!),
        ["a record that is null"] = d => d.Species["Omniraptor"] = null!,
    };

    public static IEnumerable<object[]> SpoilerNames => Spoilers.Keys.Select(k => new object[] { k });

    /// <summary>Each kind of bad value is refused by the check, and the schema's load then answers Corrupt, which the game answers as SaveUnreadable and the host holds (A7).</summary>
    [Theory]
    [MemberData(nameof(SpoilerNames))]
    public void TheCheckRefuses(string kind)
    {
        var data = HatchlingSave.From(SaveSamples.MidRun());
        Assert.Null(HatchlingSave.Problem(data));
        Spoilers[kind](data);
        Assert.NotNull(HatchlingSave.Problem(data));
        Assert.False(HatchlingSave.Valid(data));
        Assert.Equal(SaveOutcome.Corrupt, HatchlingSave.Schema.Load(HatchlingSave.Version, HatchlingSave.Schema.Save(data)).Outcome);
    }

    /// <summary>The one id that is kept: an achievement this build does not know (the engine's rule; a later version may bring it back).</summary>
    [Fact]
    public void AnUnknownAchievementIsKept()
    {
        var data = HatchlingSave.From(SaveSamples.MidRun());
        data.Achievements.Add("a-later-achievement");
        data.Run!.Offer = null;
        Assert.Null(HatchlingSave.Problem(data));
        var loaded = HatchlingSave.Schema.Load(HatchlingSave.Version, HatchlingSave.Schema.Save(data));
        Assert.Equal(SaveOutcome.Ok, loaded.Outcome);
        var state = HatchlingSave.To(loaded.State!);
        Assert.Contains("a-later-achievement", state.Achievements.Unlocked);
        Assert.False(state.Achievements.Has("a-later-achievement"));
        Assert.Contains("a-later-achievement", HatchlingSave.From(state).Achievements);
    }

    /// <summary>A null where the rules want a value, a missing property, an empty object: the check answers, it never throws.</summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Run\":{}}")]
    [InlineData("{\"Run\":{\"You\":null,\"Creatures\":null}}")]
    [InlineData("{\"Journal\":{\"Open\":[null]}}")]
    [InlineData("{\"Species\":{\"Maiasaura\":null}}")]
    [InlineData("[]")]
    [InlineData("not a save")]
    [InlineData("")]
    public void ANonsenseSaveIsCorruptAndNeverThrows(string text) =>
        Assert.Equal(SaveOutcome.Corrupt, HatchlingSave.Schema.Load(HatchlingSave.Version, text).Outcome);

    [Fact]
    public void ANullSaveIsRefusedNotThrown()
    {
        Assert.False(HatchlingSave.Valid(null!));
        Assert.False(HatchlingSave.Valid(new SaveData()));
    }

    /// <summary>A newer save is left alone, unparsed (A7): even a text that would not read.</summary>
    [Fact]
    public void ANewerSaveIsNewer() =>
        Assert.Equal(SaveOutcome.Newer, HatchlingSave.Schema.Load(HatchlingSave.Version + 1, "anything at all").Outcome);

    /// <summary>A value no table names can't become a save: From throws, so the game never writes it.</summary>
    [Fact]
    public void AValueNoTableNamesIsNeverWritten()
    {
        var state = SaveSamples.MidRun();
        state.Run!.Pace = (Pace)9;
        Assert.Throws<ArgumentOutOfRangeException>(() => HatchlingSave.From(state));
        var other = SaveSamples.MidRun();
        other.Run!.Tiles[5] = (Terrain)7;
        Assert.Throws<ArgumentOutOfRangeException>(() => HatchlingSave.From(other));
    }

    /// <summary>The copy shares nothing with the state it came from: a save made, then the state played on, still says what it said.</summary>
    [Fact]
    public void TheSaveSharesNothingWithTheState()
    {
        var state = SaveSamples.MidRun();
        var text = HatchlingSave.Schema.Save(HatchlingSave.From(state));
        var back = HatchlingSave.To(HatchlingSave.From(state));
        back.Run!.Creatures[0].Health = 1;
        back.Run.Tiles[0] = Terrain.Rock;
        back.Unlocked.Add("Maiasaura");
        back.Journal.Open[0].Seen = 70;
        Assert.Equal(text, HatchlingSave.Schema.Save(HatchlingSave.From(state)));
    }
}

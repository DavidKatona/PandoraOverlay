using System.Text.Json.Nodes;
using PandoraOverlay.Games.Engine;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The save schema (A9): a round trip, the upgrade steps and which of them run, the
/// three answers (Ok, Newer, Corrupt) and what makes each, the defects that throw,
/// numbers that must come back to the bit, and the JSON rules game authors rely on
/// (a field added or dropped needs no step). A08.
/// </summary>
public class SaveSchemaTests
{
    // ---- Round trips -------------------------------------------------------------------

    /// <summary>A state saved and loaded at the current version is the same state, and nothing was upgraded.</summary>
    [Fact]
    public void ASavedStateLoadsBackEqual()
    {
        var schema = SampleSaves.Schema();
        var state = SampleSave.Full();

        var load = schema.Load(schema.CurrentVersion, schema.Save(state));

        Assert.Equal(SaveOutcome.Ok, load.Outcome);
        Assert.False(load.Upgraded);
        Assert.Equal(state, load.State);
        SaveFixtures.AssertRoundTrips(schema, state);
    }

    /// <summary>Compact text: no line breaks and no indentation (saves are written often, inside the host's envelope).</summary>
    [Fact]
    public void ASaveIsCompact()
    {
        string text = SampleSaves.Schema().Save(SampleSave.Full());

        Assert.DoesNotContain('\n', text);
        Assert.DoesNotContain('\r', text);
        Assert.StartsWith("{\"Name\":\"Ridge run\",\"Turn\":41,", text);
    }

    /// <summary>The numbers a game can't afford to lose a bit of: a generator's whole state, a wallet's, sums of doubles.</summary>
    [Theory]
    [InlineData(uint.MaxValue, long.MaxValue, 0.30000000000000004)]
    [InlineData(0u, long.MinValue, double.Epsilon)]
    [InlineData(1u, -1L, double.MaxValue)]
    [InlineData(2147483648u, 9007199254740993L, -0.0)]
    [InlineData(123456789u, 0L, 1e-300)]
    [InlineData(4294967294u, 1L, -123.456789012345678)]
    public void NumbersSurviveARoundTripExactly(uint seed, long banked, double rate)
    {
        var schema = SampleSaves.Schema();
        var state = SampleSave.Full() with { Seed = seed, Banked = banked, Rate = rate };

        var loaded = schema.Load(3, schema.Save(state)).State!;

        Assert.Equal(seed, loaded.Seed);
        Assert.Equal(banked, loaded.Banked);
        Assert.Equal(BitConverter.DoubleToInt64Bits(rate), BitConverter.DoubleToInt64Bits(loaded.Rate));
    }

    /// <summary>0.1 + 0.2, as a game would add them, is not 0.3, and must come back as what it is.</summary>
    [Fact]
    public void ASumOfDoublesComesBackAsItIs()
    {
        var schema = SampleSaves.Schema();
        double sum = 0.1 + 0.2;

        var loaded = schema.Load(3, schema.Save(SampleSave.Full() with { Rate = sum })).State!;

        Assert.Equal(sum, loaded.Rate);
        Assert.NotEqual(0.3, loaded.Rate);
    }

    /// <summary>Numbers that pass through an upgrade step's tree are exact too: the tree keeps the text's number.</summary>
    [Fact]
    public void NumbersSurviveAnUpgradeExactly()
    {
        const string v2 = "{\"Name\":\"x\",\"Seed\":4294967295,\"Banked\":9223372036854775807,\"Rate\":0.30000000000000004,\"Wins\":1,\"Losses\":2}";

        var loaded = SampleSaves.Schema().Load(2, v2).State!;

        Assert.Equal(uint.MaxValue, loaded.Seed);
        Assert.Equal(long.MaxValue, loaded.Banked);
        Assert.Equal(0.1 + 0.2, loaded.Rate);
        Assert.Equal(3, loaded.Played);
    }

    /// <summary>A null state is a defect of the game, and so is a number JSON can't hold: found at the save, never written.</summary>
    [Fact]
    public void ANullStateOrANumberJsonCantHoldThrowsAtTheSave()
    {
        var schema = SampleSaves.Schema();

        Assert.Throws<ArgumentNullException>(() => schema.Save(null!));
        Assert.Throws<ArgumentException>(() => schema.Save(SampleSave.Full() with { Rate = double.NaN }));
        Assert.Throws<ArgumentException>(() => schema.Save(SampleSave.Full() with { Rate = double.PositiveInfinity }));
    }

    // ---- Upgrade steps -----------------------------------------------------------------

    /// <summary>Version 1 runs both steps, in order: the rename, then the field worked out from two others.</summary>
    [Fact]
    public void AVersionOneSaveRunsBothStepsInOrder()
    {
        const string v1 = "{\"Title\":\"Old run\",\"Turn\":12,\"Seed\":99,\"Banked\":500,\"Rate\":0.5,\"Wins\":7,\"Losses\":3," +
                          "\"Scores\":[2,7],\"Home\":{\"X\":4,\"Y\":5},\"Counts\":{\"plants\":1}}";
        var ran = new List<int>();

        var load = SampleSaves.Schema(ran).Load(1, v1);

        Assert.Equal(SaveOutcome.Ok, load.Outcome);
        Assert.True(load.Upgraded);
        Assert.Equal(new[] { 1, 2 }, ran);
        Assert.Equal(new SampleSave
        {
            Name = "Old run", Turn = 12, Seed = 99, Banked = 500, Rate = 0.5, Wins = 7, Played = 10,
            Scores = new() { 2, 7 }, Home = new(4, 5), Counts = new() { ["plants"] = 1 },
        }, load.State);
    }

    /// <summary>
    /// Version 2 runs only the second step. Its text still holds a stale "Title": had the
    /// first step run, the name would be "stale".
    /// </summary>
    [Fact]
    public void AVersionTwoSaveRunsOnlyTheSecondStep()
    {
        const string v2 = "{\"Name\":\"Right\",\"Title\":\"stale\",\"Turn\":3,\"Wins\":2,\"Losses\":5}";
        var ran = new List<int>();

        var load = SampleSaves.Schema(ran).Load(2, v2);

        Assert.Equal(SaveOutcome.Ok, load.Outcome);
        Assert.True(load.Upgraded);
        Assert.Equal(new[] { 2 }, ran);
        Assert.Equal("Right", load.State!.Name);
        Assert.Equal(7, load.State.Played);
    }

    /// <summary>A current save runs no step.</summary>
    [Fact]
    public void ACurrentSaveRunsNoStep()
    {
        var ran = new List<int>();
        var schema = SampleSaves.Schema(ran);

        var load = schema.Load(3, schema.Save(SampleSave.Full()));

        Assert.False(load.Upgraded);
        Assert.Empty(ran);
    }

    /// <summary>A schema at version 1 has no steps, and loads its saves.</summary>
    [Fact]
    public void ASchemaAtVersionOneNeedsNoStep()
    {
        var schema = new SaveSchema<SampleSpot>(1);

        var load = schema.Load(1, schema.Save(new SampleSpot(3, 4)));

        Assert.Equal(1, schema.CurrentVersion);
        Assert.Equal(new SaveLoad<SampleSpot>(SaveOutcome.Ok, new SampleSpot(3, 4), false), load);
    }

    /// <summary>Upgrade returns the schema itself, for chaining.</summary>
    [Fact]
    public void UpgradeChains()
    {
        var schema = new SaveSchema<SampleSave>(3);

        Assert.Same(schema, schema.Upgrade(1, o => o));
        Assert.Same(schema, schema.Upgrade(2, o => o));
    }

    // ---- Newer -------------------------------------------------------------------------

    /// <summary>A later build's save is Newer, and its text is never parsed: garbage, or none at all, makes no difference.</summary>
    [Theory]
    [InlineData(4, "garbage {{{ not json")]
    [InlineData(4, "")]
    [InlineData(4, null)]
    [InlineData(int.MaxValue, "{\"Name\":\"from far ahead\"}")]
    public void ALaterVersionIsNewerAndNeverParsed(int version, string? payload)
    {
        var ran = new List<int>();
        int checks = 0;
        var schema = new SaveSchema<SampleSave>(3, _ => { checks++; return true; })
            .Upgrade(1, SampleSaves.RenameTitle(ran))
            .Upgrade(2, SampleSaves.WorkOutPlayed(ran));

        var load = schema.Load(version, payload!);

        Assert.Equal(new SaveLoad<SampleSave>(SaveOutcome.Newer, null, false), load);
        Assert.Empty(ran);
        Assert.Equal(0, checks);
    }

    // ---- Corrupt -----------------------------------------------------------------------

    /// <summary>Text that is not a JSON object, or not one of this state's shape, is Corrupt, never a guess.</summary>
    [Theory]
    [InlineData("", "empty")]
    [InlineData("   ", "blank")]
    [InlineData("[]", "an array")]
    [InlineData("null", "JSON's null")]
    [InlineData("42", "a number")]
    [InlineData("\"text\"", "a string")]
    [InlineData("{\"Name\":\"cut off", "cut off inside a string")]
    [InlineData("{\"Name\":\"x\",\"Turn\":", "cut off after a name")]
    [InlineData("{\"Name\":\"x\"", "cut off before the brace")]
    [InlineData("{\"Name\":\"x\"} trailing", "text after the object")]
    [InlineData("{\"Turn\":\"five\"}", "a string for an int")]
    [InlineData("{\"Turn\":1.5}", "a fraction for an int")]
    [InlineData("{\"Seed\":-1}", "a negative uint")]
    [InlineData("{\"Seed\":4294967296}", "a uint one past its top")]
    [InlineData("{\"Turn\":null}", "a null for an int")]
    [InlineData("{\"Name\":5}", "a number for a string")]
    [InlineData("{\"Scores\":[1,\"two\"]}", "a string in a list of ints")]
    [InlineData("{\"Scores\":{}}", "an object for a list")]
    [InlineData("{\"Home\":{\"X\":\"a\",\"Y\":1}}", "a wrong type inside the nested record")]
    [InlineData("{\"Counts\":{\"plants\":\"many\"}}", "a wrong type inside the dictionary")]
    [InlineData("{\"Turn\":1,\"Turn\":2}", "a name given twice")]
    [InlineData("{\"Name\":\"x\",}", "a trailing comma")]
    [InlineData("{/* note */\"Name\":\"x\"}", "a comment")]
    public void BadTextIsCorrupt(string payload, string what)
    {
        var load = SampleSaves.Schema().Load(3, payload);

        Assert.True(load.Outcome == SaveOutcome.Corrupt, $"{what}: {load.Outcome}");
        Assert.Null(load.State);
        Assert.False(load.Upgraded);
    }

    /// <summary>No text at all is Corrupt, not a throw: the host's bad data is still data.</summary>
    [Fact]
    public void NoTextIsCorrupt()
    {
        Assert.Equal(SaveOutcome.Corrupt, SampleSaves.Schema().Load(3, null!).Outcome);
    }

    /// <summary>Version 0 is "no version" (A34), and below it is nothing: Corrupt, even with good text.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void AVersionUnderOneIsCorrupt(int version)
    {
        var schema = SampleSaves.Schema();

        Assert.Equal(SaveOutcome.Corrupt, schema.Load(version, schema.Save(SampleSave.Full())).Outcome);
    }

    /// <summary>A step that throws makes the save Corrupt, and the steps after it do not run.</summary>
    [Fact]
    public void AThrowingStepIsCorrupt()
    {
        var ran = new List<int>();
        var schema = new SaveSchema<SampleSave>(3)
            .Upgrade(1, _ => throw new FormatException("the old text was not what the step expected"))
            .Upgrade(2, SampleSaves.WorkOutPlayed(ran));

        var load = schema.Load(1, "{\"Title\":\"x\",\"Wins\":1,\"Losses\":1}");

        Assert.Equal(new SaveLoad<SampleSave>(SaveOutcome.Corrupt, null, false), load);
        Assert.Empty(ran);
    }

    /// <summary>A step written for a field that an odd old save lacks (here "Losses") fails, and so is Corrupt.</summary>
    [Fact]
    public void AStepThatMeetsAnUnexpectedShapeIsCorrupt()
    {
        Assert.Equal(SaveOutcome.Corrupt, SampleSaves.Schema().Load(2, "{\"Name\":\"x\",\"Wins\":1}").Outcome);
    }

    /// <summary>A step that gives no object back is Corrupt.</summary>
    [Fact]
    public void AStepGivingNoObjectIsCorrupt()
    {
        var schema = new SaveSchema<SampleSave>(2).Upgrade(1, _ => null!);

        Assert.Equal(SaveOutcome.Corrupt, schema.Load(1, "{}").Outcome);
    }

    /// <summary>The game's own check answering false makes a well-formed save Corrupt.</summary>
    [Fact]
    public void TheGamesCheckRefusingIsCorrupt()
    {
        var schema = SampleSaves.Schema();

        Assert.Equal(SaveOutcome.Corrupt, schema.Load(3, "{\"Turn\":-4}").Outcome);
        // The reader gives a list JSON's null; the check is where that is refused.
        Assert.Equal(SaveOutcome.Corrupt, schema.Load(3, "{\"Scores\":null}").Outcome);
    }

    /// <summary>The game's own check throwing is Corrupt too, not a throw out of the load.</summary>
    [Fact]
    public void TheGamesCheckThrowingIsCorrupt()
    {
        var schema = new SaveSchema<SampleSave>(1, s => s.Scores[5] > 0);

        Assert.Equal(SaveOutcome.Corrupt, schema.Load(1, "{\"Scores\":[1]}").Outcome);
    }

    /// <summary>The check sees the state after every step, once.</summary>
    [Fact]
    public void TheGamesCheckSeesTheUpgradedStateOnce()
    {
        var seen = new List<SampleSave>();
        var schema = new SaveSchema<SampleSave>(3, s => { seen.Add(s); return true; })
            .Upgrade(1, SampleSaves.RenameTitle())
            .Upgrade(2, SampleSaves.WorkOutPlayed());

        schema.Load(1, "{\"Title\":\"T\",\"Wins\":2,\"Losses\":2}");

        var only = Assert.Single(seen);
        Assert.Equal("T", only.Name);
        Assert.Equal(4, only.Played);
    }

    // ---- Defects of the build ----------------------------------------------------------

    /// <summary>
    /// A step missing below the current version is the build's defect: Load throws,
    /// naming the version, before it parses anything, whatever the version and the text.
    /// </summary>
    [Theory]
    [InlineData(3, "{\"Name\":\"good\"}")]
    [InlineData(1, "garbage that is never parsed")]
    [InlineData(4, "a newer save")]
    [InlineData(0, "")]
    public void AMissingStepThrowsBeforeAnythingIsParsed(int version, string payload)
    {
        var schema = new SaveSchema<SampleSave>(3).Upgrade(1, SampleSaves.RenameTitle());

        var thrown = Assert.Throws<InvalidOperationException>(() => schema.Load(version, payload));

        Assert.Contains("from version 2", thrown.Message);
    }

    /// <summary>With no step at all the first one missing is named.</summary>
    [Fact]
    public void TheFirstMissingStepIsNamed()
    {
        var thrown = Assert.Throws<InvalidOperationException>(() => new SaveSchema<SampleSave>(4).Upgrade(2, o => o).Load(4, "{}"));

        Assert.Contains("from version 1", thrown.Message);
    }

    /// <summary>A step from outside 1 .. current - 1, or a second step from one version, can only be a mistake.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(int.MaxValue)]
    public void AStepFromOutsideTheVersionsThrows(int fromVersion)
    {
        var schema = new SaveSchema<SampleSave>(3);

        Assert.Throws<ArgumentOutOfRangeException>(() => schema.Upgrade(fromVersion, o => o));
    }

    [Fact]
    public void ASecondStepFromOneVersionThrows()
    {
        var schema = new SaveSchema<SampleSave>(3).Upgrade(1, o => o);

        Assert.Throws<ArgumentException>(() => schema.Upgrade(1, o => o));
    }

    [Fact]
    public void NoStepAtAllThrows()
    {
        Assert.Throws<ArgumentNullException>(() => new SaveSchema<SampleSave>(2).Upgrade(1, null!));
    }

    [Fact]
    public void AVersionOneSchemaTakesNoStep()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SaveSchema<SampleSave>(1).Upgrade(1, o => o));
    }

    /// <summary>Versions start at 1 (A34).</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void ASchemaUnderVersionOneThrows(int version)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SaveSchema<SampleSave>(version));
    }

    // ---- The rules game authors rely on --------------------------------------------------

    /// <summary>
    /// A field added with a sound default needs no step: an older text without it
    /// reads with the type's default. And a field dropped needs none: an unknown name
    /// is skipped.
    /// </summary>
    [Fact]
    public void AnAddedOrDroppedFieldNeedsNoStep()
    {
        var load = SampleSaves.Schema().Load(3, "{\"Name\":\"x\",\"Turn\":2,\"Retired\":true,\"Old\":{\"a\":[1]}}");

        Assert.Equal(SaveOutcome.Ok, load.Outcome);
        Assert.Equal("x", load.State!.Name);
        Assert.Empty(load.State.Scores);       // its initialiser
        Assert.Equal(new SampleSpot(0, 0), load.State.Home);
        Assert.Empty(load.State.Counts);
    }

    /// <summary>
    /// Names are matched with case, as a step's JsonObject matches them: "name" is
    /// an unknown property, not Name. A load and a step never disagree on what a key is.
    /// </summary>
    [Fact]
    public void NamesAreMatchedWithCase()
    {
        var load = SampleSaves.Schema().Load(3, "{\"name\":\"lower\"}");

        Assert.Equal(SaveOutcome.Ok, load.Outcome);
        Assert.Equal("", load.State!.Name);
    }

    /// <summary>Public fields are saved and read back, not silently left out.</summary>
    [Fact]
    public void PublicFieldsAreSaved()
    {
        var schema = new SaveSchema<FieldSave>(1);

        string text = schema.Save(new FieldSave { Count = 9 });

        Assert.Equal("{\"Count\":9}", text);
        Assert.Equal(9, schema.Load(1, text).State!.Count);
    }

    /// <summary>The trap the summary warns of: a private setter is written and not read back. The round trip finds it.</summary>
    [Fact]
    public void APrivateSetterIsWrittenButNotReadBack()
    {
        var schema = new SaveSchema<PrivateSetterSave>(1);
        var state = new PrivateSetterSave();
        state.Play(5);

        Assert.Equal(0, schema.Load(1, schema.Save(state)).State!.Turn);
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => SaveFixtures.AssertRoundTrips(schema, state));
    }

    /// <summary>
    /// A member whose type the reader can't build (the engine's own generator: its
    /// constructor's "seed" matches no property) is written fine and then reads as
    /// Corrupt; the round trip finds it. A game saves the generator's State instead.
    /// </summary>
    [Fact]
    public void AMemberTheReaderCantBuildIsCorruptAndTheRoundTripFindsIt()
    {
        var schema = new SaveSchema<GeneratorSave>(1);
        var state = new GeneratorSave { Dice = new Rng(7) };

        Assert.Equal(SaveOutcome.Corrupt, schema.Load(1, schema.Save(state)).Outcome);
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => SaveFixtures.AssertRoundTrips(schema, state));
    }

    /// <summary>The rules are one frozen instance: nothing can loosen them for one game.</summary>
    [Fact]
    public void TheJsonRulesAreFrozen()
    {
        Assert.True(SaveJson.Options.IsReadOnly);
        Assert.False(SaveJson.Options.WriteIndented);
        Assert.False(SaveJson.Options.PropertyNameCaseInsensitive);
        Assert.True(SaveJson.Options.IncludeFields);
        Assert.Throws<InvalidOperationException>(() => SaveJson.Options.WriteIndented = true);
    }

    /// <summary>A step may change the object it is given and return it, or build a new one.</summary>
    [Fact]
    public void AStepMayBuildANewObject()
    {
        var schema = new SaveSchema<SampleSpot>(2).Upgrade(1, o => new JsonObject
        {
            ["X"] = o["Left"]!.GetValue<int>(),
            ["Y"] = o["Top"]!.GetValue<int>(),
        });

        Assert.Equal(new SampleSpot(6, 8), schema.Load(1, "{\"Left\":6,\"Top\":8}").State);
    }

    public sealed class FieldSave
    {
        public int Count;
    }

    public sealed class PrivateSetterSave
    {
        public int Turn { get; private set; }
        public void Play(int turns) => Turn += turns;
    }

    public sealed class GeneratorSave
    {
        public Rng Dice { get; set; } = new(1);
    }
}

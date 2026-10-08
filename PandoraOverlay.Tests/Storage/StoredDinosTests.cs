using System.Text.Json;
using Xunit;

namespace PandoraOverlay.Tests;

public class StoredDinosTests
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    /// <summary>A real answer of /api/user/dinos (Oct 8 2026), with its id, name, description and position changed.</summary>
    private const string Answer = """
        {
            "success": true,
            "dinos": [
                {
                    "_id": "0123456789abcdef01234567",
                    "name": "Lefty",
                    "description": "Parked by the river",
                    "dinoClass": "BP_Herrerasaurus_C",
                    "growth": 1,
                    "gender": "Female",
                    "health": 225,
                    "maxHealth": 225,
                    "hunger": 35.89087677001953,
                    "maxHunger": 74.25,
                    "thirst": 839.92138671875,
                    "maxThirst": 1000,
                    "blood": 225,
                    "maxBlood": 225,
                    "stamina": 1000,
                    "maxStamina": 1000,
                    "position": { "x": -354000.5, "y": 197800.25, "z": 22234.6 },
                    "mutations": {
                        "MutationSlot1": "Efficient Digestion",
                        "MutationSlot2": "Sustained Hydration",
                        "MutationSlot3": "Accelerated Prey Drive",
                        "MutationSlot4": "Multichambered Lungs",
                        "ParentMutationSlot1": "None",
                        "ParentMutationSlot2": "None",
                        "ParentMutationSlot3": "None",
                        "ParentMutationSlot4": "None",
                        "ElderMutationSlot1A": "None",
                        "ElderMutationSlot1B": "None",
                        "ElderMutationSlot2A": "None",
                        "ElderMutationSlot2B": "None",
                        "ElderMutationSlot3A": "None",
                        "ElderMutationSlot3B": "None",
                        "ElderMutationSlot4A": "None",
                        "ElderMutationSlot4B": "None"
                    },
                    "elderStacks": 0,
                    "primeStatus": true,
                    "isCompensated": false,
                    "storedAt": "2026-09-23T09:42:33.776Z"
                }
            ],
            "storageLimit": 10
        }
        """;

    private static StoredDino One(string dinoJson) =>
        StoredDinos.ParseList(Json($$"""{"success":true,"dinos":[{{dinoJson}}],"storageLimit":2}"""))!.Dinos.Single();

    [Fact]
    public void ReadsTheWebsitesAnswer()
    {
        var list = StoredDinos.ParseList(Json(Answer))!;

        Assert.Equal(10, list.Limit);
        Assert.Equal(9, list.Free);
        var d = Assert.Single(list.Dinos);
        Assert.Equal("0123456789abcdef01234567", d.Id);
        Assert.False(d.IdIsNumber);
        Assert.Equal("Herrerasaurus", d.Species);
        Assert.Equal("Lefty", d.Name);
        Assert.Equal("Lefty", d.DisplayName);
        Assert.Equal("Parked by the river", d.Description);
        Assert.Equal("Female", d.Gender);
        Assert.Equal(1, d.Growth);
        Assert.Equal(0, d.ElderStacks);
        Assert.True(d.Prime);
        Assert.False(d.Compensated);
        Assert.Equal(new DateTime(2026, 9, 23, 9, 42, 33, 776, DateTimeKind.Utc), d.StoredAtUtc);
        Assert.Equal(DateTimeKind.Utc, d.StoredAtUtc!.Value.Kind);
        Assert.Equal(-354000.5, d.X);
        Assert.Equal(197800.25, d.Y);

        Assert.Equal("225 / 225", d.Health.Text);
        Assert.Equal(1, d.Health.Fraction);
        Assert.Equal("36 / 74", d.Hunger.Text);
        Assert.Equal(35.89087677001953 / 74.25, d.Hunger.Fraction, 6);
        Assert.Equal("840 / 1000", d.Thirst.Text);
        Assert.Equal("1000 / 1000", d.Stamina.Text);
        Assert.Equal("225 / 225", d.Blood.Text);

        Assert.Equal(new[] { "Efficient Digestion", "Sustained Hydration", "Accelerated Prey Drive", "Multichambered Lungs" }, d.Mutations);
        Assert.Empty(d.ParentMutations);
        Assert.Empty(d.ElderMutations);
        Assert.Equal(4, d.MutationCount);
    }

    [Fact]
    public void ADeclinedAnswerIsNullAndItsWordsAreKept()
    {
        var root = Json("""{"success":false,"error":"Link your Steam account first"}""");
        Assert.Null(StoredDinos.ParseList(root));
        Assert.Equal("Link your Steam account first", StoredDinos.ServerWords(root));
        Assert.Null(StoredDinos.ParseList(Json("[]")));
        Assert.Null(StoredDinos.ServerWords(Json("""{"success":false}""")));
    }

    [Fact]
    public void ServerWordsMentioningTheCookieAreDropped() =>
        Assert.Null(StoredDinos.ServerWords(Json("""{"error":"bad connect.sid=s%3Aabc"}""")));

    [Fact]
    public void AMissingListIsEmptyAndAMissingLimitIsOneSlot()
    {
        var bare = StoredDinos.ParseList(Json("""{"success":true}"""))!;
        Assert.Empty(bare.Dinos);
        Assert.Equal(1, bare.Limit);
        Assert.Equal(1, bare.Free);
        Assert.Equal(1, StoredDinos.ParseList(Json("""{"success":true,"dinos":[],"storageLimit":0}"""))!.Limit);
    }

    [Fact]
    public void EntriesWithoutAnIdAreDroppedAndANumericIdStaysANumber()
    {
        var list = StoredDinos.ParseList(Json("""
            {"success":true,"dinos":[
              {"_id":42,"dinoClass":"BP_Stegosaurus_C"},
              {"dinoClass":"BP_Troodon_C"},
              {"_id":"  ","dinoClass":"BP_Troodon_C"},
              "junk"
            ]}
            """))!;
        var d = Assert.Single(list.Dinos);
        Assert.Equal("42", d.Id);
        Assert.True(d.IdIsNumber);
        Assert.Equal("42", d.IdJson);
        Assert.Equal("\"0123456789abcdef01234567\"", StoredDinos.ParseList(Json(Answer))!.Dinos[0].IdJson);
    }

    [Fact]
    public void UnnamedIsNoName()
    {
        Assert.Null(One("""{"_id":"a","name":"Unnamed","dinoClass":"BP_Troodon_C"}""").Name);
        Assert.Null(One("""{"_id":"a","name":"  ","dinoClass":"BP_Troodon_C"}""").Name);
        Assert.Equal("Troodon", One("""{"_id":"a","dinoClass":"BP_Troodon_C"}""").DisplayName);
    }

    [Theory]
    [InlineData("BP_Deinosuchus_C", "Deinosuchus")]
    [InlineData("/Game/Dinos/BP_Tenontosaurus.BP_Tenontosaurus_C", "Tenontosaurus")]
    [InlineData("BP_Some_Thing_C_2", "Some Thing")]
    [InlineData("Stegosaurus", "Stegosaurus")]
    [InlineData("", "Unknown")]
    [InlineData(null, "Unknown")]
    public void SpeciesFollowsTheWebsitesRule(string? dinoClass, string species) =>
        Assert.Equal(species, StoredDinos.SpeciesOf(dinoClass));

    [Fact]
    public void VitalsFollowBothStorageSystems()
    {
        Assert.Equal(new StoredVital(225, 225, 1), StoredDinos.Vital(225, 225));
        Assert.Equal(new StoredVital(100, 200, 0.5), StoredDinos.Vital(0.5, 200));   // the older system: a fraction of the maximum
        Assert.Equal(new StoredVital(300, 200, 1), StoredDinos.Vital(300, 200));     // over the top never reads as more than full
        Assert.Equal(new StoredVital(1, 1, 1), StoredDinos.Vital(1, 1));             // a maximum of 1 is not the fraction rule
        Assert.Equal(new StoredVital(10, 0, 0), StoredDinos.Vital(10, 0));
        Assert.Equal("10", StoredDinos.Vital(10, 0).Text);
        Assert.Equal(new StoredVital(0, 0, 0), StoredDinos.Vital(double.NaN, -5));
    }

    [Fact]
    public void ElderSlotsArePairsAndNoneIsEmpty()
    {
        var d = One("""
            {"_id":"a","dinoClass":"BP_Troodon_C","mutations":{
              "MutationSlot1":"None","MutationSlot2":"Hemomania",
              "ParentMutationSlot3":"Nocturnal",
              "ElderMutationSlot1A":"Reinforced Tendons","ElderMutationSlot1B":"Thick Hide",
              "ElderMutationSlot2A":"None","ElderMutationSlot2B":"Enlarged Meniscus",
              "ElderMutationSlot3A":"","ElderMutationSlot4A":"none"
            }}
            """);
        Assert.Equal(new[] { "Hemomania" }, d.Mutations);
        Assert.Equal(new[] { "Nocturnal" }, d.ParentMutations);
        Assert.Equal(new[] { "Reinforced Tendons + Thick Hide", "Enlarged Meniscus" }, d.ElderMutations);
        Assert.Equal(5, d.MutationCount); // the website's badge counts every filled slot, both halves of a pair included
    }

    [Fact]
    public void NoPositionZeroOrOffTheIslandIsNoPosition()
    {
        Assert.Null(One("""{"_id":"a"}""").X);
        Assert.Null(One("""{"_id":"a","position":{"x":0,"y":0,"z":5}}""").X);
        Assert.Null(One("""{"_id":"a","position":{"x":900000000,"y":10}}""").Y);
        Assert.Null(One("""{"_id":"a","position":{"x":"12","y":10}}""").X);
        Assert.Equal(10, One("""{"_id":"a","position":{"x":12,"y":10}}""").Y);
    }

    [Fact]
    public void StoredAtMayBeMillisecondsOrMissing()
    {
        Assert.Equal(new DateTime(2026, 9, 23, 9, 42, 33, 776, DateTimeKind.Utc),
                     One("""{"_id":"a","storedAt":1790156553776}""").StoredAtUtc);
        Assert.Null(One("""{"_id":"a"}""").StoredAtUtc);
        Assert.Null(One("""{"_id":"a","storedAt":"not a date"}""").StoredAtUtc);
    }

    [Fact]
    public void GrowthAndGenderAreTolerant()
    {
        var d = One("""{"_id":"a","growth":1.7,"gender":"male","elderStacks":-3}""");
        Assert.Equal(1, d.Growth);
        Assert.Equal("Male", d.Gender);
        Assert.Equal(0, d.ElderStacks);
        Assert.Null(One("""{"_id":"a"}""").Gender);
    }

    // ---- The two writes ---------------------------------------------------------------

    [Fact]
    public void TheRenameBodyIsTheWebsitesShape()
    {
        var d = StoredDinos.ParseList(Json(Answer))!.Dinos[0];
        var body = Json(StoredDinos.RenameBody(d, "Lefty \"II\"", "Back by the river"));
        Assert.Equal("0123456789abcdef01234567", body.GetProperty("dinoId").GetString());
        Assert.Equal("Lefty \"II\"", body.GetProperty("name").GetString());
        Assert.Equal("Back by the river", body.GetProperty("description").GetString());

        var numeric = One("""{"_id":42}""");
        Assert.Equal(JsonValueKind.Number, Json(StoredDinos.RenameBody(numeric, "", "")).GetProperty("dinoId").ValueKind);
    }

    [Fact]
    public void TheDeleteBodyCarriesOnlyTheId()
    {
        var body = Json(StoredDinos.DeleteBody(One("""{"_id":"abc"}""")));
        Assert.Equal("abc", body.GetProperty("dinoId").GetString());
        Assert.Single(body.EnumerateObject());
    }

    [Fact]
    public void InputIsOneLineWithinTheWebsitesLimits()
    {
        Assert.Equal("Lefty the second", StoredDinos.CleanName("  Lefty\tthe\r\nsecond  "));
        Assert.Equal("", StoredDinos.CleanName(null));
        Assert.Equal(StoredDinos.MaxNameLength, StoredDinos.CleanName(new string('a', 60)).Length);
        Assert.Equal(StoredDinos.MaxDescriptionLength, StoredDinos.CleanDescription(new string('b', 250)).Length);
    }

    [Fact]
    public void ARenameTakesWhatTheWebsiteKept()
    {
        var d = StoredDinos.ParseList(Json(Answer))!.Dinos[0];

        var kept = StoredDinos.ParseRename(Json("""{"success":true,"name":"Righty","description":"Moved"}"""), d, "x", "y");
        Assert.Equal(StorageEditOutcome.Ok, kept.Outcome);
        Assert.Equal("Righty", kept.Dino!.Name);
        Assert.Equal("Moved", kept.Dino.Description);
        Assert.Equal(d.Id, kept.Dino.Id);
        Assert.Equal(d.Mutations, kept.Dino.Mutations);

        var silent = StoredDinos.ParseRename(Json("""{"success":true}"""), d, "Sent", "");
        Assert.Equal("Sent", silent.Dino!.Name);
        Assert.Null(silent.Dino.Description);

        var unnamed = StoredDinos.ParseRename(Json("""{"success":true,"name":"Unnamed","description":null}"""), d, "", "");
        Assert.Null(unnamed.Dino!.Name);
        Assert.Equal("Herrerasaurus", unnamed.Dino.DisplayName);
    }

    [Fact]
    public void ARefusedOrBrokenRenameSaysSo()
    {
        var d = One("""{"_id":"a"}""");
        var refused = StoredDinos.ParseRename(Json("""{"error":"Name too long"}"""), d, "x", "y");
        Assert.Equal(StorageEditOutcome.Refused, refused.Outcome);
        Assert.Equal("Name too long", refused.Message);
        Assert.Null(refused.Dino);
        Assert.Equal(StorageEditOutcome.Failed, StoredDinos.ParseRename(Json("\"ok\""), d, "x", "y").Outcome);
    }

    [Fact]
    public void ADeleteIsOkRefusedOrFailed()
    {
        Assert.Equal(StorageEditOutcome.Ok, StoredDinos.ParseDelete(Json("""{"success":true}""")).Outcome);
        var refused = StoredDinos.ParseDelete(Json("""{"success":false,"error":"Dino not found"}"""));
        Assert.Equal(StorageEditOutcome.Refused, refused.Outcome);
        Assert.Equal("Dino not found", refused.Message);
        Assert.Equal(StorageEditOutcome.Failed, StoredDinos.ParseDelete(Json("[]")).Outcome);
    }

    [Fact]
    public void TheListFollowsARenameAndADelete()
    {
        var list = StoredDinos.ParseList(Json("""{"success":true,"dinos":[{"_id":"a","name":"One"},{"_id":"b","name":"Two"}],"storageLimit":3}"""))!;
        var renamed = list.With(list.Dinos[1] with { Name = "Deux" });
        Assert.Equal(new[] { "One", "Deux" }, renamed.Dinos.Select(d => d.Name));
        var smaller = renamed.Without("a");
        Assert.Equal("b", Assert.Single(smaller.Dinos).Id);
        Assert.Equal(2, smaller.Free);
        Assert.Equal(2, list.Dinos.Count); // the original stays as it was
    }

    // ---- Labels -------------------------------------------------------------------------

    [Theory]
    [InlineData(0.0, "0% · hatchling")]
    [InlineData(0.58, "58% · subadult")]
    [InlineData(0.996, "99% · adult")]
    [InlineData(1.0, "100% · fully grown")]
    public void GrowthReadsAsPercentAndStage(double growth, string text) =>
        Assert.Equal(text, StoredDinos.GrowthText(growth));

    [Fact]
    public void TheStoredDateLeavesOutThisYear()
    {
        var when = new DateTime(2026, 9, 23, 11, 42, 0);
        Assert.Equal("23 Sep · 11:42", StoredDinos.StoredText(when, 2026));
        Assert.Equal("23 Sep 2026", StoredDinos.StoredText(when, 2027));
    }
}

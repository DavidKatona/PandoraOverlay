using System.Text.Json;
using Xunit;

namespace PandoraOverlay.Tests;

public class CookieCleanTests
{
    [Theory]
    [InlineData("cookie: connect.sid=abc;", "connect.sid=abc")]
    [InlineData("\"connect.sid=abc\"", "connect.sid=abc")]
    [InlineData("connect.sid=abc;\r\ncf_clearance=def", "connect.sid=abc; cf_clearance=def")]
    [InlineData("  connect.sid=abc;;  ", "connect.sid=abc")]
    [InlineData("connect.sid=abc; cf_clearance=def", "connect.sid=abc; cf_clearance=def")]
    public void CleansPasteAccidents(string raw, string expected) =>
        Assert.Equal(expected, SettingsWindow.Clean(raw));
}

public class CalibrationParsingTests
{
    [Fact]
    public void FindsCalibrationInFlatResponse()
    {
        using var doc = JsonDocument.Parse(
            """{"success":true,"scaleX":0.002,"scaleY":-0.002,"offsetX":1160.9,"offsetY":1223.3,"mapSize":2500,"pinOffset":{"x":-15,"y":25}}""");
        var cal = PandoraClient.FindCalibration(doc.RootElement);
        Assert.NotNull(cal);
        Assert.Equal(2500, cal!.MapSize);
        Assert.Equal(-0.002, cal.ScaleY);
        Assert.Equal(-15, cal.PinOffsetX);
        Assert.Equal(25, cal.PinOffsetY);
    }

    [Fact]
    public void FindsCalibrationWhenWrapped()
    {
        using var doc = JsonDocument.Parse(
            """{"data":{"calibration":{"scaleX":1,"scaleY":1,"offsetX":0,"offsetY":0,"mapSize":100}}}""");
        var cal = PandoraClient.FindCalibration(doc.RootElement);
        Assert.NotNull(cal);
        Assert.Equal(0, cal!.PinOffsetX); // absent pinOffset reads as no shift
        Assert.Equal(0, cal.PinOffsetY);
    }

    [Fact]
    public void ReturnsNullWithoutCalibrationFields()
    {
        using var doc = JsonDocument.Parse("""{"success":true,"scaleX":0.002}""");
        Assert.Null(PandoraClient.FindCalibration(doc.RootElement));
    }
}

public class PrimeParsingTests
{
    private static readonly DateTime Now = new(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);

    private static PrimeCheckResult Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return PandoraClient.ParsePrime(doc.RootElement, Now, "Deinosuchus");
    }

    [Fact]
    public void ParsesNumericConditionKeys()
    {
        var result = Parse(
            """{"success":true,"data":{"isPrime":false,"isEligible":true,"conditions":{"1":true,"2":false,"3":true,"10":true}}}""");
        Assert.Equal(PrimeCheckOutcome.Ok, result.Outcome);
        var snap = result.Snapshot!;
        Assert.False(snap.IsPrime);
        Assert.True(snap.IsEligible);
        Assert.Equal(new[] { true, false, true, false, false, false, false, false, false, true }, snap.Conditions);
        Assert.Equal(Now, snap.CheckedAtUtc);
        Assert.Equal("Deinosuchus", snap.Dino);
    }

    [Fact]
    public void ParsesPrefixedKeysAndAlternateStatusNames()
    {
        var result = Parse(
            """{"success":true,"data":{"isPrimeElder":true,"isEligiblePrime":true,"conditions":{"c1":true,"c5":1,"c6":0}}}""");
        var snap = result.Snapshot!;
        Assert.True(snap.IsPrime);
        Assert.True(snap.IsEligible);
        Assert.True(snap.Conditions[0]);
        Assert.True(snap.Conditions[4]);
        Assert.False(snap.Conditions[5]);
    }

    [Fact]
    public void MapsCooldownWithRemainingTime()
    {
        var result = Parse("""{"success":false,"error":"cooldown","remainingMs":125000}""");
        Assert.Equal(PrimeCheckOutcome.Cooldown, result.Outcome);
        Assert.Equal(TimeSpan.FromSeconds(125), result.Remaining);
    }

    [Theory]
    [InlineData("""{"success":true,"remainingMs":42000}""", 42.0)]   // e.g. a shortened supporter-rank cooldown
    [InlineData("""{"success":true,"remainingMs":0}""", 0.0)]
    [InlineData("""{"success":true}""", 0.0)]                        // like the frontend: no running cooldown
    public void ReadsTheServersCooldown(string json, double expectedSeconds)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), PandoraClient.ParsePrimeCooldown(doc.RootElement));
    }

    [Theory]
    [InlineData("""{"success":false,"remainingMs":42000}""")]
    [InlineData("""[]""")]
    public void UnusableCooldownAnswersReadAsUnknown(string json)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.Null(PandoraClient.ParsePrimeCooldown(doc.RootElement));
    }

    [Fact]
    public void MapsNotInGame() =>
        Assert.Equal(PrimeCheckOutcome.NotInGame, Parse("""{"success":false,"error":"not_in_game"}""").Outcome);

    [Theory]
    [InlineData("""{"success":false,"error":"something else"}""")]
    [InlineData("""{"success":true}""")]
    [InlineData("""[1,2,3]""")]
    public void AnythingElseFailsWithoutEchoingTheServer(string json)
    {
        var result = Parse(json);
        Assert.Equal(PrimeCheckOutcome.Failed, result.Outcome);
        Assert.DoesNotContain("something else", result.Reason ?? "");
    }
}

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("v1.7.0", "1.6.0.0", true)]
    [InlineData("V2.0.0", "1.6.0.0", true)]
    [InlineData("v1.6.0", "1.6.0.0", false)]
    [InlineData("v1.5.2", "1.6.0.0", false)]
    [InlineData("garbage", "1.6.0.0", false)]
    public void ComparesReleaseTagsAgainstCurrentVersion(string tag, string current, bool expected) =>
        Assert.Equal(expected, UpdateChecker.IsNewer(tag, Version.Parse(current)));
}

public class FriendsParsingTests
{
    private static IReadOnlyList<FriendState>? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return PandoraClient.ParseFriends(doc.RootElement);
    }

    [Fact]
    public void ReadsTheRosterWithPresenceFlags()
    {
        var friends = Parse("""
            {"success":true,"friends":[
              {"steamId":"1","name":"Ann","inGame":true,"hideLocation":false,"dino":"Deinosuchus","gender":"Female",
               "growth":0.42,"health":0.9,"stamina":1,"hunger":0.5,"thirst":0.6,"yaw":-90.5,
               "headFractured":false,"bodyFractured":false,"legsFractured":true,"x":6167.09,"y":-316782.07},
              {"steamId":"2","name":"Bob","inGame":false,"hideLocation":false,"x":null,"y":null},
              {"steamId":"3","name":"Cy","inGame":true,"hideLocation":true,"x":1,"y":2}
            ]}
            """);

        Assert.NotNull(friends);
        Assert.Equal(3, friends!.Count);
        var ann = friends[0];
        Assert.True(ann.OnMap);
        Assert.Equal("Deinosuchus", ann.Dino);
        Assert.Equal(0.42, ann.Growth);
        Assert.Equal(-90.5, ann.Yaw);
        Assert.True(ann.LegsFractured);
        Assert.Equal(6167.09, ann.X);
        Assert.False(friends[1].OnMap);   // not in game, no coordinates
        Assert.Null(friends[1].X);
        Assert.False(friends[2].OnMap);   // hidden location wins over the coordinates
        Assert.True(friends[2].InGame);
    }

    [Fact]
    public void ToleratesMissingFieldsAndDropsBadEntries()
    {
        var friends = Parse("""{"success":true,"friends":[{"steamId":"1"},{"name":"no id"},"junk",{"steamId":"2","x":"far"}]}""");
        Assert.NotNull(friends);
        var ids = friends!.Select(f => f.SteamId).ToArray();
        Assert.Equal(new[] { "1" }, ids); // "no id" and "junk" dropped; the unparseable x drops that entry too
        Assert.False(friends[0].InGame);
    }

    [Fact]
    public void EmptyOrMissingListIsAnEmptyRoster()
    {
        Assert.Empty(Parse("""{"success":true,"friends":[]}""")!);
        Assert.Empty(Parse("""{"success":true}""")!);
    }

    [Fact]
    public void DeclinedIsNull()
    {
        Assert.Null(Parse("""{"success":false,"error":"no_steam_link"}"""));
        Assert.Null(Parse("""[1,2,3]"""));
    }
}

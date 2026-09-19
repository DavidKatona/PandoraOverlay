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

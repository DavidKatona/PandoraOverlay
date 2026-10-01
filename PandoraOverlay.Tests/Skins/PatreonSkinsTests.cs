using System.Text.Json;
using Xunit;

namespace PandoraOverlay.Tests;

public class PatreonSkinsTests
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    private const string List = """
        {
          "success": true,
          "isEditor": false,
          "skins": [
            { "id": 12, "name": "Ember", "description": "Glowing coals", "image": "/img/ember.png", "thumbnail": "/img/ember_t.png",
              "sv": 3, "requiredRole": "Patreon | Silver", "requiredRoleId": "1403681132398116935", "locked": false,
              "colors": { "md": {"R":1,"G":0,"B":0}, "m": {"R":0,"G":1,"B":0}, "b": {"R":0,"G":0,"B":1}, "f": {"R":0,"G":0,"B":0},
                          "u": {"R":1,"G":1,"B":1}, "d1": {"R":0.5,"G":0.5,"B":0.5} } },
            { "id": "abc-7", "name": "Glacier", "image": "https://cdn.example.com/g.png", "requiredRole": "Pandorian Fanatic",
              "requiredRoleId": "1403681314757935166", "locked": true },
            { "id": 13, "name": "  " },
            { "name": "No id" },
            "junk"
          ]
        }
        """;

    [Fact]
    public void ParsesTheListAndDropsUnusableEntries()
    {
        var skins = PatreonSkins.ParseList(Json(List))!;

        Assert.Equal(2, skins.Count);
        var ember = skins[0];
        Assert.Equal("12", ember.Id);
        Assert.True(ember.IdIsNumber);
        Assert.Equal("Ember", ember.Name);
        Assert.Equal("Glowing coals", ember.Description);
        Assert.Equal("Patreon | Silver", ember.RequiredRole);
        Assert.False(ember.Locked);
        Assert.Equal("/img/ember_t.png", ember.Picture); // the thumbnail wins, like on the site

        var glacier = skins[1];
        Assert.Equal("abc-7", glacier.Id);
        Assert.False(glacier.IdIsNumber);
        Assert.True(glacier.Locked);
        Assert.Null(glacier.Description);
        Assert.Equal("https://cdn.example.com/g.png", glacier.Picture); // no thumbnail: the image
    }

    [Fact]
    public void TheIdGoesBackWithTheTypeItCameWith()
    {
        var skins = PatreonSkins.ParseList(Json(List))!;
        Assert.Equal("12", skins[0].IdJson);
        Assert.Equal("\"abc-7\"", skins[1].IdJson);
        Assert.Equal("\"a\\u0022b\"", PatreonSkins.IdJson("a\"b", isNumber: false)); // a quote in an id can't break out of the body
    }

    [Fact]
    public void ColoursFollowTheSitesConversionAndOrder()
    {
        var colours = PatreonSkins.ParseList(Json(List))![0].Colours;

        Assert.Equal(7, colours.Count);
        Assert.Equal("#ff0000", colours[0]); // md
        Assert.Equal("#00ff00", colours[1]); // m
        Assert.Equal("#0000ff", colours[2]); // b
        Assert.Equal("#000000", colours[3]); // f
        Assert.Equal("#ffffff", colours[4]); // u
        Assert.Equal("#aeaeae", colours[5]); // d1: 0.5^(1/1.8) * 255 = 173.5 → 174
        Assert.Equal("#808080", colours[6]); // e missing: the site's grey
    }

    [Fact]
    public void ASkinWithoutColoursIsAllGrey()
    {
        var glacier = PatreonSkins.ParseList(Json(List))![1];
        Assert.All(glacier.Colours, c => Assert.Equal("#808080", c));
    }

    [Theory]
    [InlineData(-1, "#000000")]
    [InlineData(2, "#ffffff")]
    public void ColourChannelsAreClamped(double value, string expected) =>
        Assert.Equal(expected, PatreonSkins.ToHex(value, value, value));

    [Theory]
    [InlineData("""{"success":false}""")]
    [InlineData("""{"error":"unauthorized"}""")]
    [InlineData("[]")]
    public void ADeclinedListIsAMiss(string json) => Assert.Null(PatreonSkins.ParseList(Json(json)));

    [Fact]
    public void AMissingListIsEmptyNotAMiss() => Assert.Empty(PatreonSkins.ParseList(Json("""{"success":true}"""))!);

    [Fact]
    public void SortsWhatYouCanApplyFirstThenByTierThenName()
    {
        var skins = PatreonSkins.ParseList(Json("""
            {"success":true,"skins":[
              {"id":1,"name":"Zeta","requiredRoleId":"1403680490732523550","locked":true},
              {"id":2,"name":"beta","requiredRoleId":"1403681132398116935","locked":false},
              {"id":3,"name":"Alpha","requiredRoleId":"1403681132398116935","locked":false},
              {"id":4,"name":"Gamma","requiredRoleId":"1403680490732523550","locked":false},
              {"id":5,"name":"Omega","requiredRoleId":"999","locked":false}
            ]}
            """))!;

        Assert.Equal(new[] { "Gamma", "Alpha", "beta", "Omega", "Zeta" }, PatreonSkins.Sorted(skins).Select(s => s.Name));
    }

    [Fact]
    public void SearchLooksAtNameDescriptionAndTier()
    {
        var ember = PatreonSkins.ParseList(Json(List))![0];
        Assert.True(PatreonSkins.Matches(ember, null));
        Assert.True(PatreonSkins.Matches(ember, "  EMB "));
        Assert.True(PatreonSkins.Matches(ember, "coals"));
        Assert.True(PatreonSkins.Matches(ember, "silver"));
        Assert.False(PatreonSkins.Matches(ember, "glacier"));
    }

    [Theory]
    [InlineData("https://cdn.example.com/a.png", "https://cdn.example.com/a.png")]
    [InlineData("//cdn.example.com/a.png", "https://cdn.example.com/a.png")]
    [InlineData("/img/a.png", "https://islapandora.eu/img/a.png")]
    [InlineData("img/a.png", "https://islapandora.eu/img/a.png")]
    public void PicturesResolveToHttps(string raw, string expected) =>
        Assert.Equal(expected, PatreonSkins.ResolvePicture(raw)!.AbsoluteUri);

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("http://cdn.example.com/a.png")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("file:///C:/secret.png")]
    [InlineData("javascript:alert(1)")]
    public void AnythingElseIsNoPicture(string? raw) => Assert.Null(PatreonSkins.ResolvePicture(raw));

    [Fact]
    public void ApplySuccess()
    {
        var result = PatreonSkins.ParseApply(Json("""{"success":true}"""), "Ember", 2);
        Assert.Equal(SkinApplyOutcome.Ok, result.Outcome);
        Assert.Equal("Ember", result.SkinName);
        Assert.Equal(2, result.Pattern);
    }

    [Theory]
    [InlineData("""{"success":false,"message":"You can apply a skin again in 12 minutes."}""", "You can apply a skin again in 12 minutes.")]
    [InlineData("""{"error":"not_in_game"}""", "not_in_game")]
    [InlineData("""{"success":false,"message":"first","error":"second"}""", "first")]
    public void ARefusalCarriesTheServersWords(string json, string expected)
    {
        var result = PatreonSkins.ParseApply(Json(json), "Ember", 0);
        Assert.Equal(SkinApplyOutcome.Refused, result.Outcome);
        Assert.Equal(expected, result.Message);
    }

    [Fact]
    public void ARefusalWithoutWordsHasNone()
    {
        var result = PatreonSkins.ParseApply(Json("""{"success":false}"""), "Ember", 0);
        Assert.Equal(SkinApplyOutcome.Refused, result.Outcome);
        Assert.Null(result.Message);
    }

    [Fact]
    public void AnUnexpectedApplyResponseFails() =>
        Assert.Equal(SkinApplyOutcome.Failed, PatreonSkins.ParseApply(Json("[1,2]"), "Ember", 0).Outcome);

    [Fact]
    public void ServerTextIsMadeSafeToShow()
    {
        Assert.Equal("one two three", PatreonSkins.Clean("  one\r\n two\tthree\u0007 ", 140));
        Assert.Null(PatreonSkins.Clean("   ", 140));

        var cut = PatreonSkins.Clean(new string('x', 500), 140)!;
        Assert.Equal(140, cut.Length);
        Assert.EndsWith("…", cut);
    }

    [Theory]
    [InlineData("bad request: connect.sid=s%3Aabc123")]
    [InlineData("headers were CF_CLEARANCE=xyz")]
    public void TextThatCouldCarryTheCookieIsDroppedWhole(string said)
    {
        Assert.Null(PatreonSkins.Clean(said, 140));
        Assert.Null(PatreonSkins.ParseApply(Json(JsonSerializer.Serialize(new { success = false, message = said })), "Ember", 0).Message);
    }

    [Theory]
    [InlineData(0, 'A')]
    [InlineData(5, 'F')]
    [InlineData(-3, 'A')]
    [InlineData(99, 'F')]
    public void PatternLetters(int index, char letter) => Assert.Equal(letter, PatreonSkins.PatternLetter(index));

    [Fact]
    public void TierRanksFollowTheSite()
    {
        Assert.Equal(1, PatreonSkins.RankOf("1403680490732523550"));
        Assert.Equal(4, PatreonSkins.RankOf("1403681314757935166"));
        Assert.Equal(99, PatreonSkins.RankOf("something else"));
        Assert.Equal(99, PatreonSkins.RankOf(null));
    }

    [Fact]
    public void TheFeedLineNamesSkinAndPattern()
    {
        var line = SelfActivity.SkinLine("Ember", 2, new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc));
        Assert.Equal("Skin applied · Ember (pattern C)", line.Text);
        Assert.Equal(FeedKind.Skin, line.Kind);
        Assert.True(line.Mine);
    }

    [Fact]
    public void ASkinChoiceSurvivesTheConfigFile()
    {
        var config = new OverlayConfig();
        config.SkinChoices["Deinosuchus"] = new SkinChoice("12", true, "Ember", 2);
        config.SkinLastAppliedUtc = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

        var back = JsonSerializer.Deserialize<OverlayConfig>(JsonSerializer.Serialize(config))!;

        Assert.Equal(new SkinChoice("12", true, "Ember", 2), back.SkinChoices["Deinosuchus"]);
        Assert.Equal(config.SkinLastAppliedUtc, back.SkinLastAppliedUtc);
    }
}

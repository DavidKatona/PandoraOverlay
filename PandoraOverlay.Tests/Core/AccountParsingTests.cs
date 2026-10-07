using System.Text.Json;
using Xunit;

namespace PandoraOverlay.Tests;

public class AccountParsingTests
{
    private static AccountInfo Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return PandoraClient.ParseAccount(doc.RootElement);
    }

    /// <summary>The shape seen Oct 7 2026 (ids shortened): a normal player, Steam linked, map access.</summary>
    [Fact]
    public void ReadsASignedInPlayer()
    {
        var info = Parse("""
            {"ok":false,"authenticated":true,"hasAccess":false,"hasMapAccess":true,"isVerified":true,"friendsEnabled":true,
             "user":{"discordId":"7832","username":"dave","discriminator":"0","avatar":"https://cdn.example/a.png",
                     "steamId":"76561198000000042","linkId":"AAAA-BBBB-CCCC","roles":["1","2"]}}
            """);
        Assert.True(info.Authenticated);
        Assert.Equal("dave", info.Username);
        Assert.Equal("76561198000000042", info.SteamId);
        Assert.True(info.SteamLinked);
        Assert.Equal("AAAA-BBBB-CCCC", info.LinkId);
        Assert.True(info.HasMapAccess);
        Assert.True(info.IsVerified);
    }

    [Fact]
    public void AUserObjectAloneCountsAsSignedIn_LikeTheFrontend()
    {
        var info = Parse("""{"user":{"username":"dave","steamId":76561198000000042}}""");
        Assert.True(info.Authenticated);
        Assert.Equal("76561198000000042", info.SteamId); // a numeric id reads the same
    }

    [Fact]
    public void NoSteamAccountReadsAsNotLinked()
    {
        var info = Parse("""{"authenticated":true,"hasMapAccess":true,"user":{"username":"dave","steamId":"","linkId":null}}""");
        Assert.True(info.Authenticated);
        Assert.False(info.SteamLinked);
        Assert.Null(info.LinkId);
    }

    [Fact]
    public void NotSignedInAndJunkReadAsNone()
    {
        Assert.False(Parse("""{"authenticated":false}""").Authenticated);
        Assert.False(Parse("[1,2]").Authenticated);
        Assert.Equal(AccountInfo.None, Parse("null"));
    }
}

public class RefusalTests
{
    [Theory]
    [InlineData(403, "application/json", true)]
    [InlineData(401, "application/json", true)]
    [InlineData(403, "text/html", false)]   // a Cloudflare block page is not the site saying "no session"
    [InlineData(200, "application/json", false)]
    [InlineData(500, "application/json", false)]
    [InlineData(403, null, false)]
    public void OnlyTheSitesOwnJsonRefusalEndsTheSession(int status, string? mediaType, bool refusal) =>
        Assert.Equal(refusal, PandoraClient.IsRefusal(status, mediaType));
}

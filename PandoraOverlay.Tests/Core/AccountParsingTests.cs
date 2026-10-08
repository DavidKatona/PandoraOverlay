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

    [Fact]
    public void ReadsTheAvatarAndAsksDiscordForASmallOne()
    {
        var info = Parse("""{"authenticated":true,"user":{"username":"dave","avatar":"https://cdn.discordapp.com/avatars/7832/abc123.png"}}""");
        Assert.Equal("https://cdn.discordapp.com/avatars/7832/abc123.png?size=128", info.Avatar?.AbsoluteUri);
    }

    [Fact]
    public void NoAvatarReadsAsNone()
    {
        Assert.Null(Parse("""{"user":{"username":"dave"}}""").Avatar);
        Assert.Null(Parse("""{"user":{"username":"dave","avatar":null}}""").Avatar);
        Assert.Null(Parse("""{"user":{"username":"dave","avatar":""}}""").Avatar);
    }

    [Theory]
    [InlineData("https://cdn.discordapp.com/avatars/1/a_abc.gif", "https://cdn.discordapp.com/avatars/1/a_abc.gif?size=128")]
    [InlineData("https://cdn.discordapp.com/embed/avatars/3.png", "https://cdn.discordapp.com/embed/avatars/3.png?size=128")]
    [InlineData("https://media.discordapp.net/avatars/1/abc.webp?x=1", "https://media.discordapp.net/avatars/1/abc.webp?x=1&size=128")]
    [InlineData("https://cdn.discordapp.com/avatars/1/abc.png?size=64", "https://cdn.discordapp.com/avatars/1/abc.png?size=64")] // a size given is kept
    [InlineData("https://islapandora.eu/img/default-avatar.png", "https://islapandora.eu/img/default-avatar.png")]     // the site's own, as is
    public void TakesDiscordAndSiteAddresses(string raw, string expected) =>
        Assert.Equal(expected, PandoraClient.AvatarAddress(raw)?.AbsoluteUri);

    [Theory]
    [InlineData("http://cdn.discordapp.com/avatars/1/abc.png")]   // not https
    [InlineData("https://evil.example/avatars/1/abc.png")]        // another host
    [InlineData("https://cdn.discordapp.com.evil.example/a.png")] // a look-alike
    [InlineData("https://notdiscordapp.com/a.png")]
    [InlineData("/avatars/1/abc.png")]                            // relative
    [InlineData("abc123")]                                        // a bare hash
    [InlineData("file:///C:/a.png")]
    public void RefusesEverythingElse(string raw) => Assert.Null(PandoraClient.AvatarAddress(raw));
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

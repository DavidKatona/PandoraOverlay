using Xunit;

namespace PandoraOverlay.Tests;

public class SignInPolicyTests
{
    [Theory]
    [InlineData("https://islapandora.eu/auth/discord")]
    [InlineData("https://islapandora.eu/auth/discord/callback?code=x&iss=y")]
    [InlineData("https://www.islapandora.eu/auth/discord")]
    [InlineData("https://islapandora.eu/cdn-cgi/challenge-platform/h/b")]
    [InlineData("https://discord.com/api/oauth2/authorize?client_id=1")]
    [InlineData("https://discord.com/login")]
    [InlineData("https://canary.discord.com/oauth2/authorize")]
    [InlineData("about:blank")]
    public void TheLoginRoutesAndDiscordMayOpen(string url) => Assert.True(SignInPolicy.IsAllowed(new Uri(url)));

    [Theory]
    [InlineData("https://islapandora.eu/")]
    [InlineData("https://islapandora.eu/live-map")]
    [InlineData("http://islapandora.eu/auth/discord")]
    [InlineData("https://notdiscord.com/login")]
    [InlineData("https://discord.com.evil.example/login")]
    [InlineData("https://cdn.discordapp.com/x")]
    public void EverythingElseIsStopped(string url) => Assert.False(SignInPolicy.IsAllowed(new Uri(url)));

    [Theory]
    [InlineData("https://islapandora.eu/", true)]
    [InlineData("https://islapandora.eu/live-map", true)]
    [InlineData("https://islapandora.eu/login?error=denied", true)]
    [InlineData("https://islapandora.eu/auth/discord/callback?code=x", false)]
    [InlineData("https://islapandora.eu/cdn-cgi/challenge-platform/h/b", false)]
    [InlineData("https://discord.com/login", false)]
    [InlineData("about:blank", false)]
    public void TheLandingIsTheFirstSitePageOutsideTheLoginRoutes(string url, bool landing) =>
        Assert.Equal(landing, SignInPolicy.IsSignedInLanding(new Uri(url)));

    [Theory]
    [InlineData("https://discord.com/login", "Step 1 of 2 · Discord login")]
    [InlineData("https://discord.com/oauth2/authorize?client_id=1", "Step 2 of 2 · Allow the website")]
    [InlineData("https://discord.com/api/oauth2/authorize?client_id=1", "Step 2 of 2 · Allow the website")]
    [InlineData("https://islapandora.eu/auth/discord", "Connecting to islapandora.eu…")]
    [InlineData("about:blank", "")]
    public void TheAddressStripNamesTheStep(string url, string label) => Assert.Equal(label, SignInPolicy.StepLabel(new Uri(url)));

    [Fact]
    public void TheHeaderKeepsOnlyTheSessionCookiesInOrder()
    {
        var cookies = new[]
        {
            new KeyValuePair<string, string>("cf_clearance", "cf1"),
            new KeyValuePair<string, string>("_ga", "tracking"),
            new KeyValuePair<string, string>("connect.sid", "s%3Aabc.def")
        };
        Assert.Equal("connect.sid=s%3Aabc.def; cf_clearance=cf1", SignInPolicy.CookieHeader(cookies));
    }

    [Fact]
    public void TheSessionCookieAloneIsEnough()
    {
        var cookies = new[] { new KeyValuePair<string, string>("connect.sid", " s%3Aabc ") };
        Assert.Equal("connect.sid=s%3Aabc", SignInPolicy.CookieHeader(cookies));
    }

    [Fact]
    public void NoSessionCookieMeansNoHeader()
    {
        var cookies = new[]
        {
            new KeyValuePair<string, string>("cf_clearance", "cf1"),
            new KeyValuePair<string, string>("connect.sid", "   ")
        };
        Assert.Equal("", SignInPolicy.CookieHeader(cookies));
        Assert.Equal("", SignInPolicy.CookieHeader(Array.Empty<KeyValuePair<string, string>>()));
    }

    [Theory]
    [InlineData("connect.sid=abc", true)]
    [InlineData("cf_clearance=x; connect.sid=abc", true)]
    [InlineData("connect.sid=", false)]
    [InlineData("cf_clearance=x", false)]
    [InlineData("", false)]
    public void HasSessionLooksForTheSessionCookie(string header, bool expected) => Assert.Equal(expected, SignInPolicy.HasSession(header));
}

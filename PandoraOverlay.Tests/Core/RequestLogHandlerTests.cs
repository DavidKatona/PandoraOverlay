using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The LogNet line each request leaves. Every secret here is made up; the
/// point is that none of them, whatever rides the request or the answer,
/// reaches the line (CLAUDE.md, hard constraint 4).
/// </summary>
public class RequestLogHandlerTests
{
    private const string Cookie = "connect.sid=s%3ACOOKIESECRET";
    private const string AccessName = "x-test-access";
    private const string AccessValue = "ACCESSSECRET";

    /// <summary>Answers every request with what it was given, or throws it.</summary>
    private sealed class FakeSite : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage>? _answer;
        private readonly Exception? _failure;

        public FakeSite(Func<HttpResponseMessage> answer) => _answer = answer;

        public FakeSite(Exception failure) => _failure = failure;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _failure is not null ? Task.FromException<HttpResponseMessage>(_failure) : Task.FromResult(_answer!());
    }

    private static HttpResponseMessage Answer(HttpStatusCode status, string body, string mediaType)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };
        response.Headers.TryAddWithoutValidation("Set-Cookie", "connect.sid=s%3AROLLEDSECRET; Path=/; HttpOnly");
        return response;
    }

    /// <summary>A request like the site client's: the cookie, the access header and a body ride along.</summary>
    private static async Task<List<(LogVerbosity Level, string Line)>> SendAsync(HttpMessageHandler site, string url, HttpMethod? method = null)
    {
        var lines = new List<(LogVerbosity, string)>();
        using var client = new HttpClient(new RequestLogHandler(site, (level, line) => lines.Add((level, line))));
        using var request = new HttpRequestMessage(method ?? HttpMethod.Post, url) { Content = new StringContent("{\"name\":\"BODYSECRET\"}") };
        request.Headers.TryAddWithoutValidation("Cookie", Cookie);
        request.Headers.TryAddWithoutValidation(AccessName, AccessValue);
        try
        {
            using var _ = await client.SendAsync(request);
        }
        catch (Exception)
        {
            // the failure tests look at the line, and at the rethrow separately
        }
        return lines;
    }

    private static void AssertNoSecret(string line)
    {
        foreach (var secret in new[] { "COOKIESECRET", "ROLLEDSECRET", "connect.sid", AccessName, AccessValue, "BODYSECRET", "BLOCKEDSECRET", "TOKENSECRET", "MESSAGESECRET", "_t=" })
        {
            Assert.DoesNotContain(secret, line, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task CloudflaresBlockPageReadsAs403Html_AndNothingSecretGetsIn()
    {
        var lines = await SendAsync(new FakeSite(() => Answer(HttpStatusCode.Forbidden, "<html>BLOCKEDSECRET</html>", "text/html")),
                                    "https://islapandora.eu/api/map/mylocation?_t=1&token=TOKENSECRET");
        var (level, line) = Assert.Single(lines);
        Assert.Equal(LogVerbosity.Warning, level);
        Assert.Matches(new Regex(@"^POST /api/map/mylocation 403 html \d+ ms$"), line);
        AssertNoSecret(line);
    }

    [Fact]
    public async Task TheSitesOwnRefusalReadsAs403Json()
    {
        var lines = await SendAsync(new FakeSite(() => Answer(HttpStatusCode.Forbidden, "{\"error\":\"Forbidden\"}", "application/json")),
                                    "https://islapandora.eu/api/map/friends");
        Assert.Matches(new Regex(@"^POST /api/map/friends 403 json \d+ ms$"), Assert.Single(lines).Line);
    }

    [Fact]
    public async Task ATimedRequestAnsweredIsVerbose_AnyOtherIsLog()
    {
        var poll = await SendAsync(new FakeSite(() => Answer(HttpStatusCode.OK, "{\"inGame\":false}", "application/json")),
                                   "https://islapandora.eu/api/map/mylocation");
        Assert.Equal(LogVerbosity.Verbose, Assert.Single(poll).Level);

        var skins = await SendAsync(new FakeSite(() => Answer(HttpStatusCode.OK, "{}", "application/json")),
                                    "https://islapandora.eu/api/skins/patreon-skins");
        var (level, line) = Assert.Single(skins);
        Assert.Equal(LogVerbosity.Log, level);
        Assert.Matches(new Regex(@"^POST /api/skins/patreon-skins 200 json \d+ ms$"), line);
    }

    [Fact]
    public async Task AnotherHostIsWrittenAsItsNameAlone()
    {
        var lines = await SendAsync(new FakeSite(() => Answer(HttpStatusCode.OK, "png", "image/png")),
                                    "https://cdn.discordapp.com/avatars/123456789012345678/abcdef.png?size=128", HttpMethod.Get);
        var line = Assert.Single(lines).Line;
        Assert.Matches(new Regex(@"^GET cdn\.discordapp\.com 200 png \d+ ms$"), line);
        Assert.DoesNotContain("123456789012345678", line);
    }

    [Fact]
    public async Task AFailureIsItsTypeAndCodes_NeverItsMessage_AndIsThrownOn()
    {
        var failure = new HttpRequestException(HttpRequestError.ConnectionError, $"MESSAGESECRET {Cookie}",
                                               new SocketException((int)SocketError.ConnectionRefused));
        var lines = await SendAsync(new FakeSite(failure), "https://islapandora.eu/api/map/friends");
        var (level, line) = Assert.Single(lines);
        Assert.Equal(LogVerbosity.Warning, level);
        Assert.Matches(new Regex(@"^POST /api/map/friends failed: HttpRequestException \(ConnectionError\) <- SocketException \(ConnectionRefused\) after \d+ ms$"), line);
        AssertNoSecret(line);

        using var client = new HttpClient(new RequestLogHandler(new FakeSite(failure), (_, _) => { }));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.PostAsync("https://islapandora.eu/api/map/friends", null));
    }

    [Theory]
    [InlineData("https://islapandora.eu/api/map/mylocation", 200, LogVerbosity.Verbose)]
    [InlineData("https://islapandora.eu/api/map/friends", 200, LogVerbosity.Verbose)]
    [InlineData("https://islapandora.eu/map/api/heatmap-status", 200, LogVerbosity.Verbose)]
    [InlineData("https://islapandora.eu/map/heatmap-live.png?_t=1", 200, LogVerbosity.Verbose)]
    [InlineData("https://islapandora.eu/api/map/calibration", 200, LogVerbosity.Log)]
    [InlineData("https://islapandora.eu/auth/logout", 302, LogVerbosity.Log)]       // the sign-out's normal answer
    [InlineData("https://islapandora.eu/api/map/mylocation", 302, LogVerbosity.Log)] // never followed; the poll's own line says it failed
    [InlineData("https://islapandora.eu/api/map/mylocation", 403, LogVerbosity.Warning)]
    [InlineData("https://islapandora.eu/api/map/mylocation", 502, LogVerbosity.Warning)]
    [InlineData("http://islapandora.eu/api/map/mylocation", 200, LogVerbosity.Log)]  // not the site: not timed
    [InlineData("https://api.github.com/repos/x/y/releases/latest", 200, LogVerbosity.Log)]
    public void TheLevelFollowsTheRequestAndTheStatus(string url, int status, LogVerbosity expected)
    {
        Assert.Equal(expected, RequestLogHandler.VerbosityFor(new Uri(url), status));
    }

    [Theory]
    [InlineData("application/json", "json")]
    [InlineData("Application/JSON", "json")]
    [InlineData("text/html", "html")]
    [InlineData("text/plain", "text")]
    [InlineData("image/png", "png")]
    [InlineData("image/jpeg", "jpeg")]
    [InlineData("image/webp", "webp")]
    [InlineData("application/octet-stream", "application/octet-stream")]
    [InlineData("application/vnd.github+json", "application/vnd.github+json")]
    [InlineData("text/x-anything goes; here", "other")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void TheKindOfAnswerIsAShortWord(string? mediaType, string expected)
    {
        Assert.Equal(expected, RequestLogHandler.Kind(mediaType));
    }

    [Theory]
    [InlineData("https://islapandora.eu/map/heatmap-live.png?_t=1761050000000", "/map/heatmap-live.png")]
    [InlineData("https://ISLAPANDORA.EU/api/user/dinos", "/api/user/dinos")]
    [InlineData("https://api.github.com/repos/DavidKatona/PandoraOverlay/releases/latest", "api.github.com")]
    [InlineData("https://islapandora.eu.example.com/api/map/mylocation", "islapandora.eu.example.com")]
    public void TheSiteIsWrittenAsAPath_AnythingElseAsAHost(string url, string expected)
    {
        Assert.Equal(expected, RequestLogHandler.Where(new Uri(url)));
    }
}

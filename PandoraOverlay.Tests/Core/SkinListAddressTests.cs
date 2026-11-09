using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// Where the Skins page's list comes from since the website moved it (Nov 9
/// 2026): the site's page asks /api/extras/patreon/skins first and the old
/// /api/skins/patreon-skins only when that answer holds no list, and the old
/// one, asked alone until then, answered a success with no skins. The
/// overlay does what the page does: the new address, the old one only after
/// a JSON answer with no list in it, never after a page that isn't JSON (the
/// site's page stops there too), two requests at most. The website is a fake
/// behind the poll service's site client, so nothing leaves the test.
/// </summary>
public sealed class SkinListAddressTests
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private const string NewAddress = "/api/extras/patreon/skins";
    private const string OldAddress = "/api/skins/patreon-skins";
    private const string NewList = """{"success":true,"skins":[{"id":1,"name":"Ember"},{"id":2,"name":"Glacier"}]}""";
    private const string OldList = """{"success":true,"skins":[{"id":9,"name":"Old Ember"}]}""";

    [Fact]
    public async Task TheNewAddressAnswersAlone()
    {
        var (poll, site) = SkinsPage(_ => Json(HttpStatusCode.OK, NewList));

        var result = await poll.GetSkinsAsync(refresh: true);

        Assert.Equal(new[] { NewAddress }, site.Paths);
        Assert.Null(result.Problem);
        Assert.Equal(new[] { "Ember", "Glacier" }, result.Skins!.Select(s => s.Name));
    }

    public static TheoryData<HttpStatusCode, string> NoListAtTheNewAddress => new()
    {
        { HttpStatusCode.OK, """{"success":false}""" },
        { HttpStatusCode.OK, """{"success":true}""" },
        { HttpStatusCode.OK, """{"success":true,"skins":{}}""" },
        { HttpStatusCode.NotFound, """{"error":"Not found"}""" },
        { HttpStatusCode.Forbidden, """{"error":"Forbidden"}""" },
        { HttpStatusCode.InternalServerError, NewList }, // a list under a failed status: the page needs both
    };

    [Theory]
    [MemberData(nameof(NoListAtTheNewAddress))]
    public async Task AJsonAnswerWithNoListAsksTheOldAddressOnce(HttpStatusCode status, string body)
    {
        var (poll, site) = SkinsPage(path => path == NewAddress ? Json(status, body) : Json(HttpStatusCode.OK, OldList));

        var result = await poll.GetSkinsAsync(refresh: true);

        Assert.Equal(new[] { NewAddress, OldAddress }, site.Paths);
        Assert.Null(result.Problem);
        Assert.Equal("Old Ember", Assert.Single(result.Skins!).Name);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "blocked by the website")] // Cloudflare's block page
    [InlineData(HttpStatusCode.BadGateway, "website error 502")]
    [InlineData(HttpStatusCode.OK, "unreadable answer")]
    public async Task APageThatIsntJsonEndsItThere(HttpStatusCode status, string words)
    {
        var (poll, site) = SkinsPage(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent("<html>a page</html>", Encoding.UTF8, "text/html")
        });

        var result = await poll.GetSkinsAsync(refresh: true);

        Assert.Equal(new[] { NewAddress }, site.Paths);
        Assert.Null(result.Skins);
        Assert.Equal(words, result.Problem);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, """{"success":true,"skins":[]}""", null)]  // the old address on Nov 9 2026: an empty page, as before
    [InlineData(HttpStatusCode.OK, """{"success":true}""", null)]
    [InlineData(HttpStatusCode.OK, """{"success":false}""", "the server declined")]
    [InlineData(HttpStatusCode.Forbidden, """{"error":"Forbidden"}""", "blocked by the website")] // a dead session, worded as before
    public async Task TheOldAddressHasTheLastWord(HttpStatusCode status, string body, string? problem)
    {
        var (poll, site) = SkinsPage(path => path == NewAddress ? Json(HttpStatusCode.OK, """{"success":false}""") : Json(status, body));

        var result = await poll.GetSkinsAsync(refresh: true);

        Assert.Equal(new[] { NewAddress, OldAddress }, site.Paths);
        Assert.Equal(problem, result.Problem);
        if (problem is null) Assert.Empty(result.Skins!);
        else Assert.Null(result.Skins);
    }

    [Fact]
    public async Task BothRequestsAreThePagesOwn()
    {
        var (poll, site) = SkinsPage(path => path == NewAddress ? Json(HttpStatusCode.OK, "{}") : Json(HttpStatusCode.OK, OldList));

        await poll.GetSkinsAsync(refresh: true);

        Assert.Equal(2, site.Requests.Count);
        Assert.All(site.Requests, r =>
        {
            Assert.Equal("POST", r.Method);
            Assert.Equal(0, r.BodyLength);
            Assert.Equal("https://islapandora.eu/patreon", r.Referer);
            Assert.True(r.HasCookie);
        });
    }

    [Fact]
    public async Task TheFloorCoversBothRequests()
    {
        var (poll, site) = SkinsPage(path => path == NewAddress ? Json(HttpStatusCode.OK, "{}") : Json(HttpStatusCode.OK, OldList));

        await poll.GetSkinsAsync(refresh: true);
        var again = await poll.GetSkinsAsync(refresh: true); // inside the 30 s floor

        Assert.Equal(2, site.Requests.Count);
        Assert.Equal("Old Ember", Assert.Single(again.Skins!).Name);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>A never-started poll service, signed in with a made-up value, its website client pointed at the fake.</summary>
    private static (PollService Poll, SkinsSite Site) SkinsPage(Func<string, HttpResponseMessage> answer)
    {
        var config = new OverlayConfig();
        config.SetCookie("made-up-test-value-not-a-session");
        var poll = new PollService(config);
        var site = new SkinsSite(answer);
        var client = (typeof(PollService).GetField("_client", Any) ?? throw new MissingFieldException(nameof(PollService), "_client")).GetValue(poll)!;
        (client.GetType().GetField("_http", Any) ?? throw new MissingFieldException(nameof(PandoraClient), "_http"))
            .SetValue(client, new HttpClient(site));
        return (poll, site);
    }

    private sealed record Seen(string Path, string Method, long BodyLength, string? Referer, bool HasCookie);

    /// <summary>Answers by path and writes down what each request carried (the cookie only as present or not).</summary>
    private sealed class SkinsSite : HttpMessageHandler
    {
        private readonly Func<string, HttpResponseMessage> _answer;

        public SkinsSite(Func<string, HttpResponseMessage> answer) => _answer = answer;

        public List<Seen> Requests { get; } = new();

        public IEnumerable<string> Paths => Requests.Select(r => r.Path);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? 0 : (await request.Content.ReadAsByteArrayAsync(cancellationToken)).LongLength;
            Requests.Add(new Seen(request.RequestUri!.AbsolutePath, request.Method.Method, body,
                                  request.Headers.Referrer?.AbsoluteUri, request.Headers.Contains("Cookie")));
            return _answer(request.RequestUri.AbsolutePath);
        }
    }
}

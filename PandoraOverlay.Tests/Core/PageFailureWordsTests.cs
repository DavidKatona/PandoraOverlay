using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// What the website's pages say when a request fails: the status line's words
/// (ConnectionProblem), since Nov 6 2026, not an exception's type. Each goes
/// through the real client against a fake site that answers an HTML page, as
/// Cloudflare's block does, so a client that parses the body first (a skin
/// apply, Dino storage, Prime) must still end at the status. Nothing leaves
/// the test, and the service is never started.
/// </summary>
public class PageFailureWordsTests
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    public static TheoryData<HttpStatusCode, string> Failures => new()
    {
        { HttpStatusCode.Forbidden, "blocked by the website" },
        { HttpStatusCode.BadGateway, "website error 502" },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task TheSkinsList(HttpStatusCode status, string words)
    {
        var result = await SignedInAndInGame(status).GetSkinsAsync(refresh: true);
        Assert.Equal(words, result.Problem);
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task ASkinApply(HttpStatusCode status, string words)
    {
        var result = await SignedInAndInGame(status).ApplySkinAsync("1", idIsNumber: true, "Ember", 0);
        Assert.Equal(SkinApplyOutcome.Failed, result.Outcome);
        Assert.Equal(words, result.Message);
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task TheStorageList(HttpStatusCode status, string words)
    {
        var result = await SignedInAndInGame(status).GetStorageAsync();
        Assert.Equal(words, result.Problem);
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task AStorageRenameAndDelete(HttpStatusCode status, string words)
    {
        var dino = StoredDinos.ParseList(JsonDocument.Parse(SettingsThemeTests.Storage).RootElement)!.Dinos[0];

        var renamed = await SignedInAndInGame(status).RenameStoredDinoAsync(dino, "Righty", "");
        Assert.Equal(StorageEditOutcome.Failed, renamed.Outcome);
        Assert.Equal(words, renamed.Message);

        var deleted = await SignedInAndInGame(status).DeleteStoredDinoAsync(dino);
        Assert.Equal(StorageEditOutcome.Failed, deleted.Outcome);
        Assert.Equal(words, deleted.Message);
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task APrimeCheck(HttpStatusCode status, string words)
    {
        var poll = SignedInAndInGame(status);
        PrimeCheckResult? seen = null;
        poll.PrimeChecked += result => seen = result;

        await poll.CheckPrimeAsync();

        Assert.Equal(PrimeCheckOutcome.Failed, seen?.Outcome);
        Assert.Equal(words, seen?.Reason);
    }

    /// <summary>A never-started poll service past every gate, its website client pointed at the fake.</summary>
    private static PollService SignedInAndInGame(HttpStatusCode status)
    {
        var config = new OverlayConfig();
        config.SetCookie("made-up-test-value-not-a-session");
        var poll = new PollService(config);
        var client = (typeof(PollService).GetField("_client", Any) ?? throw new MissingFieldException(nameof(PollService), "_client")).GetValue(poll)!;
        (client.GetType().GetField("_http", Any) ?? throw new MissingFieldException(nameof(PandoraClient), "_http"))
            .SetValue(client, new HttpClient(new HtmlPageSite(status)));
        (typeof(PollService).GetField("_inGame", Any) ?? throw new MissingFieldException(nameof(PollService), "_inGame")).SetValue(poll, true);
        return poll;
    }

    /// <summary>Answers everything with an HTML page and the status it was given.</summary>
    private sealed class HtmlPageSite : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;

        public HtmlPageSite(HttpStatusCode status) => _status = status;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent("<html>a page</html>", Encoding.UTF8, "text/html") });
    }
}

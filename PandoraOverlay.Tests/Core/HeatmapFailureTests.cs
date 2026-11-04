using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// What a failed heatmap fetch does to the kept picture (the owner, Nov 4
/// 2026): before, any failure dropped it, and both maps stayed blank for at
/// least the minute the failed attempt counts toward. Now the picture stays
/// while it is under ten minutes old, with no extra request; an older one
/// goes, and the website's off switch still clears it at once. The website
/// is a fake behind the poll service's site client, so nothing leaves the
/// test.
/// </summary>
public sealed class HeatmapFailureTests
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    [Fact]
    public async Task AFailedFetchKeepsTheLastPictureUntilItIsTenMinutesOld()
    {
        var (poll, site) = MinimapWithItsHeatmap();
        byte[]? shown = null;
        poll.HeatmapChanged += png => shown = png;

        await poll.RefreshHeatmapAsync("test");
        var picture = Assert.IsType<byte[]>(poll.Heatmap);
        Assert.Equal(HeatmapSite.Picture, picture);

        site.Fail = true;
        MinuteUp(poll);
        await poll.RefreshHeatmapAsync("test");
        Assert.Same(picture, poll.Heatmap); // kept, and handed to the maps again
        Assert.Same(picture, shown);

        var requests = site.Requests;
        await poll.RefreshHeatmapAsync("a layer switched on"); // within the failed attempt's minute
        Assert.Equal(requests, site.Requests); // the minute still counts from every attempt: no request
        Assert.Same(picture, shown);

        Set(poll, "_heatmapPictureUtc", DateTime.UtcNow - TimeSpan.FromMinutes(11)); // ten minutes of failures
        MinuteUp(poll);
        await poll.RefreshHeatmapAsync("test");
        Assert.Null(poll.Heatmap);
        Assert.Null(shown);

        site.Fail = false; // the website back: the next attempt brings a picture again
        MinuteUp(poll);
        await poll.RefreshHeatmapAsync("test");
        Assert.Equal(HeatmapSite.Picture, poll.Heatmap);
        Assert.Same(poll.Heatmap, shown);
    }

    [Fact]
    public async Task TheWebsitesOffSwitchStillClearsThePictureAtOnce()
    {
        var (poll, site) = MinimapWithItsHeatmap();
        byte[]? shown = null;
        poll.HeatmapChanged += png => shown = png;

        await poll.RefreshHeatmapAsync("test");
        Assert.NotNull(poll.Heatmap);

        site.Enabled = false;
        MinuteUp(poll);
        await poll.RefreshHeatmapAsync("test");
        Assert.Null(poll.Heatmap);
        Assert.Null(shown);
    }

    /// <summary>A never-started poll service whose minimap shows the heatmap, its site client pointed at the fake.</summary>
    private static (PollService Poll, HeatmapSite Site) MinimapWithItsHeatmap()
    {
        var poll = new PollService(new OverlayConfig { MinimapEnabled = true, HeatmapEnabled = true });
        var client = (typeof(PollService).GetField("_client", Any) ?? throw new MissingFieldException(nameof(PollService), "_client")).GetValue(poll)!;
        var site = new HeatmapSite();
        (client.GetType().GetField("_http", Any) ?? throw new MissingFieldException(nameof(PandoraClient), "_http"))
            .SetValue(client, new HttpClient(site));
        return (poll, site);
    }

    /// <summary>As if the minute since the last attempt had run out.</summary>
    private static void MinuteUp(PollService poll) => Set(poll, "_heatmapUtc", DateTime.MinValue);

    private static void Set(PollService poll, string field, object value) =>
        (typeof(PollService).GetField(field, Any) ?? throw new MissingFieldException(nameof(PollService), field)).SetValue(poll, value);

    /// <summary>The heatmap's two addresses: the status (on, or the off switch) and the picture; Fail answers both with a site error.</summary>
    private sealed class HeatmapSite : HttpMessageHandler
    {
        public static readonly byte[] Picture = { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };

        private int _requests;

        public int Requests => _requests;
        public bool Fail { get; set; }
        public bool Enabled { get; set; } = true;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            if (Fail) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway));
            var answer = request.RequestUri!.AbsolutePath == "/map/api/heatmap-status"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Enabled ? """{"enabled":true}""" : """{"enabled":false}""", Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Picture) };
            return Task.FromResult(answer);
        }
    }
}

using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The access header's file and the handler that sends it. Every name and
/// value here is made up: the real ones never enter the repository. Nothing
/// here reads OverlayAccess.Current, which on a PC with the real file would
/// hold the real header.
/// </summary>
public class OverlayAccessTests
{
    /// <summary>What make-access.ps1 writes: N random pad bytes, then the line's N bytes XOR-ed with them.</summary>
    private static byte[] Scramble(string line, byte[]? pad = null)
    {
        var plain = Encoding.ASCII.GetBytes(line);
        pad ??= RandomNumberGenerator.GetBytes(plain.Length);
        var file = new byte[plain.Length * 2];
        pad.CopyTo(file, 0);
        for (var i = 0; i < plain.Length; i++) file[plain.Length + i] = (byte)(plain[i] ^ pad[i]);
        return file;
    }

    [Fact]
    public void UnscramblesTheLineAndSplitsItAtTheFirstColon()
    {
        Assert.Equal(("x-test-access", "Abc123xyz"), OverlayAccess.Unscramble(Scramble("x-test-access:Abc123xyz")));
        Assert.Equal(("x-test", "a:b"), OverlayAccess.Unscramble(Scramble("x-test:a:b"))); // a colon in the value stays
    }

    [Fact]
    public void AZeroPadByteLeavesItsCharacterAsItIs_AndStillReadsBack()
    {
        var line = "x-test:value1";
        Assert.Equal(("x-test", "value1"), OverlayAccess.Unscramble(Scramble(line, new byte[line.Length])));
    }

    [Fact]
    public void TheScrambledFileHoldsNoReadableLine()
    {
        var file = Scramble("x-test-access:Abc123xyz", Enumerable.Repeat((byte)0x5A, 23).ToArray());
        var text = Encoding.ASCII.GetString(file);
        Assert.DoesNotContain("x-test-access", text);
        Assert.DoesNotContain("Abc123xyz", text);
    }

    [Theory]
    [InlineData("")]               // nothing
    [InlineData("x-test")]         // no colon
    [InlineData(":value")]         // no name
    [InlineData("x-test:")]        // no value
    [InlineData("x test:value")]   // a space in the name
    [InlineData("x_test:value")]   // a character a name may not have
    [InlineData("x-test:val ue")]  // a space in the value
    [InlineData("x-test:val\tue")] // a control character
    public void ABrokenLineGivesNoHeader(string line)
    {
        Assert.Null(OverlayAccess.Unscramble(Scramble(line)));
    }

    [Fact]
    public void ABrokenFileGivesNoHeader()
    {
        Assert.Null(OverlayAccess.Unscramble(Array.Empty<byte>()));
        Assert.Null(OverlayAccess.Unscramble(new byte[] { 1, 2, 3 })); // odd: no pad/line split
        var file = Scramble("x-test:value1");
        file[^1] ^= 0x80; // a byte past ASCII in the value
        Assert.Null(OverlayAccess.Unscramble(file));
    }

    [Theory]
    [InlineData("https://islapandora.eu/api/map/mylocation", true)]
    [InlineData("https://ISLAPANDORA.EU/map/api/heatmap-status", true)]
    [InlineData("http://islapandora.eu/api/map/mylocation", false)]   // not https
    [InlineData("https://www.islapandora.eu/api/map/mylocation", false)]
    [InlineData("https://islapandora.eu.example.com/api/map/mylocation", false)]
    [InlineData("https://cdn.discordapp.com/avatars/1/a.png", false)]
    [InlineData("https://api.github.com/repos/x/y/releases/latest", false)]
    public async Task OnlyTheWebsiteItselfGetsTheHeader(string address, bool expected)
    {
        var inner = new Recorder();
        using var client = new HttpClient(new OverlayAccessHandler(inner, () => ("x-test-access", "Abc123xyz")));
        await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, address));
        Assert.Equal(expected, inner.Last!.Headers.Contains("x-test-access"));
        if (expected) Assert.Equal("Abc123xyz", inner.Last.Headers.GetValues("x-test-access").Single());
    }

    [Fact]
    public async Task NoHeaderInTheBuild_SendsTheRequestWithoutOne()
    {
        var inner = new Recorder();
        using var client = new HttpClient(new OverlayAccessHandler(inner, () => null));
        var request = new HttpRequestMessage(HttpMethod.Post, "https://islapandora.eu/api/map/mylocation");
        request.Headers.TryAddWithoutValidation("Cookie", "connect.sid=x");
        await client.SendAsync(request);
        Assert.Equal(new[] { "Cookie" }, inner.Last!.Headers.Select(h => h.Key));
    }

    [Fact]
    public async Task TheHeaderIsPutTogetherForEveryRequest()
    {
        var calls = 0;
        var inner = new Recorder();
        using var client = new HttpClient(new OverlayAccessHandler(inner, () => { calls++; return ("x-test-access", "Abc123xyz"); }));
        await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "https://islapandora.eu/api/map/mylocation"));
        await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "https://islapandora.eu/api/map/friends"));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void TheBlockingSendAttachesItToo()
    {
        var inner = new Recorder();
        using var client = new HttpClient(new OverlayAccessHandler(inner, () => ("x-test-access", "Abc123xyz")));
        client.Send(new HttpRequestMessage(HttpMethod.Post, "https://islapandora.eu/api/map/mylocation"));
        Assert.True(inner.Last!.Headers.Contains("x-test-access"));
    }

    /// <summary>Stands in for the network: keeps the last request it was handed and answers 200.</summary>
    private sealed class Recorder : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));
    }
}

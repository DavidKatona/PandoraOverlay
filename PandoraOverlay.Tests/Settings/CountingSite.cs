using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;

namespace PandoraOverlay.Tests;

/// <summary>
/// The website, as far as the dialog tests go: counts what reaches it and
/// answers an empty storage. <see cref="Install"/> points a PollService's
/// website client at it, so a test that opens the website's pages
/// (Account, Skins, Dino storage) can reach nothing, and a stray request
/// fails the test instead of reaching islapandora.eu (the 1.39 constraint
/// review, near miss N1: constraint 2).
/// </summary>
internal sealed class CountingSite : HttpMessageHandler
{
    private int _requests;

    public int Requests => _requests;

    /// <summary>A fresh fake behind the poll service's website client: nothing can leave the test.</summary>
    public static CountingSite Install(PollService poll)
    {
        const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var client = (typeof(PollService).GetField("_client", any) ?? throw new MissingFieldException(nameof(PollService), "_client")).GetValue(poll)!;
        var site = new CountingSite();
        (client.GetType().GetField("_http", any) ?? throw new MissingFieldException(nameof(PandoraClient), "_http"))
            .SetValue(client, new HttpClient(site));
        return site;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requests);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"success":true,"dinos":[],"storageLimit":10}""", Encoding.UTF8, "application/json")
        });
    }
}

using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace PandoraOverlay;

/// <summary>
/// PandoraClient, Skins part (v1.28): the calls behind the website's /patreon
/// page and the tile pictures. The list is a read, from two addresses since
/// Nov 2026 (below); apply-patreon is the overlay's first WRITE — it changes
/// your dino's skin in game — so it is only ever sent for a click
/// (PollService.Skins holds the gating). Pictures are fetched by a separate
/// client that carries no cookie and none of the site headers, whatever host
/// they live on. Same class as PandoraClient.cs, split for reading; see
/// CLAUDE.md.
/// </summary>
public sealed partial class PandoraClient
{
    // The list moved by Nov 9 2026: the site's page asks the new address first and
    // the old one only when the new one's answer holds no list. The old one, asked
    // alone by 1.28–1.43, then answered a success with no skins: an empty page.
    private const string PatreonSkinsEndpoint = "https://islapandora.eu/api/extras/patreon/skins";
    private const string OldPatreonSkinsEndpoint = "https://islapandora.eu/api/skins/patreon-skins";
    private const string ApplyPatreonSkinEndpoint = "https://islapandora.eu/api/skins/apply-patreon";
    private const string PatreonPage = "https://islapandora.eu/patreon"; // the page these calls come from on the site
    // The first build capped pictures at 4 MB, which turned the site's real ones away.
    /// <summary>The site serves its skin pictures at full size (4.7 MB seen, Oct 2026); this is a sanity cap, not a budget.</summary>
    private const int MaxPictureBytes = 16 * 1024 * 1024;

    private HttpClient? _pictures;

    /// <summary>
    /// The Patreon skins this account can see, from the site's list address
    /// (the same authenticated, empty-bodied POST its page sends on load),
    /// locked ones included. Null when the answer is JSON but holds no list
    /// (a failed status, no success or no skins array: PatreonSkins.IsList),
    /// the site page's sign to ask its old address (FetchOldPatreonSkinsAsync;
    /// PollService.Skins decides). An answer that isn't JSON throws, as the
    /// page stops there too: a failed status as that status (Cloudflare's
    /// block page), a good one as the JsonException.
    /// </summary>
    public async Task<IReadOnlyList<PatreonSkin>?> FetchPatreonSkinsAsync(CancellationToken ct = default)
    {
        using var response = await SendPatreonSkinsAsync(PatreonSkinsEndpoint, ct).ConfigureAwait(false);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(bytes);
        }
        catch (JsonException)
        {
            response.EnsureSuccessStatusCode();
            throw;
        }
        using (doc)
        {
            return response.IsSuccessStatusCode && PatreonSkins.IsList(doc.RootElement)
                ? PatreonSkins.ParseList(doc.RootElement)
                : null;
        }
    }

    /// <summary>
    /// The old list address: the site's page asks it only after the new one
    /// gave no list, and its answer is the last word. Read as 1.28–1.43 read
    /// it: a failed status throws, a decline is null, a success without a
    /// list is an empty one.
    /// </summary>
    public async Task<IReadOnlyList<PatreonSkin>?> FetchOldPatreonSkinsAsync(CancellationToken ct = default)
    {
        using var response = await SendPatreonSkinsAsync(OldPatreonSkinsEndpoint, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        return PatreonSkins.ParseList(doc.RootElement);
    }

    /// <summary>One list request, to either address: the empty-bodied POST with the cookie and the page's Referer.</summary>
    private async Task<HttpResponseMessage> SendPatreonSkinsAsync(string endpoint, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new ByteArrayContent(Array.Empty<byte>())
        };
        request.Headers.TryAddWithoutValidation("Cookie", _cookie);
        request.Headers.TryAddWithoutValidation("Referer", PatreonPage);

        var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        UpdateRollingCookie(response);
        return response;
    }

    /// <summary>
    /// Applies one skin with one pattern — the site's Apply button followed
    /// by its A–F choice: {skinId, patternIndex} as JSON. NEVER call this on
    /// a timer or without a click. Like the site, the body is read whatever
    /// the HTTP status (a refusal arrives as an error body with a message);
    /// only an unparseable response throws.
    /// </summary>
    public async Task<SkinApplyResult> ApplyPatreonSkinAsync(string skinIdJson, string skinName, int patternIndex,
                                                             CancellationToken ct = default)
    {
        var body = $"{{\"skinId\":{skinIdJson},\"patternIndex\":{patternIndex}}}";
        using var request = new HttpRequestMessage(HttpMethod.Post, ApplyPatreonSkinEndpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Cookie", _cookie);
        request.Headers.TryAddWithoutValidation("Referer", PatreonPage);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        UpdateRollingCookie(response);

        var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            return PatreonSkins.ParseApply(doc.RootElement, skinName, patternIndex);
        }
        catch (JsonException)
        {
            response.EnsureSuccessStatusCode();
            throw;
        }
    }

    /// <summary>
    /// One tile picture. Its own bare client: a User-Agent and nothing else —
    /// no Cookie, no Origin, no Referer — because the address comes from the
    /// list and may point anywhere. Capped in size. Never throws: trouble
    /// comes back as a short reason the page can show (a status code, "too
    /// large", an exception type — never anything the server wrote).
    /// </summary>
    public async Task<SkinPicture> FetchPictureAsync(Uri address, CancellationToken ct = default)
    {
        if (address.Scheme != Uri.UriSchemeHttps) return new SkinPicture(null, "not an https address");
        _pictures ??= CreatePictureClient();
        try
        {
            // Headers first, so an oversized picture is turned away before its body is downloaded.
            using var response = await _pictures.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return new SkinPicture(null, $"HTTP {(int)response.StatusCode}");
            if (response.Content.Headers.ContentLength is > MaxPictureBytes and var length)
            {
                return new SkinPicture(null, $"too large ({length / 1048576.0:0.#} MB)");
            }
            try
            {
                await response.Content.LoadIntoBufferAsync(MaxPictureBytes).ConfigureAwait(false); // a body with no declared length is capped here
            }
            catch (HttpRequestException)
            {
                return new SkinPicture(null, $"too large (over {MaxPictureBytes / 1048576} MB)");
            }
            return new SkinPicture(await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false));
        }
        catch (TaskCanceledException)
        {
            return new SkinPicture(null, "timed out");
        }
        catch (Exception ex)
        {
            return new SkinPicture(null, ex.GetType().Name);
        }
    }

    private HttpClient CreatePictureClient()
    {
        var client = new HttpClient(new RequestLogHandler(new HttpClientHandler { UseCookies = false }))
        {
            Timeout = TimeSpan.FromSeconds(60) // several MB each: a slow line needs the time
        };
        if (_http.DefaultRequestHeaders.TryGetValues("User-Agent", out var agent))
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", string.Join(" ", agent));
        }
        return client;
    }
}

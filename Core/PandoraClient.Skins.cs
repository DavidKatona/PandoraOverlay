using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace PandoraOverlay;

/// <summary>
/// PandoraClient, Skins part (v1.28): the two calls behind the website's
/// /patreon page and the tile pictures. The list is a read; apply-patreon is
/// the overlay's first WRITE — it changes your dino's skin in game — so it is
/// only ever sent for a click (PollService.Skins holds the gating). Pictures
/// are fetched by a separate client that carries no cookie and none of the
/// site headers, whatever host they live on. Same class as PandoraClient.cs,
/// split for reading; see CLAUDE.md.
/// </summary>
public sealed partial class PandoraClient
{
    private const string PatreonSkinsEndpoint = "https://islapandora.eu/api/skins/patreon-skins";
    private const string ApplyPatreonSkinEndpoint = "https://islapandora.eu/api/skins/apply-patreon";
    private const string PatreonPage = "https://islapandora.eu/patreon"; // the page these calls come from on the site
    private const int MaxPictureBytes = 4 * 1024 * 1024;

    private HttpClient? _pictures;

    /// <summary>
    /// The Patreon skins this account can see (the same authenticated,
    /// empty-bodied POST the site's page sends on load), locked ones
    /// included. Null when the server declines.
    /// </summary>
    public async Task<IReadOnlyList<PatreonSkin>?> FetchPatreonSkinsAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, PatreonSkinsEndpoint)
        {
            Content = new ByteArrayContent(Array.Empty<byte>())
        };
        request.Headers.TryAddWithoutValidation("Cookie", _cookie);
        request.Headers.TryAddWithoutValidation("Referer", PatreonPage);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        UpdateRollingCookie(response);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        return PatreonSkins.ParseList(doc.RootElement);
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
    /// list and may point anywhere. Capped in size; null on any trouble.
    /// </summary>
    public async Task<byte[]?> FetchPictureAsync(Uri address, CancellationToken ct = default)
    {
        if (address.Scheme != Uri.UriSchemeHttps) return null;
        _pictures ??= CreatePictureClient();
        try
        {
            using var response = await _pictures.GetAsync(address, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            if (response.Content.Headers.ContentLength is > MaxPictureBytes) return null;
            return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            return null; // a tile without its picture still shows its colours
        }
    }

    private HttpClient CreatePictureClient()
    {
        var client = new HttpClient(new HttpClientHandler { UseCookies = false })
        {
            Timeout = TimeSpan.FromSeconds(15),
            MaxResponseContentBufferSize = MaxPictureBytes
        };
        if (_http.DefaultRequestHeaders.TryGetValues("User-Agent", out var agent))
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", string.Join(" ", agent));
        }
        return client;
    }
}

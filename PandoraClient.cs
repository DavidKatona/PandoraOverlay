using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PandoraOverlay;

/// <summary>One player snapshot, exactly as returned by /api/map/mylocation.</summary>
public sealed record PlayerState(
    string? SteamId,
    string? Name,
    string? Dino,
    string? Gender,
    double Growth,
    double Health,
    double Stamina,
    double Hunger,
    double Thirst,
    double Yaw,
    bool HeadFractured,
    bool BodyFractured,
    bool LegsFractured,
    double X,
    double Y,
    double Z);

public sealed record MyLocationResponse(bool InGame, PlayerState? Player);

/// <summary>
/// World→map transform constants served by /api/map/calibration — the same
/// values the live-map frontend feeds its marker-placement function:
///   left fraction = (OffsetX + x·ScaleX + PinOffsetX) / MapSize
///   top fraction  = 1 − (OffsetY + y·ScaleY + PinOffsetY) / MapSize   (note the Y flip)
/// pinOffset is part of the site's coordinate mapping for EVERY marker (the
/// frontend anchors them centered via translate(-50%,-50%), player arrow
/// included) — skipping it draws ~0.6%/1% off the website, the
/// v1.13.0-and-earlier bug. Defaults keep older cached calibrations loading.
/// </summary>
public sealed record MapCalibration(double OffsetX, double OffsetY, double ScaleX, double ScaleY, double MapSize,
                                    double PinOffsetX = 0, double PinOffsetY = 0);

/// <summary>
/// Minimal client for the Isla Pandora live-map API — the overlay's entire
/// data path: authenticated, empty-bodied POSTs identical to the website
/// frontend's own (mylocation, calibration), plus the public, cookie-less
/// heatmap GETs. Nothing here touches the game.
/// </summary>
public sealed class PandoraClient : IDisposable
{
    private const string Endpoint = "https://islapandora.eu/api/map/mylocation";
    private const string CalibrationEndpoint = "https://islapandora.eu/api/map/calibration";
    private const string HeatmapStatusEndpoint = "https://islapandora.eu/map/api/heatmap-status";
    private const string HeatmapImageEndpoint = "https://islapandora.eu/map/heatmap-live.png";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private string _cookie;

    /// <summary>Latest cookie value, including any connect.sid rolled forward by Set-Cookie responses.</summary>
    public string CurrentCookie => _cookie;

    public PandoraClient(string cookie, string userAgent)
    {
        _cookie = cookie ?? "";

        // UseCookies = false: we manage the Cookie header ourselves so the
        // config-supplied cf_clearance + connect.sid pair is sent verbatim.
        _http = new HttpClient(new HttpClientHandler { UseCookies = false })
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://islapandora.eu");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://islapandora.eu/live-map");
    }

    public async Task<MyLocationResponse> FetchAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            // Empty body, Content-Length: 0 — matches the browser's request exactly.
            Content = new ByteArrayContent(Array.Empty<byte>())
        };
        request.Headers.TryAddWithoutValidation("Cookie", _cookie);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

        // Express renews the session on every request. Capture the fresh
        // connect.sid so the login keeps rolling across overlay restarts.
        UpdateRollingCookie(response);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var parsed = await JsonSerializer.DeserializeAsync<MyLocationResponse>(stream, JsonOpts, ct)
            .ConfigureAwait(false);

        return parsed ?? new MyLocationResponse(false, null);
    }

    /// <summary>
    /// Fetches the map calibration constants. Called once per launch (per
    /// credential swap at most) — static site config. POST with an empty
    /// body, mirroring the frontend's own fetch (the route is POST-only; GET
    /// returns 404). Parsing is tolerant of wrapping: the first JSON object
    /// carrying offsetX/…/mapSize anywhere in the response wins.
    /// </summary>
    public async Task<MapCalibration?> FetchCalibrationAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, CalibrationEndpoint)
        {
            Content = new ByteArrayContent(Array.Empty<byte>())
        };
        request.Headers.TryAddWithoutValidation("Cookie", _cookie);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        UpdateRollingCookie(response);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        return FindCalibration(doc.RootElement);
    }

    /// <summary>
    /// Fetches the site's pre-rendered heatmap image (an opaque 1000×1000 PNG
    /// with the grayscale map and a player-count caption baked in — approved
    /// by the site dev, Sep 2026). Honors the admin kill-switch first:
    /// /map/api/heatmap-status reporting enabled:false returns null. The site
    /// fails open on a broken status check; we fail closed — when in doubt,
    /// skip the ~1 MB download. Both URLs are public, so no Cookie header is
    /// sent — this adds no credential path. The cache-buster mirrors the
    /// live-map page's own image refresh.
    /// </summary>
    public async Task<byte[]?> FetchHeatmapAsync(CancellationToken ct = default)
    {
        using (var status = await _http.GetAsync(HeatmapStatusEndpoint, ct).ConfigureAwait(false))
        {
            status.EnsureSuccessStatusCode();
            await using var stream = await status.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("enabled", out var enabled) &&
                enabled.ValueKind == JsonValueKind.False)
            {
                return null;
            }
        }

        var url = $"{HeatmapImageEndpoint}?_t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    internal static MapCalibration? FindCalibration(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                if (TryReadNumber(el, "offsetX", out var ox) &&
                    TryReadNumber(el, "offsetY", out var oy) &&
                    TryReadNumber(el, "scaleX", out var sx) &&
                    TryReadNumber(el, "scaleY", out var sy) &&
                    TryReadNumber(el, "mapSize", out var size) && size > 0)
                {
                    // Optional nested pinOffset {x,y}; absent reads as 0.
                    double px = 0, py = 0;
                    foreach (var prop in el.EnumerateObject())
                    {
                        if (string.Equals(prop.Name, "pinOffset", StringComparison.OrdinalIgnoreCase) &&
                            prop.Value.ValueKind == JsonValueKind.Object)
                        {
                            TryReadNumber(prop.Value, "x", out px);
                            TryReadNumber(prop.Value, "y", out py);
                        }
                    }
                    return new MapCalibration(ox, oy, sx, sy, size, px, py);
                }
                foreach (var prop in el.EnumerateObject())
                {
                    if (FindCalibration(prop.Value) is { } nested) return nested;
                }
                return null;

            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray())
                {
                    if (FindCalibration(item) is { } fromArray) return fromArray;
                }
                return null;

            default:
                return null;
        }
    }

    private static bool TryReadNumber(JsonElement obj, string name, out double value)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase) &&
                prop.Value.ValueKind == JsonValueKind.Number)
            {
                value = prop.Value.GetDouble();
                return true;
            }
        }
        value = 0;
        return false;
    }

    private void UpdateRollingCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookies)) return;

        foreach (var sc in setCookies)
        {
            var match = Regex.Match(sc, @"^(connect\.sid=[^;]+)");
            if (!match.Success) continue;

            var fresh = match.Groups[1].Value;
            if (Regex.IsMatch(_cookie, @"connect\.sid=[^;]+"))
            {
                _cookie = Regex.Replace(_cookie, @"connect\.sid=[^;]+", fresh);
            }
            else if (string.IsNullOrWhiteSpace(_cookie))
            {
                _cookie = fresh;
            }
            else
            {
                _cookie = _cookie.TrimEnd(';', ' ') + "; " + fresh;
            }
        }
    }

    public void Dispose() => _http.Dispose();
}

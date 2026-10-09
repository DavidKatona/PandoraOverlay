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
/// One friend as returned by /api/map/friends (approved by the site dev,
/// relayed Sep 27 2026): the same player record the site shows for you,
/// plus the presence flags. Coordinates are nullable — the site itself
/// checks them for null before drawing. A friend who turned on "hide my
/// location" arrives with HideLocation and is never drawn, whatever the
/// coordinates say; the server decides consent, not the overlay.
/// </summary>
public sealed record FriendState(
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
    double? X,
    double? Y,
    bool InGame,
    bool HideLocation)
{
    /// <summary>Drawn on the map only when in game, sharing their location and with a position — the live-map page's own rule.</summary>
    public bool OnMap => InGame && !HideLocation && X is not null && Y is not null;
}

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
                                    double PinOffsetX = 0, double PinOffsetY = 0)
{
    /// <summary>World cm → map fractions (0–1, y flipped): the one transform behind the arrow, markers, trail and area lookup.</summary>
    public (double Fx, double Fy) ToFraction(double x, double y) =>
        (Math.Clamp((OffsetX + x * ScaleX + PinOffsetX) / MapSize, 0, 1),
         Math.Clamp(1 - (OffsetY + y * ScaleY + PinOffsetY) / MapSize, 0, 1));

    /// <summary>
    /// Map fractions → world cm: ToFraction undone (the pinOffset taken off,
    /// the y flip reversed), for a point picked ON the map — "Waypoint here",
    /// a cursor readout. The same arithmetic the minimap's menu has always
    /// used. NOT clamped: a fraction past the map's edge gives a point past
    /// it, so a caller clamps the fraction first where that matters.
    /// </summary>
    public (double X, double Y) ToWorld(double fx, double fy) =>
        ((fx * MapSize - OffsetX - PinOffsetX) / ScaleX,
         ((1 - fy) * MapSize - OffsetY - PinOffsetY) / ScaleY);
}

/// <summary>
/// One Prime check result as shown by the live-map page's "Prime Check" box:
/// overall status plus the ten condition flags (index 0 = condition 1). Cached
/// in config with its timestamp and the dino it was taken for, so the widget
/// can show the last known state across restarts.
/// </summary>
public sealed record PrimeSnapshot(bool IsPrime, bool IsEligible, bool[] Conditions, DateTime CheckedAtUtc, string? Dino = null);

public enum PrimeCheckOutcome { Ok, Cooldown, NotInGame, Failed }

/// <summary>Outcome of a prime check: a snapshot on Ok, the remaining wait on Cooldown, a short reason on Failed.</summary>
public sealed record PrimeCheckResult(
    PrimeCheckOutcome Outcome,
    PrimeSnapshot? Snapshot = null,
    TimeSpan Remaining = default,
    string? Reason = null);

/// <summary>
/// Minimal client for the Isla Pandora live-map API — the overlay's entire
/// data path: authenticated, empty-bodied POSTs identical to the website
/// frontend's own (mylocation, calibration), plus the public, cookie-less
/// heatmap GETs. Nothing here touches the game. The Patreon skins calls
/// (v1.28) live in PandoraClient.Skins.cs.
/// The account pair (auth/me, logout) is in PandoraClient.Account.cs, the
/// Dino storage trio in PandoraClient.Storage.cs. The awaits in here use
/// ConfigureAwait(false); the UI thread is regained only at the outermost
/// await, in PollService. No WPF reference, on purpose — keep it so. The
/// site sits behind Cloudflare, yet a plain HttpClient passes (checked with
/// curl): no TLS impersonation or embedded browser is needed for these calls.
/// </summary>
public sealed partial class PandoraClient : IDisposable
{
    private const string Endpoint = "https://islapandora.eu/api/map/mylocation";
    private const string CalibrationEndpoint = "https://islapandora.eu/api/map/calibration";
    private const string HeatmapStatusEndpoint = "https://islapandora.eu/map/api/heatmap-status";
    private const string HeatmapImageEndpoint = "https://islapandora.eu/map/heatmap-live.png";
    private const string PrimeCheckEndpoint = "https://islapandora.eu/api/prime/check";
    private const string PrimeCooldownEndpoint = "https://islapandora.eu/api/prime/cooldown";
    private const string FriendsEndpoint = "https://islapandora.eu/api/map/friends";
    private const double MaxPrimeCooldownMs = 3_600_000; // sanity clamp on server-reported waits

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    // A credential: it goes into the Cookie header of the site's own requests
    // and out through CurrentCookie (which the config encrypts) — never into
    // a log, an exception message or window text.
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
        // Captured before any status check: an answer that fails the check
        // may still carry the renewal.
        UpdateRollingCookie(response);

        // The site's own "no session" answer is told apart from every other
        // failure: it is final for this cookie (see PollService.SignedOut).
        if (IsRefusal((int)response.StatusCode, response.Content.Headers.ContentType?.MediaType)) throw new SessionEndedException();
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var parsed = await JsonSerializer.DeserializeAsync<MyLocationResponse>(stream, JsonOpts, ct)
            .ConfigureAwait(false);

        return parsed ?? new MyLocationResponse(false, null);
    }

    /// <summary>
    /// Fetches the map calibration constants — static site config. Succeeds
    /// once per launch (and again per credential swap at most): a FAILED
    /// fetch is retried after a successful poll, at most once a minute,
    /// until one succeeds (PollService.Calibration.cs). POST with an empty
    /// body, mirroring the frontend's own fetch (the route is POST-only; GET
    /// returns 404). Parsing is tolerant of wrapping: the first JSON object
    /// carrying offsetX/…/mapSize anywhere in the response wins. Added at the
    /// owner's direction (Sep 2026); the live-map page itself loads it on
    /// every visit, and it answers without a session too.
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
    /// PollService paces it (PollService.Maps.cs): only while a map shows the
    /// layer, and never two fetches less than a minute apart.
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

    /// <summary>
    /// One user-triggered Prime check (approved by the site dev, Sep 2026) —
    /// the same authenticated, empty-bodied POST the live-map page's "Check
    /// Prime Status" button sends. NEVER call this on a timer: the server
    /// gates it behind a 5-minute cooldown because the check does real work.
    /// Like the frontend, the JSON body is parsed regardless of HTTP status
    /// (cooldown / not-in-game arrive as error bodies); only an unparseable
    /// response throws.
    /// </summary>
    public async Task<PrimeCheckResult> CheckPrimeAsync(string? dino, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, PrimeCheckEndpoint)
        {
            Content = new ByteArrayContent(Array.Empty<byte>())
        };
        request.Headers.TryAddWithoutValidation("Cookie", _cookie);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        UpdateRollingCookie(response);

        var body = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(body);
            return ParsePrime(doc.RootElement, DateTime.UtcNow, dino);
        }
        catch (JsonException)
        {
            response.EnsureSuccessStatusCode();
            throw;
        }
    }

    /// <summary>
    /// Tolerant like the frontend: status under isPrimeElder/isPrime and
    /// isEligiblePrime/isEligible, condition flags keyed "1".."10" or
    /// "c1".."c10". Server error strings are mapped, never echoed.
    /// </summary>
    internal static PrimeCheckResult ParsePrime(JsonElement root, DateTime nowUtc, string? dino)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new PrimeCheckResult(PrimeCheckOutcome.Failed, Reason: "unexpected response");
        }

        if (IsTruthy(root, "success") &&
            root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            var conditions = new bool[10];
            if (data.TryGetProperty("conditions", out var cond) && cond.ValueKind == JsonValueKind.Object)
            {
                for (var i = 1; i <= 10; i++)
                {
                    var key = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    conditions[i - 1] = IsTruthy(cond, key) || IsTruthy(cond, "c" + key);
                }
            }
            return new PrimeCheckResult(PrimeCheckOutcome.Ok, new PrimeSnapshot(
                IsTruthy(data, "isPrimeElder") || IsTruthy(data, "isPrime"),
                IsTruthy(data, "isEligiblePrime") || IsTruthy(data, "isEligible"),
                conditions, nowUtc, dino));
        }

        var error = root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String
            ? e.GetString()
            : null;
        switch (error)
        {
            case "cooldown":
                var ms = root.TryGetProperty("remainingMs", out var r) && r.ValueKind == JsonValueKind.Number
                    ? r.GetDouble()
                    : 0;
                return new PrimeCheckResult(PrimeCheckOutcome.Cooldown,
                    Remaining: TimeSpan.FromMilliseconds(Math.Clamp(ms, 0, MaxPrimeCooldownMs)));
            case "not_in_game":
                return new PrimeCheckResult(PrimeCheckOutcome.NotInGame);
            default:
                return new PrimeCheckResult(PrimeCheckOutcome.Failed, Reason: "server declined");
        }
    }

    /// <summary>
    /// Asks the server how long this account's prime-check cooldown still
    /// runs. Called ONCE right after a successful check — never on a timer:
    /// the success response carries no cooldown, and the length differs per
    /// account (supporter ranks shorten it), so assuming the website's 5 min
    /// would lock ranked players out for longer than the server does. The
    /// live-map page asks the same endpoint on every load. Null when the
    /// server gives no usable answer.
    /// </summary>
    public async Task<TimeSpan?> FetchPrimeCooldownAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, PrimeCooldownEndpoint)
        {
            Content = new ByteArrayContent(Array.Empty<byte>())
        };
        request.Headers.TryAddWithoutValidation("Cookie", _cookie);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        UpdateRollingCookie(response);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        return ParsePrimeCooldown(doc.RootElement);
    }

    /// <summary>Like the frontend: success without a positive remainingMs means no cooldown is running.</summary>
    internal static TimeSpan? ParsePrimeCooldown(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !IsTruthy(root, "success")) return null;

        var ms = root.TryGetProperty("remainingMs", out var r) && r.ValueKind == JsonValueKind.Number
            ? r.GetDouble()
            : 0;
        return TimeSpan.FromMilliseconds(Math.Clamp(ms, 0, MaxPrimeCooldownMs));
    }

    /// <summary>
    /// Fetches the friends roster (approved Sep 2026) — the same authenticated,
    /// empty-bodied POST the live-map page sends alongside every mylocation
    /// poll. PollService paces it (every second in-game poll, each idle one)
    /// and only while a friends surface is shown; nothing else may call it.
    /// Read-only: friend management (requests, blocks, the privacy toggles)
    /// stays on the website and is never called from here. Null when the
    /// server declines (success:false) — the caller treats it as a miss.
    /// </summary>
    public async Task<IReadOnlyList<FriendState>?> FetchFriendsAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, FriendsEndpoint)
        {
            Content = new ByteArrayContent(Array.Empty<byte>())
        };
        request.Headers.TryAddWithoutValidation("Cookie", _cookie);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        UpdateRollingCookie(response);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        return ParseFriends(doc.RootElement);
    }

    /// <summary>
    /// {success, friends:[...]} → the roster; entries without a steamId are
    /// dropped, a missing or non-array friends field reads as an empty roster,
    /// success:false as null.
    /// </summary>
    internal static IReadOnlyList<FriendState>? ParseFriends(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !IsTruthy(root, "success")) return null;

        var friends = new List<FriendState>();
        if (!root.TryGetProperty("friends", out var list) || list.ValueKind != JsonValueKind.Array) return friends;
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            FriendState? friend;
            try
            {
                friend = item.Deserialize<FriendState>(JsonOpts);
            }
            catch (JsonException)
            {
                continue; // one malformed entry must not cost the whole roster
            }
            if (friend is not null && !string.IsNullOrWhiteSpace(friend.SteamId)) friends.Add(friend);
        }
        return friends;
    }

    private static bool IsTruthy(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) &&
        (v.ValueKind == JsonValueKind.True ||
         (v.ValueKind == JsonValueKind.Number && v.GetDouble() != 0));

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

    /// <summary>
    /// Express renews connect.sid on every answer (a rolling 30-day session),
    /// so a session in use never expires — provided the renewed value is
    /// saved: MainWindow re-encrypts CurrentCookie on exit and on leaving
    /// edit mode. Only connect.sid is replaced or added; the rest of the
    /// header (cf_clearance, when a pasted cookie has one) stays verbatim.
    /// </summary>
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

    public void Dispose()
    {
        _http.Dispose();
        _pictures?.Dispose();
    }
}

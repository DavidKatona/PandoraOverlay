using System.Net.Http;
using System.Text.Json;

namespace PandoraOverlay;

/// <summary>
/// Who the website session belongs to, from POST /api/auth/me — the call every
/// site page makes on load (v1.31, the in-app sign-in). Only what the overlay
/// has a use for: whether there is a session, the Discord username for the
/// Account page, whether a Steam account is linked (without one mylocation
/// can never find the player), the site's own "may use the live map" flag,
/// the Discord-server verification Steam linking needs, and the Steam LinkID
/// (the code typed in game chat to link — shown on the "Steam not linked"
/// screen only, never logged). Discord ids, avatars and roles are left out.
/// </summary>
public sealed record AccountInfo(
    bool Authenticated,
    string? Username,
    string? SteamId,
    string? LinkId,
    bool HasMapAccess,
    bool IsVerified)
{
    public static readonly AccountInfo None = new(false, null, null, null, false, false);

    public bool SteamLinked => !string.IsNullOrWhiteSpace(SteamId);
}

/// <summary>
/// The site answered "no session": 401/403 with its own JSON error body. The
/// cookie is dead (a sign-out elsewhere, an expiry), not the connection —
/// PollService stops polling and asks for a new sign-in instead of retrying
/// a refusal every minute for good.
/// </summary>
public sealed class SessionEndedException : Exception
{
    public SessionEndedException() : base("The website refused the session.") { }
}

public sealed partial class PandoraClient
{
    private const string AccountEndpoint = "https://islapandora.eu/api/auth/me";
    private const string SignOutEndpoint = "https://islapandora.eu/auth/logout";

    /// <summary>
    /// One "who am I" — right after a sign-in to confirm the captured session
    /// and read the name, and when the Account page is looked at. Never on a
    /// timer. A refusal reads as no session rather than an error.
    /// </summary>
    public async Task<AccountInfo> FetchAccountAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, AccountEndpoint)
        {
            Content = new ByteArrayContent(Array.Empty<byte>())
        };
        request.Headers.TryAddWithoutValidation("Cookie", _cookie);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        UpdateRollingCookie(response);
        if (IsRefusal((int)response.StatusCode, response.Content.Headers.ContentType?.MediaType)) return AccountInfo.None;
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        return ParseAccount(doc.RootElement);
    }

    /// <summary>
    /// Tolerant like the frontend, which takes either "authenticated" or a
    /// "user" object as signed in. steamId is read as a string or a number;
    /// blanks read as absent.
    /// </summary>
    internal static AccountInfo ParseAccount(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return AccountInfo.None;

        var authenticated = IsTruthy(root, "authenticated");
        string? username = null, steamId = null, linkId = null;
        if (root.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object)
        {
            authenticated = true;
            username = ReadText(user, "username");
            steamId = ReadText(user, "steamId");
            linkId = ReadText(user, "linkId");
        }
        return new AccountInfo(authenticated, username, steamId, linkId, IsTruthy(root, "hasMapAccess"), IsTruthy(root, "isVerified"));
    }

    /// <summary>
    /// A 401 or 403 carrying the site's own JSON (verified Oct 7 2026: a dead
    /// or missing session answers 403 {"error":"Forbidden"}). A Cloudflare
    /// block is a 403 too, but an HTML page — that stays an ordinary failure.
    /// </summary>
    internal static bool IsRefusal(int status, string? mediaType) =>
        status is 401 or 403 && string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Ends the session on the website too — the site's own logout link, one
    /// GET on a Sign out click. Its answer is a redirect to the home page,
    /// which there is no reason to download, so this request goes through
    /// its own client that follows no redirects. True when the site took it.
    /// </summary>
    public async Task<bool> SignOutAsync(CancellationToken ct = default)
    {
        using var http = new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        using var request = new HttpRequestMessage(HttpMethod.Get, SignOutEndpoint);
        if (_http.DefaultRequestHeaders.TryGetValues("User-Agent", out var ua)) request.Headers.TryAddWithoutValidation("User-Agent", ua);
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        request.Headers.TryAddWithoutValidation("Cookie", _cookie);

        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        return (int)response.StatusCode is >= 200 and < 400;
    }

    private static string? ReadText(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v)) return null;
        var text = v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null
        };
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}

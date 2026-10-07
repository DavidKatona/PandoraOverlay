namespace PandoraOverlay;

/// <summary>
/// The rules of the sign-in window (v1.31), pure and tested: which pages may
/// open inside it, which navigation means "the website has signed you in",
/// what the window says about where it is, and which of the browser's
/// cookies become the overlay's session. The flow it describes (read from
/// the site's public bundle and rehearsed Oct 7 2026): the website's own
/// "Log in with Discord" link is /auth/discord, the server redirects to
/// discord.com, Discord sends the player back to /auth/discord/callback,
/// and the server sets connect.sid and redirects to a site page — the
/// landing. The window stops THERE: no site page (and so no live-map
/// polling) ever renders in it.
/// </summary>
public static class SignInPolicy
{
    public const string SiteHost = "islapandora.eu";

    /// <summary>The website's own "Log in with Discord" link.</summary>
    public const string StartUrl = "https://islapandora.eu/auth/discord";

    /// <summary>The site's session cookie, then the Cloudflare one if the site ever issues it; nothing else is kept.</summary>
    private static readonly string[] SessionCookies = { "connect.sid", "cf_clearance" };

    /// <summary>
    /// May this top-level page open inside the window? The site's login
    /// routes (and Cloudflare's own paths, should a challenge appear),
    /// discord.com and its subdomains, and about:blank. Everything else is
    /// stopped — a clicked link opens in the real browser instead.
    /// </summary>
    public static bool IsAllowed(Uri uri)
    {
        if (string.Equals(uri.Scheme, "about", StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase)) return false;
        var host = uri.Host.ToLowerInvariant();
        if (IsSite(host)) return IsLoginPath(uri.AbsolutePath);
        return host == "discord.com" || host.EndsWith(".discord.com", StringComparison.Ordinal);
    }

    /// <summary>
    /// The callback is done and the site wants to show a page: the first
    /// top-level navigation to islapandora.eu outside its login routes. The
    /// session cookie is set by then (verified Oct 7 2026: the server's
    /// redirect goes to "/"), so this is the moment to read it and stop.
    /// </summary>
    public static bool IsSignedInLanding(Uri uri) =>
        string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase) &&
        IsSite(uri.Host.ToLowerInvariant()) &&
        !IsLoginPath(uri.AbsolutePath);

    /// <summary>What the address strip says beside the host, so the player knows where in the flow they are.</summary>
    public static string StepLabel(Uri uri)
    {
        var host = uri.Host.ToLowerInvariant();
        if (IsSite(host)) return "Connecting to islapandora.eu…";
        if (host == "discord.com" || host.EndsWith(".discord.com", StringComparison.Ordinal))
        {
            return uri.AbsolutePath.Contains("/oauth2/authorize", StringComparison.OrdinalIgnoreCase)
                ? "Step 2 of 2 · Allow the website"
                : "Step 1 of 2 · Discord login";
        }
        return "";
    }

    /// <summary>
    /// The Cookie header the overlay will send, from the browser's cookies
    /// for the site: connect.sid (and cf_clearance if present), in that
    /// order, nothing else. Empty when there is no session cookie — the
    /// sign-in did not happen.
    /// </summary>
    public static string CookieHeader(IEnumerable<KeyValuePair<string, string>> cookies)
    {
        var list = cookies.ToList();
        var parts = new List<string>();
        foreach (var name in SessionCookies)
        {
            var value = list.FirstOrDefault(c => c.Key == name && !string.IsNullOrWhiteSpace(c.Value)).Value;
            if (value is not null) parts.Add($"{name}={value.Trim()}");
        }
        return parts.Count > 0 && parts[0].StartsWith(SessionCookies[0] + "=", StringComparison.Ordinal)
            ? string.Join("; ", parts)
            : "";
    }

    /// <summary>True when the header carries a session cookie at all.</summary>
    public static bool HasSession(string cookieHeader) =>
        System.Text.RegularExpressions.Regex.IsMatch(cookieHeader ?? "", @"(^|;\s*)connect\.sid=\S");

    private static bool IsSite(string host) => host == SiteHost || host == "www." + SiteHost;

    private static bool IsLoginPath(string path) =>
        path.StartsWith("/auth/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/cdn-cgi/", StringComparison.OrdinalIgnoreCase);
}

using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace PandoraOverlay;

/// <summary>
/// One LogNet line per request (1.36), written when the answer arrives or the
/// request fails: the method, where it went, the status, the kind of answer
/// and how long the answer took to start. The time is until the answer's
/// headers; the body is read after this line (seen in game, Oct 21 2026: a
/// roster's body up to 0.4 s, the heatmap picture's ~0.4 s more). A
/// feature's own line, such as LogFriends' roster or LogMaps' heatmap
/// picture, marks when the body had been read.
///   POST /api/map/mylocation 200 json 143 ms
///   GET cdn.discordapp.com 200 png 88 ms
///   POST /api/map/friends failed: HttpRequestException (ConnectionError) after 8001 ms
/// The website's own addresses are written as their path, without the query
/// (only the heatmap's cache-buster carries one). Any other host is written as
/// its name alone, because a Discord avatar's path holds the account's id.
/// The kind of answer tells the site's own refusal (403 json) from
/// Cloudflare's block page (403 html), the lesson of Oct 12 2026. The timed
/// requests (the poll, the roster, the heatmap's pair) are Verbose, so a
/// normal session's log isn't one line every few seconds; everything else
/// answered is Log; a 4xx, a 5xx or a failure is a Warning.
/// A 4xx or 5xx from the website also gets a note in brackets (the owner's
/// okay, Nov 6 2026, after a player's "403 html" could say no more):
///   POST /api/auth/me 403 html 48 ms (Cloudflare block, ray 0123456789abcdef-VIE)
///   POST /api/auth/me 403 html 48 ms (Cloudflare challenge, ray 0123456789abcdef-VIE)
///   POST /api/auth/me 502 html 48 ms (ray 0123456789abcdef-VIE)
///   POST /api/auth/me 403 html 48 ms (no Cloudflare ray)
/// The Ray ID names that one answer in Cloudflare's security events, so the
/// site's dev can see which rule fired. It holds a number and the code of the
/// Cloudflare data centre that answered (a hint of the region, in a log only the
/// player can send, which names its time zone anyway), and no credential.
/// It is the ONE header value the log may hold (CLAUDE.md, constraint 4), and
/// only in a Ray's shape: anything else in that header reads as no ray. A
/// challenge says so in cf-mitigated, whose value is only compared, never
/// written; a block is a 403 html with a ray and without that header (a real
/// one checked Nov 6 2026). "No ray" means Cloudflare didn't send the page:
/// something between the player and it did, such as a filter on their network.
/// NEVER read here: any other header's value (the cookie and the access header
/// ride the request), a body, an exception's message.
/// It wraps every HttpClient of ours, outermost: the site client (around
/// OverlayAccessHandler), the picture client, the sign-out client and the
/// update check. Velopack's own requests go to its own log
/// (%LocalAppData%\Velopack). The model is the temporary request log of Oct 9
/// 2026, which caught the heatmap fetches 15 s and 32 s apart.
/// </summary>
internal sealed class RequestLogHandler : DelegatingHandler
{
    /// <summary>The site's timed requests: one every few seconds (or a minute) for as long as the overlay runs.</summary>
    private static readonly string[] TimedPaths =
    {
        "/api/map/mylocation", "/api/map/friends", "/map/api/heatmap-status", "/map/heatmap-live.png",
    };

    private static readonly Regex PlainMediaType = new("^[a-z0-9.+-]{1,30}/[a-z0-9.+-]{1,40}$", RegexOptions.CultureInvariant);

    /// <summary>A Ray ID as Cloudflare sends it (16 hex digits and the data centre's code, Oct–Nov 2026), with room to grow; nothing else is written.</summary>
    private static readonly Regex RayShape = new(@"\A[0-9a-f]{8,32}-[A-Z0-9]{2,8}\z", RegexOptions.CultureInvariant);

    private readonly Action<LogVerbosity, string> _write;

    /// <param name="inner">The connection underneath.</param>
    /// <param name="write">Where the line goes: LogNet by default, a list in the tests.</param>
    internal RequestLogHandler(HttpMessageHandler inner, Action<LogVerbosity, string>? write = null)
        : base(inner)
    {
        _write = write ?? AppLog.Net.Write;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            Answered(request, response, started);
            return response;
        }
        catch (Exception ex)
        {
            _write(LogVerbosity.Warning, FailureLine(request.Method, request.RequestUri, ex, Stopwatch.GetElapsedTime(started)));
            throw;
        }
    }

    // The overlay only ever sends asynchronously; the blocking path is logged the same way so none skips it.
    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var response = base.Send(request, cancellationToken);
            Answered(request, response, started);
            return response;
        }
        catch (Exception ex)
        {
            _write(LogVerbosity.Warning, FailureLine(request.Method, request.RequestUri, ex, Stopwatch.GetElapsedTime(started)));
            throw;
        }
    }

    private void Answered(HttpRequestMessage request, HttpResponseMessage response, long started)
    {
        var status = (int)response.StatusCode;
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        var note = status >= 400 && OverlayAccessHandler.IsSite(request.RequestUri)
            ? CloudflareNote(status, mediaType, FirstValue(response, "CF-RAY"), FirstValue(response, "cf-mitigated"))
            : "";
        _write(VerbosityFor(request.RequestUri, status),
               AnswerLine(request.Method, request.RequestUri, status, mediaType, Stopwatch.GetElapsedTime(started)) + note);
    }

    private static string? FirstValue(HttpResponseMessage response, string header) =>
        response.Headers.TryGetValues(header, out var values) ? values.FirstOrDefault() : null;

    /// <summary>The bracketed note on a website's 4xx or 5xx, with its leading space (the class's summary shows each form).</summary>
    internal static string CloudflareNote(int status, string? mediaType, string? ray, string? mitigated)
    {
        if (ray is null || !RayShape.IsMatch(ray)) return " (no Cloudflare ray)";
        var what = string.Equals(mitigated, "challenge", StringComparison.OrdinalIgnoreCase) ? "Cloudflare challenge, "
                 : status == 403 && Kind(mediaType) == "html" ? "Cloudflare block, "
                 : "";
        return $" ({what}ray {ray})";
    }

    /// <summary>A timed request answered 2xx is Verbose; any other 2xx or 3xx is Log (a redirect is never followed, and the caller decides whether it failed); 4xx and 5xx are Warnings.</summary>
    internal static LogVerbosity VerbosityFor(Uri? address, int status) =>
        status is < 200 or >= 400 ? LogVerbosity.Warning
        : status < 300 && IsTimed(address) ? LogVerbosity.Verbose
        : LogVerbosity.Log;

    internal static bool IsTimed(Uri? address) =>
        OverlayAccessHandler.IsSite(address) && TimedPaths.Contains(address!.AbsolutePath, StringComparer.OrdinalIgnoreCase);

    /// <summary>The website: its path, no query. Anywhere else: the host's name alone.</summary>
    internal static string Where(Uri? address) =>
        address is not { IsAbsoluteUri: true } ? "(no address)"
        : OverlayAccessHandler.IsSite(address) ? address.AbsolutePath
        : address.Host;

    /// <summary>The kind of answer, from its media type: json, html, text, png, jpeg, webp; an unusual but plain type as itself; "other" for anything odder; nothing for an answer without one.</summary>
    internal static string Kind(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType)) return "";
        var type = mediaType.Trim().ToLowerInvariant();
        return type switch
        {
            "application/json" => "json",
            "text/html" => "html",
            "text/plain" => "text",
            "image/png" => "png",
            "image/jpeg" => "jpeg",
            "image/webp" => "webp",
            _ => PlainMediaType.IsMatch(type) ? type : "other",
        };
    }

    internal static string AnswerLine(HttpMethod method, Uri? address, int status, string? mediaType, TimeSpan took)
    {
        var kind = Kind(mediaType);
        return $"{method} {Where(address)} {status.ToString(CultureInfo.InvariantCulture)}{(kind.Length > 0 ? " " + kind : "")} {Milliseconds(took)} ms";
    }

    internal static string FailureLine(HttpMethod method, Uri? address, Exception ex, TimeSpan took) =>
        $"{method} {Where(address)} failed: {AppLog.Short(ex)} after {Milliseconds(took)} ms";

    private static string Milliseconds(TimeSpan took) => ((long)took.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
}

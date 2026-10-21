using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace PandoraOverlay;

/// <summary>
/// The status line's words for a failed poll (1.36): "Disconnected · retrying
/// (no connection)" instead of "(HttpRequestException)", so a player can tell
/// their own connection from the website's answer without sending a log.
/// Before 1.36 every one of these read as the exception's type, and the
/// Cloudflare block of Oct 12 2026 looked exactly like a network blip.
/// The cases are the ones the log tells apart (AppLog.Short and
/// RequestLogHandler): the website's block page (a 403 that isn't the site's
/// own JSON refusal; that one is SessionEndedException), a rate limit, the
/// website's own errors, another status, a secure connection that failed, no
/// connection at all, no answer in time, an answer that couldn't be read.
/// Our own words, never server text or a message (CLAUDE.md, constraint 4);
/// a failure none of them fits keeps its type's name, as before. Zero WPF.
/// </summary>
public static class ConnectionProblem
{
    public static string Words(Exception ex) => ex switch
    {
        SessionEndedException => "session refused", // the second in a row ends the session ("Session ended · sign in again")
        HttpRequestException { StatusCode: HttpStatusCode.Forbidden } => "blocked by the website",
        HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => "rate-limited",
        HttpRequestException { StatusCode: { } status } when (int)status >= 500 => $"website error {(int)status}",
        HttpRequestException { StatusCode: { } status } => $"website answered {(int)status}", // a redirect, a 404 …
        HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError } => "secure connection failed",
        HttpRequestException => "no connection",
        TaskCanceledException or TimeoutException => "no answer", // HttpClient's 8 s timeout
        JsonException => "unreadable answer",
        _ => ex.GetType().Name,
    };
}

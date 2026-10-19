using System.IO;
using System.Net.Http;
using System.Text;

namespace PandoraOverlay;

/// <summary>
/// The overlay's access header for the website. Since Oct 12 2026 the site's
/// anti-abuse rule (at Cloudflare, in front of the site) guards four
/// endpoints: mylocation, friends, the Prime check and its cooldown.
/// Browsers pass it; the overlay, like any other program, got Cloudflare's
/// own "blocked" page, and the request never reached the site. The website
/// team gave the overlay a header of its own that the rule lets through.
///
/// The header's name and value are a credential, like the cookie (CLAUDE.md,
/// hard constraint 4), and they are kept OUT of this public repository: not
/// in the code, the docs, the tests or a commit message. They live in the
/// GitHub Actions secret the release build reads, in the owner's own
/// records, and in a git-ignored Assets/overlay-access.bin that
/// tools/overlay-access/make-access.ps1 writes from them. The build embeds
/// that file when it exists; this class reads it.
///
/// The file holds one "name:value" line, scrambled: N random pad bytes, then
/// the line's N bytes each XOR-ed with the pad byte at the same position.
/// The line is put back together only when a request is about to be sent,
/// and the bytes are wiped again straight after. That keeps it out of a text
/// search of the program, and it is no more than that: whoever runs the
/// overlay can still read the header off their own traffic, and the owner
/// said so to the website team. What protects the site is that every one of
/// these requests also carries the player's session.
///
/// Fail soft: a build without the file (a build from source) or with a
/// broken one sends no header, and those four endpoints stay blocked, the
/// state the whole overlay was in before the header existed.
/// </summary>
internal static class OverlayAccess
{
    private const string ResourceName = "overlay-access.bin";

    // Kept scrambled for the life of the process; read once, on the first request.
    private static readonly Lazy<byte[]?> Scrambled = new(Load);

    /// <summary>The header to attach, put together now; null when this build carries none.</summary>
    internal static (string Name, string Value)? Current() =>
        Scrambled.Value is { } file ? Unscramble(file) : null;

    /// <summary>
    /// The file's line, unscrambled and split at its first colon; null for an
    /// empty or odd-sized file, a line without a colon, an empty name or
    /// value, or a character outside <see cref="IsNameChar"/> /
    /// <see cref="IsValueChar"/>. make-access.ps1 checks the same rules
    /// before writing, so a bad line fails there, not silently here.
    /// </summary>
    internal static (string Name, string Value)? Unscramble(ReadOnlySpan<byte> file)
    {
        if (file.Length < 2 || file.Length % 2 != 0) return null;
        var half = file.Length / 2;
        var pad = file[..half];
        var mixed = file[half..];
        var line = new byte[half];
        try
        {
            for (var i = 0; i < half; i++) line[i] = (byte)(pad[i] ^ mixed[i]);
            var colon = Array.IndexOf(line, (byte)':');
            if (colon <= 0 || colon == half - 1) return null;
            for (var i = 0; i < colon; i++) if (!IsNameChar(line[i])) return null;
            for (var i = colon + 1; i < half; i++) if (!IsValueChar(line[i])) return null;
            return (Encoding.ASCII.GetString(line, 0, colon), Encoding.ASCII.GetString(line, colon + 1, half - colon - 1));
        }
        finally
        {
            Array.Clear(line); // the strings live until collected; the raw bytes need not
        }
    }

    /// <summary>A header name's character: a letter, a digit or a hyphen.</summary>
    internal static bool IsNameChar(int c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-';

    /// <summary>A header value's character: visible ASCII, no space.</summary>
    internal static bool IsValueChar(int c) => c is > ' ' and <= '~';

    private static byte[]? Load()
    {
        try
        {
            using var stream = typeof(OverlayAccess).Assembly.GetManifestResourceStream(ResourceName);
            if (stream is null) return null;
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        }
        catch (Exception)
        {
            return null; // fail soft: no header, as in a build without the file
        }
    }
}

/// <summary>
/// Attaches <see cref="OverlayAccess"/>'s header to a request just before it
/// leaves, and only when it goes to the website itself (https, the site's own
/// host). PandoraClient's site connection is the only one built with it: the
/// picture client (skin tiles, the Discord avatar), the sign-out client, the
/// update check, the updater and the sign-in window never carry the header.
/// Like the cookie it never reaches a log, an exception or window text; .NET's
/// request exceptions carry no headers, and the overlay shows only an
/// exception's type anyway.
/// </summary>
internal sealed class OverlayAccessHandler : DelegatingHandler
{
    private readonly Func<(string Name, string Value)?> _access;

    /// <param name="inner">The connection underneath.</param>
    /// <param name="access">Where the header comes from: the built-in file by default, a made-up one in the tests.</param>
    internal OverlayAccessHandler(HttpMessageHandler inner, Func<(string Name, string Value)?>? access = null)
        : base(inner)
    {
        _access = access ?? OverlayAccess.Current;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Attach(request);
        return base.SendAsync(request, cancellationToken);
    }

    // The overlay only ever sends asynchronously; the blocking path gets the same treatment so none skips it.
    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Attach(request);
        return base.Send(request, cancellationToken);
    }

    private void Attach(HttpRequestMessage request)
    {
        if (IsSite(request.RequestUri) && _access() is { } header)
        {
            request.Headers.TryAddWithoutValidation(header.Name, header.Value);
        }
    }

    /// <summary>The website itself: https and exactly the site's host.</summary>
    internal static bool IsSite(Uri? address) =>
        address is { IsAbsoluteUri: true } &&
        address.Scheme == Uri.UriSchemeHttps &&
        string.Equals(address.Host, SignInPolicy.SiteHost, StringComparison.OrdinalIgnoreCase);
}

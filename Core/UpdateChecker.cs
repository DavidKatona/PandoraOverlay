using System.Net.Http;
using System.Text.Json;

namespace PandoraOverlay;

/// <summary>
/// One fail-soft check against the GitHub releases API at launch — the only
/// network call the overlay ever makes besides the two islapandora.eu
/// endpoints. No result, no error, or no newer version all mean the same
/// thing to the UI: nothing is shown. Never re-checks during a session.
/// </summary>
public static class UpdateChecker
{
    private const string LatestReleaseApi = "https://api.github.com/repos/DavidKatona/PandoraOverlay/releases/latest";

    /// <summary>Where the tray menu's update entry sends the user.</summary>
    public const string ReleasesPage = "https://github.com/DavidKatona/PandoraOverlay/releases/latest";

    /// <summary>The newer release's tag ("v1.7.0"), or null when current/unknown/offline.</summary>
    public static async Task<string?> CheckAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "PandoraOverlay");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");

            var json = await http.GetStringAsync(LatestReleaseApi).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(tag)) return null;

            var current = typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
            return IsNewer(tag, current) ? tag : null;
        }
        catch
        {
            return null; // fail-soft: no info simply means no update UI
        }
    }

    /// <summary>"v1.7.0" vs the assembly's X.Y.Z — unparseable tags count as not newer.</summary>
    internal static bool IsNewer(string tag, Version current)
    {
        if (!Version.TryParse(tag.Trim().TrimStart('v', 'V'), out var latest)) return false;
        return latest > new Version(current.Major, current.Minor, Math.Max(current.Build, 0));
    }
}

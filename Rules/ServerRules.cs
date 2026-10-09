using System.IO;
using System.Reflection;
using System.Text.Json;

namespace PandoraOverlay;

public sealed record PackLimit(string Name, int Limit);

public sealed record PackCategory(string Category, IReadOnlyList<PackLimit> Species);

/// <summary>
/// The server rules as the Rules page shows them: where they came from,
/// when the copy was taken, the site's own "Discord has priority" note,
/// the pack limits per diet category and the numbered rules.
/// </summary>
public sealed record RulesDocument(
    string Source,
    string CopiedOn,
    string Note,
    IReadOnlyList<PackCategory> PackLimits,
    IReadOnlyList<string> Rules)
{
    /// <summary>The group limit for a species by name (case-insensitive), or null when the table doesn't list it.</summary>
    public int? LimitFor(string? dino)
    {
        if (string.IsNullOrWhiteSpace(dino)) return null;
        foreach (var category in PackLimits)
        {
            foreach (var s in category.Species)
            {
                if (string.Equals(s.Name, dino, StringComparison.OrdinalIgnoreCase)) return s.Limit;
            }
        }
        return null;
    }
}

/// <summary>
/// The server rules seed (Sep 29 2026): a dated copy of islapandora.eu/rules
/// bundled as Assets/rules.json. The site has NO rules endpoint — its page
/// bakes two arrays into its frontend bundle and even says the Discord rules
/// have priority "due to the website requiring updates" — and scraping that
/// bundle would be fragile, heavy and uncleared traffic, so the copy ships
/// with the app until the dev provides an endpoint (asked for; pending).
/// When one exists it replaces this the way calibration does: fetched once
/// per launch, cached, the seed only a fresh install's first content.
/// Parse is tolerant and pure; nothing here throws.
/// Until that endpoint exists, new rules mean a new Assets/rules.json and a
/// release — and ServerRulesTests pins the bundled copy's shape (rule count,
/// categories, species count), so the test changes with the copy.
/// </summary>
public static class ServerRules
{
    // The csproj's EmbeddedResource LogicalName — NOT a WPF Resource, so this
    // loader stays WPF-free and the tests read the very file that ships.
    public const string ResourceName = "rules.json";

    /// <summary>The bundled copy, or null if the resource is missing or unreadable.</summary>
    public static RulesDocument? LoadBundled()
    {
        try
        {
            using var stream = typeof(ServerRules).Assembly.GetManifestResourceStream(ResourceName);
            if (stream is null) return null;
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Parses a rules document; blank rules and empty categories are dropped, anything unusable is null.</summary>
    public static RulesDocument? Parse(string json)
    {
        RulesDocument? doc;
        try
        {
            doc = JsonSerializer.Deserialize<RulesDocument>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return null;
        }
        if (doc is null) return null;

        var rules = (doc.Rules ?? Array.Empty<string>())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .ToList();
        var limits = (doc.PackLimits ?? Array.Empty<PackCategory>())
            .Where(c => c is { Species.Count: > 0 } && !string.IsNullOrWhiteSpace(c.Category))
            .Select(c => new PackCategory(c.Category.Trim(),
                c.Species.Where(s => s is not null && !string.IsNullOrWhiteSpace(s.Name) && s.Limit > 0).ToList()))
            .Where(c => c.Species.Count > 0)
            .ToList();
        if (rules.Count == 0 && limits.Count == 0) return null;

        return new RulesDocument(doc.Source ?? "", doc.CopiedOn ?? "", doc.Note ?? "", limits, rules);
    }
}

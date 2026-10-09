using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PandoraOverlay;

/// <summary>One "### Added"-style group of the notes: its heading (upper-cased, may be empty) and its bullet texts.</summary>
public sealed record NoteSection(string Title, IReadOnlyList<string> Items);

/// <summary>
/// The update card's What's new (1.32): the CHANGELOG, read the simple way it
/// is written. A version's section is the text from its "## [X.Y.Z]" heading
/// to the next "## " heading; a pre-release ("1.32.0-rc.1") reads the
/// "## [Unreleased]" section, since the CHANGELOG stays there until the final
/// — the release workflow cuts the very same section for the update package
/// (`vpk pack --releaseNotes`) and the GitHub release text. Inside a section:
/// "### Added" starts a group, "- " starts a bullet, an indented line
/// continues it; backtick and bold markers are dropped and a markdown link
/// keeps its words. Pure and tolerant: nothing here throws.
/// So the CHANGELOG is player-facing text: a version's section is what players
/// read on the card and as the GitHub release text — written for them, not
/// for the code.
/// </summary>
public static partial class ReleaseNotes
{
    /// <summary>The section heading a version's notes live under: the version itself, or "Unreleased" for a pre-release.</summary>
    public static string HeadingFor(string version)
    {
        var bare = version.Split('+')[0].Trim().TrimStart('v', 'V');
        return bare.Contains('-') ? "Unreleased" : bare;
    }

    /// <summary>The raw markdown of a version's section, or null when the CHANGELOG has none (or it is empty).</summary>
    public static string? SectionFor(string? changelog, string version)
    {
        if (string.IsNullOrWhiteSpace(changelog) || string.IsNullOrWhiteSpace(version)) return null;
        var heading = "## [" + HeadingFor(version) + "]";
        var lines = changelog.Replace("\r\n", "\n").Split('\n');
        var start = Array.FindIndex(lines, l => l.TrimEnd().StartsWith(heading, StringComparison.OrdinalIgnoreCase));
        if (start < 0) return null;
        var body = new StringBuilder();
        for (var i = start + 1; i < lines.Length && !lines[i].StartsWith("## ", StringComparison.Ordinal); i++)
            body.Append(lines[i]).Append('\n');
        var text = body.ToString().Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>Markdown in, groups of bullets out. Unparseable or empty input reads as no notes.</summary>
    public static IReadOnlyList<NoteSection> Parse(string? markdown)
    {
        var sections = new List<NoteSection>();
        if (string.IsNullOrWhiteSpace(markdown)) return sections;

        string title = "";
        var items = new List<string>();
        StringBuilder? item = null;

        void EndItem()
        {
            if (item is null) return;
            var text = Clean(item.ToString());
            if (text.Length > 0) items.Add(text);
            item = null;
        }
        void EndSection()
        {
            EndItem();
            if (items.Count > 0) sections.Add(new NoteSection(title, items.ToArray()));
            items.Clear();
        }

        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0) { EndItem(); continue; }
            if (trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                EndSection();
                var hashes = trimmed.TakeWhile(c => c == '#').Count();
                // "### Added" names a group; a "## [1.32.0]" version heading (or a bare "#") is not shown.
                title = hashes >= 3 ? Clean(trimmed[hashes..]).ToUpperInvariant() : "";
                continue;
            }
            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
            {
                EndItem();
                item = new StringBuilder(trimmed[2..]);
                continue;
            }
            if (trimmed.StartsWith("---", StringComparison.Ordinal)) { EndItem(); continue; }
            if (item is not null && line.Length > trimmed.Length) { item.Append(' ').Append(trimmed); continue; } // indented: continues the bullet
            EndItem();
            item = new StringBuilder(trimmed); // a plain paragraph line reads as its own bullet
        }
        EndSection();
        return sections;
    }

    /// <summary>The bundled CHANGELOG.md (an embedded resource, like rules.json), or null if it can't be read.</summary>
    public static string? LoadBundledChangelog()
    {
        try
        {
            using var stream = typeof(ReleaseNotes).Assembly.GetManifestResourceStream("CHANGELOG.md");
            if (stream is null) return null;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>One line of text, markers dropped: `code` and **bold** lose their marks, [words](address) keeps the words.</summary>
    internal static string Clean(string text)
    {
        var s = MarkdownLink().Replace(text, "$1");
        s = s.Replace("**", "").Replace("__", "").Replace("`", "");
        return Spaces().Replace(s, " ").Trim();
    }

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}

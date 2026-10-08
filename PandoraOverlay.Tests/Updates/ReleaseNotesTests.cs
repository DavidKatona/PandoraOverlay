using System.Reflection;
using Xunit;

namespace PandoraOverlay.Tests;

public class ReleaseNotesTests
{
    private const string Changelog = """
        # Changelog

        Intro text.

        ## [Unreleased]

        ### Added

        - The update card.

        ## [1.31.2] - 2026-10-08

        ### Fixed

        - The minimap no longer waits
          for a failed calibration.
        - Second fix with `code` and **bold**.

        ### Changed

        - See [the README](https://example.com/readme) for details.

        ## [1.31.1] - 2026-10-08

        ### Added

        - Avatar.
        """;

    [Theory]
    [InlineData("1.31.2", "1.31.2")]
    [InlineData("v1.31.2", "1.31.2")]
    [InlineData("1.31.2+abc123", "1.31.2")]
    [InlineData("1.32.0-rc.1", "Unreleased")]
    [InlineData("1.32.0-rc.2+abc", "Unreleased")]
    public void PreReleasesReadTheUnreleasedSection(string version, string heading) =>
        Assert.Equal(heading, ReleaseNotes.HeadingFor(version));

    [Fact]
    public void CutsAVersionsSectionUpToTheNextOne()
    {
        var section = ReleaseNotes.SectionFor(Changelog, "1.31.2")!;
        Assert.StartsWith("### Fixed", section);
        Assert.Contains("the README", section);
        Assert.DoesNotContain("Avatar", section);
        Assert.DoesNotContain("update card", section);
    }

    [Fact]
    public void ARcReadsUnreleased_TheLastSectionReadsToTheEnd()
    {
        Assert.Contains("The update card.", ReleaseNotes.SectionFor(Changelog, "1.32.0-rc.1"));
        Assert.Equal("### Added\n\n- Avatar.", ReleaseNotes.SectionFor(Changelog, "1.31.1"));
    }

    [Fact]
    public void MissingOrEmptyReadsAsNone()
    {
        Assert.Null(ReleaseNotes.SectionFor(Changelog, "9.9.9"));
        Assert.Null(ReleaseNotes.SectionFor(null, "1.31.2"));
        Assert.Null(ReleaseNotes.SectionFor("## [1.0.0]\n\n## [0.9.0]\n- x", "1.0.0"));
        Assert.Null(ReleaseNotes.SectionFor("## [1.31.20]\n- x", "1.31.2")); // a longer version is not this one
    }

    [Fact]
    public void ParsesGroupsBulletsAndContinuations()
    {
        var sections = ReleaseNotes.Parse(ReleaseNotes.SectionFor(Changelog, "1.31.2"));
        Assert.Equal(2, sections.Count);
        Assert.Equal("FIXED", sections[0].Title);
        Assert.Equal(new[] { "The minimap no longer waits for a failed calibration.", "Second fix with code and bold." }, sections[0].Items);
        Assert.Equal("CHANGED", sections[1].Title);
        Assert.Equal("See the README for details.", Assert.Single(sections[1].Items));
    }

    [Fact]
    public void ToleratesNotesWithoutGroupsAndJunk()
    {
        var plain = ReleaseNotes.Parse("Just a line.\n\n* star bullet\n---\n## [1.0.0]\n");
        var section = Assert.Single(plain);
        Assert.Equal("", section.Title);
        Assert.Equal(new[] { "Just a line.", "star bullet" }, section.Items);
        Assert.Empty(ReleaseNotes.Parse(null));
        Assert.Empty(ReleaseNotes.Parse("   \n\n"));
        Assert.Empty(ReleaseNotes.Parse("### Added\n\n### Fixed\n"));
    }

    /// <summary>The CHANGELOG shipped inside the app has notes for the version being built — the card and About's What's new depend on it.</summary>
    [Fact]
    public void TheBundledChangelogHasNotesForThisVersion()
    {
        var changelog = ReleaseNotes.LoadBundledChangelog();
        Assert.NotNull(changelog);
        var version = typeof(ReleaseNotes).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        var notes = ReleaseNotes.Parse(ReleaseNotes.SectionFor(changelog, version));
        Assert.NotEmpty(notes);
    }
}

using Xunit;

namespace PandoraOverlay.Tests;

public class UpdaterTests
{
    /// <summary>A pre-release build sees pre-releases (testers on rc.1 get rc.2); a release build sees only releases. Build metadata after '+' never counts.</summary>
    [Theory]
    [InlineData("1.30.0-rc.1", true)]
    [InlineData("1.30.0-rc.1+abc123", true)]
    [InlineData("1.30.0", false)]
    [InlineData("1.30.0+abc123", false)]
    [InlineData("1.30.0+sha-with-dash", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyAPreReleaseBuildLooksAtPreReleases(string? version, bool expected) =>
        Assert.Equal(expected, Updater.AcceptsPreReleases(version));
}

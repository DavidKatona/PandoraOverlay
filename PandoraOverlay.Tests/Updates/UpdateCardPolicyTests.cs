using Xunit;

namespace PandoraOverlay.Tests;

public class UpdateCardPolicyTests
{
    [Theory]
    [InlineData("1.32.0", "1.31.2", 1)]
    [InlineData("1.31.10", "1.31.9", 1)]          // numeric, not text
    [InlineData("1.32.0", "1.32.0-rc.2", 1)]      // a release is above its pre-releases
    [InlineData("1.32.0-rc.10", "1.32.0-rc.2", 1)]
    [InlineData("1.32.0-rc.1", "1.32.0-beta", 1)]  // word labels compare as text
    [InlineData("1.32.0+abc", "1.32.0", 0)]       // build metadata ignored
    [InlineData("v1.32.0", "1.32.0", 0)]
    [InlineData("1.32", "1.32.0", 0)]
    public void OrdersVersionsLikeSemVer(string a, string b, int sign)
    {
        Assert.Equal(sign, Math.Sign(UpdateCardPolicy.Compare(a, b)));
        Assert.Equal(-sign, Math.Sign(UpdateCardPolicy.Compare(b, a)));
    }

    [Fact]
    public void UnparseableVersionsStillCompareEqualToThemselves()
    {
        Assert.Equal(0, UpdateCardPolicy.Compare("?", "?"));
        Assert.NotEqual(0, UpdateCardPolicy.Compare("?", "1.0.0"));
    }

    [Fact]
    public void AvailableShowsOncePerVersion()
    {
        Assert.True(UpdateCardPolicy.ShowAvailable("1.32.0", null, firstRun: false, updatedCardShown: false));
        Assert.True(UpdateCardPolicy.ShowAvailable("1.32.1", "1.32.0", false, false));
        Assert.False(UpdateCardPolicy.ShowAvailable("1.32.0", "1.32.0", false, false)); // shown before (or Later)
        Assert.False(UpdateCardPolicy.ShowAvailable("v1.32.0", "1.32.0", false, false));
    }

    [Fact]
    public void AvailableNeverOnAFirstRunOrBesideTheUpdatedCardOrWithoutAVersion()
    {
        Assert.False(UpdateCardPolicy.ShowAvailable("1.32.0", null, firstRun: true, updatedCardShown: false));
        Assert.False(UpdateCardPolicy.ShowAvailable("1.32.0", null, firstRun: false, updatedCardShown: true));
        Assert.False(UpdateCardPolicy.ShowAvailable(null, null, false, false));
        Assert.False(UpdateCardPolicy.ShowAvailable(" ", null, false, false));
    }

    [Fact]
    public void UpdatedShowsForAHigherVersionAndNamesThePreviousOne()
    {
        Assert.True(UpdateCardPolicy.ShowUpdated("1.32.0", "1.31.2", firstRun: false, out var previous));
        Assert.Equal("1.31.2", previous);
        Assert.True(UpdateCardPolicy.ShowUpdated("1.32.0-rc.2", "1.32.0-rc.1+sha", false, out previous));
        Assert.Equal("1.32.0-rc.1", previous);
    }

    [Fact]
    public void UpdatedFromBeforeTheCardShowsWithoutAPreviousVersion()
    {
        Assert.True(UpdateCardPolicy.ShowUpdated("1.32.0", null, firstRun: false, out var previous));
        Assert.Null(previous);
    }

    [Fact]
    public void UpdatedStaysQuietOnAFirstRunTheSameVersionOrADowngrade()
    {
        Assert.False(UpdateCardPolicy.ShowUpdated("1.32.0", null, firstRun: true, out _));
        Assert.False(UpdateCardPolicy.ShowUpdated("1.32.0", "1.32.0", false, out _));
        Assert.False(UpdateCardPolicy.ShowUpdated("1.32.0", "1.32.0+other", false, out _));
        Assert.False(UpdateCardPolicy.ShowUpdated("1.31.2", "1.32.0", false, out _));
        Assert.False(UpdateCardPolicy.ShowUpdated("1.32.0-rc.2", "1.32.0", false, out _));
    }
}

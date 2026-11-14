using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// A game's id is its save folder's name (A7), so the rule keeps every id to a
/// shape that can't leave the games folder or fold into another game's folder.
/// </summary>
public sealed class GameIdsTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("0")]
    [InlineData("hatchling")]
    [InlineData("the-dig")]
    [InlineData("game-2")]
    [InlineData("a--b")]
    [InlineData("abcdefghijklmnopqrstuvwxyz012345")] // 32, the longest
    public void TheShapeIsLowerCaseLettersDigitsAndInnerDashes(string id)
    {
        Assert.True(GameIds.IsValid(id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Hatchling")]                         // a capital: Windows would fold it into "hatchling"'s folder
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("a.b")]
    [InlineData("-a")]
    [InlineData("a-")]
    [InlineData("-")]
    [InlineData("a b")]
    [InlineData("c:")]
    [InlineData("a:b")]                               // an NTFS stream
    [InlineData("a\0b")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456")] // 33
    public void AnythingElseIsRefused(string? id)
    {
        Assert.False(GameIds.IsValid(id));
    }

    [Theory]
    [InlineData(0x00E9)] // e acute: a letter, but not a-z
    [InlineData(0x00C9)] // E acute, whose lower case is a letter but not a-z
    [InlineData(0xFF41)] // a full-width a
    [InlineData(0x0661)] // an Arabic-Indic one: a digit, but not 0-9
    [InlineData(0x2010)] // a hyphen that is not '-'
    public void LettersAndDigitsOfOtherScriptsAreRefused(int code)
    {
        Assert.False(GameIds.IsValid("game" + (char)code));
    }

    [Theory]
    [InlineData("con")]
    [InlineData("prn")]
    [InlineData("aux")]
    [InlineData("nul")]
    [InlineData("com0")]
    [InlineData("com1")]
    [InlineData("com2")]
    [InlineData("com3")]
    [InlineData("com4")]
    [InlineData("com5")]
    [InlineData("com6")]
    [InlineData("com7")]
    [InlineData("com8")]
    [InlineData("com9")]
    [InlineData("lpt0")]
    [InlineData("lpt1")]
    [InlineData("lpt2")]
    [InlineData("lpt3")]
    [InlineData("lpt4")]
    [InlineData("lpt5")]
    [InlineData("lpt6")]
    [InlineData("lpt7")]
    [InlineData("lpt8")]
    [InlineData("lpt9")]
    public void AWindowsDeviceNameIsRefused(string id)
    {
        // A37: Windows can't always make a folder of such a name, and which it
        // refuses differs by version, so a game's writes would fail in the field.
        Assert.False(GameIds.IsValid(id));
    }

    [Fact]
    public void TheDeviceNamesAreTwentyFour()
    {
        Assert.Equal(24, GameIds.DeviceNames.Count);
    }

    [Theory]
    [InlineData("con1")]
    [InlineData("nulls")]
    [InlineData("com10")]
    [InlineData("lpt")]
    [InlineData("console")]
    [InlineData("a-con")]
    public void ANameThatOnlyContainsADeviceNameIsFine(string id)
    {
        Assert.True(GameIds.IsValid(id));
    }

    [Fact]
    public void TheLongestIsThirtyTwo()
    {
        Assert.Equal(32, GameIds.MaxLength);
        Assert.True(GameIds.IsValid(new string('a', GameIds.MaxLength)));
        Assert.False(GameIds.IsValid(new string('a', GameIds.MaxLength + 1)));
    }
}

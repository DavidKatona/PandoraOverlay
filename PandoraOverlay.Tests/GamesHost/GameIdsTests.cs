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

    [Fact]
    public void TheLongestIsThirtyTwo()
    {
        Assert.Equal(32, GameIds.MaxLength);
        Assert.True(GameIds.IsValid(new string('a', GameIds.MaxLength)));
        Assert.False(GameIds.IsValid(new string('a', GameIds.MaxLength + 1)));
    }
}

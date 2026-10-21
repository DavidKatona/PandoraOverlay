using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// AppLog's levels, its line and its exception text. Nothing here opens the
/// real log or changes the real categories' levels: the rules are tested on
/// categories made for the test.
/// </summary>
public class AppLogTests
{
    private static LogCategory Category(LogVerbosity level = LogVerbosity.Log) => new("LogTest", level);

    private static Dictionary<string, string> Levels(params (string Name, string Level)[] entries) =>
        entries.ToDictionary(e => e.Name, e => e.Level);

    [Fact]
    public void ACategoryWritesItsLevelAndEverythingMoreSevere()
    {
        var c = Category(LogVerbosity.Log);
        Assert.True(c.IsActive(LogVerbosity.Error));
        Assert.True(c.IsActive(LogVerbosity.Display));
        Assert.True(c.IsActive(LogVerbosity.Log));
        Assert.False(c.IsActive(LogVerbosity.Verbose));
        Assert.False(c.IsActive(LogVerbosity.VeryVerbose));
        Assert.False(c.IsActive(LogVerbosity.NoLogging)); // not a level a line can have
    }

    [Fact]
    public void AFatalLineIsWrittenEvenWhenTheCategoryIsSilenced()
    {
        var c = Category(LogVerbosity.NoLogging);
        Assert.True(c.IsActive(LogVerbosity.Fatal));
        Assert.False(c.IsActive(LogVerbosity.Error));
    }

    [Fact]
    public void WithoutConfigEveryCategoryRunsAtItsDefault()
    {
        Assert.Equal(LogVerbosity.Log, AppLog.LevelFor(Category(LogVerbosity.Log), false, null));
        Assert.Equal(LogVerbosity.Warning, AppLog.LevelFor(Category(LogVerbosity.Warning), false, Levels()));
    }

    [Fact]
    public void DetailedLogRaisesToVerboseButNeverLowers()
    {
        Assert.Equal(LogVerbosity.Verbose, AppLog.LevelFor(Category(LogVerbosity.Log), true, null));
        Assert.Equal(LogVerbosity.Verbose, AppLog.LevelFor(Category(LogVerbosity.Warning), true, null));
        Assert.Equal(LogVerbosity.VeryVerbose, AppLog.LevelFor(Category(LogVerbosity.VeryVerbose), true, null));
    }

    [Fact]
    public void TheMostSpecificWordWins()
    {
        var c = Category();
        Assert.Equal(LogVerbosity.Warning, AppLog.LevelFor(c, true, Levels(("global", "Warning"))));
        Assert.Equal(LogVerbosity.VeryVerbose, AppLog.LevelFor(c, false, Levels(("global", "Warning"), ("LogTest", "VeryVerbose"))));
        Assert.Equal(LogVerbosity.Error, AppLog.LevelFor(c, true, Levels(("LogTest", "Error")))); // an entry can lower a category
    }

    [Fact]
    public void ACategoryAnswersToItsNameWithOrWithoutLogInAnyCase()
    {
        var c = Category();
        Assert.Equal(LogVerbosity.Verbose, AppLog.LevelFor(c, false, Levels(("Test", "verbose"))));
        Assert.Equal(LogVerbosity.Verbose, AppLog.LevelFor(c, false, Levels(("LOGTEST", "VERBOSE"))));
        Assert.Equal(LogVerbosity.Log, AppLog.LevelFor(c, false, Levels(("LogOther", "Verbose"))));
    }

    [Fact]
    public void AnUnknownLevelIsIgnored()
    {
        Assert.Equal(LogVerbosity.Log, AppLog.LevelFor(Category(), false, Levels(("LogTest", "loud"))));
    }

    /// <summary>Three made-up categories at the given levels, all with Log as their default.</summary>
    private static LogCategory[] AtLevels(params LogVerbosity[] levels) =>
        levels.Select((level, i) => new LogCategory($"LogTest{i}", LogVerbosity.Log) { Verbosity = level }).ToArray();

    [Fact]
    public void NothingMovedIsNoNote()
    {
        Assert.Null(AppLog.LevelsNote(AtLevels(LogVerbosity.Log, LogVerbosity.Log, LogVerbosity.Log), detailed: false));
    }

    [Fact]
    public void DetailedLogIsNamedOnce_NotAsEveryCategory()
    {
        Assert.Equal("Detailed log (every category at Verbose or more)",
                     AppLog.LevelsNote(AtLevels(LogVerbosity.Verbose, LogVerbosity.Verbose, LogVerbosity.Verbose), detailed: true));
    }

    [Fact]
    public void ACategoryMovedByConfigIsNamed_BesideDetailedLog()
    {
        Assert.Equal("LogTest1 VeryVerbose",
                     AppLog.LevelsNote(AtLevels(LogVerbosity.Log, LogVerbosity.VeryVerbose, LogVerbosity.Log), detailed: false));
        Assert.Equal("Detailed log (every category at Verbose or more); LogTest1 VeryVerbose",
                     AppLog.LevelsNote(AtLevels(LogVerbosity.Verbose, LogVerbosity.VeryVerbose, LogVerbosity.Verbose), detailed: true));
    }

    [Fact]
    public void AGlobalLevelReadsAsEveryCategory()
    {
        Assert.Equal("every category at Warning",
                     AppLog.LevelsNote(AtLevels(LogVerbosity.Warning, LogVerbosity.Warning, LogVerbosity.Warning), detailed: false));
    }

    [Theory]
    [InlineData("Fatal", LogVerbosity.Fatal)]
    [InlineData("error", LogVerbosity.Error)]
    [InlineData(" Warning ", LogVerbosity.Warning)]
    [InlineData("display", LogVerbosity.Display)]
    [InlineData("Log", LogVerbosity.Log)]
    [InlineData("VERBOSE", LogVerbosity.Verbose)]
    [InlineData("veryverbose", LogVerbosity.VeryVerbose)]
    [InlineData("NoLogging", LogVerbosity.NoLogging)]
    [InlineData("All", LogVerbosity.VeryVerbose)] // Unreal's alias
    [InlineData("none", LogVerbosity.NoLogging)]
    public void LevelsAreReadAsUnrealSpellsThem(string text, LogVerbosity expected)
    {
        Assert.True(AppLog.TryParseVerbosity(text, out var level));
        Assert.Equal(expected, level);
    }

    [Theory]
    [InlineData("5")]           // Enum.TryParse would take a number
    [InlineData("99")]
    [InlineData("Log, Verbose")] // and a list, as flags
    [InlineData("")]
    [InlineData(null)]
    [InlineData("chatty")]
    public void NumbersListsAndUnknownWordsAreNotLevels(string? text)
    {
        Assert.False(AppLog.TryParseVerbosity(text, out _));
    }

    [Fact]
    public void EveryCategoryIsListedOnceWithAnUnrealStyleName()
    {
        Assert.Equal(AppLog.Categories.Count, AppLog.Categories.Select(c => c.Name).Distinct().Count());
        Assert.All(AppLog.Categories, c => Assert.StartsWith("Log", c.Name));
        Assert.Contains(AppLog.Net, AppLog.Categories);
        Assert.Contains(AppLog.Hotkeys, AppLog.Categories);
    }

    [Fact]
    public void ALineHasUnrealsShape_TheLevelLeftOutForLog()
    {
        var at = new DateTime(2026, 10, 21, 14, 3, 12, 345);
        Assert.Equal("[2026.10.21-14.03.12:345] LogNet: Warning: POST /api/map/mylocation 403 html 212 ms",
                     AppLog.FormatLine(at, "LogNet", LogVerbosity.Warning, "POST /api/map/mylocation 403 html 212 ms"));
        Assert.Equal("[2026.10.21-14.03.12:345] LogPoll: In game",
                     AppLog.FormatLine(at, "LogPoll", LogVerbosity.Log, "In game"));
    }

    [Fact]
    public void AMessagesFurtherLinesAreIndentedUnderIt()
    {
        var line = AppLog.FormatLine(new DateTime(2026, 10, 21), "LogApp", LogVerbosity.Fatal, "Crash: X\r\n   at A.B()\n   at C.D()");
        var parts = line.Split(Environment.NewLine);
        Assert.Equal(3, parts.Length);
        Assert.EndsWith("LogApp: Fatal: Crash: X", parts[0]);
        Assert.Equal("    at A.B()", parts[1]);
        Assert.Equal("    at C.D()", parts[2]);
    }

    [Theory]
    [InlineData("Cookie: connect.sid=s%3Aabc")]
    [InlineData("something CF_CLEARANCE=xyz")]
    public void ALineNamingACookieIsLeftOutWhole(string message)
    {
        var scrubbed = AppLog.Scrub(message);
        Assert.DoesNotContain("connect.sid", scrubbed, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cf_clearance", scrubbed, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("abc", scrubbed);
    }

    [Fact]
    public void AnOrdinaryLineIsLeftAsItIs()
    {
        Assert.Equal("Roster: 3 friends, 1 in game", AppLog.Scrub("Roster: 3 friends, 1 in game"));
    }

    /// <summary>A message made to hold everything that must never reach the log.</summary>
    private const string SecretMessage = "connect.sid=s%3ASECRET https://islapandora.eu/x?token=TOPSECRET the website said: BANNED";

    [Fact]
    public void AnExceptionIsItsTypeAndCodes_InsideOnesToo_NeverItsMessage()
    {
        var ex = new HttpRequestException(HttpRequestError.ConnectionError, SecretMessage,
                                          new SocketException((int)SocketError.ConnectionRefused), HttpStatusCode.BadGateway);
        var text = AppLog.Short(ex);
        Assert.Equal("HttpRequestException (ConnectionError, 502) <- SocketException (ConnectionRefused)", text);
        Assert.DoesNotContain("SECRET", text);
        Assert.DoesNotContain("BANNED", text);
    }

    [Fact]
    public void APlainExceptionIsJustItsType()
    {
        Assert.Equal("InvalidOperationException", AppLog.Short(new InvalidOperationException(SecretMessage)));
        Assert.Equal("TaskCanceledException <- TimeoutException", AppLog.Short(new TaskCanceledException("x", new TimeoutException(SecretMessage))));
    }

    [Fact]
    public void ACrashIsDescribedWithWhereItHappened_NeverItsMessage()
    {
        Exception caught;
        try
        {
            Throw();
            return;
        }
        catch (Exception ex)
        {
            caught = ex;
        }
        var text = AppLog.Describe(caught);
        Assert.StartsWith("InvalidOperationException <- FormatException", text);
        Assert.Contains(nameof(Throw), text);       // the stack trace's method
        Assert.Contains("--- inside it: FormatException", text);
        Assert.DoesNotContain("SECRET", text);
        Assert.DoesNotContain("BANNED", text);
    }

    private static void Throw()
    {
        try
        {
            throw new FormatException(SecretMessage);
        }
        catch (FormatException inner)
        {
            throw new InvalidOperationException(SecretMessage, inner);
        }
    }
}

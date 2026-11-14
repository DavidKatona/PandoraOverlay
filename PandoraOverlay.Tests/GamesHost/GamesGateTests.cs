using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The Games window against every other state of the overlay (A13, G15 to
/// G17): the whole table of <see cref="GamesGate.Closes"/>, one line per
/// event, and a check that no event of the enum is left out of it. The
/// events are named by string: <see cref="OverlayEvent"/> is internal, and an
/// xUnit theory's parameters must be public types.
/// </summary>
public class GamesGateTests
{
    [Theory]
    // The first row: these close the Games window.
    [InlineData(nameof(OverlayEvent.HidingAll), true)]          // hide-all hiding: it goes with the widgets (G16)
    [InlineData(nameof(OverlayEvent.EditModeToggled), true)]    // edit mode: Games first, then edit mode as usual
    [InlineData(nameof(OverlayEvent.SettingsOpening), true)]    // Settings and Server rules: modal
    [InlineData(nameof(OverlayEvent.SignInOpening), true)]      // the tray's sign-in: modal
    [InlineData(nameof(OverlayEvent.MapOpening), true)]         // the map: one of the two at a time
    [InlineData(nameof(OverlayEvent.FocusLeft), true)]          // a click on the game (G17)
    [InlineData(nameof(OverlayEvent.Exiting), true)]            // exit, a Windows shutdown, an update's restart
    // ShowingAll and the second row: Games stays.
    [InlineData(nameof(OverlayEvent.ShowingAll), false)]        // the widgets come back; Games stays, on top of them
    [InlineData(nameof(OverlayEvent.AutoHidden), false)]
    [InlineData(nameof(OverlayEvent.ShownAfterAutoHide), false)]
    [InlineData(nameof(OverlayEvent.CheckPrime), false)]
    [InlineData(nameof(OverlayEvent.StatsViewToggled), false)]
    [InlineData(nameof(OverlayEvent.MinimapViewToggled), false)]
    [InlineData(nameof(OverlayEvent.HeatmapToggled), false)]
    [InlineData(nameof(OverlayEvent.SkinKey), false)]
    [InlineData(nameof(OverlayEvent.PollFailed), false)]        // Games plays on; its watch note shows (A16)
    [InlineData(nameof(OverlayEvent.SessionEnded), false)]
    public void TheTable(string what, bool closes)
    {
        Assert.Equal(closes, GamesGate.Closes(Enum.Parse<OverlayEvent>(what)));
    }

    /// <summary>A new event in the enum must get its own line above, so the table is decided for it, never left to a default.</summary>
    [Fact]
    public void EveryEventHasItsLineInTheTable()
    {
        var lines = typeof(GamesGateTests).GetMethod(nameof(TheTable))!
            .GetCustomAttributes<InlineDataAttribute>()
            .SelectMany(a => a.GetData(null!))
            .Select(row => Enum.Parse<OverlayEvent>((string)row[0]))
            .ToList();

        Assert.Equal(lines.Count, lines.Distinct().Count()); // one line each
        Assert.Equal(Enum.GetValues<OverlayEvent>().OrderBy(e => e), lines.OrderBy(e => e));
    }

    /// <summary>A value no member names (a cast) closes nothing: the window stays as the player left it.</summary>
    [Fact]
    public void AnUnknownEventClosesNothing()
    {
        Assert.False(GamesGate.Closes((OverlayEvent)999));
    }

    /// <summary>A place in MainWindow that asks the table: <c>CloseGamesFor(OverlayEvent.Name)</c>.</summary>
    private static readonly Regex ClosingPlace = new(@"CloseGamesFor\(\s*OverlayEvent\.([A-Za-z]+)\s*\)", RegexOptions.Compiled);

    /// <summary>The Games part, the one file allowed to close the window without asking the table (it is where the table is asked).</summary>
    private const string GamesPart = "MainWindow.Games.cs";

    /// <summary>
    /// The table is the code (A13): read from the source of every
    /// Shell/MainWindow*.cs, every event the table says closes the window has
    /// a place in MainWindow that asks it, no place names an event that closes
    /// nothing, and nothing but the Games part itself calls CloseGames()
    /// directly, so nothing closes Games past the table.
    /// </summary>
    [Fact]
    public void EveryClosingEventHasItsPlaceInMainWindow()
    {
        var shell = Path.Combine(ThemeFileTests.RepoRoot(), "Shell");
        var files = Directory.GetFiles(shell, "MainWindow*.cs").ToDictionary(f => Path.GetFileName(f), f => File.ReadAllText(f));
        Assert.Contains(GamesPart, files.Keys); // the walk reaches the Games part
        Assert.Contains("MainWindow.xaml.cs", files.Keys);

        var placed = files.Values
            .SelectMany(text => ClosingPlace.Matches(text).Select(m => Enum.Parse<OverlayEvent>(m.Groups[1].Value)))
            .ToHashSet();
        var closing = Enum.GetValues<OverlayEvent>().Where(GamesGate.Closes).ToHashSet();
        Assert.True(placed.SetEquals(closing),
            "Every event that closes the Games window has a place in MainWindow, and no place names one that closes nothing:\n"
            + $"without a place: {string.Join(", ", closing.Except(placed))}\n"
            + $"placed but closing nothing: {string.Join(", ", placed.Except(closing))}");

        var pastTheTable = files.Where(f => f.Key != GamesPart && f.Value.Contains("CloseGames()", StringComparison.Ordinal)).Select(f => f.Key).ToList();
        Assert.True(pastTheTable.Count == 0, "Only the Games part closes the window without asking the table: " + string.Join(", ", pastTheTable));
    }
}

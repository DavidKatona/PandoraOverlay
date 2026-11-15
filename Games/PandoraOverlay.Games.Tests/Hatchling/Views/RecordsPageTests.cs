using System.Windows.Controls;
using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// Records (H26, H27; G22): the 24 achievements in two columns of twelve in the
/// table's order, an earned one unlike one not earned, the count, the lineage's
/// totals from the state and the chosen species' line. And what the four pages
/// between runs share: their layout states, their texts, no control that takes the
/// keyboard, and nothing ticking.
/// </summary>
[Collection(LoopTimers.Name)]
public class RecordsPageTests
{
    [Fact]
    public void TheTwentyFourStandInTwoColumnsOfTwelveInTheTablesOrder() => Sta.Run(() =>
    {
        var (game, _) = ViewHarness.Attached(ViewHarness.Ready());
        using var view = ViewHarness.ViewOf(game);
        var cells = PageHarness.Open<RecordsPage>(view, PageKind.Records).CellsShown;
        Assert.Equal(24, cells.Count);
        Assert.Equal(AchievementList.All.Select(a => a.Id), cells.Select(c => c.Id));
        for (var i = 0; i < cells.Count; i++)
        {
            Assert.Equal(i / RecordsPage.PerColumn * (RecordsPage.CellWidth + RecordsPage.CellGap), cells[i].Place.Left);
            Assert.Equal(i % RecordsPage.PerColumn * RecordsPage.CellHeight, cells[i].Place.Top);
            Assert.Equal(HatchlingText.AchievementName(cells[i].Id), cells[i].Name);
            Assert.Equal(HatchlingText.AchievementText(cells[i].Id), cells[i].Text);
        }
        Assert.Equal(520, 2 * RecordsPage.CellWidth + RecordsPage.CellGap);
        Assert.Equal("+20", cells[0].Reward);
        Assert.Equal("", cells.Single(c => c.Id == AchievementList.Blood).Reward);     // pays nothing: nothing shown
        Assert.Equal("", cells.Single(c => c.Id == AchievementList.Mastery).Reward);
        Assert.Equal("+500", cells[^1].Reward);
    });

    /// <summary>
    /// H84: an achievement's text has the cell's whole width beside the mark (237), and
    /// its reward stands on the name's line, so it takes nothing from the condition
    /// that earns it (in Isle's font two conditions were cut in the 204 left beside a
    /// reward centred across both lines).
    /// </summary>
    [Fact]
    public void AnAchievementsTextHasTheWholeCellAndItsRewardTheNamesLine() => Sta.Run(() =>
    {
        var (game, _) = ViewHarness.Attached(ViewHarness.Ready());
        using var view = ViewHarness.ViewOf(game);
        var page = PageHarness.Open<RecordsPage>(view, PageKind.Records);
        PageHarness.LayOut(view);
        var blocks = page.CellBlocks;
        Assert.Equal(24, blocks.Count);
        Assert.Equal(237, RecordsPage.CellWidth - 18);
        foreach (var (cell, name, text, reward) in blocks)
        {
            Assert.Equal(RecordsPage.CellWidth - 18, text.ActualWidth);
            var nameCentre = name.TranslatePoint(new System.Windows.Point(0, name.ActualHeight / 2), cell).Y;
            var rewardCentre = reward.TranslatePoint(new System.Windows.Point(0, reward.ActualHeight / 2), cell).Y;
            Assert.Equal(nameCentre, rewardCentre, 6);
            var textTop = text.TranslatePoint(new System.Windows.Point(0, 0), cell).Y;
            Assert.True(textTop >= nameCentre, $"{name.Text}: the text at {textTop}, the name's centre at {nameCentre}");
        }
    });

    [Fact]
    public void AnEarnedOneDiffersFromOneNotEarnedAndTheCountSaysHowMany() => Sta.Run(() =>
    {
        var saved = ViewHarness.Ready();
        saved.Achievements = new Achievements(AchievementList.All, new[] { AchievementList.First, AchievementList.Six });
        var (game, _) = ViewHarness.Attached(saved);
        using var view = ViewHarness.ViewOf(game);
        var page = PageHarness.Open<RecordsPage>(view, PageKind.Records);
        var cells = page.CellsShown.ToDictionary(c => c.Id);
        var earned = cells[AchievementList.First];
        var not = cells[AchievementList.Juvenile];
        Assert.Equal((HatchlingText.Earned, "Theme.Text.Good", "Theme.Text.Bright", "Theme.Text.Good"), (earned.Mark, earned.MarkKey, earned.NameKey, earned.RewardKey));
        Assert.Equal((HatchlingText.NotEarned, "Theme.Text.Off", "Theme.Text.Text2", "Theme.Text.Highlight"), (not.Mark, not.MarkKey, not.NameKey, not.RewardKey));
        Assert.Equal(HatchlingText.AchievementsCount(2, 24), page.SummaryShown.Count);
        Assert.Equal("2 of 24", page.SummaryShown.Count);
        Assert.Equal(2, page.CellsShown.Count(c => c.Mark == HatchlingText.Earned));
    });

    [Fact]
    public void TheTotalsAreTheStates() => Sta.Run(() =>
    {
        var saved = ViewHarness.Ready();
        saved.Wallet = new Wallet(1_240, 23_456);
        saved.Totals.Runs = 37;
        saved.Totals.Wins = 9;
        saved.Totals.Kills = 112;
        saved.Journal.Done = 4;
        Unlocks.Grant(saved, "Maiasaura");
        Unlocks.Grant(saved, "Troodon");
        saved.RecordToChange("Maiasaura").Grown = 151;   // two stars
        saved.RecordToChange("Troodon").Grown = 50;      // one
        saved.RecordToChange("Troodon").WonAt = 3;
        saved.RecordToChange("Dryosaurus").WonAt = 2;
        var dryo = saved.RecordToChange("Dryosaurus");
        dryo.Runs = 14;
        dryo.BestGrowth = 99.9999999999;   // a sum of steps a hair under 100: floored with the slack, 100
        var (game, _) = ViewHarness.Attached(saved);
        using var view = ViewHarness.ViewOf(game);
        var shown = PageHarness.Open<RecordsPage>(view, PageKind.Records).SummaryShown;
        Assert.Equal(new[] { "37", "9", "112", "23,456", "4 of 21", "3 of 63", "4", "3" }, shown.Totals);
        Assert.Equal(RecordsPage.TotalsOf(game.State), shown.Totals);
        Assert.Equal("Dryosaurus: 14 runs · best 100% · won at danger 2", shown.Chosen);
    });

    [Fact]
    public void TheChosenSpeciesLineLeavesOutAWinItNeverHad() => Sta.Run(() =>
    {
        var saved = ViewHarness.Ready();
        saved.RecordToChange("Dryosaurus").Runs = 1;
        saved.RecordToChange("Dryosaurus").BestGrowth = 41.3;
        var (game, _) = ViewHarness.Attached(saved);
        using var view = ViewHarness.ViewOf(game);
        Assert.Equal("Dryosaurus: 1 run · best 41%", PageHarness.Open<RecordsPage>(view, PageKind.Records).SummaryShown.Chosen);
    });

    [Fact]
    public void AnAchievementEarnedWhileRecordsIsShownShowsAtOnce() => Sta.Run(() =>
    {
        var saved = ViewHarness.Ready();
        foreach (var name in new[] { "Maiasaura", "Troodon", "Triceratops" }) Unlocks.Grant(saved, name);
        var (game, host) = ViewHarness.Attached(saved);
        using var view = ViewHarness.ViewOf(game);
        var page = PageHarness.Open<RecordsPage>(view, PageKind.Records);
        Assert.Equal("0 of 24", page.SummaryShown.Count);
        // The sixth species unlocked by growing it to Adult on the server earns "A wide lineage".
        game.OnReading(new DinoReading(true, "Stegosaurus", "Male", 0.749));
        host.ManualClock.Advance(TimeSpan.FromSeconds(3));
        game.OnReading(new DinoReading(true, "Stegosaurus", "Male", 0.751));
        Assert.True(game.State.Achievements.Has(AchievementList.Six));
        Assert.Equal("1 of 24", page.SummaryShown.Count);
        Assert.Equal(HatchlingText.Earned, page.CellsShown.Single(c => c.Id == AchievementList.Six).Mark);
        Assert.Equal("6 of 21", page.SummaryShown.Totals[4]);
    });

    [Fact]
    public void TheLayoutStatesShowWhatThePlanNames() => Sta.Run(() =>
    {
        var (game, _) = ViewHarness.Attached(ViewHarness.Running());
        using var view = ViewHarness.ViewOf(game);
        view.ShowLayoutState("records-fresh");
        var page = PageHarness.Shown<RecordsPage>(view);
        Assert.All(page.CellsShown, c => Assert.Equal(HatchlingText.NotEarned, c.Mark));
        Assert.Equal("0 of 24", page.SummaryShown.Count);
        Assert.Equal(HatchlingText.ChosenLine(Samples.Species, 0, 0, 0), page.SummaryShown.Chosen);

        view.ShowLayoutState("records-full");
        Assert.All(page.CellsShown, c => Assert.Equal(HatchlingText.Earned, c.Mark));
        Assert.Equal("24 of 24", page.SummaryShown.Count);
        Assert.Equal(new[] { "12,345", "9,876", "123,456", "12,345,678", "21 of 21", "63 of 63", "1,234", "5" }, page.SummaryShown.Totals);
        Assert.Equal("Pachycephalosaurus: 1,234 runs · best 100% · won at danger 5", page.SummaryShown.Chosen);
    });

    // ===== The four pages between runs, together ==============================================

    /// <summary>Every layout state of the four pages shows without throwing, its root measures the game's area, and no loop holds a timer after it (A11, A26).</summary>
    [Fact]
    public void EveryPagesLayoutStateShowsAndMeasuresTheGamesArea() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(ViewHarness.Running());
        using var view = ViewHarness.ViewOf(game);
        var writes = host.Memory.Writes.Count;
        foreach (var state in PageHarness.States.Concat(PageHarness.States.Reverse()))
        {
            view.ShowLayoutState(state);
            var root = view.Root;
            root.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            Assert.Equal(new System.Windows.Size(GameFrame.ViewWidth, GameFrame.ViewHeight), root.DesiredSize);
            PageHarness.LayOut(view);
            Assert.Equal(0, GameLoop.LiveTimers);
            Assert.Equal(LoopRate.Off, view.TheLoop.Rate);
        }
        Assert.Equal(writes, host.Memory.Writes.Count);
        Assert.Empty(host.Faults);
    });

    /// <summary>
    /// A scan of every text the four pages show in every state: none is empty where
    /// the plan gives it words (the only blanks are a card's danger won with no win
    /// and the reward of an achievement that pays nothing), and every one that names
    /// the currency names it as the text class builds it (A24).
    /// </summary>
    [Fact]
    public void EveryTextIsThePlansAndNoneIsEmptyWhereThePlanGivesWords() => Sta.Run(() =>
    {
        var (game, _) = ViewHarness.Attached(ViewHarness.Running());
        using var view = ViewHarness.ViewOf(game);
        foreach (var state in PageHarness.States)
        {
            view.ShowLayoutState(state);
            var page = Assert.IsAssignableFrom<System.Windows.FrameworkElement>(Assert.Single(((HatchlingViewRoot)view.Root).PageHost.Children));
            var texts = PageHarness.VisibleTexts(page).ToList();
            var blanks = texts.Count(t => t.Text.Length == 0);
            var allowed = page switch
            {
                CampPage camp => camp.CardsShown.Count(c => c.Won.Length == 0),
                RecordsPage => AchievementList.All.Count(a => a.Reward is AchievementReward.None),
                _ => 0,
            };
            Assert.True(blanks == allowed, $"{state}: {blanks} empty texts, {allowed} allowed");
            PageHarness.TheCurrencyIsTheTextClasss(texts);
            Assert.All(texts, t => Assert.Equal(System.Windows.TextTrimming.CharacterEllipsis, t.TextTrimming));
        }
    });

    /// <summary>No control of the four pages can take the keyboard focus (A33), as the view's own test holds for the Run page.</summary>
    [Fact]
    public void NoControlOfThePagesTakesTheKeyboardFocus() => Sta.Run(() =>
    {
        var (game, _) = ViewHarness.Attached(ViewHarness.Ready());
        using var view = ViewHarness.ViewOf(game);
        PageHarness.NoControlIsFocusable(PageHarness.Shown<CampPage>(view), atLeast: 6);       // five radios and the button
        PageHarness.NoControlIsFocusable(PageHarness.Open<JournalPage>(view, PageKind.Journal), atLeast: 3);   // the three Swaps
        PageHarness.NoControlIsFocusable(PageHarness.Open<LineagePage>(view, PageKind.Lineage), atLeast: 0);
        PageHarness.NoControlIsFocusable(PageHarness.Open<RecordsPage>(view, PageKind.Records), atLeast: 0);
        // The view as a whole on Camp: the tabs and Camp's controls.
        view.TabClicked(PageKind.Camp);
        PageHarness.NoControlIsFocusable((System.Windows.FrameworkElement)view.Root, atLeast: 10);
    });

    /// <summary>Nothing ticks on the pages between runs (A19): the loop is off on each, at rest and after a click.</summary>
    [Fact]
    public void TheLoopIsOffOnEveryPageBetweenRuns() => Sta.Run(() =>
    {
        var saved = ViewHarness.Ready(runs: 1);
        saved.Wallet = new Wallet(5_000, 5_000);
        var (game, _) = ViewHarness.Attached(saved);
        using var view = ViewHarness.ViewOf(game);
        Assert.Equal(LoopRate.Off, view.TheLoop.Rate);
        PageHarness.Shown<CampPage>(view).CardPressed("Maiasaura");
        PageHarness.Shown<CampPage>(view).CardReleased("Maiasaura");
        Assert.Equal(LoopRate.Off, view.TheLoop.Rate);
        var lineage = PageHarness.Open<LineagePage>(view, PageKind.Lineage);
        lineage.BuyClicked(Trait.DeepStomach);
        Assert.Equal(LoopRate.Off, view.TheLoop.Rate);
        PageHarness.Open<JournalPage>(view, PageKind.Journal).SwapClicked(0);
        Assert.Equal(LoopRate.Off, view.TheLoop.Rate);
        PageHarness.Open<RecordsPage>(view, PageKind.Records);
        Assert.Equal(LoopRate.Off, view.TheLoop.Rate);
        Assert.Equal(0, GameLoop.LiveTimers);
    });
}

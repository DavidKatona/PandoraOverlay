using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The Guide (H94): a fifth tab, reachable in Camp and during a run, that changes
/// nothing in the run; its list of sections, each a layout state, a click showing that
/// one; and its words held to the rules' own constants, so the Guide can never say one
/// number while the rules play another. Whether each section fits its card in every
/// theme is the app's test (<c>HatchlingInTheWindowTests</c>), where the themes are.
/// </summary>
[Collection(LoopTimers.Name)]
public class GuidePageTests
{
    // ---- The page ----------------------------------------------------------------------------

    [Fact]
    public void TheGuideListsEverySectionAndEachIsALayoutState() => Sta.Run(() =>
    {
        var (game, _) = ViewHarness.Attached(ViewHarness.Ready());
        using var view = ViewHarness.ViewOf(game);
        var page = PageHarness.Open<GuidePage>(view, PageKind.Guide);

        var (sections, chosen) = page.ListShown;
        Assert.Equal(HatchlingText.Guide.Select(s => s.Name), sections);
        Assert.Equal(0, chosen);
        Assert.Equal(new[] { "goal", "turn", "moving", "food", "predators", "growing", "danger", "lineage", "server", "terms" },
            HatchlingText.Guide.Select(s => s.Id));
        Assert.Equal(HatchlingText.Guide.Count, HatchlingText.Guide.Select(s => s.Id).Distinct().Count());
        Assert.Equal(HatchlingText.Guide.Select(s => "guide-" + s.Id), HatchlingView.AllStates.Where(s => s.StartsWith("guide-", StringComparison.Ordinal)));
        Assert.All(HatchlingText.Guide, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Name));
            Assert.NotEmpty(s.Entries);
            Assert.All(s.Entries, e => Assert.False(string.IsNullOrWhiteSpace(e.Lead) || string.IsNullOrWhiteSpace(e.Text), s.Id));
            if (s.Table is { } table) Assert.All(table.Rows, r => Assert.Equal(table.Header.Count, r.Count));
        });
        PageHarness.NoControlIsFocusable(page, atLeast: HatchlingText.Guide.Count);
    });

    [Fact]
    public void AClickOnASectionShowsItAndOnlyIt() => Sta.Run(() =>
    {
        var (game, _) = ViewHarness.Attached(ViewHarness.Ready());
        using var view = ViewHarness.ViewOf(game);
        var page = PageHarness.Open<GuidePage>(view, PageKind.Guide);

        for (var i = 0; i < HatchlingText.Guide.Count; i++)
        {
            page.Buttons[i].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            var section = HatchlingText.Guide[i];
            Assert.Equal(i, page.ListShown.Checked);
            Assert.Single(page.Buttons, b => b.IsChecked == true);
            var (title, texts) = page.SectionShown;
            Assert.Equal(section.Name, title);
            var expected = new List<string>();
            if (section.Intro is { } intro) expected.Add(intro);
            if (section.Table is { } table) expected.AddRange(table.Header.Concat(table.Rows.SelectMany(r => r)));
            expected.AddRange(section.Entries.SelectMany(e => new[] { e.Lead, e.Text }));
            Assert.Equal(expected, texts);
        }

        // The page remembers the section while the view lives: away and back, still there.
        view.TabClicked(PageKind.Records);
        view.TabClicked(PageKind.Guide);
        Assert.Equal(HatchlingText.Guide.Count - 1, page.ListShown.Checked);
    });

    [Fact]
    public void EachGuideStateShowsItsSection() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(ViewHarness.Running());
        using var view = ViewHarness.ViewOf(game);
        var writes = host.Memory.Writes.Count;
        foreach (var section in HatchlingText.Guide.Reverse())
        {
            view.ShowLayoutState("guide-" + section.Id);
            Assert.Equal(PageKind.Guide, view.Shown);
            Assert.Equal((HatchlingText.TabCamp, PageKind.Guide), view.TabsShown);   // the sample has no run: the bar at its widest
            Assert.Equal(section.Name, view.GuidePage.SectionShown.Title);
            Assert.Equal(HatchlingText.Money(Samples.Balance), view.WalletShown);
            PageHarness.LayOut(view);
        }
        Assert.Equal(writes, host.Memory.Writes.Count);
        Assert.Throws<ArgumentException>(() => view.ShowLayoutState("guide-nowhere"));
    });

    [Fact]
    public void TheGuideOpensDuringARunAndChangesNothingInIt() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(ViewHarness.Running());
        using var view = ViewHarness.ViewOf(game);
        Assert.Equal(PageKind.Run, view.Shown);
        var before = SaveFiles.PayloadOf(game.State);
        var writes = host.Memory.Writes.Count;

        var page = PageHarness.Open<GuidePage>(view, PageKind.Guide);
        Assert.Equal((HatchlingText.TabRun, PageKind.Guide), view.TabsShown);
        page.Buttons[2].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        Assert.Equal(before, SaveFiles.PayloadOf(game.State));
        Assert.Equal(writes, host.Memory.Writes.Count);
        Assert.Equal(LoopRate.Off, view.TheLoop.Rate);   // nothing ticks on a page between turns

        view.TabClicked(PageKind.Run);
        Assert.Equal(PageKind.Run, view.Shown);
        Assert.Equal(before, SaveFiles.PayloadOf(game.State));
        Assert.Empty(host.Faults);
    });

    [Fact]
    public void ThePageTextsAreTrimmedOrWrapAndNoneIsEmpty() => Sta.Run(() =>
    {
        var (game, _) = ViewHarness.Attached(ViewHarness.Ready());
        using var view = ViewHarness.ViewOf(game);
        foreach (var section in HatchlingText.Guide)
        {
            view.ShowLayoutState("guide-" + section.Id);
            var texts = PageHarness.VisibleTexts(view.GuidePage).ToList();
            Assert.True(texts.Count > section.Entries.Count * 2, section.Id);
            // The table's labelled corner is the one blank.
            Assert.Equal(section.Table is { } t && t.Header[0].Length == 0 ? 1 : 0, texts.Count(x => x.Text.Length == 0));
            // A one-line text ends in an ellipsis if a theme runs it long; a sentence wraps and is never cut.
            Assert.All(texts, x => Assert.True(x.TextTrimming == TextTrimming.CharacterEllipsis || x.TextWrapping == TextWrapping.Wrap, x.Text));
            Assert.All(texts.Where(x => x.TextWrapping == TextWrapping.Wrap), x => Assert.True(double.IsPositiveInfinity(x.MaxHeight), x.Text));
        }
    });

    // ---- The words are the rules' numbers ----------------------------------------------------

    private static GuideSection Section(string id) => HatchlingText.Guide.Single(s => s.Id == id);

    /// <summary>
    /// Walk, Sneak and Sprint, the owner's own question (Nov 15): the table's rows are the
    /// rules' constants, and the sentence that answers it reads as it does today.
    /// </summary>
    [Fact]
    public void WalkSneakAndSprintAreTheRulesNumbers()
    {
        var table = Section("moving").Table ?? throw new InvalidOperationException("no table");
        Assert.Equal(new[] { "", HatchlingText.Walk, HatchlingText.Sneak, HatchlingText.Sprint }, table.Header);
        var noise = table.Rows.Single(r => r[0] == HatchlingText.GuideNoise);
        Assert.Equal(new[] { Tuning.WalkNoise, Tuning.SneakNoise, Tuning.SprintNoise }.Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture)), noise.Skip(1));
        var cost = table.Rows.Single(r => r[0] == HatchlingText.GuideCost);
        Assert.Equal("×" + Tuning.PlainDrain, cost[1]);
        Assert.Equal("×" + Tuning.SneakDrain.ToString(System.Globalization.CultureInfo.InvariantCulture), cost[2]);
        // The sprint spans every species' own numbers, perks included (Tireless sprints at a
        // walk's cost, Fleet one hex further for 30% less): what a player can meet at Camp.
        var species = SpeciesTable.All.Select(s => Stats.Of(s, new Dictionary<Trait, int>(), 0, Array.Empty<Instinct>(), Tuning.GrowthMost)).ToList();
        var least = species.Min(s => s.SprintDrain).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        var most = species.Max(s => s.SprintDrain).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal($"×{least} to ×{most}", cost[3]);
        var hexes = table.Rows.Single(r => r[0] == HatchlingText.GuideHexes);
        Assert.Equal($"up to {species.Min(s => s.SprintRange)} or {species.Max(s => s.SprintRange)}", hexes[3]);
        var tireless = Stats.Of(SpeciesTable.All.Single(s => s.Perk == Perk.Tireless), new Dictionary<Trait, int>(), 0, Array.Empty<Instinct>(), Tuning.GrowthMost);
        Assert.Equal(Tuning.TirelessDrain, tireless.SprintDrain);

        // Today's words, so a reader sees what the test holds.
        Assert.Equal(new[] { "3", "1", "5" }, noise.Skip(1));
        Assert.Equal(new[] { "×1", "×1.5", "×1 to ×2.5" }, cost.Skip(1));
        Assert.Equal("up to 2 or 3", hexes[3]);
        Assert.Equal("Both move one hex. Sneak is quieter, noise 1 not 3, but takes ×1.5 the food and water.", HatchlingText.GuideWalkOrSneak);
        Assert.Contains(Section("moving").Entries, e => e.Text == HatchlingText.GuideWalkOrSneak);
    }

    [Fact]
    public void TheDrainsAreTheRulesNumbers()
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        Assert.Contains($"Food falls by {Tuning.FoodPerTurn.ToString(inv)} and water by {Tuning.WaterPerTurn.ToString(inv)}, out of {Tuning.FoodMost.ToString(inv)}", HatchlingText.GuideDrains);
        Assert.Contains($"falls {Builds.Of(Build.Apex).Hunger.ToString(inv)} times as fast", HatchlingText.GuideDrains);
        Assert.Equal("Food falls by 1.6 and water by 2, out of 100, times your move's cost. An Apex's food falls 1.3 times as fast.", HatchlingText.GuideDrains);
        Assert.Equal(HatchlingText.GuideDrains, Section("food").Entries[0].Text);

        // Every noise a move makes, from its constant.
        Assert.StartsWith(
            $"Walk {Tuning.WalkNoise}, sneak {Tuning.SneakNoise}, sprint {Tuning.SprintNoise}, attack {Tuning.AttackNoise}, eat or drink {Tuning.EatNoise}, eat fish {Tuning.FishNoise}, rest {Tuning.RestNoise}, before your build's.",
            HatchlingText.GuideNoises);
        Assert.Contains(Section("predators").Entries, e => e.Text == HatchlingText.GuideNoises);
    }

    [Fact]
    public void TheApexsWaitIsTheDangersOwn()
    {
        var first = Dangers.Of(Dangers.Lowest);
        Assert.Contains($"Outlast it for {first.Outlast} turns at danger {first.Level}", HatchlingText.GuideApex);
        Assert.Equal("At Elder a Tyrannosaurus arrives and always knows where you are. Outlast it for 15 turns at danger 1, or defeat it: either is a win.", HatchlingText.GuideApex);
        Assert.Contains(Section("goal").Entries, e => e.Text == HatchlingText.GuideApex);

        // The danger table, row by row, is the dangers' table.
        var table = Section("danger").Table ?? throw new InvalidOperationException("no table");
        Assert.Equal(Dangers.All.Count, table.Rows.Count);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        for (var i = 0; i < Dangers.All.Count; i++)
        {
            var d = Dangers.All[i];
            Assert.Equal(new[]
            {
                d.Level.ToString(inv), "×" + d.Threat.ToString("0.##", inv), $"every {d.Every} turns",
                $"{d.Most(Stages.Hatchling)} to {d.Most(Stages.Elder)}", $"{d.Outlast} turns", "×" + d.Pay.ToString("0.##", inv),
            }, table.Rows[i]);
        }
    }

    [Fact]
    public void TheStagesTableIsTheRulesScale()
    {
        var table = Section("growing").Table ?? throw new InvalidOperationException("no table");
        var size = table.Rows.Single(r => r[0] == HatchlingText.GuideSize);
        var lines = new[] { 0.0 }.Concat(Tuning.StageLines).ToList();
        Assert.Equal(lines.Select(g => $"{Tuning.Round(Stats.Scale(g) * 100)}%"), size.Skip(1));
        Assert.Equal(new[] { "35%", "57%", "78%", "100%", "115%" }, size.Skip(1));
        var growth = table.Rows.Single(r => r[0] == HatchlingText.GuideGrowth);
        Assert.Equal(new[] { "0%", "25%", "50%", "75%", "100%" }, growth.Skip(1));
    }

    [Fact]
    public void TheTurnAfterTheFirstPointsAtTheGuide()
    {
        Assert.Equal(1, Tuning.HintGuideTurn);
        Assert.Contains(HatchlingText.TabGuide, Hints.Text(HintCode.Guide));
    }
}

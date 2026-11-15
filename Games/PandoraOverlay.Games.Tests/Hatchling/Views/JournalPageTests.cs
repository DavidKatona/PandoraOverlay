using System.Text.RegularExpressions;
using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The Journal (H12, H18, H27, H71; G4, G6): the three entries and the one for the
/// real dino's species marked; the armed Swap; what server growth pays, the real
/// dino's species first, five rows at most; six lines lately at most; the last
/// line's three states; a reading that pays refreshing the page; and never a wait.
/// </summary>
[Collection(LoopTimers.Name)]
public class JournalPageTests
{
    private static int Writes(FakeHost host) => host.Memory.Writes.Count;

    private static DinoReading Reading(string species, double growth) => new(true, species, "Male", growth);

    /// <summary>A Camp whose journal asks for these three, with so much in the wallet.</summary>
    private static GameState Asking(long balance, params string[] species)
    {
        var state = ViewHarness.Ready(runs: 2);
        state.Wallet = new Wallet(balance, balance);
        state.Journal.Open = species.Select(n => SpeciesTable.TryGet(n, out var info) ? Journal.EntryFor(info) : throw new ArgumentException(n)).ToList();
        return state;
    }

    private static readonly string[] Three = { "Maiasaura", "Triceratops", "Pteranodon" };

    [Fact]
    public void TheJournalShowsItsThreeEntries() => Sta.Run(() =>
    {
        var saved = Asking(0, Three);
        saved.Journal.Open[0].Seen = 32.7;
        var (game, _) = ViewHarness.Attached(saved);
        using var view = ViewHarness.ViewOf(game);
        var entries = PageHarness.Open<JournalPage>(view, PageKind.Journal).EntriesShown;
        Assert.Equal(3, entries.Count);
        Assert.Equal(HatchlingText.Entry("Maiasaura", 75), entries[0].Title);
        Assert.Equal("Grow a Maiasaura by 75%", entries[0].Title);
        Assert.Equal(HatchlingText.Plus(Tuning.JournalReward), entries[0].Reward);
        Assert.Equal("32 of 75%", entries[0].Progress);
        Assert.Equal(32.7 / 75, entries[0].Share, 9);
        Assert.Equal("Grow a Triceratops by 50%", entries[1].Title);          // a big build: 50% for 500
        Assert.Equal(HatchlingText.Plus(Tuning.JournalBigReward), entries[1].Reward);
        Assert.Equal("0 of 50%", entries[1].Progress);
        Assert.Equal("Grow a Pteranodon by 75%", entries[2].Title);
        Assert.All(entries, e => Assert.Equal((HatchlingText.EntryNotPlaying, "Theme.Text.Dim"), (e.Line, e.LineKey)));
        Assert.All(entries, e => Assert.False(e.SwapEnabled));                // the wallet short of the swap's price
    });

    [Fact]
    public void TheEntryForTheRealDinosSpeciesIsMarked() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(Asking(0, Three));
        using var view = ViewHarness.ViewOf(game);
        var page = PageHarness.Open<JournalPage>(view, PageKind.Journal);
        game.OnReading(Reading("maiasaura", 0.429));   // in another case: the table's spelling (H36)
        var entries = page.EntriesShown;
        Assert.Equal((HatchlingText.EntryPlaying("Maiasaura", 42), "Theme.Text.Highlight"), (entries[0].Line, entries[0].LineKey));
        Assert.Equal("Your Maiasaura now: 42%", entries[0].Line);
        Assert.Equal(HatchlingText.EntryNotPlaying, entries[1].Line);
        Assert.Equal(HatchlingText.RealDino("Maiasaura", 42), page.FootShown.RealDino);

        host.ManualClock.Advance(TimeSpan.FromSeconds(3));
        game.OnReading(DinoReading.NotInGame);
        Assert.All(page.EntriesShown, e => Assert.Equal(HatchlingText.EntryNotPlaying, e.Line));
        Assert.Equal(HatchlingText.RealDinoOut, page.FootShown.RealDino);
    });

    [Fact]
    public void TheLastLineHasItsThreeStates() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(Asking(0, Three));
        using var view = ViewHarness.ViewOf(game);
        var page = PageHarness.Open<JournalPage>(view, PageKind.Journal);
        Assert.False(game.HasReading);
        Assert.Equal(HatchlingText.RealDinoWaiting, page.FootShown.RealDino);
        game.OnReading(DinoReading.NotInGame);
        Assert.Equal(HatchlingText.RealDinoOut, page.FootShown.RealDino);
        host.ManualClock.Advance(TimeSpan.FromSeconds(3));
        game.OnReading(Reading("Tyrannosaurus", 0.618));
        Assert.Equal("Your real dino: Tyrannosaurus, 61%", page.FootShown.RealDino);
        // A whole percent is floored with the growth's slack: 0.29 is 29, not 28.
        host.ManualClock.Advance(TimeSpan.FromSeconds(3));
        game.OnReading(Reading("Stegosaurus", 0.29));
        Assert.Equal(HatchlingText.RealDino("Stegosaurus", 29), page.FootShown.RealDino);
    });

    [Fact]
    public void SwapIsArmedCostsAndReplaces() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(Asking(120, Three));
        using var view = ViewHarness.ViewOf(game);
        var page = PageHarness.Open<JournalPage>(view, PageKind.Journal);
        var entries = page.EntriesShown;
        Assert.All(entries, e => Assert.Equal((HatchlingText.Swap(Tuning.SwapPrice), true, "NeutralButtonStyle"), (e.Swap, e.SwapEnabled, e.SwapStyle)));
        var writes = Writes(host);

        page.SwapClicked(1);
        Assert.Equal(1, page.ArmedEntry);
        Assert.Equal((HatchlingText.SwapArmed, "SaveButtonStyle"), (page.EntriesShown[1].Swap, page.EntriesShown[1].SwapStyle));
        Assert.Equal(writes, Writes(host));
        page.Disarm();                                  // the mouse leaving it
        Assert.Equal(-1, page.ArmedEntry);
        Assert.Equal(HatchlingText.Swap(Tuning.SwapPrice), page.EntriesShown[1].Swap);
        page.SwapClicked(1);
        page.SwapClicked(2);                            // another Swap: that one armed, the first not
        Assert.Equal(2, page.ArmedEntry);
        Assert.Equal(HatchlingText.Swap(Tuning.SwapPrice), page.EntriesShown[1].Swap);
        view.TabClicked(PageKind.Records);              // leaving the page disarms
        view.TabClicked(PageKind.Journal);
        Assert.Equal(-1, page.ArmedEntry);
        Assert.Equal(writes, Writes(host));

        page.SwapClicked(1);
        page.SwapClicked(1);
        Assert.Equal(writes + 1, Writes(host));
        Assert.Equal(70, game.State.Wallet.Balance);
        Assert.Equal(HatchlingText.Money(70), view.WalletShown);
        Assert.NotEqual("Triceratops", game.State.Journal.Open[1].Species);
        Assert.Equal(HatchlingText.Entry(game.State.Journal.Open[1].Species, (int)game.State.Journal.Open[1].Target), page.EntriesShown[1].Title);
        Assert.Equal("Maiasaura", game.State.Journal.Open[0].Species);

        // Short of the price: disabled, and a click does nothing.
        page.SwapClicked(0);
        page.SwapClicked(0);
        Assert.Equal(20, game.State.Wallet.Balance);
        Assert.All(page.EntriesShown, e => Assert.False(e.SwapEnabled));
        page.SwapClicked(2);
        page.SwapClicked(2);
        Assert.Equal(-1, page.ArmedEntry);
        Assert.Equal(20, game.State.Wallet.Balance);
        Assert.Empty(host.Faults);
    });

    [Fact]
    public void WhatServerGrowthPaysListsTheRealDinoFirstThenTheMostGrownFiveAtMost() => Sta.Run(() =>
    {
        var saved = Asking(0, Three);
        var grown = new (string Name, double Grown, double Fresh)[]
        {
            ("Tenontosaurus", 80, 2.4), ("Dryosaurus", 140.6, 1.5), ("Kentrosaurus", 20, 3.0),
            ("Omniraptor", 300, 1.0), ("Gallimimus", 55, 2.0), ("Troodon", 10, 3.0), ("Allosaurus", 60, 2.7),
        };
        foreach (var (name, g, f) in grown)
        {
            var record = saved.RecordToChange(name);
            record.Grown = g;
            record.Fresh = f;
        }
        var (game, _) = ViewHarness.Attached(saved);
        using var view = ViewHarness.ViewOf(game);
        var page = PageHarness.Open<JournalPage>(view, PageKind.Journal);
        Assert.Equal(new[] { "Omniraptor", "Dryosaurus", "Tenontosaurus", "Allosaurus", "Gallimimus" },
            page.PaysShown.Select(p => p.Row[..p.Row.IndexOf(' ')]));
        Assert.Equal((HatchlingText.PaysRow("Dryosaurus", 140), HatchlingText.Times(1.5)), (page.PaysShown[1].Row, page.PaysShown[1].Times));
        Assert.All(page.PaysShown, p => Assert.Equal("Theme.Text.Text", p.Key));

        // The real dino's species first, in Highlight, even never grown; and the rows stay five.
        game.OnReading(Reading("Stegosaurus", 0.5));
        var pays = page.PaysShown;
        Assert.Equal(JournalPage.MostPays, pays.Count);
        Assert.Equal((HatchlingText.PaysRow("Stegosaurus", 0), HatchlingText.Times(Tuning.FreshMax), "Theme.Text.Highlight"), pays[0]);
        Assert.Equal(new[] { "Stegosaurus", "Omniraptor", "Dryosaurus", "Tenontosaurus", "Allosaurus" }, pays.Select(p => p.Row[..p.Row.IndexOf(' ')]));
        Assert.All(pays.Skip(1), p => Assert.Equal("Theme.Text.Text", p.Key));
    });

    [Fact]
    public void LatelyShowsSixLinesAtMostNewestFirstAndSaysHowWithNone() => Sta.Run(() =>
    {
        var (empty, _) = ViewHarness.Attached(Asking(0, Three));
        using (var view = ViewHarness.ViewOf(empty))
        {
            var (lines, none) = PageHarness.Open<JournalPage>(view, PageKind.Journal).LatelyShown;
            Assert.Empty(lines);
            Assert.True(none);
        }

        var saved = Asking(0, Three);
        saved.Lately = new List<LatelyLine>
        {
            new(LatelyKind.Grew, "Maiasaura", 3.2, 2.4, 31, 0),
            new(LatelyKind.Star, "Maiasaura", 0, 0, 0, 0),
            new(LatelyKind.Unlocked, "Maiasaura", 0, 0, 0, 0),
            new(LatelyKind.Journal, "Maiasaura", 0, 0, 400, 75),
            new(LatelyKind.Grew, "Troodon", 1.0, 3.0, 12, 0),
            new(LatelyKind.Grew, "Allosaurus", 0.4, 1.0, 1, 0),
        };
        var (game, _) = ViewHarness.Attached(saved);
        using var busy = ViewHarness.ViewOf(game);
        var shown = PageHarness.Open<JournalPage>(busy, PageKind.Journal).LatelyShown;
        Assert.False(shown.None);
        Assert.Equal(ServerPlay.LatelyMost, shown.Lines.Count);
        Assert.Equal(("Maiasaura grew 3.2% at ×2.4", "+31"), shown.Lines[0]);
        Assert.Equal(("Maiasaura: a new star", "★"), shown.Lines[1]);
        Assert.Equal(("Maiasaura unlocked in Hatchling", "new"), shown.Lines[2]);
        Assert.Equal(("Journal: Maiasaura by 75%", "+400"), shown.Lines[3]);
        Assert.Equal(("Allosaurus grew 0.4% at ×1.0", "+1"), shown.Lines[5]);
    });

    [Fact]
    public void AReadingThatPaysWhileTheJournalIsShownRefreshesIt() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(Asking(0, Three));
        using var view = ViewHarness.ViewOf(game);
        var page = PageHarness.Open<JournalPage>(view, PageKind.Journal);
        var changed = 0;
        game.Changed += () => changed++;
        game.OnReading(Reading("Maiasaura", 0.300));
        host.ManualClock.Advance(TimeSpan.FromSeconds(3));
        game.OnReading(Reading("Maiasaura", 0.302));
        Assert.True(changed >= 2);
        Assert.True(game.State.Wallet.Balance > 0);
        var (lines, none) = page.LatelyShown;
        Assert.False(none);
        Assert.Equal(JournalPage.LatelyText(game.State.Lately[0]), lines[0]);
        Assert.StartsWith("Maiasaura grew 0.2% at ×3.0", lines[0].Words);
        Assert.Equal(HatchlingText.Money(game.State.Wallet.Balance), view.WalletShown);
        Assert.Equal(HatchlingText.PaysRow("Maiasaura", 0), page.PaysShown[0].Row);
        Assert.Equal(LoopRate.Off, view.TheLoop.Rate);
        Assert.Empty(host.Faults);
    });

    [Fact]
    public void NoTextOfTheBusiestJournalNamesAWait() => Sta.Run(() =>
    {
        var (game, _) = ViewHarness.Attached(ViewHarness.Running());
        using var view = ViewHarness.ViewOf(game);
        view.ShowLayoutState("journal-busy");
        var page = PageHarness.Shown<JournalPage>(view);
        var texts = PageHarness.Walk(page).OfType<System.Windows.Controls.TextBlock>().Select(t => t.Text).ToList();
        Assert.True(texts.Count > 30, $"{texts.Count} texts");
        var wait = new Regex(@"\d\s*(day|hour|min)", RegexOptions.IgnoreCase);
        Assert.DoesNotContain(texts, t => wait.IsMatch(t));
        Assert.DoesNotContain(texts, t => t.Contains("come back", StringComparison.OrdinalIgnoreCase) && t != HatchlingText.PaysNote);
    });

    [Fact]
    public void TheLayoutStatesShowWhatThePlanNames() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(ViewHarness.Running());
        game.OnReading(Reading("Triceratops", 0.5));   // the game's own reading: a sample never shows it
        using var view = ViewHarness.ViewOf(game);

        view.ShowLayoutState("journal-fresh");
        var page = PageHarness.Shown<JournalPage>(view);
        Assert.Equal(3, page.EntriesShown.Count);
        Assert.All(page.EntriesShown, e => Assert.Equal(0, e.Share));
        Assert.True(page.LatelyShown.None);
        Assert.Equal(HatchlingText.RealDinoWaiting, page.FootShown.RealDino);
        Assert.Empty(page.PaysShown);

        view.ShowLayoutState("journal-busy");
        var entries = page.EntriesShown;
        Assert.Equal(HatchlingText.EntryPlaying(Samples.Species, 100), entries[0].Line);
        Assert.Equal(HatchlingText.EntryNotPlaying, entries[1].Line);
        Assert.Equal(ServerPlay.LatelyMost, page.LatelyShown.Lines.Count);
        Assert.Equal(JournalPage.MostPays, page.PaysShown.Count);
        Assert.Equal("Theme.Text.Highlight", page.PaysShown[0].Key);
        Assert.Equal(HatchlingText.RealDino(Samples.Species, 100), page.FootShown.RealDino);

        // A sample takes no click.
        var writes = Writes(host);
        page.SwapClicked(0);
        page.SwapClicked(0);
        Assert.Equal(writes, Writes(host));
    });
}

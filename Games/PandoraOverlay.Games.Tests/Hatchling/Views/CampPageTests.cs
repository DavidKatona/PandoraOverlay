using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// What the tests of the four pages between runs share: the page the view shows,
/// read off its page host as the window would see it; a walk of what is on it.
/// </summary>
internal static class PageHarness
{
    /// <summary>The page the view shows now, as its type.</summary>
    public static T Shown<T>(HatchlingView view) where T : class =>
        Assert.IsType<T>(Assert.Single(((HatchlingViewRoot)view.Root).PageHost.Children));

    /// <summary>The view on <paramref name="kind"/>, by its tab, as a click on it.</summary>
    public static T Open<T>(HatchlingView view, PageKind kind) where T : class
    {
        view.TabClicked(kind);
        Assert.Equal(kind, view.Shown);
        return Shown<T>(view);
    }

    /// <summary>Every element under <paramref name="root"/> by the logical tree, the root included.</summary>
    public static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var node in Walk(child))
                yield return node;
    }

    /// <summary>Every text block under <paramref name="root"/> that would be seen: neither it nor a parent collapsed or hidden.</summary>
    public static IEnumerable<TextBlock> VisibleTexts(DependencyObject root)
    {
        var shown = new List<TextBlock>();
        void Visit(DependencyObject node)
        {
            if (node is UIElement { Visibility: not Visibility.Visible }) return;
            if (node is TextBlock text) shown.Add(text);
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) Visit(child);
        }
        Visit(root);
        return shown;
    }

    /// <summary>No control of the page can take the keyboard focus (A33).</summary>
    public static void NoControlIsFocusable(FrameworkElement page, int atLeast)
    {
        var controls = Walk(page).OfType<ButtonBase>().ToList();
        Assert.True(controls.Count >= atLeast, $"{controls.Count} controls found");
        Assert.All(controls, c => Assert.False(c.Focusable, c.Name));
    }

    /// <summary>
    /// Every text that names the currency names it as <see cref="HatchlingText"/>
    /// builds it (A24): an amount (<c>HatchlingText.Money</c>), or one of its sentences that holds
    /// the word. The source scan holds the word to <c>HatchlingText.cs</c>; this holds
    /// what a page shows.
    /// </summary>
    public static void TheCurrencyIsTheTextClasss(IEnumerable<TextBlock> texts)
    {
        var sentences = new[] { HatchlingText.NoteFirst, HatchlingText.NoteLocked, HatchlingText.TotalEarned, HatchlingText.TraitText(Trait.Legacy) };
        var amount = new Regex(@"\d " + Regex.Escape(HatchlingText.Currency) + @"\b");
        foreach (var text in texts.Select(t => t.Text).Where(t => t.Contains(HatchlingText.Currency, StringComparison.OrdinalIgnoreCase)))
            Assert.True(amount.IsMatch(text) || sentences.Contains(text), $"\"{text}\" names the currency not by the text class");
    }

    /// <summary>The root laid out in the game's frame, as the window's layout test does.</summary>
    public static void LayOut(HatchlingView view)
    {
        var root = view.Root;
        root.Measure(new Size(GameFrame.ViewWidth, GameFrame.ViewHeight));
        root.Arrange(new Rect(0, 0, GameFrame.ViewWidth, GameFrame.ViewHeight));
        Assert.Equal(GameFrame.ViewWidth, root.ActualWidth);
        Assert.Equal(GameFrame.ViewHeight, root.ActualHeight);
    }

    /// <summary>The layout states of the four pages between runs (A26): the plan's ten.</summary>
    public static readonly string[] States =
    {
        "camp-first", "camp-locked", "camp-everything", "lineage-fresh", "lineage-mid", "lineage-full",
        "journal-fresh", "journal-busy", "records-fresh", "records-full",
    };
}

/// <summary>
/// Camp (H27; H10, H15, H18, H20; G6): the 21 cards in the table's order and what
/// each says, a card chosen by a press and a release on it (H79), the panel, the
/// danger's radios, the button's four states with the armed unlock, the start, and
/// its layout states. The handlers are called as the events call them.
/// </summary>
[Collection(LoopTimers.Name)]
public class CampPageTests
{
    private static int Writes(FakeHost host) => host.Memory.Writes.Count;

    /// <summary>A Camp with something to spend and a run behind it (the first minute's note gone).</summary>
    private static GameState Rich(long balance = 5_000, int runs = 3)
    {
        var state = ViewHarness.Ready(runs: runs);
        state.Wallet = new Wallet(balance, balance);
        return state;
    }

    [Fact]
    public void TheCardsAreTheTablesTwentyOneAndAFreshSaveShowsTwoUnlocked() => Sta.Run(() =>
    {
        var (game, _) = ViewHarness.Attached(GameState.Fresh(SaveSamples.Moment));
        using var view = ViewHarness.ViewOf(game);
        var page = PageHarness.Shown<CampPage>(view);
        var cards = page.CardsShown;
        Assert.Equal(SpeciesTable.All.Select(s => s.Name), cards.Select(c => c.Species));
        Assert.Equal(SpeciesTable.All.Select(s => s.Name), cards.Select(c => c.Name));
        Assert.Equal(2, cards.Count(c => c.Fresh != HatchlingText.CardLocked));
        Assert.Equal(19, cards.Count(c => c.Fresh == HatchlingText.CardLocked));
        Assert.All(cards.Where(c => c.Fresh == HatchlingText.CardLocked), c =>
        {
            Assert.Equal("Theme.Text.Dim", c.NameKey);
            Assert.Equal("Theme.Text.Off", c.FreshKey);
        });
        Assert.All(SpeciesTable.Starters, name =>
        {
            var card = cards.Single(c => c.Species == name);
            Assert.Equal("Theme.Text.Bright", card.NameKey);
            Assert.Equal(HatchlingText.CardFresh(Tuning.FreshMax), card.Fresh);   // never grown: x3.0
            Assert.Equal("Theme.Text.Good", card.FreshKey);
        });
        // The second line: the build and its foods, a card's way ("plants, meat").
        Assert.Equal(HatchlingText.CardKind(Build.Runner, Diet.Plants), cards[0].Kind);
        Assert.Equal("Forager · plants, meat, fish", cards.Single(c => c.Species == "Beipiaosaurus").Kind);
        Assert.All(cards, c => Assert.Equal("", c.Won));
        Assert.All(cards, c => Assert.Equal(HatchlingText.Stars(0), c.Stars));
        // The cards stand in three columns and seven rows, in the table's order row by row.
        var places = page.Cards.Children.OfType<Border>().Select(b => (b.Margin.Left, b.Margin.Top)).ToList();
        Assert.Equal(21, places.Count);
        Assert.Equal((0.0, 0.0), places[0]);
        Assert.Equal((2 * (CampPage.CardWidth + CampPage.CardGap), 0.0), places[2]);
        Assert.Equal((0.0, 6 * (CampPage.CardHeight + CampPage.CardGap)), places[18]);
        Assert.Equal((2 * (CampPage.CardWidth + CampPage.CardGap), 6 * (CampPage.CardHeight + CampPage.CardGap)), places[20]);
    });

    [Fact]
    public void ACardSaysWhatServerGrowthPaysItsStarsAndItsBestDanger() => Sta.Run(() =>
    {
        var saved = Rich();
        var record = saved.RecordToChange("Dryosaurus");
        record.Grown = 160;      // two stars
        record.Fresh = 1.95;     // reads x2.0 (H24): Good, as shown
        record.WonAt = 3;
        saved.RecordToChange("Herrerasaurus").Fresh = 1.94;   // reads x1.9: Dim
        var (game, _) = ViewHarness.Attached(saved);
        using var view = ViewHarness.ViewOf(game);
        var cards = PageHarness.Shown<CampPage>(view).CardsShown;
        var dryo = cards.Single(c => c.Species == "Dryosaurus");
        Assert.Equal(HatchlingText.Stars(2), dryo.Stars);
        Assert.Equal(HatchlingText.CardWon(3), dryo.Won);
        Assert.Equal("Server growth ×2.0", dryo.Fresh);
        Assert.Equal("Theme.Text.Good", dryo.FreshKey);
        var herrera = cards.Single(c => c.Species == "Herrerasaurus");
        Assert.Equal("Server growth ×1.9", herrera.Fresh);
        Assert.Equal("Theme.Text.Dim", herrera.FreshKey);
    });

    [Fact]
    public void FreshnessIsReadAtTheHostsCalendar() => Sta.Run(() =>
    {
        var saved = Rich();
        saved.RecordToChange("Dryosaurus").Fresh = 1.0;
        saved.FreshAsOf = ManualClock.Start;
        var (game, host) = ViewHarness.Attached(saved);
        host.ManualClock.UtcNow = ManualClock.Start.AddDays(2);   // two days later: +0.5 (H18)
        using var view = ViewHarness.ViewOf(game);
        var page = PageHarness.Shown<CampPage>(view);
        Assert.Equal(HatchlingText.CardFresh(1.5), page.CardsShown[0].Fresh);
        Assert.Equal(HatchlingText.ServerPays(1.5), page.PanelShown.Pays);
        // Nothing on the page names a wait (G4).
        Assert.DoesNotContain(PageHarness.VisibleTexts(page), t => Regex.IsMatch(t.Text, @"\d\s*(day|hour|min)"));
    });

    [Fact]
    public void ChoosingACardChangesThePanelAndTheSave() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(Rich());
        using var view = ViewHarness.ViewOf(game);
        var page = PageHarness.Shown<CampPage>(view);
        Assert.Equal("Dryosaurus", page.PanelShown.Name);
        Assert.Equal("Theme.Text.Highlight", page.CardsShown[0].LineKey);

        var writes = Writes(host);
        page.CardPressed("Pachycephalosaurus");
        page.CardReleased("Pachycephalosaurus");
        Assert.Equal(writes + 1, Writes(host));
        Assert.Equal("Pachycephalosaurus", game.State.Selected);
        var panel = page.PanelShown;
        Assert.Equal("Pachycephalosaurus", panel.Name);
        Assert.Equal(HatchlingText.PanelKind(Build.Grazer, Diet.Plants), panel.Kind);
        Assert.Equal(HatchlingText.PerkName(Perk.Headbutt), panel.Perk);
        Assert.Equal(HatchlingText.PerkText(Perk.Headbutt), panel.PerkText);
        Assert.Equal(HatchlingText.BuildLine(Build.Grazer), panel.Build);
        var stats = Stats.Of(SpeciesTable.All[3], game.State.Ranks, 0, Array.Empty<Instinct>(), Tuning.ScaleAdultAt);
        Assert.Equal(HatchlingText.Count(stats.MaxHealth), panel.Health);
        Assert.Equal(HatchlingText.OneDecimal(stats.Attack), panel.Attack);
        Assert.Equal(HatchlingText.Count(stats.Defense), panel.Defense);
        Assert.Equal(HatchlingText.SightValue(stats.Sight), panel.Sight);
        Assert.Equal(HatchlingText.ServerGrown(0, 50), panel.Grown);
        var cards = page.CardsShown;
        Assert.Equal("Theme.Text.Highlight", cards.Single(c => c.Species == "Pachycephalosaurus").LineKey);
        Assert.Equal("Theme.Card.Line", cards[0].LineKey);

        // The chosen one again: nothing written.
        page.CardPressed("Pachycephalosaurus");
        page.CardReleased("Pachycephalosaurus");
        Assert.Equal(writes + 1, Writes(host));
    });

    [Fact]
    public void TheNumbersCountTheLineageAndTheStarsAndNoInstinct() => Sta.Run(() =>
    {
        var saved = Rich();
        saved.Ranks[Trait.DeepStomach] = 1;
        saved.Ranks[Trait.ThickHide] = 2;
        saved.Ranks[Trait.KeenNose] = 1;
        saved.RecordToChange("Dryosaurus").Grown = 320;   // three stars
        var (game, _) = ViewHarness.Attached(saved);
        using var view = ViewHarness.ViewOf(game);
        var panel = PageHarness.Shown<CampPage>(view).PanelShown;
        var stats = Stats.Of(SpeciesTable.All[0], saved.Ranks, 3, Array.Empty<Instinct>(), Tuning.ScaleAdultAt);
        Assert.Equal(HatchlingText.Count(stats.MaxHealth), panel.Health);
        Assert.Equal(HatchlingText.Count(Builds.Of(Build.Runner).Defense + 2), panel.Defense);
        Assert.Equal(HatchlingText.SightValue(stats.Sight), panel.Sight);
        Assert.Equal(HatchlingText.Stars(3), panel.Stars);
        Assert.Equal(HatchlingText.ServerGrownAll(320), panel.Grown);
    });

    [Fact]
    public void ACardIsChosenOnlyByAPressAndAReleaseOnIt() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(Rich());
        using var view = ViewHarness.ViewOf(game);
        var page = PageHarness.Shown<CampPage>(view);
        var writes = Writes(host);

        page.CardReleased("Maiasaura");                           // a release alone
        page.CardPressed("Maiasaura");
        page.CardReleased("Triceratops");                         // released on another
        page.CardReleased("Maiasaura");                           // that press was spent
        page.CardPressed("Maiasaura");
        page.CardLeft("Maiasaura");                               // the mouse left it
        page.CardReleased("Maiasaura");
        page.CardPressed("Maiasaura");
        view.TabClicked(PageKind.Records);                        // the page left
        view.TabClicked(PageKind.Camp);
        page.CardReleased("Maiasaura");
        Assert.Equal(writes, Writes(host));
        Assert.Equal("Dryosaurus", game.State.Selected);

        page.CardPressed("Maiasaura");
        page.CardReleased("Maiasaura");
        Assert.Equal("Maiasaura", game.State.Selected);
    });

    /// <summary>
    /// H85: a reading that changes the state redraws the page shown about every 3 s
    /// while the player's dino grows, and a press and a release straddling one keep
    /// the click. A step of 1% in 30 s is under the fastest growth <c>SeenGrowth</c>
    /// allows and pays at any freshness.
    /// </summary>
    [Fact]
    public void APressSurvivesAReadingThatRedrawsThePage() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(Rich());
        using var view = ViewHarness.ViewOf(game);
        var page = PageHarness.Shown<CampPage>(view);
        var before = game.State.Wallet.Balance;

        page.CardPressed("Maiasaura");
        game.OnReading(new DinoReading(true, "Dryosaurus", "Male", 0.300));
        host.ManualClock.Advance(TimeSpan.FromSeconds(30));
        game.OnReading(new DinoReading(true, "Dryosaurus", "Male", 0.310));
        Assert.True(game.State.Wallet.Balance > before, $"{game.State.Wallet.Balance} after {before}");
        Assert.Equal(HatchlingText.Money(game.State.Wallet.Balance), view.WalletShown);   // the view redrew
        Assert.Equal(PageKind.Camp, view.Shown);

        page.CardReleased("Maiasaura");
        Assert.Equal("Maiasaura", game.State.Selected);
        Assert.Empty(host.Faults);
    });

    /// <summary>
    /// H83: the chosen species' kind line stands in a row of its own under the header,
    /// across the panel's whole 242, where every kind fits every font (beside the well
    /// it had 158, and the Beipiaosaurus' lost "fish").
    /// </summary>
    [Fact]
    public void TheKindLineHasThePanelsWholeWidth() => Sta.Run(() =>
    {
        var (game, _) = ViewHarness.Attached(Rich());
        using var view = ViewHarness.ViewOf(game);
        var page = PageHarness.Shown<CampPage>(view);
        PageHarness.LayOut(view);
        Assert.Equal(242, page.ChosenKind.ActualWidth);
        Assert.True(page.ChosenKind.TranslatePoint(new Point(0, 0), page.Panel).Y >= 56);
    });

    [Fact]
    public void TheChosenCardKeepsItsLineUnderTheMouseAndTheOthersTakeTheHoverLine() => Sta.Run(() =>
    {
        var (game, _) = ViewHarness.Attached(Rich());
        using var view = ViewHarness.ViewOf(game);
        var page = PageHarness.Shown<CampPage>(view);
        page.CardEntered("Dryosaurus");
        Assert.Equal("Theme.Text.Highlight", page.CardsShown[0].LineKey);
        page.CardLeft("Dryosaurus");
        page.CardEntered("Maiasaura");
        Assert.Equal("Theme.Card.HoverLine", page.CardsShown.Single(c => c.Species == "Maiasaura").LineKey);
        page.CardLeft("Maiasaura");
        Assert.Equal("Theme.Card.Line", page.CardsShown.Single(c => c.Species == "Maiasaura").LineKey);
        Assert.DoesNotContain(page.CardsShown, c => c.LineKey == "Theme.Nav.Selected.Bar");
    });

    [Fact]
    public void TheButtonHasThePlansFourStates() => Sta.Run(() =>
    {
        // 1. No run finished yet: the first minute's note, "Start a run".
        var (first, _) = ViewHarness.Attached(Rich(runs: 0));
        using (var view = ViewHarness.ViewOf(first))
        {
            var shown = PageHarness.Shown<CampPage>(view).ActionShown;
            Assert.Equal((HatchlingText.NoteFirst, HatchlingText.StartRun, true, "SaveButtonStyle"), shown);
        }

        // 2. Unlocked, a run behind: the start's note.
        var (unlocked, _) = ViewHarness.Attached(Rich(runs: 1));
        using (var view = ViewHarness.ViewOf(unlocked))
            Assert.Equal((HatchlingText.NoteStart, HatchlingText.StartRun, true, "SaveButtonStyle"), PageHarness.Shown<CampPage>(view).ActionShown);

        // 3. Locked, the wallet short: the price, disabled. With no run finished too: a locked species can't start.
        var short_ = Rich(balance: 399, runs: 0);
        short_.Selected = "Maiasaura";
        var (poor, _) = ViewHarness.Attached(short_);
        using (var view = ViewHarness.ViewOf(poor))
            Assert.Equal((HatchlingText.NoteLocked, HatchlingText.UnlockShort(400), false, "NeutralButtonStyle"), PageHarness.Shown<CampPage>(view).ActionShown);

        // 4. Locked, the wallet enough: the unlock, armed by its first click.
        var enough = Rich(balance: 400);
        enough.Selected = "Maiasaura";
        var (rich, _) = ViewHarness.Attached(enough);
        using (var view = ViewHarness.ViewOf(rich))
        {
            var page = PageHarness.Shown<CampPage>(view);
            Assert.Equal((HatchlingText.NoteLocked, HatchlingText.Unlock(400), true, "NeutralButtonStyle"), page.ActionShown);
            page.ActionClicked();
            Assert.Equal((HatchlingText.NoteLocked, HatchlingText.UnlockArmed, true, "SaveButtonStyle"), page.ActionShown);
        }
    });

    [Fact]
    public void TheUnlockNeedsTwoClicksDisarmsOnLeavingAndSpendsThePrice() => Sta.Run(() =>
    {
        var saved = Rich(balance: 1_000);
        saved.Bought = 2;   // the third bought: 600 (H10)
        saved.Selected = "Maiasaura";
        var (game, host) = ViewHarness.Attached(saved);
        using var view = ViewHarness.ViewOf(game);
        var page = PageHarness.Shown<CampPage>(view);
        var writes = Writes(host);
        Assert.Equal(HatchlingText.Unlock(600), page.ActionShown.Button);

        page.ActionClicked();
        Assert.True(page.UnlockArmed);
        Assert.Equal(writes, Writes(host));   // the first click does nothing else
        page.Disarm();                        // the mouse leaving it (MouseLeave calls Disarm)
        Assert.False(page.UnlockArmed);
        Assert.Equal(HatchlingText.Unlock(600), page.ActionShown.Button);
        page.ActionClicked();
        Assert.True(page.UnlockArmed);
        page.CardPressed("Triceratops");      // a click elsewhere disarms, and the choice moves
        page.CardReleased("Triceratops");
        Assert.False(page.UnlockArmed);
        Assert.Equal("Triceratops", game.State.Selected);
        page.CardPressed("Maiasaura");
        page.CardReleased("Maiasaura");
        view.TabClicked(PageKind.Lineage);    // leaving the page disarms
        view.TabClicked(PageKind.Camp);
        Assert.False(page.UnlockArmed);
        Assert.False(Unlocks.IsUnlocked(game.State, "Maiasaura"));

        writes = Writes(host);
        page.ActionClicked();
        page.ActionClicked();
        Assert.Equal(writes + 1, Writes(host));
        Assert.True(Unlocks.IsUnlocked(game.State, "Maiasaura"));
        Assert.Equal(400, game.State.Wallet.Balance);
        Assert.Equal(HatchlingText.Money(400), view.WalletShown);
        Assert.Equal((HatchlingText.NoteStart, HatchlingText.StartRun, true, "SaveButtonStyle"), page.ActionShown);
        var card = page.CardsShown.Single(c => c.Species == "Maiasaura");
        Assert.Equal("Theme.Text.Bright", card.NameKey);
        Assert.NotEqual(HatchlingText.CardLocked, card.Fresh);
        Assert.Empty(host.Faults);
    });

    [Fact]
    public void AnArmedUnlockOfASpeciesGrownToAdultMeanwhileGoes() => Sta.Run(() =>
    {
        var saved = Rich(balance: 400);
        saved.Selected = "Maiasaura";
        var (game, host) = ViewHarness.Attached(saved);
        using var view = ViewHarness.ViewOf(game);
        var page = PageHarness.Shown<CampPage>(view);
        page.ActionClicked();
        Assert.True(page.UnlockArmed);
        // Growing the species to Adult on the server unlocks it: the armed button goes, the start comes (H10).
        game.OnReading(new DinoReading(true, "Maiasaura", "Male", 0.749));
        host.ManualClock.Advance(TimeSpan.FromSeconds(3));
        game.OnReading(new DinoReading(true, "Maiasaura", "Male", 0.751));
        Assert.True(Unlocks.IsUnlocked(game.State, "Maiasaura"));
        Assert.False(page.UnlockArmed);
        Assert.Equal(HatchlingText.StartRun, page.ActionShown.Button);
    });

    [Fact]
    public void TheDangersRadiosFollowTheHighestUnlockedAndTheChoice() => Sta.Run(() =>
    {
        var saved = Rich();
        saved.MaxDanger = 3;
        var (game, host) = ViewHarness.Attached(saved);
        using var view = ViewHarness.ViewOf(game);
        var page = PageHarness.Shown<CampPage>(view);
        var (radios, line) = page.DangerShown;
        Assert.Equal(new[] { (true, true), (true, false), (true, false), (false, false), (false, false) }, radios);
        Assert.Equal(HatchlingText.DangerOne, line);

        var writes = Writes(host);
        page.DangerClicked(3);
        Assert.Equal(writes + 1, Writes(host));
        Assert.Equal(3, game.State.Danger);
        (radios, line) = page.DangerShown;
        Assert.Equal(new[] { (true, false), (true, false), (true, true), (false, false), (false, false) }, radios);
        Assert.Equal(HatchlingText.DangerMore(Dangers.Of(3).Pay), line);
        Assert.Equal("Tougher hunters, more of them · pays ×1.5", line);

        page.DangerClicked(5);   // not unlocked: refused, nothing written
        page.DangerClicked(3);   // the chosen one: nothing written
        Assert.Equal(writes + 1, Writes(host));
        Assert.Single(page.DangerShown.Radios, r => r.Checked);
        Assert.True(page.DangerShown.Radios[2].Checked);
    });

    [Fact]
    public void StartARunStartsOneAndTheFirstTabBecomesRun() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(Rich());
        using var view = ViewHarness.ViewOf(game);
        var page = PageHarness.Shown<CampPage>(view);
        var writes = Writes(host);
        page.ActionClicked();
        Assert.Equal(writes + 1, Writes(host));
        Assert.NotNull(game.State.Run);
        Assert.Equal("Dryosaurus", game.State.Run!.Species);
        Assert.Equal(PageKind.Run, view.Shown);
        Assert.Equal((HatchlingText.TabRun, PageKind.Run), view.TabsShown);
        Assert.IsType<RunPage>(Assert.Single(((HatchlingViewRoot)view.Root).PageHost.Children));
        // With a run, Camp can't be reached (H20): its tab is the run.
        view.TabClicked(PageKind.Camp);
        Assert.Equal(PageKind.Run, view.Shown);
    });

    [Fact]
    public void TheFirstMinutesNoteShowsOnlyWithNoRunFinished() => Sta.Run(() =>
    {
        foreach (var (runs, note) in new[] { (0, HatchlingText.NoteFirst), (1, HatchlingText.NoteStart), (40, HatchlingText.NoteStart) })
        {
            var (game, _) = ViewHarness.Attached(Rich(runs: runs));
            using var view = ViewHarness.ViewOf(game);
            Assert.Equal(note, PageHarness.Shown<CampPage>(view).ActionShown.Note);
        }
    });

    [Fact]
    public void ThePortraitIsTheArtsDrawnAtOneAndPaintedOnlyWhenTheSpeciesChanges() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(Rich());
        var art = ArtBook.Load(typeof(HatchlingGame).Assembly);
        using var view = new HatchlingView(game, host, art);
        var page = PageHarness.Shown<CampPage>(view);
        var portrait = art.Frame("Dryosaurus", CampPage.PortraitAction, 0);
        Assert.NotNull(portrait);
        var expected = new PixelBuffer(CampPage.PortraitWidth, CampPage.PortraitHeight);
        expected.Draw(portrait!, CampPage.PortraitX + portrait!.PivotX, CampPage.PortraitY + portrait.PivotY);
        Assert.Equal(expected.Pixels, page.Portrait.Buffer.Pixels);
        Assert.All(Enumerable.Range(0, CampPage.PortraitWidth), x => Assert.Equal(0u, page.Portrait.Buffer.Pixels[x]));   // the top row: drawn at y 1

        page.CardPressed("Triceratops");
        page.CardReleased("Triceratops");
        Assert.NotEqual(expected.Pixels, page.Portrait.Buffer.Pixels);

        // Without the art, nothing is drawn and nothing throws (H30).
        var (bare, bareHost) = ViewHarness.Attached(Rich());
        using var empty = new HatchlingView(bare, bareHost, ArtBook.Empty);
        Assert.All(PageHarness.Shown<CampPage>(empty).Portrait.Buffer.Pixels, p => Assert.Equal(0u, p));
    });

    [Fact]
    public void WithoutAPortraitTheStandingFrameIsDrawnCentred() => Sta.Run(() =>
    {
        var full = ArtBook.Load(typeof(HatchlingGame).Assembly);
        var creatures = ArtBook.From(new[] { ("creatures", Atlas.Load(typeof(HatchlingGame).Assembly, "creatures")) });
        Assert.Null(creatures.Frame("Dryosaurus", CampPage.PortraitAction, 0));
        var stand = creatures.Frame("Dryosaurus", ArtNames.Stand, 0)!;
        Assert.NotNull(full.Frame("Dryosaurus", ArtNames.Stand, 0));
        var (game, host) = ViewHarness.Attached(Rich());
        using var view = new HatchlingView(game, host, creatures);
        var expected = new PixelBuffer(CampPage.PortraitWidth, CampPage.PortraitHeight);
        expected.Draw(stand, (CampPage.PortraitWidth - stand.Frame.Width) / 2 + stand.PivotX, (CampPage.PortraitHeight - stand.Frame.Height) / 2 + stand.PivotY);
        Assert.Equal(expected.Pixels, PageHarness.Shown<CampPage>(view).Portrait.Buffer.Pixels);
        Assert.Contains(expected.Pixels, p => p != 0);
    });

    [Fact]
    public void NothingIsTakenInALayoutState() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(Rich());
        using var view = ViewHarness.ViewOf(game);
        view.ShowLayoutState("camp-locked");
        var page = PageHarness.Shown<CampPage>(view);
        Assert.True(page.UnlockArmed);
        var writes = Writes(host);
        page.ActionClicked();
        page.CardPressed("Maiasaura");
        page.CardReleased("Maiasaura");
        page.DangerClicked(2);
        Assert.Equal(writes, Writes(host));
        Assert.Equal(Samples.Species, view.State.Selected);
    });

    [Fact]
    public void TheLayoutStatesShowWhatThePlanNames() => Sta.Run(() =>
    {
        var (game, _) = ViewHarness.Attached(ViewHarness.Running());
        using var view = ViewHarness.ViewOf(game);

        view.ShowLayoutState("camp-first");
        var page = PageHarness.Shown<CampPage>(view);
        Assert.Equal(2, page.CardsShown.Count(c => c.Fresh != HatchlingText.CardLocked));
        Assert.Equal("Dryosaurus", page.PanelShown.Name);
        Assert.Equal(HatchlingText.NoteFirst, page.ActionShown.Note);
        Assert.Equal(HatchlingText.Money(0), view.WalletShown);
        Assert.Equal((HatchlingText.TabCamp, PageKind.Camp), view.TabsShown);

        view.ShowLayoutState("camp-locked");
        Assert.Equal(Samples.Species, page.PanelShown.Name);
        Assert.Equal((HatchlingText.NoteLocked, HatchlingText.UnlockArmed, true, "SaveButtonStyle"), page.ActionShown);
        Assert.Equal(HatchlingText.Money(Samples.Balance), view.WalletShown);
        Assert.False(page.DangerShown.Radios[4].Enabled);
        Assert.Equal("Tougher hunters, more of them · pays ×1.75", page.DangerShown.Line);

        view.ShowLayoutState("camp-everything");
        var cards = page.CardsShown;
        Assert.DoesNotContain(cards, c => c.Fresh == HatchlingText.CardLocked);
        Assert.All(cards, c => Assert.Equal(HatchlingText.Stars(3), c.Stars));
        Assert.All(cards, c => Assert.Equal(HatchlingText.CardWon(5), c.Won));
        Assert.Equal(HatchlingText.StartRun, page.ActionShown.Button);   // no unlock button
        Assert.True(page.DangerShown.Radios[4].Checked);
        // The widest kind line of the 21 (H83), so the window's walk holds it in every theme.
        Assert.Equal("Beipiaosaurus", page.PanelShown.Name);
        Assert.Equal(HatchlingText.PanelKind(Build.Forager, Diet.Plants | Diet.Meat | Diet.Fish), page.PanelShown.Kind);
        Assert.Equal(HatchlingText.ServerGrownAll(345), page.PanelShown.Grown);
        Assert.Contains(cards, c => c.FreshKey == "Theme.Text.Good");
        Assert.Contains(cards, c => c.FreshKey == "Theme.Text.Dim");
    });
}

using System.Windows;
using System.Windows.Controls;
using PandoraOverlay.Games.Engine;

namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// Camp (H27), the page between runs where the next one is chosen: the 21 species
/// in the table's order, what the chosen one is and what growing it on the server
/// pays now, the danger, and the start (or the unlock). It is where the game's
/// second purpose is first met (G6): every card says what growing that species
/// on the server pays NOW, and a locked one says how growing it there unlocks it
/// here (H10). Nothing on it counts down (G4, H18): freshness is a multiplier.
///
/// <para><b>Shown only while no run exists</b> (H20): with one, the first tab is
/// Run and this page is not reachable, so it never speaks of a run under way.</para>
///
/// <para><b>The page never changes the game's state</b> (A20, H70): a click is a
/// call on <see cref="HatchlingGame"/> (<c>Select</c>, <c>SetDanger</c>,
/// <c>Unlock</c>, <c>StartRun</c>), which saves before it returns; the page then
/// draws <c>view.State</c> again. Every handler runs through the view's guard
/// (A10), and nothing ticks here (the loop stays off, A19).</para>
/// </summary>
internal sealed partial class CampPage : Grid, IHatchlingPage
{
    /// <summary>A species' card (H27): 170 x 58, 6 apart, three columns and seven rows.</summary>
    internal const double CardWidth = 170, CardHeight = 58, CardGap = 6;

    /// <summary>The columns of cards.</summary>
    internal const int Columns = 3;

    /// <summary>The portrait's surface in the well (H27): 72 x 52, the art drawn at (0, 1).</summary>
    internal const int PortraitWidth = 72, PortraitHeight = 52, PortraitX = 0, PortraitY = 1;

    /// <summary>The portraits' action in the art book (task H12's atlas: <c>&lt;Species&gt;/portrait/0</c>).</summary>
    internal const string PortraitAction = "portrait";

    private readonly HatchlingView _view;
    private readonly HatchlingGame _game;
    private readonly ArtBook _art;
    private readonly PixelSurface _portrait;
    private readonly SpeciesCard[] _cards;
    private readonly RadioButton[] _radios = new RadioButton[Dangers.Highest];
    private readonly CardPress<string> _press = new();

    /// <summary>The species under the mouse, or null: its card wears the hover line unless it is the chosen one.</summary>
    private string? _hovered;

    /// <summary>The species the portrait was last painted for: painted again only when the chosen one changes.</summary>
    private string? _portraitOf;

    /// <summary>The species the armed unlock would unlock.</summary>
    private string? _armedFor;

    /// <summary>The button's style as last set: what a test reads.</summary>
    private string _buttonStyle = "SaveButtonStyle";

    public CampPage(HatchlingView view, HatchlingGame game, ArtBook art)
    {
        _view = view;
        _game = game;
        _art = art;
        InitializeComponent();

        _portrait = new PixelSurface(PortraitWidth, PortraitHeight)
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Well.Child = _portrait;

        _cards = SpeciesTable.All.Select((species, i) => MakeCard(species.Name, i)).ToArray();
        for (var level = Dangers.Lowest; level <= Dangers.Highest; level++)
            _radios[level - 1] = MakeRadio(level);

        ActionButton.Click += (_, _) => Clicked(ActionClicked);
        ActionButton.MouseLeave += (_, _) => _view.Guarded(Disarm);
        // A click anywhere else disarms the unlock ("armed": a stray click never acts).
        PreviewMouseLeftButtonDown += (_, e) => _view.Guarded(() =>
        {
            if (!PageParts.IsInside(e.OriginalSource as DependencyObject, ActionButton)) Disarm();
        });
    }

    /// <inheritdoc />
    public FrameworkElement Root => this;

    /// <summary>The unlock is armed: its next click unlocks.</summary>
    internal bool UnlockArmed => _armedFor is not null;

    /// <summary>The portrait's surface: the tests read its buffer.</summary>
    internal PixelSurface Portrait => _portrait;

    // ===== IHatchlingPage ==================================================================

    /// <inheritdoc />
    public void Show()
    {
        _press.Forget();
        Draw();
    }

    /// <inheritdoc />
    public void Refresh()
    {
        _press.Forget();
        Draw();
    }

    /// <inheritdoc />
    public void Hide()
    {
        _press.Forget();
        _hovered = null;
        Disarm();
    }

    // ===== Drawing ===========================================================================

    /// <summary>The page as the state stands: every card, the panel, the danger, the note and the button. Nothing is made: every element is updated in place.</summary>
    private void Draw()
    {
        var state = _view.State;
        var now = PageParts.Now(_view, _game);
        foreach (var card in _cards) DrawCard(card, state, now);
        DrawPanel(state, now);
        SyncRadios();
        DrawAction(state);
    }

    /// <summary>
    /// A species' card (H27): the name (Bright when unlocked, Dim when not) and its
    /// stars; the build and what it eats, and the highest danger won with it; what
    /// server growth pays for it now (Good from ×2.0, as shown), or "Locked". Its
    /// line: the chosen one's in <c>Theme.Text.Highlight</c> (A46), kept under the
    /// mouse; the hover line for the others.
    /// </summary>
    private void DrawCard(SpeciesCard card, GameState state, DateTime now)
    {
        if (!SpeciesTable.TryGet(card.Species, out var info)) return;   // a card is made from the table
        var unlocked = Unlocks.IsUnlocked(state, info.Name);
        var record = state.Record(info.Name);

        card.Name.Text = info.Name;
        card.NameKey = unlocked ? "Theme.Text.Bright" : "Theme.Text.Dim";
        card.Name.SetResourceReference(TextBlock.ForegroundProperty, card.NameKey);
        card.Stars.Text = HatchlingText.Stars(state.Stars(info.Name));
        card.Kind.Text = HatchlingText.CardKind(info.Build, EatsOf(info));
        card.Won.Text = record.WonAt > 0 ? HatchlingText.CardWon(record.WonAt) : "";
        if (unlocked)
        {
            var fresh = Freshness.At(state, info.Name, now);
            card.Fresh.Text = HatchlingText.CardFresh(fresh);
            card.FreshKey = IsGood(fresh) ? "Theme.Text.Good" : "Theme.Text.Dim";
        }
        else
        {
            card.Fresh.Text = HatchlingText.CardLocked;
            card.FreshKey = "Theme.Text.Off";
        }
        card.Fresh.SetResourceReference(TextBlock.ForegroundProperty, card.FreshKey);
        DrawLine(card, state);
    }

    private void DrawLine(SpeciesCard card, GameState state)
    {
        card.LineKey = string.Equals(card.Species, state.Selected, StringComparison.Ordinal) ? "Theme.Text.Highlight"
            : string.Equals(card.Species, _hovered, StringComparison.Ordinal) ? "Theme.Card.HoverLine"
            : "Theme.Card.Line";
        card.Card.SetResourceReference(Border.BorderBrushProperty, card.LineKey);
    }

    /// <summary>
    /// The chosen species (H27): its portrait, name, stars and kind; its numbers at
    /// 75% growth with the lineage and its stars as they are now and no instinct
    /// (what a run of it would start toward, H19); its perk and its build's line;
    /// what it has grown on the server and what growing it pays now.
    /// </summary>
    private void DrawPanel(GameState state, DateTime now)
    {
        if (!SpeciesTable.TryGet(state.Selected, out var species)) return;   // the save's check holds the choice to the table
        var stars = state.Stars(species.Name);
        var stats = Stats.Of(species, state.Ranks, stars, Array.Empty<Instinct>(), Tuning.ScaleAdultAt);

        PaintPortrait(species.Name);
        ChosenName.Text = species.Name;
        ChosenStars.Text = HatchlingText.Stars(stars);
        ChosenKind.Text = HatchlingText.PanelKind(species.Build, stats.Eats);
        HealthValue.Text = HatchlingText.Count(stats.MaxHealth);
        AttackValue.Text = HatchlingText.OneDecimal(stats.Attack);
        DefenseValue.Text = HatchlingText.Count(stats.Defense);
        SightValue.Text = HatchlingText.SightValue(stats.Sight);
        PerkName.Text = HatchlingText.PerkName(species.Perk);
        PerkText.Text = HatchlingText.PerkText(species.Perk);
        BuildLine.Text = HatchlingText.BuildLine(species.Build);

        // A whole percent, floored with the stars' own slack, so "every star" and the
        // percent shown never disagree about a line a sum of small steps sits a hair under.
        var grown = PageParts.WholePercent(state.Record(species.Name).Grown);
        ServerGrown.Text = stars < Tuning.StarsAt.Count
            ? HatchlingText.ServerGrown(grown, (int)Tuning.StarsAt[stars])
            : HatchlingText.ServerGrownAll(grown);
        var fresh = Freshness.At(state, species.Name, now);
        ServerPays.Text = HatchlingText.ServerPays(fresh);
        ServerPays.SetResourceReference(TextBlock.ForegroundProperty, IsGood(fresh) ? "Theme.Text.Good" : "Theme.Text.Dim");

        DangerLine.Text = state.Danger <= Dangers.Lowest
            ? HatchlingText.DangerOne
            : HatchlingText.DangerMore(Dangers.Of(state.Danger).Pay);
    }

    /// <summary>
    /// The note and the button by the plan's four states (H27): a locked species
    /// offers its unlock (disabled while the wallet is short, armed when it is not);
    /// an unlocked one starts a run, with the first minute's note until a run has
    /// been finished. The armed unlock stays armed only while it still unlocks the
    /// same species with the wallet enough.
    /// </summary>
    private void DrawAction(GameState state)
    {
        if (!Unlocks.IsUnlocked(state, state.Selected))
        {
            Note.Text = HatchlingText.NoteLocked;
            var price = Unlocks.PriceNow(state);
            var enough = state.Wallet.Balance >= price;
            if (!enough || !string.Equals(_armedFor, state.Selected, StringComparison.Ordinal)) _armedFor = null;
            ActionButton.IsEnabled = enough;
            if (UnlockArmed) SetButton(HatchlingText.UnlockArmed, "SaveButtonStyle");
            else if (enough) SetButton(HatchlingText.Unlock(price), "NeutralButtonStyle");
            else SetButton(HatchlingText.UnlockShort(price), "NeutralButtonStyle");
            return;
        }
        _armedFor = null;
        Note.Text = state.Totals.Runs == 0 ? HatchlingText.NoteFirst : HatchlingText.NoteStart;
        ActionButton.IsEnabled = Camp.CanStart(state);
        SetButton(HatchlingText.StartRun, "SaveButtonStyle");
    }

    private void SetButton(string words, string style)
    {
        ActionButton.Content = words;
        if (_buttonStyle == style) return;
        _buttonStyle = style;
        ActionButton.SetResourceReference(StyleProperty, style);
    }

    /// <summary>
    /// The portrait (6.3): <c>&lt;Species&gt;/portrait/0</c> drawn at (0, 1); without one
    /// the standing frame at 1x, centred; nothing without either. Painted once
    /// whenever the chosen species changes: nothing here moves.
    /// </summary>
    private void PaintPortrait(string species)
    {
        if (string.Equals(_portraitOf, species, StringComparison.Ordinal)) return;
        _portraitOf = species;
        var buffer = _portrait.Buffer;
        buffer.Clear(0);
        if (_art.Frame(species, PortraitAction, 0) is { } portrait)
            buffer.Draw(portrait, PortraitX + portrait.PivotX, PortraitY + portrait.PivotY);
        else if (_art.Frame(species, ArtNames.Stand, 0) is { } stand)
            buffer.Draw(stand, (PortraitWidth - stand.Frame.Width) / 2 + stand.PivotX, (PortraitHeight - stand.Frame.Height) / 2 + stand.PivotY);
        _portrait.Present();
    }

    /// <summary>The radios as the state has them: the chosen danger checked, those above the highest unlocked disabled (H15).</summary>
    private void SyncRadios()
    {
        var state = _view.State;
        for (var i = 0; i < _radios.Length; i++)
        {
            var level = i + Dangers.Lowest;
            _radios[i].IsEnabled = level <= state.MaxDanger;
            _radios[i].IsChecked = level == state.Danger;
        }
    }

    /// <summary>What a diet eats on Camp: the build's, and fish for a Wader (the rules' own answer, <see cref="Stats"/>).</summary>
    private static Diet EatsOf(SpeciesInfo species) =>
        Stats.Of(species, new Dictionary<Trait, int>(), 0, Array.Empty<Instinct>(), Tuning.ScaleAdultAt).Eats;

    /// <summary>A multiplier shown in Good: from ×2.0 as it reads (one decimal, H24), so the colour and the words never disagree.</summary>
    private static bool IsGood(double fresh) => Tuning.Round1(fresh) >= Tuning.FreshGood;

    // ===== What a click does ===================================================================

    /// <summary>A click on a control: the handler through the guard, then the radios put back as the state has them (a radio clicked while nothing was taken shows what is true).</summary>
    private void Clicked(Action handler)
    {
        _view.Guarded(handler);
        _view.Settled(SyncRadios);
    }

    /// <summary>The left button went down on a species' card (H79): the first half of a click.</summary>
    internal void CardPressed(string species) => _press.Press(species);

    /// <summary>The left button came up on a species' card: a choice when the press began on that same card (H79).</summary>
    internal void CardReleased(string species)
    {
        if (_press.Release(species)) SpeciesClicked(species);
    }

    /// <summary>The mouse left a species' card: its hover line goes, and a press begun on it is forgotten (H79).</summary>
    internal void CardLeft(string species)
    {
        _press.Left(species);
        if (string.Equals(_hovered, species, StringComparison.Ordinal)) _hovered = null;
        if (CardOf(species) is { } card) DrawLine(card, _view.State);
    }

    /// <summary>The mouse came onto a species' card: the hover line, unless it is the chosen one (which keeps its own, A46).</summary>
    internal void CardEntered(string species)
    {
        _hovered = species;
        if (CardOf(species) is { } card) DrawLine(card, _view.State);
    }

    /// <summary>A species is chosen (kept in the save, H27), locked or not, so its panel says what it is and what unlocks it.</summary>
    internal void SpeciesClicked(string species)
    {
        Disarm();
        if (PageParts.Sampled(_view, _game)) return;
        if (string.Equals(_view.State.Selected, species, StringComparison.Ordinal)) return;
        if (_game.Select(species)) Draw();
    }

    /// <summary>A danger's radio (H15): the next run's danger; one above the highest unlocked is not taken.</summary>
    internal void DangerClicked(int level)
    {
        Disarm();
        if (PageParts.Sampled(_view, _game)) return;
        var state = _view.State;
        if (level == state.Danger || level > state.MaxDanger) return;
        if (_game.SetDanger(level)) Draw();
    }

    /// <summary>
    /// The button: "Start a run" starts one on the chosen species at the chosen
    /// danger and shows the Run page (the first tab is Run from then, H20); the
    /// unlock is armed by its first click and unlocks by its second, its price
    /// from the game's one wallet (G3, H10), the wallet in the bar following.
    /// </summary>
    internal void ActionClicked()
    {
        if (PageParts.Sampled(_view, _game)) return;
        var state = _view.State;
        if (Unlocks.IsUnlocked(state, state.Selected))
        {
            Disarm();
            if (_game.StartRun()) _view.ShowPage(PageKind.Run);
            return;
        }
        if (!UnlockArmed)
        {
            Arm();
            return;
        }
        var species = _armedFor!;
        Disarm();
        if (_game.Unlock(species)) _view.ShowWallet();
        Draw();
    }

    /// <summary>
    /// Arms the unlock (its first click's look: "Click again to unlock" in the save
    /// style), only where the state offers one the wallet can pay. The layout state
    /// <c>camp-locked</c> shows it so (A26).
    /// </summary>
    internal void Arm()
    {
        var state = _view.State;
        if (Unlocks.IsUnlocked(state, state.Selected) || state.Wallet.Balance < Unlocks.PriceNow(state)) return;
        _armedFor = state.Selected;
        SetButton(HatchlingText.UnlockArmed, "SaveButtonStyle");
    }

    /// <summary>The unlock back to its first look: the mouse leaving it, a click anywhere else, or the page left.</summary>
    internal void Disarm()
    {
        if (!UnlockArmed) return;
        _armedFor = null;
        DrawAction(_view.State);
    }

    // ===== Made once ============================================================================

    /// <summary>One species' card, at its place in the grid (row by row, in the table's order), with its three lines and its mouse.</summary>
    private SpeciesCard MakeCard(string species, int index)
    {
        var name = PageParts.Line("Theme.Text.Bright", "Theme.Size.12");
        name.SetResourceReference(TextBlock.FontWeightProperty, "Theme.Font.ValueWeight");
        var stars = PageParts.Line("Theme.Text.Highlight", "Theme.Size.11");
        var kind = PageParts.Line("Theme.Text.Dim", "Theme.Size.11");
        var won = PageParts.Line("Theme.Text.Good", "Theme.Size.10.5");
        var fresh = PageParts.Line("Theme.Text.Dim", "Theme.Size.11");

        var lines = PageParts.Rows(16, 15, 15);
        PageParts.In(lines, 0, PageParts.LeftAndRight(name, stars));
        PageParts.In(lines, 1, PageParts.LeftAndRight(kind, won));
        PageParts.In(lines, 2, fresh);

        var card = PageParts.Card(CardWidth, CardHeight);
        card.Child = lines;
        card.HorizontalAlignment = HorizontalAlignment.Left;
        card.VerticalAlignment = VerticalAlignment.Top;
        card.Margin = new Thickness(index % Columns * (CardWidth + CardGap), index / Columns * (CardHeight + CardGap), 0, 0);
        card.MouseEnter += (_, _) => _view.Guarded(() => CardEntered(species));
        card.MouseLeave += (_, _) => _view.Guarded(() => CardLeft(species));
        card.MouseLeftButtonDown += (_, _) => _view.Guarded(() => CardPressed(species));
        card.MouseLeftButtonUp += (_, _) => _view.Guarded(() => CardReleased(species));
        Cards.Children.Add(card);
        return new SpeciesCard(species, card, name, stars, kind, won, fresh);
    }

    /// <summary>A danger's radio, "1" to "5", in the row (focusable never, A33).</summary>
    private RadioButton MakeRadio(int level)
    {
        var radio = new RadioButton
        {
            Focusable = false,
            Content = HatchlingText.Count(level),
            Margin = new Thickness(0, 0, level < Dangers.Highest ? 12 : 0, 0),
        };
        radio.SetResourceReference(StyleProperty, "Radio");
        radio.Click += (_, _) => Clicked(() => DangerClicked(level));
        DangerRadios.Children.Add(radio);
        return radio;
    }

    private SpeciesCard? CardOf(string species) =>
        _cards.FirstOrDefault(c => string.Equals(c.Species, species, StringComparison.Ordinal));

    /// <summary>A species' card and its parts, with the keys last set on them (what a test reads: a key set by reference can't be read back).</summary>
    private sealed class SpeciesCard
    {
        public SpeciesCard(string species, Border card, TextBlock name, TextBlock stars, TextBlock kind, TextBlock won, TextBlock fresh)
        {
            Species = species;
            Card = card;
            Name = name;
            Stars = stars;
            Kind = kind;
            Won = won;
            Fresh = fresh;
        }

        public string Species { get; }
        public Border Card { get; }
        public TextBlock Name { get; }
        public TextBlock Stars { get; }
        public TextBlock Kind { get; }
        public TextBlock Won { get; }
        public TextBlock Fresh { get; }
        public string NameKey { get; set; } = "";
        public string FreshKey { get; set; } = "";
        public string LineKey { get; set; } = "";
    }

    // ===== What a test reads ===================================================================

    /// <summary>The cards as shown, in their order: the species, the three lines' words, and the keys of the name, the third line and the card's line.</summary>
    internal IReadOnlyList<(string Species, string Name, string Stars, string Kind, string Won, string Fresh, string NameKey, string FreshKey, string LineKey)> CardsShown =>
        _cards.Select(c => (c.Species, c.Name.Text, c.Stars.Text, c.Kind.Text, c.Won.Text, c.Fresh.Text, c.NameKey, c.FreshKey, c.LineKey)).ToList();

    /// <summary>The panel as shown.</summary>
    internal (string Name, string Stars, string Kind, string Health, string Attack, string Defense, string Sight,
        string Perk, string PerkText, string Build, string Grown, string Pays) PanelShown =>
        (ChosenName.Text, ChosenStars.Text, ChosenKind.Text, HealthValue.Text, AttackValue.Text, DefenseValue.Text, SightValue.Text,
            PerkName.Text, PerkText.Text, BuildLine.Text, ServerGrown.Text, ServerPays.Text);

    /// <summary>The note, the button's words, whether it is enabled, and its style's key.</summary>
    internal (string Note, string Button, bool Enabled, string Style) ActionShown =>
        (Note.Text, ActionButton.Content as string ?? "", ActionButton.IsEnabled, _buttonStyle);

    /// <summary>The five radios: enabled, checked; and the danger's line.</summary>
    internal (IReadOnlyList<(bool Enabled, bool Checked)> Radios, string Line) DangerShown =>
        (_radios.Select(r => (r.IsEnabled, r.IsChecked == true)).ToList(), DangerLine.Text);
}

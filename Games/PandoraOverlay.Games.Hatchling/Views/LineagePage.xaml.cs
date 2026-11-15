using System.Windows;
using System.Windows.Controls;

namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// The Lineage (H11, H27): the sixteen traits that are kept between runs, the
/// grind the owner asked for made visible (G5). Four branches in columns, each
/// trait a card: its ranks as pips, its rule, and one of its next rank's price,
/// "Complete", or the trait it needs first. A card that can be bought is a click:
/// one click (a press and a release on it, H79) buys one rank.
///
/// <para><b>A run under way is untouched (H19)</b>: a run began with a copy of the
/// ranks, so a trait bought here during one counts from the next, and the page's
/// first line says so. The page is reachable during a run for that reason.</para>
///
/// <para><b>The page never changes the game's state</b> (A20): a purchase is
/// <c>game.BuyTrait</c>, which spends from the game's one wallet (G3) and saves
/// before it returns; the page draws <c>view.State</c> again and the wallet in the
/// bar follows. Nothing ticks here.</para>
/// </summary>
internal sealed partial class LineagePage : Grid, IHatchlingPage
{
    /// <summary>A branch's column (H27): 186 wide, 10 apart; its name 20 high over its four cards.</summary>
    internal const double ColumnWidth = 186, ColumnGap = 10, BranchNameHeight = 20;

    /// <summary>A trait's card (H27): 186 x 97, 6 apart.</summary>
    internal const double CardHeight = 97, CardGap = 6;

    private readonly HatchlingView _view;
    private readonly HatchlingGame _game;
    private readonly TraitCard[] _cards;
    private readonly CardPress<Trait> _press = new();

    /// <summary>The trait whose card is under the mouse, or null.</summary>
    private Trait? _hovered;

    public LineagePage(HatchlingView view, HatchlingGame game)
    {
        _view = view;
        _game = game;
        InitializeComponent();
        for (var branch = 0; branch < Lineage.Branches; branch++) MakeBranchName(branch);
        _cards = Lineage.All.Select(MakeCard).ToArray();
    }

    /// <inheritdoc />
    public FrameworkElement Root => this;

    // ===== IHatchlingPage ==================================================================

    /// <inheritdoc />
    public void Show()
    {
        _press.Forget();
        Draw();
    }

    /// <inheritdoc />
    /// <remarks>
    /// A redraw in place keeps a card's press (H85): a reading that changes the state
    /// redraws the page shown about every 3 s while the player's dino grows, and a press
    /// and a release straddling one would otherwise lose the click. The release still
    /// asks the game (<see cref="BuyClicked"/>: <c>CanBuy</c>, then <c>BuyTrait</c>).
    /// </remarks>
    public void Refresh() => Draw();

    /// <inheritdoc />
    public void Hide()
    {
        _press.Forget();
        _hovered = null;
    }

    // ===== Drawing ===========================================================================

    private void Draw()
    {
        var state = _view.State;
        foreach (var card in _cards) DrawCard(card, state);
    }

    /// <summary>
    /// A trait's card (H27): the name (Dim while it needs the trait above it) and its
    /// pips; its rule; at the bottom "Complete" (Good), "Needs ... first" (Dim), or the
    /// next rank's price (Highlight when it can be bought, Off when the wallet is
    /// short). Its line: the hover line under the mouse, on a card that can be bought.
    /// </summary>
    private void DrawCard(TraitCard card, GameState state)
    {
        var info = Lineage.Of(card.Trait);
        var rank = state.Rank(card.Trait);
        var blocked = LineageRules.Blocked(state, card.Trait);

        card.Name.Text = HatchlingText.TraitName(card.Trait);
        card.NameKey = blocked ? "Theme.Text.Dim" : "Theme.Text.Bright";
        card.Name.SetResourceReference(TextBlock.ForegroundProperty, card.NameKey);
        card.Pips.Text = HatchlingText.Pips(rank, info.MaxRank);
        card.Rule.Text = HatchlingText.TraitText(card.Trait);

        if (rank >= info.MaxRank)
        {
            card.Bottom.Text = HatchlingText.TraitComplete;
            card.BottomKey = "Theme.Text.Good";
        }
        else if (blocked && Lineage.Above(card.Trait) is { } above)
        {
            card.Bottom.Text = HatchlingText.TraitNeeds(above.Trait);
            card.BottomKey = "Theme.Text.Dim";
        }
        else
        {
            var price = LineageRules.PriceOf(state, card.Trait);
            card.Bottom.Text = HatchlingText.TraitPrice(price);
            card.BottomKey = state.Wallet.Balance >= price ? "Theme.Text.Highlight" : "Theme.Text.Off";
        }
        card.Bottom.SetResourceReference(TextBlock.ForegroundProperty, card.BottomKey);
        DrawLine(card, state);
    }

    private void DrawLine(TraitCard card, GameState state)
    {
        card.LineKey = _hovered == card.Trait && CanBuy(state, card.Trait) ? "Theme.Card.HoverLine" : "Theme.Card.Line";
        card.Card.SetResourceReference(Border.BorderBrushProperty, card.LineKey);
    }

    /// <summary>The card is a click: the rules would sell its next rank now (not complete, not blocked, the wallet enough), and it is not a sample's.</summary>
    private bool CanBuy(GameState state, Trait trait) =>
        !PageParts.Sampled(_view, _game) && LineageRules.CanBuy(state, trait);

    // ===== What a click does ===================================================================

    /// <summary>The left button went down on a trait's card (H79).</summary>
    internal void CardPressed(Trait trait) => _press.Press(trait);

    /// <summary>The left button came up on a trait's card: a purchase when the press began on that same card (H79).</summary>
    internal void CardReleased(Trait trait)
    {
        if (_press.Release(trait)) BuyClicked(trait);
    }

    /// <summary>The mouse came onto a trait's card.</summary>
    internal void CardEntered(Trait trait)
    {
        _hovered = trait;
        DrawLine(_cards[(int)trait], _view.State);
    }

    /// <summary>The mouse left a trait's card: its hover line goes, and a press begun on it is forgotten (H79).</summary>
    internal void CardLeft(Trait trait)
    {
        _press.Left(trait);
        if (_hovered == trait) _hovered = null;
        DrawLine(_cards[(int)trait], _view.State);
    }

    /// <summary>
    /// A click on a trait's card: one rank bought when the rules sell it (H11), the
    /// wallet in the bar following at once; a card that can't be bought does nothing.
    /// </summary>
    internal void BuyClicked(Trait trait)
    {
        if (!CanBuy(_view.State, trait)) return;
        if (_game.BuyTrait(trait)) _view.ShowWallet();
        Draw();
    }

    // ===== Made once ============================================================================

    private void MakeBranchName(int branch)
    {
        var name = PageParts.Line("Theme.Text.Bright", "Theme.Size.12", HatchlingText.BranchName(branch));
        name.SetResourceReference(TextBlock.FontWeightProperty, "Theme.Font.ValueWeight");
        name.Width = ColumnWidth;
        name.Height = BranchNameHeight;
        name.HorizontalAlignment = HorizontalAlignment.Left;
        name.VerticalAlignment = VerticalAlignment.Top;
        name.Margin = new Thickness(branch * (ColumnWidth + ColumnGap), 0, 0, 0);
        Branches.Children.Add(name);
    }

    /// <summary>A trait's card in its branch's column, by its tier from the top: its name and pips (16), its rule (room for three lines, 44), what is left, its bottom line (15).</summary>
    private TraitCard MakeCard(TraitInfo info)
    {
        var name = PageParts.Line("Theme.Text.Bright", "Theme.Size.12");
        name.SetResourceReference(TextBlock.FontWeightProperty, "Theme.Font.ValueWeight");
        var pips = PageParts.Line("Theme.Text.Highlight", "Theme.Size.11");
        var rule = PageParts.Wrapping("Theme.Text.Text", "Theme.Size.11", 44);
        var bottom = PageParts.Line("Theme.Text.Highlight", "Theme.Size.11");

        var lines = PageParts.Rows(16, 44, 0, 15);
        PageParts.In(lines, 0, PageParts.LeftAndRight(name, pips));
        PageParts.In(lines, 1, rule);
        PageParts.In(lines, 3, bottom);

        var card = PageParts.Card(ColumnWidth, CardHeight);
        card.Child = lines;
        card.HorizontalAlignment = HorizontalAlignment.Left;
        card.VerticalAlignment = VerticalAlignment.Top;
        card.Margin = new Thickness(info.Branch * (ColumnWidth + ColumnGap), BranchNameHeight + (info.Tier - 1) * (CardHeight + CardGap), 0, 0);
        var trait = info.Trait;
        card.MouseEnter += (_, _) => _view.Guarded(() => CardEntered(trait));
        card.MouseLeave += (_, _) => _view.Guarded(() => CardLeft(trait));
        card.MouseLeftButtonDown += (_, _) => _view.Guarded(() => CardPressed(trait));
        card.MouseLeftButtonUp += (_, _) => _view.Guarded(() => CardReleased(trait));
        Branches.Children.Add(card);
        return new TraitCard(trait, card, name, pips, rule, bottom);
    }

    /// <summary>A trait's card and its parts, with the keys last set on them (what a test reads).</summary>
    private sealed class TraitCard
    {
        public TraitCard(Trait trait, Border card, TextBlock name, TextBlock pips, TextBlock rule, TextBlock bottom)
        {
            Trait = trait;
            Card = card;
            Name = name;
            Pips = pips;
            Rule = rule;
            Bottom = bottom;
        }

        public Trait Trait { get; }
        public Border Card { get; }
        public TextBlock Name { get; }
        public TextBlock Pips { get; }
        public TextBlock Rule { get; }
        public TextBlock Bottom { get; }
        public string NameKey { get; set; } = "";
        public string BottomKey { get; set; } = "";
        public string LineKey { get; set; } = "";
    }

    // ===== What a test reads ===================================================================

    /// <summary>The cards as shown, in the trait table's order: where each stands (its card's margin), its words, and the keys of its name, its bottom line and its line.</summary>
    internal IReadOnlyList<(Trait Trait, Thickness Place, string Name, string Pips, string Rule, string Bottom, string NameKey, string BottomKey, string LineKey)> CardsShown =>
        _cards.Select(c => (c.Trait, c.Card.Margin, c.Name.Text, c.Pips.Text, c.Rule.Text, c.Bottom.Text, c.NameKey, c.BottomKey, c.LineKey)).ToList();
}

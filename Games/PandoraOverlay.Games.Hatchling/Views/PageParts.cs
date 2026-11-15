using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// What the four pages between runs (Camp, the Lineage, the Journal, Records) build
/// in code the same way: a line of text by its two theme keys, a card, the rule
/// that makes a card a click, and the one test of whether a page shows a sample.
/// Every key handed in is ONE whole literal at its call (A25: the app's key scan
/// reads each <c>"Theme.…"</c> string in the game's source, and a key put together
/// from parts would escape it); nothing here names a key of its own but the card's.
/// </summary>
internal static class PageParts
{
    /// <summary>
    /// A one-line text: its colour and size by theme key, trimmed with an ellipsis
    /// when it does not fit (a string that no longer fits in a theme's font ends in
    /// an ellipsis, never in another wording), centred in its row.
    /// </summary>
    public static TextBlock Line(string foregroundKey, string sizeKey, string text = "")
    {
        var block = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, foregroundKey);
        block.SetResourceReference(TextBlock.FontSizeProperty, sizeKey);
        return block;
    }

    /// <summary>
    /// A wrapping text held to <paramref name="height"/> (its room for so many lines
    /// in the plan's boxes): a word that would spill is cut at the last line that
    /// fits, with an ellipsis, so nothing below it moves (H27: a fixed page).
    /// </summary>
    public static TextBlock Wrapping(string foregroundKey, string sizeKey, double height)
    {
        var block = Line(foregroundKey, sizeKey);
        block.TextWrapping = TextWrapping.Wrap;
        block.MaxHeight = height;
        block.VerticalAlignment = VerticalAlignment.Top;
        return block;
    }

    /// <summary>
    /// A card (the plan's 2): <c>Theme.Card.Surface</c>, its line, the card corner,
    /// one unit of line. The plan's padding is measured from the card's edge, so
    /// the line's unit comes off it: (8, 6) is a padding of (7, 5) inside the line,
    /// and a species' card of 58 keeps its three lines (16 + 15 + 15).
    /// </summary>
    public static Border Card(double width, double height)
    {
        var card = new Border
        {
            Width = width,
            Height = height,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(7, 5, 7, 5),
        };
        card.SetResourceReference(Border.BackgroundProperty, "Theme.Card.Surface");
        card.SetResourceReference(Border.BorderBrushProperty, "Theme.Card.Line");
        card.SetResourceReference(Border.CornerRadiusProperty, "Theme.Corner.Card");
        return card;
    }

    /// <summary>A grid of fixed rows, top to bottom, for a card's lines.</summary>
    public static Grid Rows(params double[] heights)
    {
        var grid = new Grid();
        foreach (var height in heights)
            grid.RowDefinitions.Add(new RowDefinition { Height = height > 0 ? new GridLength(height) : new GridLength(1, GridUnitType.Star) });
        return grid;
    }

    /// <summary>A row of a card: a part at the left that takes what is left, and one at the right sized to what it holds.</summary>
    public static Grid LeftAndRight(FrameworkElement left, FrameworkElement right, double gap = 6)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        right.Margin = new Thickness(gap, 0, 0, 0);
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    /// <summary>Puts <paramref name="element"/> in row <paramref name="row"/> of <paramref name="grid"/>.</summary>
    public static T In<T>(Grid grid, int row, T element) where T : UIElement
    {
        Grid.SetRow(element, row);
        grid.Children.Add(element);
        return element;
    }

    /// <summary>
    /// The page shows a layout state's sample (A26), not the game's own state:
    /// <c>view.State</c> is the sample then. A page then takes no click (a sample is
    /// a still picture, and the game's actions would act on the save), and reads
    /// what it would otherwise ask the host's calendar or the last reading from the
    /// sample itself, so a picture is the same on every machine and every day.
    /// </summary>
    public static bool Sampled(HatchlingView view, HatchlingGame game) => !ReferenceEquals(view.State, game.State);

    /// <summary>
    /// The calendar a page reads freshness at (H18): the host's, or, for a sample,
    /// the moment its freshness was last brought up to date, so a sample shows the
    /// multipliers it was built with and never one a later day would make.
    /// </summary>
    public static DateTime Now(HatchlingView view, HatchlingGame game) =>
        Sampled(view, game) ? view.State.FreshAsOf : game.UtcNow;

    /// <summary>The node is <paramref name="container"/> or inside it (a click on an armed control's own content is a click on it).</summary>
    public static bool IsInside(DependencyObject? node, DependencyObject container)
    {
        for (var d = node; d is not null;
             d = d is Visual ? VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (ReferenceEquals(d, container)) return true;
        return false;
    }

    /// <summary>A whole percent of growth, floored (never rounded up to a stage or a target not reached), with the slack summed steps need (H58).</summary>
    public static int WholePercent(double percent) => (int)Math.Floor(percent + RunPay.GrowthSlack);
}

/// <summary>
/// A card that is a click (H79): the left button pressed on it and released on the
/// same card. A release alone picks nothing, nor does a press on one card released
/// on another; the press is forgotten when the mouse leaves the card it began on and
/// when the page is shown or left; a redraw in place keeps it (H85). One per page: a
/// page's cards share it.
/// </summary>
/// <typeparam name="T">What names a card: a species, a trait.</typeparam>
internal sealed class CardPress<T> where T : notnull
{
    private T? _pressed;
    private bool _down;

    /// <summary>The left button went down on <paramref name="card"/>.</summary>
    public void Press(T card)
    {
        _pressed = card;
        _down = true;
    }

    /// <summary>The left button came up on <paramref name="card"/>: true when the press began on that same card. The press is spent either way.</summary>
    public bool Release(T card)
    {
        var was = _down && EqualityComparer<T>.Default.Equals(_pressed!, card);
        Forget();
        return was;
    }

    /// <summary>The mouse left <paramref name="card"/>: a press begun on it is forgotten.</summary>
    public void Left(T card)
    {
        if (_down && EqualityComparer<T>.Default.Equals(_pressed!, card)) Forget();
    }

    /// <summary>No press is remembered (the page shown or left).</summary>
    public void Forget()
    {
        _pressed = default;
        _down = false;
    }

    /// <summary>A press is remembered (for the tests).</summary>
    public bool Down => _down;
}

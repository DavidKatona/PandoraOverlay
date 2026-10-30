using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace PandoraOverlay;

/// <summary>
/// The walk (FIT.md section 3, the fit factor): whether a window's text and
/// pages fit the frame they are laid out in, read off the layout itself. After
/// a window's measure and arrange passes every one-line text either got the
/// width it asks for or it did not, and every scroll area the frame must hold
/// whole either holds its content or it does not. Pure given a laid-out root:
/// it reads the visual tree and formats text, it changes nothing, and it
/// knows no window by name, so a page or a frame the owner resizes later is
/// measured as it is (the constants in the code ARE the layout; the walk reads
/// them rather than keeping a copy).
/// <para>
/// The one implementation: the tests lay the Settings dialog, the update
/// cards and the sign-in window out at a theme's fit factor and hold every
/// theme to it (<c>DialogLayoutTests</c>, <c>FitFactorTests</c>), and the
/// overlay walks its live windows once after a theme switch and logs what it
/// found (<see cref="Count"/>, MainWindow.ApplyTheme). Until the fit factor's
/// release this was the tests' private code.
/// </para>
/// <para>
/// In layout units, before any scale transform: a widget's size slider and
/// Windows' display scaling scale a frame and its text together, so what
/// fits at one scale fits at every scale.
/// </para>
/// </summary>
public static class FitProbe
{
    /// <summary>
    /// Layout rounding's room for a one-line text inside its box: a line
    /// within half a pixel of its box is drawn whole. Only the lines have it:
    /// a scroll area has none, since WPF's ScrollViewer shows its bar at any
    /// overflow (<see cref="Spills"/>).
    /// </summary>
    public const double Slack = 0.5;

    #region The scroll areas a frame holds whole

    /// <summary>
    /// Marks a scroll area that must hold its content whole in every theme:
    /// the Settings dialog's page column (<c>local:FitProbe.HoldsWhole="True"</c>),
    /// where a scrollbar would mean a theme's font made a page taller than the
    /// dialog's frame. A list in its own fixed-height ScrollViewer scrolls by
    /// design (the waypoints, the skins), so a scroll area is not one unless
    /// it says so.
    /// </summary>
    public static readonly DependencyProperty HoldsWholeProperty =
        DependencyProperty.RegisterAttached("HoldsWhole", typeof(bool), typeof(FitProbe), new PropertyMetadata(false));

    public static bool GetHoldsWhole(DependencyObject element) => (bool)element.GetValue(HoldsWholeProperty);

    public static void SetHoldsWhole(DependencyObject element, bool value) => element.SetValue(HoldsWholeProperty, value);

    #endregion

    #region What the walk finds

    /// <summary>
    /// A one-line text wider than its room: its natural width in its own font,
    /// the room the boxes around it leave, whether it ends in an ellipsis, and
    /// whether its own box fits that room (or the ellipsis itself is cut off).
    /// </summary>
    public sealed record Line(string Text, double Natural, double Room, bool Trims, bool BoxFits)
    {
        /// <summary>
        /// Cut off, against the framework's rule: a line that runs past its
        /// room ends in an ellipsis, and the ellipsis shows (the owner,
        /// question 29). A line that trims is a theme's ellipsis, allowed.
        /// </summary>
        public bool CutOff => !Trims || !BoxFits;
    }

    /// <summary>A scroll area marked <see cref="HoldsWholeProperty"/> whose content is taller than it: a page that would scroll.</summary>
    public sealed record Spill(double Extent, double Viewport);

    /// <summary>Everything the walk found in one laid-out root.</summary>
    public sealed record Report(IReadOnlyList<Line> Lines, IReadOnlyList<Spill> Spills)
    {
        /// <summary>The lines cut off against the rule (no ellipsis, or an ellipsis past the edge).</summary>
        public IEnumerable<Line> CutOff => Lines.Where(l => l.CutOff);

        /// <summary>Nothing cut off and nothing spilling: what the tests hold every theme to at its fit factor.</summary>
        public bool Fits => !CutOff.Any() && Spills.Count == 0;

        /// <summary>Every overflow, an ellipsis included: what the self-check in the log counts.</summary>
        public int Overflows => Lines.Count + Spills.Count;
    }

    /// <summary>The walk over one laid-out root (a window's content).</summary>
    public static Report Walk(FrameworkElement root) => new(LongLines(root).ToList(), Spills(root).ToList());

    /// <summary>
    /// Every overflow in these laid-out roots, an ellipsis included (the
    /// owner's self-check, Oct 30 2026: a count in the log after a switch, so
    /// a theme file that claims a factor its font does not fit shows there).
    /// Roots not on screen count nothing.
    /// </summary>
    public static int Count(IEnumerable<FrameworkElement> roots) =>
        roots.Where(r => r.IsVisible).Sum(r => Walk(r).Overflows);

    #endregion

    #region The lines

    /// <summary>
    /// Every one-line text that is on screen (no hidden page or collapsed
    /// part around it) and runs past its room: its natural width, in its own
    /// font, against the narrowest box around it up to the root (a trimming
    /// text's room is its own width, and its box must fit the rest). A
    /// wrapping text can't run past an edge; it can only make its page taller,
    /// which the scroll areas catch (the hints, the footer sentence).
    /// </summary>
    public static IEnumerable<Line> LongLines(FrameworkElement root)
    {
        foreach (var text in Descendants(root).OfType<TextBlock>())
        {
            if (text.TextWrapping != TextWrapping.NoWrap || !Shown(text, root) || NaturalWidth(text) is not { } natural) continue;
            var origin = text.TranslatePoint(new Point(0, 0), root).X;
            var around = double.MaxValue;
            for (var up = VisualTreeHelper.GetParent(text); up is not null; up = VisualTreeHelper.GetParent(up))
            {
                if (up is FrameworkElement { ActualWidth: > 0 } box)
                    around = Math.Min(around, box.TranslatePoint(new Point(box.ActualWidth, 0), root).X - origin);
                if (ReferenceEquals(up, root)) break;
            }
            var trims = text.TextTrimming != TextTrimming.None;
            var room = trims ? Math.Min(text.ActualWidth, around) : around;
            if (natural > room + Slack) yield return new Line(TextOf(text), natural, room, trims, text.ActualWidth <= around + Slack);
        }
    }

    /// <summary>A text's one line in its own fonts, run by run; null for what is no single line of runs (a line break, nothing at all).</summary>
    public static double? NaturalWidth(TextBlock text)
    {
        if (text.Inlines.Count == 0) return string.IsNullOrEmpty(text.Text) ? null : Width(text.Text, text);
        if (text.Inlines.Any(i => i is not Run)) return null;
        var width = text.Inlines.Cast<Run>().Sum(r => string.IsNullOrEmpty(r.Text) ? 0 : Width(r.Text, r));
        return width > 0 ? width : null;
    }

    private static string TextOf(TextBlock text) =>
        text.Inlines.Count == 0 ? text.Text : string.Concat(text.Inlines.OfType<Run>().Select(r => r.Text));

    // In layout units (pixelsPerDip 1): the text's own size, whatever scale is above it.
    private static double Width(string s, DependencyObject font) =>
        new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface((FontFamily)font.GetValue(TextElement.FontFamilyProperty), (FontStyle)font.GetValue(TextElement.FontStyleProperty),
                             (FontWeight)font.GetValue(TextElement.FontWeightProperty), (FontStretch)font.GetValue(TextElement.FontStretchProperty)),
                (double)font.GetValue(TextElement.FontSizeProperty), Brushes.White, 1.0)
            .WidthIncludingTrailingWhitespace;

    #endregion

    #region The scroll areas

    /// <summary>
    /// Every marked scroll area on screen whose content is taller than its
    /// viewport by WPF's own test (<see cref="Taller"/>), with no
    /// <see cref="Slack"/>: the ScrollViewer shows its bar at any overflow, so
    /// a page a fraction of a pixel taller than its column already scrolls
    /// (a theme's Skins page, 0.46 px over, showed the bar the half pixel had
    /// let through; Oct 30 2026).
    /// </summary>
    public static IEnumerable<Spill> Spills(FrameworkElement root) =>
        Descendants(root).OfType<ScrollViewer>()
                         .Where(v => GetHoldsWhole(v) && Shown(v, root) && Taller(v.ExtentHeight, v.ViewportHeight))
                         .Select(v => new Spill(v.ExtentHeight, v.ViewportHeight));

    /// <summary>
    /// The ScrollViewer's own test for showing its bar, so the walk says
    /// exactly what WPF draws: WPF's DoubleUtil.GreaterThan (internal to WPF),
    /// greater and not close, close being within a relative epsilon,
    /// (|a| + |b| + 10) × DBL_EPSILON (equal values are close). A content
    /// height that differs from its column's only by floating-point noise
    /// (5.7e-14 px, Default's Skins page) shows no bar, and spills nothing.
    /// </summary>
    private static bool Taller(double extent, double viewport)
    {
        const double DblEpsilon = 2.2204460492503131e-16;
        if (extent <= viewport) return false;
        var epsilon = (Math.Abs(extent) + Math.Abs(viewport) + 10.0) * DblEpsilon;
        return extent - viewport >= epsilon;
    }

    #endregion

    #region The tree

    /// <summary>On screen if nothing around it is hidden or collapsed (a window never shown has no IsVisible to ask).</summary>
    private static bool Shown(DependencyObject element, FrameworkElement root)
    {
        for (var up = element; up is not null; up = VisualTreeHelper.GetParent(up))
        {
            if (up is UIElement { Visibility: not Visibility.Visible }) return false;
            if (ReferenceEquals(up, root)) return true;
        }
        return true;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject d)
    {
        yield return d;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(d, i))) yield return child;
    }

    #endregion
}

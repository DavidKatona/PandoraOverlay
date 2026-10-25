using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace PandoraOverlay;

/// <summary>
/// Names drawn by ONE element straight onto its drawing context — no
/// element per name, no layout per name — and each distinct name PREPARED
/// ONCE: its text formatted (Display mode: snapped to pixels, crisp) and
/// recorded, outline and all, as a frozen drawing, so a zoom only stamps
/// those drawings at new whole-pixel positions. History (Oct 9 2026, the
/// owner found zooming slow in game): nine TextBlocks per name made a zoom
/// step 110–140 ms on the owner's fast CPU, 95% of it the names; drawing
/// each name's text nine times per step was still ~35 ms, because every
/// DrawText runs the text formatter again. The outline is the same text
/// eight times one device pixel around, in near-black, under the light
/// text: crisp, never a blur (a blurred shadow was "blurry and hard to read").
/// Now a zoom step costs 4–6 ms, the same as with names off (measured
/// off-screen on the owner's CPU). Don't go back to an element per name or
/// to DrawText per step, and don't bring a blur effect such as a
/// DropShadowEffect back (owner, Oct 9 2026).
/// <para>
/// The look is the theme's (1.39): the typeface is <c>Theme.Font.Family</c>
/// (Segoe UI in Default, the bundled Montserrat in Isle), the outline
/// <c>Theme.Map.NameOutline</c>; the sizes and the fill colours come from the
/// caller, who reads them from the theme too (MapCanvas.LabelSize /
/// LabelBrush, a friend's name in the friend's colour). A prepared name is
/// kept by font and outline as well, so a name prepared in one theme is
/// never stamped in another; and a theme switch empties the cache
/// (<see cref="ForgetPrepared"/>, MainWindow.ApplyTheme, before any map
/// redraws), so the drawings of a theme no longer on screen don't linger.
/// </para>
/// </summary>
public sealed class NameLayer : FrameworkElement
{
    private static readonly (int X, int Y)[] Steps = { (-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1) };

    /// <summary>Every name prepared so far, by text, size, weight, colour, font, outline and pixel density: a name is prepared once per theme.</summary>
    private static readonly Dictionary<(string Text, double Size, bool Semibold, Brush Brush, ThemeFont Font, ThemeColor Outline, double Dpi), (Drawing Drawing, Size Box)> Prepared = new();

    /// <summary>The theme's family as the two faces a name is drawn in, made once per family.</summary>
    private static (ThemeFont Font, Typeface Regular, Typeface Semibold)? _faces;

    private readonly List<(Drawing Drawing, TranslateTransform At)> _names = new();

    public NameLayer()
    {
        IsHitTestVisible = false;
    }

    /// <summary>
    /// Forgets every prepared name: a theme switch calls it (MainWindow.ApplyTheme)
    /// right after the theme's dictionary is swapped and before any map redraws,
    /// so the next names are prepared afresh in the new theme's font and outline.
    /// </summary>
    public static void ForgetPrepared() => Prepared.Clear();

    /// <summary>How many names are prepared now (for the tests).</summary>
    internal static int PreparedCount => Prepared.Count;

    private static (Typeface Regular, Typeface Semibold) Faces(ThemeFont font)
    {
        if (_faces is not { } kept || kept.Font != font)
        {
            var family = Theme.FontFamilyOf(font.Address);
            kept = (font,
                    new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                    new Typeface(family, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal));
            _faces = kept;
        }
        return (kept.Regular, kept.Semibold);
    }

    /// <summary>
    /// One name as a frozen drawing in its own box (top-left at 0,0): the
    /// outline's pixel all round, the light text one pixel in. Formatted and
    /// recorded the first time it is asked for, then reused.
    /// </summary>
    private static (Drawing Drawing, Size Box) Prepare(string text, double size, bool semibold, Brush brush, double pixelsPerDip)
    {
        var font = Theme.Current.Font.Family;
        var outlineColour = Theme.Current.Map.NameOutline;
        var key = (text, size, semibold, brush, font, outlineColour, pixelsPerDip);
        if (Prepared.TryGetValue(key, out var prepared)) return prepared;

        var (regular, bold) = Faces(font);
        var typeface = semibold ? bold : regular;
        var fill = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, brush,
                                     null, TextFormattingMode.Display, pixelsPerDip);
        var outline = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, outlineColour.ToBrush(),
                                        null, TextFormattingMode.Display, pixelsPerDip);
        var step = 1 / pixelsPerDip; // one device pixel
        var group = new DrawingGroup();
        using (var context = group.Open())
        {
            foreach (var (dx, dy) in Steps) context.DrawText(outline, new Point(step + dx * step, step + dy * step));
            context.DrawText(fill, new Point(step, step));
        }
        group.Freeze();
        prepared = (group, new Size(fill.WidthIncludingTrailingWhitespace + 2 * step, fill.Height + 2 * step));
        Prepared[key] = prepared;
        return prepared;
    }

    /// <summary>The box a name takes, its outline included: what LabelLayout places.</summary>
    public static Size Measure(string text, double size, bool semibold, Brush brush, double pixelsPerDip) =>
        Prepare(text, size, semibold, brush, pixelsPerDip).Box;

    /// <summary>Replaces the names drawn: each its text, size, weight, colour and the top-left of its box (as Measure gave it).</summary>
    public void Show(IEnumerable<(string Text, double Size, bool Semibold, Brush Brush, Point At)> names)
    {
        _names.Clear();
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var n in names)
        {
            // On whole device pixels, so the recorded text lands exactly on the pixel grid.
            var at = new TranslateTransform(Math.Round(n.At.X * dpi) / dpi, Math.Round(n.At.Y * dpi) / dpi);
            at.Freeze();
            _names.Add((Prepare(n.Text, n.Size, n.Semibold, n.Brush, dpi).Drawing, at));
        }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        foreach (var (drawing, at) in _names)
        {
            dc.PushTransform(at);
            dc.DrawDrawing(drawing);
            dc.Pop();
        }
    }
}

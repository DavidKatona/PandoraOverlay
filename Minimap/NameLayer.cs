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
/// </summary>
public sealed class NameLayer : FrameworkElement
{
    public const double AreaFontSize = 13;
    public const double NameFontSize = 12;

    private static readonly FontFamily Font = new("Segoe UI");
    private static readonly Typeface Regular = new(Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface Semibold = new(Font, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    public static readonly Brush AreaBrush = Frozen(Color.FromRgb(0xF2, 0xF5, 0xF8));
    public static readonly Brush NameBrush = Frozen(Color.FromRgb(0xE2, 0xE8, 0xEE));
    private static readonly Brush Outline = Frozen(Color.FromArgb(0xE6, 0x06, 0x0A, 0x10));
    private static readonly (int X, int Y)[] Steps = { (-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1) };

    /// <summary>Every name prepared so far, by text, size, weight, colour and pixel density: a name is prepared once per session.</summary>
    private static readonly Dictionary<(string Text, double Size, bool Semibold, Brush Brush, double Dpi), (Drawing Drawing, Size Box)> Prepared = new();

    private readonly List<(Drawing Drawing, TranslateTransform At)> _names = new();

    public NameLayer()
    {
        IsHitTestVisible = false;
    }

    private static Brush Frozen(Color colour)
    {
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// One name as a frozen drawing in its own box (top-left at 0,0): the
    /// outline's pixel all round, the light text one pixel in. Formatted and
    /// recorded the first time it is asked for, then reused.
    /// </summary>
    private static (Drawing Drawing, Size Box) Prepare(string text, double size, bool semibold, Brush brush, double pixelsPerDip)
    {
        var key = (text, size, semibold, brush, pixelsPerDip);
        if (Prepared.TryGetValue(key, out var prepared)) return prepared;

        var typeface = semibold ? Semibold : Regular;
        var fill = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, brush,
                                     null, TextFormattingMode.Display, pixelsPerDip);
        var outline = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, Outline,
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

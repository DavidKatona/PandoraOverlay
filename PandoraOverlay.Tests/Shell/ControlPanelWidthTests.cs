using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The control panel is the one window without a fixed frame: it is as wide
/// as its one row (or its hint line). A theme must never make it wider, or a
/// saved position at the screen's edge would push it off (PLAN.md section 12,
/// question 17): Isle's 11.5 px buttons exist for exactly this. The row is
/// measured as ControlPanelWindow.xaml and Controls.xaml lay it out: each
/// button its label plus its padding (12 a side) and its margin (6, none on a
/// group's last), each group as wide as its buttons or its caption (9 px, a
/// 1 px margin), the separators 1 px with 10 a side, then the panel's own
/// padding (10 a side) and border (1). Text alone would pass while the panel
/// grew (REVIEW.md, risks).
/// </summary>
public class ControlPanelWidthTests
{
    private const double ButtonPadding = 2 * 12;
    private const double ButtonMargin = 6;
    private const double Separator = 1 + 2 * 10;
    private const double CaptionMargin = 1;
    private const double HintMargin = 2;
    private const double Frame = 2 * 10 + 2 * 1;

    /// <summary>The row as the XAML has it: a lone Settings button, then each group's caption over its buttons, then Lock and Exit.</summary>
    private static readonly (string? Caption, string[] Buttons)[] Row =
    {
        (null, new[] { "Settings…" }),
        ("STATS", new[] { "Show/hide", "View" }),
        ("MINIMAP", new[] { "Show/hide", "Map view", "Heatmap", "Areas" }),
        ("PRIME", new[] { "Show/hide", "Check" }),
        ("ACTIVITY", new[] { "Show/hide" }),
        (null, new[] { "Lock", "Exit" }),
    };

    /// <summary>The hint with the default edit-mode key (SetHotkeyLabel).</summary>
    private static readonly string Hint = $"drag panels to move · right-click map = waypoints · {OverlayConfig.DefaultHotkey} locks";

    private static double Measure(string text, ThemeFont font, ThemeWeight weight, double size) =>
        new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(Theme.FontFamilyOf(font.Address), FontStyles.Normal, weight.ToFontWeight(), FontStretches.Normal),
                size, Brushes.White, 1.0)
            .WidthIncludingTrailingWhitespace;

    /// <summary>The panel's width in a theme, its glow painted inside (it adds nothing).</summary>
    internal static double PanelWidth(ThemeTokens t)
    {
        var f = t.Font;
        var row = 0.0;
        for (var g = 0; g < Row.Length; g++)
        {
            var (caption, buttons) = Row[g];
            var group = buttons.Sum(b => Measure(b, f.Family, f.BodyWeight, f.ControlPanelButtonSize) + ButtonPadding) + ButtonMargin * (buttons.Length - 1);
            if (caption is not null) group = Math.Max(group, CaptionMargin + Measure(caption, f.Family, f.CaptionWeight, 9));
            row += group + (g > 0 ? Separator : 0);
        }
        var hint = HintMargin + Measure(Hint, f.Family, f.BodyWeight, 10);
        return Math.Max(row, hint) + Frame;
    }

    public static IEnumerable<object[]> EveryTheme() => Theme.All.Select(e => new object[] { e.Id });

    /// <summary>No theme's control panel is wider than Default's, the width players' layouts were made with.</summary>
    [Theory]
    [MemberData(nameof(EveryTheme))]
    public void NoThemeWidensTheControlPanel(string id)
    {
        var theme = Theme.All.Single(e => e.Id == id).Tokens;
        var mine = PanelWidth(theme);
        var defaults = PanelWidth(DefaultTheme.Tokens);
        Assert.True(mine <= defaults, $"{id}: {mine:0.0} px against Default's {defaults:0.0}");
    }

    /// <summary>The measure counts the layout, not just the text: Default's panel comes out at the 958 px the golden renders' windows mode draws it (panel-01, 1×; a pixel for its rounding up).</summary>
    [Fact]
    public void TheMeasureIsThePanelsWidth() =>
        Assert.InRange(PanelWidth(DefaultTheme.Tokens), 957, 958);
}

using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The control panel's fixed frame (ControlPanelFrame, 1.39): the owner saw
/// Isle's compact panel and wanted that size for every theme (Oct 25 2026),
/// so its parts have fixed sizes and every label is trimmed inside its part.
/// Two things to hold: the frame's numbers add up, with the gaps the XAML
/// puts between the parts (ControlPanelWindow.xaml and Controls.xaml), to the
/// panel's outer size; and every theme's labels, captions and hint fit their
/// parts in its own font, so nothing is trimmed in a shipped theme (a theme
/// whose font no longer fits fails here, and the owner hears it).
/// </summary>
public class ControlPanelWidthTests
{
    // The gaps, as the XAML has them.
    private const double Border = 1;
    private const double PaddingX = 10, PaddingY = 8;   // RootPanel's Padding="10,8"
    private const double ButtonGap = 6;                 // PanelButton's Margin, none on a group's last
    private const double Separator = 1 + 2 * 10;        // PanelSeparator: 1 wide, 10 a side
    private const double CaptionMargin = 1;             // PanelCaption's Margin="1,0,0,3"
    private const double CaptionGap = 3;
    private const double HintMargin = 2, HintGap = 5;   // HintText's Margin="2,5,0,0"
    private const double LabelPadding = 6;              // PanelButton's Padding="6,0": how close a label may come to the edges

    /// <summary>The row as the XAML has it: a lone Settings button, then each group's caption over its buttons, then Lock and Exit.</summary>
    private static readonly (string? Caption, (string Label, double Width)[] Buttons)[] Row =
    {
        (null, new[] { ("Settings…", ControlPanelFrame.SettingsButton) }),
        ("STATS", new[] { ("Show/hide", ControlPanelFrame.ShowHideButton), ("View", ControlPanelFrame.ViewButton) }),
        ("MINIMAP", new[]
        {
            ("Show/hide", ControlPanelFrame.ShowHideButton), ("Map view", ControlPanelFrame.MapViewButton),
            ("Heatmap", ControlPanelFrame.HeatmapButton), ("Areas", ControlPanelFrame.AreasButton),
        }),
        ("PRIME", new[] { ("Show/hide", ControlPanelFrame.ShowHideButton), ("Check", ControlPanelFrame.CheckButton) }),
        ("ACTIVITY", new[] { ("Show/hide", ControlPanelFrame.ShowHideButton) }),
        (null, new[] { ("Lock", ControlPanelFrame.LockButton), ("Exit", ControlPanelFrame.ExitButton) }),
    };

    /// <summary>The longest hint SetHotkeyLabel can write: a two-modifier F-key.</summary>
    private const string Hint = "drag panels to move · right-click map = waypoints · Ctrl+Shift+F12 locks";

    private static FormattedText Text(string text, ThemeFont font, ThemeWeight weight, double size) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(Theme.FontFamilyOf(font.Address), FontStyles.Normal, weight.ToFontWeight(), FontStretches.Normal),
            size, Brushes.White, 1.0);

    private static double GroupWidth((string Label, double Width)[] buttons) =>
        buttons.Sum(b => b.Width) + ButtonGap * (buttons.Length - 1);

    /// <summary>The row's buttons, gaps and separators, the padding and the border are the frame's width; nothing is left over or short.</summary>
    [Fact]
    public void TheFramesWidthIsItsRow()
    {
        var row = Row.Sum(g => GroupWidth(g.Buttons)) + Separator * (Row.Length - 1);
        Assert.Equal(ControlPanelFrame.Width, row + 2 * (PaddingX + Border));
    }

    /// <summary>A caption, the buttons and the hint, with their gaps, the padding and the border, are the frame's height.</summary>
    [Fact]
    public void TheFramesHeightIsItsRows()
    {
        var rows = ControlPanelFrame.CaptionHeight + CaptionGap + ControlPanelFrame.ButtonHeight + HintGap + ControlPanelFrame.HintHeight;
        Assert.Equal(ControlPanelFrame.Height, rows + 2 * (PaddingY + Border));
    }

    /// <summary>The compact size the owner chose (Isle's panel at 100%, step 3's render, Oct 25 2026).</summary>
    [Fact]
    public void TheFrameIsTheCompactPanel() =>
        Assert.Equal((940.0, 74.0), (ControlPanelFrame.Width, ControlPanelFrame.Height));

    public static IEnumerable<object[]> EveryTheme() => Theme.All.Select(e => new object[] { e.Id });

    /// <summary>
    /// Every theme's labels fit their buttons (within the label padding a
    /// side) and their line in the button's height; every caption fits its
    /// group and its row; the longest hint fits the panel's inside and its
    /// row. A label that doesn't would be trimmed with an ellipsis, in a
    /// panel that never grows.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryTheme))]
    public void EveryThemesTextFitsTheFrame(string id)
    {
        var f = Theme.All.Single(e => e.Id == id).Tokens.Font;
        var problems = new List<string>();
        foreach (var (caption, buttons) in Row)
        {
            foreach (var (label, width) in buttons)
            {
                var t = Text(label, f.Family, f.BodyWeight, f.ControlPanelButtonSize);
                if (t.WidthIncludingTrailingWhitespace > width - 2 * LabelPadding)
                    problems.Add($"\"{label}\" {t.WidthIncludingTrailingWhitespace:0.0} px in a {width} px button");
                if (t.Height > ControlPanelFrame.ButtonHeight)
                    problems.Add($"\"{label}\" {t.Height:0.0} px tall in a {ControlPanelFrame.ButtonHeight} px button");
            }
            if (caption is null) continue;
            var c = Text(caption, f.Family, f.CaptionWeight, 9);
            if (CaptionMargin + c.WidthIncludingTrailingWhitespace > GroupWidth(buttons))
                problems.Add($"\"{caption}\" {c.WidthIncludingTrailingWhitespace:0.0} px over a {GroupWidth(buttons)} px group");
            if (c.Height > ControlPanelFrame.CaptionHeight)
                problems.Add($"\"{caption}\" {c.Height:0.0} px tall in a {ControlPanelFrame.CaptionHeight} px row");
        }
        var hint = Text(Hint, f.Family, f.BodyWeight, 10);
        var inside = ControlPanelFrame.Width - 2 * (PaddingX + Border) - HintMargin;
        if (hint.WidthIncludingTrailingWhitespace > inside)
            problems.Add($"the hint {hint.WidthIncludingTrailingWhitespace:0.0} px in {inside}");
        if (hint.Height > ControlPanelFrame.HintHeight)
            problems.Add($"the hint {hint.Height:0.0} px tall in a {ControlPanelFrame.HintHeight} px row");
        Assert.True(problems.Count == 0, $"{id}: " + string.Join("; ", problems));
    }
}

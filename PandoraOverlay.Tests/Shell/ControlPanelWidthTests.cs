using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The control panel's fixed frame (ControlPanelFrame, 1.39): the owner saw
/// Isle's compact panel and wanted that size for every theme (Oct 25 2026),
/// so its parts have fixed sizes and every label is trimmed inside its part.
/// Two things to hold: the frame's numbers add up, with the gaps the XAML
/// puts between the parts (ControlPanelWindow.xaml and Controls.xaml, kept as
/// the frame contract's known insets), to the panel's outer size; and every
/// theme's labels, captions and hint fit their parts in its own font at its
/// fit factor, so nothing is trimmed in a shipped theme. The row, its gaps
/// and the strings are the app's frame contract since the fit factor's
/// release (Themes/Fit/FrameContract.cs), measured here and at every
/// candidate factor by FitFactorTests; a line within half a pixel of its row
/// is drawn whole (the Nightshade attempt's captions came to 12.28 px in
/// their 12 px row, empty descender room under capitals, and failed here for
/// nothing the owner could see).
/// </summary>
public class ControlPanelWidthTests
{
    private static double GroupWidth((string Label, double Width)[] buttons) => FrameContract.PanelGroupWidth(buttons);

    /// <summary>The row's buttons, gaps and separators, the padding and the border are the frame's width; nothing is left over or short.</summary>
    [Fact]
    public void TheFramesWidthIsItsRow()
    {
        var row = FrameContract.PanelRow.Sum(g => GroupWidth(g.Buttons)) + FrameContract.PanelSeparator * (FrameContract.PanelRow.Count - 1)
                  + ControlPanelFrame.SessionGap;
        Assert.Equal(ControlPanelFrame.Width, row + 2 * (FrameContract.PanelPaddingX + FrameContract.PanelBorder));
    }

    /// <summary>A caption, the buttons and the hint, with their gaps, the padding and the border, are the frame's height.</summary>
    [Fact]
    public void TheFramesHeightIsItsRows()
    {
        var rows = ControlPanelFrame.CaptionHeight + FrameContract.PanelCaptionGap + ControlPanelFrame.ButtonHeight
                   + FrameContract.PanelHintGap + ControlPanelFrame.HintHeight;
        Assert.Equal(ControlPanelFrame.Height, rows + 2 * (FrameContract.PanelPaddingY + FrameContract.PanelBorder));
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
        var tokens = Theme.All.Single(e => e.Id == id).Tokens;
        var problems = FrameContract.Misses(tokens).Where(m => m.Where == FrameContract.ControlPanel).Select(m => m.ToString()).ToList();
        Assert.True(problems.Count == 0, $"{id}: " + string.Join("; ", problems));
    }

    /// <summary>
    /// The Pack's room in the row (Nov 2026): Map view, Heatmap and Areas gave
    /// way to one Layers button, the PACK group took its Show/hide and a
    /// separator, and what was left is the gap before Lock and Exit.
    /// </summary>
    [Fact]
    public void ThePacksGroupTookTheRoomTheLayersMenuFreed()
    {
        const double mapView = 72, heatmap = 72, areas = 52;
        var freed = mapView + heatmap + areas + 2 * FrameContract.PanelButtonGap - ControlPanelFrame.LayersButton;
        var pack = ControlPanelFrame.ShowHideButton + FrameContract.PanelSeparator;
        Assert.Equal(152, freed);
        Assert.Equal(99, pack);
        Assert.Equal(ControlPanelFrame.SessionGap, freed - pack);
        Assert.Contains(FrameContract.PanelRow, g => g.Caption == "PACK");
    }

    /// <summary>Every theme's Layers menu captions, rows and hotkeys fit the menu's width.</summary>
    [Theory]
    [MemberData(nameof(EveryTheme))]
    public void EveryThemesLayersMenuFits(string id)
    {
        var tokens = Theme.All.Single(e => e.Id == id).Tokens;
        var problems = FrameContract.Misses(tokens).Where(m => m.Where == FrameContract.LayersMenu).Select(m => m.ToString()).ToList();
        Assert.True(problems.Count == 0, $"{id}: " + string.Join("; ", problems));
    }

    /// <summary>Every theme's Pack strings fit their room in the Pack's rows (its header is with the widgets' headers).</summary>
    [Theory]
    [MemberData(nameof(EveryTheme))]
    public void EveryThemesPackStringsFit(string id)
    {
        var tokens = Theme.All.Single(e => e.Id == id).Tokens;
        var problems = FrameContract.Misses(tokens).Where(m => m.Where is FrameContract.PackRowsWhere or FrameContract.Headers)
                                                   .Select(m => m.ToString()).ToList();
        Assert.True(problems.Count == 0, $"{id}: " + string.Join("; ", problems));
    }

    /// <summary>The half pixel of slack is the walk's (FitProbe.Slack), and every part of the row is in the contract.</summary>
    [Fact]
    public void TheContractHoldsEveryPartOfTheRow()
    {
        var panel = FrameContract.Slots.Where(s => s.Where == FrameContract.ControlPanel).ToList();
        var labels = FrameContract.PanelRow.Sum(g => g.Buttons.Length);
        var captions = FrameContract.PanelRow.Count(g => g.Caption is not null);
        Assert.Equal(labels + captions + 1, panel.Count); // and the hint
        Assert.Equal(0.5, FitProbe.Slack);
    }
}

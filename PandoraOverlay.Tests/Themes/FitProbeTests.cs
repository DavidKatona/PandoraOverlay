using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The walk itself (Themes/Fit/FitProbe.cs, the fit factor's one
/// implementation), on layouts built here: a line past its room is found,
/// cut off unless it trims inside its box; a line within half a pixel of it
/// is drawn whole; a hidden part is not on screen; a marked scroll area that
/// can't hold its content spills, by any amount (no slack: WPF shows its bar),
/// and an unmarked one scrolls by design. The
/// dialogs it walks for every theme are DialogLayoutTests' and FitFactorTests'.
/// </summary>
public class FitProbeTests
{
    private const string Long = "A line far longer than the box it has to sit in";

    /// <summary>A box of the given width holding one text, laid out.</summary>
    private static (Border Root, TextBlock Text) Box(double width, string text, TextTrimming trimming = TextTrimming.None)
    {
        var block = new TextBlock { Text = text, FontSize = 12, TextTrimming = trimming };
        var root = new Border { Width = width, Child = block };
        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        root.Arrange(new Rect(root.DesiredSize));
        root.UpdateLayout();
        return (root, block);
    }

    [Fact]
    public void ALineThatFitsIsNoOverflow() => TheThemeOnScreen.OnSta(() =>
    {
        var (root, _) = Box(400, "Short");
        var report = FitProbe.Walk(root);
        Assert.Empty(report.Lines);
        Assert.True(report.Fits);
        Assert.Equal(0, report.Overflows);
    });

    [Fact]
    public void ALineCutOffWithoutAnEllipsisIsFound() => TheThemeOnScreen.OnSta(() =>
    {
        var (root, text) = Box(60, Long);
        var line = Assert.Single(FitProbe.LongLines(root));
        Assert.Equal(Long, line.Text);
        Assert.Equal(FitProbe.NaturalWidth(text), line.Natural);
        Assert.Equal(60, line.Room, 3);
        Assert.False(line.Trims);
        Assert.True(line.CutOff);
        Assert.False(FitProbe.Walk(root).Fits);
    });

    [Fact]
    public void ALineThatTrimsIsAnOverflowButNotCutOff() => TheThemeOnScreen.OnSta(() =>
    {
        var (root, _) = Box(60, Long, TextTrimming.CharacterEllipsis);
        var line = Assert.Single(FitProbe.LongLines(root));
        Assert.True(line.Trims);
        Assert.True(line.BoxFits);
        Assert.False(line.CutOff);
        var report = FitProbe.Walk(root);
        Assert.True(report.Fits); // the framework allows an ellipsis
        Assert.Equal(1, report.Overflows); // the self-check counts it
    });

    [Fact]
    public void HalfAPixelIsDrawnWhole() => TheThemeOnScreen.OnSta(() =>
    {
        var natural = FitProbe.NaturalWidth(Box(400, "Stamina").Text)!.Value;
        Assert.Empty(FitProbe.LongLines(Box(natural - FitProbe.Slack + 0.01, "Stamina").Root));
        Assert.Single(FitProbe.LongLines(Box(natural - FitProbe.Slack - 0.01, "Stamina").Root));
    });

    [Fact]
    public void AHiddenLineIsNotOnScreen() => TheThemeOnScreen.OnSta(() =>
    {
        var (root, text) = Box(60, Long);
        text.Visibility = Visibility.Hidden;
        Assert.Empty(FitProbe.LongLines(root));
    });

    [Fact]
    public void OnlyAMarkedScrollAreaSpills() => TheThemeOnScreen.OnSta(() =>
    {
        ScrollViewer Viewer(bool marked)
        {
            var viewer = new ScrollViewer { Height = 50, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new Border { Height = 200 } };
            if (marked) FitProbe.SetHoldsWhole(viewer, true);
            viewer.Measure(new Size(100, 50));
            viewer.Arrange(new Rect(0, 0, 100, 50));
            viewer.UpdateLayout();
            return viewer;
        }
        Assert.Empty(FitProbe.Spills(Viewer(marked: false))); // a list scrolls by design
        var spill = Assert.Single(FitProbe.Spills(Viewer(marked: true)));
        Assert.Equal(200, spill.Extent, 3);
        Assert.Equal(50, spill.Viewport, 3);
    });

    /// <summary>
    /// A scroll area has no slack: content taller than its viewport by less
    /// than the half pixel a line gets already spills, because WPF shows the
    /// bar for it (a page 0.46 px over its column showed one, Oct 30 2026).
    /// Content taller only by floating-point noise does not: the walk uses the
    /// ScrollViewer's own comparison, and WPF shows no bar for it either. At a
    /// column of the Settings dialog's size (456 px) WPF's epsilon is about
    /// 2.0e-13 px, so 1e-13 over is noise (Default's Skins page is 5.7e-14 over).
    /// </summary>
    [Fact]
    public void AScrollAreaSpillsAtAnyOverflowWpfWouldShow() => TheThemeOnScreen.OnSta(() =>
    {
        const double Column = 456;
        ScrollViewer Marked(double content)
        {
            var viewer = new ScrollViewer { Height = Column, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new Border { Height = content } };
            FitProbe.SetHoldsWhole(viewer, true);
            viewer.Measure(new Size(100, Column));
            viewer.Arrange(new Rect(0, 0, 100, Column));
            viewer.UpdateLayout();
            return viewer;
        }
        var over = Marked(Column + 0.2); // inside the slack a line would get
        Assert.True(0.2 < FitProbe.Slack);
        Assert.Equal(Visibility.Visible, over.ComputedVerticalScrollBarVisibility); // WPF shows the bar
        var spill = Assert.Single(FitProbe.Spills(over));
        Assert.Equal(Column + 0.2, spill.Extent, 3);

        var noise = Marked(Column + 1e-13);
        Assert.True(noise.ExtentHeight > noise.ViewportHeight); // taller, as a double
        Assert.Equal(Visibility.Collapsed, noise.ComputedVerticalScrollBarVisibility); // but WPF shows no bar
        Assert.Empty(FitProbe.Spills(noise));

        var level = Marked(Column);
        Assert.Equal(Visibility.Collapsed, level.ComputedVerticalScrollBarVisibility);
        Assert.Empty(FitProbe.Spills(level));
    });
}

using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The walk itself (Themes/Fit/FitProbe.cs, the fit factor's one
/// implementation), on layouts built here: a line past its room is found,
/// cut off unless it trims inside its box; a line within half a pixel of it
/// is drawn whole; a hidden part is not on screen; a marked scroll area that
/// can't hold its content spills, an unmarked one scrolls by design. The
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
}

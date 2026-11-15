using System.Windows;
using PandoraOverlay.Games;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The Games window's fixed frame (A11, <see cref="GamesFrame"/>): its sums
/// against the Contract's sizes, which a game lays its screens out in, and the
/// one scale it is drawn at on a small screen.
/// </summary>
public class GamesFrameTests
{
    [Fact]
    public void TheViewAreaIsTheContractsRoom()
    {
        Assert.Equal(GamesFrame.Width - 2, GamesFrame.ViewWidth);
        Assert.Equal(GamesFrame.Height - 2 - GamesFrame.HeaderHeight, GamesFrame.ViewHeight);
        Assert.Equal(GameFrame.ViewWidth, GamesFrame.ViewWidth);
        Assert.Equal(GameFrame.ViewHeight, GamesFrame.ViewHeight);
        Assert.Equal((798.0, 510.0), (GamesFrame.ViewWidth, GamesFrame.ViewHeight));
    }

    [Fact]
    public void TheTilesArtBoxIsTheContractsTileArt()
    {
        Assert.Equal(GamesFrame.TileWidth - 12, GamesFrame.TileArtWidth);
        Assert.Equal(GameFrame.TileArtWidth, GamesFrame.TileArtWidth);
        Assert.Equal(GameFrame.TileArtHeight, GamesFrame.TileArtHeight);
        Assert.Equal(96, GamesFrame.TileArtHeight);
    }

    /// <summary>Three tiles, two gaps, the padding both sides and the scrollbar's room fill the view area's width exactly (G1).</summary>
    [Fact]
    public void ThreeTilesAndTheScrollBarsRoomFillTheWidth()
    {
        Assert.Equal(3, GamesFrame.Columns);
        Assert.Equal(GamesFrame.ViewWidth,
            2 * GamesFrame.GridSidePadding + GamesFrame.Columns * GamesFrame.TileWidth + (GamesFrame.Columns - 1) * GamesFrame.TileGap + GamesFrame.ScrollBarRoom);
        // The tiles' panel (each tile's gap at its right) fits before the scrollbar's room.
        Assert.True(GamesFrame.GridSidePadding + GamesFrame.GridPanelWidth <= GamesFrame.ViewWidth - GamesFrame.ScrollBarRoom);
    }

    /// <summary>Two rows of tiles fit the view area, a third does not: the scrollbar comes with the seventh game (G1).</summary>
    [Fact]
    public void TwoRowsFitAndTheThirdScrolls()
    {
        double Rows(int n) => GamesFrame.GridTopPadding + n * (GamesFrame.TileHeight + GamesFrame.TileGap);
        Assert.True(Rows(2) <= GamesFrame.ViewHeight);
        Assert.True(Rows(3) > GamesFrame.ViewHeight);
    }

    // ---- The header's sound group and the title's room (A82, A83) -----------------------

    /// <summary>The group's sizes and their sum: three 22 buttons, five pips 4 wide and 2 apart rising 4 to 12, 4 between each part: 106.</summary>
    [Fact]
    public void TheSoundGroupsSizesSum()
    {
        Assert.Equal(GamesSound.Steps, GamesFrame.SoundPips); // one pip per step
        Assert.Equal(new double[] { 4, 6, 8, 10, 12 }, Enumerable.Range(0, GamesFrame.SoundPips).Select(GamesFrame.SoundPipHeight));
        Assert.Equal(GamesFrame.SoundPipsHeight, GamesFrame.SoundPipHeight(GamesFrame.SoundPips - 1));
        Assert.Equal(GamesFrame.SoundPipWidth / 4, GamesFrame.SoundPipMaxCorner); // rising bars in every theme (A89)
        Assert.Equal(GamesFrame.SoundPips * GamesFrame.SoundPipWidth + (GamesFrame.SoundPips - 1) * GamesFrame.SoundPipGap, GamesFrame.SoundPipsWidth);
        Assert.Equal(28, GamesFrame.SoundPipsWidth);
        Assert.Equal(3 * GamesFrame.SoundButtonSize + GamesFrame.SoundPipsWidth + 3 * GamesFrame.SoundGroupGap, GamesFrame.SoundGroupWidth);
        Assert.Equal(106, GamesFrame.SoundGroupWidth);
        Assert.Equal((22.0, 4.0, 14.0), (GamesFrame.SoundButtonSize, GamesFrame.SoundGroupGap, GamesFrame.SoundGroupSpace));

        // In the header's height: the buttons with its bottom line, the pips' box inside a button's height.
        Assert.True(GamesFrame.SoundButtonSize < GamesFrame.HeaderHeight - 1);
        Assert.True(GamesFrame.SoundPipsHeight <= GamesFrame.SoundButtonSize);

        // The XAML's spacing is these numbers.
        Assert.Equal(new Thickness(GamesFrame.SoundGroupGap, 0, 0, 0), GamesFrame.SoundGapBefore);
        Assert.Equal(new Thickness(GamesFrame.SoundPipGap, 0, 0, 0), GamesFrame.SoundPipGapBefore);
        Assert.Equal(new Thickness(0, 0, GamesFrame.SoundGroupSpace, 0), GamesFrame.SoundGroupMargin);
        Assert.Equal(new Thickness(GamesFrame.HeaderPaddingLeft, 0, GamesFrame.HeaderPaddingRight, 0), GamesFrame.HeaderPadding);
    }

    /// <summary>
    /// The title's room and the rest of the header within its 778 (A83): the
    /// rest as measured at its widest in every bundled theme (Isle's
    /// Montserrat, 571.43 with every part's margin: GamesFrame.TitleMaxWidth's
    /// comment), and every part that is no text. The texts themselves are held in
    /// every theme at its own fit factor by GamesLayoutTests, with a title
    /// longer than its room.
    /// </summary>
    [Fact]
    public void TheTitlesRoomAndTheRestFitTheHeader()
    {
        const double widestRest = 571.43;
        // What is no text: the back control's 10 after it, the subtitle's 10 before
        // it, the watch note's 14 after it, the group and its 14, the hint's 10, ✕'s 22.
        const double fixedParts = 10 + 10 + 14 + GamesFrame.SoundGroupWidth + GamesFrame.SoundGroupSpace + 10 + 22;

        Assert.Equal(778, GamesFrame.HeaderInnerWidth);
        Assert.Equal(GamesFrame.Width - 2 - GamesFrame.HeaderPaddingLeft - GamesFrame.HeaderPaddingRight, GamesFrame.HeaderInnerWidth);
        Assert.True(GamesFrame.TitleMaxWidth + widestRest <= GamesFrame.HeaderInnerWidth);
        Assert.True(GamesFrame.TitleMaxWidth + fixedParts <= GamesFrame.HeaderInnerWidth);
        Assert.Equal(0, GamesFrame.TitleMaxWidth % 10); // rounded down to a whole 10
        Assert.True(GamesFrame.HeaderInnerWidth - widestRest - GamesFrame.TitleMaxWidth < 10, "the title's room is what is left, rounded down to a whole 10, not less");
    }

    [Fact]
    public void TheTilesButtonFitsTheTile()
    {
        Assert.True(GamesFrame.TileButtonWidth < GamesFrame.TileWidth - 10);
        Assert.True(GamesFrame.TileButtonRowHeight < GamesFrame.TileHeight - GamesFrame.TileArtHeight);
    }

    [Theory]
    [InlineData(1920, 1080, 1.0)]
    [InlineData(1280, 720, 1.0)]
    [InlineData(1024, 576, 528.0 / 552)]  // the height limits it
    [InlineData(911, 512, 464.0 / 552)]   // 1366 x 768 at 150%
    public void FitScaleDrawsTheWholeWindowSmallerOnlyWhenItMust(double width, double height, double expected)
    {
        var scale = GamesFrame.FitScale(new Size(width, height), 1.0);

        Assert.Equal(expected, scale, 10);
        Assert.True(scale <= 1);
        Assert.True(GamesFrame.Width * scale <= width - 2 * GamesFrame.ScreenMargin + 1e-9);
        Assert.True(GamesFrame.Height * scale <= height - 2 * GamesFrame.ScreenMargin + 1e-9);
    }

    [Fact]
    public void AtTheDefaultScaleABigScreenNeverDrawsItLarger() =>
        Assert.Equal(1.0, GamesFrame.FitScale(new Size(7680, 4320), 1.0));

    // ---- The player's scale (GamesScale, A75) -----------------------------------------

    /// <summary>The chosen scale is what is drawn where it fits: 200% on a 4K work area, 150% on 1440p.</summary>
    [Theory]
    [InlineData(3840, 2160, 2.0, 2.0)]
    [InlineData(7680, 4320, 2.0, 2.0)]
    [InlineData(2560, 1440, 1.5, 1.5)]
    [InlineData(1920, 1080, 1.25, 1.25)]
    [InlineData(1920, 1080, 0.75, 0.75)]
    public void TheChosenScaleIsDrawnWhereItFits(double width, double height, double chosen, double expected) =>
        Assert.Equal(expected, GamesFrame.FitScale(new Size(width, height), chosen), 10);

    /// <summary>
    /// Never larger than the monitor's work area less the margin: 200% on a
    /// 1920 x 1080 work area (a 1200 x 828 frame would fit, 1600 x 1104 not) is
    /// the largest that fits, the height's (1032 / 552).
    /// </summary>
    [Theory]
    [InlineData(1920, 1080, 2.0, 1032.0 / 552)]
    [InlineData(1920, 1040, 2.0, 992.0 / 552)]   // a taskbar off the height
    [InlineData(1280, 720, 1.5, 672.0 / 552)]
    [InlineData(911, 512, 2.0, 464.0 / 552)]     // 1366 x 768 at 150%: as at 100%
    public void AChosenScaleTooLargeForTheScreenIsClampedToTheFit(double width, double height, double chosen, double expected)
    {
        var scale = GamesFrame.FitScale(new Size(width, height), chosen);

        Assert.Equal(expected, scale, 10);
        Assert.True(scale < chosen);
        Assert.True(GamesFrame.Width * scale <= width - 2 * GamesFrame.ScreenMargin + 1e-9);
        Assert.True(GamesFrame.Height * scale <= height - 2 * GamesFrame.ScreenMargin + 1e-9);
    }

    /// <summary>A small screen draws the window as it always did, whatever is chosen above 1; a chosen 0.75 there is the smaller of the two.</summary>
    [Theory]
    [InlineData(1024, 576)]
    [InlineData(911, 512)]
    public void BelowOneTheScreensFitIsAsBefore(double width, double height)
    {
        var screen = new Size(width, height);
        var fit = Math.Min((width - 2 * GamesFrame.ScreenMargin) / GamesFrame.Width, (height - 2 * GamesFrame.ScreenMargin) / GamesFrame.Height);

        Assert.Equal(fit, GamesFrame.FitScale(screen, 1.0), 10);
        Assert.Equal(fit, GamesFrame.FitScale(screen, 2.0), 10);
        Assert.Equal(Math.Min(fit, 0.75), GamesFrame.FitScale(screen, 0.75), 10);
    }

    /// <summary>A hand-edited config.json can hold anything: it is held to 75%..200%, and a value that is not a number reads as 100%.</summary>
    [Theory]
    [InlineData(5.0, 2.0)]
    [InlineData(0.1, 0.75)]
    [InlineData(-3.0, 0.75)]
    [InlineData(double.NaN, 1.0)]
    [InlineData(double.PositiveInfinity, 1.0)]
    public void AChosenScaleIsHeldToTheSlidersRange(double chosen, double expected) =>
        Assert.Equal(expected, GamesFrame.FitScale(new Size(7680, 4320), chosen), 10);

    [Fact]
    public void TheRangeIsTheWidgetsFloorUpToTwoHundredPercent() =>
        Assert.Equal((0.75, 2.0), (GamesFrame.MinScale, GamesFrame.MaxScale));
}

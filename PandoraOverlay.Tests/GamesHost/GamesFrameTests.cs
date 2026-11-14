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
        var scale = GamesFrame.FitScale(new Size(width, height));

        Assert.Equal(expected, scale, 10);
        Assert.True(scale <= 1);
        Assert.True(GamesFrame.Width * scale <= width - 2 * GamesFrame.ScreenMargin + 1e-9);
        Assert.True(GamesFrame.Height * scale <= height - 2 * GamesFrame.ScreenMargin + 1e-9);
    }

    [Fact]
    public void ABigScreenNeverDrawsItLarger() =>
        Assert.Equal(1.0, GamesFrame.FitScale(new Size(7680, 4320)));
}

using System.Windows;
using Xunit;

namespace PandoraOverlay.Tests;

public sealed class MapViewportTests
{
    private static readonly Size Square = new(284, 284); // the minimap's map square at 100%

    // ---- The inverse of the calibration transform -------------------------------

    private static readonly MapCalibration Calibration = new(100, 200, 0.5, -0.25, 1000, 10, -20);

    [Fact]
    public void ToWorldUndoesToFraction()
    {
        foreach (var (x, y) in new[] { (300.0, 400.0), (0.0, 0.0), (1500.0, -3000.0), (-200.0, 700.0) })
        {
            var (fx, fy) = Calibration.ToFraction(x, y);
            var (wx, wy) = Calibration.ToWorld(fx, fy);

            Assert.Equal(x, wx, 9);
            Assert.Equal(y, wy, 9);
        }
    }

    [Fact]
    public void ToWorldIsTheArithmeticTheMinimapsMenuUses()
    {
        // The menu's own line until phase 2 moves it onto ToWorld: the swap must not move a waypoint by a bit.
        foreach (var (fx, fy) in new[] { (0.26, 0.92), (0.0, 1.0), (0.5, 0.5), (0.123456789, 0.987654321) })
        {
            var menu = ((fx * Calibration.MapSize - Calibration.OffsetX - Calibration.PinOffsetX) / Calibration.ScaleX,
                        ((1 - fy) * Calibration.MapSize - Calibration.OffsetY - Calibration.PinOffsetY) / Calibration.ScaleY);

            Assert.Equal(menu, Calibration.ToWorld(fx, fy));
        }
    }

    [Fact]
    public void ToWorldIsNotClampedToTheMap()
    {
        var (atEdge, _) = Calibration.ToWorld(0, 0.5);
        var (pastEdge, _) = Calibration.ToWorld(-0.1, 0.5);

        Assert.True(pastEdge < atEdge); // ScaleX is positive here: left of the map is further west
    }

    // ---- Views ------------------------------------------------------------------

    [Fact]
    public void TheWholeMapOnASquarePanelIsTheIslandView()
    {
        var view = MapViewport.Whole(Square);

        Assert.Equal(284, view.MapSize);
        Assert.Equal(new Point(0, 0), view.Offset);
        Assert.Equal(1, view.Zoom);
        Assert.Equal(new Point(71, 213), view.ToPanel(0.25, 0.75));
    }

    [Fact]
    public void TheWholeMapOnAWidePanelIsCentredAlongIt()
    {
        var view = MapViewport.Whole(new Size(400, 300));

        Assert.Equal(300, view.MapSize);
        Assert.Equal(new Point(50, 0), view.Offset);
    }

    [Fact]
    public void ACentredViewPutsThePointAtThePanelsCentre()
    {
        var view = MapViewport.Centered(Square, 5, 0.3, 0.6);
        var drawn = view.ToPanel(0.3, 0.6);

        Assert.Equal(1420, view.MapSize);
        Assert.Equal(5, view.Zoom, 12);
        Assert.Equal(142, drawn.X, 9);
        Assert.Equal(142, drawn.Y, 9);
    }

    [Fact]
    public void TheCentredViewComputesExactlyWhatTheMinimapComputes()
    {
        // Bit for bit, so the refactor can put the minimap on it without moving a pixel.
        foreach (var (size, zoom, fx, fy) in new[] { (284.0, 5.0, 0.4119, 0.5871), (284.0, 1.25, 0.03, 0.99), (213.0, 6.0, 0.777, 0.111) })
        {
            var view = MapViewport.Centered(new Size(size, size), zoom, fx, fy);
            var mapSize = size * zoom;
            var tx = size / 2 - fx * mapSize;
            var ty = size / 2 - fy * mapSize;

            Assert.Equal(mapSize, view.MapSize);
            Assert.Equal(new Point(tx, ty), view.Offset);
            Assert.Equal(new Point(0.25 * mapSize + tx, 0.75 * mapSize + ty), view.ToPanel(0.25, 0.75)); // a marker
            Assert.Equal(((100 - tx) / mapSize, (50 - ty) / mapSize), view.FractionAt(new Point(100, 50))); // the menu's spot
        }
    }

    [Fact]
    public void FractionAtUndoesToPanel()
    {
        var view = MapViewport.Centered(Square, 3.3, 0.21, 0.67);
        var (fx, fy) = view.FractionAt(view.ToPanel(0.8, 0.1));

        Assert.Equal(0.8, fx, 12);
        Assert.Equal(0.1, fy, 12);
    }

    [Fact]
    public void FractionAtRunsPastTheMapsEdge()
    {
        var (fx, fy) = MapViewport.Whole(Square).FractionAt(new Point(-28.4, 312.4));

        Assert.Equal(-0.1, fx, 12);
        Assert.Equal(1.1, fy, 12);
    }

    [Fact]
    public void ThePanelIncludesItsEdgesAndSlackWidensIt()
    {
        var view = MapViewport.Whole(Square);

        Assert.True(view.InPanel(new Point(0, 0)));
        Assert.True(view.InPanel(new Point(284, 284)));
        Assert.False(view.InPanel(new Point(-0.01, 100)));
        Assert.False(view.InPanel(new Point(100, 284.01)));
        Assert.True(view.InPanel(new Point(-5, 289), slack: 6));
        Assert.False(view.InPanel(new Point(-7, 100), slack: 6));
    }

    [Fact]
    public void AnEdgeIndicatorStaysAMarginInside()
    {
        var view = MapViewport.Whole(Square);

        Assert.Equal(new Point(8, 276), view.ClampIntoPanel(new Point(-500, 900), 8));
        Assert.Equal(new Point(100, 50), view.ClampIntoPanel(new Point(100, 50), 8));
    }

    [Fact]
    public void TheVisiblePartIsTheMapUnderThePanel()
    {
        Assert.Equal(new Rect(0, 0, 1, 1), MapViewport.Whole(Square).VisibleFractions);

        var zoomed = MapViewport.Centered(Square, 2, 0.5, 0.5).VisibleFractions;
        Assert.Equal(0.25, zoomed.X, 12);
        Assert.Equal(0.25, zoomed.Y, 12);
        Assert.Equal(0.5, zoomed.Width, 12);
        Assert.Equal(0.5, zoomed.Height, 12);
    }

    [Fact]
    public void ADragMovesTheMapWithTheCursor()
    {
        var view = MapViewport.Centered(Square, 2, 0.5, 0.5);
        var before = view.ToPanel(0.6, 0.4);

        var after = view.PanBy(new Vector(30, -12)).ToPanel(0.6, 0.4);

        Assert.Equal(before.X + 30, after.X, 9);
        Assert.Equal(before.Y - 12, after.Y, 9);
    }

    // ---- Zoom around the cursor --------------------------------------------------

    [Fact]
    public void ZoomingKeepsTheMapPointUnderTheCursor()
    {
        var view = MapViewport.Centered(new Size(900, 900), 1.4, 0.55, 0.45);
        var cursor = new Point(612, 301);
        var under = view.FractionAt(cursor);

        var zoomed = view.ZoomAround(cursor, 1.15, 1, 3);
        var stillUnder = zoomed.FractionAt(cursor);

        Assert.Equal(1.4 * 1.15, zoomed.Zoom, 12);
        Assert.Equal(under.Fx, stillUnder.Fx, 12);
        Assert.Equal(under.Fy, stillUnder.Fy, 12);
    }

    [Fact]
    public void ZoomIsClampedAndAStepAtTheLimitChangesNothing()
    {
        var view = MapViewport.Whole(new Size(900, 900));

        var max = view.ZoomAround(new Point(10, 10), 100, 1, 3);
        Assert.Equal(3, max.Zoom, 12);
        Assert.Equal(max, max.ZoomAround(new Point(700, 450), 1.15, 1, 3)); // the wheel at the limit can't creep the view
        Assert.Equal(view, view.ZoomAround(new Point(300, 300), 1 / 1.15, 1, 3)); // nor at the other end
    }

    [Fact]
    public void TheSharpnessCapIsAZoomForAPixelDensity()
    {
        Assert.Equal(1000.0 * 3 / 900, MapViewport.ZoomForDensity(900, 1000, 3), 12);
        Assert.Equal(1, MapViewport.ZoomForDensity(1000, 1000, 1), 12);
    }

    // ---- Fit and the pan limit ---------------------------------------------------

    [Fact]
    public void FittingAPartFillsThePanelAlongItsLongerSideAndCentresIt()
    {
        var part = new Rect(0.2, 0.1, 0.6, 0.3);

        var view = MapViewport.Fit(Square, part, 1, 10);
        var topLeft = view.ToPanel(part.Left, part.Top);
        var bottomRight = view.ToPanel(part.Right, part.Bottom);

        Assert.Equal(1 / 0.6, view.Zoom, 12);
        Assert.Equal(0, topLeft.X, 9);
        Assert.Equal(284, bottomRight.X, 9);
        Assert.Equal(284 - bottomRight.Y, topLeft.Y, 9); // centred down the panel
        Assert.True(topLeft.Y > 0);
    }

    [Fact]
    public void FittingRespectsTheZoomLimits()
    {
        Assert.Equal(3, MapViewport.Fit(Square, new Rect(0.4, 0.4, 0.01, 0.01), 1, 3).Zoom, 12);
        Assert.Equal(1, MapViewport.Fit(Square, new Rect(0, 0, 1, 1), 1.0, 3).Zoom, 12);
    }

    [Fact]
    public void FittingNothingShowsTheWholeMapCentred()
    {
        var view = MapViewport.Fit(Square, Rect.Empty, 1, 3);

        Assert.Equal(1, view.Zoom, 12);
        Assert.Equal(0, view.Offset.X, 12);
        Assert.Equal(0, view.Offset.Y, 12);
    }

    [Fact]
    public void ThePanLimitBringsTheCentreBackOntoThePart()
    {
        var part = new Rect(0.1, 0.2, 0.8, 0.6);
        var lost = MapViewport.Centered(Square, 3, 0.97, 0.5); // dragged east past the part

        var (fx, fy) = lost.ClampPan(part).FractionAt(lost.Center);

        Assert.Equal(0.9, fx, 12);
        Assert.Equal(0.5, fy, 12);
    }

    [Fact]
    public void InsideThePanLimitNothingMoves()
    {
        var view = MapViewport.Centered(Square, 3, 0.5, 0.5);

        Assert.Equal(view, view.ClampPan(new Rect(0.1, 0.2, 0.8, 0.6)));
        Assert.Equal(view, view.ClampPan(Rect.Empty));
    }
}

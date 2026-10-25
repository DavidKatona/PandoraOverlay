using System.Windows;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>The themed frame's geometry (Themes/FrameGeometry.cs): the cut polygon, the lit accents, the painted glow, the device-pixel snap.</summary>
public class FrameGeometryTests
{
    private const double W = WidgetFrame.Width;     // 298
    private const double H = WidgetFrame.SmallHeight;

    private static void Near(Point expected, Point actual) =>
        Assert.True((expected - actual).Length < 1e-9, $"expected {expected}, got {actual}");

    [Fact]
    public void TheCutPolygonHasSixPointsWhereExpected()
    {
        var p = FrameGeometry.CutPolygon(W, H, 10)!;

        Assert.Equal(6, p.Count);
        Near(new Point(0, 0), p[0]);
        Near(new Point(W - 10, 0), p[1]);  // the top edge stops 10 short of the corner
        Near(new Point(W, 10), p[2]);      // the cut, at 45°
        Near(new Point(W, H), p[3]);       // the bottom-right stays square
        Near(new Point(10, H), p[4]);
        Near(new Point(0, H - 10), p[5]);  // the bottom-left cut
    }

    /// <summary>Inset for a stroke, every edge moves in by the same distance, the diagonals measured square to them.</summary>
    [Fact]
    public void AnInsetPolygonKeepsItsEdgesParallelAndTheSameDistanceIn()
    {
        const double d = 0.5;
        var p = FrameGeometry.CutPolygon(W, H, 10, d)!;

        Near(new Point(d, d), p[0]);
        Near(new Point(W - d, H - d), p[3]);
        // Both diagonals still at 45°…
        Assert.Equal(p[2].X - p[1].X, p[2].Y - p[1].Y, 9);
        Assert.Equal(p[4].X - p[5].X, p[4].Y - p[5].Y, 9);
        // …and d in from the uninset diagonal x − y = W − 10.
        Assert.Equal(d, ((W - 10) - (p[1].X - p[1].Y)) / Math.Sqrt(2), 9);
    }

    [Fact]
    public void NoCutIsNoPolygon() // Default: the Border's own drawing
    {
        Assert.Null(FrameGeometry.CutPolygon(W, H, 0));
        Assert.Null(FrameGeometry.CutPolygon(W, H, 0, 0.5));
    }

    [Fact]
    public void TheLitPathsRunTheirLengths()
    {
        var (topRight, bottomLeft) = FrameGeometry.LitPaths(W, H, cut: 10, edge: 26, side: 20)!.Value;

        var expected = 26 + 10 * Math.Sqrt(2) + 20; // along the edge, round the cut, down the side
        Assert.Equal(expected, FrameGeometry.Length(topRight), 9);
        Assert.Equal(expected, FrameGeometry.Length(bottomLeft), 9);
        Near(new Point(W - 10 - 26, 0), topRight[0]);
        Near(new Point(W, 10 + 20), topRight[^1]);
        Near(new Point(0, H - 10 - 20), bottomLeft[0]);
        Near(new Point(10 + 26, H), bottomLeft[^1]);
    }

    [Fact]
    public void WithoutACutTheLitPathsTurnASquareCorner() // the map window: square, lit, no cut
    {
        var (topRight, _) = FrameGeometry.LitPaths(600, 400, cut: 0, edge: 26, side: 20)!.Value;

        Assert.Equal(3, topRight.Count); // the cut's two points are one corner
        Near(new Point(600, 0), topRight[1]);
        Assert.Equal(46, FrameGeometry.Length(topRight), 9);
    }

    /// <summary>Inset for its stroke, a square corner turns at the inset bounds' own corner, so the line never leaves the frame.</summary>
    [Fact]
    public void AnInsetSquareCornerTurnsAtTheInsetCorner()
    {
        const double d = 0.75;
        var (topRight, bottomLeft) = FrameGeometry.LitPaths(600, 400, cut: 0, edge: 26, side: 20, inset: d)!.Value;

        Assert.Equal(new[] { new Point(600 - d - 26, d), new Point(600 - d, d), new Point(600 - d, d + 20) }, topRight.Points);
        Assert.Equal(new[] { new Point(d, 400 - d - 20), new Point(d, 400 - d), new Point(d + 26, 400 - d) }, bottomLeft.Points);
        Assert.Equal(0, topRight.Arc);
    }

    /// <summary>
    /// A rounded frame without a cut (question 23): each run turns its corner
    /// along the frame's curve at the stroke's inset, an arc of the radius less
    /// the inset, the straight runs measured past it as past a cut.
    /// </summary>
    [Fact]
    public void ARoundedFramesLitRunsFollowItsCurve()
    {
        const double d = 0.75, radius = 10, arc = radius - d;
        var (topRight, bottomLeft) = FrameGeometry.LitPaths(W, H, cut: 0, edge: 26, side: 20, inset: d, radius: radius)!.Value;

        Assert.Equal(arc, topRight.Arc, 9);
        Assert.Equal(arc, bottomLeft.Arc, 9);
        Assert.Equal(3, topRight.Count); // start, the corner the arc rounds, end
        Near(new Point(W - d - arc - 26, d), topRight[0]);
        Near(new Point(W - d, d), topRight[1]);
        Near(new Point(W - d, d + arc + 20), topRight[2]);
        Near(new Point(d, H - d - arc - 20), bottomLeft[0]);
        Near(new Point(d + arc + 26, H - d), bottomLeft[2]);

        var expected = 26 + Math.PI / 2 * arc + 20; // along the edge, round the quarter arc, down the side
        Assert.Equal(expected, FrameGeometry.Length(topRight), 9);
        Assert.Equal(expected, FrameGeometry.Length(bottomLeft), 9);
    }

    /// <summary>The rounded run's geometry: a line to where the curve begins, the arc (radius, turning the right way), a line on.</summary>
    [Fact]
    public void ARoundedRunIsALineAnArcAndALine()
    {
        var (topRight, bottomLeft) = FrameGeometry.LitPaths(W, H, cut: 0, edge: 26, side: 20, inset: 0.75, radius: 10)!.Value;
        var geometry = FrameGeometry.LitGeometry(topRight, bottomLeft);

        Assert.True(geometry.IsFrozen);
        Assert.Equal(2, geometry.Figures.Count);
        foreach (var (figure, run, sweep) in new[]
                 {
                     (geometry.Figures[0], topRight, System.Windows.Media.SweepDirection.Clockwise), // right along the top, then down
                     (geometry.Figures[1], bottomLeft, System.Windows.Media.SweepDirection.Counterclockwise), // down the left, then right
                 })
        {
            Assert.False(figure.IsClosed);
            Assert.Equal(run[0], figure.StartPoint);
            Assert.Equal(3, figure.Segments.Count);
            var into = Assert.IsType<System.Windows.Media.LineSegment>(figure.Segments[0]);
            var arc = Assert.IsType<System.Windows.Media.ArcSegment>(figure.Segments[1]);
            var on = Assert.IsType<System.Windows.Media.LineSegment>(figure.Segments[2]);
            Assert.Equal(new Size(run.Arc, run.Arc), arc.Size);
            Assert.Equal(sweep, arc.SweepDirection);
            Assert.False(arc.IsLargeArc);
            Assert.Equal(run.Arc, (into.Point - run[1]).Length, 9); // the curve begins an arc's radius before the corner…
            Assert.Equal(run.Arc, (arc.Point - run[1]).Length, 9);  // …and ends one after it
            Assert.Equal(run[2], on.Point);
        }
    }

    /// <summary>Radius 0, or a cut, keeps the sharp runs as before: Default's are 0 long and Isle's frames have cuts, so neither changes a pixel.</summary>
    [Fact]
    public void ACutOrNoRadiusKeepsTheSharpRuns()
    {
        var withRadius = FrameGeometry.LitPaths(W, H, cut: 10, edge: 26, side: 20, inset: 0.75, radius: 10)!.Value;
        var without = FrameGeometry.LitPaths(W, H, cut: 10, edge: 26, side: 20, inset: 0.75)!.Value;
        Assert.Equal(without.TopRight.Points, withRadius.TopRight.Points);
        Assert.Equal(0, withRadius.TopRight.Arc);

        var square = FrameGeometry.LitPaths(600, 400, cut: 0, edge: 26, side: 20, radius: 0)!.Value;
        var figure = FrameGeometry.LitGeometry(square.TopRight).Figures[0];
        Assert.IsType<System.Windows.Media.PolyLineSegment>(Assert.Single(figure.Segments));
    }

    /// <summary>A rounded run snaps its ends and its corner as a polyline does and keeps its arc.</summary>
    [Fact]
    public void ARoundedRunSnapsAndKeepsItsArc()
    {
        var run = FrameGeometry.LitPaths(W, H, cut: 0, edge: 26, side: 20, inset: 0.75, radius: 10)!.Value.TopRight;
        var snapped = FrameGeometry.SnapStroke(run, 1.5, 1.25);

        Assert.Equal(run.Arc, snapped.Arc);
        Assert.Equal(FrameGeometry.SnapStroke(run.Points, 1.5, 1.25), snapped.Points);
    }

    [Fact]
    public void NoLitRunIsNoLitPaths() => Assert.Null(FrameGeometry.LitPaths(W, H, cut: 10, edge: 0, side: 0));

    /// <summary>Without a cut the top line spans the straight edge between the two corner radii, half its width in from each, at half its width down.</summary>
    [Fact]
    public void TheTopLineWithoutACutSpansBetweenTheCornerRadii()
    {
        var top = FrameGeometry.LitTop(W, H, cut: 0, radius: 10, stroke: 1.5)!;

        Assert.Equal(2, top.Count);
        Near(new Point(10 + 0.75, 0.75), top[0]);
        Near(new Point(W - 10 - 0.75, 0.75), top[1]);
        Assert.Equal(W - 2 * 10 - 1.5, FrameGeometry.Length(top), 9); // width − 2·radius − the two half-width insets

        var square = FrameGeometry.LitTop(W, H, cut: 0, radius: 0, stroke: 1.5)!;
        Assert.Equal(W - 1.5, FrameGeometry.Length(square), 9);
    }

    /// <summary>With a cut it runs from the square top-left corner to where the top-right cut begins, where the inset outline turns.</summary>
    [Fact]
    public void TheTopLineWithACutStopsAtTheCut()
    {
        var top = FrameGeometry.LitTop(W, H, cut: 10, radius: 0, stroke: 1.5)!;
        var outline = FrameGeometry.CutPolygon(W, H, 10, 0.75)!;

        Near(outline[0], top[0]); // the top-left corner
        Near(outline[1], top[1]); // the cut's start on the top edge
        Assert.Equal(W - 10 - 0.75 * Math.Sqrt(2), FrameGeometry.Length(top), 9);
    }

    [Fact]
    public void NoTopLineIsNoRun() // Default: Lit.Top is 0
    {
        Assert.Null(FrameGeometry.LitTop(W, H, cut: 0, radius: 10, stroke: 0));
        Assert.Null(FrameGeometry.LitTop(W, H, cut: 10, radius: 0, stroke: 0));
        Assert.Null(FrameGeometry.LitTop(30, H, cut: 0, radius: 15, stroke: 1.5)); // the corners leave the edge no length
    }

    [Fact]
    public void AGlowIsThreeStrokesWidestFirst()
    {
        var glow = FrameGeometry.Glow(3);

        Assert.Equal(3, glow.Count);
        Assert.Equal(new FrameGeometry.GlowStroke(6.0, 0.05), glow[0]);
        Assert.Equal(2 * 3, glow[0].Width, 9);
        Assert.Equal(1.4 * 3, glow[1].Width, 9);
        Assert.Equal(0.10, glow[1].Opacity, 9);
        Assert.Equal(0.8 * 3, glow[2].Width, 9);
        Assert.Equal(0.18, glow[2].Opacity, 9);
    }

    [Fact]
    public void NoGlowIsNoStrokes() => Assert.Empty(FrameGeometry.Glow(0));

    /// <summary>At 100%, 125% and 200%, a 1.5 px line's edges land on whole device pixels, its width a whole number of them.</summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(2.0)]
    public void ASnappedLineSitsOnWholeDevicePixels(double ppd)
    {
        foreach (var centre in new[] { 0.75, 10.3, 167.25, 297.25 })
        {
            var width = FrameGeometry.SnapWidth(1.5, ppd);
            var at = FrameGeometry.SnapLine(centre, 1.5, ppd);
            var near = (at - width / 2) * ppd;
            var far = (at + width / 2) * ppd;

            Assert.Equal(Math.Round(near), near, 9);
            Assert.Equal(Math.Round(far), far, 9);
            Assert.True(width * ppd >= 1);
            Assert.True(Math.Abs(at - centre) <= 1 / ppd, "snapping moved the line by more than a device pixel");
        }
    }

    [Theory]
    [InlineData(1.0, 1.5, 2.0)]   // 1.5 device px rounds up to 2
    [InlineData(1.25, 1.5, 1.6)]  // 1.875 → 2 device px = 1.6 dips
    [InlineData(2.0, 1.5, 1.5)]   // exactly 3 device px
    [InlineData(1.25, 0.2, 0.8)]  // never thinner than one device pixel
    public void ASnappedWidthIsWholeDevicePixels(double ppd, double width, double expected) =>
        Assert.Equal(expected, FrameGeometry.SnapWidth(width, ppd), 9);

    [Fact]
    public void ACoordinateSnapsToTheNearestDevicePixel()
    {
        Assert.Equal(10.0, FrameGeometry.Snap(10.3, 1.0), 9);
        Assert.Equal(10.4, FrameGeometry.Snap(10.3, 1.25), 9); // 12.875 → 13 device px
        Assert.Equal(10.5, FrameGeometry.Snap(10.3, 2.0), 9);  // 20.6 → 21
    }

    /// <summary>The glass's corners, every coordinate on a whole device pixel (ThemedBorder's fill).</summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(2.0)]
    public void ASnappedShapeHasEveryCornerOnAWholeDevicePixel(double ppd)
    {
        var corners = FrameGeometry.SnapPoints(FrameGeometry.CutPolygon(W, H, 10)!, ppd);

        Assert.Equal(6, corners.Count);
        foreach (var p in corners)
        {
            Assert.Equal(Math.Round(p.X * ppd), p.X * ppd, 9);
            Assert.Equal(Math.Round(p.Y * ppd), p.Y * ppd, 9);
        }
    }

    /// <summary>An outline 1.5 wide, half its width in (ThemedBorder's line): its straight edges cover whole device pixels at every scale.</summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(2.0)]
    public void ASnappedOutlineCoversWholeDevicePixels(double ppd)
    {
        const double width = 1.5;
        var line = FrameGeometry.SnapWidth(width, ppd);
        var outline = FrameGeometry.SnapStroke(FrameGeometry.CutPolygon(W, H, 10, width / 2)!, width, ppd);

        Assert.Equal(6, outline.Count);
        foreach (var p in outline)
        {
            foreach (var c in new[] { p.X, p.Y })
            {
                var near = (c - line / 2) * ppd;
                Assert.Equal(Math.Round(near), near, 9);
            }
        }
        // The top edge is still level and the left edge upright, the bottom-right corner square.
        Assert.Equal(outline[0].Y, outline[1].Y, 9);
        Assert.Equal(outline[0].X, outline[5].X, 9);
        Assert.Equal(outline[2].X, outline[3].X, 9);
        Assert.Equal(outline[3].Y, outline[4].Y, 9);
    }
}

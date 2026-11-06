using System.Windows.Media;
using Path = System.Windows.Shapes.Path;

namespace PandoraOverlay;

/// <summary>
/// MapCanvas, route part: the route's later legs. Dashed as the tracking lines
/// are (the same width and pattern, counted in stroke widths), but fainter, so
/// the leg you are walking is the one that stands out (the owner's call). In
/// each leg's target stop's palette colour: data, the same in every theme, so
/// a theme switch leaves them alone. In the map's own layer, in map pixels,
/// like the trail: they move with the map's glides at no cost per frame, and
/// sit under the tracking lines and the markers. No route draws nothing at
/// all, so the golden renders hold. Same class as MapCanvas.xaml.cs, split for
/// reading.
/// </summary>
public partial class MapCanvas
{
    /// <summary>Faint enough to read as "later", strong enough to read over the jungle. The first try, 55%, all but lost a pale colour's leg on the green at the map's 1.5 px. To be judged in game.</summary>
    internal const double RouteLegOpacity = 0.8;

    private const double RouteLegWidth = 1.5;

    private static readonly DoubleCollection RouteLegDashes = FrozenDashes();

    private static DoubleCollection FrozenDashes()
    {
        var dashes = new DoubleCollection { 4, 3 };
        dashes.Freeze();
        return dashes;
    }

    /// <summary>
    /// The legs to draw, replacing the last set: one path per colour rather
    /// than one element per leg, since a route can hold 64 stops. Call again
    /// when the route or the map's rendered size changes.
    /// </summary>
    public void ShowRouteLegs(IReadOnlyList<RouteLeg> legs)
    {
        RouteLegsLayer.Children.Clear();
        foreach (var colour in legs.GroupBy(l => l.Colour))
        {
            var lines = new StreamGeometry();
            using (var context = lines.Open())
            {
                foreach (var leg in colour)
                {
                    context.BeginFigure(leg.From, isFilled: false, isClosed: false);
                    context.LineTo(leg.To, isStroked: true, isSmoothJoin: false);
                }
            }
            lines.Freeze();
            RouteLegsLayer.Children.Add(new Path
            {
                Data = lines,
                Stroke = MapBrushes.Of(colour.Key),
                StrokeThickness = RouteLegWidth,
                StrokeDashArray = RouteLegDashes,
                Opacity = RouteLegOpacity,
                IsHitTestVisible = false,
            });
        }
    }
}

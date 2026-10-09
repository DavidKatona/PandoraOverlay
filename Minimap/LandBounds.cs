using System.Windows;

namespace PandoraOverlay;

/// <summary>
/// The land's extent on an area map: the smallest rectangle, in map
/// fractions (0–1 across and down), holding every pixel that belongs to an
/// area. An area includes its coastal water, so the box keeps a margin of
/// sea round the land by construction. The big map opens fitted to it and
/// stops a drag from losing it (MapViewport.Fit / ClampPan). Nothing here
/// knows which map it is: a new map's areas give a new box. Pure, tested.
/// </summary>
public static class LandBounds
{
    /// <summary>The box round every pixel with an area, pixel edges included; null when no pixel has one.</summary>
    public static Rect? Of(AreaMap map)
    {
        int left = map.Size, top = map.Size, right = -1, bottom = -1;
        for (var y = 0; y < map.Size; y++)
        {
            for (var x = 0; x < map.Size; x++)
            {
                if (map.IndexAtPixel(x, y) == AreaMap.None) continue;
                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }
        }
        if (right < 0) return null;
        double size = map.Size;
        return new Rect(left / size, top / size, (right + 1 - left) / size, (bottom + 1 - top) / size);
    }
}

using System.Windows;
using System.Windows.Media;

namespace PandoraOverlay.Games.Engine;

/// <summary>
/// The arithmetic that puts a logical pixel buffer on screen sharp (A17),
/// kept pure so every number of it can be pinned with no window and no
/// display: how many device pixels a playfield takes at a scale
/// (<see cref="DeviceSize"/>), how its pixels are made into device pixels
/// (<see cref="Scale(uint[], int, int, uint[], int, int)"/>), and where the
/// result is drawn so that it lands on whole device pixels
/// (<see cref="Place"/>). <see cref="PixelSurface"/> is the only caller in
/// the engine; it holds a <see cref="ScalePlan"/> per device size, so the
/// weight tables are worked out once per size, never per frame.
///
/// <para><b>Why the engine scales, not WPF.</b> WPF asked to draw a bitmap at
/// another size filters it (blurred) or, with NearestNeighbor, gives pixels
/// of uneven width at a fractional scale (one logical pixel two device pixels
/// wide, its neighbour one). Whole-number scaling in a fixed frame would leave
/// the board at 57% to 80% of its room at 125% to 175% (A17). So the engine
/// makes the bitmap at the device's own resolution and WPF draws it 1:1.</para>
///
/// <para><b>Two cases.</b> At a whole-number scale (100%, 200%, 300%) every
/// logical pixel becomes a k x k block: exact. At any other scale (125%,
/// 150%, 175%, a window drawn smaller) each device pixel is the area-weighted
/// average of the logical pixels it covers: a logical pixel keeps a solid
/// core and gets edges at most one device pixel soft, which is the honest
/// best a fractional scale allows.</para>
/// </summary>
internal static class Presenter
{
    /// <summary>
    /// The weights of one device pixel always sum to exactly this: 256, so a
    /// sum of weighted channels divides by a shift and a flat colour stays
    /// exactly that colour (256 x c / 256 is c, whatever the rounding).
    /// </summary>
    internal const int WeightTotal = 256;

    /// <summary>
    /// The device size a logical size takes at a scale: each side rounded to
    /// whole device pixels (a half away from zero, so the answer never
    /// depends on banker's rounding), at least 1. A scale is the element's
    /// real scale (the display scale times any layout scale above it), so it
    /// must be a positive finite number.
    /// </summary>
    public static (int Width, int Height) DeviceSize(int width, int height, double scaleX, double scaleY)
    {
        if (width < 1) throw new ArgumentOutOfRangeException(nameof(width), width, "A logical size is at least 1.");
        if (height < 1) throw new ArgumentOutOfRangeException(nameof(height), height, "A logical size is at least 1.");
        CheckScale(scaleX, nameof(scaleX));
        CheckScale(scaleY, nameof(scaleY));
        return (DeviceSide(width, scaleX), DeviceSide(height, scaleY));
    }

    /// <summary>
    /// Scales premultiplied pixels from (w, h) to (dw, dh). A whole-number
    /// scale copies; anything else is an area average. Works out the weight
    /// tables on every call: for tests and one-off use. The surface keeps a
    /// <see cref="ScalePlan"/> of its device size and calls
    /// <see cref="Scale(uint[], uint[], ScalePlan)"/>, which allocates nothing.
    /// </summary>
    public static void Scale(uint[] source, int w, int h, uint[] target, int dw, int dh) =>
        Scale(source, target, new ScalePlan(w, h, dw, dh));

    /// <summary>
    /// Scales <paramref name="source"/> (the plan's logical size) into
    /// <paramref name="target"/> (its device size), with the plan's
    /// precomputed tables and its own intermediate rows: no allocation.
    /// </summary>
    public static void Scale(uint[] source, uint[] target, ScalePlan plan)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(plan);
        if (source.Length != plan.Width * plan.Height)
            throw new ArgumentException($"The source holds {source.Length} pixels, not {plan.Width} x {plan.Height}.", nameof(source));
        if (target.Length != plan.DeviceWidth * plan.DeviceHeight)
            throw new ArgumentException($"The target holds {target.Length} pixels, not {plan.DeviceWidth} x {plan.DeviceHeight}.", nameof(target));

        if (plan.Block is int k)
            CopyBlocks(source, plan.Width, plan.Height, target, k);
        else
            AreaAverage(source, target, plan);
    }

    /// <summary>
    /// Where a surface's bitmap goes so it lands on whole device pixels, from
    /// the surface's transform to its root visual (<paramref name="toRoot"/>,
    /// any layout or render scale above it, and where it sits) and the
    /// display scale (<paramref name="dpiScaleX"/>, <paramref name="dpiScaleY"/>).
    /// <para>
    /// The real scale is the display scale times the transform's own scale
    /// along each axis (the length of its axis vector, so a mirror or a turn
    /// does not give a negative or a shrunk scale). The surface's origin in
    /// device pixels is the transform's offset (where its (0, 0) lands in the
    /// root) times the display scale; the root's own origin is the window's
    /// client corner, which Windows always places on a whole device pixel.
    /// That origin is rounded, and the difference, divided by the real scale,
    /// is how far the bitmap is nudged in the surface's own units: the
    /// surface's point (offset) maps to exactly the rounded device pixel. The
    /// bitmap is then drawn at its natural size, device size divided by the
    /// real scale, which is exactly device size device pixels: 1:1, never
    /// scaled by WPF. The nudge is at most half a device pixel each way.
    /// </para>
    /// </summary>
    public static Placement Place(int width, int height, Matrix toRoot, double dpiScaleX, double dpiScaleY)
    {
        CheckScale(dpiScaleX, nameof(dpiScaleX));
        CheckScale(dpiScaleY, nameof(dpiScaleY));
        var scaleX = dpiScaleX * Math.Sqrt(toRoot.M11 * toRoot.M11 + toRoot.M12 * toRoot.M12);
        var scaleY = dpiScaleY * Math.Sqrt(toRoot.M21 * toRoot.M21 + toRoot.M22 * toRoot.M22);
        // A transform that collapses the surface (a scale of 0) or is not a
        // number leaves nothing to draw on: present it at 1, which draws
        // nothing wrong and recovers at the next layout.
        if (!(scaleX > 0) || double.IsInfinity(scaleX)) scaleX = dpiScaleX;
        if (!(scaleY > 0) || double.IsInfinity(scaleY)) scaleY = dpiScaleY;

        var (deviceWidth, deviceHeight) = DeviceSize(width, height, scaleX, scaleY);

        var originX = toRoot.OffsetX * dpiScaleX;
        var originY = toRoot.OffsetY * dpiScaleY;
        var nudgeX = (Math.Round(originX, MidpointRounding.AwayFromZero) - originX) / scaleX;
        var nudgeY = (Math.Round(originY, MidpointRounding.AwayFromZero) - originY) / scaleY;
        if (!double.IsFinite(nudgeX)) nudgeX = 0;
        if (!double.IsFinite(nudgeY)) nudgeY = 0;

        return new Placement(deviceWidth, deviceHeight, scaleX, scaleY, nudgeX, nudgeY,
            deviceWidth / scaleX, deviceHeight / scaleY);
    }

    /// <summary>Where a surface with no presentation source is drawn (a test, an off-screen render): scale 1, no nudge.</summary>
    public static Placement Unplaced(int width, int height) =>
        new(width, height, 1, 1, 0, 0, width, height);

    private static int DeviceSide(int logical, double scale) =>
        Math.Max(1, (int)Math.Min(int.MaxValue, Math.Round(logical * scale, MidpointRounding.AwayFromZero)));

    private static void CheckScale(double scale, string name)
    {
        if (!(scale > 0) || double.IsInfinity(scale))
            throw new ArgumentOutOfRangeException(name, scale, "A scale is a positive finite number.");
    }

    // ---- The whole-number case -------------------------------------------------------

    /// <summary>
    /// Every logical pixel becomes a k x k block: each logical row is widened
    /// once into the first of its k device rows, and the other k - 1 are
    /// copies of that one. At k = 1 a plain copy.
    /// </summary>
    private static void CopyBlocks(uint[] source, int w, int h, uint[] target, int k)
    {
        if (k == 1)
        {
            Array.Copy(source, target, source.Length);
            return;
        }

        var dw = w * k;
        for (var y = 0; y < h; y++)
        {
            var first = y * k * dw;
            var t = first;
            var s = y * w;
            for (var x = 0; x < w; x++)
            {
                var p = source[s + x];
                for (var i = 0; i < k; i++)
                    target[t++] = p;
            }
            for (var r = 1; r < k; r++)
                Array.Copy(target, first, target, first + r * dw, dw);
        }
    }

    // ---- The fractional case ----------------------------------------------------------

    /// <summary>
    /// Separable area sampling: each logical row is resampled along x into
    /// the plan's intermediate rows (dw x h), then each device column along y
    /// from those (dw x dh). Each pass rounds to the nearest 8-bit value.
    /// <para>
    /// The four channels are worked on at once, in a 64-bit word with one
    /// 16-bit lane per channel (<see cref="Spread"/>): a channel times a
    /// weight is at most 255 x 256 = 65,280, and the weights of one device
    /// pixel sum to 256, so a lane's sum never passes 65,280 and never spills
    /// into its neighbour. That is the same arithmetic as four separate sums,
    /// in a quarter of the multiplications.
    /// </para>
    /// <para>
    /// Premultiplied stays premultiplied: in every logical pixel a colour
    /// channel is at most the alpha, so its weighted sum is at most the
    /// alpha's weighted sum over the same pixels and weights, and rounding
    /// to the nearest keeps that order. A flat colour stays flat: its sum is
    /// 256 x c, exactly c after the shift.
    /// </para>
    /// </summary>
    private static void AreaAverage(uint[] source, uint[] target, ScalePlan plan)
    {
        int w = plan.Width, h = plan.Height, dw = plan.DeviceWidth, dh = plan.DeviceHeight;
        var mid = plan.Intermediate;
        AxisWeights xs = plan.X!, ys = plan.Y!;

        // Along x: (w x h) to (dw x h).
        for (var y = 0; y < h; y++)
        {
            var row = y * w;
            var outRow = y * dw;
            for (var i = 0; i < dw; i++)
            {
                var first = xs.First[i];
                var at = xs.Offset[i];
                var end = xs.Offset[i + 1];
                ulong sum = 0;
                for (var j = first; at < end; at++, j++)
                    sum += Spread(source[row + j]) * (uint)xs.Weight[at];
                mid[outRow + i] = Pack(sum);
            }
        }

        // Along y: (dw x h) to (dw x dh). Row by row of the target, so both
        // arrays are walked in memory order.
        for (var i = 0; i < dh; i++)
        {
            var first = ys.First[i];
            var start = ys.Offset[i];
            var end = ys.Offset[i + 1];
            var outRow = i * dw;
            if (end - start == 1)
            {
                // The whole device row lies in one logical row (a whole-number
                // stretch along y, or a row's solid core): its weight is 256.
                Array.Copy(mid, first * dw, target, outRow, dw);
                continue;
            }
            for (var x = 0; x < dw; x++)
            {
                ulong sum = 0;
                var j = first;
                for (var at = start; at < end; at++, j++)
                    sum += Spread(mid[j * dw + x]) * (uint)ys.Weight[at];
                target[outRow + x] = Pack(sum);
            }
        }
    }

    /// <summary>
    /// 0xAARRGGBB spread into four 16-bit lanes, 0x00AA_00RR_00GG_00BB, so
    /// one multiplication by a weight of up to 256 scales all four channels
    /// without any lane reaching the next.
    /// </summary>
    internal static ulong Spread(uint p) =>
        ((ulong)(p & 0xFF000000) << 24)
        | ((ulong)(p & 0x00FF0000) << 16)
        | ((ulong)(p & 0x0000FF00) << 8)
        | (p & 0x000000FF);

    /// <summary>
    /// The four lane sums (each a weighted sum whose weights total 256)
    /// divided by 256, rounded to the nearest (+128 before the shift; a lane
    /// is at most 65,280, so +128 stays inside it), and packed back into
    /// 0xAARRGGBB.
    /// </summary>
    internal static uint Pack(ulong lanes)
    {
        var v = ((lanes + 0x0080_0080_0080_0080UL) >> 8) & 0x00FF_00FF_00FF_00FFUL;
        return (uint)(((v >> 24) & 0xFF000000) | ((v >> 16) & 0x00FF0000) | ((v >> 8) & 0x0000FF00) | (v & 0xFF));
    }
}

/// <summary>
/// Where a surface's bitmap is drawn, in the surface's own units, and at what
/// device size it is made (<see cref="Presenter.Place"/>). The bitmap's DPI is
/// 96 times the real scale, so its natural size is exactly
/// (<see cref="DrawWidth"/>, <see cref="DrawHeight"/>) and WPF draws it 1:1.
/// </summary>
internal readonly record struct Placement(
    int DeviceWidth, int DeviceHeight,
    double ScaleX, double ScaleY,
    double OffsetX, double OffsetY,
    double DrawWidth, double DrawHeight);

/// <summary>
/// One axis's weight table, logical n to device d: for device pixel i, the
/// logical pixels First[i] .. First[i] + (Offset[i + 1] - Offset[i]) - 1, with
/// weights Weight[Offset[i]] .. that sum to exactly
/// <see cref="Presenter.WeightTotal"/>.
/// <para>
/// Device pixel i covers the logical span [i n / d, (i + 1) n / d). Counted in
/// units of 1/d of a logical pixel, so every bound is a whole number and no
/// overlap is rounded: the span is [i n, (i + 1) n), logical pixel j is
/// [j d, (j + 1) d), their overlap is a whole number of units, and the overlaps
/// of one device pixel sum to n. Each weight is its overlap's share of 256,
/// rounded down; the few units the rounding lost go to the largest weight (the
/// first of equals), so the sum is exactly 256 and a flat colour stays flat.
/// Growing (d &gt; n) a device pixel covers one or two logical pixels; shrinking
/// (d &lt; n), up to n / d + 2.
/// </para>
/// </summary>
internal sealed class AxisWeights
{
    public AxisWeights(int logical, int device)
    {
        if (logical < 1) throw new ArgumentOutOfRangeException(nameof(logical), logical, "A side is at least 1.");
        if (device < 1) throw new ArgumentOutOfRangeException(nameof(device), device, "A side is at least 1.");

        First = new int[device];
        Offset = new int[device + 1];
        var weights = new List<int>(device * 2);
        for (var i = 0; i < device; i++)
        {
            long lo = (long)i * logical, hi = (long)(i + 1) * logical;
            var j0 = (int)(lo / device);
            var j1 = (int)((hi - 1) / device);
            First[i] = j0;
            Offset[i] = weights.Count;

            var sum = 0;
            var largestAt = -1;
            var largest = -1;
            for (var j = j0; j <= j1; j++)
            {
                var overlap = Math.Min(hi, (long)(j + 1) * device) - Math.Max(lo, (long)j * device);
                var weight = (int)(overlap * Presenter.WeightTotal / logical);
                if (weight > largest)
                {
                    // Strictly greater: of equal weights, the first keeps the remainder.
                    largest = weight;
                    largestAt = weights.Count;
                }
                weights.Add(weight);
                sum += weight;
            }
            weights[largestAt] += Presenter.WeightTotal - sum;
        }
        Offset[device] = weights.Count;
        Weight = weights.ToArray();
    }

    /// <summary>The first logical pixel under each device pixel.</summary>
    public int[] First { get; }

    /// <summary>Where each device pixel's weights start in <see cref="Weight"/>; one more entry than device pixels, the last the end.</summary>
    public int[] Offset { get; }

    /// <summary>Every device pixel's weights, one after another, each run summing to 256.</summary>
    public int[] Weight { get; }
}

/// <summary>
/// Everything <see cref="Presenter.Scale(uint[], uint[], ScalePlan)"/> needs
/// for one pair of sizes, worked out once: the case (a whole-number block, or
/// the area average with its two weight tables and its intermediate rows). The
/// surface remakes it only when its device size changes.
/// </summary>
internal sealed class ScalePlan
{
    public ScalePlan(int width, int height, int deviceWidth, int deviceHeight)
    {
        if (width < 1) throw new ArgumentOutOfRangeException(nameof(width), width, "A side is at least 1.");
        if (height < 1) throw new ArgumentOutOfRangeException(nameof(height), height, "A side is at least 1.");
        if (deviceWidth < 1) throw new ArgumentOutOfRangeException(nameof(deviceWidth), deviceWidth, "A side is at least 1.");
        if (deviceHeight < 1) throw new ArgumentOutOfRangeException(nameof(deviceHeight), deviceHeight, "A side is at least 1.");
        Width = width;
        Height = height;
        DeviceWidth = deviceWidth;
        DeviceHeight = deviceHeight;

        if (deviceWidth % width == 0 && deviceHeight % height == 0
            && deviceWidth / width == deviceHeight / height)
        {
            Block = deviceWidth / width;
            X = Y = null;
            Intermediate = Array.Empty<uint>();
        }
        else
        {
            X = new AxisWeights(width, deviceWidth);
            Y = new AxisWeights(height, deviceHeight);
            Intermediate = new uint[(long)deviceWidth * height <= Array.MaxLength
                ? deviceWidth * height
                : throw new ArgumentOutOfRangeException(nameof(deviceWidth), deviceWidth, "Too many pixels for one array.")];
        }
    }

    public int Width { get; }
    public int Height { get; }
    public int DeviceWidth { get; }
    public int DeviceHeight { get; }

    /// <summary>k when every logical pixel becomes a k x k block (a whole-number scale); null for the area average.</summary>
    public int? Block { get; }

    /// <summary>The weights along x (area average only).</summary>
    public AxisWeights? X { get; }

    /// <summary>The weights along y (area average only).</summary>
    public AxisWeights? Y { get; }

    /// <summary>The rows after the pass along x: device width x logical height (area average only).</summary>
    public uint[] Intermediate { get; }
}

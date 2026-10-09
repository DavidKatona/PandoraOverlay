# Generates the overlay's AREA MAP: Assets/areas.png + Assets/areas.json.
#
# DON'T RE-RUN THIS OVER THE BUNDLED MAP WITHOUT THE OWNER'S WORD. Assets/areas.png has
# been HAND-CORRECTED since Oct 5 2026 (Central Dome painted larger to the south-east,
# ~2,000 px, nearly all taken from Swamps), and a run overwrites every painted correction.
# To redraw the picture after painting, run preview-area-map.ps1: it only reads.
#
# The map answers "which area am I in": an image the size of the island map with one
# flat colour per named area and nothing else (transparent = no area, open sea). The
# overlay reads it as data - your position, through the same transform as the arrow,
# picks a pixel; the pixel's colour is the area (Minimap/AreaMap.cs).
#
# EVERYTHING ABOUT A PARTICULAR MAP IS INPUT, NOT CODE. For a new map: replace
# Assets/map.png, replace area-labels.json (the site's map calibration and one label
# point per area, in world cm, with a size hint and a sea flag) and run this script.
# Nothing here, in the overlay or in its tests names an area or treats one differently
# from another; how an area comes out depends only on where its label is and on the map.
# That is the owner's rule (Oct 2 2026), and it covers counting on this island too: how many
# areas, the picture's size, how much land there is. So the calibration lives in the input
# (and is copied into areas.json for the tests), distances are in metres, colours past the
# hand-picked ones are generated, and the one number about the map PICTURE's style, not the
# island, is named as such (SeaTolerance).
#
# The label points give the NAMES. The BORDERS are not from any source - nobody
# publishes them and none are drawn on the map - so they are computed here:
#   * sea = the colour of the map's corner, connected to the image border (lakes and
#     rivers stay land);
#   * every land pixel goes to the label that reaches it soonest travelling OVER LAND
#     ("large" labels spread 1.3x as fast, "small" ones 0.7x), so an area never jumps a bay;
#   * sea labels claim the water around them (about 1.1 km x the same factor);
#   * a land area whose label sits offshore takes the nearest shore as its land and
#     also claims the water around the label;
#   * islets nothing reached take the nearest claimed area; a 375 m band of coastal
#     water follows the land beside it; open sea stays empty.
# The owner accepted the computed borders as the first version. Their known weak spots, where
# painting helps: straight borders that ignore rivers and ridges, a small area as a round blob
# (the land round a landmark, not its walls), a coastal area claiming a big octagon of sea.
# Rendered and NOT taken (Oct 2 2026): rounder water shapes - Spread stepping in 16
# directions instead of 8, so bays aren't angular. Too subtle at actual size to change a map
# already tried in game; it would live only here, so it can still be added if the angular
# bays start to bother.
# Distances are in metres and turned into pixels with the calibration, so they hold for
# a map of another size or scale.
#
# Corrections are meant to be PAINTED: edit Assets/areas.png with the colours listed in
# Assets/areas.json (hard edges, no anti-aliasing, save as PNG). Re-running this script
# OVERWRITES such corrections - it is the starting point, not a build step. After
# painting, preview-area-map.ps1 redraws the picture for people (docs/area-map.jpg)
# from the painted map; it only reads. -PreviewPath here calls that same script.
#
# Usage (Windows PowerShell 5.1):   .\make-area-map.ps1 [-PreviewPath docs\area-map.jpg]
param(
    [string]$Labels = "$PSScriptRoot\area-labels.json",
    [string]$Map = "$PSScriptRoot\..\..\Assets\map.png",
    [string]$OutDir = "$PSScriptRoot\..\..\Assets",
    [string]$PreviewPath = ''
)

$code = @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

public class AreaLabel
{
    public string Name; public double X; public double Y; public double W; public bool Ocean;   // X, Y: world cm
    public string Colour; public int Px; public int Py; public int Sx; public int Sy; public bool SeaType; public int Pixels;
}

// The site's map calibration: world cm -> map fraction, the transform the overlay's arrow uses.
public class MapCal
{
    public double OffsetX, OffsetY, ScaleX, ScaleY, MapSize, PinOffsetX, PinOffsetY;
}

public class MinHeap
{
    float[] k = new float[1 << 16]; int[] v = new int[1 << 16]; int[] l = new int[1 << 16];
    public int Count;
    public void Push(float key, int val, int lab)
    {
        if (Count == k.Length) { Array.Resize(ref k, Count * 2); Array.Resize(ref v, Count * 2); Array.Resize(ref l, Count * 2); }
        int i = Count++;
        while (i > 0)
        {
            int p = (i - 1) >> 1;
            if (k[p] <= key) break;
            k[i] = k[p]; v[i] = v[p]; l[i] = l[p]; i = p;
        }
        k[i] = key; v[i] = val; l[i] = lab;
    }
    public void Pop(out float key, out int val, out int lab)
    {
        key = k[0]; val = v[0]; lab = l[0];
        Count--;
        float lk = k[Count]; int lv = v[Count]; int ll = l[Count];
        int i = 0;
        while (true)
        {
            int c = 2 * i + 1;
            if (c >= Count) break;
            if (c + 1 < Count && k[c + 1] < k[c]) c++;
            if (k[c] >= lk) break;
            k[i] = k[c]; v[i] = v[c]; l[i] = l[c]; i = c;
        }
        k[i] = lk; v[i] = lv; l[i] = ll;
    }
}

public static class AreaMapGenerator
{
    // How the borders are drawn, in METRES - the calibration turns them into pixels of whatever map is given.
    const double SeaLabelReach = 1125;   // a sea label claims the water this far around it (x its size factor)
    const double CoastalBand = 375;      // water this close to land belongs to that land's area
    const double LabelSnap = 750;        // a label this close to the shore (or, for a sea label, to water) is moved onto it

    // About the map PICTURE, not the island: how close to the corner pixel's colour counts as sea.
    const int SeaTolerance = 38;

    const int MaxAreas = 255;            // the overlay keeps one byte per pixel, 0 = no area

    // Green-Armytage's 26 "alphabet" colours, built to be told apart (Ebony swapped for a light grey);
    // a map with more areas gets further colours made up to differ from these and from each other.
    static readonly string[] Palette = {
        "F0A3FF","0075DC","993F00","4C005C","FFE100","005C31","2BCE48","FFCC99","808080","94FFB5","8F7C00","9DCC00","C20088",
        "003380","FFA405","FFA8BB","426600","FF0010","5EF1F2","00998F","E0FF66","740AFF","990000","FFFF80","DDDDDD","FF5005" };

    static int N;   // pixels per side of the map picture

    static int ColourDistance(string a, string b)
    {
        int d = 0;
        for (int i = 0; i < 6; i += 2)
        {
            int x = Convert.ToInt32(a.Substring(i, 2), 16) - Convert.ToInt32(b.Substring(i, 2), 16);
            d += x * x;
        }
        return d;
    }

    // Past the hand-picked ones: the first made-up colour that is clearly apart from every colour in use
    // (the bar is lowered a little with each try, so a map with very many areas still gets one).
    static string ColourFor(int k, HashSet<string> used)
    {
        if (k < Palette.Length) return Palette[k];
        for (int t = 0; ; t++)
        {
            int apart = Math.Max(0, 90 - t) * Math.Max(0, 90 - t);
            double h = ((k + t) * 0.61803398875) % 1.0, s = 0.55 + 0.2 * ((k + t) % 3), v = 0.95 - 0.25 * ((k + t) % 2);
            double f = h * 6 - Math.Floor(h * 6), p = v * (1 - s), q = v * (1 - f * s), u = v * (1 - (1 - f) * s);
            double r, g, b;
            switch ((int)Math.Floor(h * 6) % 6)
            {
                case 0: r = v; g = u; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = u; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = u; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
            string hex = string.Format("{0:X2}{1:X2}{2:X2}", (int)Math.Round(r * 255), (int)Math.Round(g * 255), (int)Math.Round(b * 255));
            if (used.Contains(hex)) continue;
            bool clear = true;
            foreach (string other in used) if (ColourDistance(hex, other) < apart) { clear = false; break; }
            if (clear) return hex;
        }
    }

    static void Spread(bool[] pass, List<int> sIdx, List<int> sLab, double[] speed, float limit, int[] lab, float[] cst)
    {
        for (int i = 0; i < lab.Length; i++) { lab[i] = -1; cst[i] = float.MaxValue; }
        MinHeap h = new MinHeap();
        for (int s = 0; s < sIdx.Count; s++)
        {
            int idx = sIdx[s];
            if (cst[idx] > 0) { cst[idx] = 0; lab[idx] = sLab[s]; h.Push(0, idx, sLab[s]); }
        }
        while (h.Count > 0)
        {
            float c; int i; int L;
            h.Pop(out c, out i, out L);
            if (c > cst[i] || lab[i] != L) continue;
            int x = i % N, y = i / N;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= N || ny >= N) continue;
                int j = ny * N + nx;
                if (!pass[j]) continue;
                double step = (dx != 0 && dy != 0) ? 1.41421356 : 1.0;
                float nc = c + (float)(step / (speed == null ? 1.0 : speed[L]));
                if (nc > limit) continue;
                if (nc < cst[j]) { cst[j] = nc; lab[j] = L; h.Push(nc, j, L); }
            }
        }
    }

    static bool Nearest(bool[] mask, int px, int py, int reach, out int ox, out int oy)
    {
        double best = double.MaxValue; ox = px; oy = py; bool found = false;
        for (int y = Math.Max(0, py - reach); y <= Math.Min(N - 1, py + reach); y++)
        for (int x = Math.Max(0, px - reach); x <= Math.Min(N - 1, px + reach); x++)
        {
            if (!mask[y * N + x]) continue;
            double d = (x - px) * (x - px) + (y - py) * (y - py);
            if (d < best && d <= reach * reach) { best = d; ox = x; oy = y; found = true; }
        }
        return found;
    }

    static void SaveArgb(int[] pixels, string path)
    {
        using (Bitmap b = new Bitmap(N, N, PixelFormat.Format32bppArgb))
        {
            BitmapData d = b.LockBits(new Rectangle(0, 0, N, N), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(pixels, 0, d.Scan0, N * N);
            b.UnlockBits(d);
            b.Save(path, ImageFormat.Png);
        }
    }

    public static string Run(string mapPath, MapCal cal, AreaLabel[] labels, string outPng)
    {
        if (labels.Length == 0) throw new Exception("no labels");
        if (labels.Length > MaxAreas) throw new Exception("more than " + MaxAreas + " labels");
        StringBuilder log = new StringBuilder();
        int[] argb;
        using (Bitmap src = new Bitmap(mapPath))
        {
            if (src.Width != src.Height) throw new Exception("the map picture is not square");
            N = src.Width;
            argb = new int[N * N];
            using (Bitmap map = new Bitmap(N, N, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(map)) g.DrawImage(src, 0, 0, N, N);
                BitmapData d = map.LockBits(new Rectangle(0, 0, N, N), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                Marshal.Copy(d.Scan0, argb, 0, N * N);
                map.UnlockBits(d);
            }
        }

        // Metres per pixel, from the calibration: world cm per map unit is 1 / scale, and the picture is MapSize units wide.
        double metresPerPixel = cal.MapSize / (N * Math.Abs(cal.ScaleX)) / 100.0;
        double km2PerPixel = metresPerPixel * metresPerPixel / 1e6;
        int seaReach = (int)Math.Round(SeaLabelReach / metresPerPixel);
        int coastBand = (int)Math.Round(CoastalBand / metresPerPixel);
        int snap = (int)Math.Round(LabelSnap / metresPerPixel);

        // Sea = the colour of the map's corner, connected to the border (so lakes and rivers stay "land").
        int c0 = argb[3 * N + 3];
        int r0 = (c0 >> 16) & 255, g0 = (c0 >> 8) & 255, b0 = c0 & 255;
        bool[] navy = new bool[N * N];
        for (int i = 0; i < N * N; i++)
        {
            int r = (argb[i] >> 16) & 255, g = (argb[i] >> 8) & 255, b = argb[i] & 255;
            int dr = r - r0, dg = g - g0, db = b - b0;
            navy[i] = dr * dr + dg * dg + db * db < SeaTolerance * SeaTolerance;
        }
        bool[] sea = new bool[N * N];
        Queue<int> q = new Queue<int>();
        for (int t = 0; t < N; t++)
        {
            int[] edge = { t, (N - 1) * N + t, t * N, t * N + N - 1 };
            foreach (int e in edge) if (navy[e] && !sea[e]) { sea[e] = true; q.Enqueue(e); }
        }
        while (q.Count > 0)
        {
            int i = q.Dequeue(); int x = i % N, y = i / N;
            if (x > 0 && navy[i - 1] && !sea[i - 1]) { sea[i - 1] = true; q.Enqueue(i - 1); }
            if (x < N - 1 && navy[i + 1] && !sea[i + 1]) { sea[i + 1] = true; q.Enqueue(i + 1); }
            if (y > 0 && navy[i - N] && !sea[i - N]) { sea[i - N] = true; q.Enqueue(i - N); }
            if (y < N - 1 && navy[i + N] && !sea[i + N]) { sea[i + N] = true; q.Enqueue(i + N); }
        }
        bool[] land = new bool[N * N];
        int landCount = 0;
        for (int i = 0; i < N * N; i++) { land[i] = !sea[i]; if (land[i]) landCount++; }
        log.AppendLine(string.Format("map {0} px, {1:0.00} m per pixel; sea colour #{2:X2}{3:X2}{4:X2}; land {5} px ({6:0.0} km2)",
            N, metresPerPixel, r0, g0, b0, landCount, landCount * km2PerPixel));

        // Seeds. A label's pixel is found exactly as the overlay finds yours: floor(fraction x size).
        double[] speed = new double[labels.Length];
        HashSet<string> usedColours = new HashSet<string>();
        List<int> landSeeds = new List<int>(), landLabs = new List<int>(), seaSeeds = new List<int>(), seaLabs = new List<int>();
        for (int k = 0; k < labels.Length; k++)
        {
            AreaLabel a = labels[k];
            string hex = ColourFor(k, usedColours);
            usedColours.Add(hex);
            a.Colour = "#" + hex;
            a.Px = (int)Math.Floor((cal.OffsetX + a.X * cal.ScaleX + cal.PinOffsetX) / cal.MapSize * N);
            a.Py = (int)Math.Floor((1 - (cal.OffsetY + a.Y * cal.ScaleY + cal.PinOffsetY) / cal.MapSize) * N);
            a.Px = Math.Max(0, Math.Min(N - 1, a.Px)); a.Py = Math.Max(0, Math.Min(N - 1, a.Py));
            speed[k] = a.W;
            bool onSea = sea[a.Py * N + a.Px];
            int sx = a.Px, sy = a.Py;
            if (a.Ocean) { a.SeaType = true; if (!onSea) Nearest(sea, a.Px, a.Py, snap, out sx, out sy); }
            else if (onSea)
            {
                // An offshore label of a land area: its land from the nearest shore, plus the water around the label.
                a.SeaType = !Nearest(land, a.Px, a.Py, snap, out sx, out sy);
                if (!a.SeaType) { seaSeeds.Add(a.Py * N + a.Px); seaLabs.Add(k); }
            }
            a.Sx = sx; a.Sy = sy;
            if (a.SeaType) { seaSeeds.Add(sy * N + sx); seaLabs.Add(k); } else { landSeeds.Add(sy * N + sx); landLabs.Add(k); }
        }

        int[] owner = new int[N * N], lab = new int[N * N];
        float[] cst = new float[N * N];

        // A: land, from the land labels (weighted).
        Spread(land, landSeeds, landLabs, speed, float.MaxValue, lab, cst);
        for (int i = 0; i < N * N; i++) owner[i] = land[i] ? lab[i] : -1;

        // B: sea labels claim the water around them, up to their reach x weight.
        Spread(sea, seaSeeds, seaLabs, speed, seaReach, lab, cst);
        for (int i = 0; i < N * N; i++) if (sea[i] && lab[i] >= 0) owner[i] = lab[i];

        // C: islets no land label reached take the nearest claimed pixel's area.
        bool[] all = new bool[N * N];
        for (int i = 0; i < N * N; i++) all[i] = true;
        List<int> s1 = new List<int>(), l1 = new List<int>();
        for (int i = 0; i < N * N; i++) if (owner[i] >= 0) { s1.Add(i); l1.Add(owner[i]); }
        Spread(all, s1, l1, null, float.MaxValue, lab, cst);
        for (int i = 0; i < N * N; i++) if (land[i] && owner[i] < 0) owner[i] = lab[i];

        // D: a band of coastal water belongs to the land beside it; open sea stays empty.
        s1.Clear(); l1.Clear();
        for (int i = 0; i < N * N; i++) if (land[i] && owner[i] >= 0) { s1.Add(i); l1.Add(owner[i]); }
        Spread(sea, s1, l1, null, coastBand, lab, cst);
        for (int i = 0; i < N * N; i++) if (sea[i] && owner[i] < 0 && lab[i] >= 0) owner[i] = lab[i];

        for (int i = 0; i < N * N; i++) if (owner[i] >= 0) labels[owner[i]].Pixels++;

        Color[] colours = new Color[labels.Length];
        for (int k = 0; k < labels.Length; k++) colours[k] = ColorTranslator.FromHtml(labels[k].Colour);

        // The colour-coded map: flat colours, hard edges, transparent where no area.
        int[] outPx = new int[N * N];
        for (int i = 0; i < N * N; i++) outPx[i] = owner[i] < 0 ? 0 : colours[owner[i]].ToArgb();
        SaveArgb(outPx, outPng);

        for (int k = 0; k < labels.Length; k++)
        {
            AreaLabel a = labels[k];
            log.AppendLine(string.Format("{0,-22} {1}  label px ({2},{3})  seed ({4},{5})  {6,-4}  w {7:0.0}  {8,6} px  {9,5:0.0} km2",
                a.Name, a.Colour, a.Px, a.Py, a.Sx, a.Sy, a.SeaType ? "sea" : "land", a.W, a.Pixels, a.Pixels * km2PerPixel));
        }
        return log.ToString();
    }
}
'@
Add-Type -TypeDefinition $code -ReferencedAssemblies System.Drawing

$source = Get-Content (Resolve-Path $Labels) -Raw -Encoding UTF8 | ConvertFrom-Json
$cal = New-Object MapCal
foreach ($field in 'OffsetX', 'OffsetY', 'ScaleX', 'ScaleY', 'MapSize', 'PinOffsetX', 'PinOffsetY') { $cal.$field = [double]$source.Calibration.$field }
$labelList = New-Object System.Collections.Generic.List[AreaLabel]
foreach ($l in $source.Labels) {
    $a = New-Object AreaLabel
    $a.Name = $l.Name
    $a.X = [double]$l.X
    $a.Y = [double]$l.Y
    $a.Ocean = [bool]$l.Sea
    $a.W = switch ($l.Size) { 'large' { 1.3 } 'small' { 0.7 } default { 1.0 } }
    $labelList.Add($a)
}
$areas = $labelList.ToArray()

$outDirFull = (Resolve-Path $OutDir).Path
$log = [AreaMapGenerator]::Run((Resolve-Path $Map).Path, $cal, $areas, (Join-Path $outDirFull 'areas.png'))

# The legend the overlay reads beside the image: which colour is which area, where its
# label point is (world cm - the same coordinates as a waypoint), and the calibration
# the label points were placed with.
$inv = [Globalization.CultureInfo]::InvariantCulture
function Num([double]$value) { $value.ToString('R', $inv) }
function Text([string]$value) { ($value -replace '\\', '\\') -replace '"', '\"' }
$rows = foreach ($a in $areas) {
    '    {{ "Name": "{0}", "Colour": "{1}", "X": {2}, "Y": {3} }}' -f (Text $a.Name), $a.Colour, (Num $a.X), (Num $a.Y)
}
$calJson = '{{ "OffsetX": {0}, "OffsetY": {1}, "ScaleX": {2}, "ScaleY": {3}, "MapSize": {4}, "PinOffsetX": {5}, "PinOffsetY": {6} }}' -f `
    (Num $cal.OffsetX), (Num $cal.OffsetY), (Num $cal.ScaleX), (Num $cal.ScaleY), (Num $cal.MapSize), (Num $cal.PinOffsetX), (Num $cal.PinOffsetY)
$json = "{`n" +
    ('  "Source": "Area names: {0} Borders: computed by tools/area-map from the label points and the island map; they are the overlay''s own, not from any source.",' -f (Text $source.Source)) + "`n" +
    ('  "CopiedOn": "{0}",' -f (Text $source.CopiedOn)) + "`n" +
    ('  "GeneratedOn": "{0}",' -f (Get-Date -Format 'yyyy-MM-dd')) + "`n" +
    ('  "Calibration": {0},' -f $calJson) + "`n" +
    '  "Areas": [' + "`n" + ($rows -join ",`n") + "`n  ]`n}`n"
[IO.File]::WriteAllText((Join-Path $outDirFull 'areas.json'), $json, (New-Object Text.UTF8Encoding $false))
$log

# The picture for people is drawn from the two files just written, by the script that
# can also redraw it alone (after corrections were painted, when this one must not run).
if ($PreviewPath) {
    & "$PSScriptRoot\preview-area-map.ps1" -Out $PreviewPath -Map (Resolve-Path $Map).Path -AreaDir $outDirFull -Labels (Resolve-Path $Labels).Path
}

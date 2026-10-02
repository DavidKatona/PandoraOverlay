# Generates the overlay's AREA MAP: Assets/areas.png + Assets/areas.json (+ Assets/land.png, the land mask).
#
# The map answers "which area am I in": an image the size of Assets/map.png with one
# flat colour per named area and nothing else (transparent = no area, open sea). The
# overlay reads it as data - your position, through the same transform as the arrow,
# picks a pixel; the pixel's colour is the area (Minimap/AreaMap.cs).
#
# The NAMES are VulnonaMAP's 26 Gateway area labels (area-labels.json: one point each,
# plus a size hint). The BORDERS are not from any source - nobody publishes them and
# none are drawn on the map - so they are computed here:
#   * sea = the map's navy, connected to the image border (lakes and rivers stay land);
#   * every land pixel goes to the label that reaches it soonest travelling OVER LAND
#     ("large" labels spread 1.3x as fast, "small" ones 0.7x), so an area never jumps a bay;
#   * sea labels claim the water around them (about 1.1 km x the same factor);
#   * a land area whose label sits offshore takes the nearest shore as its land and
#     also claims the water around the label;
#   * islets nothing reached take the nearest claimed area; a 375 m band of coastal
#     water follows the land beside it; open sea stays empty.
#
# Corrections are meant to be PAINTED: edit Assets/areas.png with the colours listed in
# Assets/areas.json (hard edges, no anti-aliasing, save as PNG). Re-running this script
# OVERWRITES such corrections - it is the starting point, not a build step.
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
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

public class AreaLabel
{
    public string Name; public double Lat; public double Long; public double W; public bool Ocean;
    public string Colour; public int Px; public int Py; public int Sx; public int Sy; public bool SeaType; public int Pixels;
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
    const int N = 1000;

    // The site's map calibration (POST /api/map/calibration, Sep 2026) - the transform the
    // overlay's arrow uses. World cm -> map fraction; the pin offset is part of it.
    const double OffsetX = 1160.9249840132136, OffsetY = 1223.2852629424794;
    const double ScaleX = 0.0020010632626191725, ScaleY = -0.0020003567492836005;
    const double MapSize = 2500, PinX = -15, PinY = 25;

    // Green-Armytage's 26 "alphabet" colours, built to be told apart (Ebony swapped for a light grey).
    static readonly string[] Palette = {
        "F0A3FF","0075DC","993F00","4C005C","FFE100","005C31","2BCE48","FFCC99","808080","94FFB5","8F7C00","9DCC00","C20088",
        "003380","FFA405","FFA8BB","426600","FF0010","5EF1F2","00998F","E0FF66","740AFF","990000","FFFF80","DDDDDD","FF5005" };

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

    public static string Run(string mapPath, AreaLabel[] labels, string outPng, string outLand, string previewPath)
    {
        if (labels.Length > Palette.Length) throw new Exception("more labels than palette colours");
        StringBuilder log = new StringBuilder();
        int[] argb = new int[N * N];
        using (Bitmap src = new Bitmap(mapPath))
        using (Bitmap map = new Bitmap(N, N, PixelFormat.Format32bppArgb))
        {
            if (src.Width != N || src.Height != N) throw new Exception("map is not 1000x1000");
            using (Graphics g = Graphics.FromImage(map)) g.DrawImage(src, 0, 0, N, N);
            BitmapData d = map.LockBits(new Rectangle(0, 0, N, N), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(d.Scan0, argb, 0, N * N);
            map.UnlockBits(d);
        }

        // Sea = the navy of the map's corner, connected to the border (so teal lakes and rivers stay "land").
        int c0 = argb[3 * N + 3];
        int r0 = (c0 >> 16) & 255, g0 = (c0 >> 8) & 255, b0 = c0 & 255;
        bool[] navy = new bool[N * N];
        for (int i = 0; i < N * N; i++)
        {
            int r = (argb[i] >> 16) & 255, g = (argb[i] >> 8) & 255, b = argb[i] & 255;
            int dr = r - r0, dg = g - g0, db = b - b0;
            navy[i] = dr * dr + dg * dg + db * db < 38 * 38;
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
        log.AppendLine(string.Format("sea colour #{0:X2}{1:X2}{2:X2}; land pixels {3} ({4:0.0} km2)", r0, g0, b0, landCount, landCount * 156.25 / 1e6));

        // The land mask (white = land, lakes and rivers included; black = sea): the overlay's border
        // layer draws a border only where both sides are land, so its lines end at the coast.
        int[] landPx = new int[N * N];
        for (int i = 0; i < N * N; i++) landPx[i] = land[i] ? unchecked((int)0xFFFFFFFF) : unchecked((int)0xFF000000);
        using (Bitmap lm = new Bitmap(N, N, PixelFormat.Format32bppArgb))
        {
            BitmapData ld = lm.LockBits(new Rectangle(0, 0, N, N), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(landPx, 0, ld.Scan0, N * N);
            lm.UnlockBits(ld);
            lm.Save(outLand, ImageFormat.Png);
        }

        // Seeds. A label's pixel is found exactly as the overlay finds yours: floor(fraction x size).
        double[] speed = new double[labels.Length];
        List<int> landSeeds = new List<int>(), landLabs = new List<int>(), seaSeeds = new List<int>(), seaLabs = new List<int>();
        for (int k = 0; k < labels.Length; k++)
        {
            AreaLabel a = labels[k];
            a.Colour = "#" + Palette[k];
            double wx = a.Long * 1000, wy = a.Lat * 1000;
            a.Px = (int)Math.Floor((OffsetX + wx * ScaleX + PinX) / MapSize * N);
            a.Py = (int)Math.Floor((1 - (OffsetY + wy * ScaleY + PinY) / MapSize) * N);
            a.Px = Math.Max(0, Math.Min(N - 1, a.Px)); a.Py = Math.Max(0, Math.Min(N - 1, a.Py));
            speed[k] = a.W;
            bool onSea = sea[a.Py * N + a.Px];
            int sx = a.Px, sy = a.Py;
            if (a.Ocean) { a.SeaType = true; if (!onSea) Nearest(sea, a.Px, a.Py, 60, out sx, out sy); }
            else if (onSea)
            {
                // An offshore label of a land area: its land from the nearest shore, plus the water around the label.
                a.SeaType = !Nearest(land, a.Px, a.Py, 60, out sx, out sy);
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

        // B: sea labels claim the water around them, up to ~1.1 km x weight.
        Spread(sea, seaSeeds, seaLabs, speed, 90f, lab, cst);
        for (int i = 0; i < N * N; i++) if (sea[i] && lab[i] >= 0) owner[i] = lab[i];

        // C: islets no land label reached take the nearest claimed pixel's area.
        bool[] all = new bool[N * N];
        for (int i = 0; i < N * N; i++) all[i] = true;
        List<int> s1 = new List<int>(), l1 = new List<int>();
        for (int i = 0; i < N * N; i++) if (owner[i] >= 0) { s1.Add(i); l1.Add(owner[i]); }
        Spread(all, s1, l1, null, float.MaxValue, lab, cst);
        for (int i = 0; i < N * N; i++) if (land[i] && owner[i] < 0) owner[i] = lab[i];

        // D: a 375 m band of coastal water belongs to the land beside it; open sea stays empty.
        s1.Clear(); l1.Clear();
        for (int i = 0; i < N * N; i++) if (land[i] && owner[i] >= 0) { s1.Add(i); l1.Add(owner[i]); }
        Spread(sea, s1, l1, null, 30f, lab, cst);
        for (int i = 0; i < N * N; i++) if (sea[i] && owner[i] < 0 && lab[i] >= 0) owner[i] = lab[i];

        for (int i = 0; i < N * N; i++) if (owner[i] >= 0) labels[owner[i]].Pixels++;

        Color[] colours = new Color[labels.Length];
        for (int k = 0; k < labels.Length; k++) colours[k] = ColorTranslator.FromHtml(labels[k].Colour);

        // The colour-coded map: flat colours, hard edges, transparent where no area.
        int[] outPx = new int[N * N];
        for (int i = 0; i < N * N; i++) outPx[i] = owner[i] < 0 ? 0 : colours[owner[i]].ToArgb();
        using (Bitmap id = new Bitmap(N, N, PixelFormat.Format32bppArgb))
        {
            BitmapData d = id.LockBits(new Rectangle(0, 0, N, N), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(outPx, 0, d.Scan0, N * N);
            id.UnlockBits(d);
            id.Save(outPng, ImageFormat.Png);
            if (!string.IsNullOrEmpty(previewPath)) SavePreview(mapPath, id, owner, labels, colours, previewPath);
        }

        for (int k = 0; k < labels.Length; k++)
        {
            AreaLabel a = labels[k];
            log.AppendLine(string.Format("{0,-22} {1}  label px ({2},{3})  seed ({4},{5})  {6,-4}  w {7:0.0}  {8,6} px  {9,5:0.0} km2",
                a.Name, a.Colour, a.Px, a.Py, a.Sx, a.Sy, a.SeaType ? "sea" : "land", a.W, a.Pixels, a.Pixels * 156.25 / 1e6));
        }
        return log.ToString();
    }

    // The map with the areas over it, their borders, names and a legend - for people, not for the overlay.
    static void SavePreview(string mapPath, Bitmap id, int[] owner, AreaLabel[] labels, Color[] colours, string path)
    {
        const int S = 2, LegendW = 470;
        using (Bitmap pv = new Bitmap(N * S + LegendW, N * S, PixelFormat.Format32bppArgb))
        using (Bitmap src = new Bitmap(mapPath))
        using (Graphics g = Graphics.FromImage(pv))
        {
            g.Clear(Color.FromArgb(0x16, 0x1C, 0x23));
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, new Rectangle(0, 0, N * S, N * S));
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            ColorMatrix cm = new ColorMatrix(); cm.Matrix33 = 0.5f;
            using (ImageAttributes ia = new ImageAttributes())
            {
                ia.SetColorMatrix(cm);
                g.DrawImage(id, new Rectangle(0, 0, N * S, N * S), 0, 0, N, N, GraphicsUnit.Pixel, ia);
            }
            using (SolidBrush edge = new SolidBrush(Color.FromArgb(200, 10, 12, 16)))
            {
                for (int y = 0; y < N - 1; y++)
                for (int x = 0; x < N - 1; x++)
                {
                    int o = owner[y * N + x];
                    if (o != owner[y * N + x + 1] || o != owner[(y + 1) * N + x]) g.FillRectangle(edge, x * S + 1, y * S + 1, S, S);
                }
            }
            g.PixelOffsetMode = PixelOffsetMode.Default;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (FontFamily ff = new FontFamily("Segoe UI"))
            using (Pen outline = new Pen(Color.FromArgb(230, 0, 0, 0), 4f))
            using (StringFormat centre = new StringFormat())
            {
                outline.LineJoin = LineJoin.Round;
                centre.Alignment = StringAlignment.Center; centre.LineAlignment = StringAlignment.Center;
                for (int k = 0; k < labels.Length; k++)
                {
                    AreaLabel a = labels[k];
                    g.FillEllipse(Brushes.White, a.Px * S - 4, a.Py * S - 4, 8, 8);
                    g.DrawEllipse(Pens.Black, a.Px * S - 4, a.Py * S - 4, 8, 8);
                    using (GraphicsPath p = new GraphicsPath())
                    {
                        p.AddString(a.Name, ff, (int)FontStyle.Bold, 21f, new PointF(a.Px * S, a.Py * S - 20), centre);
                        g.DrawPath(outline, p);
                        g.FillPath(Brushes.White, p);
                    }
                }
                using (Font title = new Font(ff, 17f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Font row = new Font(ff, 16f, FontStyle.Regular, GraphicsUnit.Pixel))
                using (Font small = new Font(ff, 13f, FontStyle.Regular, GraphicsUnit.Pixel))
                using (SolidBrush text = new SolidBrush(Color.FromArgb(0xEC, 0xF2, 0xF8)))
                using (SolidBrush dim = new SolidBrush(Color.FromArgb(0x9A, 0xA7, 0xB0)))
                {
                    int lx = N * S + 24, ly = 28;
                    g.DrawString("PANDORA OVERLAY - AREAS", title, text, lx, ly); ly += 28;
                    g.DrawString("Names: VulnonaMAP's Gateway labels (white dots).", small, dim, lx, ly); ly += 18;
                    g.DrawString("Borders: computed by the overlay's generator.", small, dim, lx, ly); ly += 34;
                    for (int k = 0; k < labels.Length; k++)
                    {
                        AreaLabel a = labels[k];
                        using (SolidBrush sw = new SolidBrush(colours[k])) g.FillRectangle(sw, lx, ly + 2, 26, 18);
                        g.DrawRectangle(Pens.Black, lx, ly + 2, 26, 18);
                        g.DrawString(a.Name, row, text, lx + 36, ly);
                        g.DrawString(string.Format("{0}  {1:0.0} km2", a.Colour, a.Pixels * 156.25 / 1e6), small, dim, lx + 36, ly + 20);
                        ly += 46;
                    }
                }
            }
            if (path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            {
                // Half size as a JPEG: small enough for the README.
                using (Bitmap half = new Bitmap(pv.Width / 2, pv.Height / 2, PixelFormat.Format24bppRgb))
                {
                    using (Graphics hg = Graphics.FromImage(half))
                    {
                        hg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        hg.DrawImage(pv, new Rectangle(0, 0, half.Width, half.Height));
                    }
                    ImageCodecInfo jpeg = null;
                    foreach (ImageCodecInfo c in ImageCodecInfo.GetImageEncoders()) if (c.MimeType == "image/jpeg") jpeg = c;
                    using (EncoderParameters ep = new EncoderParameters(1))
                    {
                        ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 86L);
                        half.Save(path, jpeg, ep);
                    }
                }
            }
            else
            {
                pv.Save(path, ImageFormat.Png);
            }
        }
    }
}
'@
Add-Type -TypeDefinition $code -ReferencedAssemblies System.Drawing

$source = Get-Content (Resolve-Path $Labels) -Raw -Encoding UTF8 | ConvertFrom-Json
$labelList = New-Object System.Collections.Generic.List[AreaLabel]
foreach ($l in $source.Labels) {
    $a = New-Object AreaLabel
    $a.Name = $l.Name
    $a.Lat = [double]$l.Lat
    $a.Long = [double]$l.Long
    $a.Ocean = [bool]$l.Sea
    $a.W = switch ($l.Size) { 'large' { 1.3 } 'small' { 0.7 } default { 1.0 } }
    $labelList.Add($a)
}
$areas = $labelList.ToArray()

$outDirFull = (Resolve-Path $OutDir).Path
$preview = if ($PreviewPath) { [IO.Path]::GetFullPath((Join-Path (Get-Location) $PreviewPath)) } else { '' }
$log = [AreaMapGenerator]::Run((Resolve-Path $Map).Path, $areas, (Join-Path $outDirFull 'areas.png'), (Join-Path $outDirFull 'land.png'), $preview)

# The legend the overlay reads beside the image: which colour is which area, and where
# its label point is (world cm - the same coordinates as a waypoint).
$inv = [Globalization.CultureInfo]::InvariantCulture
$rows = foreach ($a in $areas) {
    '    {{ "Name": "{0}", "Colour": "{1}", "X": {2}, "Y": {3} }}' -f ($a.Name -replace '"', '\"'), $a.Colour,
        ($a.Long * 1000).ToString($inv), ($a.Lat * 1000).ToString($inv)
}
$json = "{`n" +
    '  "Source": "Area names: VulnonaMAP (vulnona.com), Gateway v0.21.772 labels. Borders: computed by tools/area-map from those 26 label points and the island map; they are the overlay''s own, not from any source.",' + "`n" +
    ('  "CopiedOn": "{0}",' -f $source.CopiedOn) + "`n" +
    ('  "GeneratedOn": "{0}",' -f (Get-Date -Format 'yyyy-MM-dd')) + "`n" +
    '  "Size": 1000,' + "`n" +
    '  "Areas": [' + "`n" + ($rows -join ",`n") + "`n  ]`n}`n"
[IO.File]::WriteAllText((Join-Path $outDirFull 'areas.json'), $json, (New-Object Text.UTF8Encoding $false))
$log

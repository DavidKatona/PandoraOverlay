# Draws the area map FOR PEOPLE (docs/area-map.jpg): the island map with the areas
# tinted over it, their borders, their names at the label points and a legend.
#
# It only READS what the overlay ships - Assets/map.png, Assets/areas.png and
# Assets/areas.json - and never writes the area map. So it is the way to redraw the
# picture after corrections were PAINTED into Assets/areas.png: the generator
# (make-area-map.ps1) would compute the map afresh and overwrite them. The generator's
# own -PreviewPath calls this script, so there is one picture, drawn in one place.
#
# Like the generator, nothing here is about one named area or one island: the areas,
# their colours and label points and the calibration all come from areas.json, and an
# area's size is counted from the image. area-labels.json is optional and only adds
# the "sea" tag to the legend.
#
# Usage (Windows PowerShell 5.1):   .\preview-area-map.ps1 [-Out docs\area-map.jpg] [-Hex] [-FullSize]
#   A .jpg is written at half size (small enough for the README) unless -FullSize; a .png is full size.
#   -FullSize, -Title and -Hex are for a one-off picture, such as a Discord post.
param(
    [string]$Out = "$PSScriptRoot\..\..\docs\area-map.jpg",
    [string]$Map = "$PSScriptRoot\..\..\Assets\map.png",
    [string]$AreaDir = "$PSScriptRoot\..\..\Assets",
    [string]$Labels = "$PSScriptRoot\area-labels.json",
    [string]$Title = 'PANDORA OVERLAY - AREAS',
    [string]$Sub1 = 'Names: the label points (white dots).',
    [string]$Sub2 = "Borders: the overlay's own estimate.",
    [switch]$Hex,
    [switch]$FullSize,
    [int]$JpegQuality = 86
)
$ErrorActionPreference = 'Stop'

$code = @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

public static class AreaMapPreview
{
    const int Side = 2000;                    // the picture's map side, whatever the map's size
    const int LegendW = 470;

    // labelX / labelY: the label points as map fractions (0-1). Returns a short report.
    public static string Render(string mapPath, string areasPath, string outPath, string[] names, string[] hexes,
        double[] labelX, double[] labelY, bool[] sea, double km2PerPixel,
        string titleText, string sub1, string sub2, bool showHex, bool half, long jpegQuality)
    {
        int n;
        int[] px;
        using (Bitmap id = new Bitmap(areasPath))
        {
            if (id.Width != id.Height) throw new Exception("the area map is not square");
            n = id.Width;
            BitmapData d = id.LockBits(new Rectangle(0, 0, n, n), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            px = new int[n * n];
            Marshal.Copy(d.Scan0, px, 0, px.Length);
            id.UnlockBits(d);
        }

        // Colour -> area by the overlay's own rule: fully opaque in a legend colour, anything else is no area.
        Dictionary<int, int> byColour = new Dictionary<int, int>();
        Color[] colours = new Color[names.Length];
        for (int k = 0; k < names.Length; k++)
        {
            int rgb = int.Parse(hexes[k].TrimStart('#'), NumberStyles.HexNumber);
            colours[k] = Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
            byColour[colours[k].ToArgb()] = k;
        }
        int[] owner = new int[n * n];
        int[] count = new int[names.Length];
        int unknown = 0;
        for (int i = 0; i < px.Length; i++)
        {
            int k;
            if (byColour.TryGetValue(px[i], out k)) { owner[i] = k; count[k]++; }
            else { owner[i] = -1; if (((px[i] >> 24) & 255) != 0) unknown++; }
        }

        float S = Side / (float)n;
        using (Bitmap pv = new Bitmap(Side + LegendW, Side, PixelFormat.Format32bppArgb))
        using (Bitmap src = new Bitmap(mapPath))
        using (Bitmap id = new Bitmap(areasPath))
        using (Graphics g = Graphics.FromImage(pv))
        {
            g.Clear(Color.FromArgb(0x16, 0x1C, 0x23));
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, new Rectangle(0, 0, Side, Side));
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            ColorMatrix cm = new ColorMatrix(); cm.Matrix33 = 0.5f;
            using (ImageAttributes ia = new ImageAttributes())
            {
                ia.SetColorMatrix(cm);
                g.DrawImage(id, new Rectangle(0, 0, Side, Side), 0, 0, n, n, GraphicsUnit.Pixel, ia);
            }
            using (SolidBrush edge = new SolidBrush(Color.FromArgb(200, 10, 12, 16)))
            {
                for (int y = 0; y < n - 1; y++)
                for (int x = 0; x < n - 1; x++)
                {
                    int o = owner[y * n + x];
                    if (o != owner[y * n + x + 1] || o != owner[(y + 1) * n + x]) g.FillRectangle(edge, x * S + S / 2, y * S + S / 2, S, S);
                }
            }
            g.PixelOffsetMode = PixelOffsetMode.Default;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (FontFamily ff = new FontFamily("Segoe UI"))
            using (Pen outline = new Pen(Color.FromArgb(230, 0, 0, 0), 4f))
            using (StringFormat centre = new StringFormat())
            {
                outline.LineJoin = LineJoin.Round;
                centre.Alignment = StringAlignment.Center; centre.LineAlignment = StringAlignment.Center;
                for (int k = 0; k < names.Length; k++)
                {
                    float lx = (float)(labelX[k] * Side), ly = (float)(labelY[k] * Side);
                    g.FillEllipse(Brushes.White, lx - 4, ly - 4, 8, 8);
                    g.DrawEllipse(Pens.Black, lx - 4, ly - 4, 8, 8);
                    using (GraphicsPath p = new GraphicsPath())
                    {
                        p.AddString(names[k], ff, (int)FontStyle.Bold, 21f, new PointF(lx, ly - 20), centre);
                        g.DrawPath(outline, p);
                        g.FillPath(Brushes.White, p);
                    }
                }
                // The legend: as many rows as there are areas, squeezed to fit the height if there are many.
                int rowHeight = Math.Max(18, Math.Min(46, (Side - 150) / names.Length));
                using (Font title = new Font(ff, 17f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Font row = new Font(ff, Math.Min(16f, rowHeight * 0.36f + 2), FontStyle.Regular, GraphicsUnit.Pixel))
                using (Font small = new Font(ff, 13f, FontStyle.Regular, GraphicsUnit.Pixel))
                using (SolidBrush text = new SolidBrush(Color.FromArgb(0xEC, 0xF2, 0xF8)))
                using (SolidBrush dim = new SolidBrush(Color.FromArgb(0x9A, 0xA7, 0xB0)))
                {
                    int x0 = Side + 24, y0 = 28;
                    g.DrawString(titleText, title, text, x0, y0); y0 += 28;
                    g.DrawString(sub1, small, dim, x0, y0); y0 += 18;
                    g.DrawString(sub2, small, dim, x0, y0); y0 += 34;
                    for (int k = 0; k < names.Length; k++)
                    {
                        using (SolidBrush sw = new SolidBrush(colours[k])) g.FillRectangle(sw, x0, y0 + 2, 26, Math.Min(18, rowHeight - 4));
                        g.DrawRectangle(Pens.Black, x0, y0 + 2, 26, Math.Min(18, rowHeight - 4));
                        g.DrawString(names[k], row, text, x0 + 36, y0);
                        if (rowHeight >= 40)
                        {
                            string detail = string.Format(CultureInfo.InvariantCulture, "{0:0.0} km\u00B2", count[k] * km2PerPixel);
                            if (showHex) detail = hexes[k].ToUpperInvariant() + "  " + detail;
                            if (sea[k]) detail += "  sea";
                            g.DrawString(detail, small, dim, x0 + 36, y0 + 20);
                        }
                        y0 += rowHeight;
                    }
                }
            }
            if (outPath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            {
                int w = half ? pv.Width / 2 : pv.Width, h = half ? pv.Height / 2 : pv.Height;
                using (Bitmap flat = new Bitmap(w, h, PixelFormat.Format24bppRgb))
                {
                    using (Graphics fg = Graphics.FromImage(flat))
                    {
                        fg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        fg.DrawImage(pv, new Rectangle(0, 0, w, h));
                    }
                    ImageCodecInfo jpeg = null;
                    foreach (ImageCodecInfo c in ImageCodecInfo.GetImageEncoders()) if (c.MimeType == "image/jpeg") jpeg = c;
                    using (EncoderParameters ep = new EncoderParameters(1))
                    {
                        ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, jpegQuality);
                        flat.Save(outPath, jpeg, ep);
                    }
                }
            }
            else
            {
                pv.Save(outPath, ImageFormat.Png);
            }
        }

        StringBuilder log = new StringBuilder();
        log.AppendLine(string.Format(CultureInfo.InvariantCulture, "area map {0} px, {1} areas, {2} px in a colour the legend does not list", n, names.Length, unknown));
        for (int k = 0; k < names.Length; k++)
            log.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-22} {1}  {2,6} px  {3,5:0.0} km2{4}", names[k], hexes[k], count[k], count[k] * km2PerPixel, sea[k] ? "  sea" : ""));
        return log.ToString();
    }
}
'@
if (-not ('AreaMapPreview' -as [type])) { Add-Type -TypeDefinition $code -ReferencedAssemblies System.Drawing }

$areaDirFull = (Resolve-Path $AreaDir).Path
$legend = Get-Content (Join-Path $areaDirFull 'areas.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$cal = $legend.Calibration

$seaByName = @{}
if ($Labels -and (Test-Path $Labels)) {
    foreach ($l in (Get-Content (Resolve-Path $Labels) -Raw -Encoding UTF8 | ConvertFrom-Json).Labels) { $seaByName[[string]$l.Name] = [bool]$l.Sea }
}

$names = @(); $hexes = @(); $fx = @(); $fy = @(); $sea = @()
foreach ($a in $legend.Areas) {
    $names += [string]$a.Name
    $hexes += [string]$a.Colour
    # World cm -> map fraction: the site's transform, the one the overlay's arrow uses (note the Y flip).
    $fx += [double](($cal.OffsetX + $a.X * $cal.ScaleX + $cal.PinOffsetX) / $cal.MapSize)
    $fy += [double](1 - ($cal.OffsetY + $a.Y * $cal.ScaleY + $cal.PinOffsetY) / $cal.MapSize)
    $sea += [bool]($seaByName.ContainsKey([string]$a.Name) -and $seaByName[[string]$a.Name])
}

$areasPng = Join-Path $areaDirFull 'areas.png'
$probe = New-Object System.Drawing.Bitmap $areasPng
$n = $probe.Width
$probe.Dispose()
$metresPerPixel = ($cal.MapSize / $n) / [math]::Abs($cal.ScaleX) / 100
$km2PerPixel = $metresPerPixel * $metresPerPixel / 1e6

$outFull = if ([IO.Path]::IsPathRooted($Out)) { [IO.Path]::GetFullPath($Out) } else { [IO.Path]::GetFullPath((Join-Path (Get-Location) $Out)) }
[AreaMapPreview]::Render((Resolve-Path $Map).Path, $areasPng, $outFull,
    [string[]]$names, [string[]]$hexes, [double[]]$fx, [double[]]$fy, [bool[]]$sea, $km2PerPixel,
    $Title, $Sub1, $Sub2, [bool]$Hex, (-not $FullSize), [long]$JpegQuality)
"picture: $outFull"

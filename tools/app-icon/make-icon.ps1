# Draws the overlay's app icon and writes Assets/app.ico.
#
# The design is concept #22, "Pandora frame", picked by the owner on Nov 15 2026
# from the icon canvas (https://claude.ai/artifact/Qw7EGhw7RELx18uCMN374s, round 2):
# the minimap's arrow in the Pandora theme's orange, two-tone, on a near-black
# square with tight corners and a full-width lit top edge, as the theme's own
# widget frames have.
#
# Drawn by WPF's own vector drawing at every size Windows asks for (16 to 256), not
# scaled down from one big picture: each size is rendered from the shapes at that
# size. Below 32 px the frame's line and the lit edge are held to one device pixel
# at least, so they don't fade to nothing in the tray. Each size is stored as a PNG
# entry in the ICO, as the old icon's were (Windows Vista and later read them;
# System.Drawing.Icon, which the tray uses, and WPF's Image both do).
#
# Run from the repo root in Windows PowerShell (STA, as it is by default):
#   powershell -NoProfile -File tools/app-icon/make-icon.ps1 [-Preview <folder>]
# -Preview also writes each size as a PNG into that folder, to look at.
# Makes no request and reads nothing but this script.
param([string]$Preview = "")

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName PresentationCore, WindowsBase

$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$out = Join-Path $repo "Assets\app.ico"
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256

function Brush([string]$hex) {
    $b = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.ColorConverter]::ConvertFromString($hex))
    $b.Freeze(); $b
}
function Geometry([string]$data) { [System.Windows.Media.Geometry]::Parse($data) }

$ground = Brush "#0B0B0E"
$frameLine = Brush "#3A3A42"
$orange = Brush "#FF7A1A"
$light = Brush "#FF9F57"

function Render([int]$px) {
    $s = $px / 256.0
    $visual = [System.Windows.Media.DrawingVisual]::new()
    $dc = $visual.RenderOpen()
    $dc.PushTransform([System.Windows.Media.ScaleTransform]::new($s, $s))

    # Widths in the 256 design, held to one device pixel at least when drawn small.
    $frameWidth = [Math]::Max(4.0, 1.0 / $s)
    $litWidth = [Math]::Max(7.0, 1.25 / $s)

    # The square: x 8..248, corner 24.
    $pen = [System.Windows.Media.Pen]::new($frameLine, $frameWidth)
    $dc.DrawRoundedRectangle($ground, $pen, [System.Windows.Rect]::new(8, 8, 240, 240), 24, 24)

    # The lit top edge, as the Pandora theme's frames have it.
    $lit = [System.Windows.Media.Pen]::new($orange, $litWidth)
    $lit.StartLineCap = "Round"; $lit.EndLineCap = "Round"
    $dc.DrawLine($lit, [System.Windows.Point]::new(30, 10), [System.Windows.Point]::new(226, 10))

    # The arrow, two-tone: the whole in orange, its left face lighter.
    $dc.DrawGeometry($orange, $null, (Geometry "M128,44 L200,214 L128,174 L56,214 Z"))
    $dc.DrawGeometry($light, $null, (Geometry "M128,44 L128,174 L56,214 Z"))

    $dc.Pop()
    $dc.Close()

    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($px, $px, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [System.IO.MemoryStream]::new()
    $encoder.Save($stream)
    , $stream.ToArray()
}

$pngs = @{}
foreach ($px in $sizes) { $pngs[$px] = Render $px }

if ($Preview) {
    New-Item -ItemType Directory -Force $Preview | Out-Null
    foreach ($px in $sizes) { [System.IO.File]::WriteAllBytes((Join-Path $Preview "app-$px.png"), $pngs[$px]) }
}

# The ICO: a 6-byte header, a 16-byte entry per size, then the PNGs.
$ms = [System.IO.MemoryStream]::new()
$w = [System.IO.BinaryWriter]::new($ms)
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($px in $sizes) {
    $dim = if ($px -ge 256) { 0 } else { $px }
    $w.Write([Byte]$dim); $w.Write([Byte]$dim)
    $w.Write([Byte]0); $w.Write([Byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$pngs[$px].Length); $w.Write([UInt32]$offset)
    $offset += $pngs[$px].Length
}
foreach ($px in $sizes) { $w.Write($pngs[$px]) }
$w.Flush()
[System.IO.File]::WriteAllBytes($out, $ms.ToArray())
"Wrote $out ($($ms.Length) bytes, sizes $($sizes -join ', '))"

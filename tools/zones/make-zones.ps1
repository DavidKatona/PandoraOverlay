# Converts VulnonaMAP's zone data into the map's ZONE LAYERS: Assets/zones.json.
#
# The map's Layers menu draws three kinds of zone (Minimap/ZoneSet.cs, ZoneAsset.cs,
# MapCanvas): where the game's migration zones, patrol zones and sanctuaries CAN be.
# Which one is active is decided inside the game, per species or per player, and
# nothing outside the game can know it, so the file holds only the shapes.
#
# The source is VulnonaMAP (https://vulnona.com/game/map/, community-made, by Coco.N
# and its map explorers): its plain-text map data, the sections Migration, PatrolZone
# and Sanctuary. Its terms: no licence ("how you handle it is up to you", a link back
# appreciated), the same basis as the waypoint packs and the area names. The website's
# own zone pictures (islapandora.eu/live-map) are NOT the source: they are not cleared
# for use, and in Oct 2026 they were older than the game's layout.
#
# THIS SCRIPT MAKES NO REQUEST: download the data file by hand first. Vulnona's index,
# https://vulnona.com/game/map/dat.txt, lists each map dataset with the game versions
# it covers and its date; a dataset's data is map/<folder>/data_1.txt, for example
# https://vulnona.com/game/map/map/Gateway_v0.21.7/data_1.txt
#
# Vulnona's format (tab-separated text): "dir<TAB>Name" opens a section and "dirEnd"
# closes it. In a section, a "line" record is a closed polygon, one "lat,long," vertex
# per line, and a "circle" record is one "lat,long,a,b,deg" line; "#---" separates
# records. The 4th field of a record's header line holds its tags ("mmz" = a mass
# migration). World cm = value x 1000, Long -> X, Lat -> Y: the conversion the packs
# and the area labels use.
#
# It fails rather than guess: an unknown record, an OVAL circle (a <> b: Vulnona's
# rotation convention was never checked against the game, and no zone was oval in
# Oct 2026), a polygon under 3 points, a mass tag outside Migration, or any point
# outside the map picture (checked with the bundled calibration seed,
# Assets/calibration.json).
#
# EVERYTHING ABOUT THE ZONES IS INPUT, NOT CODE (the owner's map-agnostic rule, as for
# the area map): the overlay knows three kinds and nothing about where they are. A new
# game version or a new map is a new data file and a run of this script. Vulnona's
# record labels ("[01] :mz") are dropped, so no zone is named anywhere.
# The output is deterministic (the file's order, whole cm, LF, UTF-8 without BOM): a
# re-run over the same input changes nothing. -PreviewPath draws the zones over
# Assets/map.png for a look by eye; the drawing lives only here.
#
# Usage (Windows PowerShell 5.1):
#   .\make-zones.ps1 -In data_1.txt -Dataset "Gateway v0.21.772" -GameVersions "0.21.720-0.21.772" `
#       -SourceFile https://vulnona.com/game/map/map/Gateway_v0.21.7/data_1.txt -SourceUpdated 2026-09-21 `
#       [-CopiedOn 2026-10-12] [-PreviewPath zones-preview.png]
param(
    [Parameter(Mandatory = $true)][string]$In,
    [Parameter(Mandatory = $true)][string]$Dataset,
    [Parameter(Mandatory = $true)][string]$GameVersions,
    [Parameter(Mandatory = $true)][string]$SourceFile,
    [Parameter(Mandatory = $true)][string]$SourceUpdated,
    [string]$CopiedOn = (Get-Date -Format 'yyyy-MM-dd'),
    [string]$Out = '',
    [string]$Calibration = '',
    [string]$Map = '',
    [string]$PreviewPath = ''
)
$ErrorActionPreference = 'Stop'
$inv = [Globalization.CultureInfo]::InvariantCulture
# The defaults are resolved here: $PSScriptRoot can be empty in a param block under "powershell -File".
$assets = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\..\Assets'
if ($Out -eq '') { $Out = Join-Path $assets 'zones.json' }
if ($Calibration -eq '') { $Calibration = Join-Path $assets 'calibration.json' }
if ($Map -eq '') { $Map = Join-Path $assets 'map.png' }

# Vulnona's section names -> the overlay's zone kinds. Other sections are ignored.
$kinds = @{ 'Migration' = 'Migration'; 'PatrolZone' = 'Patrol'; 'Sanctuary' = 'Sanctuary' }

$seed = Get-Content $Calibration -Raw -Encoding UTF8 | ConvertFrom-Json
function Fraction([double]$x, [double]$y) {
    # MapCalibration.ToFraction without the clamp: a point off the picture must show as off it.
    $fx = ($seed.offsetX + $x * $seed.scaleX + $seed.pinOffset.x) / $seed.mapSize
    $fy = 1 - ($seed.offsetY + $y * $seed.scaleY + $seed.pinOffset.y) / $seed.mapSize
    return , @($fx, $fy)
}
function Fail([string]$why) { throw "make-zones: $why" }
function OnPicture([double]$x, [double]$y, [string]$what) {
    $f = Fraction $x $y
    if ($f[0] -lt 0 -or $f[0] -gt 1 -or $f[1] -lt 0 -or $f[1] -gt 1) { Fail "$what at ($x, $y) lies outside the map picture" }
}
function Cm([string]$value) { return [long][Math]::Round([double]::Parse($value, $inv) * 1000) }

$zones = New-Object System.Collections.Generic.List[object]
$section = $null; $record = $null; $tags = @(); $points = $null; $lineNo = 0

function Finish {
    if ($null -eq $script:record) { return }
    if ($script:record -eq 'line') {
        $pts = $script:points
        if ($pts.Count -gt 1 -and $pts[0][0] -eq $pts[$pts.Count - 1][0] -and $pts[0][1] -eq $pts[$pts.Count - 1][1]) { $pts.RemoveAt($pts.Count - 1) } # the closing repeat
        if ($pts.Count -lt 3) { Fail "a polygon in $($script:section) ending near line $($script:lineNo) has $($pts.Count) points" }
        $script:zones.Add([pscustomobject]@{ Kind = $kinds[$script:section]; Mass = $script:tags -contains 'mmz'; Polygon = $pts.ToArray(); Circle = $null })
    }
    elseif ($null -eq $script:points) { Fail "a circle in $($script:section) ending near line $($script:lineNo) has no centre line" }
    $script:record = $null
}

foreach ($raw in Get-Content $In -Encoding UTF8) {
    $lineNo++
    $line = $raw.Trim()
    if ($line -eq '') { continue }
    $f = $line -split "`t"
    if ($f[0] -eq 'dir') { Finish; $section = $f[1]; continue }
    if ($f[0] -eq 'dirEnd') { Finish; $section = $null; continue }
    if ($null -eq $section -or -not $kinds.ContainsKey($section)) { continue }
    if ($line.StartsWith('#')) { Finish; continue }
    if ($f[0] -eq 'line' -or $f[0] -eq 'circle') {
        Finish
        $record = $f[0]
        $tags = if ($f.Count -gt 3) { @($f[3] -split ' ' | Where-Object { $_ -ne '' }) } else { @() }
        if ($tags -contains 'mmz' -and $section -ne 'Migration') { Fail "line ${lineNo}: a mass tag outside Migration" }
        $points = if ($record -eq 'line') { New-Object System.Collections.Generic.List[object] } else { $null }
        continue
    }
    if ($null -eq $record) { Fail "line ${lineNo}: '$line' is not a record Vulnona's zone sections are known to hold" }
    $n = $line.TrimEnd(',') -split ','
    if ($record -eq 'line') {
        if ($n.Count -ne 2) { Fail "line ${lineNo}: a polygon vertex should be 'lat,long'" }
        $x = Cm $n[1]; $y = Cm $n[0]
        OnPicture $x $y "a vertex (line $lineNo)"
        $last = if ($points.Count -gt 0) { $points[$points.Count - 1] } else { $null }
        if ($null -eq $last -or $last[0] -ne $x -or $last[1] -ne $y) { $points.Add(@($x, $y)) } # a repeated vertex adds nothing
    }
    else {
        if ($n.Count -ne 5) { Fail "line ${lineNo}: a circle should be 'lat,long,a,b,deg'" }
        $x = Cm $n[1]; $y = Cm $n[0]; $a = Cm $n[2]; $b = Cm $n[3]
        if ($a -ne $b) { Fail "line ${lineNo}: an oval (a <> b); check Vulnona's rotation convention against the game before converting ovals" }
        if ($a -le 0) { Fail "line ${lineNo}: a circle without a radius" }
        OnPicture ($x - $a) ($y - $a) "a circle's edge (line $lineNo)"
        OnPicture ($x + $a) ($y + $a) "a circle's edge (line $lineNo)"
        $zones.Add([pscustomobject]@{ Kind = $kinds[$section]; Mass = $tags -contains 'mmz'; Polygon = $null; Circle = @($x, $y, $a) })
        $points = @($x, $y)
        $record = $null
    }
}
Finish
if ($zones.Count -eq 0) { Fail "no zones found in $In" }

# ---- Write: one zone per line, so a refresh reads as a diff ---------------------------
function JsonString([string]$s) { return '"' + $s.Replace('\', '\\').Replace('"', '\"') + '"' }
$sb = New-Object Text.StringBuilder
[void]$sb.Append("{`n")
[void]$sb.Append('  "Format": 1,' + "`n")
[void]$sb.Append('  "Source": "VulnonaMAP",' + "`n")
[void]$sb.Append('  "SourceLink": "https://vulnona.com/game/map/",' + "`n")
[void]$sb.Append('  "SourceFile": ' + (JsonString $SourceFile) + ",`n")
[void]$sb.Append('  "Dataset": ' + (JsonString $Dataset) + ",`n")
[void]$sb.Append('  "GameVersions": ' + (JsonString $GameVersions) + ",`n")
[void]$sb.Append('  "SourceUpdated": ' + (JsonString $SourceUpdated) + ",`n")
[void]$sb.Append('  "CopiedOn": ' + (JsonString $CopiedOn) + ",`n")
[void]$sb.Append('  "Credit": "Zone data: VulnonaMAP (vulnona.com/game/map), community-made by Coco.N and its map explorers.",' + "`n")
[void]$sb.Append('  "Note": "Where the zones CAN be, in world cm (Long x 1000 = X, Lat x 1000 = Y). Which one is active is decided inside the game. Converted by tools/zones/make-zones.ps1; never edited by hand.",' + "`n")
[void]$sb.Append('  "Zones": [' + "`n")
for ($i = 0; $i -lt $zones.Count; $i++) {
    $z = $zones[$i]
    $mass = if ($z.Mass) { '"Mass": true, ' } else { '' }
    $shape = if ($null -ne $z.Circle) {
        '"Circle": { "X": ' + $z.Circle[0] + ', "Y": ' + $z.Circle[1] + ', "R": ' + $z.Circle[2] + ' }'
    } else {
        '"Polygon": [' + (($z.Polygon | ForEach-Object { '[' + $_[0] + ',' + $_[1] + ']' }) -join ',') + ']'
    }
    $comma = if ($i -lt $zones.Count - 1) { ',' } else { '' }
    [void]$sb.Append('    { "Kind": "' + $z.Kind + '", ' + $mass + $shape + ' }' + $comma + "`n")
}
[void]$sb.Append("  ]`n}`n")
$outFull = [IO.Path]::GetFullPath($Out)
[IO.File]::WriteAllText($outFull, $sb.ToString(), (New-Object Text.UTF8Encoding $false))

$counts = $zones | Group-Object Kind | ForEach-Object { "$($_.Count) $($_.Name)" }
$massCount = @($zones | Where-Object { $_.Mass }).Count
Write-Host "Wrote $outFull : $($counts -join ', ') ($massCount mass migrations), $(@($zones | Where-Object { $_.Polygon }).Count) polygons, $(@($zones | Where-Object { $_.Circle }).Count) circles"

# ---- Preview: the zones over the map picture, in the map's colours ---------------------
if ($PreviewPath -ne '') {
    Add-Type -AssemblyName System.Drawing
    $base = [System.Drawing.Image]::FromFile([IO.Path]::GetFullPath($Map))
    $size = $base.Width
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.DrawImage($base, 0, 0, $size, $size)
    $base.Dispose()
    # The website's hues (MapBrushes.Zone*), the app's tint strengths.
    $look = @{
        'Migration' = @(0x79, 0xD9, 0x63, 51)
        'Patrol'    = @(0xA9, 0x0F, 0x33, 61)
        'Sanctuary' = @(0xF0, 0x6E, 0xAA, 89)
    }
    function Px([double]$x, [double]$y) { $f = Fraction $x $y; return New-Object System.Drawing.PointF ([float]($f[0] * $size)), ([float]($f[1] * $size)) }
    foreach ($kind in 'Migration', 'Patrol', 'Sanctuary') {
        $c = $look[$kind]
        $fill = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb($c[3], $c[0], $c[1], $c[2]))
        foreach ($z in $zones | Where-Object { $_.Kind -eq $kind }) {
            $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(204, $c[0], $c[1], $c[2])), 1.5
            if ($z.Mass) { $pen.DashPattern = @(4, 3) }
            if ($null -ne $z.Circle) {
                $centre = Px $z.Circle[0] $z.Circle[1]
                $edge = Px ($z.Circle[0] + $z.Circle[2]) $z.Circle[1]
                $r = [Math]::Abs($edge.X - $centre.X)
                $g.FillEllipse($fill, $centre.X - $r, $centre.Y - $r, 2 * $r, 2 * $r)
                $g.DrawEllipse($pen, $centre.X - $r, $centre.Y - $r, 2 * $r, 2 * $r)
            }
            else {
                $pts = [System.Drawing.PointF[]]@($z.Polygon | ForEach-Object { Px $_[0] $_[1] })
                $g.FillPolygon($fill, $pts)
                $g.DrawPolygon($pen, $pts)
            }
        }
    }
    $g.Dispose()
    $bmp.Save([IO.Path]::GetFullPath($PreviewPath), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "Preview: $([IO.Path]::GetFullPath($PreviewPath))"
}

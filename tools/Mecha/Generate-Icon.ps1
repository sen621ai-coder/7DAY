# Renders the Combat Robot GLB into a silhouette item icon (front view,
# vertex-dot projection) without Unity. Output: ItemIcons 256x256 PNG.
param(
    [string]$Glb = "F:\SteamLibrary\steamapps\common\7 Days To Die\Mods\ZZ-PZAEC_Mecha\Resources\combat_robot.glb",
    [string]$Out = "F:\SteamLibrary\steamapps\common\7 Days To Die\Mods\ZZ-PZAEC_Mecha\ItemIcons\vehicleCombatRobotPlaceable.png"
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$bytes = [System.IO.File]::ReadAllBytes($Glb)
$jsonLen = [System.BitConverter]::ToUInt32($bytes, 12)
$g = [System.Text.Encoding]::UTF8.GetString($bytes, 20, $jsonLen) | ConvertFrom-Json
$binHeader = 20 + $jsonLen; while ($binHeader % 4 -ne 0) { $binHeader++ }
$binBase = $binHeader + 8

function ReadFloats($ai) {
    $a = $g.accessors[$ai]; $bv = $g.bufferViews[$a.bufferView]
    $stride = $bv.byteStride; if (-not $stride) { $stride = 12 }
    $r = @(); for ($i = 0; $i -lt $a.count; $i++) { $o = $binBase + $bv.byteOffset + $a.byteOffset + $stride * $i
        $r += [System.BitConverter]::ToSingle($bytes, $o); $r += [System.BitConverter]::ToSingle($bytes, $o + 4); $r += [System.BitConverter]::ToSingle($bytes, $o + 8) }
    ,$r }
function QuatMat($x, $y, $z, $w) {
    $xx = $x * $x; $yy = $y * $y; $zz = $z * $z
    ,@([double](1 - 2 * ($yy + $zz)), [double](2 * ($x * $y - $z * $w)), [double](2 * ($x * $z + $y * $w)), 0,
       [double](2 * ($x * $y + $z * $w)), [double](1 - 2 * ($xx + $zz)), [double](2 * ($y * $z - $x * $w)), 0,
       [double](2 * ($x * $z - $y * $w)), [double](2 * ($y * $z + $x * $w)), [double](1 - 2 * ($xx + $yy)), 0, 0, 0, 0, 1) }
function MulMat($a, $b) {
    $r = New-Object 'double[]' 16
    for ($row = 0; $row -lt 4; $row++) { for ($col = 0; $col -lt 4; $col++) {
        $s = 0.0; for ($k = 0; $k -lt 4; $k++) { $s += $a[$row * 4 + $k] * $b[$k * 4 + $col] }
        $r[$row * 4 + $col] = $s } }
    ,$r }
$script:world = @{}
function NodeLocal($n) {
    $m = @([double]1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1)
    if ($n.scale) { for ($i = 0; $i -lt 3; $i++) { $m[0 * 4 + $i] *= [double]$n.scale[0]; $m[1 * 4 + $i] *= [double]$n.scale[1]; $m[2 * 4 + $i] *= [double]$n.scale[2] } }
    if ($n.rotation) { $m = MulMat $m (QuatMat ([double]$n.rotation[0]) ([double]$n.rotation[1]) ([double]$n.rotation[2]) ([double]$n.rotation[3])) }
    if ($n.translation) { $m[3] += [double]$n.translation[0]; $m[7] += [double]$n.translation[1]; $m[11] += [double]$n.translation[2] }
    ,$m }
function Walk($idx, $parent) {
    $n = $g.nodes[$idx]
    $m = MulMat $parent (NodeLocal $n)
    $script:world[[int]$idx] = $m
    if ($n.children) { foreach ($c in $n.children) { Walk ([int]$c) $m } } }
for ($i = 0; $i -lt $g.scenes[0].nodes.Count; $i++) { Walk ([int]$g.scenes[0].nodes[$i]) (@([double]1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1)) }

$points = New-Object System.Collections.ArrayList
for ($i = 0; $i -lt $g.nodes.Count; $i++) {
    $n = $g.nodes[$i]; if ($null -eq $n.mesh) { continue }
    $mat = 0; if ($g.meshes[[int]$n.mesh].primitives[0].material -ne $null) { $mat = [int]$g.meshes[[int]$n.mesh].primitives[0].material }
    if ($g.materials[$mat].name -eq 'Floor') { continue }
    $verts = ReadFloats $g.meshes[[int]$n.mesh].primitives[0].attributes.POSITION
    $m = $script:world[[int]$i]
    for ($v = 0; $v -lt $verts.Count; $v += 3) {
        $x = $verts[$v]; $y = $verts[$v + 1]; $z = $verts[$v + 2]
        [void]$points.Add(@(($m[0] * $x + $m[4] * $y + $m[8] * $z + $m[12]), ($m[1] * $x + $m[5] * $y + $m[9] * $z + $m[13]))) } }

$minX = [double]::MaxValue; $maxX = [double]::MinValue; $minY = [double]::MaxValue; $maxY = [double]::MinValue
foreach ($p in $points) { if ($p[0] -lt $minX) { $minX = $p[0] }; if ($p[0] -gt $maxX) { $maxX = $p[0] }; if ($p[1] -lt $minY) { $minY = $p[1] }; if ($p[1] -gt $maxY) { $maxY = $p[1] } }

$size = 256; $pad = 26
$bmp = New-Object System.Drawing.Bitmap($size, $size)
$gfx = [System.Drawing.Graphics]::FromImage($bmp)
$gfx.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$gfx.Clear([System.Drawing.Color]::Transparent)
$spanX = $maxX - $minX; $spanY = $maxY - $minY
$span = [Math]::Max($spanX, $spanY)
# Fill silhouette: soft dark body, then luminous cyan edge dots.
$fill = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(150, 18, 26, 34))
$edge = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(230, 90, 220, 255))
foreach ($p in $points) {
    $cx = $pad + ($p[0] - $minX - ($spanX - $span) / 2) / $span * ($size - 2 * $pad)
    $cy = $size - $pad - ($p[1] - $minY - ($spanY - $span) / 2) / $span * ($size - 2 * $pad)
    $gfx.FillRectangle($fill, [float]($cx - 1.5), [float]($cy - 1.5), 3.0, 3.0)
}
foreach ($p in $points) {
    $cx = $pad + ($p[0] - $minX - ($spanX - $span) / 2) / $span * ($size - 2 * $pad)
    $cy = $size - $pad - ($p[1] - $minY - ($spanY - $span) / 2) / $span * ($size - 2 * $pad)
    $gfx.FillRectangle($edge, [float]($cx - 1.0), [float]($cy - 1.0), 2.0, 2.0)
}
$gfx.Dispose()
$dir = Split-Path $Out
if (!(Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output ("icon written: {0} ({1} verts)" -f $Out, $points.Count)

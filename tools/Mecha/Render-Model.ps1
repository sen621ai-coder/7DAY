# Renders the Combat Robot GLB into a readable PNG (isometric view, vertex
# cloud + ground reference) without Unity. Used to compare the source model
# against in-game screenshots.
param(
    [string]$Glb = "F:\llm\combat_robot.glb",
    [string]$Out = "F:\SteamLibrary\steamapps\common\7 Days To Die\Mods\.local-tests\MechaNativeQA\combat_robot_reference.png"
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
    # glTF local matrix is T*R*S; a "matrix" field (column-major) overrides TRS.
    $m = @([double]1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1)
    if ($n.matrix -and $n.matrix.Count -eq 16) {
        # column-major glTF -> row-major layout used by this renderer
        for ($c = 0; $c -lt 4; $c++) { for ($r = 0; $r -lt 4; $r++) { $m[$r * 4 + $c] = [double]$n.matrix[$c * 4 + $r] } }
    }
    else {
        if ($n.rotation) { $m = QuatMat ([double]$n.rotation[0]) ([double]$n.rotation[1]) ([double]$n.rotation[2]) ([double]$n.rotation[3]) }
        if ($n.scale) {
            $m[0] *= [double]$n.scale[0]; $m[4] *= [double]$n.scale[0]; $m[8] *= [double]$n.scale[0]
            $m[1] *= [double]$n.scale[1]; $m[5] *= [double]$n.scale[1]; $m[9] *= [double]$n.scale[1]
            $m[2] *= [double]$n.scale[2]; $m[6] *= [double]$n.scale[2]; $m[10] *= [double]$n.scale[2]
        }
        if ($n.translation) { $m[3] += [double]$n.translation[0]; $m[7] += [double]$n.translation[1]; $m[11] += [double]$n.translation[2] }
    }
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
    $matIdx = [int]$g.meshes[[int]$n.mesh].primitives[0].material
    if ($g.materials[$matIdx].name -eq 'Floor') { continue }
    $verts = ReadFloats $g.meshes[[int]$n.mesh].primitives[0].attributes.POSITION
    $m = $script:world[[int]$i]
    for ($v = 0; $v -lt $verts.Count; $v += 3) {
        $x = $verts[$v]; $y = $verts[$v + 1]; $z = $verts[$v + 2]
        [void]$points.Add(@(($m[0] * $x + $m[1] * $y + $m[2] * $z + $m[3]),
                            ($m[4] * $x + $m[5] * $y + $m[6] * $z + $m[7]),
                            ($m[8] * $x + $m[9] * $y + $m[10] * $z + $m[11]))) } }

# Isometric projection: u = (x - z) * cos30, v = -y + (x + z) * sin30 * .5
$minU = 1e30; $maxU = -1e30; $minV = 1e30; $maxV = -1e30
foreach ($p in $points) {
    $u = ($p[0] - $p[2]) * .866; $v = -$p[1] + ($p[0] + $p[2]) * .25
    if ($u -lt $minU) { $minU = $u }; if ($u -gt $maxU) { $maxU = $u }
    if ($v -lt $minV) { $minV = $v }; if ($v -gt $maxV) { $maxV = $v } }
$W = 1200; $H = 900; $pad = 60
$bmp = New-Object System.Drawing.Bitmap($W, $H)
$gfx = [System.Drawing.Graphics]::FromImage($bmp)
$gfx.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$gfx.Clear([System.Drawing.Color]::FromArgb(255, 12, 14, 18))
$span = [Math]::Max($maxU - $minU, $maxV - $minV)
$pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 90, 220, 255), 1.4)
$fill = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(90, 40, 90, 120))
foreach ($p in $points) {
    $u = ($p[0] - $p[2]) * .866; $v = -$p[1] + ($p[0] + $p[2]) * .25
    $cx = $pad + ($u - $minU - (($maxU - $minU) - $span) / 2) / $span * ($W - 2 * $pad)
    $cy = $pad + ($v - $minV - (($maxV - $minV) - $span) / 2) / $span * ($H - 2 * $pad)
    $gfx.FillRectangle($fill, [float]($cx - 2.2), [float]($cy - 2.2), 4.4, 4.4)
}
foreach ($p in $points) {
    $u = ($p[0] - $p[2]) * .866; $v = -$p[1] + ($p[0] + $p[2]) * .25
    $cx = $pad + ($u - $minU - (($maxU - $minU) - $span) / 2) / $span * ($W - 2 * $pad)
    $cy = $pad + ($v - $minV - (($maxV - $minV) - $span) / 2) / $span * ($H - 2 * $pad)
    $gfx.DrawLine($pen, [float]$cx, [float]$cy, [float]($cx + .6), [float]($cy + .6))
}
# height ruler on the left
$scalePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 255, 170, 60), 1)
$gfx.DrawLine($scalePen, 40, 60, 40, $H - 60)
$font = New-Object System.Drawing.Font("Consolas", 11)
$textBrush = [System.Drawing.Brushes]::Orange
$gfx.DrawString(("model height " + [math]::Round(($maxV - $minV), 2) + " units | verts " + $points.Count), $font, $textBrush, 24, $H - 36)
$gfx.Dispose()
$dir = Split-Path $Out
if (!(Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output ("reference render: {0} ({1} verts, height {2})" -f $Out, $points.Count, [math]::Round($maxV - $minV, 2))

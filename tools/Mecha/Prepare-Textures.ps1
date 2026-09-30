# Downsamples the Buster Drone GLB's embedded textures to bounded PNG files
# next to the mod. The runtime loader prefers these files over the 4K images
# embedded in the 77 MB GLB, keeping VRAM close to 2K-era budgets.
# Works on Windows PowerShell 5.1 (System.Drawing) and pwsh 7.
param(
    [string]$Glb = "F:\SteamLibrary\steamapps\common\7 Days To Die\Mods\ZZ-PZAEC_Mecha\Resources\buster_drone.glb",
    [string]$OutDir = "F:\SteamLibrary\steamapps\common\7 Days To Die\Mods\ZZ-PZAEC_Mecha\Resources\MechaTextures",
    [int]$MaxSize = 1024
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$bytes = [System.IO.File]::ReadAllBytes($Glb)
$magic = [System.Text.Encoding]::ASCII.GetString($bytes, 0, 4)
if ($magic -ne 'glTF') { throw "Not a GLB: magic=$magic" }
$jsonLen = [System.BitConverter]::ToUInt32($bytes, 12)
$json = [System.Text.Encoding]::UTF8.GetString($bytes, 20, $jsonLen)
$g = $json | ConvertFrom-Json
$jsonEnd = 20 + $jsonLen
while ($jsonEnd % 4 -ne 0) { $jsonEnd++ }
$binData = $jsonEnd + 8

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$report = @()
for ($i = 0; $i -lt $g.images.Count; $i++) {
    $img = $g.images[$i]
    if ($img.mimeType -ne 'image/png' -and $img.mimeType -ne 'image/jpeg') {
        $report += "img[$i] skip mime=$($img.mimeType)"; continue
    }
    $bv = $g.bufferViews[$img.bufferView]
    $off = $binData + [int]$bv.byteOffset
    $len = [int]$bv.byteLength
    $pngBytes = New-Object byte[] $len
    [Array]::Copy($bytes, $off, $pngBytes, 0, $len)
    $ms = New-Object System.IO.MemoryStream(,$pngBytes)
    $bmp = [System.Drawing.Bitmap]::FromStream($ms)
    $w = $bmp.Width; $h = $bmp.Height
    if ($w -gt $MaxSize -or $h -gt $MaxSize) {
        $scale = [Math]::Min($MaxSize / $w, $MaxSize / $h)
        $nw = [Math]::Max(1, [int][Math]::Round($w * $scale / 2.0) * 2)
        $nh = [Math]::Max(1, [int][Math]::Round($h * $scale / 2.0) * 2)
        $small = New-Object System.Drawing.Bitmap($nw, $nh)
        $gfx = [System.Drawing.Graphics]::FromImage($small)
        $gfx.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $gfx.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $gfx.DrawImage($bmp, 0, 0, $nw, $nh)
        $gfx.Dispose()
        $bmp.Dispose()
        $bmp = $small
    }
    $out = Join-Path $OutDir ("tex_{0}.png" -f $i)
    $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
    $kb = [math]::Round((Get-Item $out).Length / 1KB)
    $report += ("img[{0}] {1}x{2} -> {3}x{4} {5}KB tex_{0}.png" -f $i, $w, $h, $bmp.Width, $bmp.Height, $kb)
    $bmp.Dispose()
    $ms.Dispose()
}
$report
Write-Output ("DONE: {0} textures in {1}" -f $report.Count, $OutDir)

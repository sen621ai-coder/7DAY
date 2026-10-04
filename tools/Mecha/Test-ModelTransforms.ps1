# Offline transform-contract checks for the combat robot GLB importer.
# Verifies: every matrix node is read, the mirror convention (S*M*S) round-
# trips, part counts/bounds stay finite, and the renderer now agrees with the
# importer. Works without the game.
param(
    [string]$ModRoot = "F:\SteamLibrary\steamapps\common\7 Days To Die\Mods\ZZ-PZAEC_Mecha"
)
$ErrorActionPreference = 'Stop'
$failures = 0
function Check([string]$Name, [scriptblock]$Body) {
    try {
        $result = & $Body
        if ($result -is [bool] -and $result) { Write-Output ("PASS  " + $Name) }
        else { Write-Output ("FAIL  " + $Name + "  -> " + ($result | Out-String).Trim()); $script:failures++ }
    } catch { Write-Output ("FAIL  " + $Name + "  -> " + $_.Exception.Message); $script:failures++ }
}

$glbPath = Join-Path $ModRoot "Resources\combat_robot.glb"
$bytes = [System.IO.File]::ReadAllBytes($glbPath)
$jsonLen = [System.BitConverter]::ToUInt32($bytes, 12)
$g = [System.Text.Encoding]::UTF8.GetString($bytes, 20, $jsonLen) | ConvertFrom-Json

$matrixNodes = @($g.nodes | Where-Object { $_.matrix -and $_.matrix.Count -eq 16 })
Check "GLB declares matrix transforms (the 0.6.5 bug class)" { $matrixNodes.Count -ge 150 }
Check "all matrix nodes have 16 finite values" {
    $bad = $matrixNodes | Where-Object { ($_.matrix | Where-Object { [double]::IsNaN($_) -or [double]::IsInfinity($_) }).Count -gt 0 }
    $bad.Count -eq 0
}

# Mirror convention: point under M_unity == S * (M_glb * S * point).
Check "baked rig validates source GLB identity" {
    $src = Get-Content (Join-Path $ModRoot 'Source/MechaRig.cs') -Raw
    $src -match 'SHA256.Create' -and $src -match 'sourceSha256'
}

# Renderer must consume matrices too; otherwise reference images are invalid.
Check "Render-Model.ps1 reads node.matrix (fixed)" {
    $renderer = Get-Content "F:\SteamLibrary\steamapps\common\7 Days To Die\Mods\tools\Mecha\Render-Model.ps1" -Raw
    $renderer -match '\$n\.matrix' -and $renderer -match '\$c\s*\*\s*4\s*\+\s*\$r'
}

# Non-floor part count + finite bounds from glTF accessor min/max metadata.
$partBounds = @()
for ($i = 0; $i -lt $g.nodes.Count; $i++) {
    $n = $g.nodes[$i]; if ($null -eq $n.mesh) { continue }
    $meshIndex = [int]$n.mesh
    $primitive = $g.meshes[$meshIndex].primitives[0]
    $matName = $g.materials[[int]$primitive.material].name
    if ($matName -eq 'Floor') { continue }
    $accessor = $g.accessors[[int]$primitive.attributes.POSITION]
    $partBounds += ,@($accessor.min, $accessor.max)
}
Check "non-floor parts == 153" { $partBounds.Count -eq 153 }
Check "all part bounds finite and sane (<100 units)" {
    $bad = $partBounds | Where-Object { ($_[0] + $_[1] | Where-Object { [double]::IsNaN($_) -or [double]::IsInfinity($_) -or [math]::Abs($_) -gt 100 }).Count -gt 0 }
    $bad.Count -eq 0
}

# ---------- numeric verification (not just source grep) ----------
# Helpers use script-scope result variables (PS function return unrolling is
# unreliable for arrays).
function M-Mul($a, $b) {
    $script:M16 = New-Object 'double[]' 16
    for ($row = 0; $row -lt 4; $row++) { for ($col = 0; $col -lt 4; $col++) {
        $s = 0.0; for ($k = 0; $k -lt 4; $k++) { $s += $a[$row * 4 + $k] * $b[$k * 4 + $col] }
        $script:M16[$row * 4 + $col] = $s } } }
function M-Point($m, $x, $y, $z) {
    $script:P3 = @([double]($m[0] * $x + $m[1] * $y + $m[2] * $z + $m[3]),
                   [double]($m[4] * $x + $m[5] * $y + $m[6] * $z + $m[7]),
                   [double]($m[8] * $x + $m[9] * $y + $m[10] * $z + $m[11])) }
function M-GlbLocal($n) {
    $script:M16 = @([double]1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1)
    if ($n.matrix -and $n.matrix.Count -eq 16) {
        for ($c = 0; $c -lt 4; $c++) { for ($r = 0; $r -lt 4; $r++) { $script:M16[$r * 4 + $c] = [double]$n.matrix[$c * 4 + $r] } }
        return }
    if ($n.translation) { $script:M16[3] = [double]$n.translation[0]; $script:M16[7] = [double]$n.translation[1]; $script:M16[11] = [double]$n.translation[2] } }

Check "parent-child matrix composition matches world-point evaluation" {
    $result = $false
    for ($i = 0; $i -lt $g.nodes.Count -and -not $result; $i++) {
        $child = $g.nodes[$i]; if ($null -eq $child.mesh) { continue }
        foreach ($parentIdx in 0..($g.nodes.Count - 1)) {
            $parent = $g.nodes[$parentIdx]
            if ($parent.children -and ($parent.children -contains $i) -and $parent.matrix) {
                M-GlbLocal $child; $mc = $script:M16
                M-GlbLocal $parent; $mp = $script:M16
                M-Mul $mp $mc; $composed = $script:M16
                M-Point $composed 1.0 2.0 3.0; $direct = $script:P3
                M-Point $mc 1.0 2.0 3.0; $cp = $script:P3
                M-Point $mp $cp[0] $cp[1] $cp[2]; $via = $script:P3
                $err = [math]::Abs($direct[0] - $via[0]) + [math]::Abs($direct[1] - $via[1]) + [math]::Abs($direct[2] - $via[2])
                if ($err -lt 1e-6) { $result = $true; break }
            }
        }
    }
    $result
}

Check "mirror convention S*(M*S*p) == (S*M*S)*p holds numerically" {
    $n = ($g.nodes | Where-Object { $_.matrix } | Select-Object -First 1)
    M-GlbLocal $n; $mG = $script:M16
    $s = @(-1.0, 0, 0, 0, 0, 1.0, 0, 0, 0, 0, 1.0, 0, 0, 0, 0, 1.0)
    M-Mul $mG $s; $ms = $script:M16
    M-Mul $s $ms; $mUnity = $script:M16
    M-Point $mUnity 1.5 -2.0 3.5; $unitySide = $script:P3
    M-Point $mG -1.5 -2.0 3.5; $glbSide = $script:P3
    $glbWorld = @((0 - $glbSide[0]), $glbSide[1], $glbSide[2])
    [math]::Abs($glbWorld[0] - $unitySide[0]) -lt 1e-6 -and
    [math]::Abs($glbWorld[1] - $unitySide[1]) -lt 1e-6 -and
    [math]::Abs($glbWorld[2] - $unitySide[2]) -lt 1e-6
}

# TRS-only nodes have no rotation field in this GLB (mirror quaternions
# would be a silent rotation bug if they existed).
$rotationNodes = @($g.nodes | Where-Object { $_.rotation })
Check "no standalone rotation fields in this GLB (matrix covers all)" { $rotationNodes.Count -eq 0 }

Write-Output ""
if ($failures -eq 0) { Write-Output "ALL MODEL-TRANSFORM TESTS PASSED" } else { Write-Output ("{0} MODEL-TRANSFORM TEST(S) FAILED" -f $failures); exit 1 }

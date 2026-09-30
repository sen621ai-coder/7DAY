# Prints material -> texture mapping of a GLB (for loader wiring).
param([string]$Glb = "F:\SteamLibrary\steamapps\common\7 Days To Die\Mods\ZZ-PZAEC_Mecha\Resources\combat_robot.glb")
$ErrorActionPreference = 'Stop'
$bytes = [System.IO.File]::ReadAllBytes($Glb)
$jsonLen = [System.BitConverter]::ToUInt32($bytes, 12)
$g = [System.Text.Encoding]::UTF8.GetString($bytes, 20, $jsonLen) | ConvertFrom-Json
for ($i = 0; $i -lt $g.materials.Count; $i++) {
    $m = $g.materials[$i]
    $bc = ''; if ($m.pbrMetallicRoughness -and $m.pbrMetallicRoughness.baseColorTexture) { $bc = $m.pbrMetallicRoughness.baseColorTexture.index }
    $em = ''; if ($m.emissiveTexture) { $em = $m.emissiveTexture.index }
    $no = ''; if ($m.normalTexture) { $no = $m.normalTexture.index }
    $mr = ''; if ($m.pbrMetallicRoughness -and $m.pbrMetallicRoughness.metallicRoughnessTexture) { $mr = $m.pbrMetallicRoughness.metallicRoughnessTexture.index }
    Write-Output ('mat[{0}] name={1} baseColor=tex_{2} mr=tex_{3} emissive=tex_{4} normal=tex_{5}' -f $i, $m.name, $bc, $mr, $em, $no)
}

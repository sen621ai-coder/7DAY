# Offline assertions for the PZAEC Buster Drone walker.
# GLB structure, external textures, XML wiring, recipe/progression hooks,
# network damage ceiling and build freshness. No game process required.
# Works on Windows PowerShell 5.1 and pwsh 7.
param(
    [string]$ModRoot = "F:\SteamLibrary\steamapps\common\7 Days To Die\Mods\_disabled-ZZ-PZAEC_Mecha",
    [string]$ModsRoot = "F:\SteamLibrary\steamapps\common\7 Days To Die\Mods"
)
$ErrorActionPreference = 'Stop'
$failures = 0
function Check([string]$Name, [scriptblock]$Body) {
    try {
        $result = & $Body
        if ($result -is [bool] -and $result) { Write-Output ("PASS  " + $Name) }
        else { Write-Output ("FAIL  " + $Name + "  -> " + ($result | Out-String).Trim()); $script:failures++ }
    } catch {
        Write-Output ("FAIL  " + $Name + "  -> " + $_.Exception.Message); $script:failures++
    }
}

# ---------- GLB structure ----------
$glbPath = Join-Path $ModRoot "Resources\buster_drone.glb"
$bytes = [System.IO.File]::ReadAllBytes($glbPath)
$jsonLen = [System.BitConverter]::ToUInt32($bytes, 12)
$g = [System.Text.Encoding]::UTF8.GetString($bytes, 20, $jsonLen) | ConvertFrom-Json
$nodeNames = @($g.nodes | ForEach-Object { $_.name })

Check "GLB magic/version" { [System.Text.Encoding]::ASCII.GetString($bytes, 0, 4) -eq 'glTF' }
Check "GLB nodes=92 meshes=39 materials=3" { $g.nodes.Count -eq 92 -and $g.meshes.Count -eq 39 -and $g.materials.Count -eq 3 }
foreach ($key in @('Drone_Body', 'Drone_IEye', 'Drone_ILens', 'Drone_leg_F', 'Drone_leg_R', 'Drone_leg_L', 'Drone_Turb_Blade_L', 'Drone_Turb_Blade_R')) {
    Check ("GLB anchor node $key") { $nodeNames -contains $key }
}
$legSegments = @($g.nodes | Where-Object { $_.name -match '^[FRL]_P[1-7](_[GM])?$' })
Check "GLB 21 leg segment joints" { $legSegments.Count -eq 21 }
$anim = $g.animations[0]
Check "GLB Start_Liftoff animation 100 channels" { $anim.name -eq 'Start_Liftoff' -and $anim.channels.Count -eq 100 }
$animInput = $g.accessors[$anim.samplers[0].input]
Check "GLB animation duration 25s" {
    ([double](@($animInput.min))[0]) -eq 0 -and ([double](@($animInput.max))[0]) -eq 25
}
$materialNames = @($g.materials | ForEach-Object { $_.name })
Check "GLB materials body+material" { ($materialNames -contains 'body') -and ($materialNames -contains 'material') }
Check "GLB body emissive texture" {
    $body = $g.materials | Where-Object { $_.name -eq 'body' }
    $body.emissiveTexture.index -eq 5 -and $body.pbrMetallicRoughness.baseColorTexture.index -eq 3
}

# ---------- External textures ----------
foreach ($index in @(3, 5, 6, 7, 9)) {
    $path = Join-Path $ModRoot ("Resources\MechaTextures\tex_{0}.png" -f $index)
    Check ("texture tex_$index exists <=1024") {
        Add-Type -AssemblyName System.Drawing
        $bmp = [System.Drawing.Bitmap]::FromFile($path)
        $ok = $bmp.Width -le 1024 -and $bmp.Height -le 1024
        $bmp.Dispose(); $ok
    }
}

# ---------- XML wiring ----------
[xml]$vehicles = Get-Content (Join-Path $ModRoot "Config\vehicles.xml") -Raw
[xml]$items = Get-Content (Join-Path $ModRoot "Config\items.xml") -Raw
[xml]$entities = Get-Content (Join-Path $ModRoot "Config\entityclasses.xml") -Raw
[xml]$recipes = Get-Content (Join-Path $ModRoot "Config\recipes.xml") -Raw
[xml]$progression = Get-Content (Join-Path $ModRoot "Config\progression.xml") -Raw

Check "vehicles.xml defines vehicleBusterDrone" {
    ($vehicles.configs.append.vehicle | Where-Object { $_.name -eq 'vehicleBusterDrone' }) -ne $null
}
Check "vehicles.xml seat0 + storage + fuelTank" {
    $v = $vehicles.configs.append.vehicle | Where-Object { $_.name -eq 'vehicleBusterDrone' }
    $classes = @($v.property | ForEach-Object { $_.class })
    ($classes -contains 'seat0') -and ($classes -contains 'storage') -and ($classes -contains 'fuelTank')
}
Check "vehicles.xml fire key overrides" {
    $props = $vehicles.configs.append.vehicle | Where-Object { $_.name -eq 'vehicleBusterDrone' }
    ($props.property | Where-Object { $_.name -eq 'pzMechaBeamKey' }).value -eq 'Mouse0' -and
    ($props.property | Where-Object { $_.name -eq 'pzMechaMissileKey' }).value -eq 'G'
}
Check "items.xml placeable Action1 vehicle" {
    $item = $items.configs.append.item | Where-Object { $_.name -eq 'vehicleBusterDronePlaceable' }
    $action = $item.property | Where-Object { $_.class -eq 'Action1' }
    ($action.property | Where-Object { $_.name -eq 'Vehicle' }).value -eq 'vehicleBusterDrone' -and
    ($action.property | Where-Object { $_.name -eq 'VehicleSize' }) -ne $null
}
Check "items.xml chassis + both ammo items" {
    @($items.configs.append.item | ForEach-Object { $_.name }) -contains 'vehicleBusterDroneChassis' -and
    @($items.configs.append.item | ForEach-Object { $_.name }) -contains 'ammoPZAECMechaCell' -and
    @($items.configs.append.item | ForEach-Object { $_.name }) -contains 'ammoPZAECMechaMissile'
}
Check "entityclasses.xml vehicle entity" {
    ($entities.configs.append.entity_class | Where-Object { $_.name -eq 'vehicleBusterDrone' }) -ne $null
}
Check "recipes.xml uses AEC final assembly bench" {
    ($recipes.configs.append.recipe | Where-Object { $_.name -eq 'vehicleBusterDronePlaceable' }).craft_area -eq 'aecVehicleFinalAssemblyBench'
}
Check "AEC final assembly bench exists in 07 mod" {
    Select-String -Path (Join-Path $ModsRoot "07-AEC-Vehicles-NoMicrocraft\Config\recipes.xml") -Pattern 'aecVehicleFinalAssemblyBench' -Quiet
}
Check "progression.xml unlocks at craftingVehicles 100" {
    $passive = $progression.configs.append.passive_effect | Where-Object { $_.name -eq 'RecipeTagUnlocked' }
    $passive.level -eq '100,100' -and $passive.tags -match 'vehicleBusterDronePlaceable'
}

# ---------- Damage ceiling (16-bit network safety without Damage32) ----------
$rules = Get-Content (Join-Path $ModRoot "Source\MechaRules.cs") -Raw
Check "all damage constants <= 65535" {
    $matches2 = [regex]::Matches($rules, 'Damage\s*=\s*([0-9.]+)f')
    $bad = $matches2 | Where-Object { [double]$_.Groups[1].Value -gt 65535 }
    @($bad).Count -eq 0
}

# ---------- v0.2.0 hardening: armor mods, crew protection, field repair ----------
Check "items.xml placeable exposes vehicle mod tags" {
    $item = $items.configs.append.item | Where-Object { $_.name -eq 'vehicleBusterDronePlaceable' }
    $tags = ($item.property | Where-Object { $_.name -eq 'Tags' }).value
    $tags -match 'varmor' -and $tags -match 'vengine' -and $tags -match 'vfuel' -and $tags -match 'vlight' -and $tags -match 'vstorage' -and $tags -match 'canHaveCosmetic'
}
Check "vehicles.xml repair key is F, mode key is X, battle repair R" {
    $props = $vehicles.configs.append.vehicle | Where-Object { $_.name -eq 'vehicleBusterDrone' }
    ($props.property | Where-Object { $_.name -eq 'pzMechaRepairKey' }).value -eq 'F' -and
    ($props.property | Where-Object { $_.name -eq 'pzMechaModeKey' }).value -eq 'X' -and
    ($props.property | Where-Object { $_.name -eq 'pzMechaBattleRepairKey' }).value -eq 'R'
}
Check "rules define vanilla repair kit + 8s channel + blade constants" {
    $rules -match 'RepairKit\s*=\s*"resourceRepairKit"' -and $rules -match 'RepairSeconds\s*=\s*8f' -and
    $rules -match 'SweepDamage\s*=\s*45000f' -and $rules -match 'HeavyDamage\s*=\s*60000f' -and
    $rules -match 'HeavyChargeSeconds\s*=\s*\.5f'
}
Check "weapons define repair ops 5/6/7 and melee ops 8/9/10" {
    $weapons = Get-Content (Join-Path $ModRoot "Source\MechaWeapons.cs") -Raw
    $weapons -match 'Repair\s*=\s*5,\s*RepairStop\s*=\s*6,\s*BattleRepair\s*=\s*7,\s*SwitchMelee\s*=\s*8,\s*MeleeSweep\s*=\s*9,\s*MeleeHeavy\s*=\s*10'
}
$armor = Get-Content (Join-Path $ModRoot "Source\MechaArmor.cs") -Raw
Check "armor composes after ApacheArmor and M1" {
    $armor -match 'HarmonyAfter\("pzaec\.apache\.armor\.v1",\s*"pzaec\.m1\.abrams"\)'
}
Check "armor damage channels 15/30/50 hull, 10% cap (main battle)" {
    $armor -match '\.3\s*:\s*collision\s*\?\s*\.5\s*:\s*\.15' -and $armor -match 'cap\s*=\s*\.1;'
}
Check "crew direct-hit channels 10/20, 20% HP cap" {
    $armor -match 'explosionDepth\s*>\s*0\s*\?\s*\.2\s*:\s*collision\s*\?\s*\.25\s*:\s*\.1' -and $armor -match 'cap\s*=\s*\.2'
}
Check "crew hull transfer zeroed (closed cabin)" {
    $armor -match '__result\s*=\s*0f'
}
Check "main-battle hull 2,000,000 and cell stack 200" {
    $item = $items.configs.append.item | Where-Object { $_.name -eq 'vehicleBusterDronePlaceable' }
    $degradation = ($item.effect_group.passive_effect | Where-Object { $_.name -eq 'DegradationMax' }).value
    $cell = $items.configs.append.item | Where-Object { $_.name -eq 'ammoPZAECMechaCell' }
    $stack = ($cell.property | Where-Object { $_.name -eq 'Stacknumber' }).value
    $degradation -eq '2000000' -and $stack -eq '200'
}
Check "melee mode gates beam fire server-side" {
    $weapons = Get-Content (Join-Path $ModRoot "Source\MechaWeapons.cs") -Raw
    $weapons -match 'op\s*==\s*Fire\s*&&\s*state\.MeleeMode'
}

# ---------- Build freshness ----------
$dll = Get-Item (Join-Path $ModRoot "PZAEC.Mecha.dll") -ErrorAction SilentlyContinue
Check "PZAEC.Mecha.dll exists" { $dll -ne $null }
if ($dll) {
    Check "DLL newer than all sources" {
        $newest = (Get-ChildItem (Join-Path $ModRoot "Source") -Filter *.cs | Sort-Object LastWriteTime -Descending | Select-Object -First 1).LastWriteTime
        $dll.LastWriteTime -ge $newest
    }
}
[xml]$modInfo = Get-Content (Join-Path $ModRoot "ModInfo.xml") -Raw -Encoding UTF8
Check "ModInfo version matches README headline" {
    (Get-Content (Join-Path $ModRoot "README.md") -TotalCount 1 -Encoding UTF8) -match [regex]::Escape($modInfo.xml.Version.Value)
}

Write-Output ("")
if ($failures -eq 0) { Write-Output "ALL MECHA TESTS PASSED" } else { Write-Output ("{0} MECHA TEST(S) FAILED" -f $failures); exit 1 }

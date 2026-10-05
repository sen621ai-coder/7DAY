# Offline assertions for the PZAEC Buster Drone walker.
# GLB structure, external textures, XML wiring, recipe/progression hooks,
# network damage ceiling and build freshness. No game process required.
# Works on Windows PowerShell 5.1 and pwsh 7.
param(
    [string]$ModRoot = "F:\SteamLibrary\steamapps\common\7 Days To Die\Mods\ZZ-PZAEC_Mecha",
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
$glbPath = Join-Path $ModRoot "Resources\combat_robot.glb"
$bytes = [System.IO.File]::ReadAllBytes($glbPath)
$jsonLen = [System.BitConverter]::ToUInt32($bytes, 12)
$g = [System.Text.Encoding]::UTF8.GetString($bytes, 20, $jsonLen) | ConvertFrom-Json
$nodeNames = @($g.nodes | ForEach-Object { $_.name })

Check "GLB magic/version" { [System.Text.Encoding]::ASCII.GetString($bytes, 0, 4) -eq 'glTF' }
Check "GLB flat sculpture 154 meshes / 311 nodes / 4 materials / 15 images" {
    $g.meshes.Count -eq 154 -and $g.nodes.Count -eq 311 -and $g.materials.Count -eq 4 -and $g.images.Count -eq 15
}
Check "GLB has zero animations (procedural gait required)" { $g.animations.Count -eq 0 }
Check "GLB display floor node exists (must be skipped)" { $nodeNames -contains 'Floor.001' }
$materialNames = @($g.materials | ForEach-Object { $_.name })
Check "GLB materials Hands/Torse/Legs/Floor" {
    ($materialNames -contains 'Hands') -and ($materialNames -contains 'Torse') -and
    ($materialNames -contains 'Legs') -and ($materialNames -contains 'Floor')
}
Check "GLB Hands material maps baseColor tex_0" {
    $hands = $g.materials | Where-Object { $_.name -eq 'Hands' }
    $hands.pbrMetallicRoughness.baseColorTexture.index -eq 0
}

# ---------- External textures (baseColor set used by loader) ----------
foreach ($index in @(0, 4, 8)) {
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

Check "vehicles.xml defines vehicleCombatRobot" {
    ($vehicles.configs.append.vehicle | Where-Object { $_.name -eq 'vehicleCombatRobot' }) -ne $null
}
Check "vehicles.xml seat0 + storage + fuelTank" {
    $v = $vehicles.configs.append.vehicle | Where-Object { $_.name -eq 'vehicleCombatRobot' }
    $classes = @($v.property | ForEach-Object { $_.class })
    ($classes -contains 'seat0') -and ($classes -contains 'storage') -and ($classes -contains 'fuelTank')
}
Check "vehicles.xml fire key overrides" {
    $props = $vehicles.configs.append.vehicle | Where-Object { $_.name -eq 'vehicleCombatRobot' }
    ($props.property | Where-Object { $_.name -eq 'pzMechaBeamKey' }).value -eq 'Mouse0' -and
    ($props.property | Where-Object { $_.name -eq 'pzMechaMissileKey' }).value -eq 'G'
}
Check "items.xml placeable Action1 vehicle" {
    $item = $items.configs.append.item | Where-Object { $_.name -eq 'vehicleCombatRobotPlaceable' }
    $action = $item.property | Where-Object { $_.class -eq 'Action1' }
    ($action.property | Where-Object { $_.name -eq 'Vehicle' }).value -eq 'vehicleCombatRobot' -and
    ($action.property | Where-Object { $_.name -eq 'VehicleSize' }) -ne $null
}
Check "items.xml chassis + both ammo items" {
    @($items.configs.append.item | ForEach-Object { $_.name }) -contains 'vehicleCombatRobotChassis' -and
    @($items.configs.append.item | ForEach-Object { $_.name }) -contains 'ammoPZAECMechaCell' -and
    @($items.configs.append.item | ForEach-Object { $_.name }) -contains 'ammoPZAECMechaMissile'
}
Check "entityclasses.xml vehicle entity" {
    ($entities.configs.append.entity_class | Where-Object { $_.name -eq 'vehicleCombatRobot' }) -ne $null
}
Check "recipes.xml uses AEC final assembly bench" {
    ($recipes.configs.append.recipe | Where-Object { $_.name -eq 'vehicleCombatRobotPlaceable' }).craft_area -eq 'aecVehicleFinalAssemblyBench'
}
Check "AEC final assembly bench exists in 07 mod" {
    Select-String -Path (Join-Path $ModsRoot "07-AEC-Vehicles-NoMicrocraft\Config\recipes.xml") -Pattern 'aecVehicleFinalAssemblyBench' -Quiet
}
Check "progression.xml unlocks at craftingVehicles 100" {
    $passive = $progression.configs.append.passive_effect | Where-Object { $_.name -eq 'RecipeTagUnlocked' }
    $passive.level -eq '100,100' -and $passive.tags -match 'vehicleCombatRobotPlaceable'
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
    $item = $items.configs.append.item | Where-Object { $_.name -eq 'vehicleCombatRobotPlaceable' }
    $tags = ($item.property | Where-Object { $_.name -eq 'Tags' }).value
    $tags -match 'varmor' -and $tags -match 'vengine' -and $tags -match 'vfuel' -and $tags -match 'vlight' -and $tags -match 'vstorage' -and $tags -match 'canHaveCosmetic'
}
Check "vehicles.xml repair keys retained and melee key removed" {
    $props = $vehicles.configs.append.vehicle | Where-Object { $_.name -eq 'vehicleCombatRobot' }
    ($props.property | Where-Object { $_.name -eq 'pzMechaRepairKey' }).value -eq 'F' -and
    @($props.property | Where-Object { $_.name -eq 'pzMechaModeKey' }).Count -eq 0 -and
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
    $item = $items.configs.append.item | Where-Object { $_.name -eq 'vehicleCombatRobotPlaceable' }
    $degradation = ($item.effect_group.passive_effect | Where-Object { $_.name -eq 'DegradationMax' }).value
    $cell = $items.configs.append.item | Where-Object { $_.name -eq 'ammoPZAECMechaCell' }
    $stack = ($cell.property | Where-Object { $_.name -eq 'Stacknumber' }).value
    $degradation -eq '2000000' -and $stack -eq '200'
}
Check "retired melee commands rejected server-side" {
    $weapons = Get-Content (Join-Path $ModRoot "Source\MechaWeapons.cs") -Raw
    $weapons -match 'if\(op==SwitchMelee\|\|op==MeleeSweep\|\|op==MeleeHeavy\)return;'
}

# ---------- v0.4.0 all-terrain mobility ----------
Check "vehicles.xml hover/jump/descend keys + turbo 13.5 + unstick 2" {
    $props = $vehicles.configs.append.vehicle | Where-Object { $_.name -eq 'vehicleCombatRobot' }
    ($props.property | Where-Object { $_.name -eq 'pzMechaHoverKey' }).value -eq 'Q' -and
    ($props.property | Where-Object { $_.name -eq 'pzMechaJumpKey' }).value -eq 'Space' -and
    ($props.property | Where-Object { $_.name -eq 'pzMechaDescendKey' }).value -eq 'C' -and
    ($props.property | Where-Object { $_.name -eq 'velocityMax_turbo' }).value -eq '7.5, 3.6, 13.5, 3.6' -and
    ($props.property | Where-Object { $_.name -eq 'unstickForce' }).value -eq '2'
}
Check "rules define hover/charge-jump/trample constants" {
    $rules -match 'HoverHeight\s*=\s*1\.2f' -and $rules -match 'JumpMaxSpeed\s*=\s*9\.9f' -and
    $rules -match 'TrampleDamage\s*=\s*12000f' -and $rules -match 'StompDamage\s*=\s*30000f' -and
    $rules -match 'StompKnockback\s*=\s*120f'
}
$model = Get-Content (Join-Path $ModRoot "Source\MechaModel.cs") -Raw
Check "foot support replaces hidden wheels and low belly hull" {
    $support = Get-Content (Join-Path $ModRoot 'Source/MechaGroundSupport.cs') -Raw
    $model -match 'wheel.enabled\s*=\s*false' -and $model -notmatch 'new GameObject\("MechaBellySlider"' -and
    $support -match 'count<4' -and $support -match 'GroundSupport' -and $support -match 'ForceMode.Force'
}
Check "weapons define trample ops 11/12 with server rate limits" {
    $weapons = Get-Content (Join-Path $ModRoot "Source\MechaWeapons.cs") -Raw
    $weapons -match 'Trample\s*=\s*11,\s*Stomp\s*=\s*12' -and $weapons -match 'NextStomp\s*=\s*now\s*\+\s*Rules\.StompCooldown' -and
    $weapons -match 'Rules\.TrampleEntityInterval'
}

# ---------- v0.5.0 biped rig + IK gait ----------
$modelSrc = Get-Content (Join-Path $ModRoot "Source\MechaModel.cs") -Raw
$gaitSrc = (Get-Content (Join-Path $ModRoot "Source\MechaDeploy.cs") -Raw) + (Get-Content (Join-Path $ModRoot "Source\MechaGait.cs") -Raw)
Check "old trike GLB removed" { !(Test-Path (Join-Path $ModRoot "Resources\buster_drone.glb")) }
Check "rig manifest covers source nodes and articulated joints" {
    $rig = Get-Content (Join-Path $ModRoot 'Resources/combat_robot_rig.json') -Raw | ConvertFrom-Json
    @($rig.parts.node | Sort-Object -Unique).Count -eq 153 -and
    @($rig.joints.name).Contains('AnkleL') -and @($rig.joints.name).Contains('ElbowR')
}
Check "rig manifest excludes display floor" {
    $rig = Get-Content (Join-Path $ModRoot 'Resources/combat_robot_rig.json') -Raw | ConvertFrom-Json
    @($rig.parts | Where-Object {$_.material -eq 3}).Count -eq 0
}
Check "native gait source and QA harness present" {
    (Test-Path (Join-Path $ModRoot 'Source/MechaGait.cs')) -and
    (Test-Path (Join-Path $ModsRoot 'tools/Mecha/MotionNativeQA.cs'))
}
Check "deploy rise is procedural (no GLB animation dependency)" {
    $gaitSrc -match 'DeployRiseSeconds' -and $rules -match 'TargetHeight\s*=\s*3\.2f'
}

# ---------- v0.6.0 boarding ceremony ----------
$boarding = Get-Content (Join-Path $ModRoot "Source\MechaBoarding.cs") -Raw
Check "boarding gates native entry and detach request" {
    $boarding -match 'typeof\(Boarding\),\s*nameof\(EnterObserve\)' -and
    $boarding -match 'typeof\(Boarding\),\s*nameof\(ExitObserve\)'
}
Check "boarding four-phase timing constants" {
    $rules -match 'BoardExpandSeconds\s*=\s*1\.2f' -and $rules -match 'BoardLiftSeconds\s*=\s*\.8f' -and
    $rules -match 'BoardCloseSeconds\s*=\s*1\.5f'
}
Check "0.7.0 articulated presentation enabled" {
    $rules -match 'BoardingEnabled = true' -and $rules -match 'GaitIkEnabled = true'
}
Check "vehicles.xml carries full seat/handle IK anchors" {
    $props = $vehicles.configs.append.vehicle | Where-Object { $_.name -eq 'vehicleCombatRobot' }
    $seat = ($props.property | Where-Object { $_.class -eq 'seat0' }).property | ForEach-Object { $_.name }
    $bars = ($props.property | Where-Object { $_.class -eq 'handlebars' }).property | ForEach-Object { $_.name }
    ($seat -contains 'IKFootLPosition') -and ($seat -contains 'IKHandRPosition') -and
    ($seat -contains 'IKFootRRotation') -and ($bars -contains 'IKHandLPosition') -and
    ($bars -contains 'IKHandRRotation')
}
Check "baked geometry is shipped" {
    (Get-Item (Join-Path $ModRoot 'Resources/combat_robot_rig.bin')).Length -gt 1000000
}
Check "weapons locked during ceremony" {
    $weapons = Get-Content (Join-Path $ModRoot "Source\MechaWeapons.cs") -Raw
    $weapons -match 'if\s*\(Boarding\.Active\(vehicle\)\)\s*return;'
}
Check "boarding event routes through FX to remote spectators" {
    $fx = Get-Content (Join-Path $ModRoot "Source\MechaFX.cs") -Raw
    $fx -match 'Boarding\.BoardEvent' -and $fx -match 'Boarding\.Receive' -and
    $boarding -match 'CameraRide' -and $boarding -match 'Describe'
}
Check "four second boarding and per-vehicle pose API present" {
    $boarding -match 'ApplyPose' -and $boarding -match 'ReceiveSnapshot' -and $boarding -match 'full\?4f'
}
Check "HUD ceremony overlay (letterbox + typewriter)" {
    $hud = Get-Content (Join-Path $ModRoot "Source\MechaHUD.cs") -Raw
    $hud -match 'DrawBoardingOverlay' -and $hud -match '战斗系统联机'
}

# ---------- v0.6.1 hotfix ----------
Check "pose stores and restores bind transforms" {
    $modelSrc -match 'RestRot' -and $modelSrc -match 'RestPos' -and $gaitSrc -match 'ResetPose'
}
Check "boarding transfers after kneeling with recursion guard" {
    $boarding -match 'static bool EnterObserve' -and $boarding -match 'static bool ExitObserve' -and
    $boarding -match 'TransferAt' -and $boarding -match 'committing=false'
}
Check "intent op guard includes motion and boarding skip" {
    (Get-Content (Join-Path $ModRoot 'Source/MechaWeapons.cs') -Raw) -match 'op > Samurai.Cancel'
}
Check "dedicated silhouette icon generated and referenced" {
    $icon = Join-Path $ModRoot "ItemIcons\vehicleCombatRobotPlaceable.png"
    $atlas = Join-Path $ModRoot "UIAtlases\ItemIconAtlas\vehicleCombatRobotPlaceable.png"
    if (!(Test-Path $icon) -or !(Test-Path $atlas)) { return $false }
    Add-Type -AssemblyName System.Drawing
    $bmp = [System.Drawing.Bitmap]::FromFile($icon); $ok = $bmp.Width -eq 256 -and $bmp.Height -eq 256
    $bmp.Dispose()
    $item = $items.configs.append.item | Where-Object { $_.name -eq 'vehicleCombatRobotPlaceable' }
    $iconRef = ($item.property | Where-Object { $_.name -eq 'CustomIcon' }).value
    return $ok -and $iconRef -eq 'vehicleCombatRobotPlaceable'
}
Check "chinese naming: Prototype with shared ammunition" {
    $loc = Get-Content (Join-Path $ModRoot "Config\Localization.csv") -Raw -Encoding UTF8
    $loc.Contains('vehicleCombatRobotPlaceable,items,item,,,Unit-01 (Prototype),初号机（试验体）') -and
    $loc -match 'ammoPZAECMechaCell,items,item,,,Energy Cell,能量电池'
}

Check "rig importer wired and fuel HUD retained" {
    $modelSrc -match 'RobotRig.Build' -and
    (Get-Content (Join-Path $ModRoot 'Source/MechaHUD.cs') -Raw) -match '燃料耗尽'
}
Check "ground support has torso hull, current COM, F9 diagnostics and sound gate" {
    $modelSrc -match 'centerOfMass\s*=\s*new Vector3\(0,\s*1.3f' -and
    (Get-Content (Join-Path $ModRoot 'Source/MechaDebug.cs') -Raw) -match 'GroundSupport.Diagnostics' -and
    (Get-Content (Join-Path $ModRoot 'Source/MechaFX.cs') -Raw) -match 'soundGate'
}

# ---------- v0.6.8 trigger discipline ----------
Check "aim messages cannot fire: trigger state is separate" {
    $weapons = Get-Content (Join-Path $ModRoot "Source\MechaWeapons.cs") -Raw
    $weapons -match 'TriggerHeld\s*=\s*op\s*==\s*Fire' -and
    $weapons -match 'state\.TriggerHeld\s*&&\s*state\.Aiming\)\s*FireBeam' -and
    $weapons -match 'state\.MissileTrigger\s*&&\s*state\.MissileAiming\s*&&\s*!Samurai.Busy\(state.Vehicle\)\)\s*FireMissile'
}
Check "stale sequences drop without extending the session" {
    $weapons = Get-Content (Join-Path $ModRoot "Source\MechaWeapons.cs") -Raw
    $weapons -match 'if\s*\(sequence\s*<=\s*LastSequence\)\s*return\s*false;'
}
Check "stop clears trigger + aim + lock together" {
    $weapons = Get-Content (Join-Path $ModRoot "Source\MechaWeapons.cs") -Raw
    $weapons -match 'state\.TriggerHeld\s*=\s*false;\s*state\.MissileTrigger\s*=\s*false;'
}
Check "beam events dedup by vehicle+serial; melee has its own sound" {
    $fx = Get-Content (Join-Path $ModRoot "Source\MechaFX.cs") -Raw
    $fx -match 'seenBeamSerials' -and $fx -match 'electric_fence_impact'
}

Check "robot has no automotive engine audio" {
    (Get-Content (Join-Path $ModRoot 'Config/vehicles.xml') -Raw) -notmatch 'Vehicles/Suv|Vehicles/Motorbike|batterybank_start|batterybank_stop'
}
Check "native wheel drive disabled" {
    $v = $vehicles.configs.append.vehicle | Where-Object {$_.name -eq 'vehicleCombatRobot'}
    ($v.property | Where-Object {$_.name -eq 'motorTorque_turbo'}).value -eq '0, 0, 0, 0' -and
    ($v.property | Where-Object {$_.name -eq 'steerAngleMax'}).value -eq '0'
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

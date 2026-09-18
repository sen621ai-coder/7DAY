#Requires -Version 7.0
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
Add-Type -TypeDefinition (Get-Content "$root/99-AEC_T16_RuntimeFix/Source/ApacheWeaponRules.cs" -Raw)
[xml]$enemies=Get-Content "$root/98-AECxProjectZ_Tweaks/Config/entityclasses.xml" -Raw
[xml]$items=Get-Content "$root/ZZ-PZAEC_ApacheFlight/Config/items.xml" -Raw
[xml]$recipes=Get-Content "$root/ZZ-PZAEC_ApacheFlight/Config/recipes.xml" -Raw
$cannon=[AECT16RuntimeFix.ApacheWeaponRules]::CannonEntityDamage
$rocket=[AECT16RuntimeFix.ApacheWeaponRules]::RocketEntityDamage
function Check($condition,$message){if(!$condition){throw $message}}
Check ($cannon -eq 20000 -and $rocket -eq 100000) 'Support damage budget changed; review balance targets.'
Check ([float]$items.SelectSingleNode("//item[@name='pzApache30mm']//passive_effect[@name='EntityDamage']").value -eq $cannon) 'Cannon XML/runtime mismatch'
Check ([float]$items.SelectSingleNode("//item[@name='pzApacheRocket']//property[@name='EntityDamage']").value -eq $rocket) 'Rocket XML/runtime mismatch'
Check ([float]$items.SelectSingleNode("//item[@name='pzApacheRocket']//display_value[@name='dExEntityDamage']").value -eq $rocket) 'Rocket display mismatch'
Check ([AECT16RuntimeFix.ApacheWeaponRules]::CannonBlockDamage -eq 8 -and [AECT16RuntimeFix.ApacheWeaponRules]::RocketBlockDamage -eq 50) 'Terrain damage must not scale with entity damage'
Check ([float]$items.SelectSingleNode("//item[@name='pzApache30mm']//passive_effect[@name='BlockDamage']").value -eq 8) 'Cannon block XML mismatch'
Check ([float]$items.SelectSingleNode("//item[@name='pzApacheRocket']//property[@name='BlockDamage']").value -eq 50) 'Rocket block XML mismatch'
foreach($name in @('pzApache30mm','pzApacheRocket')){
 $recipe=$recipes.SelectSingleNode("//recipe[@name='$name']")
 Check ($recipe.craft_area -eq 'workbench' -and $recipe.always_unlocked -eq 'false') 'Ammo must retain gated workbench production'
 $powder=[float]$recipe.SelectSingleNode("ingredient[@name='resourceGunPowder']").count/[float]$recipe.count
 Check ($powder -ge $(if($name -eq 'pzApache30mm'){10}else{30})) 'Ammo powder budget below support baseline'
}
# Simulate the actual heat/cooldown gate, including recovery, at 1 ms resolution.
$gate=[AECT16RuntimeFix.ApacheWeaponRules+Gate]::new()
$shotTimes=[Collections.Generic.List[double]]::new()
for($ms=0;$ms -le 180000;$ms++){
 $now=[float]($ms/1000.0)
 if($gate.Ready(1,$now)){$gate.Commit(1,$now);$shotTimes.Add($ms/1000.0)}
}
Check ($shotTimes.Count -gt 450 -and $shotTimes.Count -lt 550) 'Sustained cannon heat budget outside expected support band'
$suffixes=@('Transcendent','Ascendant','Eternal','Apocalyptic')
$expected=@(160000,240000,360000,540000)
$rows=foreach($i in 0..3){
 $tier=16+$i
 $name="AECZombieArleneTier$tier$($suffixes[$i])"
 $hp=[double]$enemies.SelectSingleNode("//entity_class[@name='$name']//passive_effect[@name='HealthMax']").value
 Check ($hp -eq $expected[$i]) "Mob health changed at T$tier; recalibrate support weapons"
 $boss=$enemies.SelectSingleNode("//effect_group[@name='PZAEC T$tier Boss Final Escalation']")
 $bossHP=[double]$boss.SelectSingleNode("passive_effect[@name='HealthMax']").value
 $resist=[double]$boss.SelectSingleNode("passive_effect[@name='PhysicalDamageResist']").value
 $shots=[int][Math]::Ceiling($hp/$cannon)
 $armored=[int][Math]::Ceiling($hp/($cannon*.4))
 Check ($shots -ge 8 -and $shots -le 27) 'Unarmored mob should take 8-27 body hits'
 Check ($rocket*3/$bossHP -lt .01) 'A full rocket salvo must remain below 1% boss HP before mitigation'
 [pscustomobject]@{Tier=$tier;MobHP=$hp;BossHP=$bossHP;BossArmor=$resist;CannonBodyShots=$shots;CannonSeconds=$shotTimes[$shots-1];Cannon60PctArmorShots=$armored;Cannon60PctArmorSeconds=$shotTimes[$armored-1];RocketFullDamageHits=[Math]::Ceiling($hp/$rocket);RocketHalfDamageHits=[Math]::Ceiling($hp/($rocket*.5))}
}
$rows|Format-Table -AutoSize
Write-Output ("180-second cannon simulation: {0} shots; average base DPS {1:N0} (all body hits, before armor/difficulty)." -f $shotTimes.Count,($shotTimes.Count*$cannon/180))
Write-Output 'PASS: current tier health, final boss escalation, actual heat-gated time-to-kill, XML/runtime damage agreement, terrain limits and ammo economy.'
Write-Output 'These are configuration-based estimates, not measured in-game damage; headshots, body parts, armor, difficulty, buffs, explosion distance/occlusion and missed shots change results.'

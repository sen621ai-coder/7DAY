$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
Add-Type -TypeDefinition (Get-Content "$root/ZZ-PZAEC_M1Abrams/Source/M1EffectRules.cs" -Raw)
$events=[PZAEC.M1.EffectRules+Events]::new()
if(!$events.Accept(12) -or !$events.Accept(10) -or $events.Accept(12) -or $events.Accept(10)){throw 'Out-of-order effects must survive; duplicates must not replay'}
foreach($fps in @(15,30,60,144)){
 $frame=1.0/$fps
 $main=[PZAEC.M1.EffectRules]::FlashLife($frame,$false)
 $mg=[PZAEC.M1.EffectRules]::FlashLife($frame,$true)
 if($main -lt .089 -or $main -gt .161 -or $mg -lt .054 -or $mg -gt .086){throw "Unbounded flash at $fps FPS"}
}
if(![PZAEC.M1.EffectRules]::Fresh(.1) -or [PZAEC.M1.EffectRules]::Fresh(.5) -or [PZAEC.M1.EffectRules]::Fresh(-1)){throw 'Fresh delayed shots must display; stale/invalid shots must not'}
Write-Output 'PASS M1 effect reordering, deduplication, lifetime and stale-event checks'
foreach($timing in @(@(.45,.12),@(.15,.06))){
 $life=$timing[0];$hold=$timing[1]
 if([PZAEC.M1.EffectRules]::Fade($hold,$life,$hold) -gt .00001){throw 'AP must retain full brightness through hold'}
 if([Math]::Abs([PZAEC.M1.EffectRules]::Fade(($life+$hold)/2,$life,$hold)-.5) -gt .00001){throw 'AP must fade gradually after hold'}
 if([PZAEC.M1.EffectRules]::Fade($life,$life,$hold) -lt .99999){throw 'AP must fully fade at expiry'}
}
Write-Output 'PASS AP brightness hold and linear fade timing'
Add-Type -TypeDefinition (Get-Content "$root/ZZ-PZAEC_M1Abrams/Source/M1ImpactRules.cs" -Raw)
foreach($surface in [Enum]::GetValues([PZAEC.M1.ImpactSurface])){
 if([PZAEC.M1.ImpactRules]::Decode([PZAEC.M1.ImpactRules]::Encode($surface)) -ne $surface){throw 'Surface wire roundtrip'}
 foreach($ap in @($true,$false)){
  $sparks=[PZAEC.M1.ImpactRules]::Sparks($ap,$surface)
  if($surface -notin @('Metal','Stone') -and $sparks -ne 0){throw 'Soft surfaces cannot spark'}
 }
}
if([PZAEC.M1.ImpactRules]::Classify('cloth','metal','Mmetal_cloth',$false) -ne 'Cloth'){throw 'Visible surface must override structural metal'}
if([PZAEC.M1.ImpactRules]::Classify('stone','stone','Msandstone',$false) -ne 'Stone'){throw 'Sandstone is not loose sand'}
foreach($surface in [Enum]::GetValues([PZAEC.M1.ImpactSurface])){if([PZAEC.M1.ImpactRules]::FireCount($true,$surface) -ne 2){throw 'Every AP impact needs one fireball and one flash'}}
if([PZAEC.M1.ImpactRules]::Dust($true,'Organic') -ne 0 -or [PZAEC.M1.ImpactRules]::Dust($false,'Organic') -ne 0){throw 'Organic impacts have no soil dust'}
foreach($ap in @($true,$false)){if([PZAEC.M1.ImpactRules]::Smoke($ap,'Water') -ne 0){throw 'Water has no lingering combustion smoke'}}
if([PZAEC.M1.ImpactRules]::FireCount($false,'Water') -ne 0){throw 'HE water policy unchanged'}
Write-Output 'PASS surface classification, wire encoding and material effect policy'

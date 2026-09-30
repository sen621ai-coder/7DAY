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

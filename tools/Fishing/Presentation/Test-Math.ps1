#Requires -Version 7.0
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path (Split-Path $PSScriptRoot))
$contracts=Get-ChildItem "$root/ZZZ-PZAEC_Fishing/Source/Contracts" -Filter '*.cs' | Sort-Object Name
Add-Type -Path ($contracts.FullName + "$root/ZZZ-PZAEC_Fishing/Source/Presentation/PresentationMath.cs" + "$root/ZZZ-PZAEC_Fishing/Source/Presentation/PresentationMotion.cs")
# Test the compiled production implementation directly; no copy of its formulas.
$passed=0
function Assert([bool]$condition,[string]$name){if(!$condition){throw "FAIL: $name"};$script:passed++;Write-Output "PASS: $name"}
function V([float]$x,[float]$y,[float]$z){[PZAEC.Fishing.Contracts.Vec3]::new($x,$y,$z)}
$points=[PZAEC.Fishing.Contracts.Vec3[]]::new(65)
$from=V 0 1 0
$to=V 4 1 0
[PZAEC.Fishing.Presentation.PresentationMath]::FillLine($from,$to,4,$points)
Assert (($points[32].Y-1) -eq 0) 'taut horizontal line stays straight'
[PZAEC.Fishing.Presentation.PresentationMath]::FillLine($from,$to,6,$points)
$length=0
for($i=1;$i -lt $points.Length;$i++){$length+=([PZAEC.Fishing.Contracts.Vec3]::op_Subtraction($points[$i],$points[$i-1])).Length}
Assert ([Math]::Abs($length-6) -lt .003) 'slack geometry respects available length'
Assert ($points[32].Y -lt 1) 'slack sags under gravity'
Assert ($points[0].X -eq 0 -and $points[64].X -eq 4) 'line endpoints remain anchored'
[PZAEC.Fishing.Presentation.PresentationMath]::FillLine($from,$to,[float]::NaN,$points)
Assert ($points[32].IsFinite -and $points[32].Y -eq 1) 'non-finite line length fails safely'
[PZAEC.Fishing.Presentation.PresentationMath]::FillLine($from,$from,0,$points)
Assert ($points[32].IsFinite) 'coincident endpoints remain finite'
$p=[PZAEC.Fishing.Presentation.PresentationMath]::RodPoint($from,$to,(V 0 0 1),4,1)
Assert (($p.X -eq 4) -and ($p.Y -eq 1)) 'bent rod ends at physical tip'
$p=[PZAEC.Fishing.Presentation.PresentationMath]::RodPoint((V 0 0 0),(V 1 0 2),(V 0 0 1),2.4,.1)
Assert ([Math]::Abs($p.X) -lt .0001 -and [Math]::Abs($p.Z-.24) -lt .0001) 'rod handle remains rigid under load'
$main=[PZAEC.Fishing.Contracts.Vec3[]]::new(33);$leader=[PZAEC.Fishing.Contracts.Vec3[]]::new(17)
$routeDeficit=[PZAEC.Fishing.Presentation.PresentationMath]::FillRoutedLine((V 0 1 0),(V 4 1 0),(V 4 -1 0),6,$main,$leader)
Assert ($routeDeficit -eq 0 -and $main[32].X -eq $leader[0].X -and $main[32].Y -eq $leader[0].Y) 'main line and leader share float attachment'
$routeDeficit=[PZAEC.Fishing.Presentation.PresentationMath]::FillRoutedLine((V 0 1 0),(V 4 1 0),(V 4 -1 0),5,$main,$leader)
Assert ([Math]::Abs($routeDeficit-1) -lt .001) 'impossible route length is exposed instead of concealed'
$motion=[PZAEC.Fishing.Presentation.PresentationMotion]::new();$motion.Step(1000,1,$false,10);$motion.Step(1000.016,3,$true,9.995)
$phase=$motion.SwimPhase;$handle=$motion.HandleDegrees
Assert ($phase -gt 0 -and $phase -lt .6) 'speed change advances swim phase continuously'
$motion.Step(1000.016,8,$true,9.995)
Assert ($motion.SwimPhase -eq $phase -and $motion.HandleDegrees -eq $handle) 'duplicate render cannot spin fish or reel'
$motion.Step(1000.032,3,$true,10.01)
Assert ($motion.HandleDegrees -eq $handle -and $motion.SpoolDegrees -ne 0) 'payout rotates spool while crank stays still'
$motion.Step(1000.048,3,$true,2)
Assert ($motion.HandleDegrees -eq $handle) 'network line correction does not spin handle'
$motion.Reset();Assert ($motion.SwimPhase -eq 0 -and $motion.HandleDegrees -eq 0) 'motion resets on session cleanup'
$pose=[PZAEC.Fishing.Contracts.RodPose]::new();$pose.Forward=V 0 0 1;$pose.PitchRadians=[Math]::PI/6;$pose.YawRadians=[Math]::PI/2
$aim=[PZAEC.Fishing.Presentation.PresentationMath]::RodAim($pose)
Assert ([Math]::Abs($aim.Y-.5) -lt .0001 -and $aim.X -gt .86 -and [Math]::Abs($aim.Z) -lt .0001) 'rod uses pitch and yaw against reference basis'
$p=[PZAEC.Fishing.Presentation.PresentationMath]::Scene((V 10005 8 20007),(V 10000 0 20000))
Assert ($p.X -eq 5 -and $p.Y -eq 8 -and $p.Z -eq 7) 'floating origin subtracted once'
$session=[guid]::NewGuid()
$gate=[PZAEC.Fishing.Presentation.PresentationEventGate]::new();$gate.Reset($session)
$e=[PZAEC.Fishing.Contracts.FishingEvent]::new();$e.SessionId=$session;$e.Sequence=1
Assert ($gate.Accept($e)) 'new event accepted'
Assert (!$gate.Accept($e)) 'duplicate event suppressed'
$e.Sequence=0;Assert (!$gate.Accept($e)) 'stale event suppressed'
$e.Sequence=2;$e.SessionId=[guid]::NewGuid();Assert (!$gate.Accept($e)) 'wrong session ignored'
$gate.Reset($e.SessionId);$e.Sequence=1;Assert ($gate.Accept($e)) 'sequence resets with new session'
$gate.Reset([guid]::Empty);Assert (!$gate.Accept($e)) 'clear rejects late events'
$frame=[PZAEC.Fishing.Contracts.RenderFrame]::new()
$s=[PZAEC.Fishing.Contracts.FishingSnapshot]::new();$s.SessionId=$session;$s.Tick=10;$s.Phase=[PZAEC.Fishing.Contracts.FishingPhase]::Waiting
$frame.Previous=$s;$s.Tick=11;$frame.Current=$s
Assert ([PZAEC.Fishing.Presentation.PresentationMath]::CanInterpolate($frame)) 'adjacent frames interpolate'
$s.Phase=[PZAEC.Fishing.Contracts.FishingPhase]::Fighting;$frame.Current=$s
Assert (![PZAEC.Fishing.Presentation.PresentationMath]::CanInterpolate($frame)) 'phase change snaps instead of interpolating incompatible state'
$s.LineTensionNewtons=[float]::NaN;Assert (![PZAEC.Fishing.Presentation.PresentationMath]::Valid($s)) 'invalid snapshot rejected'
Write-Output "PASS: $passed production geometry/event tests"

#Requires -Version 7.0
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
$mod=Join-Path $root 'ZZZ-PZAEC_Surveillance'
[xml]$blocks=Get-Content (Join-Path $mod 'Config/blocks.xml')
[xml]$recipes=Get-Content (Join-Path $mod 'Config/recipes.xml')
[xml]$info=Get-Content (Join-Path $mod 'ModInfo.xml')
$screen=$blocks.configs.append.block|Where-Object name -eq 'PZAEC_SurveillanceScreen4x3'
$camera=$blocks.configs.append.block|Where-Object name -eq 'PZAEC_SurveillanceCamera'
function Property($block,$name){($block.property|Where-Object name -eq $name).value}
if(!$screen -or !$camera){throw 'Surveillance blocks missing'}
if((Property $screen 'MultiBlockDim') -ne '4,3,1'){throw 'Screen is not 4x3x1'}
if(Property $screen 'OversizedBounds'){throw 'Screen must use native multiblock support, not oversized bottom-only support'}
if((Property $screen 'RequiredPower') -ne '30'){throw 'Screen power mismatch'}
if((Property $screen 'StabilitySupport') -ne 'true'){throw 'Screen must support its upper occupied rows'}
if((Property $camera 'RequiredPower') -ne '5'){throw 'Camera power mismatch'}
if((Property $screen 'Class') -ne 'PZAEC_SurveillanceScreen,PZAEC.Surveillance'){throw 'Screen runtime class binding mismatch'}
if((Property $camera 'Class') -ne 'PZAEC_SurveillanceCamera,PZAEC.Surveillance'){throw 'Camera runtime class binding mismatch'}
if(@($recipes.configs.append.recipe).Count -ne 2){throw 'Recipe count mismatch'}
if($info.xml.Version.value -ne '1.0.10'){throw 'ModInfo version mismatch'}
$dll=Join-Path $mod 'PZAEC.Surveillance.dll';if(!(Test-Path $dll)){throw 'Compiled DLL missing'}
$source=Get-Content (Join-Path $mod 'Source/SurveillanceState.cs') -Raw
if($source -notmatch 'WirelessRange=128f'){throw 'Wireless range contract missing'}
$render=Get-Content (Join-Path $mod 'Source/SurveillanceRenderService.cs') -Raw
if($render -notmatch 'active.Count>=RenderPolicy.MaxActive'){throw 'Client budget gate missing'}
if($source -notmatch 'writes are disabled for this session'){throw 'Corrupt-save preservation guard missing'}
if($source -notmatch 'expectedRevision!=s\.Revision'){throw 'Concurrent configuration revision guard missing'}
$protocol=Get-Content (Join-Path $mod 'Source/SurveillanceProtocol.cs') -Raw
if($protocol -notmatch 'NetPackagePZSurveillanceResult'){throw 'Server configuration result package missing'}
& (Join-Path $PSScriptRoot 'Test-Policy.ps1')
& (Join-Path $PSScriptRoot 'Test-Markers.ps1')
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Test-ScreenRepair.ps1')
if($LASTEXITCODE -ne 0){throw 'Screen placement/interaction regression tests failed'}
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Test-PreviewLifetime.ps1')
if($LASTEXITCODE -ne 0){throw 'Preview resource lifetime tests failed'}
# The state harness uses replacement world/network types; isolate it from the main process.
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Test-MarkerState.ps1')
if($LASTEXITCODE -ne 0){throw 'Marker state/network tests failed'}
Write-Output 'PASS: block/config, render scheduling, marker classification/projection, persistence and wire round-trips.'

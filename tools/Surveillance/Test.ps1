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
if((Property $screen 'OversizedBounds') -ne '(-0.5,1,0),(4,3,1)'){throw 'Screen bounds do not match native x=-2..1, y=0..2 footprint'}
if((Property $screen 'RequiredPower') -ne '30'){throw 'Screen power mismatch'}
if((Property $camera 'RequiredPower') -ne '5'){throw 'Camera power mismatch'}
if((Property $screen 'Class') -ne 'PZAEC_SurveillanceScreen,PZAEC.Surveillance'){throw 'Screen runtime class binding mismatch'}
if((Property $camera 'Class') -ne 'PZAEC_SurveillanceCamera,PZAEC.Surveillance'){throw 'Camera runtime class binding mismatch'}
if(@($recipes.configs.append.recipe).Count -ne 2){throw 'Recipe count mismatch'}
if($info.xml.Version.value -ne '1.0.0'){throw 'ModInfo version mismatch'}
$dll=Join-Path $mod 'PZAEC.Surveillance.dll';if(!(Test-Path $dll)){throw 'Compiled DLL missing'}
$source=Get-Content (Join-Path $mod 'Source/SurveillanceState.cs') -Raw
if($source -notmatch 'WirelessRange=128f'){throw 'Wireless range contract missing'}
$render=Get-Content (Join-Path $mod 'Source/SurveillanceRenderService.cs') -Raw
if($render -notmatch 'new RenderTexture\(768,576,16'){throw 'Standard render target contract missing'}
if($render -notmatch 'admitted\.Count>=2'){throw 'Two-stream client budget missing'}
if($source -notmatch 'writes are disabled for this session'){throw 'Corrupt-save preservation guard missing'}
if($source -notmatch 'expectedRevision!=s\.Revision'){throw 'Concurrent configuration revision guard missing'}
$protocol=Get-Content (Join-Path $mod 'Source/SurveillanceProtocol.cs') -Raw
if($protocol -notmatch 'NetPackagePZSurveillanceResult'){throw 'Server configuration result package missing'}
Write-Output 'PASS: 4x3 screen, independent power, 128-block wireless binding, persistence, recipes and DLL are present.'

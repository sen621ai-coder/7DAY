#Requires -Version 7.0
$ErrorActionPreference='Stop'
$state=Get-Content (Join-Path $PSScriptRoot 'artifacts/seed-session.json') -Raw|ConvertFrom-Json
$process=Get-Process -Id $state.ProcessId -ErrorAction SilentlyContinue
if($process -and $process.ProcessName -eq '7DaysToDie' -and [Math]::Abs(($process.StartTime-[datetime]$state.StartTime).TotalSeconds) -lt 1){throw 'The save bootstrap is still running; wait for normal shutdown.'}
$expected=[IO.Path]::GetFullPath((Join-Path $env:APPDATA '7DaysToDie/Saves/Navezgane/FishingTest'))
if([IO.Path]::GetFullPath($state.Target) -ne $expected){throw 'Unexpected destination.'}
$stagePrefix=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'artifacts'))+[IO.Path]::DirectorySeparatorChar
if(![IO.Path]::GetFullPath($state.Source).StartsWith($stagePrefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Unexpected source.'}
if(Test-Path -LiteralPath $expected){throw 'Existing test save will not be overwritten.'}
if(!(Select-String -LiteralPath $state.Log -Pattern '\[FishingTestSeed\] SAVED' -Quiet)){throw 'No successful save marker.'}
if(!(Test-Path -LiteralPath (Join-Path $state.Source 'main.ttw'))){throw 'Missing native world state.'}
Copy-Item -LiteralPath $state.Source -Destination $expected -Recurse
$kit=New-Object Xml.XmlDocument
$root=$kit.CreateElement('FishingTestKit');$kit.AppendChild($root)|Out-Null
$root.SetAttribute('id',[guid]::NewGuid().ToString('D'));$root.SetAttribute('savePath',$expected)
foreach($gift in @(@('pzaecFishingRodBasic',2),@('pzaecFishingBaitWorm',200),@('pzaecFishingMealGrilled',10))){
    $item=$kit.CreateElement('Item');$item.SetAttribute('name',$gift[0]);$item.SetAttribute('count',[string]$gift[1]);$root.AppendChild($item)|Out-Null
}
$kitPath=Join-Path $expected 'pzaec-fishing-test-kit.xml';$kit.Save($kitPath)
[pscustomobject]@{Save=$expected;KitRequest=$kitPath;Delivery='First local player login; two rods, 200 worms, 10 grilled fish'}|ConvertTo-Json

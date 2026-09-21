$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
$files=@((Join-Path $PSScriptRoot 'LifecycleFixture.cs'),(Join-Path $root 'ZZZ-PZAEC_BasementLight/Source/BasementLight.cs'),(Join-Path $root 'ZZZ-PZAEC_BasementLight/Source/Photometry.cs'))
Add-Type -Path $files
[BasementLifecycleTests]::Run()

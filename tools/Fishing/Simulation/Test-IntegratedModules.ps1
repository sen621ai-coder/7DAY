#Requires -Version 7.0
$ErrorActionPreference='Stop'
$intermediate=Join-Path $PSScriptRoot 'artifacts/modules/obj/'
$modPath=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../ZZZ-PZAEC_Fishing'))
$result = dotnet run --project (Join-Path $PSScriptRoot 'Regression.Modules.csproj') --configuration Release "-p:BaseIntermediateOutputPath=$intermediate" -- $modPath
$code=$LASTEXITCODE
$result | Tee-Object -FilePath (Join-Path $PSScriptRoot 'artifacts/modules/results.txt')
if($code -ne 0){throw 'B real-module regression failed.'}

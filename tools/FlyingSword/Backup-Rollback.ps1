#Requires -Version 7.0
param([ValidateSet('Backup','Disable','Enable')][string]$Mode='Backup')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Split-Path (Split-Path $PSScriptRoot)))
$mod=Join-Path $root 'ZZ-PZAEC_FlyingSword'
if(!(Test-Path -LiteralPath $mod -PathType Container)){throw 'FlyingSword directory is absent.'}
if($Mode -ne 'Backup' -and (Get-Process 7DaysToDie,7DaysToDieServer -ErrorAction SilentlyContinue)){throw 'Exit the game before changing mod activation.'}
$manifest=Join-Path $mod 'ModInfo.xml';$disabled=Join-Path $mod 'ModInfo.xml.disabled'
if($Mode -eq 'Enable'){
 if((Test-Path -LiteralPath $disabled) -and !(Test-Path -LiteralPath $manifest)){Move-Item -LiteralPath $disabled -Destination $manifest}
 Write-Output 'FlyingSword enabled for the next game launch.'
 return
}
$backup=Join-Path $root ('.local-tests/FlyingSword/releases/'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N').Substring(0,6))
New-Item -ItemType Directory -Path $backup -Force|Out-Null
Copy-Item -LiteralPath $mod -Destination (Join-Path $backup 'ZZ-PZAEC_FlyingSword') -Recurse
Get-ChildItem -LiteralPath $mod -Recurse -File|ForEach-Object { $hash=Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256;[pscustomobject]@{Path=[IO.Path]::GetRelativePath($mod,$_.FullName);SHA256=$hash.Hash}}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $backup 'sha256.json') -Encoding utf8
Write-Output ('BACKUP='+$backup)
if($Mode -eq 'Disable'){
 if(Test-Path -LiteralPath $disabled){throw 'Disabled manifest already exists; no files replaced.'}
 if(Test-Path -LiteralPath $manifest){Move-Item -LiteralPath $manifest -Destination $disabled}
 Write-Output 'FlyingSword disabled; all mod files and saves preserved.'
}

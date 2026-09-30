#Requires -Version 7.0
param()
$ErrorActionPreference='Stop'
$state=Get-Content (Join-Path $PSScriptRoot 'artifacts/native-session.json') -Raw|ConvertFrom-Json
$process=Get-Process -Id $state.ProcessId -ErrorAction SilentlyContinue
if(!$process){Write-Output 'Probe already stopped';exit}
if($process.ProcessName -ne '7DaysToDie' -or [Math]::Abs(($process.StartTime-[datetime]$state.StartTime).TotalSeconds) -gt 1){throw 'PID no longer identifies the probe process.'}
$prefix=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'artifacts'))+[IO.Path]::DirectorySeparatorChar
if(![IO.Path]::GetFullPath($state.QaRoot).StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Unexpected probe workspace.'}
Stop-Process -Id $process.Id -Force
Write-Output ('Stopped isolated probe PID '+$state.ProcessId)

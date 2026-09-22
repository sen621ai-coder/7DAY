#Requires -Version 7.0
$ErrorActionPreference='Stop'
$qaBase=[IO.Path]::GetFullPath((Join-Path (Split-Path $PSScriptRoot) '.local-tests/CargoDrones/NativeQA'))
$session=Get-Content -LiteralPath (Join-Path $qaBase 'session.json') -Raw | ConvertFrom-Json
$run=[IO.Path]::GetFullPath($session.QaRoot)
if(-not $session.CrashAfterNativeSave -or -not $run.StartsWith($qaBase+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Not an authorized isolated crash-test session.'}
$ready=Join-Path $run 'UserData/Saves/Navezgane/CargoDroneQA_Isolated/cargo-crash-ready.txt'
if(-not(Test-Path -LiteralPath $ready)){throw 'Native save has not reached the requested interruption point.'}
$qaProcess=Get-Process -Id $session.ProcessId -ErrorAction Stop
if($qaProcess.ProcessName -ne '7DaysToDie' -or [Math]::Abs(($qaProcess.StartTime-[datetime]$session.Started).TotalSeconds) -gt 1){throw 'Process identity mismatch; refusing to stop it.'}
Stop-Process -Id $qaProcess.Id -Force
if(-not $qaProcess.WaitForExit(10000)){throw 'QA process did not exit; no crash receipt written.'}
$receipt=[pscustomobject]@{ProcessId=$session.ProcessId;Started=$session.Started;StoppedUtc=[datetime]::UtcNow.ToString('o');Point='AfterNativeSaveBeforeCommit'} | ConvertTo-Json
$bytes=[Text.Encoding]::UTF8.GetBytes($receipt)
$file=[IO.File]::Open((Join-Path $run 'crash-killed.json'),[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
try{$file.Write($bytes,0,$bytes.Length);$file.Flush($true)}finally{$file.Dispose()}
"Killed only isolated QA PID $($session.ProcessId) at the recorded native-save checkpoint. Restart source: $run"

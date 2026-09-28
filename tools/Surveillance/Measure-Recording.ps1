#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$EventsPath)
$ErrorActionPreference='Stop'
$rows=@(Get-Content -LiteralPath $EventsPath | Where-Object { !$_.StartsWith('#') } | ConvertFrom-Csv)
$culture=[Globalization.CultureInfo]::InvariantCulture
function Number($value){[double]::Parse($value,$culture)}
function Summary($values){
  $sorted=@($values|Sort-Object)
  if(!$sorted.Count){return $null}
  [pscustomobject]@{Count=$sorted.Count;Mean=($sorted|Measure-Object -Average).Average;P95=$sorted[[math]::Ceiling(.95*$sorted.Count)-1];Max=$sorted[-1]}
}
$frames=@($rows|Where-Object event -eq '0')
$success=@($rows|Where-Object event -eq '1')
$failure=@($rows|Where-Object event -eq '2')
$feeds=@(foreach($group in ($success|Group-Object camera)){
  $times=@($group.Group|ForEach-Object {Number $_.seconds})
  $first=@($group.Group|Where-Object { (Number $_.wakeToFirstFrameMs) -ge 0 }|ForEach-Object {Number $_.wakeToFirstFrameMs})
  $gaps=@($group.Group|Where-Object { (Number $_.previousFrameAgeMs) -ge 0 }|ForEach-Object {Number $_.previousFrameAgeMs})
  [pscustomobject]@{
    Camera=$group.Name
    SuccessfulRenders=$group.Count
    FailedRenders=@($failure|Where-Object camera -eq $group.Name).Count
    UpdatesPerSecondDuringCapture=if($times.Count -gt 1 -and $times[-1] -gt $times[0]){($times.Count-1)/($times[-1]-$times[0])}else{0}
    RenderCpuMs=Summary @($group.Group|ForEach-Object {Number $_.durationMs})
    SuccessfulUpdateIntervalMs=Summary $gaps
    WakeToFirstSampleMs=Summary $first
  }
})
[pscustomobject]@{
  File=(Resolve-Path -LiteralPath $EventsPath).Path
  Header=Get-Content -LiteralPath $EventsPath -TotalCount 1
  GameFrameMs=Summary @($frames|ForEach-Object {Number $_.durationMs})
  Cameras=$feeds
  Note='CPU/sample timing only; not GPU or monitor presentation latency. Frequency includes inactive gaps between first and last samples. Check header for overwritten samples.'
}|ConvertTo-Json -Depth 7

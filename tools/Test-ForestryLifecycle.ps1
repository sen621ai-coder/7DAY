$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
Add-Type -Path @((Join-Path $root '99-AEC_T16_RuntimeFix/Source/ForestryPreflight.cs'),(Join-Path $root '99-AEC_T16_RuntimeFix/Source/ForestryRetryState.cs'))
[AECT16RuntimeFix.ForestryPreflight]::Validate((Join-Path $root '98-AECxProjectZ_Tweaks/Resources/Forestry'))
$scratch=Join-Path $root ('.local-tests/forestry-preflight-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $scratch | Out-Null
Copy-Item (Join-Path $root '98-AECxProjectZ_Tweaks/Resources/Forestry/*.png') $scratch
Copy-Item (Join-Path $root '98-AECxProjectZ_Tweaks/Resources/Forestry/*.meshbin') $scratch
function ExpectFailure($folder,$tokens){
    try{[AECT16RuntimeFix.ForestryPreflight]::Validate($folder);throw 'Preflight accepted broken input'}
    catch{foreach($token in $tokens){if($_.Exception.ToString() -notlike "*$token*"){throw}}}
}
try{
    Remove-Item -LiteralPath (Join-Path $scratch 'normal3.png')
    [IO.File]::WriteAllBytes((Join-Path $scratch 'sawmill-lod.meshbin'),[byte[]](0,0,0,0))
    ExpectFailure $scratch @('normal3.png','sawmill-lod.meshbin')
    $retry=[AECT16RuntimeFix.ForestryRetryState]::new()
    $retry.Fail(10)
    if($retry.Ready(14.9) -or !$retry.Ready(15)){throw 'First retry delay wrong'}
    $retry.Fail(15)
    if($retry.Ready(29) -or !$retry.Ready(30)){throw 'Second retry delay wrong'}
    $retry.Fail(30)
    if($retry.Ready(59) -or !$retry.Ready(60)){throw 'Third retry delay wrong'}
    $retry.Fail(60)
    if($retry.Ready(10000)){throw 'Retry budget not bounded'}
    $retry.Restart(10001)
    if(!$retry.Ready(10001) -or $retry.Failures -ne 0){throw 'A confirmed resource repair did not reopen the retry budget'}
    $retry.Reset()
    if($retry.Pending -or $retry.Failures -ne 0){throw 'Unload/rebind did not cancel retry state'}
    $retry.Fail(0);$retry.Recovered()
    if($retry.Pending -or $retry.Ready(100)){throw 'Recovered binding retried again'}
    Write-Output 'PASS: full resource preflight; combined missing/corrupt diagnostics; 5/15/30s retries; exhaustion; unload reset; successful recovery.'
}finally{
    $resolved=[IO.Path]::GetFullPath($scratch)
    $expected=[IO.Path]::GetFullPath((Join-Path $root '.local-tests'))+[IO.Path]::DirectorySeparatorChar
    if(!$resolved.StartsWith($expected,[StringComparison]::OrdinalIgnoreCase)){throw 'Scratch path escaped workspace'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

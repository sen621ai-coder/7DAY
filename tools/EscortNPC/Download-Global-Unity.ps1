#Requires -Version 7.0
$ErrorActionPreference='Stop'
$url='https://download.unity3d.com/download_unity/7670c08855a9/Windows64EditorInstaller/UnitySetup64-2022.3.62f2.exe'
$directory='E:/soft/UnityInstallers'
$destination=Join-Path $directory 'UnitySetup64-2022.3.62f2-global.exe'
New-Item -ItemType Directory -Force -Path $directory | Out-Null
$headers=@(& curl.exe -sS -I --connect-timeout 15 --max-time 30 $url)
if($LASTEXITCODE -ne 0){throw 'Cannot reach official global Unity download host.'}
if(($headers -join "`n") -match '(?im)^Location:'){
    throw 'Official URL redirects instead of serving the global build. Fix route first; no download was started.'
}
$status=@($headers | Where-Object {$_ -match '^HTTP/'})[-1]
if($status -notmatch '\s200\s'){throw "Unexpected HTTP response: $status"}
$sizeLine=@($headers | Where-Object {$_ -match '(?i)^Content-Length:'})[-1]
if(-not $sizeLine){throw 'Missing expected download length'}
$expected=[long]($sizeLine -replace '(?i)^Content-Length:\s*','')
# Do not follow redirects on GET either; a changing regional route must fail.
& curl.exe --fail --retry 3 --connect-timeout 20 --continue-at - --output $destination $url
if($LASTEXITCODE -ne 0){throw 'Download failed; partial file retained.'}
if((Get-Item -LiteralPath $destination).Length -ne $expected){throw 'Incomplete installer or changed regional route.'}
$sig=Get-AuthenticodeSignature -LiteralPath $destination
if($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'Unity Technologies'){
    throw "Expected a valid global Unity Technologies signature; got $($sig.Status), $($sig.SignerCertificate.Subject)"
}
Get-FileHash -LiteralPath $destination -Algorithm SHA256
'Global Unity installer downloaded. Verify installed ProductVersion before using the project.'

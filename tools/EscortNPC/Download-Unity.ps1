# Resume the verified official editor download with eight HTTP byte ranges.
# Each part is length-checked; final executable must also pass Authenticode.
#Requires -Version 7.0
$ErrorActionPreference='Stop'
$destination='E:/soft/UnityInstallers/UnitySetup64-2022.3.62f2.exe'
$url='https://download.unitychina.cn/download_unity/7670c08855a9/Windows64EditorInstaller/UnitySetup64-2022.3.62f2.exe'
$total=3754403072L
$offset=(Get-Item -LiteralPath $destination).Length
if ($offset -gt $total) { throw 'Existing download exceeds expected size' }
if ($offset -lt $total) {
    $block=[long][math]::Ceiling(($total-$offset)/8)
    $parts=@(); $jobs=@()
    for($i=0;$i -lt 8;$i++) {
        $start=$offset+$i*$block
        if($start -ge $total){break}
        $end=[math]::Min($total-1,$start+$block-1)
        $part="$destination.part$i"
        $parts+=@{Path=$part;Length=($end-$start+1)}
        $jobs+=Start-ThreadJob -ArgumentList $url,$start,$end,$part -ScriptBlock {
            param($uri,$first,$last,$output)
            & curl.exe -sS --fail --location --retry 3 --connect-timeout 30 --max-time 1800 --range "$first-$last" --output $output $uri
            if($LASTEXITCODE -ne 0){throw "Range download failed: $first-$last"}
        }
    }
    $jobs | Wait-Job | Receive-Job -ErrorAction Stop
    foreach($part in $parts) { if((Get-Item -LiteralPath $part.Path).Length -ne $part.Length){throw "Invalid range size: $($part.Path)"} }
    $stream=[IO.File]::Open($destination,[IO.FileMode]::Append)
    try { foreach($part in $parts) { $inputStream=[IO.File]::OpenRead($part.Path);try{$inputStream.CopyTo($stream)}finally{$inputStream.Dispose()} } }
    finally{$stream.Dispose()}
}
if((Get-Item -LiteralPath $destination).Length -ne $total){throw 'Final size mismatch'}
$signature=Get-AuthenticodeSignature -LiteralPath $destination
if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Unity|优三缔科技') {throw "Invalid Unity signature: $($signature.Status)"}
Get-FileHash -LiteralPath $destination -Algorithm SHA256
'Official Unity editor downloaded and signature verified.'

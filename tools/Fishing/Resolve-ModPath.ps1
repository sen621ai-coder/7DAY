# The game loads either directory when ModInfo.xml is present, even with an _disabled prefix.
$fishingModsRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
foreach($fishingFolder in @('ZZZ-PZAEC_Fishing','_disabled-ZZZ-PZAEC_Fishing')) {
    $fishingCandidate=Join-Path $fishingModsRoot $fishingFolder
    if(Test-Path -LiteralPath (Join-Path $fishingCandidate 'ModInfo.xml')) {return $fishingCandidate}
}
throw 'Fishing mod with ModInfo.xml not found.'

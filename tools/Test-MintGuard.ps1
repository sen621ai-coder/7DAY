#Requires -Version 7.0
$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path (Split-Path -Parent $PSScriptRoot) '96-SakuraPreview/Source/SakuraMissionState.cs')
function Assert($value,$label){if(-not $value){throw $label}}
foreach($tier in 16..19){
    $state=[SakuraPreview.SakuraMissionState]::new()
    $state.Guard=$true;$state.Tier=$tier;$state.StartDistance=800
    $member=[SakuraPreview.EscortMember]::new();$member.Key='A';$state.Members.Add($member)
    $near=[Collections.Generic.HashSet[string]]::new();[void]$near.Add('A')
    for($wave=0;$wave -lt $state.WaveCount;$wave++){
        1..12 | ForEach-Object {$state.Tick(1,$true,$true,0,$near)}
        Assert $state.Active 'Cannot complete before every wave'
        Assert ($state.WantWave(0,$true)) 'Guard waves require no travel progress'
        $ids=[Collections.Generic.List[int]]::new()
        1..$state.WaveSize | ForEach-Object {$ids.Add(1000*$wave+$_)}
        $state.CommitWave($ids)
        Assert (-not $state.WantWave(0,$true)) 'No overlapping waves'
        foreach($id in @($ids.ToArray())){$state.EnemyObserved($id,$true,$true)}
    }
    1..65 | ForEach-Object {$state.Tick(1,$true,$true,0,$near)}
    Assert ($state.Phase -eq 'Completed') 'Guard completes at original position'
    Assert ($state.BeginClaim('A')) 'Eligible defender can claim'
    Assert (-not $state.BeginClaim('A')) 'No duplicate rewards'
}
$state=[SakuraPreview.SakuraMissionState]::new();$state.Guard=$true;$state.Tier=16
$state.Tick(1,$true,$true,6,$null)
Assert ($state.Phase -eq 'Failed') 'Moved defender fails rather than becoming escort'
$state=[SakuraPreview.SakuraMissionState]::new();$state.Guard=$true;$state.Tier=16
$state.Tick(1,$false,$true,0,$null)
Assert ($state.Phase -eq 'Failed') 'Defender death fails'
'PASS: T16-T19 stationary waves, completion, single reward, displacement and death.'

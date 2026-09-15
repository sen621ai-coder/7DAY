#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $PSScriptRoot 'Source/EscortSession.cs')
function Assert-Escort($condition, $message) { if (-not $condition) { throw $message } }
function New-Session($tier) {
    $session = [SakuraEscort.EscortSession]::new(); $session.Tier = $tier
    $site = [SakuraEscort.TraderSite]::new(); $site.Id='near'; $site.X=1000; $site.Enabled=$true
    $far = [SakuraEscort.TraderSite]::new(); $far.Id='far'; $far.X=2000; $far.Enabled=$true
    $disabled = [SakuraEscort.TraderSite]::new(); $disabled.Id='disabled'; $disabled.X=500
    Assert-Escort ($session.Accept('host',3,0,0,[SakuraEscort.TraderSite[]]@($far,$disabled,$site),[string[]]@('friend'))) 'Accept failed'
    Assert-Escort ($session.Trader -eq 'near') 'Did not choose nearest enabled destination'
    return $session
}
foreach ($tier in 16..19) {
    $s=New-Session $tier
    Assert-Escort (-not $s.Accept('intruder',2,0,0,[SakuraEscort.TraderSite[]]@(),[string[]]@())) 'Double acceptance allowed'
    Assert-Escort (-not $s.SetPaused('intruder',$true,2)) 'Outsider controlled NPC'
    Assert-Escort (-not $s.SetPaused('host',$true,[double]::NaN)) 'Invalid distance accepted'
    Assert-Escort (-not $s.BeginReward('host')) 'Early reward allowed'
    foreach ($i in 1..12) { $s.Tick(5,$true,$true,[string[]]@('host','friend'),0,0,$false,$false) }
    # Arriving quickly cannot bypass any required ambush.
    $s.Tick(1,$true,$true,[string[]]@('host','friend'),1000,0,$true,$false)
    Assert-Escort ($s.State -eq 'Following') 'Completed before ambushes'
    foreach ($wave in 1..[SakuraEscort.EscortSession]::WaveCount($tier)) {
        Assert-Escort ($s.MarkWaveStarted()) 'Wave did not start'
        Assert-Escort (-not $s.MarkWaveStarted()) 'Duplicate simultaneous wave'
        $s.Tick(1,$true,$true,[string[]]@('host','friend'),1000,0,$true,$false)
        Assert-Escort ($s.State -eq 'Following') 'Completed with active wave'
        $s.MarkWaveCleared()
    }
    $s.Tick(1,$true,$true,[string[]]@('host','friend'),1000,0,$true,$false)
    Assert-Escort ($s.State -eq 'Completed') 'Completion failed'
    Assert-Escort (-not $s.BeginReward('intruder')) 'Outsider reward allowed'
    Assert-Escort ($s.BeginReward('host')) 'Reward claim failed'
    Assert-Escort (-not $s.BeginReward('host')) 'Pending reward repeated'
    Assert-Escort ($s.ConfirmReward('host')) 'Reward confirmation failed'
    Assert-Escort (-not $s.BeginReward('host')) 'Reward granted twice'
    Assert-Escort ($s.BeginReward('friend')) 'Participating teammate excluded'
    $death=New-Session $tier
    $death.Tick(1,$false,$true,[string[]]@('host'),0,0,$false,$false)
    Assert-Escort ($death.State -eq 'Failed') 'NPC death did not fail mission'
    $drop=New-Session $tier
    $drop.Tick(1,$true,$false,[string[]]@('friend'),0,0,$false,$false)
    Assert-Escort ($drop.State -eq 'Paused') 'Missing leader did not pause'
    Assert-Escort (-not $drop.TakeOver('intruder',2,$false)) 'Outsider took over'
    Assert-Escort ($drop.TakeOver('friend',2,$false)) 'Teammate takeover failed'
}
'PASS: T16-T19 destination, duplicate acceptance, permissions, wave gates, NPC death, disconnect takeover and reward receipts.'

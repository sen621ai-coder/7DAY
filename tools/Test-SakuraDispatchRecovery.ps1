#Requires -Version 7.0
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
Add-Type -Path (Join-Path $root '96-SakuraPreview/Source/SakuraDispatchPolicy.cs')
function Assert([bool]$ok,[string]$label){if(-not $ok){throw $label}}
foreach($tier in 16..19){
    foreach($prefix in @('mint','sakura')){
        $id="${prefix}DispatchT$tier"
        Assert ([SakuraPreview.SakuraDispatchPolicy]::Tier($id) -eq $tier) "Recognize $id"
        Assert ([SakuraPreview.SakuraDispatchPolicy]::Tier($id.ToLowerInvariant()) -eq $tier) "Recognize normalized $id"
        Assert ([SakuraPreview.SakuraDispatchPolicy]::ShouldRetry($id,$false,'1',[float]::MaxValue)) "Recover completed $id without receipt"
    }
}
Assert ([SakuraPreview.SakuraDispatchPolicy]::Tier('mintGuardT16') -eq 0) 'Never reconcile an active guard mission as a blueprint'
Assert ([SakuraPreview.SakuraDispatchPolicy]::IsMint('MINTDISPATCHT16')) 'Normalized Mint must spawn Mint, not Sakura'
Assert (-not [SakuraPreview.SakuraDispatchPolicy]::IsMint('sakuraDispatchT16')) 'Sakura dispatch must retain its own target type'
Assert (-not [SakuraPreview.SakuraDispatchPolicy]::ShouldRetry('mintDispatchT16',$true,'1',100)) 'Failed blueprint must not re-register'
Assert (-not [SakuraPreview.SakuraDispatchPolicy]::ShouldRetry('mintDispatchT16',$false,'1',4.9)) 'Unconfirmed retry is rate limited'
Assert ([SakuraPreview.SakuraDispatchPolicy]::ShouldRetry('mintDispatchT16',$false,'1',5)) 'Unconfirmed request retries after five seconds'
Assert (-not [SakuraPreview.SakuraDispatchPolicy]::ShouldRetry('mintDispatchT16',$false,'2',29.9)) 'Confirmed request is polled less often'
Assert ([SakuraPreview.SakuraDispatchPolicy]::ShouldRetry('mintDispatchT16',$false,'2',30)) 'Confirmed request rechecks server registration'
foreach($state in 0..4){Assert (-not [string]::IsNullOrWhiteSpace([SakuraPreview.SakuraDispatchPolicy]::StatusText([byte]$state))) "Status $state must be visible"}
'PASS: T16-T19 and normalized quest IDs; lost/confirmed receipt retries; failed quests excluded; distinct server status text.'

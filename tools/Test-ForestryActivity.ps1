$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
# Execute the pure policy from the shipped source; Unity integration is compiled
# against installed game assemblies separately, and still needs an in-game check.
$source=Get-Content (Join-Path $root '99-AEC_T16_RuntimeFix/Source/AutoForestryActivity.cs') -Raw
$start=$source.IndexOf('    public static class ForestryVisualState')
$end=$source.IndexOf('    public sealed class AutoForestryActivity')
if($start -lt 0 -or $end -le $start){throw 'Forestry presentation policy not found'}
Add-Type -TypeDefinition ('using System; namespace AECT16RuntimeFix {'+$source.Substring($start,$end-$start)+'}')
function Check($value,$expected,$message){if($value -ne $expected){throw "$message : got $value, expected $expected"}}
foreach($capacity in @(18,36)){
    Check ([AECT16RuntimeFix.ForestryVisualState]::LogCount(0,$capacity)) 0 'Empty store must show no logs'
    Check ([AECT16RuntimeFix.ForestryVisualState]::LogCount(1,$capacity)) 1 'First product must appear'
    Check ([AECT16RuntimeFix.ForestryVisualState]::LogCount($capacity,$capacity)) 5 'Full store'
    $previous=0
    foreach($stored in 0..$capacity){
        $current=[AECT16RuntimeFix.ForestryVisualState]::LogCount($stored,$capacity)
        if($current -lt $previous -or $current -gt 5){throw 'Inventory appearance must be monotonic and bounded'}
        $previous=$current
    }
}
Check ([AECT16RuntimeFix.ForestryVisualState]::LogCount(36,18)) 5 'Removing packer preserves an over-capacity appearance'
Check ([AECT16RuntimeFix.ForestryVisualState]::LogCount(5,0)) 0 'No enabled output slots'
Check ([AECT16RuntimeFix.ForestryVisualState]::Status($false,$false,$true,$true,$false)) 0 'Unsynchronized client stays idle'
Check ([AECT16RuntimeFix.ForestryVisualState]::Status($true,$false,$true,$true,$false)) 1 'Fueled working state'
Check ([AECT16RuntimeFix.ForestryVisualState]::Status($true,$false,$false,$true,$false)) 2 'Full storage stops'
Check ([AECT16RuntimeFix.ForestryVisualState]::Status($true,$false,$true,$false,$false)) 3 'Insufficient fuel stops'
Check ([AECT16RuntimeFix.ForestryVisualState]::Status($true,$true,$true,$true,$false)) 4 'Blocked or submerged stops'
Check ([AECT16RuntimeFix.ForestryVisualState]::Status($true,$false,$true,$true,$true)) 0 'Native disabled state takes precedence'
Check ([AECT16RuntimeFix.ForestryVisualState]::Status($true,$false,$false,$false,$true)) 2 'Full storage shown ahead of fuel warning'
Write-Output 'PASS: empty/partial/full stores; packer removal; unknown sync; running, full, fuel, obstruction and native stop states.'

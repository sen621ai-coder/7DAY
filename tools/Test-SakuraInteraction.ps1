#Requires -Version 7.0
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
Add-Type -Path (Join-Path $root '96-SakuraPreview/Source/SakuraInteractionState.cs')
$state=[SakuraPreview.SakuraInteractionState]::new()
function Assert-Equal($actual,$expected,$name){if($actual -ne $expected){throw "$name expected $expected, got $actual"}}
Assert-Equal ($state.Handle($false,$true,10,1,2,1)) 255 'Client cannot author state'
Assert-Equal ($state.Handle($true,$true,10,17,2,1)) 255 'Out of reach'
Assert-Equal ($state.Handle($true,$true,10,[float]::NaN,2,1)) 255 'Invalid distance'
Assert-Equal ($state.Handle($true,$false,10,1,2,1)) 255 'Dead actor'
Assert-Equal ($state.Handle($true,$true,10,1,9,1)) 255 'Unknown command'
Assert-Equal ($state.Handle($true,$true,10,16,2,1)) 2 'Follow at valid boundary'
Assert-Equal $state.Leader 10 'Accepted leader'
Assert-Equal ($state.Handle($true,$true,11,1,3,1.1)) 255 'Rate limit'
Assert-Equal ($state.Handle($true,$true,11,1,3,2)) 5 'Other player cannot stop'
Assert-Equal ($state.Handle($true,$true,11,1,2,3)) 5 'Other player cannot steal'
Assert-Equal $state.Leader 10 'Ownership retained'
Assert-Equal ($state.Handle($true,$true,11,1,4,4)) 4 'Other player may chat'
Assert-Equal ($state.Handle($true,$true,10,1,3,5)) 3 'Owner may stop'
Assert-Equal $state.Leader -1 'Wait clears leader'
Assert-Equal ($state.Handle($true,$true,11,1,2,6)) 2 'New player may follow after release'
'PASS: authority, reach, invalid inputs, rate limit, ownership, chatting and wait transitions.'

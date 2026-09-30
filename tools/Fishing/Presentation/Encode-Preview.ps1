#Requires -Version 7.0
$ErrorActionPreference='Stop'
$frames=Join-Path $PSScriptRoot 'artifacts/UnityProject/Build/MotionFrames'
$out=Join-Path $PSScriptRoot 'Previews/fishing-motion.mp4'
if(!(Test-Path "$frames/frame-0143.png")){throw 'Run Test-Unity.ps1 successfully first.'}
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
$caption='Unity editor preview | scripted snapshots | not in-game'
& ffmpeg -hide_banner -loglevel error -y -framerate 24 -i "$frames/frame-%04d.png" -frames:v 144 -vf "drawtext=text='$caption':x=20:y=h-38:fontcolor=white:fontsize=19:box=1:boxcolor=black@0.6" -c:v libx264 -crf 20 -pix_fmt yuv420p -movflags +faststart $out
if($LASTEXITCODE -ne 0){throw 'Preview encoding failed'}
Write-Output "Created editor-only preview: $out"

#Requires -Version 7.0
param([string]$Project='E:/soft/7DTD-Modding/SakuraEscort')
$ErrorActionPreference='Stop'
$template=Join-Path $PSScriptRoot 'UnityTemplate'
$character=Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) '.local-tests/EscortNPC/Character'
if(-not(Test-Path (Join-Path $character 'Sakura.fbx'))){throw 'Run build_character.py in Blender first.'}
foreach($file in Get-ChildItem -LiteralPath $template -File -Recurse) {
    $relative=[IO.Path]::GetRelativePath($template,$file.FullName)
    $target=Join-Path $Project $relative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
    if(-not(Test-Path -LiteralPath $target)){Copy-Item -LiteralPath $file.FullName -Destination $target}
}
New-Item -ItemType Directory -Force -Path "$Project/Assets/Character","$Project/SourceArt","$Project/Logs" | Out-Null
foreach($pair in @(@('Sakura.fbx','Assets/Character/Sakura.fbx'),@('Sakura.blend','SourceArt/Sakura.blend'),@('Sakura-preview.png','SourceArt/Sakura-preview.png'))) {
    $target=Join-Path $Project $pair[1]
    if(-not(Test-Path -LiteralPath $target)){Copy-Item -LiteralPath (Join-Path $character $pair[0]) -Destination $target}
}
"Prepared Unity project: $Project"

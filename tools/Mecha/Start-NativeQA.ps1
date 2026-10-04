#Requires -Version 7.0
# Smoke QA: boots an isolated dedicated server with only Harmony + the Mecha
# mod, waits for the loader line, then scans the log for the walker install
# marker and Mecha-related errors. Gameplay tuning still needs a real client.
param(
    [int]$TimeoutSeconds = 300,
    [switch]$KeepRunning,
    [switch]$MotionProbe
)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot); $game = Split-Path $root
if (Get-Process 7DaysToDie, 7DaysToDieServer -ErrorAction SilentlyContinue) { throw 'A game/QA process is already running; leave it untouched.' }
$qa = Join-Path $root ('.local-tests/MechaNativeQA/run-' + [guid]::NewGuid().ToString('N'))
$data = Join-Path $qa 'UserData'; $mods = Join-Path $data 'Mods'
New-Item -ItemType Directory -Force $mods | Out-Null
Copy-Item -LiteralPath (Join-Path $root '0_TFP_Harmony') -Destination $mods -Recurse
# Shadow every other installed mod with an empty manifest so the game, which
# also scans its installation Mods folder, cannot double-load anything.
foreach ($dir in Get-ChildItem -LiteralPath $root -Directory) {
    if ($dir.Name -in @('0_TFP_Harmony', 'ZZ-PZAEC_Mecha')) { continue }
    if (!(Test-Path -LiteralPath (Join-Path $dir.FullName 'ModInfo.xml'))) { continue }
    $shadow = Join-Path $mods $dir.Name; New-Item -ItemType Directory -Force $shadow | Out-Null
    Copy-Item -LiteralPath (Join-Path $dir.FullName 'ModInfo.xml') -Destination $shadow
}
$target = Join-Path $mods 'ZZ-PZAEC_Mecha'
Copy-Item -LiteralPath (Join-Path $root 'ZZ-PZAEC_Mecha') -Destination $target -Recurse
if($MotionProbe) {
    & (Join-Path $PSScriptRoot 'Build.ps1') -OutputPath (Join-Path $target 'PZAEC.Mecha.dll') -ExtraSources (Join-Path $PSScriptRoot 'MotionNativeQA.cs')
}
# The isolated world lacks AEC endgame items; stub missing recipe ingredients
# (M1 NativeQA pattern). Live recipes stay exactly as shipped.
[xml]$base = Get-Content -LiteralPath (Join-Path $game 'Data/Config/items.xml')
[xml]$items = Get-Content -LiteralPath (Join-Path $target 'Config/items.xml')
[xml]$recipes = Get-Content -LiteralPath (Join-Path $target 'Config/recipes.xml')
$known = @{}
foreach ($item in $base.items.item) { $known[$item.name] = $true }
foreach ($item in $items.configs.append.item) { $known[$item.name] = $true }
$missing = @($recipes.SelectNodes('//ingredient') | ForEach-Object { $_.name } | Sort-Object -Unique | Where-Object { !$known.ContainsKey($_) })
if ($missing.Count -gt 0) {
    $stubs = Join-Path $mods '01-MechaQAMaterialStubs'
    New-Item -ItemType Directory -Force (Join-Path $stubs 'Config') | Out-Null
    '<xml><Name value="MechaQAMaterialStubs"/><DisplayName value="Mecha QA material stubs"/><Description value="Isolated test only"/><Author value="QA"/><Version value="0.1.0"/></xml>' | Set-Content (Join-Path $stubs 'ModInfo.xml')
    ('<configs><append xpath="/items">' + (($missing | ForEach-Object { '<item name="' + $_ + '"><property name="Extends" value="resourceForgedSteel"/></item>' }) -join '') + '</append></configs>') | Set-Content (Join-Path $stubs 'Config/items.xml')
}

[xml]$config = Get-Content -LiteralPath (Join-Path $game 'serverconfig.xml')
$values = @{ GameWorld = 'Navezgane'; GameName = 'MechaQA_Isolated'; ServerName = 'Mecha Isolated QA'; ServerPort = '27987'; ServerVisibility = '0'; ServerPassword = 'MechaQALocalOnly'; TelnetEnabled = 'false'; WebDashboardEnabled = 'false'; TerminalWindowEnabled = 'false'; EACEnabled = 'false'; UserDataFolder = $data; ServerMaxPlayerCount = '1' }
foreach ($key in $values.Keys) {
    $node = $config.SelectSingleNode("/ServerSettings/property[@name='$key']")
    if (!$node) { $node = $config.CreateElement('property'); $node.SetAttribute('name', $key); $config.DocumentElement.AppendChild($node) | Out-Null }
    $node.SetAttribute('value', $values[$key])
}
$cfg = Join-Path $qa 'serverconfig.xml'; $config.Save($cfg)
$log = Join-Path $qa 'game.log'
$arguments = @('-batchmode', '-dedicated', '-crossplatform=None', '-serverplatforms=Steam,LAN', ('-configfile="' + $cfg + '"'), ('-UserDataFolder="' + $data + '"'), '-logfile', ('"' + $log + '"'))
if($MotionProbe){$arguments += '-mechaMotionQA'}
$process = Start-Process -FilePath (Join-Path $game '7DaysToDie.exe') -WorkingDirectory $game -WindowStyle Hidden -PassThru -ArgumentList $arguments
[pscustomobject]@{ ProcessId = $process.Id; Log = $log; QaRoot = $qa } | ConvertTo-Json | Set-Content (Join-Path $root '.local-tests/MechaNativeQA/session.json')

$loaded = $false; $installed = $false; $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while ((Get-Date) -lt $deadline -and -not $process.HasExited) {
    Start-Sleep -Seconds 5
    if (Test-Path $log) {
        $text = Get-Content $log -Raw -ErrorAction SilentlyContinue
        if ($text -match 'Loaded Mod: PZAEC_Mecha') { $loaded = $true }
        if ($text -match '\[Mecha\] Combat Robot biped installed') { $installed = $true }
        if ($loaded -and $installed -and (!$MotionProbe -or $text -match '\[MechaMotionQA\] COMPLETE')) { break }
    }
}

Write-Output ("loaded={0} installed={1} exited={2}" -f $loaded, $installed, $process.HasExited)
if (Test-Path $log) {
    Write-Output '--- Mecha/Buster/Exception lines ---'
    Select-String -Path $log -Pattern 'Mecha|Buster|ERR|Exception' | Select-Object -Last 40 | ForEach-Object { $_.Line.Substring(0, [Math]::Min(180, $_.Line.Length)) }
}
if (-not $KeepRunning) {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    Write-Output 'QA server stopped.'
    if ($loaded -and $installed -and (!$MotionProbe -or (Get-Content $log -Raw) -match '\[MechaMotionQA\] COMPLETE failures=0')) { Write-Output 'MECHA NATIVE SMOKE PASSED'; exit 0 }
    Write-Output 'MECHA NATIVE SMOKE FAILED'; exit 1
}

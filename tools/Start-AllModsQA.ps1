#Requires -Version 7.0
# Boot a dedicated server with EVERY installed mod and a copy of a real save.
# Verifies all mods initialize and the world loads (reproduces the client
# crash scenario without touching the user's actual save).
param(
    [string]$GameName = 'ModTest_2026100',
    [int]$TimeoutSeconds = 420,
    [switch]$KeepRunning
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$game = Split-Path $root
if (Get-Process 7DaysToDie, 7DaysToDieServer -ErrorAction SilentlyContinue) { throw 'A game/QA process is already running; leave it untouched.' }
$qa = Join-Path $root ('.local-tests/AllModsQA/run-' + [guid]::NewGuid().ToString('N'))
$data = Join-Path $qa 'UserData'
New-Item -ItemType Directory -Force (Join-Path $data 'Saves/Navezgane') | Out-Null
Copy-Item ((Join-Path $env:APPDATA '7DaysToDie/Saves/Navezgane/') + $GameName) ((Join-Path $data 'Saves/Navezgane/') + $GameName) -Recurse

[xml]$config = Get-Content -LiteralPath (Join-Path $game 'serverconfig.xml')
$values = @{ GameWorld = 'Navezgane'; GameName = $GameName; ServerName = 'AllMods QA'; ServerPort = '27989'; ServerVisibility = '0'; ServerPassword = 'MechaQALocalOnly'; TelnetEnabled = 'false'; WebDashboardEnabled = 'false'; TerminalWindowEnabled = 'false'; EACEnabled = 'false'; UserDataFolder = $data; ServerMaxPlayerCount = '1' }
foreach ($key in $values.Keys) {
    $node = $config.SelectSingleNode("/ServerSettings/property[@name='$key']")
    if (!$node) { $node = $config.CreateElement('property'); $node.SetAttribute('name', $key); $config.DocumentElement.AppendChild($node) | Out-Null }
    $node.SetAttribute('value', $values[$key])
}
$cfg = Join-Path $qa 'serverconfig.xml'; $config.Save($cfg)
$log = Join-Path $qa 'game.log'
$arguments = @('-batchmode', '-dedicated', '-crossplatform=None', '-serverplatforms=Steam,LAN', ('-configfile="' + $cfg + '"'), ('-UserDataFolder="' + $data + '"'), '-logfile', ('"' + $log + '"'))
$process = Start-Process -FilePath (Join-Path $game '7DaysToDie.exe') -WorkingDirectory $game -WindowStyle Hidden -PassThru -ArgumentList $arguments
[pscustomobject]@{ ProcessId = $process.Id; Log = $log; QaRoot = $qa } | ConvertTo-Json | Set-Content (Join-Path $root '.local-tests/AllModsQA/session.json')

$worldLoaded = $false
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while ((Get-Date) -lt $deadline -and -not $process.HasExited) {
    Start-Sleep -Seconds 5
    if (Test-Path $log) {
        $text = Get-Content $log -Raw -ErrorAction SilentlyContinue
        if ($text -match 'INF Inflicted 0\r?\n|GameMessage: Server started|Server started|world loaded|World loaded') { $worldLoaded = $true }
        if ($text -match 'INF Started thread|INF GameVersion' -and $text -match 'Navezgane') { }
        if ($text -match 'INF .* INF|Loading players\.xml') { }
        if ($text.Contains('Started thread SaveChunks')) { $worldLoaded = $true; break }
    }
}
Start-Sleep -Seconds 20
Write-Output ("exited={0} worldLoaded={1}" -f $process.HasExited, $worldLoaded)
Write-Output '--- Mod init failures / errors / world-load state ---'
if (Test-Path $log) {
    Select-String -Path $log -Pattern 'Failed initializing|Failed creating|Initialization failed|ReflectionTypeLoad|ERR |NullReferenceException|MultiBlockManager|TileEntityComposite|StartGame|Loading players|INF Version' | Select-Object -First 60 | ForEach-Object { $_.Line.Substring(0, [Math]::Min(200, $_.Line.Length)) }
}
if (-not $KeepRunning) {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    Write-Output 'QA server stopped.'
    Write-Output ('RUN=' + $qa)
}

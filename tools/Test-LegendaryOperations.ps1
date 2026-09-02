$ErrorActionPreference = 'Stop'
$modRoot = Split-Path -Parent $PSScriptRoot
function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
[xml]$dialogs = Get-Content -LiteralPath (Join-Path $modRoot '98-AECxProjectZ_Tweaks/Config/dialogs.xml') -Raw
[xml]$quests = Get-Content -LiteralPath (Join-Path $modRoot '98-AECxProjectZ_Tweaks/Config/quests.xml') -Raw
[xml]$buffs = Get-Content -LiteralPath (Join-Path $modRoot '98-AECxProjectZ_Tweaks/Config/buffs.xml') -Raw
$statements = @($dialogs.SelectNodes('//statement'))
$responses = @($dialogs.SelectNodes('//response'))
Assert-True (@($statements | Group-Object id | Where-Object Count -gt 1).Count -eq 0) 'Duplicate statement IDs'
Assert-True (@($responses | Group-Object id | Where-Object Count -gt 1).Count -eq 0) 'Duplicate response IDs'
$central = $dialogs.SelectSingleNode("//statement[@id='pzaec_aec_tier_central_hub']")
Assert-True ($central.response_entry.id -contains 'pzaec_open_legendary_menu') 'Missing legendary category entry'
$master = $dialogs.SelectSingleNode("//statement[@id='pzaec_aec_master_hub']")
Assert-True ($master.response_entry.id -notcontains 'pzaec_go_to_t16') 'T16 still in master category'
$hub = $dialogs.SelectSingleNode("//statement[@id='pzaec_aec_legendary_hub']")
$sizes = @('small', 'medium', 'large', 'huge', 'massive')
$thresholds = @{ 17 = 50000; 18 = 70000; 19 = 90000 }
$textKeys = [System.Collections.Generic.HashSet[string]]::new()
foreach ($node in $dialogs.SelectNodes('//*[@text]')) { [void]$textKeys.Add($node.text) }
$localization = @{}
foreach ($relative in @('98-AECxProjectZ_Tweaks/Config/Localization.csv', '99-AEC_T16_RuntimeFix/Config/Localization.csv')) {
    $rows = @(Import-Csv -LiteralPath (Join-Path $modRoot $relative))
    $scoped = @($rows | Where-Object { $_.Key -match 'legendary|_t1[6789]_header|browse_t1[6789]|master_hub_title' })
    Assert-True (@($scoped | Group-Object Key | Where-Object Count -gt 1).Count -eq 0) "Duplicate legendary localization keys in $relative"
    foreach ($row in $rows) { $localization[$row.Key] = $row }
}
$pages = 0
$slots = 0
foreach ($tier in 16..19) {
    Assert-True ($hub.response_entry.id -contains "pzaec_go_to_t$tier") "Missing T$tier hub entry"
    foreach ($area in 1..5) {
        $size = $sizes[$area - 1]
        $page = $dialogs.SelectSingleNode("//statement[@id='pzaec_t${tier}_$size']")
        Assert-True ($null -ne $page) "Missing T$tier $size page"
        Assert-True (@($page.quest_entry).Count -eq 6) "Expected 6 offer slots in T$tier $size"
        Assert-True ($page.response_entry.id -contains 'pzaec_return_to_legendary') 'Wrong category return'
        foreach ($entry in $page.quest_entry) {
            Assert-True ($entry.id -eq "aec_quest_T${tier}_A${area}_clear") 'Wrong quest ID'
            Assert-True (-not $entry.HasAttribute('listindex')) 'Legacy fixed index remains'
            $quest = $quests.SelectSingleNode("//quest[@id='$($entry.id)']")
            Assert-True ($null -ne $quest) "Missing quest definition $($entry.id)"
            Assert-True ($quest.template -eq "aec_base_A$area") 'Wrong area template'
            $slots++
        }
        foreach ($entry in $page.response_entry) {
            if ($entry.id -eq 'nevermind') { continue }
            $response = $dialogs.SelectSingleNode("//response[@id='$($entry.id)']")
            Assert-True ($null -ne $response) "Missing response $($entry.id)"
            Assert-True ($null -ne $dialogs.SelectSingleNode("//statement[@id='$($response.nextstatementid)']")) 'Broken navigation target'
        }
        if ($area -lt 5) {
            $forward = $dialogs.SelectSingleNode("//response[@id='pzaec_fwd_t${tier}_$size']")
            Assert-True ($forward.nextstatementid -eq "pzaec_t${tier}_$($sizes[$area])") 'Incorrect next page'
        }
        if ($area -gt 1) {
            $back = $dialogs.SelectSingleNode("//response[@id='pzaec_back_t${tier}_$size']")
            Assert-True ($back.nextstatementid -eq "pzaec_t${tier}_$($sizes[$area - 2])") 'Incorrect previous page'
        }
        $pages++
    }
    if ($tier -ge 17) {
        $gate = $dialogs.SelectSingleNode("//response[@id='pzaec_go_to_t$tier']/requirement")
        Assert-True ($gate.type -eq 'AECLegendaryGameStage, AEC.T16.RuntimeFix') 'Wrong requirement type'
        Assert-True ([int]$gate.value -eq $thresholds[$tier]) 'Wrong GS threshold'
    }
}
foreach ($key in $textKeys) {
    if ($key -match 'legendary|_t1[6789]_header|browse_t1[6789]|master_hub_title') {
        Assert-True ($localization.ContainsKey($key)) "Missing localization $key"
        Assert-True (-not [string]::IsNullOrWhiteSpace($localization[$key].schinese)) "Missing Chinese text $key"
    }
}
Assert-True ($localization['pzaec_aec_master_hub_title'].schinese -match 'T11–T15') 'Stale master category label'

# Unit-test the actual requirement source against lightweight game API stubs.
# This validates threshold logic, not Unity's runtime dialog/POI behavior.
$source = Get-Content -LiteralPath (Join-Path $modRoot '99-AEC_T16_RuntimeFix/Source/DialogRequirementAECLegendaryGameStage.cs') -Raw
$stubs = @'
public class EntityPlayer { public int gameStage; }
public class EntityNPC { }
public class BaseDialogRequirement {
    public string Value { get; set; }
    public virtual bool CheckRequirement(EntityPlayer player, EntityNPC npc) { return false; }
}
'@
if (-not ('DialogRequirementAECLegendaryGameStage' -as [type])) { Add-Type -TypeDefinition ($stubs + $source) }
$tests = 0
foreach ($tier in 17..19) {
    $gate = [DialogRequirementAECLegendaryGameStage]::new()
    $gate.Value = [string]$thresholds[$tier]
    foreach ($gs in @(($thresholds[$tier] - 1), $thresholds[$tier], ($thresholds[$tier] + 1), 150000)) {
        $player = [EntityPlayer]::new()
        $player.gameStage = $gs
        Assert-True ($gate.CheckRequirement($player, $null) -eq ($gs -ge $thresholds[$tier])) "Boundary failed: T$tier at GS $gs"
        $tests++
    }
    Assert-True (-not $gate.CheckRequirement($null, $null)) 'Null player was accepted'
}
$gate.Value = 'invalid'
Assert-True (-not $gate.CheckRequirement($player, $null)) 'Malformed threshold was accepted'
Write-Output "PASS: $pages pages; $slots direct quest slots; category routing, quest IDs, localization and $tests GS boundary checks."

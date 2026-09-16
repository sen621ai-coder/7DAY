# Generates native configuration without changing the model bundles.
$ErrorActionPreference='Stop'
$root=Join-Path (Split-Path $PSScriptRoot) '96-SakuraPreview/Config'
function EscapeXml([string]$text){[Security.SecurityElement]::Escape($text)}
$lines=[Collections.Generic.List[string]]::new()
function L([string]$key,[string]$text){$escaped=$text.Replace('"','""');$lines.Add("$key,dialogs,UI,,false,`"$escaped`",`"$escaped`"");return $key}
$dialogs=[Text.StringBuilder]::new('<configs><append xpath="/dialogs">')
$replies=@{0='你好，很高兴见到你！';1='废土里很危险。选择任务后，我们一起坚持到最后吧。';2='好呀，我跟着你。';3='我在这里等你，记得回来。';4='能平安见到你，就是今天的好事。';5='我正在跟随另一位伙伴，请先让对方叫停。';6='我是原地守护角色，请选择守护任务。';16='任务已接取！可以在任务日志中查看进度，并在地图和指南针上查看目标。';20='任务进度已更新，请查看任务日志。';21='任务已放弃，本次没有奖励。';22='奖励已发放：公爵币和同阶奖励箱在你脚边，经验已到账。';23='现在由你带路。';30='任务系统暂时无法处理，请联系服主查看日志。';31='这位角色已经接取过任务，或你已有进行中的任务。';32='没有找到地图上的商店，暂时无法接取。';33='需要先制作并使用护送或守护任务图纸。';34='只有任务成员可以操作；暂停和放弃需要当前带领者。';35='暂不可领奖：须完成任务并参与至少一半时间，且尚未领取。';36='缺少奖励箱物品配置。未消耗领奖资格，请联系服主。';37='服务器没有及时回应，请查看任务进度后重试。';38='这次任务已失败，护送已结束，本次没有奖励。';39='这不是分配给你的救援委托，或已开始救援。请查看任务日志。'}
foreach($kind in 'sakura','mint'){
 $guard=$kind -eq 'mint';$name=if($guard){'Mint'}else{'小樱'};$job=if($guard){'原地守护'}else{'护送'}
 [void]$dialogs.Append("<dialog id=`"${kind}Native`" startstatementid=`"start`">")
 $main=if($guard){@('about','missions','chat','done')}else{@('about','missions','follow','wait','chat','done')}
 $menus=@{start=$main;missions=@('t16','t17','t18','t19','locked','progress','claim','takeover','abandon','back');confirm=@('abandonYes','back');waiting=@('back')}
 $text=@{start="你好，我是$name。需要我帮忙吗？";missions="$job ：只显示已分配给你的救援档位。找到角色后开始救援。任务自动共享给在线队友；任一成员都可开始救援。参与至少一半护送时间才能领奖。";confirm='确定放弃本次任务？放弃后没有奖励，角色会离开，需要寻找新的角色。';waiting='正在等待服务器回应……'}
 foreach($menu in 'start','missions','confirm','waiting'){
  $key=L "${kind}Native_$menu" $text[$menu]
  [void]$dialogs.Append("<statement id=`"$menu`" text=`"$key`">")
  foreach($response in $menus[$menu]){[void]$dialogs.Append("<response_entry id=`"$response`"/>")};[void]$dialogs.Append('</statement>')
 }
 foreach($number in ($replies.Keys|Sort-Object)){
  $key=L "${kind}Native_reply$number" $replies[$number]
  [void]$dialogs.Append("<statement id=`"reply$number`" text=`"$key`"><response_entry id=`"missions`"/><response_entry id=`"back`"/><response_entry id=`"done`"/></statement>")
 }
 $responses=@{about=@('你在这里做什么？','waiting',1);missions=@("$job 任务",'missions',-1);follow=@('跟我来','waiting',2);wait=@('在这里等我','waiting',3);chat=@('聊聊天','waiting',4);progress=@('查看任务进度','waiting',20);claim=@('领取任务奖励','waiting',22);takeover=@('接替掉线队友带路','waiting',23);abandon=@('放弃任务…','confirm',-1);abandonYes=@('确认放弃','waiting',21);locked=@('暂无待开始的任务（先使用对应任务图纸）','missions',-1);back=@('返回','start',-1);done=@('再见','',-1)}
 foreach($tier in 16..19){$responses["t$tier"]=@("开始 T$tier 救援 — 公爵币 $($tier*4000-60000) + 奖励箱 ×1",'waiting',$tier)}
 foreach($id in ($responses.Keys|Sort-Object)){
  $r=$responses[$id];$key=L "${kind}Native_response_$id" $r[0];$next=if($r[1]){" nextstatementid=`"$($r[1])`""}else{''}
  [void]$dialogs.Append("<response id=`"$id`" text=`"$key`"$next>")
  if($id -match '^t(16|17|18|19)$' -or $id -eq 'locked'){
    $gate=if($id -eq 'locked'){0}else{[int]$id.Substring(1)}
    [void]$dialogs.Append("<requirement type=`"SakuraTier, Sakura.Preview`" id=`"$gate`"/>")
  }
  if($r[2] -ge 0){[void]$dialogs.Append("<action type=`"Sakura, Sakura.Preview`" id=`"$($r[2])`"/>")};[void]$dialogs.Append('</response>')
 };[void]$dialogs.Append('</dialog>')
}
[void]$dialogs.Append('</append></configs>');[xml]$d=$dialogs.ToString();$d.Save((Join-Path $root 'dialogs.xml'))
[xml]$npc=Get-Content (Join-Path (Split-Path (Split-Path $root)) '../Data/Config/npc.xml') -Raw
[xml]$out='<configs><append xpath="/npc"/></configs>'
foreach($kind in 'sakura','mint'){$node=$out.ImportNode($npc.SelectSingleNode('/npc/npc_info[@id="traderjen"]'),$true);$node.id="${kind}NativeNPC";$node.name=if($kind -eq 'sakura'){'小樱'}else{'Mint'};$node.name_key=if($kind -eq 'sakura'){'sakuraCompanion'}else{'mintGuardian'};$node.dialog_id="${kind}Native";[void]$out.configs.append.AppendChild($node)}
$out.Save((Join-Path $root 'npc.xml'))
[xml]$entities=Get-Content (Join-Path $root 'entityclasses.xml') -Raw
foreach($pair in @(@('sakuraCompanion','sakuraNativeNPC'),@('mintGuardian','mintNativeNPC'))){$entity=$entities.SelectSingleNode("//entity_class[@name='$($pair[0])']");$node=$entity.SelectSingleNode("property[@name='NPCID']");if(!$node){$node=$entities.CreateElement('property');$node.SetAttribute('name','NPCID');[void]$entity.AppendChild($node)};$node.SetAttribute('value',$pair[1])};$entities.Save((Join-Path $root 'entityclasses.xml'))
[xml]$quests='<configs><append xpath="/quests"/></configs>'
foreach($kind in 'sakura','mint'){foreach($tier in 16..19){
 $id=if($kind -eq 'sakura'){"sakuraEscortT$tier"}else{"mintGuardT$tier"};$title=if($kind -eq 'sakura'){"拯救小樱 T$tier"}else{"Mint 原地守护 T$tier"}
 $desc=if($kind -eq 'sakura'){'先到地图标记处找到小樱并对话，然后保护她前往最近商店外围，清除全部伏击。寻找期间不计护送时间。'}else{'在据点保护 Mint，击败全部来袭波次。'}
 $desc+=" 奖励：$((($tier-15)*4000)) 公爵币、$((10000+($tier-16)*5000)) 经验、T$tier 奖励箱 ×1。完成后与任务人物交谈领奖；物品掉在领奖者脚边。在线同队自动共享坐标和进度；已有其他同伴任务时不能加入。参与至少一半护送时间。离开人物活动范围（护送 60 米、守护 30 米）超过 60 秒、目标死亡或超过 30 分钟会失败。服务器重启会结束未完成任务。失败后角色离开，需要寻找新的角色。"
 $q=$quests.CreateElement('quest');$q.SetAttribute('id',$id)
 $props=[ordered]@{name_key=(L "${id}Name" $title);subtitle_key="${id}Name";description_key=(L "${id}Desc" $desc);category='同伴任务';icon='ui_game_symbol_quest';repeatable='true';shareable='false';allow_remove='false';add_to_tier_complete='false';difficulty='1';difficulty_tier='1';completiontype='TurnIn';return_to_quest_giver='false'}
 foreach($p in $props.GetEnumerator()){$n=$quests.CreateElement('property');$n.SetAttribute('name',$p.Key);$n.SetAttribute('value',$p.Value);[void]$q.AppendChild($n)}
 foreach($objectiveId in 'goal','health','waves','enemies'){$obj=$quests.CreateElement('objective');$obj.SetAttribute('type','SakuraMission, Sakura.Preview');$obj.SetAttribute('id',$objectiveId);$obj.SetAttribute('phase','1');[void]$q.AppendChild($obj)};[void]$quests.configs.append.AppendChild($q)
}}
$quests.Save((Join-Path $root 'quests.xml'))
$csv=Join-Path $root 'Localization.csv';$existing=Get-Content $csv|Where-Object {$_ -notmatch '^(sakuraDispatch_|mintDispatch_|sakuraMissionBlueprint|mintMissionBlueprint|sakuraNative_|mintNative_|sakuraEscortT\d+(Name|Desc),|mintGuardT\d+(Name|Desc),)'};@($existing)+@($lines)|Set-Content $csv -Encoding utf8






& python (Join-Path $PSScriptRoot 'EscortNPC/generate_mission_rewards.py')
if($LASTEXITCODE -ne 0){throw 'Mission reward generator failed'}

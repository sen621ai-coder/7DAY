#Requires -Version 7.0
param([string]$QaRoot)
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
if(!$QaRoot){$QaRoot=(Get-Content (Join-Path $root '.local-tests/FlyingSword/session.json') -Raw|ConvertFrom-Json).QaRoot}
$mod=Join-Path $root 'ZZ-PZAEC_FlyingSword'
$tested=Join-Path $QaRoot 'UserData/Mods/ZZ-PZAEC_FlyingSword'
$log=Get-Content (Join-Path $QaRoot 'game.log')
if(!($log -match '\[JuqueQA\] COMPLETE failures=0')){throw 'Native run did not pass.'}
Get-ChildItem (Join-Path $mod 'Source') -Filter *.cs|ForEach-Object {
 if((Get-FileHash $_.FullName).Hash -ne (Get-FileHash (Join-Path $tested ('Source/'+$_.Name))).Hash){throw ('Source changed after QA: '+$_.Name)}
}
$out=Join-Path $mod 'Validation/poses';New-Item -ItemType Directory -Force $out|Out-Null
$cases=[ordered]@{'ground-idle'='地面手持';'ground-grip-close'='握柄与手掌近景';'ground-grip-back'='背面持剑检查';'ground-full-charge'='满蓄聚光与预备姿态';'ground-release-swing'='挥剑与短促斩痕';'ground-charged-wave'='满蓄月牙光刃';'ground-normal-wave'='普通剑气';'ground-impact'='命中散光';'flight-tail-hover'='静止：仅剑身光晕';'flight-tail-launch'='起步：尾焰渐入';'flight-aura-boost'='高速：尾焰延长';'flight-tail-turn'='转弯：世界空间弧形尾迹';'flight-tail-stopped'='停下：尾焰消散';'pose-empty-hover'='空手悬停：双手自然外露';'pose-empty-look-away'='空手航向：不跟随鼠标转身';'pose-empty-boost-turn'='加速前倾与剑身侧倾';'pose-M60-yaw0'='M60 正前方向下瞄准';'pose-M60-yaw90'='M60 右转 90°';'pose-M60-yaw135'='M60 右侧射界 135°';'pose-M60-yaw-135'='M60 左侧射界 135°';'pose-M60-pitch-80'='M60 最大下俯 80°';'pose-M60-pitch45'='M60 最大上仰 45°';'pose-M60-down80-yaw135'='右转 135° 与下俯 80°';'pose-M60-down80-yaw-135'='左转 135° 与下俯 80°';'pose-M60-firing'='M60 开火动画';'pose-M60-reload'='M60 换弹动画'}
$cards=foreach($key in $cases.Keys){Copy-Item -LiteralPath (Join-Path $tested ($key+'.png')) -Destination $out;'<figure><img src="'+$key+'.png" alt="'+$cases[$key]+'"><figcaption>'+$cases[$key]+'</figcaption></figure>'}
$version=([xml](Get-Content (Join-Path $mod 'ModInfo.xml'))).xml.Version.value
$summary=($log|Where-Object{$_ -match '\[JuqueQA\] COMPLETE'}) -join "`n"
$sha=(Get-FileHash (Join-Path $mod 'PZAEC.FlyingSword.dll')).Hash
@("巨阙剑 $version 原生姿态检查", "QA: $QaRoot", "Production DLL SHA256: $sha", 'QA copy contains extra test probes; all production .cs source hashes match.', 'Native SDCS armored BaseFemale model; native sword and M60 animations; fixed-frame bone skinning bake for dedicated rendering.', 'Not a live input test, continuous motion review, multiplayer test, or proof for every body/armor/weapon.', $summary, '')+($log|Where-Object{$_ -match '\[Juque(Pose|Ground|QA)\]'})|Set-Content (Join-Path $out 'report.txt') -Encoding utf8
$html=@"
<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>巨阙剑 $version · 姿态验收</title>
<style>body{margin:0;background:#17212b;color:#e8efef;font:17px/1.7 system-ui}main{max-width:1200px;margin:auto;padding:32px}h1{font-size:28px}p{max-width:980px;color:#c8d5dc}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(360px,1fr));gap:18px}figure{margin:0;background:#263440;border-radius:12px;overflow:hidden}img{display:block;width:100%}figcaption{padding:12px 18px}a{color:#8ee7dc}</style>
<main><h1>巨阙剑 $version · 原生姿态验收</h1><p>隔离游戏环境中的实际骨骼、服装、持剑与 M60 动画。截图由当前动画骨骼烘焙蒙皮后渲染，避免专服同步测试未刷新显示帧；没有使用概念图替代游戏效果。</p><p>检查范围：空手悬停、倾身侧倾、水平射界、俯仰极限、开火与换弹，以及地面蓄力、挥剑、普通/满蓄剑气和命中散光。脚底支撑点与枪口朝向另有数值断言。固定帧不代表连续动作、镜头手感或双客户端同步已验收；当前样本也不能覆盖所有体型、装甲和枪械。</p><p><a href="report.txt">查看测量与回归测试日志</a></p><div class="grid">$($cards -join "`n")</div></main></html>
"@
$html|Set-Content (Join-Path $out 'index.html') -Encoding utf8
Write-Output (Join-Path $out 'index.html')

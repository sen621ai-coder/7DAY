"""Publish native evidence without treating functional success as visual acceptance."""
import argparse,html,json,shutil
from pathlib import Path
p=argparse.ArgumentParser()
for k in ['replay','motion','resources','output']:p.add_argument('--'+k,required=True,type=Path)
a=p.parse_args();root=Path(__file__).resolve().parents[2];old=root/'tools/Mecha/Generated/Articulation-0.15.0';out=a.output;out.mkdir(parents=True,exist_ok=True)
d=json.loads((a.resources/'samurai_style_gundam_mecha_rig.json').read_text());v=json.loads((out/'validation.json').read_text())
page='''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>完全体修复进度</title><style>body{margin:0;background:#141b24;color:#e6eaf0;font:16px/1.75 system-ui,"Microsoft YaHei"}main{max-width:1280px;margin:auto;padding:32px 24px}h2{margin-top:36px}.note{padding:18px 24px;background:#293341;border-left:4px solid #ed7777}.pair{display:grid;grid-template-columns:1fr 1fr;gap:12px}figure{margin:0;background:#202a38;border:1px solid #3b4656}img{width:100%;display:block}figcaption{padding:10px 16px}a{color:#a8ceff}@media(max-width:700px){.pair{grid-template-columns:1fr}}</style><main><p>2026-10-06 · 原生引擎输出 · 原贴图 · 图片未修饰</p><h1>完全体修复进度</h1><div class="note"><b>尚未完成整机外观修复，候选没有安装。</b><p>本轮按新方向取消大舱门，改为胸口两块原有红白小装甲配合开合；中央胸甲固定，窄缝后使用原曲面和原贴图。肩臂交界、原胸甲减面尖片及护手附近仍有残缺轮廓。本轮已修正小舱门上方凸出的固定内衬黑尖角。功能和几何检查通过不能替代外观验收。正式游戏仍为 0.15.0。</p></div>
<h2>本轮保留的修正</h2><ul><li>盾牌按表面连通与已核对的装饰位置分件，后侧红白装饰、上沿白甲与提手不再误随肩膀转动。取消颜色判断与平面裁切盾牌的试验方案。</li><li>取消整块胸板下翻与大舱室。两块小装甲各外翻 12°、平移约 1.7 厘米；中央胸甲固定。只露窄缝与两条短灯光，关闭后复位。</li><li>握柄接触点锁定在一致变换上；肩肘腕保留窄带过渡，粗大的接缝三角面局部分段，静止表面和贴图仍来自原模型。</li><li>恢复完整左翼后生成镜像右翼。头部、原金色剑饰和已验证剑刃核心保留。</li><li>内衬保持薄层，限制跨装甲的大块自动封口。改变外观的平面背板、局部自动重建、胸甲削层与零散肩甲重绑均未采用。</li></ul>
<h2>仍需修复</h2><ul><li>小舱门是进入动作示意，不表现真实可供人穿过的大型舱室；保留原模型胸甲轮廓。</li><li>肩臂交界、左臂背侧、持剑护手附近仍有尖片和残缺表面，纠正骨骼归属不能解决融合雕塑的全部切口。</li><li>局部重建试验出现贴图锯齿和鼓包，未采用。下一步需要按具体装甲轮廓重建这些局部表面、投射原贴图，再逐动作验收。</li></ul><h2>当前候选验证</h2>'''
page+=f'<p>原生功能回归 {v["native_functional_passes"]} 项通过，失败 {v["native_functional_failures"]}；主离线检查 63 项通过。模型 {d["triangles"]:,} 个三角面、{len(d["parts"])} 个分件。源表面及 UV、双翼、剑身归属、96 个金色饰面、1,116 条剑刃射线与双膝机构检查通过。</p>'
page+=f'<p>47 个全身姿态五视图，另加 5 个小舱门阶段三视图，共 250 张；重点近景仍未通过外观验收。{v["shared_arm_seam_pairs"]:,} 对共享接缝最大计算间隙 {v["seam_max_gap_m"]:.3g} 米，只验证这些接缝，不代表整机没有缺面或穿插。</p><p>实际装甲剑身净空检查：{html.escape(str(v["sword_clearance"]))}。未完成真人驾驶和双机联机视觉验收。</p>'
page+='<h2>本轮新增：固定封边黑尖角修复</h2><p>两处原生定位点确认，黑尖角来自向外偏移的固定内衬。改为向内凹入六毫米并沿用原边缘贴图。下图只代表这处缺陷，不代表整机无破损。</p><div class="pair">'
previous=root/'.local-tests/MechaNativeQA/run-49ccdf2e95ee4024bab2b77ed4ebe0f9/UserData/Saves/Navezgane/MechaQA_Isolated/articulation-review'
shutil.copy2(previous/'small-hatch-000-0.png',out/'before-fixed-rim.png')
shutil.copy2(a.replay/'small-hatch-000-0.png',out/'after-fixed-rim.png')
for name,label in [('before-fixed-rim.png','上轮：黑色内衬凸出'),('after-fixed-rim.png','本轮：封边向内凹入，保留原贴图')]:page+=f'<figure><img src="{name}"><figcaption>{label}</figcaption></figure>'
page+='</div>'
if v.get('detail_probe_run'):
 source=root/'.local-tests/MechaNativeQA'/v['detail_probe_run']/'UserData/Saves/Navezgane/MechaQA_Isolated/articulation-review/chest-source-0.png'
 shutil.copy2(source,out/'diagnostic-original-chest.png')
 page+='<h2>原因对照：原始高精度胸甲（未采用）</h2><p>原始面片能减少上沿的大片三角碎片；仅恢复法线无明显改善。局部自动接边最大差距约四厘米，直接替换仍会留下接缝，因此试验已撤回。下图是诊断证据，不是当前候选。</p><figure><img src="diagnostic-original-chest.png"><figcaption>仅诊断：原始高精度局部面片，未纳入候选</figcaption></figure>'
page+='<h2>本轮：两块原装甲小舱门</h2><p>左右面片按原模型分别选取，未强制镜像。拖动下方开合程度可查看原生正面截图。</p>'
for value in [0,25,50,75,100]:
 name=f'small-hatch-{value:03d}-0.png';shutil.copy2(a.replay/name,out/name)
page+='<input aria-label="开合程度" type="range" min="0" max="4" value="0" oninput="document.getElementById(\'smallstage\').src=\'small-hatch-\'+[\'000\',\'025\',\'050\',\'075\',\'100\'][this.value]+\'-0.png\';document.getElementById(\'smallvalue\').textContent=this.value*25+\'%\'"><span id="smallvalue">0%</span><figure><img id="smallstage" src="small-hatch-000-0.png" alt="两块原胸甲小舱门"><figcaption>原生近景：关闭到完全打开</figcaption></figure>'
entries=[('action-weight-entry-0','下蹲开舱'),('ShoulderR-ElbowR-3','肘腕与持剑手'),('KneeL-AnkleL-1','侧面翼片与腿部'),('action-flight-boost-2','飞行后视'),('action-complete-rest-0','日常站姿'),('Torso-ShoulderR-0','抬臂蓄力'),('HipL-KneeL-0','跨阶抬腿')]
for stem,title in entries:
 page+=f'<h2>{title}</h2><div class="pair">'
 for folder,prefix,source,label in [(old,'before','after-'+stem+'.png','已安装的 0.15.0'),(a.replay,'after',stem+'.png','当前候选：未安装')]:
  name=prefix+'-'+stem+'.png';shutil.copy2(folder/source,out/name);page+=f'<figure><a href="{name}"><img loading="lazy" src="{name}" alt="{title}"></a><figcaption>{label}</figcaption></figure>'
 page+='</div>'
page+='<h2>开舱连续阶段</h2><div class="pair">'
for value in [0,25,50,75,100]:
 name=f'hatch-{value:03d}-0.png';shutil.copy2(a.replay/name,out/name);page+=f'<figure><a href="{name}"><img loading="lazy" src="{name}"></a><figcaption>开舱参数 {value}%</figcaption></figure>'
page+='</div><h2>未通过近景</h2>'
shutil.copy2(a.replay/'hatch-100-4.png',out/'remaining-hatch-side.png');page+='<figure><a href="remaining-hatch-side.png"><img src="remaining-hatch-side.png"></a><figcaption>小舱门开合侧面；肩臂原有残缺仍需修复</figcaption></figure>'
for name in ['replay-report.txt','replayed-poses.json']:shutil.copy2(a.replay/name,out/name)
shutil.copy2(a.motion/'report.txt',out/'motion-report.txt');(out/'repair-metrics.json').write_text(json.dumps(d['panelRepair'],ensure_ascii=False,indent=2),encoding='utf-8')
page+='''<h2>复核文件</h2><p><a href="motion-report.txt">功能报告</a> · <a href="replay-report.txt">重放报告</a> · <a href="replayed-poses.json">实际重放姿态</a> · <a href="repair-metrics.json">修复指标</a> · <a href="candidate-runtime.patch">源码差异</a> · <a href="validation.json">状态与哈希</a></p><p>候选：.local-tests/Mecha-0.16.0/candidate；原版备份：.local-tests/Mecha-0.16.0/baseline。候选 DLL 是不带 QA 入口的普通构建。全动作视图与小舱门五阶段三视图保留在对应原生 QA 目录。</p></main></html>'''
if v.get('small_hatch',{}).get('live_installed'):
 page=page.replace('尚未完成整机外观修复，候选没有安装。','0.16.0 已安装，整机外观仍有待修复项。').replace('正式游戏仍为 0.15.0。','正式模组已替换为 0.16.0。').replace('已安装的 0.15.0','替换前的 0.15.0').replace('当前候选：未安装','已安装的 0.16.0').replace('当前候选验证','已安装版本验证')
 page=page.replace('</main>',f'<p>替换前完整备份：{html.escape(v["installation"]["backup"])}。安装后的文件一致性与离线检查：{html.escape(v["installation"]["offline_checks"])}。</p></main>')
(out/'index.html').write_text(page,encoding='utf-8');print(out/'index.html')

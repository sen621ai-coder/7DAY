"""Collect the isolated native evidence, preserving raw screenshots and reports."""
import argparse, csv, hashlib, html, json, re, shutil, statistics
from pathlib import Path

p=argparse.ArgumentParser();p.add_argument('--session-root',type=Path,required=True);a=p.parse_args()
r=a.session_root;out=Path(__file__).parent/'Generated/JusticePolish-0.20.0';out.mkdir(parents=True,exist_ok=True)
def entries(name):
    value=json.loads((r/name).read_text(encoding='utf-8-sig'))
    return value if isinstance(value,list) else [value]
release=entries('release-results.json');delivery=entries('delivery-results.json')
assert all(x['ExitCode']==0 for x in release)
assert all('Frames' in x['Output'] for x in release if x['Name'].startswith('Performance')), 'Exclude synchronous-frame draft benchmarks'
metrics={}
for label,name in [('baseline','PerformanceBaseline'),('candidate','PerformanceCandidate')]:
    entry=next(x for x in release if x['Name']==name)
    source=Path(entry['QaRoot'])/'UserData/Saves/Navezgane/MechaQA_Isolated/polish-review'
    dest=out/label;dest.mkdir(exist_ok=True)
    for f in source.iterdir():
        if f.is_file():shutil.copy2(f,dest/f.name)
    rows=list(csv.DictReader((source/'performance.csv').open(encoding='utf-8-sig')))
    metrics[label]={str(n):statistics.median(float(x['total_p95_ms']) for x in rows if int(x['count'])==n) for n in [1,4]}
results=[]
for n in ['1','4']:
    before=metrics['baseline'][n];after=metrics['candidate'][n];delta=after-before;limit=max(before*.15,1)
    results.append(dict(count=int(n),baseline_ms=before,candidate_ms=after,delta_ms=delta,limit_ms=limit,passed=delta<=limit))
(out/'performance-summary.json').write_text(json.dumps({'metric':'Gait.Update + synchronous Camera.Render/ReadPixels P95; 960x960; three runs of 120 distinct engine frames after 60 warmups; one near or four medium LOD units','allocation_counter_valid':False,'results':results},indent=2),encoding='utf-8')
reports=out/'reports';reports.mkdir(exist_ok=True)
for name in ['release-results.json','delivery-results.json','functional-results.json','offline-final.log']:
    shutil.copy2(r/name,reports/name)
for entry in release:
    if entry['Name']=='SkimFinal':
        for f in Path(entry['QaRoot']).rglob('*qa.txt'):shutil.copy2(f,reports/('final-'+f.name))
for label,name in [('baseline','VideoBaseline'),('candidate','VideoFinal')]:
    source=Path(next(x for x in delivery if x['Name']==name)['QaRoot'])/'UserData/Saves/Navezgane/MechaQA_Isolated/justice-videos'
    for f in source.glob('preview-*.png'):shutil.copy2(f,out/label/f.name)
    shutil.copy2(source/'recording-context.txt',out/label/'recording-context.txt')
manifest={str(f.relative_to(r/'candidate')):hashlib.sha256(f.read_bytes()).hexdigest() for f in (r/'candidate').rglob('*') if f.is_file()}
(out/'candidate-sha256.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
views=[('头部近景','head-close','head-close'),('同几何材质对照','normal-on','material-original-geometry'),('整体正面','ground-front','ground-front'),('整体背面','ground-rear','ground-rear'),('白模／法线','white-normal','white-normal'),('白模／无贴图法线','white-flat','white-flat'),('夜间','night','night'),('滑行','skim-front','skim-front'),('飞行推进器','boost-rear','boost-rear'),('降落','landing-front','landing-front'),('驾驶视角','first-person','first-person'),('单台近景性能场景','performance-scene-1','performance-scene-1'),('四台中景性能场景','performance-scene-4','performance-scene-4')]
table=''.join(f'<tr><td>{x["count"]} 台</td><td>{x["baseline_ms"]:.3f} ms</td><td>{x["candidate_ms"]:.3f} ms</td><td>+{x["delta_ms"]:.3f} ms</td><td>{"通过" if x["passed"] else "未通过"}</td></tr>' for x in results)
options=''.join(f'<option value="{i}">{html.escape(v[0])}</option>' for i,v in enumerate(views))
videos=''.join(f'<section><h2>{title}</h2><div class="pair">'+''.join(f'<figure><figcaption>{label}</figcaption><video controls preload="none" src="{label}/{name}.mp4"></video></figure>' for label in ['baseline','candidate'])+'</div></section>' for name,title in [('walk','步行与转向'),('skim','滑行起停'),('flight','起降'),('combat','剑盾枪切换'),('boarding','上下机姿态')])
page='''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>正义完全体 0.20.0 原生对照</title>
<style>body{margin:0;background:#10161d;color:#e7edf4;font:16px/1.65 system-ui,sans-serif}main{max-width:1380px;margin:auto;padding:32px}h1{margin-bottom:8px}h2{font-size:20px}p{color:#aebdcb}.pair{display:grid;grid-template-columns:1fr 1fr;gap:16px}figure{margin:0;background:#19232e;border-radius:10px;overflow:hidden}figcaption{padding:12px 18px;color:#b7c6d5}img,video{width:100%;display:block}select{background:#223345;color:white;padding:10px;border:1px solid #48627b;border-radius:6px;font:inherit;margin:14px 0}table{border-collapse:collapse;margin:20px 0;width:100%}td,th{padding:10px 14px;text-align:left;border-bottom:1px solid #344453}a{color:#82c7ff}section{margin-top:30px}@media(max-width:800px){.pair{grid-template-columns:1fr}main{padding:16px}}</style>
<main><h1>正义完全体 · 0.19 → 0.20</h1><p>原生 Unity 渲染；保留原模型、骨架与配色。截图按当前骨骼烘焙并直接保存。</p>
<select id="view">OPTIONS</select><div class="pair"><figure><figcaption>0.19.0 基线</figcaption><img id="before"></figure><figure><figcaption>0.20.0 候选</figcaption><img id="after"></figure></div>
<section><h2>固定夹具性能</h2><table><tr><th>场景</th><th>基线 P95 中位数</th><th>候选 P95 中位数</th><th>增量</th><th>门槛</th></tr>TABLE</table><p>每场景三轮，跨真实引擎帧进行 60 帧预热、120 帧采样。记录 Gait.Update 与同步渲染／读回耗时；不是游戏完整帧耗时或 FPS 承诺。Mono 分配计数校准失效，不将原始 0 读数当作零分配证据。</p></section>
VIDEOS<section><h2>验收范围</h2><p>录像为隔离原生脚本驱动。实景人工驾驶、真实双客户端与有效托管分配分析仍需单独验证。旧通用夹具在基线及候选均有相同的 33 项失败；没有计入通过项。</p><p><a href="acceptance.txt">完整验收记录与回滚位置</a> · <a href="performance-summary.json">性能数据</a></p></section></main>
<script>const views=VIEWS;const select=document.querySelector('#view');function show(){const v=views[+select.value];document.querySelector('#before').src='baseline/'+v[1]+'.png';document.querySelector('#after').src='candidate/'+v[2]+'.png'}select.addEventListener('change',show);show();</script></html>'''
page=page.replace('OPTIONS',options).replace('TABLE',table).replace('VIDEOS',videos).replace('VIEWS',json.dumps(views,ensure_ascii=False))
(out/'index.html').write_text(page,encoding='utf-8')
print(json.dumps(results,indent=2));print(out.resolve())
assert all(x['passed'] for x in results), 'Performance gate failed; do not install candidate'

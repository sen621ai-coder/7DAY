"""Assemble labelled review media from actual isolated Unity captures.

The captures contain scripted production poses, not human driving or HUD footage.
"""
import argparse, pathlib, subprocess
from PIL import Image, ImageDraw, ImageFont

p = argparse.ArgumentParser()
p.add_argument('captures', type=pathlib.Path)
p.add_argument('output', type=pathlib.Path)
p.add_argument('--ffmpeg', default=r'D:\DvEnvironment\ffmpeg-8.0.1-full_build-shared\bin\ffmpeg.exe')
args = p.parse_args()
args.output.mkdir(parents=True, exist_ok=True)
font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 23)
small = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 18)

def panel(left, right, title, left_label='第三人称', right_label='第一视角＋真实机体', note='隔离引擎演示 · 原速 · 不含真人驾驶输入和 HUD'):
    canvas = Image.new('RGB', (1280, 580), '#0b111b')
    canvas.paste(Image.open(left).convert('RGB'), (0, 64))
    canvas.paste(Image.open(right).convert('RGB'), (640, 64))
    d = ImageDraw.Draw(canvas)
    d.text((20, 5), title, font=font, fill='#bcebed')
    d.text((20, 36), left_label, font=small, fill='#aac8df')
    d.text((660, 36), right_label, font=small, fill='#aac8df')
    d.line((640, 64, 640, 544), fill='#35455c')
    d.text((20, 550), note, font=small, fill='#8796ac')
    return canvas

def movie(folder, target, fps=60):
    subprocess.run([args.ffmpeg, '-hide_banner', '-loglevel', 'error', '-y', '-framerate', str(fps), '-i', str(folder / '%04d.png'), '-c:v', 'libx264', '-threads', '2', '-crf', '19', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', str(args.output / target)], check=True)

complete = args.output / 'complete-frames'
complete.mkdir(exist_ok=True)
index = 0
for action, label in [('left-cut', '斩击 1'), ('right-cut', '斩击 2'), ('heavy', '蓄力重劈')]:
    count = len(list(args.captures.glob(f'view-sequence-{action}-third-*.png')))
    for frame in range(count):
        phase = frame / (count - 1)
        stage = '承重起手' if phase < .22 else '发力挥砍' if phase <= .44 else '制动停剑' if phase <= .56 else '回收'
        prefix = 'view-sequence-' + action
        panel(args.captures / f'{prefix}-third-{frame:03d}.png', args.captures / f'{prefix}-first-{frame:03d}.png', f'初号机（完全体）  {label} · {stage}').save(complete / f'{index:04d}.png')
        index += 1
movie(complete, 'complete-dual-view.mp4')

prototype = args.output / 'prototype-frames'
prototype.mkdir(exist_ok=True)
for frame in range(120):
    panel(args.captures / f'view-prototype-walk-third-{frame:03d}.png', args.captures / f'view-prototype-walk-first-{frame:03d}.png', '初号机（试验体）  步行＋手掌炮后坐（表现探针）').save(prototype / f'{frame:04d}.png')
movie(prototype, 'prototype-dual-view.mp4')

panel(args.captures / 'view-energy-impact-000.png', args.captures / 'view-missile-impact-005.png', '激光与导弹命中表现对照', '激光：青白接触火花／电弧', '导弹：原生火球／烟尘', '原生引擎命中静帧 · 激光溅射伤害与爆炸表现分离').save(args.output / 'laser-missile-comparison.png')
panel(args.captures / 'view-complete-third-000.png', args.captures / 'view-complete-first-000.png', '初号机（完全体） · 双视角', note='原生引擎隔离渲染 · 第一视角仅隐藏镜头遮挡部件').save(args.output / 'complete-views.png')
panel(args.captures / 'view-prototype-third-000.png', args.captures / 'view-prototype-first-000.png', '初号机（试验体） · 双视角', note='原生引擎隔离渲染 · 第一视角保留真实手臂、武器和腿部').save(args.output / 'prototype-views.png')
print('Exported two dual-view clips, weapon comparison and camera stills:', args.output)

if (args.captures / 'weight-entry-000.png').exists():
    ceremony = args.output / 'ceremony-frames'
    ceremony.mkdir(exist_ok=True)
    index = 0
    for prefix, label in [('weight-entry', '上机 7 秒'), ('weight-exit', '下机 6 秒')]:
        for frame in sorted(args.captures.glob(prefix+'-*.png')):
            im = Image.new('RGB',(640,540),'#0b111b');im.paste(Image.open(frame).convert('RGB'),(0,44));d=ImageDraw.Draw(im);d.text((16,5),label+' · 承重 / 触底 / 停顿 / 起立',font=small,fill='#bcebed');d.text((16,518),'原速 · 隔离引擎脚本演示',font=small,fill='#8796ac');im.save(ceremony/f'{index:04d}.png');index+=1
    movie(ceremony,'complete-ceremony.mp4',30)
    rear = args.output / 'rear-frames';rear.mkdir(exist_ok=True)
    for i,frame in enumerate(sorted(args.captures.glob('view-weight-turn-rear-*.png'))):
        Image.open(frame).save(rear/f'{i:04d}.png')
    movie(rear,'complete-turn-rear.mp4',30)
    panel(args.captures/'view-laser-10fps-complete-third-000.png',args.captures/'view-laser-10fps-complete-first-000.png','完全体激光 · 注入 10 FPS 帧时','第三人称白芯青辉','第一视角光束', '原生材质与像素检查 · 游戏帧 token 在隔离探针中模拟').save(args.output/'complete-laser-10fps.png')
    panel(args.captures/'view-laser-10fps-prototype-third-000.png',args.captures/'view-laser-10fps-prototype-first-000.png','试验体激光 · 注入 10 FPS 帧时','第三人称白芯青辉','第一视角光束', '原生材质与像素检查 · 游戏帧 token 在隔离探针中模拟').save(args.output/'prototype-laser-10fps.png')

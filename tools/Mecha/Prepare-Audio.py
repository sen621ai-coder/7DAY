"""Deterministic original mechanical sound assets; no third-party samples."""
import argparse, math, pathlib, random, struct, wave
args=argparse.ArgumentParser();args.add_argument('--only',nargs='+');selected=args.parse_args().only
out=pathlib.Path(__file__).resolve().parents[2]/'ZZ-PZAEC_Mecha/Resources/Audio'
out.mkdir(parents=True,exist_ok=True)
spec={'idle':(2,55,.15),'servo':(2,180,.18),'boost':(2,95,.28),
      'step-left':(.65,62,.8),'step-right':(.7,57,.8),'jump':(.65,110,.65),
      'land':(1,42,.95),'hatch-open':(.7,140,.5),'hatch-close':(.6,85,.6),'ready':(.65,330,.3),'sword':(.45,125,.5),'shield':(.28,240,.5),'head-laser':(.22,680,.4),'palm-laser':(.18,520,.35),'laser-impact':(.20,900,.30),'sword-impact':(.18,260,.38)}
for name,(seconds,freq,gain) in spec.items():
    if selected and name not in selected:continue
    rate=22050;rng=random.Random(name);count=int(seconds*rate);pcm=[];filtered=0
    loop=name in ('idle','servo','boost')
    for i in range(count):
        t=i/rate;x=i/count;noise=rng.uniform(-1,1);filtered=filtered*.92+noise*.08
        if loop:
            env=min(1,x*30,(1-x)*30);v=.55*math.sin(2*math.pi*freq*t)+.2*math.sin(2*math.pi*freq*3*t)+filtered*(2 if name=='boost' else .5)
        elif name=='sword':
            env=math.sin(math.pi*x)**2;v=filtered*3+.18*math.sin(2*math.pi*(90+140*x)*t)
        elif name in ('head-laser','palm-laser','laser-impact'):
            env=math.sin(math.pi*x)**2;v=.65*math.sin(2*math.pi*(freq-400*x)*t)+filtered
        elif name=='ready':
            env=math.sin(math.pi*x)**2;v=math.sin(2*math.pi*(freq if x<.5 else freq*1.5)*t)*.5
        elif name=='sword-impact':
            env=(1-math.exp(-t*180))*math.exp(-x*7);v=.55*math.sin(2*math.pi*(freq-80*x)*t)+filtered*2+noise*.15*math.exp(-t*30)
        else:
            env=(1-math.exp(-t*180))*math.exp(-x*(8 if name.startswith('step') or name=='land' else 4))
            v=.65*math.sin(2*math.pi*freq*t)+filtered*2+noise*.15*math.exp(-t*30)
        pcm.append(struct.pack('<h',int(max(-.98,min(.98,v*env*gain))*32767)))
    with wave.open(str(out/(name+'.wav')),'wb') as w:w.setnchannels(1);w.setsampwidth(2);w.setframerate(rate);w.writeframes(b''.join(pcm))
print('Generated',len(selected) if selected else len(spec),'original mono PCM mechanical sounds')

# Smooth reactor bed: integral cycles over four seconds, no per-loop fade dip,
# random impulses or high harmonics. AudioSource handles start/stop fades.
rate=22050
if not selected or 'reactor-idle' in selected:
 with wave.open(str(out/'reactor-idle.wav'),'wb') as w:
    w.setnchannels(1);w.setsampwidth(2);w.setframerate(rate)
    w.writeframes(b''.join(struct.pack('<h',round(32767*.10*(.85*math.sin(2*math.pi*80*i/rate)+.15*math.sin(2*math.pi*120*i/rate)))) for i in range(rate*4)))

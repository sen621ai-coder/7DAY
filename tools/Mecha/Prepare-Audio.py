"""Deterministic original mechanical sound assets; no third-party samples."""
import math, pathlib, random, struct, wave
out=pathlib.Path(__file__).resolve().parents[2]/'ZZ-PZAEC_Mecha/Resources/Audio'
out.mkdir(parents=True,exist_ok=True)
spec={'idle':(2,55,.15),'servo':(2,180,.18),'boost':(2,95,.28),
      'step-left':(.65,62,.8),'step-right':(.7,57,.8),'jump':(.65,110,.65),
      'land':(1,42,.95),'hatch-open':(.7,140,.5),'hatch-close':(.6,85,.6),'ready':(.65,330,.3)}
for name,(seconds,freq,gain) in spec.items():
    rate=22050;rng=random.Random(name);count=int(seconds*rate);pcm=[];filtered=0
    loop=name in ('idle','servo','boost')
    for i in range(count):
        t=i/rate;x=i/count;noise=rng.uniform(-1,1);filtered=filtered*.92+noise*.08
        if loop:
            env=min(1,x*30,(1-x)*30);v=.55*math.sin(2*math.pi*freq*t)+.2*math.sin(2*math.pi*freq*3*t)+filtered*(2 if name=='boost' else .5)
        elif name=='ready':
            env=math.sin(math.pi*x)**2;v=math.sin(2*math.pi*(freq if x<.5 else freq*1.5)*t)*.5
        else:
            env=(1-math.exp(-t*180))*math.exp(-x*(8 if name.startswith('step') or name=='land' else 4))
            v=.65*math.sin(2*math.pi*freq*t)+filtered*2+noise*.15*math.exp(-t*30)
        pcm.append(struct.pack('<h',int(max(-.98,min(.98,v*env*gain))*32767)))
    with wave.open(str(out/(name+'.wav')),'wb') as w:w.setnchannels(1);w.setsampwidth(2);w.setframerate(rate);w.writeframes(b''.join(pcm))
print('Generated',len(spec),'original mono PCM mechanical sounds')

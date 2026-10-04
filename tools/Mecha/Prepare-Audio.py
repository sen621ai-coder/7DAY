"""Original deterministic mechanical sound synthesis; no borrowed samples.

Mono PCM16; legacy assets are preserved. Production uses variant-prefixed assets.
No powered-idle bed. Movement loops have irregular hydraulic / air textures,
without a repeating engine fundamental or a per-cycle fade dip.
"""
import argparse, array, json, math, pathlib, random, sys, wave
RATE=22050
OUT=pathlib.Path(__file__).resolve().parents[2]/'ZZ-PZAEC_Mecha/Resources/Audio'
LOOPS={'servo':4.,'thruster':4.,'laser-charge':4.}
DURATIONS={'power-on':.78,'power-off':.62,'step-left':.52,'step-right':.54,'land':.95,'jump':.48,'entry-brace':.36,'kneel-lock':.42,'hatch-open':.65,'hatch-close':.65,'stand-lock':.38,'ready':.32,'sword-prepare':.32,'sword-swing':.44,'sword-heavy':.62,'sword-brake':.29,'shield':.30,'palm-laser':.24,'head-laser':.34,'missile-release':.75,'laser-impact':.28,'sword-impact':.38}
DESIGN={'servo':'irregular geared hydraulic motion, no bass oscillator','thruster':'broadband pressure exhaust, no firing-order rhythm','laser-charge':'quiet high electrical texture; runtime charge raises pitch','power-on':'relay pair, capacitor rise, final contactor; then silence','power-off':'contactor release, falling capacitor, pressure release','step-left':'sole contact, delayed structural load, inharmonic metal tail','step-right':'alternate sole resonance / brace timing','land':'load-bearing contact / frame settling, not explosion','jump':'pressure valve and piston impulse','entry-brace':'short brake / load brace','kneel-lock':'two knee ratchet / lock contacts','hatch-open':'unlatch, hydraulic slide, end stop','hatch-close':'slide, panel contact, locking bolt','stand-lock':'support latch / final lock; dwell silent','ready':'restrained electrical confirmation','sword-prepare':'grip actuator / blade preload','sword-swing':'air cutting sweep / short metal tension','sword-heavy':'longer lower air sweep / stronger preload','sword-brake':'joint brake / damped blade flex','shield':'electrical snap / tight metal response','palm-laser':'compact bright coil discharge / fast electrical pulse','head-laser':'deeper capacitor punch / paired arc discharge','missile-release':'pod latch / pneumatic ejector / short exhaust, no explosion','laser-impact':'localized conductive crackle','sword-impact':'inharmonic steel contact / short frame resonance'}

def smooth(x):
    x=max(0.,min(1.,x));return x*x*(3-2*x)

def layers(name,count):
    rng=random.Random(name);raw=[rng.uniform(-1,1) for _ in range(count)];states=[0.,0.,0.];values=[[],[],[]]
    scale=.78 if name.startswith('complete-') else 1.
    coefficients=[1-math.exp(-2*math.pi*f*scale/RATE) for f in (130,650,3200)]
    for x in raw[-RATE//2:]+raw:
        for j,k in enumerate(coefficients):states[j]+=k*(x-states[j]);values[j].append(states[j])
    return raw,*[v[-count:] for v in values]

def make(cue,complete):
    mass=1. if complete else .76;seconds=LOOPS.get(cue,DURATIONS.get(cue,.35))
    if complete and cue in ('step-left','step-right','land','hatch-close','kneel-lock'):seconds*=1.18
    count=round(seconds*RATE);seed=('complete-' if complete else 'prototype-')+cue
    raw,low,mid,fast=layers(seed,count);rng=random.Random(seed+'/modulation');knots=[rng.uniform(.55,1.) for _ in range(24)];samples=[]
    def contact(t,at,strength=1.,frequency=75.,decay=.14,metal=.20):
        age=t-at
        if age<0:return 0.
        frequency*=.78 if complete else 1.;attack=1-math.exp(-age*650)
        load=.62*math.sin(2*math.pi*frequency*age)*math.exp(-age/decay)
        ring=sum(a*math.sin(2*math.pi*frequency*f*age)*math.exp(-age/(decay*d)) for f,a,d in ((3.17,.35,.62),(5.83,.20,.34),(9.31,.13,.22)))
        return strength*attack*(load+metal*ring)
    for i in range(count):
        t=i/RATE;x=i/count;band=fast[i]-mid[i];hydraulics=mid[i]-low[i]
        if cue in LOOPS:
            k=x*len(knots);j=int(k);blend=smooth(k-j);modulation=knots[j]*(1-blend)+knots[(j+1)%len(knots)]*blend
            if cue=='servo':value=(.68*hydraulics+.26*band)*modulation
            elif cue=='thruster':value=(.58*mid[i]+.22*low[i]+.12*band)*modulation
            else:value=.12*band+.025*(math.sin(2*math.pi*1260*t)+math.sin(2*math.pi*1910*t))
        elif cue.startswith('step') or cue=='land':
            side=1.04 if cue.endswith('right') else 1.;impact=1. if cue=='land' else .78
            value=contact(t,.008,impact,69*side,.19*mass,.22)+contact(t,.070*mass,.43*impact,112*side,.12*mass,.38)+(band*.27+mid[i]*.36)*math.exp(-t/.042)
        elif cue in ('entry-brace','kneel-lock','stand-lock','sword-prepare','sword-brake'):
            value=contact(t,.008,.55,125,.075,.7)+contact(t,.11 if cue=='kneel-lock' else .065,.28,190,.055,.9)+hydraulics*.22*math.sin(math.pi*x)**2
        elif cue in ('hatch-open','hatch-close'):
            slide=smooth(t/.09)*(1-smooth((t-seconds*.58)/.13));value=hydraulics*.38*slide+band*.075*slide
            if cue=='hatch-open':value+=contact(t,.012,.35,180,.07,.9)+contact(t,seconds*.70,.25,135,.075,.5)
            else:value+=contact(t,seconds*.56,.68,95,.10,.6)+contact(t,seconds*.73,.30,205,.045,.9)
        elif cue in ('power-on','power-off','ready'):
            value=contact(t,.012,.26,260,.03,1.)+contact(t,.14 if cue!='ready' else .10,.16,380,.024,1.)
            env=math.sin(math.pi*x)**2;frequency=(480+880*x) if cue!='power-off' else (1200-870*x)
            value+=env*(.10*math.sin(2*math.pi*frequency*t)+.16*band)
            if cue=='power-on':value+=contact(t,.62,.34,130,.07,.4)
        elif cue in ('sword-swing','sword-heavy'):
            env=math.sin(math.pi*x)**(1.6 if cue=='sword-heavy' else 2.4)
            value=env*(1.05*band+.30*hydraulics)+contact(t,.006,.22,140,.08,.8)+.035*math.sin(2*math.pi*(340+460*x)*t)*env
        elif cue in ('palm-laser','head-laser','laser-impact','shield'):
            head=cue=='head-laser';env=(1-math.exp(-t*900))*math.exp(-t/(.080 if head else .045));frequency=(1350-800*x) if head else (2150-1250*x)
            value=env*(.18*math.sin(2*math.pi*frequency*t)+.65*band+.14*raw[i])
            if head:value+=contact(t,.008,.33,135,.08,.5)+contact(t,.042,.21,225,.055,.8)
            if cue=='laser-impact':value+=band*.15*math.exp(-t/.06)*math.sin(t*740)**2
            if cue=='shield':value+=contact(t,.014,.30,245,.075,.8)
        elif cue=='missile-release':
            jet=smooth((t-.10)/.045)*(1-smooth((t-.28)/.20));value=contact(t,.008,.37,210,.06,.9)+contact(t,.072,.50,92,.10,.35)+jet*(.75*mid[i]+.27*band)
        elif cue=='sword-impact':value=contact(t,.006,.78,160,.12,1.)+band*.42*math.exp(-t/.025)
        elif cue=='jump':value=contact(t,.015,.38,100,.11,.4)+math.sin(math.pi*x)**2*(.55*mid[i]+.28*band)
        else:raise ValueError(cue)
        if cue not in LOOPS:value*=smooth(t/.004)*smooth((seconds-t)/.024)
        samples.append(value)
    mean=sum(samples)/len(samples);samples=[v-mean for v in samples]
    if cue in LOOPS:
        seam=samples[-1]-samples[0];samples=[v-seam*i/(len(samples)-1) for i,v in enumerate(samples)]
        dc=sum(samples)/len(samples);samples=[v-dc for v in samples]
    peak=max(abs(v) for v in samples) or 1.;target=.27 if cue=='servo' else .36 if cue=='thruster' else .14 if cue=='laser-charge' else .76
    samples=[v*min(1.,target/peak) for v in samples]
    if cue not in LOOPS:samples[0]=samples[-1]=0.
    return samples

def write(path,samples):
    pcm=array.array('h',(round(max(-.999,min(.999,v))*32767) for v in samples))
    if sys.byteorder!='little':pcm.byteswap()
    with wave.open(str(path),'wb') as w:w.setnchannels(1);w.setsampwidth(2);w.setframerate(RATE);w.writeframes(pcm.tobytes())

def read(path):
    with wave.open(str(path),'rb') as w:
        assert w.getnchannels()==1 and w.getsampwidth()==2 and w.getframerate()==RATE
        pcm=array.array('h');pcm.frombytes(w.readframes(w.getnframes()))
        if sys.byteorder!='little':pcm.byteswap()
        return [x/32767 for x in pcm]

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--only',nargs='+');parser.add_argument('--review-dir',type=pathlib.Path);args=parser.parse_args();OUT.mkdir(parents=True,exist_ok=True);manifest=[]
    for complete in (False,True):
        for cue in list(DURATIONS)+list(LOOPS):
            if not complete and (cue.startswith('sword-') and cue!='sword-impact' or cue in ('head-laser','laser-charge')):continue
            name=('complete-' if complete else 'prototype-')+cue
            if args.only and name not in args.only and cue not in args.only:continue
            samples=make(cue,complete);write(OUT/(name+'.wav'),samples)
            manifest.append(dict(name=name,cue=cue,variant='complete' if complete else 'prototype',loop=cue in LOOPS,seconds=len(samples)/RATE,peak=max(abs(x) for x in samples),rms=math.sqrt(sum(x*x for x in samples)/len(samples)),design=DESIGN[cue]))
    generated=len(manifest);manifest_path=OUT/'mecha-audio-manifest.json'
    if args.only and manifest_path.exists():
        previous=json.loads(manifest_path.read_text(encoding='utf-8'))['assets'];merged={x['name']:x for x in previous};merged.update({x['name']:x for x in manifest});manifest=list(merged.values())
    manifest_path.write_text(json.dumps(dict(rate=RATE,format='mono PCM16',copyright='Original procedural synthesis; no external samples',assets=manifest),ensure_ascii=False,indent=2),encoding='utf-8')
    if args.review_dir:
        args.review_dir.mkdir(parents=True,exist_ok=True);track=[];timeline=[]
        for label,name in [('old servo','servo'),('prototype hydraulic servo','prototype-servo'),('complete hydraulic servo','complete-servo'),('old step','step-left'),('prototype sole and frame','prototype-step-left'),('complete sole and frame','complete-step-left'),('old beam','palm-laser'),('prototype palm coil','prototype-palm-laser'),('complete head capacitor','complete-head-laser')]:
            timeline.append(dict(start_seconds=len(track)/RATE,label=label,asset=name+'.wav'));track+=read(OUT/(name+'.wav'))[:RATE]+[0.]*round(.4*RATE)
        write(args.review_dir/'old-new-comparison.wav',track);(args.review_dir/'comparison-timeline.json').write_text(json.dumps(timeline,indent=2),encoding='utf-8')
        for variant in ('prototype','complete'):
            order=['power-on','entry-brace','kneel-lock','hatch-open','hatch-close','stand-lock','step-left','step-right','land','missile-release','head-laser' if variant=='complete' else 'palm-laser']
            if variant=='complete':order+=['sword-prepare','sword-swing','sword-heavy','sword-brake']
            order+=['power-off'];samples=[];timeline=[]
            for cue in order:timeline.append(dict(start_seconds=len(samples)/RATE,cue=cue));samples+=read(OUT/(variant+'-'+cue+'.wav'))+[0.]*round(.38*RATE)
            write(args.review_dir/(variant+'-cue-demo.wav'),samples);(args.review_dir/(variant+'-timeline.json')).write_text(json.dumps(timeline,indent=2),encoding='utf-8')
        (args.review_dir/'design-manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
    print('Generated',generated,'original variant mechanical assets; legacy WAVs preserved')

if __name__=='__main__':main()

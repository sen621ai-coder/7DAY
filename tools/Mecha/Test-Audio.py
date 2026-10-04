"""Meaningful asset checks: levels, silent event boundaries, loop seam / DC,
non-engine periodicity, variant distinction and original design coverage."""
import array, hashlib, json, math, pathlib, sys, wave
root=pathlib.Path(__file__).resolve().parents[2]
folder=root/'ZZ-PZAEC_Mecha/Resources/Audio'
manifest=json.loads((folder/'mecha-audio-manifest.json').read_text(encoding='utf-8'))
assert manifest['rate']==22050 and manifest['format']=='mono PCM16'
assets={x['name']:x for x in manifest['assets']}
assert len(assets)==44
total=0
for name,meta in assets.items():
    path=folder/(name+'.wav');total+=path.stat().st_size
    with wave.open(str(path),'rb') as w:
        assert (w.getnchannels(),w.getsampwidth(),w.getframerate(),w.getcomptype())==(1,2,22050,'NONE'),name
        pcm=array.array('h');pcm.frombytes(w.readframes(w.getnframes()))
        if sys.byteorder!='little':pcm.byteswap()
    a=[x/32767 for x in pcm];peak=max(abs(x) for x in a);rms=math.sqrt(sum(x*x for x in a)/len(a));dc=sum(a)/len(a)
    assert .015<peak<.80 and .001<rms<.30,(name,peak,rms)
    assert abs(dc)<.004,(name,dc)
    assert abs(len(a)/22050-meta['seconds'])<1/22050
    assert abs(peak-meta['peak'])<1/32767
    assert meta['design'] and meta['variant'] in ('complete','prototype')
    if meta['loop']:
        assert len(a)==22050*4 and abs(a[0]-a[-1])<=1/32767,(name,'loop boundary')
        quarters=[math.sqrt(sum(x*x for x in a[i:i+22050])/22050) for i in range(0,len(a),22050)]
        assert max(quarters)/min(quarters)<2,(name,'periodic fade dip',quarters)
        # An engine-like bass note would repeat strongly at a rotation / firing
        # interval. Random moving servo / air texture should not have that peak.
        if meta['cue'] in ('servo','thruster'):
            segment=a[22050:33075];energy=sum(x*x for x in segment)
            correlation=max(abs(sum(segment[i]*segment[i+lag] for i in range(len(segment)-lag))/energy) for lag in range(90,500,7))
            assert correlation<.30,(name,'strong bass periodicity',correlation)
    else:
        assert a[0]==0 and a[-1]==0,(name,'event edge click')
        assert .2<=len(a)/22050<=1.2
for cue in ('step-left','step-right','land','servo','thruster','power-on','hatch-close','missile-release'):
    a=folder/('prototype-'+cue+'.wav');b=folder/('complete-'+cue+'.wav')
    assert hashlib.sha256(a.read_bytes()).digest()!=hashlib.sha256(b.read_bytes()).digest(),cue
assert 'prototype-palm-laser' in assets and 'complete-head-laser' in assets
assert all('complete-'+cue in assets for cue in ('sword-prepare','sword-swing','sword-heavy','sword-brake','laser-charge'))
assert not any('idle' in name for name in assets)
print('PASS',len(assets),'original variant PCM assets, no clipping / DC / edge clicks, seamless texture loops and low bass periodicity')
print('PASS source, impact, mechanical locks, two laser identities and sword phases distinct; no persistent idle bed; bytes=',total)

"""Exported hinge geometry must close both knees and stay on its own joint."""
import argparse,collections,json,struct,math
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--resources',type=Path,default=Path(__file__).resolve().parents[2]/'ZZ-PZAEC_Mecha/Resources');args=p.parse_args()
d=json.loads((args.resources/'samurai_style_gundam_mecha_rig.json').read_text());raw=(args.resources/'samurai_style_gundam_mecha_rig.bin').read_bytes()
parts=[p for p in d['parts'] if p.get('generatedRepair')=='armoured-knee-joint-v2']
assert len(parts)==2,'both knees require a closed mechanism'
for p in parts:
    side=p['role'][-1];bone=next(i for i,j in enumerate(d['joints']) if j['name']=='Knee'+side);origin=d['joints'][bone]['position']
    v=list(struct.iter_unpack('<8f2i2f',raw[p['offset']:p['offset']+p['vertices']*48]));ix=struct.unpack_from('<'+'I'*p['indices'],raw,p['offset']+p['vertices']*48)
    edges=collections.Counter();directed=collections.Counter()
    for a in v:
        assert a[8:]==(bone,bone,1.,0.),'incorrect knee ownership'
        q=[a[i]-origin[i] for i in range(3)]
        assert abs(q[0])<.101 and -.156<q[1]<.200 and -.141<q[2]<.180,'oversized knee mechanism'
    for i in range(0,len(ix),3):
        tri=[tuple(round(c,6) for c in v[ix[i+j]][:3]) for j in range(3)]
        assert len(set(tri))==3
        for j in range(3):
            edge=(tri[j],tri[(j+1)%3]);directed[edge]+=1;edges[tuple(sorted(edge))]+=1
    assert set(edges.values())=={2},'open knee shell'
    assert all(directed[(b,a)]==n for (a,b),n in directed.items()),'inconsistent knee winding'
    assert abs((max(a[0] for a in v)+min(a[0] for a in v))/2-origin[0])<1e-5
    assert len({a[6:8] for a in v})==4,'graphite core and white/red/gold plates required'
    assert len(ix)//3==416
    print('PASS',side,'compact closed knee with four material regions; rigid Knee ownership')

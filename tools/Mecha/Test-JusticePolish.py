"""Derived Justice geometry/material integrity and unchanged 0.19 interfaces."""
import argparse, hashlib, json, struct
from pathlib import Path
import numpy as np
from PIL import Image

p = argparse.ArgumentParser()
p.add_argument('--mod-root', type=Path, required=True)
p.add_argument('--baseline', type=Path, required=True)
a = p.parse_args()
r = a.mod_root/'Resources/Justice'
b = a.baseline/'Resources/Justice'
d = json.loads((r/'justice.json').read_text(encoding='utf-8'))
old = json.loads((b/'justice.json').read_text(encoding='utf-8'))
m = json.loads((r/d['polishManifest']).read_text(encoding='utf-8'))
for key in old:
    if key != 'version': assert d[key] == old[key], key
for name in ['justice.glb', 'justice.bin', 'tex_0.png', 'tex_1.png', 'tex_2.png']:
    assert (r/name).read_bytes() == (b/name).read_bytes(), name
assert m['sourceSha256'] == hashlib.sha256((r/'justice.glb').read_bytes()).hexdigest()
assert m['sourceTangentAudit']['matchedExportVertices'] > 6500
assert len(d['bones']) == 65 and len(d['parts']) == 19
raw = (r/m['geometry']).read_bytes()
dt = np.dtype([('v','<f4',3),('n','<f4',3),('uv','<f4',2),('j','<i4',4),('w','<f4',4),('t','<f4',4)])
assert dt.itemsize == m['vertexStride'] == 80
for lod, budget in [(0,60000), (1,20000)]:
    parts = [x for x in m['parts'] if x['lod'] == lod]
    assert sorted(x['sourcePart'] for x in parts) == list(range(19))
    total = sum(x['indices']//3 for x in parts)
    assert total <= budget
    for x in parts:
        v = np.frombuffer(raw, dt, x['vertices'], x['offset'])
        ix = np.frombuffer(raw, '<i4', x['indices'], x['offset']+x['vertices']*80)
        for k in ['v','n','uv','w','t']: assert np.isfinite(v[k]).all()
        assert ix.min() >= 0 and ix.max() < len(v)
        assert len(np.unique(ix)) == len(v), 'unused skin vertices'
        assert (v['j'] >= 0).all() and (v['j'] < 65).all()
        assert np.max(abs(v['w'].sum(1)-1)) < .0001 and v['w'].min() >= 0
        assert np.max(abs(np.linalg.norm(v['t'][:,:3],axis=1)-1)) < .001
        assert np.max(abs(np.sum(v['t'][:,:3]*v['n'],axis=1))) < .001
        assert set(v['t'][:,3]) <= {-1,1}
        tris = ix.reshape(-1,3); half = len(tris)//2
        assert np.array_equal(tris[:half][:,[0,2,1]],tris[half:])
    print('PASS LOD',lod,total,'triangles; all roles, normalized weights/tangents, no orphan vertices')
for entry in m['patchAudit']:
    source = d['parts'][entry['sourcePart']]
    assert source['role'] == entry['role']
    selected = [t for patch in entry['patches'] for t in patch['triangles']]
    assert len(selected) == len(set(selected)) and max(selected, default=0) < source['indices']//6
    assert all(0 <= x['bone'] < 65 and 0 < x['bevelMeters'] <= .004 for x in entry['patches'])
for name in ['albedo','normal','material','emission']:
    assert Image.open(r/m[name]).size == (2048,2048)
n = np.asarray(Image.open(r/m['normal']))
assert n.shape == (2048,2048,4) and np.all(n[:,:,0] == 255)
mask = np.asarray(Image.open(r/m['material']))
assert len(np.unique(mask[:,:,0])) >= 3 and len(np.unique(mask[:,:,3])) >= 3
assert all(0 <= jet['bone'] < 65 and len(jet['position']) == 3 for jet in m['jets'])
print('PASS originals, skeleton, sockets, weapon visibility, clips, patch audit, 2K maps, AG normal encoding')

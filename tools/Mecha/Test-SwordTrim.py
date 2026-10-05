"""Exact reviewed original trim identities and physical clearance to silver core."""
import argparse, hashlib, json
from pathlib import Path
import numpy as np

root = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser()
parser.add_argument('--resources', type=Path, default=root/'ZZ-PZAEC_Mecha/Resources')
args = parser.parse_args()
doc = json.loads((args.resources/'samurai_style_gundam_mecha_rig.json').read_text())
raw = (args.resources/'samurai_style_gundam_mecha_rig.bin').read_bytes()
fixture = json.loads((root/'tools/Mecha/Sword-Trim-Landmarks.json').read_text())
wanted = {p['faceSha256'] for p in fixture['landmarks']}
dtype = np.dtype([('vertex', '<f4', 8), ('bones', '<i4', 2), ('weights', '<f4', 2)])
found = {}; core = []
for part in doc['parts']:
    v = np.frombuffer(raw, dtype, part['vertices'], part['offset'])
    ix = np.frombuffer(raw, '<u4', part['indices'], part['offset']+48*part['vertices']).reshape(-1, 3)
    if part.get('generatedRepair') and part['role']=='SwordBlade': core.append(v['vertex'][ix][:, :, :3])
    for tri in v[ix]:
        key = hashlib.sha256(tri['vertex'].tobytes()).hexdigest()
        if key in wanted:
            assert key not in found, 'duplicate restored surface'
            found[key] = (part['role'], tri)
assert set(found)==wanted, f'{len(wanted-set(found))} original gold surfaces missing from runtime sword'
assert len(found)==doc['swordTrimRestoration']['sourceFaces']==96
sword = next(i for i, j in enumerate(doc['joints']) if j['name']=='Sword')
for role, tri in found.values():
    assert role=='SwordBlade' and np.all(tri['bones']==sword) and np.all(tri['weights']==[1, 0])
print('PASS all', len(found), 'reviewed original gold faces retain exact geometry / UVs / normals and rigid Sword ownership')
tip = np.array(doc['swordAnchors']['bladeTip'])
axis = np.array(doc['swordAnchors']['bladeRoot'])-tip; axis /= np.linalg.norm(axis)
normal = np.array(doc['swordAnchors']['bladeNormal']); normal -= axis*np.dot(axis, normal); normal /= np.linalg.norm(normal)
core_depth = (np.concatenate(core)-tip)@normal
gap = 1.; sides = {-1: 0, 1: 0}
for role, tri in found.values():
    xyz = tri['vertex'][:, :3]; n = np.cross(xyz[1]-xyz[0], xyz[2]-xyz[0])
    depth = (xyz-tip)@normal
    along = (xyz-tip)@axis; width = (xyz-tip)@np.cross(normal, axis)
    assert np.all(width >= .006+.012*along-.001) and np.all(width <= .183+.018*along+.001), 'restored trim protrudes beyond blade outline'
    if n@normal>0:
        gap = min(gap, depth.min()-core_depth.max()); sides[1] += 1
    else:
        gap = min(gap, core_depth.min()-depth.max()); sides[-1] += 1
assert min(sides.values())>0 and gap>.00199, ('core hides trim or coplanar flicker', gap, sides)
print('PASS original trim on both sides stays outside the closed core; minimum gap', gap)

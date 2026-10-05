"""Verify rendered symmetry, oriented surfaces/UVs and original lower-tip landmarks.

Checks real stored mesh data, independently of the ownership volume rules.
"""
import argparse, collections, json, pathlib, struct

root = pathlib.Path(__file__).resolve().parents[2]
p = argparse.ArgumentParser()
p.add_argument('--resources', type=pathlib.Path, default=root/'ZZ-PZAEC_Mecha/Resources')
args = p.parse_args()
doc = json.loads((args.resources/'samurai_style_gundam_mecha_rig.json').read_text())
raw = (args.resources/'samurai_style_gundam_mecha_rig.bin').read_bytes()
ids = {j['name']: i for i, j in enumerate(doc['joints'])}
surfaces = {}
vertices = {}
for role in ('WingL', 'WingR'):
    faces = collections.Counter(); points = []
    for part in doc['parts']:
        if part['role'] != role: continue
        records = list(struct.iter_unpack('<8f2i2f', raw[part['offset']:part['offset']+48*part['vertices']]))
        assert all(v[8:10] == (ids[role], ids[role]) and v[10:] == (1., 0.) for v in records)
        points.extend(v[:3] for v in records)
        ix = struct.unpack_from('<'+'I'*part['indices'], raw, part['offset']+48*part['vertices'])
        for i in range(0, len(ix), 3):
            tri = [list(records[k][:8]) for k in ix[i:i+3]]
            if role == 'WingR':
                for v in tri: v[0] *= -1; v[3] *= -1
                tri = [tri[0], tri[2], tri[1]]
            faces[(part['material'], tuple(tuple(v) for v in tri))] += 1
    assert faces
    surfaces[role] = faces; vertices[role] = points
assert surfaces['WingL'] == surfaces['WingR'], 'Asymmetric wing topology, winding, normals or texture coordinates'
print('PASS every rendered wing face mirrors its counterpart, including normals / UVs / winding')
# Captured on the intact original-derived left tip before the fix. Exact geometry
# symmetry alone would also pass if both lower wings had been removed.
for side, sign in [('L', -1), ('R', 1)]:
    tip = (sign*.77156734, .50134009, -.65893555)
    error = min(sum((a-b)**2 for a,b in zip(v,tip))**.5 for v in vertices['Wing'+side])
    assert error < .00001, (side, 'missing lower tip', error)
    assert max(v[2] for v in vertices['Wing'+side]) < -.12, 'Forward gold scrap in rear wing'
    print('PASS', side, 'lower-fin tip present and independently bound; error', error)
assert doc['sourceOnlyParts'] and all(p['role']=='WingR' for p in doc['sourceOnlyParts'])
print('PASS old asymmetric right assembly archived outside the runtime part list')

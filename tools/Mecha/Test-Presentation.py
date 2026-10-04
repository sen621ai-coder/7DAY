"""0.12 asset invariants against the full pre-change backup; no game mocks."""
import collections, hashlib, json, pathlib, struct, wave

root = pathlib.Path(__file__).resolve().parents[2]
mod = root / 'ZZ-PZAEC_Mecha'
baseline = sorted((root / '.local-tests').glob('Mecha-before-0.12.0-*/ZZ-PZAEC_Mecha'))[-1]
stem = 'samurai_style_gundam_mecha'

def faces(folder):
    doc = json.loads((folder / (stem + '_rig.json')).read_text())
    raw = (folder / (stem + '_rig.bin')).read_bytes()
    result = collections.Counter()
    for part in doc['parts']:
        if part.get('generatedRepair'):continue
        start = part['offset']
        indices = struct.unpack_from('<' + 'I' * part['indices'], raw, start + part['vertices'] * 48)
        for i in range(0, len(indices), 3):
            # Include winding, material, position, normal and UV, excluding the
            # intentionally rebound sword weights. No rounded comparison.
            result[(part['material'], b''.join(raw[start + indices[i+k]*48:start + indices[i+k]*48+32] for k in range(3)))] += 1
    return doc, result

old, old_faces = faces(baseline / 'Resources')
new, new_faces = faces(mod / 'Resources')
assert old_faces == new_faces, 'face / winding / vertex attribute changed'
assert sum(new_faces.values()) == 207192
assert new['sourceNodes'] == sorted({p['node'] for p in old['parts']})
assert len([p for p in new['parts'] if not p.get('generatedRepair')]) <= len(old['parts'])
assert sum(p['indices']//3 for p in new['parts'] if p.get('generatedRepair')) == new['bladeRepairTriangles'] == 44
assert all(p['role'] in new['renderRoles'] for p in new['parts'])
assert {'ArmL', 'ArmR', 'SwordBlade', 'SwordHilt', 'Head', 'Torso', 'Backpack', 'Shield'} <= set(new['renderRoles'])
assert sum(new['renderRoles'].values()) == new['triangles']
assert new['joints'][:len(old['joints'])] == old['joints']
assert {j['name']:j['parent'] for j in new['joints'][len(old['joints']):]} == {'Sword':'HandR','Shield':'HandL'}
print('PASS exact 207192 faces, winding, normals, UVs, original joints and 17 source nodes;',len(new['parts']),'render groups')

for name in ('combat_robot.glb', stem + '.glb', 'combat_robot_rig.json', 'combat_robot_rig.bin'):
    assert (mod / 'Resources' / name).read_bytes() == (baseline / 'Resources' / name).read_bytes(), name
print('PASS original GLBs and Prototype baked rig unchanged')
for before in (baseline / 'Resources/Audio').glob('*.wav'):
    assert before.read_bytes() == (mod / 'Resources/Audio' / before.name).read_bytes(), before.name
for name in ('palm-laser', 'laser-impact', 'sword-impact'):
    with wave.open(str(mod / 'Resources/Audio' / (name + '.wav'))) as clip:
        assert clip.getnchannels() == 1 and clip.getsampwidth() == 2 and clip.getframerate() == 22050
        assert .15 <= clip.getnframes() / clip.getframerate() <= .25
print('PASS existing sounds unchanged; three new short PCM16 contacts / energy pulse')
for before in (baseline / 'Config').glob('*'):
    if before.is_file():
        assert before.read_bytes() == (mod / 'Config' / before.name).read_bytes(), before.name
for directory in ('ItemIcons', 'UIAtlases'):
    for before in (baseline / directory).rglob('*'):
        if before.is_file():
            assert before.read_bytes() == (mod / directory / before.relative_to(baseline / directory)).read_bytes()
print('PASS gameplay XML, shared ammo, icons, recipes and saved item identifiers unchanged')

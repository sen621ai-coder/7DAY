"""Justice resource integrity; original Samurai geometry checks remain separate."""
import hashlib,json,math,struct
from pathlib import Path
p=Path(__file__).resolve().parents[2]/'ZZ-PZAEC_Mecha/Resources/Justice'
d=json.loads((p/'justice.json').read_text());b=(p/'justice.bin').read_bytes();src=(p/'justice.glb').read_bytes()
assert hashlib.sha256(src).hexdigest()==d['sourceSha256']
g=json.loads(src[20:20+struct.unpack_from('<I',src,12)[0]])
assert len(d['bones'])==len(d['bindposes'])==65 and d['nodes']==g['skins'][0]['joints']
assert d['height']==3.2 and len(d['parts'])==19
for i,bone in enumerate(d['bones']):
 assert -1<=bone['parent']<i and len(bone['trs'])==10
 assert all(math.isfinite(v) for v in bone['trs']+d['bindposes'][i])
assert {x['name'] for x in d['clips']}=={a['name'] for a in g['animations']}|{'Walk','Reverse','TurnLeft','TurnRight'}
for part in d['parts']:
 end=part['offset']+part['vertices']*64
 for v in struct.iter_unpack('<8f4i4f',b[part['offset']:end]):
  assert all(math.isfinite(x) for x in v) and all(0<=x<65 for x in v[8:12])
  assert min(v[12:])>=0 and abs(sum(v[12:])-1)<.0001
 ix=struct.unpack_from('<'+'I'*part['indices'],b,end)
 assert max(ix)<part['vertices'] and len(ix)%6==0
for clip in d['clips']:
 end=clip['offset']+clip['frames']*65*40;assert end<=len(b)
 for v in struct.iter_unpack('<10f',b[clip['offset']:end]):
  assert all(math.isfinite(x) for x in v) and abs(sum(x*x for x in v[3:7])-1)<.001
for i in range(3):assert (p/f'tex_{i}.png').stat().st_size>100
for name in ['bladeRoot','bladeTip','gunMuzzle','gunGrip']:assert len(d[name])==3 and all(math.isfinite(x) for x in d[name])
print('PASS source SHA256, original 65 joints, 4-weight vertices, bind matrices, UVs, indices, 10 finite quaternion clips, 3 textures, weapon sockets')

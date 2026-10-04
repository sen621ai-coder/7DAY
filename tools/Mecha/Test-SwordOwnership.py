"""Regression: source-reviewed fin faces must never belong to the sword again.
Exact identities do not duplicate the spatial selection rule under test.
"""
import argparse,hashlib,json,pathlib,struct
root=pathlib.Path(__file__).resolve().parents[2]
p=argparse.ArgumentParser();p.add_argument('--resources',type=pathlib.Path,default=root/'ZZ-PZAEC_Mecha/Resources');args=p.parse_args()
fixtures=json.loads((root/'tools/Mecha/Sword-Ownership-Landmarks.json').read_text())['landmarks'];wanted={f['faceSha256']:f for f in fixtures};found={}
doc=json.loads((args.resources/'samurai_style_gundam_mecha_rig.json').read_text());raw=(args.resources/'samurai_style_gundam_mecha_rig.bin').read_bytes()
for part in doc['parts']:
 start=part['offset'];ix=struct.unpack_from('<'+'I'*part['indices'],raw,start+48*part['vertices'])
 for i in range(0,len(ix),3):
  payload=b''.join(raw[start+48*k:start+48*k+32] for k in ix[i:i+3]);key=hashlib.sha256(payload).hexdigest()
  if key in wanted:found[key]=part['role']
failures=0
for key,f in wanted.items():
 ok=found.get(key)==f['expectedRole'];failures+=not ok;print(('PASS ' if ok else 'FAIL ')+f['name']+': '+str(found.get(key))+' expected '+f['expectedRole'])
raise SystemExit(bool(failures))

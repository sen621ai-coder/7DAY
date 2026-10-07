"""Measure hand grip and shield bounds from the exported four-weight skin."""
import json,struct
from pathlib import Path
import numpy as np
from scipy.spatial.transform import Rotation
p=Path(__file__).resolve().parents[2]/'ZZ-PZAEC_Mecha/Resources/Justice'
d=json.loads((p/'justice.json').read_text());data=(p/'justice.bin').read_bytes();nodes=d['nodes'];clip=next(c for c in d['clips'] if c['name']=='02-RIFLE')
world=[]
for i,b in enumerate(d['bones']):
 v=struct.unpack_from('<10f',data,clip['offset']+(120*65+i)*40);m=np.eye(4);m[:3,:3]=Rotation.from_quat(v[3:7]).as_matrix()@np.diag(v[7:]);m[:3,3]=v[:3];world.append(world[b['parent']]@m if b['parent']>=0 else m)
d['gunGrip']=(np.linalg.inv(world[nodes.index(64)])@np.r_[world[nodes.index(27)][:3,3],1])[:3].tolist()
(p/'justice.json').write_text(json.dumps(d,separators=(',',':')))
print('Rifle grip measured',d['gunGrip'])

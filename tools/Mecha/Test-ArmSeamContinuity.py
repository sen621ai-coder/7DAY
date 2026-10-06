"""Measure shared arm seam separation under independently recorded poses."""
import argparse,json
from pathlib import Path
import numpy as np
from scipy.spatial import cKDTree

p=argparse.ArgumentParser();p.add_argument('--resources',required=True,type=Path);p.add_argument('--poses',required=True,type=Path);a=p.parse_args()
d=json.loads((a.resources/'samurai_style_gundam_mecha_rig.json').read_text());raw=(a.resources/'samurai_style_gundam_mecha_rig.bin').read_bytes()
dt=np.dtype([('v','<f4',8),('b','<i4',2),('w','<f4',2)])
v=np.concatenate([np.frombuffer(raw,dt,part['vertices'],part['offset']) for part in d['parts'] if not part.get('generatedRepair')])
ids={j['name']:i for i,j in enumerate(d['joints'])};rest=np.array([j['position'] for j in d['joints']]);pairs=[]
for side in ['L','R']:
 for upper,lower in [('Torso','Shoulder'+side),('Shoulder'+side,'Elbow'+side),('Elbow'+side,'Hand'+side)]:
  left=np.flatnonzero(v['b'][:,0]==ids[upper]);right=np.flatnonzero(v['b'][:,0]==ids[lower]);distance,near=cKDTree(v['v'][left,:3]).query(v['v'][right,:3]);same=distance<2e-6
  assert same.sum()>20,(upper,lower,'too few shared seam samples')
  pairs.append((upper,lower,left[near[same]],right[same]))
poses=json.loads(a.poses.read_text());maximum=0.
left=np.flatnonzero(v['b'][:,0]==ids['HandR']);right=np.flatnonzero(v['b'][:,0]==ids['Sword'])
distance,near=cKDTree(v['v'][left,:3]).query(v['v'][right,:3]);same=distance<2e-6
assert same.sum()>20,'missing original hand/grip contact'
pairs.append(('HandR','Sword',left[near[same]],right[same]))
for pose in poses:
 m=np.array(pose['bones']).reshape(-1,3,4)
 for upper,lower,left,right in pairs:
  def skin(ix):
   b=v['b'][ix];local=v['v'][ix,:3][:,None,:]-rest[b]
   return ((np.einsum('nwij,nwj->nwi',m[b,:,:3],local)+m[b,:,3])*v['w'][ix,:,None]).sum(1)
  error=np.linalg.norm(skin(left)-skin(right),axis=1).max();maximum=max(maximum,float(error))
  assert error<.0002,(pose.get('auditPair'),upper,lower,float(error))
print('PASS',len(poses),'recorded poses;',sum(len(x[2]) for x in pairs),'shared seam pairs; maximum opening',maximum,'m')

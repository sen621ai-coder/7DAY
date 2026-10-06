"""Independent source-surface preservation and rigid articulation checks.
Requires NumPy/SciPy only for offline asset QA.
"""
import argparse,json,hashlib
from pathlib import Path
import numpy as np
from scipy.spatial import cKDTree
p=argparse.ArgumentParser();p.add_argument('--resources',type=Path,required=True);a=p.parse_args()
d=json.loads((a.resources/'samurai_style_gundam_mecha_rig.json').read_text());raw=(a.resources/'samurai_style_gundam_mecha_rig.bin').read_bytes();dt=np.dtype([('v','<f4',8),('b','<i4',2),('w','<f4',2)])
def read(part):
 v=np.frombuffer(raw,dt,part['vertices'],part['offset']);ix=np.frombuffer(raw,'<u4',part['indices'],part['offset']+48*part['vertices']).reshape(-1,3);return v,ix
if 'panelRepair' in d:
 import runpy
 runpy.run_path(str(Path(__file__).with_name('Test-PanelIntegrity.py')),run_name='__main__')
 raise SystemExit(0)
source=d['articulationSourceParts'];outer=[p for p in d['parts'] if p.get('generatedRepair')!='recessed-joint-interior-v1'];liners=[p for p in d['parts'] if p.get('generatedRepair')=='recessed-joint-interior-v1']
sf=np.concatenate([read(p)[0][read(p)[1]]['v'] for p in source]);of=np.concatenate([read(p)[0][read(p)[1]]['v'] for p in outer])
def area(f):return np.linalg.norm(np.cross(f[:,1,:3]-f[:,0,:3],f[:,2,:3]-f[:,0,:3]),axis=1).sum(dtype=np.float64)*.5
assert abs(area(sf)-area(of))<1e-4,(area(sf),area(of))
# Original attribute tuples or barycentrically interpolated cut points only.
old=np.unique(sf.reshape(-1,8),axis=0);new=np.unique(of.reshape(-1,8),axis=0)
distance,_=cKDTree(old).query(new);cut=new[distance>1e-6];tree=cKDTree(sf[:,:,:3].mean(1));error=0
for chunk in np.array_split(cut,max(1,len(cut)//128)):
 if not len(chunk):continue
 _,ix=tree.query(chunk[:,:3],k=128);tri=sf[ix].astype(np.float64);q=chunk[:,None,:3];v0=tri[:,:,1,:3]-tri[:,:,0,:3];v1=tri[:,:,2,:3]-tri[:,:,0,:3];v2=q-tri[:,:,0,:3]
 aa=(v0*v0).sum(-1);bb=(v0*v1).sum(-1);cc=(v1*v1).sum(-1);dd=(v2*v0).sum(-1);ee=(v2*v1).sum(-1);den=aa*cc-bb*bb;safe=np.where(abs(den)>1e-24,den,1)
 u=(cc*dd-bb*ee)/safe;v=(aa*ee-bb*dd)/safe;w=np.stack([1-u-v,u,v],-1);pred=(tri*w[:,:,:,None]).sum(2);pred[:,:,3:6]/=np.maximum(np.linalg.norm(pred[:,:,3:6],axis=-1,keepdims=True),1e-12)
 valid=(w.min(-1)>-2e-3)&(abs(den)>1e-24);err=np.linalg.norm(pred[:,:,:3]-chunk[:,None,:3],axis=-1);err[~valid]=np.inf;best=err.argmin(1);row=np.arange(len(chunk))
 # Check UVs against any matching source face (UV seams may be coincident).
 attr=np.linalg.norm(pred[:,:,6:8]-chunk[:,None,6:8],axis=-1);ok=((err<2e-5)&(attr<2e-3)).any(1)
 # Long narrow source triangles can have a distant centroid. For unresolved
 # points use the full source AABB broad phase, then double-precision barycentrics.
 for point in chunk[~ok]:
  mask=((sf[:,:,:3].min(1)-2e-5<=point[:3])&(sf[:,:,:3].max(1)+2e-5>=point[:3])).all(1);t=sf[mask].astype(float);x=t[:,1,:3]-t[:,0,:3];y=t[:,2,:3]-t[:,0,:3];z=point[:3]-t[:,0,:3]
  aa=(x*x).sum(1);bb=(x*y).sum(1);cc=(y*y).sum(1);dd=(z*x).sum(1);ee=(z*y).sum(1);den=aa*cc-bb*bb;safe=np.where(abs(den)>1e-24,den,1);u=(cc*dd-bb*ee)/safe;v=(aa*ee-bb*dd)/safe;w=np.stack([1-u-v,u,v],1);pred=(t*w[:,:,None]).sum(1)
  e=np.linalg.norm(pred[:,:3]-point[:3],axis=1);valid=(abs(den)>1e-24)&(w.min(1)>-2e-3)&(np.linalg.norm(pred[:,6:8]-point[6:8],axis=1)<2e-3);assert valid.any() and e[valid].min()<2e-5,('source surface mismatch',point.tolist());error=max(error,float(e[valid].min()))
 if ok.any():error=max(error,float(err[ok].min(1).max()))
assert error<2e-5,error
print('PASS original exterior area preserved; all new cut vertices stay on original surfaces and UVs; maximum position error',error)
for part in d['parts']:
 v,ix=read(part);assert np.isfinite(v['v']).all();assert np.all(v['w']==[1,0]);assert np.all(v['b'][:,0]==v['b'][:,1]);assert np.all(v['b'][ix,0]==v['b'][ix[:,:1],0]);assert np.allclose(np.linalg.norm(v['v'][:,3:6],axis=1),1,atol=.001)
for part in source:
 if part['role'] in ['Head','Backpack','ChestDoor','ChestL','ChestR','WingL','WingR','SwordBlade'] or part.get('generatedRepair'):
  assert next(x for x in d['parts'] if x['nodeName']==part['nodeName'])==part
assert len(liners)==5 and d['triangles']<250000
print('PASS all exterior/interior faces rigid; original head/cockpit/backpack/wings/blade/knee hubs unchanged; five graphite interior batches')
# Regression landmarks on original upper shield component must follow shoulder,
# and restored grip shell must stay with HandR when Sword rotates independently.
ids={j['name']:i for i,j in enumerate(d['joints'])}
shield=next(x for x in source if x['role']=='Shield');v,ix=read(shield);f=v[ix];c=f['v'][:,:,:3].mean(1);mask=(c[:,1]>2.53)&(c[:,2]>.24);landmarks=f[mask]['v'][:,:,:3].mean(1)
current=np.concatenate([read(x)[0][read(x)[1]] for x in outer]);center=current['v'][:,:,:3].mean(1);dd,near=cKDTree(center).query(landmarks);assert dd.max()<1e-5 and np.all(current['b'][near,0,0]==ids['ShoulderL'])
assert d['articulationRepair']['restoredShoulderFaces']==len(landmarks)==1672
assert d['articulationRepair']['restoredGripFaces']==2747
print('PASS shoulder component follows ShoulderL independently of Shield; source shoulder landmarks checked',len(landmarks))
hilt=next(x for x in source if x['role']=='SwordHilt');v,ix=read(hilt);hf=v[ix];hc=hf['v'][:,:,:3].mean(1);grip=np.array(d['joints'][ids['HandR']]['position']);axis=np.array([.9407,2.1757,1.3423])-grip;axis/=np.linalg.norm(axis);along=(hc-grip)@axis;radial=np.linalg.norm(hc-grip-along[:,None]*axis,axis=1);selected=(np.linalg.norm(hc-grip,axis=1)<.18)&(along>-.12)&(along<.045)&(radial>.055)
dd,near=cKDTree(center).query(hc[selected]);assert dd.max()<1e-5 and np.all(current['b'][near,0,0]==ids['HandR'])
print('PASS restored grip shell remains on HandR independently of Sword; source landmarks checked',int(selected.sum()))
print('NOTE geometry checks do not certify every plate collision; native appearance review remains required')

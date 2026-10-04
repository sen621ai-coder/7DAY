"""Ray coverage of the actual exported blade; 0.12.4 must fail this test."""
import argparse, collections, json
from pathlib import Path
import numpy as np

parser=argparse.ArgumentParser()
parser.add_argument('--resources',type=Path,default=Path(__file__).resolve().parents[2]/'ZZ-PZAEC_Mecha/Resources')
args=parser.parse_args();stem='samurai_style_gundam_mecha_rig'
doc=json.loads((args.resources/(stem+'.json')).read_text(encoding='utf-8-sig'))
raw=(args.resources/(stem+'.bin')).read_bytes()
dtype=np.dtype([('v','<f4',8),('bones','<i4',2),('weights','<f4',2)])
faces=[];repairs=[]
for p in doc['parts']:
    if p['role']!='SwordBlade':continue
    v=np.frombuffer(raw,dtype=dtype,count=p['vertices'],offset=p['offset'])
    ix=np.frombuffer(raw,dtype='<u4',count=p['indices'],offset=p['offset']+48*p['vertices']).reshape(-1,3)
    faces.append(v['v'][ix][:,:,:3])
    if p.get('generatedRepair'):repairs.append(v[ix])
points=np.concatenate(faces).astype(float)
tip=np.array(doc['swordAnchors']['bladeTip']);axis=np.array(doc['swordAnchors']['bladeRoot'])-tip;axis/=np.linalg.norm(axis)
normal=np.array(doc['swordAnchors']['bladeNormal']);normal-=axis*np.dot(axis,normal);normal/=np.linalg.norm(normal)
across=np.cross(normal,axis);local=(points-tip)@np.stack([axis,across,normal],axis=1)
# Shoot through the exported geometry from BOTH broad-face sides. Mere graph
# connectivity or a sword capsule does not detect these visible mid-blade holes.
a,b,c=local[:,0],local[:,1],local[:,2];ab=b-a;ac=c-a
den=ab[:,0]*ac[:,1]-ab[:,1]*ac[:,0];valid=abs(den)>1e-10
missing=[];samples=0
for t in np.linspace(.74,1.66,93):
    for w in np.linspace(.045,.155,12):
        q=np.array([t,w])-a[:,:2]
        u=np.divide(q[:,0]*ac[:,1]-q[:,1]*ac[:,0],den,out=np.zeros_like(den),where=valid)
        v=np.divide(ab[:,0]*q[:,1]-ab[:,1]*q[:,0],den,out=np.zeros_like(den),where=valid)
        hit=valid&(u>=-1e-7)&(v>=-1e-7)&(u+v<=1+1e-7)
        depths=(a[:,2]+u*ab[:,2]+v*ac[:,2])[hit]
        samples+=1
        if len(depths)<2 or np.ptp(depths)<.012:missing.append((round(t,3),round(w,3)))
assert not missing,f'{len(missing)}/{samples} blade samples lack two continuous faces: {missing[:12]}'
print(f'PASS {samples} mid-blade rays have two metal surfaces with nonzero thickness')
assert len(repairs)==1,'one explicitly tagged repair required'
r=repairs[0];p=r['v'][:,:,:3];edges=collections.Counter();directed=collections.Counter()
for tri in p:
    q=[tuple(v) for v in tri]
    for i in range(3):
        edge=(q[i],q[(i+1)%3]);directed[edge]+=1;edges[tuple(sorted(edge))]+=1
assert set(edges.values())=={2},'open/nonmanifold repair edge'
assert all(directed[(b,a)]==n for (a,b),n in directed.items()),'inconsistent winding'
n=np.cross(p[:,1]-p[:,0],p[:,2]-p[:,0]);assert np.linalg.norm(n,axis=1).min()>1e-6
assert np.all(np.einsum('ij,ij->i',n,r['v'][:,0,3:6])>0),'backward lighting normals'
sword=next(i for i,j in enumerate(doc['joints']) if j['name']=='Sword')
assert np.all(r['bones']==sword) and np.all(r['weights']==[1,0])
assert len(r)==doc['bladeRepairTriangles']==44
print('PASS repair is closed, consistently wound, nondegenerate and rigidly owned by Sword')

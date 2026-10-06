"""Independent regression checks for complete-panel articulation.
Connectivity, archived source attributes, joint-domain ownership and blade
identity are separate from the generator's spatial classification rules.
"""
import argparse,hashlib,json
from pathlib import Path
import numpy as np
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import connected_components
from scipy.spatial import cKDTree
p=argparse.ArgumentParser();p.add_argument('--resources',required=True,type=Path);a=p.parse_args();stem='samurai_style_gundam_mecha';d=json.loads((a.resources/(stem+'_rig.json')).read_text());raw=(a.resources/(stem+'_rig.bin')).read_bytes();dt=np.dtype([('v','<f4',8),('b','<i4',2),('w','<f4',2)]);names=[j['name'] for j in d['joints']];ids={n:i for i,n in enumerate(names)};positions=np.array([j['position'] for j in d['joints']])
def read(part):
 v=np.frombuffer(raw,dt,part['vertices'],part['offset']);ix=np.frombuffer(raw,'<u4',part['indices'],part['offset']+48*part['vertices']).reshape(-1,3);assert ix.max()<len(v);return v[ix]
outer=[p for p in d['parts'] if p.get('generatedRepair') not in ['recessed-joint-interior-v1','source-textured-panel-back-v1']];inside=[p for p in d['parts'] if p.get('generatedRepair') in ['recessed-joint-interior-v1','source-textured-panel-back-v1']];f=np.concatenate([read(p) for p in outer]);q=f['v'][:,:,:3];bones=f['b'][:,0,0];allf=np.concatenate([read(p) for p in d['parts']]);assert len(allf)==d['triangles']<250000;assert len(d['parts'])<=38
assert np.isfinite(allf['v']).all() and np.isfinite(allf['w']).all()
assert np.all(allf['w']>=0) and np.allclose(allf['w'].sum(-1),1,atol=1e-6)
assert np.all(allf['b'][:,0:1,0]==allf['b'][:,:,0]);assert np.allclose(np.linalg.norm(allf['v'][:,:,3:6],axis=-1),1,atol=.001)
allowed={frozenset([ids[a],ids[b]]) for side in ['L','R'] for a,b in [('Torso','Shoulder'+side),('Shoulder'+side,'Elbow'+side),('Elbow'+side,'Hand'+side)]}
mixed=allf.reshape(-1);mixed=mixed[mixed['w'][:,1]>0]
assert len(mixed)>0 and len(mixed)<allf.size*.15
assert all(frozenset(pair) in allowed for pair in np.unique(mixed['b'],axis=0))
grip_pair=np.all(np.sort(mixed['b'],axis=1)==sorted([ids['ElbowR'],ids['HandR']]),axis=1)
assert np.all(mixed['w'][~grip_pair,1]<=.50001)
end=max(p['offset']+48*p['vertices']+4*p['indices'] for p in d['panelSourceParts']+d.get('sourceOnlyParts',[]));assert hashlib.sha256(raw[:end]).hexdigest()==d['panelRepair']['sourceBinarySha256']
print('PASS finite geometry, unit normals, source archive hash and bounded arm-only blends; triangles',len(allf),'batches',len(d['parts']))
for side in ['L','R']:
 wing=q[bones==ids['Wing'+side]];points,ix=np.unique(np.round(wing.reshape(-1,3),5),axis=0,return_inverse=True);ix=ix.reshape(-1,3);g=coo_matrix((np.ones(len(ix)*2),(np.r_[ix[:,0],ix[:,1]],np.r_[ix[:,1],ix[:,2]])),shape=(len(points),len(points)));count,_=connected_components(g,directed=False);assert count==1,('detached wing island',side,count)
 tip=np.array([(-1 if side=='L' else 1)*.77156734,.50134009,-.65893555]);assert cKDTree(points).query(tip)[0]<2e-5;assert points[:,1].max()>1.85 and points[:,2].min()<-1.4
 print('PASS wing',side,'one continuous surface from root through middle to original lower tip')
# The previously orbiting 14 cm waist fragment must remain with the torso.
cent=q.mean(1);dist,near=cKDTree(cent).query([.492,1.641,.135]);assert dist<.06 and bones[near]==ids['Torso'],('waist fragment',dist,names[bones[near]])
# Test whole faces, not just their centroids: wrists used to own triangles
# reaching back across the elbow and tearing the forearm when hands rotated.
for side in ['L','R']:
 for joint,parent in [('Elbow','Shoulder'),('Hand','Elbow')]:
  pivot=positions[ids[joint+side]];normal=positions[ids[parent+side]]-pivot;normal/=np.linalg.norm(normal)
  lower=q[bones==ids[joint+side]];assert ((lower-pivot)@normal).max()<3e-5,(side,joint,'face crosses proximal seam')
print('PASS waist fragment no longer orbits elbow; entire arm faces respect proximal joint seams')
# Screenshot-reviewed rear shield crest: this used to follow the shoulder
# while the rest of the same red/white decoration followed the shield.
dist,near=cKDTree(cent).query([-.98962,2.24205,-.27201])
assert dist<.02 and bones[near]==ids['Shield'],('detached shield crest',dist,names[bones[near]])
assert not (bones==ids['ChestDoor']).any(), 'main breastplate must not be a large moving hatch'
for name in ['ChestL','ChestR']:
 panel=q[bones==ids[name]];assert len(panel)>20
 extent=np.ptp(panel.reshape(-1,3),axis=0);assert extent[0]<.20 and extent[1]<.15 and panel[:,:,2].min()>=.57999,('oversize small hatch',name,extent)
 assert np.all(f[bones==ids[name]]['w']==[1,0]), 'small armour must move as a rigid plate'
print('PASS reviewed rear shield crest follows Shield; main breastplate fixed; two compact original armour panels are rigid')
# Independently picked from the intact static shield: upper white plate,
# handle, and front/root. The old height cutoff assigned these to ShoulderL.
shield_points=[[-.88517,2.50817,.08163],[-.91029,2.47340,.00574],
               [-.87026,2.54724,.13586],[-.82955,2.67557,.14893],
               [-.90986,2.54275,-.15516],[-.86100,2.59983,.14029],
               [-.86060,2.44988,.23691],[-.81648,2.35889,.43825]]
distance,near=cKDTree(cent).query(shield_points)
assert distance.max()<.025,('shield landmark lost',distance.tolist())
assert np.all(bones[near]==ids['Shield']),('shield handle/plate split', [names[b] for b in bones[near]])
shield_records=f[bones==ids['Shield']]
assert np.all(shield_records['w']==[1,0]),'shield must stay rigid with its handle'
print('PASS eight intact-source shield landmarks and all shield vertices follow one rigid equipment transform')
# Head and blade appearance are immutable; hilt/forearm ownership can change.
for role in ['Head']:
 sf=np.concatenate([read(p)['v'] for p in d['panelSourceParts'] if p['role']==role]);of=np.concatenate([read(p)['v'] for p in outer if p['role']==role])
 def fingerprint(t):return np.sort(np.ascontiguousarray(t.reshape(-1,24)).view(np.dtype((np.void,96))).reshape(-1))
 assert np.isin(fingerprint(sf),fingerprint(of)).all(),('protected exterior changed',role)
blade_tag='closed-blade-overlap-surface-v1'
assert np.array_equal(fingerprint(np.concatenate([read(p)['v'] for p in d['panelSourceParts'] if p.get('generatedRepair')==blade_tag])),fingerprint(np.concatenate([read(p)['v'] for p in outer if p.get('generatedRepair')==blade_tag])))
print('PASS original head and verified blade core tuples unchanged; proximal mislabelled armour is checked by full exterior provenance below')
# Every exterior attribute must come from the archived surface (or an
# interpolated seam point). Undo the documented wing reflection first.
sf=np.concatenate([read(p)['v'] for p in d['panelSourceParts']]);of=f['v'].copy();mirror=bones==ids['WingR'];of[mirror,:,0]*=-1;of[mirror,:,3]*=-1
old=np.unique(sf.reshape(-1,8),axis=0);new=np.unique(of.reshape(-1,8),axis=0);distance,_=cKDTree(old).query(new);cut=new[distance>1e-6];lo=sf[:,:,:3].min(1);hi=sf[:,:,:3].max(1);error=0.
for point in cut:
 candidate=((lo-2e-5<=point[:3])&(hi+2e-5>=point[:3])).all(1);tri=sf[candidate].astype(float);xx=tri[:,1,:3]-tri[:,0,:3];yy=tri[:,2,:3]-tri[:,0,:3];zz=point[:3]-tri[:,0,:3]
 aa=(xx*xx).sum(1);bb=(xx*yy).sum(1);cc=(yy*yy).sum(1);dd=(zz*xx).sum(1);ee=(zz*yy).sum(1);den=aa*cc-bb*bb;safe=np.where(abs(den)>1e-24,den,1);u=(cc*dd-bb*ee)/safe;v=(aa*ee-bb*dd)/safe;w=np.stack([1-u-v,u,v],1);pred=(tri*w[:,:,None]).sum(1)
 e=np.linalg.norm(pred[:,:3]-point[:3],axis=1);valid=(abs(den)>1e-24)&(w.min(1)>-2e-3)&(np.linalg.norm(pred[:,6:8]-point[6:8],axis=1)<2e-3)
 assert valid.any() and e[valid].min()<2e-5,('exterior provenance mismatch',point.tolist());error=max(error,float(e[valid].min()))
print('PASS all exterior seam points lie on archived source surfaces with matching UVs; maximum error',error,'new points',len(cut))

# All generated surfaces are explicitly marked as interiors, so exterior
# preservation tests cannot accidentally count hidden backing as original skin.
assert inside and all(p['generatedRepair'] in ['recessed-joint-interior-v1','source-textured-panel-back-v1'] for p in inside)
for role in ['ChestL','ChestR','WingL','WingR','ArmL','ArmR','Shield']:
 assert any(p['role']==role for p in inside),('missing interior',role)
print('PASS chest, wings and arms have separate recessed backing batches; no generic sphere batch')
# Native frontal picks at the two black spikes above the right small hatch.
# Test the visible ray intersection, not just a vertex's distance to a bone.
tri=allf['v'][:,:,:3].astype(float)
graphite=np.concatenate([np.full(p['indices']//3,p.get('generatedRepair')=='recessed-joint-interior-v1') for p in d['parts']])
origin=np.array([0,2.28,1.9]);scale=450/np.tan(np.deg2rad(18))
for px,py in [(270,390),(274,386)]:
 ray=np.array([-(px-550)/scale,(450-py)/scale,-1.]);ray/=np.linalg.norm(ray)
 e1=tri[:,1]-tri[:,0];e2=tri[:,2]-tri[:,0];h=np.cross(ray,e2);det=np.einsum('ij,ij->i',e1,h);safe=np.where(abs(det)>1e-12,det,1.)
 delta=origin-tri[:,0];u=np.einsum('ij,ij->i',delta,h)/safe;qv=np.cross(delta,e1);vv=qv@ray/safe;t=np.einsum('ij,ij->i',e2,qv)/safe
 valid=(det>1e-12)&(u>=0)&(vv>=0)&(u+vv<=1)&(t>0);assert valid.any()
 t[~valid]=np.inf;nearest=t.argmin();assert not graphite[nearest],('exposed fixed hatch graphite rim',px,py)
print('PASS closed-chest native black-spike rays hit original skin or source-textured rim, not protruding graphite')
print('NOTE these geometric checks do not replace native all-angle visual acceptance')

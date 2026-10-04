"""Rigid semantic ownership for the fused Complete sculpture; fitted metres.

Object_* and material IDs are spatial chunks, not separate equipment meshes.
Keep every source face tuple. Add a tagged closed surface at the blade overlap.
"""
from pathlib import Path
import hashlib,json,itertools,numpy as np
root=Path(__file__).resolve().parents[2];res=root/'ZZ-PZAEC_Mecha/Resources';stem='samurai_style_gundam_mecha'
doc=json.loads((res/(stem+'_rig.json')).read_text());raw=(res/(stem+'_rig.bin')).read_bytes()
for name,parent,pos in [('Sword','HandR',[.78,1.81,.76]),('Shield','HandL',[-.85,1.72,.36])]:
    if not any(j['name']==name for j in doc['joints']):doc['joints'].append(dict(name=name,parent=parent,position=pos))
ids={j['name']:i for i,j in enumerate(doc['joints'])};names={i:n for n,i in ids.items()}
dtype=np.dtype([('vertex','<f4',8),('bones','<i4',2),('weights','<f4',2)])
allfaces=[];materials=[];nodes=set(doc.get('sourceNodes',[]))
for part in doc['parts']:
    if part.get('generatedRepair'):continue
    nodes.add(part['node']);v=np.frombuffer(raw,dtype=dtype,count=part['vertices'],offset=part['offset'])
    ix=np.frombuffer(raw,dtype='<u4',count=part['indices'],offset=part['offset']+48*part['vertices']).reshape(-1,3)
    allfaces.append(v[ix].copy());materials.extend([part['material']]*len(ix))
face=np.concatenate(allfaces);materials=np.array(materials);points=face['vertex'][:,:,:3];center=points.mean(1);x,y,z=center.T
before=len(face)
def geometry_hash(records):
    rows=np.ascontiguousarray(records['vertex'].reshape(-1,24)).view(np.dtype((np.void,96))).reshape(-1)
    return hashlib.sha256(np.sort(rows).tobytes()).hexdigest()
identity=geometry_hash(face)
dominant=np.stack([((face['bones']==k)*face['weights']).sum(axis=(1,2)) for k in range(len(doc['joints']))],axis=1)
bone=np.argmax(dominant,axis=1)
# The broad cylinder previously swallowed side spurs and body armour. The
# PCA plane follows the actual thin blade while retaining its ornate outline.
grip=np.array([.78,1.81,.76]);a=np.array([.23,.50,-1.10]);b=np.array([.92,2.18,1.34]);delta=b-a
t=np.clip((center-a)@delta/(delta@delta),0,1);distance=np.linalg.norm(center-(a+t[:,None]*delta),axis=1)
capsule=(distance<.225)&(x>.16)&(y<2.23)
blade_normal=np.array([.96168816,-.0582994,-.267875]);blade_center=np.array([.3950305,.820023,-.5686091])
plane=(center-blade_center)@blade_normal;sword=capsule&(abs(plane)<np.where(y>1.55,.16,.14))
# The lower rear fin crosses the sword's broad capsule in the source sculpture.
# Its red/white plate is ABOVE the blade spine, not an ornament on the blade.
# Calibrate this overlap in a blade-local frame from the original GLB side views;
# normal distance alone cannot distinguish these two nearly touching surfaces.
# Limit the correction to the crossed middle span, preserving tip and collar.
blade_axis=np.array([.73,1.57,.30])-a;blade_axis/=np.linalg.norm(blade_axis)
cross_normal=blade_normal-blade_axis*np.dot(blade_normal,blade_axis);cross_normal/=np.linalg.norm(cross_normal)
blade_across=np.cross(cross_normal,blade_axis)
blade_along=(center-a)@blade_axis;blade_width=(center-a)@blade_across
lower_fin_overlap=sword&(blade_along>.72)&(blade_along<1.80)&(blade_width<.075)
blade_depth=(center-a)@cross_normal
# Residual gold fin tips point out of the broad face. Width-only ownership
# missed these on both sides; retain only the thin blade envelope in this span.
side_fin_overlap=sword&(blade_along>.90)&(blade_along<1.68)&((blade_depth>.005)|(blade_depth<-.10))
lower_fin_overlap|=side_fin_overlap
sword&=~lower_fin_overlap
# Only near-coincident reflected body surfaces are removed. The source is
# fused, so a wider mirror test would also catch a distinct but nearby blade.
normal=np.cross(points[:,1]-points[:,0],points[:,2]-points[:,0]);normal/=np.maximum(np.linalg.norm(normal,axis=1)[:,None],1e-12)
cell=.008;left=np.flatnonzero((x<-.15)&(z<.25));bucket={}
for index,key in zip(left,np.floor(center[left]/cell).astype(int)):bucket.setdefault(tuple(key),[]).append(int(index))
mirrored=np.zeros(before,bool);mirror_owner=np.zeros(before,dtype=int);neighbours=list(itertools.product((-1,0,1),repeat=3))
for index in np.flatnonzero(capsule&(z<.25)):
    q=center[index]*np.array([-1,1,1]);key=np.floor(q/cell).astype(int);candidates=[]
    for shift in neighbours:candidates.extend(bucket.get(tuple(key+shift),()))
    if not candidates:continue
    candidates=np.array(candidates);dist=np.linalg.norm(center[candidates]-q,axis=1);near=int(candidates[dist.argmin()]);owner=names[int(bone[near])]
    if dist.min()<cell and owner in ('Torso','HipL','KneeL','AnkleL') and np.dot(normal[index]*[-1,1,1],normal[near])>.7:
        mirrored[index]=True;mirror_owner[index]=ids[owner[:-1]+'R' if owner.endswith('L') else owner]
sword&=~mirrored
# Reset former equipment outside the calibrated boundary so re-running this
# partition never retains stale Sword / Wing labels in rejected surfaces.
former=np.isin(bone,[ids['Sword'],ids['WingL'],ids['WingR']])&~sword
bone[former]=ids['Torso']
for side,sign in [('L',-1),('R',1)]:
    half=former&(x*sign>0)
    bone[half&(y<1.354)]=ids['Hip'+side];bone[half&(y<.802)]=ids['Knee'+side];bone[half&(y<.25)]=ids['Ankle'+side]
bone[former&(x>.58)&(y>1.63)&(z>.35)]=ids['HandR'];bone[mirrored]=mirror_owner[mirrored]
# Taper follows the complete fin neck toward its waist root. The previous
# constant Z threshold left sections of a plate on animated leg bones.
folded=(abs(x)>.08)&(y>.35)&(y<2.08)&((z<-.48)|((abs(x)>.30)&(y>1.55)&(z<-.24)))
neck=(abs(x)>.30)&(y>.35)&(y<2.08)&(z<(.55*(y-1.8)-.18))
wing=(folded|neck|lower_fin_overlap)&~sword
shield=(x<-.76)&(y>.85)&(y<2.85)&(z>-.20)&(z<.85);wing&=~shield
head_top=(abs(x)<.52)&(y>2.63)&~sword&~wing&~shield&np.isin(bone,[ids['Torso'],ids['Backpack'],ids['Head']])
bone[head_top]=ids['Head']
back=(z<.10)&(y>1.45)&(y<2.63)&~sword&~wing&~shield&np.isin(bone,[ids['Torso'],ids['Backpack'],ids['WingL'],ids['WingR']])
bone[back]=ids['Backpack'];bone[wing&(x<0)]=ids['WingL'];bone[wing&(x>0)]=ids['WingR'];bone[shield]=ids['Shield'];bone[sword]=ids['Sword']
# Rigid complete faces, with duplicated vertices at mechanical seams, avoid
# interpolating across thigh/wing, back/arm and finger/blade ownership.
groups={};audit={};parts=[];blob=bytearray()
for material in np.unique(materials):
    for k in np.unique(bone[materials==material]):
        name=names[int(k)];role='Arm'+name[-1] if name.startswith(('Shoulder','Elbow','Hand')) else 'Leg'+name[-1] if name.startswith(('Hip','Knee','Ankle','Foot')) else name
        selected=(materials==material)&(bone==k);records=face[selected].copy();records['bones']=k;records['weights']=[1,0]
        if name=='Sword':
            along=(records['vertex'][:,:,:3].mean(1)-grip)@((a-grip)/np.linalg.norm(a-grip))
            for label,mask in [('SwordBlade',along>.12),('SwordHilt',along<=.12)]:
                if mask.any():groups.setdefault((int(material),label),[]).append(records[mask].reshape(-1))
        else:groups.setdefault((int(material),role),[]).append(records.reshape(-1))
for (material,role),chunks in sorted(groups.items()):
    flat=np.concatenate(chunks);unique,newix=np.unique(flat,return_inverse=True);offset=len(blob);blob.extend(unique.tobytes());blob.extend(newix.astype('<u4').tobytes())
    parts.append(dict(node=min(nodes),nodeName='Complete_'+role,primitive=0,joint='Torso',role=role,material=material,offset=offset,vertices=len(unique),indices=len(newix)));audit[role]=len(newix)//3
assert sum(audit.values())==before==207192
after=[]
for part in parts:
    v=np.frombuffer(blob,dtype=dtype,count=part['vertices'],offset=part['offset']);ix=np.frombuffer(blob,dtype='<u4',count=part['indices'],offset=part['offset']+48*part['vertices']).reshape(-1,3);after.append(v[ix])
assert geometry_hash(np.concatenate(after))==identity,'Geometry/UV/normal face tuple changed'
del v,ix
from CompleteBladeSurface import build
repair=build(dtype,ids['Sword']).reshape(-1)
unique,newix=np.unique(repair,return_inverse=True);offset=len(blob)
blob.extend(unique.tobytes());blob.extend(newix.astype('<u4').tobytes())
parts.append(dict(node=min(nodes),nodeName='Complete_SwordSurfaceRestore',primitive=0,
    joint='Torso',role='SwordBlade',material=0,offset=offset,vertices=len(unique),
    indices=len(newix),generatedRepair='closed-blade-overlap-surface-v1'))
repair_faces=len(newix)//3;audit['SwordBlade']+=repair_faces
from CompleteKneeHubs import build as build_knee
knee_faces=0
for side in ('L','R'):
    k=ids['Knee'+side];repair=build_knee(dtype,k,doc['joints'][k]['position']).reshape(-1)
    unique,newix=np.unique(repair,return_inverse=True);offset=len(blob)
    blob.extend(unique.tobytes());blob.extend(newix.astype('<u4').tobytes())
    parts.append(dict(node=min(nodes),nodeName='Complete_KneeHub'+side,primitive=0,
        joint='Torso',role='Leg'+side,material=0,offset=offset,vertices=len(unique),
        indices=len(newix),generatedRepair='closed-knee-hinge-v1'))
    count=len(newix)//3;knee_faces+=count;audit['Leg'+side]+=count
doc.update(parts=parts,sourceNodes=sorted(nodes),renderRoles=audit,geometryTupleSha256=identity,
    triangles=before+repair_faces+knee_faces,sourceRigTriangles=before,bladeRepairTriangles=repair_faces,kneeRepairTriangles=knee_faces,
    equipmentBinding='rigid complete faces: shield Shield, sword Sword, rear fins WingL/WingR, back Backpack, mechanical limbs and chest leaves',
    wingBinding='complete rigid rear fins and tapered necks; WingL/WingR under Backpack',
    wingVertices=[sum(p['vertices'] for p in parts if p['role']==name) for name in ['WingL','WingR']],
    swordAnchors=dict(grip=grip.tolist(),bladeRoot=[.73,1.57,.30],bladeTip=a.tolist(),hiltTip=[.9407,2.1757,1.3423],bladeNormal=blade_normal.tolist()),
    roleBinding='rigid complete faces; thin sword plane and conservative mirrored body rejection; tapered rear fins; rigid Backpack and Shield',
    bindingAudit=dict(mirroredBodyFaces=int(mirrored.sum()),swordCapsuleRejectedFaces=int((capsule&~sword).sum()),lowerFinOverlapFaces=int(lower_fin_overlap.sum()),sideFinOverlapFaces=int(side_fin_overlap.sum()),rigidArmour=True))
(res/(stem+'_rig.json')).write_text(json.dumps(doc,indent=2),encoding='utf-8');(res/(stem+'_rig.bin')).write_bytes(blob)
print('PASS rigid semantic face partition:',audit,'source triangles unchanged',before,'closed blade repair',repair_faces,'source nodes',len(nodes),'audit',doc['bindingAudit'])

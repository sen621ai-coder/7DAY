"""Split baked Complete triangles into semantic render roles without simplification.
Every original vertex tuple/triangle is retained. Sword is a rigid child of HandR.
"""
from pathlib import Path
import json,numpy as np
root=Path(__file__).resolve().parents[2];res=root/'ZZ-PZAEC_Mecha/Resources';stem='samurai_style_gundam_mecha'
doc=json.loads((res/(stem+'_rig.json')).read_text());raw=(res/(stem+'_rig.bin')).read_bytes()
if not any(j['name']=='Sword' for j in doc['joints']):doc['joints'].append(dict(name='Sword',parent='HandR',position=[.78,1.81,.76]))
ids={j['name']:i for i,j in enumerate(doc['joints'])};names={i:n for n,i in ids.items()}
dtype=np.dtype([('vertex','<f4',8),('bones','<i4',2),('weights','<f4',2)])
groups={};nodes=set(doc.get('sourceNodes',[]));audit={};before=0
for part in doc['parts']:
    nodes.add(part['node']);v=np.frombuffer(raw,dtype=dtype,count=part['vertices'],offset=part['offset']).copy();ix=np.frombuffer(raw,dtype='<u4',count=part['indices'],offset=part['offset']+48*part['vertices']).reshape(-1,3);before+=len(ix)
    # Group whole faces; do not hide half a triangle at joint seams.
    face=v[ix];dominant=[]
    for k in range(len(doc['joints'])):dominant.append(((face['bones']==k)*face['weights']).sum(axis=(1,2)))
    bone=np.argmax(np.stack(dominant,axis=1),axis=1)
    rigid=(face['bones'][:,:,0]==ids['HandR']).all(1)&(face['weights'][:,:,0]>.999).all(1)
    center=face['vertex'][:,:,:3].mean(1);a=np.array([.23,.50,-1.10]);b=np.array([.92,2.18,1.34]);d=b-a;t=np.clip((center-a)@d/(d@d),0,1);dist=np.linalg.norm(center-(a+t[:,None]*d),axis=1)
    sword=(rigid&(dist<.225)&(center[:,0]>.16)&(center[:,1]<2.23))|(bone==ids['Sword'])
    bone[sword]=ids['Sword']
    for k in np.unique(bone):
        name=names[int(k)];role='Arm'+name[-1] if name.startswith(('Shoulder','Elbow','Hand')) else 'Leg'+name[-1] if name.startswith(('Hip','Knee','Ankle','Foot')) else name
        chosen=ix[bone==k];records=v[chosen.reshape(-1)].copy()
        if name=='Sword':
            records['bones']=ids['Sword'];records['weights']=[1,0]
            # Hilt and blade are distinct render groups sharing one rigid root.
            pts=records['vertex'].reshape(-1,3,8)[:,:,:3].mean(1);along=(pts-np.array([.78,1.81,.76]))@((a-np.array([.78,1.81,.76]))/np.linalg.norm(a-np.array([.78,1.81,.76])))
            for label,mask in [('SwordBlade',along>.12),('SwordHilt',along<=.12)]:
                if mask.any():groups.setdefault((part['material'],label),[]).append(records.reshape(-1,3)[mask].reshape(-1))
        else:groups.setdefault((part['material'],role),[]).append(records)
parts=[];blob=bytearray()
for (material,role),chunks in sorted(groups.items()):
    flat=np.concatenate(chunks);unique,newix=np.unique(flat,return_inverse=True);offset=len(blob);blob.extend(unique.tobytes());blob.extend(newix.astype('<u4').tobytes());parts.append(dict(node=min(nodes),nodeName='Complete_'+role,primitive=0,joint='Torso',role=role,material=material,offset=offset,vertices=len(unique),indices=len(newix)));audit[role]=len(newix)//3
assert sum(audit.values())==before==doc['triangles']
doc.update(parts=parts,sourceNodes=sorted(nodes),renderRoles=audit,swordAnchors=dict(grip=[.78,1.81,.76],bladeRoot=[.73,1.57,.30],bladeTip=[.23,.50,-1.10]),roleBinding='whole faces by explicit calibrated joints; rigid sword child of HandR; material/role batched')
(res/(stem+'_rig.json')).write_text(json.dumps(doc,indent=2),encoding='utf-8');(res/(stem+'_rig.bin')).write_bytes(blob)
print('PASS semantic face partition:',audit,'triangles unchanged',before,'source nodes',len(nodes))

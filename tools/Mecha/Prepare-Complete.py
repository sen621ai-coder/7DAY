"""Bake Samurai Style Gundam into a textured, weighted runtime rig.
Requires numpy, Pillow, pymeshlab (asset-build tools only; no game dependency).
Source is preserved. MeshLab texture-aware reduction keeps source chunk borders.
"""
import hashlib, io, json, pathlib, struct, sys
import numpy as np
from PIL import Image
ROOT=pathlib.Path(__file__).resolve().parents[2]

def wing_groups(q):
    """Folded rear fins, in fitted metres. Exclude the physical sword capsule."""
    x,y,z=q.T
    a=np.array([.23,.50,-1.10]);b=np.array([.92,2.18,1.34]);ab=b-a
    t=np.clip((q-a)@ab/(ab@ab),0,1);sword=np.linalg.norm(q-(a+t[:,None]*ab),axis=1)<.22
    wing=(np.abs(x)>.08)&(y>.35)&(y<2.08)&((z<-.48)|((np.abs(x)>.30)&(y>1.55)&(z<-.24)))&~sword
    return np.where(wing,np.where(x<0,7,8),0)

WING_JOINTS=[dict(name='WingL',parent='Backpack',position=[-.34,1.88,-.30]),dict(name='WingR',parent='Backpack',position=[.34,1.88,-.30])]
if '--rebind-wings' in sys.argv:
    # Existing face records have already been reduced and texture-calibrated.
    # Copy attributes unchanged; duplicate only vertices whose rigid face binding differs.
    res=ROOT/'ZZ-PZAEC_Mecha/Resources';stem='samurai_style_gundam_mecha'
    doc=json.loads((res/(stem+'_rig.json')).read_text());old=(res/(stem+'_rig.bin')).read_bytes()
    for j in WING_JOINTS:
        if not any(b['name']==j['name'] for b in doc['joints']):doc['joints'].append(j)
    ids={j['name']:i for i,j in enumerate(doc['joints'])};blob=bytearray();counts=[0,0]
    dtype=np.dtype([('vertex','<f4',8),('bones','<i4',2),('weights','<f4',2)])
    for part in doc['parts']:
        rec=np.frombuffer(old,dtype=dtype,count=part['vertices'],offset=part['offset'])
        ix=np.frombuffer(old,dtype='<u4',count=part['indices'],offset=part['offset']+48*part['vertices']).reshape(-1,3)
        group=wing_groups(rec['vertex'][ix,:3].mean(axis=1))
        # Tuple (old vertex, wing face group) keeps surfaces rigid and UV seams intact.
        pairs=np.column_stack((ix.reshape(-1),np.repeat(group,3)))
        unique,newix=np.unique(pairs,axis=0,return_inverse=True);new=rec[unique[:,0]].copy()
        for group_id,joint in [(7,'WingL'),(8,'WingR')]:
            mask=unique[:,1]==group_id;new['bones'][mask]=ids[joint];new['weights'][mask]=[1,0];counts[group_id-7]+=int(mask.sum())
        part['offset']=len(blob);part['vertices']=len(new);blob.extend(new.tobytes());blob.extend(newix.astype('<u4').tobytes())
    assert min(counts)>500,counts
    doc['wingBinding']='rigid rear fin faces; WingL/WingR under Backpack; fitted-metre roots'
    doc['wingVertices']=counts
    (res/(stem+'_rig.json')).write_text(json.dumps(doc,indent=2),encoding='utf-8');(res/(stem+'_rig.bin')).write_bytes(blob)
    print('WINGS',counts,'vertices; topology, textures, original GLB and other bindings preserved')
    # The authoritative semantic pass also restores complete plate ownership;
    # a wing-only rebind must not leave old hand / leg blend weights behind.
    import runpy
    runpy.run_path(str(ROOT/'tools/Mecha/Prepare-RenderRoles.py'));sys.exit(0)
sys.path.insert(0,str(ROOT/'.local-tests/mecha-asset-tools'))
import pymeshlab
RES=ROOT/'ZZ-PZAEC_Mecha/Resources'
stem='samurai_style_gundam_mecha'
source=RES/(stem+'.glb')
raw=source.read_bytes(); length=struct.unpack_from('<I',raw,12)[0]
g=json.loads(raw[20:20+length]); base=length+28
world={}
def walk(i,parent):
    n=g['nodes'][i]
    assert not any(k in n for k in ['rotation','translation','scale']), 'Unexpected TRS; author a new import map'
    world[i]=parent@np.array(n.get('matrix',np.eye(4).flatten(order='F')),dtype=float).reshape(4,4,order='F')
    for child in n.get('children',[]):walk(child,world[i])
for i in g['scenes'][g.get('scene',0)]['nodes']:walk(i,np.eye(4))
def accessor(index):
    a=g['accessors'][index];v=g['bufferViews'][a['bufferView']]
    n={'SCALAR':1,'VEC2':2,'VEC3':3}[a['type']];dtype=np.dtype({5126:'<f4',5123:'<u2',5125:'<u4'}[a['componentType']])
    return np.ndarray((a['count'],n),dtype,raw,base+v.get('byteOffset',0)+a.get('byteOffset',0),strides=(v.get('byteStride',n*dtype.itemsize),dtype.itemsize)).copy()
sources=[]
for i,n in enumerate(g['nodes']):
    if 'mesh' not in n:continue
    for primitive in g['meshes'][n['mesh']]['primitives']:
        v=accessor(primitive['attributes']['POSITION']).astype(float);v=v@world[i][:3,:3].T+world[i][:3,3];v[:,0]*=-1
        f=accessor(primitive['indices']).reshape(-1,3).astype(np.int32)[:,[0,2,1]].copy()
        uv=accessor(primitive['attributes']['TEXCOORD_0']).astype(float)
        assert np.array_equal(uv,accessor(primitive['attributes']['TEXCOORD_1']))
        sources.append((i,n['name'],v,f,uv))
floor=min(v[:,1].min() for _,_,v,_,_ in sources);top=max(v[:,1].max() for _,_,v,_,_ in sources);scale=3.2/(top-floor)
def fit(p):return [float(p[0]*scale),float((p[1]-floor)*scale+.05),float(p[2]*scale)]
# Authored normalized-source landmarks. These are independent of Youth Edition.
joints=[dict(name='Torso',parent='',position=fit([0,-.06,.08])),dict(name='Head',parent='Torso',position=fit([0,.31,.12])),dict(name='Backpack',parent='Torso',position=fit([0,.2,-.13]))]
for side,sign in [('L',-1),('R',1)]:
    for name,parent,p in [('Hip','Torso',[.1,-.065,.07]),('Knee','Hip',[.16,-.27,.11]),('Ankle','Knee',[.215,-.455,.12]),('Foot','Ankle',[.215,floor,.16]),('Shoulder','Torso',[.20,.20,.09]),('Elbow','Shoulder',[.23,.055,.13]),('Hand','Elbow',[.245,-.11,.17])]:
        p[0]*=sign;joints.append(dict(name=name+side,parent=parent if parent=='Torso' else parent+side,position=fit(p)))
# Calibrated in fitted metres against front/side orthographic source views.
landmarks={'Head':[0,2.61,.38],'ShoulderL':[-.68,2.40,.26],'ElbowL':[-.79,2.05,.35],'HandL':[-.85,1.72,.36],
           'ShoulderR':[.65,2.40,.26],'ElbowR':[.77,2.10,.49],'HandR':[.78,1.81,.76]}
for j in joints:
    if j['name'] in landmarks:j['position']=landmarks[j['name']]
# Original front armor becomes three rigid hatch leaves; closed bind pose is unchanged.
for name,pos in [('ChestL',[-.27,2.22,.57]),('ChestR',[.27,2.22,.57]),('ChestDoor',[0,1.96,.58])]:
    joints.append(dict(name=name,parent='Torso',position=pos))
joints.extend(WING_JOINTS)
jointid={j['name']:i for i,j in enumerate(joints)}

def equipment(points):
    # Authored equipment volumes in fitted body coordinates, not source chunk IDs.
    q=points.copy();q[:,1]=(q[:,1]-floor)*scale+.05;q[:,[0,2]]*=scale
    x,y,z=q.T;group=np.zeros(len(q),dtype=np.int32)
    # Shield is the thin slab outside the left forearm, including its lower tip.
    group[(x<-.76)&(y>.85)&(y<2.85)&(z>-.20)&(z<.85)]=1
    # Sword occupies a diagonal capsule outside the right thigh, plus the hilt.
    a=np.array([.23,.50,-1.10]);b=np.array([.92,2.18,1.34]);ab=b-a
    t=np.clip((q-a)@ab/(ab@ab),0,1);dist=np.linalg.norm(q-(a+t[:,None]*ab),axis=1)
    group[(dist<.22)&(x>.16)&(y<2.23)]=2
    # Head face/helmet, excluding the backpack horns behind the head.
    group[(np.abs(x)<.43)&(y>2.63)&(z>.15)]=3
    chest=(np.abs(x)<.46)&(y>1.94)&(y<2.49)&(z>.40)
    group[chest&(x<-.13)]=4;group[chest&(x>.13)]=5;group[chest&(np.abs(x)<=.13)]=6
    wings=wing_groups(q);group[wings>0]=wings[wings>0]
    return group
def weights(v):
    x,y,z=v.T;side=np.where(x<0,'L','R');bones=np.zeros((len(v),2),dtype='<i4');w=np.zeros((len(v),2),dtype='<f4');w[:,0]=1
    def single(mask,name):bones[mask,:]=jointid[name]
    def blend(mask,a,b,t):
        bones[mask,0]=jointid[a];bones[mask,1]=jointid[b];w[mask,1]=np.clip(t[mask],0,1);w[mask,0]=1-w[mask,1]
    single(np.ones(len(v),dtype=bool),'Torso')
    blend((np.abs(x)<.115)&(y>.24),'Torso','Head',(y-.24)/.045)
    for suffix,sgn in [('L',-1),('R',1)]:
        half=x*sgn>0
        leg=half&(y<-.06)
        blend(leg,'Torso','Hip'+suffix,(-y-.06)/.065)
        blend(leg&(y<-.22),'Hip'+suffix,'Knee'+suffix,(-y-.22)/.09)
        blend(leg&(y<-.40),'Knee'+suffix,'Ankle'+suffix,(-y-.40)/.075)
        arm=half&(np.abs(x)>.195)&(y>.035)
        blend(arm,'Torso','Shoulder'+suffix,(np.abs(x)-.175)/.055)
        blend(arm&(y<.12),'Shoulder'+suffix,'Elbow'+suffix,(.12-y)/.10)
        blend(arm&(y<-.045),'Elbow'+suffix,'Hand'+suffix,(-y-.045)/.085)
    back=z<-.16;single(back,'Backpack');w[back,0]=1;w[back,1]=0
    # Rebind forearms using calibrated fitted-metre elbows and grips.
    q=v.copy();q[:,1]=(q[:,1]-floor)*scale+.05;q[:,[0,2]]*=scale
    for suffix,sgn in [('L',-1),('R',1)]:
        mask=(q[:,0]*sgn>.58)&(q[:,1]>1.73)&(q[:,1]<2.55)&(q[:,2]>.1)
        blend(mask,'Shoulder'+suffix,'Elbow'+suffix,(2.25-q[:,1])/.20)
        blend(mask&(q[:,1]<2.0),'Elbow'+suffix,'Hand'+suffix,(2.0-q[:,1])/.17)
    assert np.allclose(w.sum(1),1) and np.isfinite(w).all()
    return bones,w
parts=[];blob=bytearray();original=0
dummy=ROOT/'.local-tests/mecha-texture-placeholder.png';Image.new('RGB',(4,4),'white').save(dummy)
for node,name,v,f,uv in sources:
    original+=len(f)
    ms=pymeshlab.MeshSet();ms.add_mesh(pymeshlab.Mesh(vertex_matrix=v,face_matrix=f,v_tex_coords_matrix=uv))
    ms.compute_texcoord_transfer_vertex_to_wedge()
    ms.set_texture_per_mesh(textname=str(dummy))
    ms.meshing_decimation_quadric_edge_collapse_with_texture(targetfacenum=max(2000,len(f)//10),preserveboundary=True,preservenormal=True,qualitythr=.3)
    m=ms.current_mesh();points=m.vertex_matrix();faces=m.face_matrix();norm=m.vertex_normal_matrix();tex=m.wedge_tex_coord_matrix()
    # MeshLab can cancel opposite incident normals at a few thin-surface vertices.
    # Recover from an actual nondegenerate incident face, never a made-up up vector.
    bad=np.linalg.norm(norm,axis=1)<1e-10
    if bad.any():
        face_norm=np.cross(points[faces[:,1]]-points[faces[:,0]],points[faces[:,2]]-points[faces[:,0]])
        valid=np.linalg.norm(face_norm,axis=1)>1e-14
        for corners,n in zip(faces[valid],face_norm[valid]):
            for vertex in corners:
                if bad[vertex]:norm[vertex]=n;bad[vertex]=False
        assert not bad[faces.reshape(-1)].any(), 'Referenced vertex has no valid surface normal'
    # Deduplicate complete attribute tuples; retain UV seams and normal continuity.
    groups=equipment(points[faces].mean(axis=1))
    packed=np.column_stack((points[faces.reshape(-1)],norm[faces.reshape(-1)],tex,np.repeat(groups,3))).astype('<f4')
    unique,indices=np.unique(packed,axis=0,return_inverse=True);verts=unique[:,:8].copy()
    bones,w=weights(verts[:,:3])
    for group,joint in [(1,'HandL'),(2,'HandR'),(3,'Head'),(4,'ChestL'),(5,'ChestR'),(6,'ChestDoor'),(7,'WingL'),(8,'WingR')]:
        mask=unique[:,8]==group;bones[mask,:]=jointid[joint];w[mask,0]=1;w[mask,1]=0
    verts[:,1]=(verts[:,1]-floor)*scale+.05;verts[:,[0,2]]*=scale
    # glTF image origin is top-left; Unity LoadImage UV origin is bottom-left.
    verts[:,7]=1-verts[:,7]
    lens=np.linalg.norm(verts[:,3:6],axis=1);verts[:,3:6]/=np.maximum(lens[:,None],1e-12)
    record=np.empty(len(verts),dtype=[('vertex','<f4',8),('bones','<i4',2),('weights','<f4',2)])
    record['vertex']=verts;record['bones']=bones;record['weights']=w
    offset=len(blob);blob.extend(record.tobytes());blob.extend(indices.astype('<u4').tobytes())
    parts.append(dict(node=node,nodeName=name,primitive=0,joint='Torso',material=0,offset=offset,vertices=len(verts),indices=len(indices)))
    print(name,len(f),'->',len(indices)//3,flush=True)
doc=dict(equipmentBinding='rigid per face: shield HandL, sword HandR, helmet Head; front chest ChestL/ChestR/ChestDoor; rear fins WingL/WingR',sourceSha256=hashlib.sha256(raw).hexdigest(),sourceParts=len(sources),triangles=sum(p['indices']//3 for p in parts),sourceTriangles=original,height=3.2,skinned=True,joints=joints,parts=parts)
(RES/(stem+'_rig.json')).write_text(json.dumps(doc,indent=2),encoding='utf-8');(RES/(stem+'_rig.bin')).write_bytes(blob)
out=RES/'CompleteTextures';out.mkdir(exist_ok=True)
for i,img in enumerate(g['images']):
    view=g['bufferViews'][img['bufferView']];data=raw[base+view.get('byteOffset',0):base+view.get('byteOffset',0)+view['byteLength']]
    picture=Image.open(io.BytesIO(data));picture.thumbnail((2048,2048),Image.Resampling.LANCZOS)
    if i==0:picture.convert('RGB').save(out/'base.png')
    else:
        arr=np.asarray(picture.convert('RGB'));unity=np.empty((*arr.shape[:2],4),dtype=np.uint8);unity[:,:,:3]=arr[:,:,2,None];unity[:,:,3]=255-arr[:,:,1];Image.fromarray(unity).save(out/'metallic.png')
(RES/'Samurai-Attribution.txt').write_text('Samurai Style Gundam Mecha\nAuthor: lawlietrecluze\nSource: '+g['asset']['extras']['source']+'\nLicense: CC-BY-4.0\nOriginal GLB retained. Runtime derivative: texture-aware mesh reduction, texture channel conversion, procedural skin weights.\n',encoding='utf-8')
print('COMPLETE',doc['sourceTriangles'],'->',doc['triangles'],'triangles;',len(blob),'bytes')

# Final deterministic semantic partition for cockpit visibility and sword roots.
import runpy
runpy.run_path(str(ROOT/"tools/Mecha/Prepare-RenderRoles.py"))

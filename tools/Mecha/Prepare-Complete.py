"""Bake Samurai Style Gundam into a textured, weighted runtime rig.
Requires numpy, Pillow, pymeshlab (asset-build tools only; no game dependency).
Source is preserved. MeshLab texture-aware reduction keeps source chunk borders.
"""
import hashlib, io, json, pathlib, struct, sys
import numpy as np
from PIL import Image
ROOT=pathlib.Path(__file__).resolve().parents[2]
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
jointid={j['name']:i for i,j in enumerate(joints)}
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
        arm=half&(np.abs(x)>.175)&(y>-.25)
        blend(arm,'Torso','Shoulder'+suffix,(np.abs(x)-.175)/.055)
        blend(arm&(y<.12),'Shoulder'+suffix,'Elbow'+suffix,(.12-y)/.10)
        blend(arm&(y<-.045),'Elbow'+suffix,'Hand'+suffix,(-y-.045)/.085)
    back=z<-.16;single(back,'Backpack');w[back,0]=1;w[back,1]=0
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
    packed=np.column_stack((points[faces.reshape(-1)],norm[faces.reshape(-1)],tex)).astype('<f4')
    verts,indices=np.unique(packed,axis=0,return_inverse=True)
    bones,w=weights(verts[:,:3]);verts[:,1]=(verts[:,1]-floor)*scale+.05;verts[:,[0,2]]*=scale
    lens=np.linalg.norm(verts[:,3:6],axis=1);verts[:,3:6]/=np.maximum(lens[:,None],1e-12)
    record=np.empty(len(verts),dtype=[('vertex','<f4',8),('bones','<i4',2),('weights','<f4',2)])
    record['vertex']=verts;record['bones']=bones;record['weights']=w
    offset=len(blob);blob.extend(record.tobytes());blob.extend(indices.astype('<u4').tobytes())
    parts.append(dict(node=node,nodeName=name,primitive=0,joint='Torso',material=0,offset=offset,vertices=len(verts),indices=len(indices)))
    print(name,len(f),'->',len(indices)//3,flush=True)
doc=dict(sourceSha256=hashlib.sha256(raw).hexdigest(),sourceParts=len(sources),triangles=sum(p['indices']//3 for p in parts),sourceTriangles=original,height=3.2,skinned=True,joints=joints,parts=parts)
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

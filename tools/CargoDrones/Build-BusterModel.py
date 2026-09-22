"""Offline conversion of the user-supplied Buster Drone GLB to a small native mesh pack.
Requires NumPy/Pillow; no Unity editor, runtime glTF importer or third-party scripts.
Run: python tools/CargoDrones/Build-BusterModel.py --source <buster_drone.glb>
"""
import argparse, hashlib, io, json, math, struct
from pathlib import Path
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]

def slerp(a, b, t):
    a, b = np.array(a, float), np.array(b, float)
    dot = float(a @ b)
    if dot < 0: b, dot = -b, -dot
    if dot > .9995:
        q = a + t * (b-a)
        return q / np.linalg.norm(q)
    angle = math.acos(np.clip(dot, -1, 1))
    return (math.sin((1-t)*angle)*a + math.sin(t*angle)*b)/math.sin(angle)

def matrix(p, q, s):
    x,y,z,w = q
    m = np.eye(4)
    m[:3,:3] = np.array([[1-2*y*y-2*z*z,2*x*y-2*z*w,2*x*z+2*y*w],
        [2*x*y+2*z*w,1-2*x*x-2*z*z,2*y*z-2*x*w],
        [2*x*z-2*y*w,2*y*z+2*x*w,1-2*x*x-2*y*y]]) @ np.diag(s)
    m[:3,3] = p
    return m

def multiply(a,b):
    av,bv=np.array(a[:3]),np.array(b[:3]);aw,bw=a[3],b[3]
    return np.r_[aw*bv+bw*av+np.cross(av,bv),aw*bw-av@bv]

def build(source, out):
    data = source.read_bytes()
    magic, version, total = struct.unpack_from('<III', data)
    assert magic == 0x46546c67 and version == 2 and total == len(data)
    size, kind = struct.unpack_from('<II', data, 12)
    assert kind == 0x4e4f534a
    g = json.loads(data[20:20+size]); binary = data[28+size:]
    assert struct.unpack_from('<I', data, 24+size)[0] == 0x004e4942
    assert not g.get('skins') and not g.get('extensionsRequired')
    def accessor(i):
        a=g['accessors'][i]; v=g['bufferViews'][a['bufferView']]
        assert not a.get('sparse') and not a.get('normalized')
        dtype={5126:'<f4',5125:'<u4',5123:'<u2',5121:'u1'}[a['componentType']]
        dims={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4}[a['type']]
        offset=v.get('byteOffset',0)+a.get('byteOffset',0)
        stride=v.get('byteStride',np.dtype(dtype).itemsize*dims)
        return np.ndarray((a['count'],dims),dtype,buffer=binary,offset=offset,
            strides=(stride,np.dtype(dtype).itemsize)).copy()
    nodes=g['nodes']
    for node in nodes:
        if 'matrix' not in node: continue
        m=np.array(node.pop('matrix'),float).reshape(4,4).T
        scale=np.linalg.norm(m[:3,:3],axis=0)
        if np.linalg.det(m[:3,:3])<0: scale[0]*=-1
        r=m[:3,:3]/scale
        assert np.allclose(r.T@r,np.eye(3),atol=1e-5), 'Shear is unsupported'
        # Symmetric eigen decomposition yields a stable quaternion near 180 degrees.
        xx,xy,xz=r[0];yx,yy,yz=r[1];zx,zy,zz=r[2]
        k=np.array([[xx-yy-zz,xy+yx,xz+zx,zy-yz],[xy+yx,yy-xx-zz,yz+zy,xz-zx],
            [xz+zx,yz+zy,zz-xx-yy,yx-xy],[zy-yz,xz-zx,yx-xy,xx+yy+zz]])/3
        _,vectors=np.linalg.eigh(k);q=vectors[:,-1]
        node.update(translation=m[:3,3].tolist(),rotation=q.tolist(),scale=scale.tolist())
    parent={c:i for i,n in enumerate(nodes) for c in n.get('children',[])}
    def descendants(i):
        return [i]+[j for c in nodes[i].get('children',[]) for j in descendants(c)]
    env=next(i for i,n in enumerate(nodes) if n['name']=='Env')
    keep=[i for i in range(len(nodes)) if i not in descendants(env)]
    index={n:i for i,n in enumerate(keep)}
    body=next(i for i,n in enumerate(nodes) if n['name']=='Drone_Body')
    mechanical=set(descendants(body))-{body}
    rotor_ids=[i for i,n in enumerate(nodes) if n['name'] in ('Drone_Turb_Blade_L','Drone_Turb_Blade_R')]
    assert len(rotor_ids)==2
    channels={}
    animation=g['animations'][0]; assert animation['name']=='Start_Liftoff'
    for c in animation['channels']:
        sampler=animation['samplers'][c['sampler']]
        assert sampler.get('interpolation','LINEAR')=='LINEAR'
        channels[c['target']['node'],c['target']['path']]=(accessor(sampler['input'])[:,0],accessor(sampler['output']))
    def pose(i,t):
        values=[]
        for path,default in [('translation',[0,0,0]),('rotation',[0,0,0,1]),('scale',[1,1,1])]:
            value=np.array(nodes[i].get(path,default),float)
            if (i,path) in channels:
                times,keys=channels[i,path]
                # Root motion, body rocking and scripted turns stay at the initial pose.
                at=t if i in mechanical and i not in rotor_ids else 0
                k=max(0,min(len(times)-2,int(np.searchsorted(times,at,side='right'))-1))
                alpha=float(np.clip((at-times[k])/(times[k+1]-times[k]),0,1))
                value=slerp(keys[k],keys[k+1],alpha) if path=='rotation' else keys[k]*(1-alpha)+keys[k+1]*alpha
            if path=='translation': value=value*np.array([1,1,-1])
            if path=='rotation': value=value*np.array([-1,-1,1,1])
            values.append(value)
        assert 'matrix' not in nodes[i], 'TRS expected'
        return values
    poses={i:(pose(i,0),pose(i,25)) for i in keep}
    mesh_ids=sorted({nodes[i]['mesh'] for i in keep if 'mesh' in nodes[i]})
    meshes=[]; corners={}; axes={}
    for mid in mesh_ids:
        prims=g['meshes'][mid]['primitives']; assert len(prims)==1
        p=prims[0]; a=p['attributes']; assert p.get('mode',4)==4
        assert not any(g['meshes'][mid].get('weights',[])), 'Nonzero default morph needs baking'
        xyz=accessor(a['POSITION'])*[1,1,-1]; normal=accessor(a['NORMAL'])*[1,1,-1]
        tangent=accessor(a['TANGENT'])*[1,1,-1,-1]
        uv=accessor(a['TEXCOORD_0']); uv[:,1]=1-uv[:,1]
        for slot in ('normalTexture','occlusionTexture','emissiveTexture'):
            assert g['materials'][p['material']].get(slot,{}).get('texCoord',0)==0
        triangles=accessor(p['indices']).reshape(-1,3)[:,[0,2,1]]
        lo,hi=xyz.min(0),xyz.max(0)
        corners[mid]=np.array([[x,y,z,1] for x in [lo[0],hi[0]] for y in [lo[1],hi[1]] for z in [lo[2],hi[2]]])
        meshes.append((xyz,normal,tangent,uv,triangles,p['material']-1))
        assert p['material'] in (1,2)
    for i in rotor_ids:
        children=[c for c in descendants(i) if 'mesh' in nodes[c]]; assert len(children)==1
        child=children[0]; mid=nodes[child]['mesh']; verts=meshes[mesh_ids.index(mid)][0]
        # Actual blade plane determines the spin axis in the pivot's local coordinates.
        _,eig=np.linalg.eigh(np.cov(verts.T)); axis=eig[:,0]
        axis=matrix(*poses[child][0])[:3,:3]@axis; axis/=np.linalg.norm(axis)
        axes[i]=axis
    def transforms(blend,angle):
        result={}
        for i in keep:
            a,b=poses[i]; p=a[0]*(1-blend)+b[0]*blend; q=slerp(a[1],b[1],blend); s=a[2]*(1-blend)+b[2]*blend
            local=matrix(p,q,s)
            if i in axes:
                axis=axes[i]; h=angle*.5; spin=np.r_[axis*math.sin(h),math.cos(h)]
                local=local@matrix([0,0,0],spin,[1,1,1])
            result[i]=(result[parent[i]] if i in parent else np.eye(4))@local
        return result
    # The demonstration banks the whole body; that root motion is removed here.
    # Reorient each complete turbine housing so its flight blade plane stays horizontal.
    world=transforms(1,0)
    for i in rotor_ids:
        mount=parent[i]; basis=world[parent[mount]][:3,:3]
        axis=world[i][:3,:3]@axes[i]
        if axis[1]<0: axis=-axis
        a=np.linalg.solve(basis,axis);a/=np.linalg.norm(a)
        b=np.linalg.solve(basis,[0,1,0]);b/=np.linalg.norm(b)
        correction=np.r_[np.cross(a,b),1+a@b];correction/=np.linalg.norm(correction)
        poses[mount][1][1]=multiply(correction,poses[mount][1][1])
    # Orient the lens toward Unity +Z; then center the whole animated envelope.
    rest=transforms(0,0)
    eye=next(i for i in keep if nodes[i]['name']=='Drone_ILens_body_0')
    eye_center=(rest[eye]@corners[nodes[eye]['mesh']].mean(0))[:3]
    body_mesh=next(i for i in keep if nodes[i]['name']=='Drone_Body_body_0')
    body_center=(rest[body_mesh]@corners[nodes[body_mesh]['mesh']].mean(0))[:3]
    delta=eye_center-body_center; yaw=-math.atan2(delta[0],delta[2])
    facing=matrix([0,0,0],[0,math.sin(yaw/2),0,math.cos(yaw/2)],[1,1,1])
    clouds=[]
    for blend in np.linspace(0,1,41):
        for angle in np.linspace(0,math.tau,24,endpoint=False):
            world=transforms(blend,angle)
            clouds.append(np.concatenate([(corners[nodes[i]['mesh']]@(facing@world[i]).T)[:,:3] for i in keep if 'mesh' in nodes[i]]))
    points=np.concatenate(clouds); center=(points.min(0)+points.max(0))*.5
    centered=points-center; radius=np.linalg.norm(centered[:,[0,2]],axis=1).max(); height=abs(centered[:,1]).max()
    scale=min(.74/radius,.54/height) # margin for between-sample interpolation; server half extents .8/.6
    out.mkdir(parents=True,exist_ok=True)
    texture_report=[]
    def image(texture):
        im=g['images'][g['textures'][texture['index']]['source']]; view=g['bufferViews'][im['bufferView']]
        raw=binary[view.get('byteOffset',0):view.get('byteOffset',0)+view['byteLength']]
        result=Image.open(io.BytesIO(raw)).convert('RGBA'); result.thumbnail((2048,2048),Image.Resampling.LANCZOS)
        return result
    for mi, mat in enumerate(g['materials'][1:]):
        pbr=mat['pbrMetallicRoughness']
        base=image(pbr['baseColorTexture'])
        base.save(out/f'{mi}-base.png')
        orm=image(pbr['metallicRoughnessTexture']); r,green,blue,_=orm.split()
        one=Image.new('L',orm.size,255)
        Image.merge('RGBA',(blue,one,one,green.point(lambda v:255-v))).save(out/f'{mi}-metal.png')
        Image.merge('RGBA',(r,r,r,one)).save(out/f'{mi}-occlusion.png')
        normal=image(mat['normalTexture']); nr,ng,_,_=normal.split(); no=Image.new('L',normal.size,255)
        Image.merge('RGBA',(no,ng,no,nr)).save(out/f'{mi}-normal.png')
        if 'emissiveTexture' in mat: image(mat['emissiveTexture']).save(out/f'{mi}-emission.png')
        texture_report.append({'material':mat['name'],'size':list(base.size),'alpha':base.getchannel('A').getextrema()})
    with (out/'buster.yfmesh').open('wb') as f:
        def ints(*v): f.write(struct.pack('<'+'i'*len(v),*v))
        def floats(v): f.write(np.asarray(v,dtype='<f4').tobytes())
        def string(v): b=v.encode();ints(len(b));f.write(b)
        ints(0x59464244,1,len(keep),len(meshes));floats([scale,yaw]);floats(center)
        for i in keep:
            string(nodes[i]['name']);ints(index.get(parent.get(i),-1),mesh_ids.index(nodes[i]['mesh']) if 'mesh' in nodes[i] else -1)
            for p in poses[i]:
                for v in p:floats(v)
            floats(axes.get(i,[0,0,0]))
        for xyz,normal,tangent,uv,triangles,mat in meshes:
            ints(len(xyz),triangles.size,mat)
            floats(np.concatenate([xyz,normal,tangent,uv],axis=1));f.write(np.asarray(triangles,dtype='<i4').tobytes())
    report={'source_sha256':hashlib.sha256(data).hexdigest(),'asset':g['asset'],'meshes':len(meshes),
        'triangles':sum(len(m[4]) for m in meshes),'nodes':len(keep),'rotors':[nodes[i]['name'] for i in rotor_ids],
        'scale':scale,'yaw_degrees':math.degrees(yaw),'center':center.tolist(),'radial_extent':radius*scale,'vertical_extent':height*scale,
        'textures':texture_report,'changes':'Removed Env display stage; retained 38 rigid meshes; 2K PBR textures; mirrored coordinates/UVs and winding for Unity; packed Standard normal/metal maps; mechanical pose blend from source 0s/25s with root/body motion removed and flight turbine planes leveled; static pupil without morph animation; continuous blade rotation about measured local axes; normalized animated envelope.',
        'files':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(out.iterdir()) if p.suffix in ('.png','.yfmesh')}}
    (out/'provenance.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({k:v for k,v in report.items() if k not in ('files','asset')},indent=2))

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--source',type=Path,required=True)
    parser.add_argument('--output',type=Path,default=ROOT/'97-AutomationWorkshop/Resources/CargoDrone')
    args=parser.parse_args();build(args.source,args.output)

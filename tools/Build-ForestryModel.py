"""Bake the user-supplied GLB into a small, version-independent runtime mesh file.
No downloaded code is executed. Requires numpy and Pillow.
"""
import argparse, hashlib, io, json, struct
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
from ForestrySurfaces import surfaces, remap as remap_surfaces, simplify

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / '98-AECxProjectZ_Tweaks/Resources/Forestry'

def build(source):
    raw = source.read_bytes()
    assert raw[:4] == b'glTF' and struct.unpack_from('<I', raw, 4)[0] == 2
    chunks, offset = {}, 12
    while offset < len(raw):
        size, kind = struct.unpack_from('<II', raw, offset)
        chunks[kind] = raw[offset+8:offset+8+size]
        offset += 8 + size
    doc = json.loads(chunks[0x4e4f534a]); blob = chunks[0x004e4942]
    def accessor(index):
        a = doc['accessors'][index]; v = doc['bufferViews'][a['bufferView']]
        assert 'sparse' not in a
        dtype = {5126:'<f4',5125:'<u4',5123:'<u2',5121:'u1'}[a['componentType']]
        dim = {'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4}[a['type']]
        step = np.dtype(dtype).itemsize
        return np.ndarray((a['count'],dim), dtype=dtype, buffer=blob,
            offset=v.get('byteOffset',0)+a.get('byteOffset',0),
            strides=(v.get('byteStride',dim*step),step)).copy()
    images=[]
    for item in doc['images']:
        v=doc['bufferViews'][item['bufferView']]; start=v.get('byteOffset',0)
        images.append(Image.open(io.BytesIO(blob[start:start+v['byteLength']])).convert('RGBA'))
    def tex(index): return images[doc['textures'][index]['source']]
    # A layout-preserving authored albedo edit. Keep original data maps at their
    # native resolution; texture resolution need not match across PBR channels.
    detail=ROOT/'tools/Forestry/Textures/color1-detail.png'
    if detail.exists():
        slot=doc['materials'][1]['pbrMetallicRoughness']['baseColorTexture']['index']
        images[doc['textures'][slot]['source']]=Image.open(detail).convert('RGBA')
    OUT.mkdir(parents=True,exist_ok=True)
    for i,m in enumerate(doc['materials']):
        p=m['pbrMetallicRoughness']
        tex(p['baseColorTexture']['index']).save(OUT/f'color{i}.png')
        mr=np.array(tex(p['metallicRoughnessTexture']['index']))
        packed=np.zeros_like(mr); packed[:,:,0]=mr[:,:,2]; packed[:,:,3]=255-mr[:,:,1]
        Image.fromarray(packed).save(OUT/f'metal{i}.png')
        normal=np.array(tex(m['normalTexture']['index']))
        packed=np.full_like(normal,255); packed[:,:,1]=normal[:,:,1]; packed[:,:,3]=normal[:,:,0]
        Image.fromarray(packed).save(OUT/f'normal{i}.png')
    parts=[]
    # glTF RH world -> Unity LH, then turn the long axis along X.
    basis=np.array([[0,0,1],[0,1,0],[1,0,0]],dtype=float)
    def walk(index,parent):
        node=doc['nodes'][index]
        assert not any(k in node for k in ('rotation','translation','scale')), 'Unexpected TRS: review source'
        matrix=parent @ np.array(node.get('matrix',np.eye(4).flatten(order='F')),dtype=float).reshape(4,4,order='F')
        if 'mesh' in node:
            for p in doc['meshes'][node['mesh']]['primitives']:
                assert p.get('mode',4)==4
                a=p['attributes']; pos=accessor(a['POSITION']); norm=accessor(a['NORMAL'])
                pos=(pos @ matrix[:3,:3].T + matrix[:3,3]) @ basis.T
                norm=norm @ np.linalg.inv(matrix[:3,:3]) @ basis.T
                norm/=np.maximum(np.linalg.norm(norm,axis=1,keepdims=True),1e-9)
                uv=accessor(a['TEXCOORD_0']); uv[:,1]=1-uv[:,1]
                faces=accessor(p['indices']).reshape(-1,3).astype('<i4')
                if np.linalg.det(basis @ matrix[:3,:3])<0: faces=faces[:,[0,2,1]]
                parts.append([pos,norm,uv,faces,p['material']])
        for child in node.get('children',[]): walk(child,matrix)
    for node in doc['scenes'][doc.get('scene',0)]['nodes']: walk(node,np.eye(4))
    allpos=np.concatenate([p[0] for p in parts]); low=allpos.min(0); high=allpos.max(0)
    size=high-low; scale=min(5.8/size[0],3.8/size[2],3.7/size[1])
    center=(low+high)/2; center[1]=low[1]
    for p in parts: p[0]=(p[0]-center)*scale+np.array([0,.08,0])
    # Remove the separate yard ground and perimeter fence, preserving whole connected
    # components rather than cutting triangles through the building or conveyor.
    points=np.concatenate([p[0] for p in parts])
    unique,inverse=np.unique(np.round(points,5),axis=0,return_inverse=True)
    parent=list(range(len(unique)))
    def root(a):
        while parent[a]!=a:
            parent[a]=parent[parent[a]]; a=parent[a]
        return a
    offset=0
    for p in parts:
        for a,b,c in inverse[p[3]+offset]:
            r=root(a); parent[root(b)]=r; parent[root(c)]=r
        offset+=len(p[0])
    groups={}
    for i in range(len(unique)): groups.setdefault(root(i),[]).append(i)
    keep=np.zeros(len(unique),dtype=bool)
    for group in groups.values():
        q=unique[group]; lo=q.min(0); hi=q.max(0)
        if lo[0]>-2.2 and hi[0]<2.2 and lo[2]>-1.2 and hi[2]<1.2:
            keep[group]=True
    offset=0; filtered=[]
    for pos,norm,uv,faces,mat in parts:
        faces=faces[keep[inverse[faces+offset]].all(1)]; offset+=len(pos)
        if not len(faces): continue
        indices,remap=np.unique(faces,return_inverse=True)
        filtered.append([pos[indices],norm[indices],uv[indices],remap.reshape(-1,3),mat])
    parts=filtered; q=np.concatenate([p[0] for p in parts]); lo=q.min(0); hi=q.max(0)
    fitted=hi-lo; refit=min(9.5/fitted[0],5.2/fitted[2],3.65/fitted[1])
    center=(lo+hi)/2; center[1]=lo[1]
    for p in parts: p[0]=(p[0]-center)*refit+np.array([0,.10,.30])
    # Keep 10x6 and the four-cell height budget. Most correction goes to lower
    # walls/door openings; the tower should not become excessively tall.
    height_scale=1.36
    knee=1.15; lower_gain=1.8
    upper_gain=(fitted[1]*refit*height_scale-(knee-.10)*lower_gain)/(fitted[1]*refit-(knee-.10))
    for p in parts:
        y=p[0][:,1].copy(); lower=y<=knee
        p[0][:,1]=np.where(lower,.10+(y-.10)*lower_gain,.10+(knee-.10)*lower_gain+(y-knee)*upper_gain)
        p[1][:,1]/=np.where(lower,lower_gain,upper_gain)
        p[1]/=np.maximum(np.linalg.norm(p[1],axis=1,keepdims=True),1e-9)
    surfaces(ROOT,OUT,images,doc)
    original_triangles=sum(len(p[3])*2 for p in parts)
    parts=remap_surfaces(parts)
    assert sum(len(p[3])*2 for p in parts)==original_triangles
    def write_mesh(path,data):
      with path.open('wb') as f:
        f.write(b'YFF1'); f.write(struct.pack('<II',len(doc['materials']),len(data)))
        for pos,norm,uv,faces,mat in data:
            count=len(pos)
            # Source materials are two-sided; explicit backfaces also work with stock game shaders.
            verts=np.concatenate([np.c_[pos,norm,uv],np.c_[pos,-norm,uv]]).astype('<f4')
            tris=np.concatenate([faces,faces[:,[0,2,1]]+count]).astype('<i4').flatten()
            f.write(struct.pack('<III',mat,len(verts),len(tris))); f.write(verts.tobytes()); f.write(tris.tobytes())
    write_mesh(OUT/'sawmill.meshbin',parts)
    distant=simplify(parts)
    write_mesh(OUT/'sawmill-lod.meshbin',distant)
    report={'source':source.name,'sha256':hashlib.sha256(raw).hexdigest(),'sourceSize':size.tolist(),
        'scale':scale*refit,'heightScale':height_scale,'modelSize':(fitted*refit*np.array([1,height_scale,1])).tolist(),'footprint':[10,4,6],
        'heightProfile':{'knee':knee,'lowerGain':lower_gain,'upperGain':upper_gain},
        'adaptation':'Removed detached yard and fence; raised lower walls, total height ratio 1.36; base y=.10, center z=.30',
        'parts':len(parts),'triangles':sum(len(p[3])*2 for p in parts),'materials':len(doc['materials']),
        'meshSha256':hashlib.sha256((OUT/'sawmill.meshbin').read_bytes()).hexdigest()}
    report['detailAlbedo']=detail.exists()
    report['surfaceMaterials']=['source_misc','source_fallback','roof','brick','wood','painted_metal']
    report['lodTriangles']=sum(len(p[3])*2 for p in distant)
    report['lodSha256']=hashlib.sha256((OUT/'sawmill-lod.meshbin').read_bytes()).hexdigest()
    report['albedoSizes']=[list(tex(m['pbrMetallicRoughness']['baseColorTexture']['index']).size) for m in doc['materials']]
    (OUT/'model-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    preview(parts,images,doc,OUT/'preview.png',900)
    preview(distant,images,doc,OUT/'preview-lod.png',900)
    icons=ROOT/'98-AECxProjectZ_Tweaks/ItemIcons'; icons.mkdir(exist_ok=True)
    preview(parts,images,doc,icons/'yfAutoForestry.png',256,True)
    print(json.dumps(report,indent=2))

def preview(parts,images,doc,path,res,transparent=False):
    camera=np.array([7.,7.,-9.]); camera/=np.linalg.norm(camera)
    right=np.cross([0,1,0],camera); right/=np.linalg.norm(right); up=np.cross(camera,right)
    basis=np.array([right,up,camera]); data=[]
    for pos,norm,uv,faces,mat in parts:
        projected=pos@basis.T
        data.append((projected,norm,uv,faces,mat))
    xy=np.concatenate([p[0][:,:2] for p in data]); lo=xy.min(0); hi=xy.max(0)
    scale=res*.88/max(hi-lo); middle=(hi+lo)/2
    canvas=np.zeros((res,res,4),dtype=np.uint8)
    if not transparent: canvas[:]=[30,35,40,255]
    depth=np.full((res,res),-np.inf)
    light=np.array([.3,.9,-.3]); light/=np.linalg.norm(light)
    for projected,norm,uv,faces,mat in data:
        screen=(projected[:,:2]-middle)*scale; screen[:,1]*=-1; screen+=res/2
        ti=doc['materials'][mat]['pbrMetallicRoughness']['baseColorTexture']['index']
        tex=np.array(images[doc['textures'][ti]['source']]); h,w=tex.shape[:2]
        for face in faces:
            p=screen[face]; xmin,ymin=np.maximum(np.floor(p.min(0)).astype(int),0); xmax,ymax=np.minimum(np.ceil(p.max(0)).astype(int),res-1)
            if xmax<xmin or ymax<ymin: continue
            denom=(p[1,1]-p[2,1])*(p[0,0]-p[2,0])+(p[2,0]-p[1,0])*(p[0,1]-p[2,1])
            if abs(denom)<1e-8: continue
            yy,xx=np.mgrid[ymin:ymax+1,xmin:xmax+1]; xx=xx+.5; yy=yy+.5
            a=((p[1,1]-p[2,1])*(xx-p[2,0])+(p[2,0]-p[1,0])*(yy-p[2,1]))/denom
            b=((p[2,1]-p[0,1])*(xx-p[2,0])+(p[0,0]-p[2,0])*(yy-p[2,1]))/denom; c=1-a-b
            z=a*projected[face[0],2]+b*projected[face[1],2]+c*projected[face[2],2]
            mask=(a>=0)&(b>=0)&(c>=0)&(z>depth[ymin:ymax+1,xmin:xmax+1])
            if not mask.any(): continue
            coords=a[...,None]*uv[face[0]]+b[...,None]*uv[face[1]]+c[...,None]*uv[face[2]]
            color=tex[((1-coords[:,:,1])*h).astype(int)%h,(coords[:,:,0]*w).astype(int)%w].copy()
            mask &= color[:,:,3]>100
            shade=.55+.45*abs(float(norm[face].mean(0)@light)); color[:,:,:3]=(color[:,:,:3]*shade).astype(np.uint8)
            color[:,:,3]=255
            canvas[ymin:ymax+1,xmin:xmax+1][mask]=color[mask]; depth[ymin:ymax+1,xmin:xmax+1][mask]=z[mask]
    Image.fromarray(canvas).save(path)

if __name__=='__main__':
    parser=argparse.ArgumentParser(); parser.add_argument('source',type=Path)
    build(parser.parse_args().source)

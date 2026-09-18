"""Read the pinned user asset without modifying it. Geometry is in design metres."""
import io, json, struct, hashlib
from pathlib import Path
import numpy as np
from PIL import Image

SOURCE=Path('F:/game/m1_abrams.glb')
HASH='b6d0b9c75dcaa4ff0aa2f85cc207a0def4d6d4b0ce47378979288cccfe835c3b'
SCALE=8.996092816

def read():
    raw=SOURCE.read_bytes()
    assert hashlib.sha256(raw).hexdigest()==HASH, 'Source changed; reassess semantic map'
    size=struct.unpack_from('<I',raw,12)[0]; doc=json.loads(raw[20:20+size]); blob=raw[28+size:]
    def acc(i):
        a=doc['accessors'][i]; b=doc['bufferViews'][a['bufferView']]
        dt=np.dtype({5126:'<f4',5125:'<u4',5123:'<u2',5121:'u1'}[a['componentType']])
        n={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4}[a['type']]
        return np.ndarray((a['count'],n),dt,buffer=blob,offset=b.get('byteOffset',0)+a.get('byteOffset',0),strides=(b.get('byteStride',n*dt.itemsize),dt.itemsize)).copy()
    images=[]
    for item in doc['images']:
        b=doc['bufferViews'][item['bufferView']]; off=b.get('byteOffset',0)
        images.append(Image.open(io.BytesIO(blob[off:off+b['byteLength']])).convert('RGBA'))
    parts=[]; basis=np.array([[0,0,1],[0,1,0],[-1,0,0]],float)
    def walk(i,parent):
        node=doc['nodes'][i]; m=parent@np.array(node.get('matrix',np.eye(4).flatten(order='F'))).reshape(4,4,order='F')
        assert not any(k in node for k in ['translation','rotation','scale'])
        if 'mesh' in node:
            for p in doc['meshes'][node['mesh']]['primitives']:
                a=p['attributes']; v=acc(a['POSITION'])@m[:3,:3].T+m[:3,3]
                v=(v-np.array([.099508214,-.242106505,0]))@basis.T*SCALE
                n=acc(a['NORMAL'])@np.linalg.inv(m[:3,:3])@basis.T
                n/=np.maximum(np.linalg.norm(n,axis=1,keepdims=True),1e-12)
                f=acc(p['indices']).reshape(-1,3).astype(np.int32)
                parts.append(dict(v=v,n=n,uv=acc(a['TEXCOORD_0']),f=f,mat=p['material'],mesh=node['mesh']))
        for c in node.get('children',[]):walk(c,m)
    for i in doc['scenes'][0]['nodes']:walk(i,np.eye(4))
    return doc,images,parts

def components(p):
    _,inv=np.unique(np.round(p['v'],5),axis=0,return_inverse=True); parent=np.arange(inv.max()+1)
    def root(i):
        while parent[i]!=i:parent[i]=parent[parent[i]];i=parent[i]
        return i
    for t in inv[p['f']]:
        a=root(t[0]);parent[root(t[1])]=a;parent[root(t[2])]=a
    labels=np.array([root(i) for i in range(len(parent))])[inv]
    groups=[np.flatnonzero(labels[p['f'][:,0]]==l) for l in np.unique(labels)]
    return sorted(groups,key=lambda ids:(-len(ids),tuple(p['v'][p['f'][ids]].mean(axis=(0,1)))))

if __name__=='__main__':
    doc,images,parts=read(); out=Path(__file__).parent
    report=[]
    for p in parts:
        for i,ids in enumerate(components(p)):
            v=p['v'][p['f'][ids]].reshape(-1,3)
            report.append(dict(mesh=p['mesh'],id=i,triangles=len(ids),min=v.min(0).round(4).tolist(),max=v.max(0).round(4).tolist(),center=((v.min(0)+v.max(0))/2).round(4).tolist()))
    (out/'components.json').write_text(json.dumps(report,indent=2),encoding='utf8')
    print(json.dumps([x for x in report if x['triangles']>=80],indent=1))

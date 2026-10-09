"""Convert the supplied static GLB into a small, audited runtime mesh. stdlib only."""
import hashlib,json,struct
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
out=ROOT/'ZZ-PZAEC_FlyingSword/Resources'
raw=(out/'legend_sword.glb').read_bytes()
magic,version,length=struct.unpack_from('<4sII',raw)
assert magic==b'glTF' and version==2 and length==len(raw)
n,kind=struct.unpack_from('<II',raw,12);doc=json.loads(raw[20:20+n]);start=28+n
assert struct.unpack_from('<I',raw,24+n)[0]==0x004e4942
data=raw[start:]
def read(index):
 a=doc['accessors'][index];v=doc['bufferViews'][a['bufferView']]
 fmt={5126:'f',5125:'I',5123:'H',5121:'B'}[a['componentType']];nc={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4}[a['type']]
 size=struct.calcsize('<'+fmt*nc);stride=v.get('byteStride',size);offset=v.get('byteOffset',0)+a.get('byteOffset',0)
 return [struct.unpack_from('<'+fmt*nc,data,offset+i*stride) for i in range(a['count'])]
p=doc['meshes'][0]['primitives'][0];pos=read(p['attributes']['POSITION']);norm=read(p['attributes']['NORMAL']);uv=read(p['attributes']['TEXCOORD_0']);indices=[x[0] for x in read(p['indices'])]
lo=[min(v[i] for v in pos) for i in range(3)];hi=[max(v[i] for v in pos) for i in range(3)];center=[(a+b)/2 for a,b in zip(lo,hi)];scale=1/(hi[0]-lo[0])
with (out/'juque.mesh').open('wb') as f:
 f.write(b'JQ01');f.write(struct.pack('<II',len(pos),len(indices)))
 for v,no,t in zip(pos,norm,uv):
  xyz=((v[2]-center[2])*scale,(v[1]-center[1])*scale,-(v[0]-center[0])*scale)
  f.write(struct.pack('<8f',*xyz,no[2],no[1],-no[0],*t))
 for i in range(0,len(indices),3): f.write(struct.pack('<III',indices[i],indices[i+2],indices[i+1]))
for i,img in enumerate(doc['images']):
 view=doc['bufferViews'][img['bufferView']];off=view.get('byteOffset',0)
 (out/('color.jpg' if i==0 else 'normal.png')).write_bytes(data[off:off+view['byteLength']])
report={'source_sha256':hashlib.sha256(raw).hexdigest(),'vertices':len(pos),'triangles':len(indices)//3,'normalization':'source (z,y,-x), centered, length 1 metre','source':doc['asset']}
(out/'provenance.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(report,ensure_ascii=False))

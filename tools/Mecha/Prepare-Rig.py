"""Bake the fixed Combat Robot sculpture into rigid articulated parts (stdlib only).
The GLB is never modified. Node bindings below are authored for this exact asset.
Paired arm/leg meshes contain disconnected left/right geometry; split triangles,
never cut faces, and reject any triangle crossing the symmetry plane.
"""
import hashlib, json, math, pathlib, struct
ROOT = pathlib.Path(__file__).resolve().parents[2]
RES = ROOT / 'ZZ-PZAEC_Mecha/Resources'
data = (RES / 'combat_robot.glb').read_bytes()
length = struct.unpack_from('<I', data, 12)[0]
g = json.loads(data[20:20+length]); base = length + 28
identity = [1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1]
world = {}
def mul(a,b):
    return [sum(a[k*4+r]*b[c*4+k] for k in range(4)) for c in range(4) for r in range(4)]
def walk(i,p):
    n=g['nodes'][i]; world[i]=mul(p,n.get('matrix',identity))
    for c in n.get('children',[]): walk(c,world[i])
for i in g['scenes'][g.get('scene',0)]['nodes']: walk(i,identity)
def read(ai):
    a=g['accessors'][ai]; v=g['bufferViews'][a['bufferView']]
    count={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4}[a['type']]
    code={5126:'f',5125:'I',5123:'H'}[a['componentType']]; fmt='<'+code*count
    start=base+v.get('byteOffset',0)+a.get('byteOffset',0); stride=v.get('byteStride',struct.calcsize(fmt))
    return [struct.unpack_from(fmt,data,start+k*stride) for k in range(a['count'])]
def point(m,p): return [-sum(m[k*4]*p[k] for k in range(3))-m[12],sum(m[k*4+1]*p[k] for k in range(3))+m[13],sum(m[k*4+2]*p[k] for k in range(3))+m[14]]
def normal(m,p):
    # Inverse transpose of upper 3x3 (cofactor columns), then mirror X.
    a,b,c=m[0:3],m[4:7],m[8:11]
    cross=lambda u,v:[u[1]*v[2]-u[2]*v[1],u[2]*v[0]-u[0]*v[2],u[0]*v[1]-u[1]*v[0]]
    cols=[cross(b,c),cross(c,a),cross(a,b)]; det=sum(a[j]*cols[0][j] for j in range(3))
    q=[sum(cols[k][r]*p[k] for k in range(3))/det for r in range(3)]; q[0]*=-1
    size=math.sqrt(sum(x*x for x in q)); return [x/size for x in q]
sources=[]
for i,n in enumerate(g['nodes']):
    if 'mesh' not in n: continue
    for pi,p in enumerate(g['meshes'][n['mesh']]['primitives']):
        if g['materials'][p['material']]['name']=='Floor': continue
        pos=[point(world[i],v) for v in read(p['attributes']['POSITION'])]
        norm=[normal(world[i],v) for v in read(p['attributes']['NORMAL'])]
        uv=read(p['attributes']['TEXCOORD_0']); idx=[v[0] for v in read(p['indices'])]
        sources.append((i,n['name'],pi,p['material'],pos,norm,uv,idx))
floor=min(p[1] for s in sources for p in s[4]); top=max(p[1] for s in sources for p in s[4]); scale=3.2/(top-floor)
def fit(p): return [p[0]*scale,(p[1]-floor)*scale+.05,p[2]*scale]
# Explicit anatomical ownership, not height classification at runtime.
binding={i:'Torso' for i in range(4,310,2)}
for i in list(range(4,24,2))+[38]: binding[i]='Hand'
for i in list(range(24,56,2))+[308]: binding[i]='Elbow'
for i in [40,56,58,60,62,64,302,304,306]: binding[i]='Shoulder'
for i in [96,100,102,104,106,108,110]: binding[i]='Head'
for i in range(194,226,2): binding[i]='Hip'
for i in range(226,256,2): binding[i]='Knee'
for i in range(256,302,2): binding[i]='Ankle'
joints=[dict(name='Torso',parent='',position=fit([0,-1.35,-.4])),dict(name='Head',parent='Torso',position=fit([0,1.05,.25])),dict(name='Backpack',parent='Torso',position=fit([0,.3,-1.15]))]
for side,sign in [('L',-1),('R',1)]:
    for name,parent,p in [('Hip','Torso',[1.25,-1.35,-.4]),('Knee','Hip',[1.25,-2.7,.41]),('Ankle','Knee',[1.25,-4.16,.263]),('Foot','Ankle',[1.25,floor,.263]),('Shoulder','Torso',[1.58,.8,-.31]),('Elbow','Shoulder',[2.44,-.49,-.26]),('Hand','Elbow',[2.44,-.48,1.08])]:
        p[0]*=sign; joints.append(dict(name=name+side,parent=parent if parent=='Torso' else parent+side,position=fit(p)))
parts=[]; blob=bytearray(); original_triangles=0
for i,name,pi,material,pos,norm,uv,idx in sources:
    group=binding[i]; paired=group not in ('Torso','Head'); selections={'L':[],'R':[]} if paired else {'':[]}
    original_triangles+=len(idx)//3
    for t in range(0,len(idx),3):
        tri=idx[t:t+3]; xs=[pos[k][0] for k in tri]
        if paired and min(xs)<-.0001 and max(xs)>.0001: raise ValueError(('cross-midline triangle',i,t))
        side=('L' if sum(xs)<0 else 'R') if paired else ''
        selections[side].extend([tri[0],tri[2],tri[1]]) # mirrored winding
    for side,indices in selections.items():
        if not indices: continue
        remap={}; verts=[]; local=[]
        for old in indices:
            if old not in remap: remap[old]=len(verts); verts.append(old)
            local.append(remap[old])
        offset=len(blob)
        for v in verts: blob.extend(struct.pack('<8f',*(fit(pos[v])+norm[v]+list(uv[v]))))
        for v in local: blob.extend(struct.pack('<I',v))
        parts.append(dict(node=i,nodeName=name,primitive=pi,joint=group+side,material=material,offset=offset,vertices=len(verts),indices=len(local)))
assert len(sources)==153 and sum(p['indices'] for p in parts)==original_triangles*3
doc=dict(sourceSha256=hashlib.sha256(data).hexdigest(),height=3.2,sourceParts=153,triangles=original_triangles,joints=joints,parts=parts)
(RES/'combat_robot_rig.json').write_text(json.dumps(doc,ensure_ascii=False,indent=2),encoding='utf-8')
(RES/'combat_robot_rig.bin').write_bytes(blob)
print('Rig:',len(parts),'rigid pieces;',original_triangles,'triangles; scale',scale,'floor',floor)

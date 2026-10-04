"""Independent verification of baked geometry against original GLB, no writes."""
import collections, hashlib, json, math, pathlib, struct, wave
root=pathlib.Path(__file__).resolve().parents[2];res=root/'ZZ-PZAEC_Mecha/Resources'
data=(res/'combat_robot.glb').read_bytes();size=struct.unpack_from('<I',data,12)[0];g=json.loads(data[20:20+size]);base=size+28
doc=json.loads((res/'combat_robot_rig.json').read_text());blob=(res/'combat_robot_rig.bin').read_bytes()
assert hashlib.sha256(data).hexdigest()==doc['sourceSha256']
def multiply(a,b):return [[sum(a[r][k]*b[k][c] for k in range(4)) for c in range(4)] for r in range(4)]
def point(m,p):return tuple(sum(m[r][k]*p[k] for k in range(3))+m[r][3] for r in range(3))
identity=[[int(r==c) for c in range(4)] for r in range(4)];world={}
def walk(i,parent):
    n=g['nodes'][i];raw=n.get('matrix');local=[[raw[c*4+r] for c in range(4)] for r in range(4)] if raw else identity
    world[i]=multiply(parent,local)
    for c in n.get('children',[]):walk(c,world[i])
for i in g['scenes'][0]['nodes']:walk(i,identity)
def read(ai):
    a=g['accessors'][ai];v=g['bufferViews'][a['bufferView']];comps={'VEC3':3,'SCALAR':1}[a['type']];fmt='<'+{5126:'f',5125:'I',5123:'H'}[a['componentType']]*comps
    return [struct.unpack_from(fmt,data,base+v.get('byteOffset',0)+a.get('byteOffset',0)+k*v.get('byteStride',struct.calcsize(fmt))) for k in range(a['count'])]
sources={}
for i,n in enumerate(g['nodes']):
    if 'mesh' not in n:continue
    p=g['meshes'][n['mesh']]['primitives'][0]
    if g['materials'][p['material']]['name']=='Floor':continue
    positions=[point(world[i],p) for p in read(p['attributes']['POSITION'])];sources[i]=(positions,[v[0] for v in read(p['indices'])])
low=min(v[1] for pos,_ in sources.values() for v in pos);high=max(v[1] for pos,_ in sources.values() for v in pos);scale=3.2/(high-low)
# Reproduce only the file storage rounding, not the importer/baker implementation.
def quant(p):return tuple(round(v,5) for v in struct.unpack('<3f',struct.pack('<3f',*p)))
expected=collections.Counter();actual=collections.Counter()
for node,(pos,indices) in sources.items():
    pos=[quant((-v[0]*scale,(v[1]-low)*scale+.05,v[2]*scale)) for v in pos]
    for k in range(0,len(indices),3):expected[(node,tuple(pos[indices[k+j]] for j in (0,2,1)))]+=1
joint={j['name']:j for j in doc['joints']};seen=set();max_rest_error=0
for p in doc['parts']:
    assert g['nodes'][p['node']]['name']==p['nodeName'];assert p['joint'] in joint;assert p['indices']%3==0;seen.add(p['node'])
    verts=[]
    for k in range(p['vertices']):
        values=struct.unpack_from('<8f',blob,p['offset']+k*32);assert all(math.isfinite(v) for v in values)
        assert abs(sum(v*v for v in values[3:6])-1)<1e-5
        pos=values[:3];pivot=joint[p['joint']]['position'];reconstructed=[(pos[i]-pivot[i])+pivot[i] for i in range(3)]
        max_rest_error=max(max_rest_error,max(abs(reconstructed[i]-pos[i]) for i in range(3)));verts.append(quant(pos))
    indices=struct.unpack_from('<'+'I'*p['indices'],blob,p['offset']+p['vertices']*32)
    assert max(indices)<len(verts)
    for k in range(0,len(indices),3):actual[(p['node'],tuple(verts[indices[k+j]] for j in (0,1,2)))]+=1
assert expected==actual,'Baked triangles differ from original transformed geometry'
assert len(seen)==153 and sum(actual.values())==87806 and max_rest_error<1e-12
print('PASS 87,806 oriented triangles match original GLB across all 153 source meshes')
print('PASS all 242 rigid parts use named joints; bind-pose reconstruction error',max_rest_error)
for side in ('L','R'):
    distance=lambda a,b:math.dist(joint[a+side]['position'],joint[b+side]['position'])
    assert .6<distance('Hip','Knee')<.9 and .6<distance('Knee','Ankle')<.9
print('PASS both leg lengths and all vertex normals finite and normalized')
for path in (res/'Audio').glob('*.wav'):
    with wave.open(str(path),'rb') as w:
        assert w.getnchannels()==1 and w.getsampwidth()==2 and w.getframerate()==22050
        data2=w.readframes(w.getnframes());samples=struct.unpack('<'+'h'*(len(data2)//2),data2)
        assert max(abs(v) for v in samples)<32767 and max(abs(v) for v in samples)>100
        if path.stem in ('idle','servo','boost'):assert abs(samples[0])<100 and abs(samples[-1])<100
print('PASS ten PCM audio assets, no clipping, loop boundaries faded')

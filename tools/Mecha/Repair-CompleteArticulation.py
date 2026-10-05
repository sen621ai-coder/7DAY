"""Mechanical joint surgery on the fitted static sculpture.
Keep source surface positions and interpolate source UV/normal only where a joint
plane subdivides a triangle. Recessed cut-face liners stay inside the original rim.
Requires NumPy/SciPy. Repaired inputs rebuild from their verified source archive,
so rerunning does not accumulate liners or require an untracked local backup.
"""
import argparse,json,hashlib,collections
from pathlib import Path
import numpy as np
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import connected_components
p=argparse.ArgumentParser();p.add_argument('--source',type=Path,required=True);p.add_argument('--output',type=Path,required=True);args=p.parse_args()
stem='samurai_style_gundam_mecha';doc=json.loads((args.source/(stem+'_rig.json')).read_text());raw=(args.source/(stem+'_rig.bin')).read_bytes()
if 'articulationRepair' in doc:
 archive=doc.pop('articulationSourceParts');repair=doc.pop('articulationRepair')
 length=max(p['offset']+48*p['vertices']+4*p['indices'] for p in archive+doc.get('sourceOnlyParts',[]))
 raw=raw[:length]
 assert hashlib.sha256(raw).hexdigest()==repair['sourceBinarySha256'],'source archive hash mismatch'
 doc['parts']=archive
dt=np.dtype([('vertex','<f4',8),('bones','<i4',2),('weights','<f4',2)]);ids={j['name']:i for i,j in enumerate(doc['joints'])};pos={j['name']:np.array(j['position']) for j in doc['joints']};groups=collections.defaultdict(list);caps=collections.defaultdict(list);beforeArea=0.;afterArea=0.;capfaces=0;shieldShoulderFaces=0;gripFaces=0
keepParts=[]
body={'Torso','HipL','HipR','KneeL','KneeR','AnkleL','AnkleR','ShoulderL','ShoulderR','ElbowL','ElbowR','HandL','HandR'}
def role(b):
 return 'Leg'+b[-1] if b.startswith(('Hip','Knee','Ankle')) else 'Arm'+b[-1] if b.startswith(('Shoulder','Elbow','Hand')) else b

def area(v):return np.linalg.norm(np.cross(v[:,1,:3]-v[:,0,:3],v[:,2,:3]-v[:,0,:3]),axis=1).sum()*.5

def emit(poly,b,material,source=True):
 global afterArea
 if len(poly)<3:return
 faces=np.array([[poly[0],poly[i],poly[i+1]] for i in range(1,len(poly)-1)])
 valid=np.linalg.norm(np.cross(faces[:,1,:3]-faces[:,0,:3],faces[:,2,:3]-faces[:,0,:3]),axis=1)>1e-10;faces=faces[valid]
 if len(faces)==0:return
 if source:afterArea+=area(faces)
 records=np.zeros((len(faces),3),dt);records['vertex']=faces;records['bones']=ids[b];records['weights']=[1,0];groups[(material,role(b) if source else 'Liner_'+role(b))].append(records)

def split(poly,n,pivot,upper,lower,material,key):
 distances=poly[:,:3]@n-n@pivot
 if distances.min()>=-1e-7:return poly,None
 if distances.max()<=1e-7:return None,poly
 sides=[[],[]];intersections=[]
 for i in range(len(poly)):
  a=poly[i];b=poly[(i+1)%len(poly)];da=distances[i];db=distances[(i+1)%len(poly)]
  if da>=-1e-7:sides[0].append(a)
  if da<=1e-7:sides[1].append(a)
  if da*db< -1e-14:
   q=a+(b-a)*(da/(da-db));q[3:6]/=max(np.linalg.norm(q[3:6]),1e-12);sides[0].append(q);sides[1].append(q);intersections.append(q)
 if len(intersections)==2:
  caps[(upper,key,material)].append((intersections[0],intersections[1],pivot,n))
  caps[(lower,key,material)].append((intersections[1],intersections[0],pivot,-n))
 return tuple(np.array(s) if len(s)>=3 else None for s in sides)

for part in doc['parts']:
 v=np.frombuffer(raw,dt,part['vertices'],part['offset']);ix=np.frombuffer(raw,'<u4',part['indices'],part['offset']+part['vertices']*48).reshape(-1,3)
 faces=v['vertex'][ix].copy();beforeArea+=area(faces)
 if part.get('generatedRepair') or not (part['role'] in ('Torso','Shield','SwordHilt') or part['role'].startswith(('Arm','Leg'))):
  keepParts.append(dict(part));afterArea+=area(faces);continue
 # Preserve equipment and existing knee/weapon repairs byte-for-byte at surface level.
 for k,poly in enumerate(faces):
  b=doc['joints'][int(v['bones'][ix[k,0],0])]['name'];material=part['material'];center=poly[:,:3].mean(0);side='L' if center[0]<0 else 'R';sign=-1 if side=='L' else 1
  if part['role']=='Shield':
   # Detached upper shoulder crest was swallowed by the broad shield volume.
   if center[1]>2.53 and center[2]>.24:b='ShoulderL';shieldShoulderFaces+=1
   else:emit(poly,'Shield',material);continue
  if part['role']=='SwordHilt':
   grip=pos['HandR'];axis=np.array([.9407,2.1757,1.3423])-grip;axis/=np.linalg.norm(axis);along=(center-grip)@axis;radial=np.linalg.norm(center-grip-axis*along)
   if np.linalg.norm(center-grip)<.18 and -.12<along<.045 and radial>.055:b='HandR';gripFaces+=1
   else:
    records=np.zeros((1,3),dt);records['vertex']=poly;records['bones']=ids['Sword'];records['weights']=[1,0];groups[(material,'SwordHilt')].append(records);afterArea+=area(poly[None]);continue
  if part.get('generatedRepair') or b not in body:emit(poly,b,material);continue
  if b=='HandR' and part['role']=='SwordHilt':emit(poly,b,material);continue
  leg=b.startswith(('Hip','Knee','Ankle')) or (b=='Torso' and poly[:,1].min()<1.46 and abs(center[0])>.14)
  arm=b.startswith(('Shoulder','Elbow','Hand')) or (b=='Torso' and abs(center[0])>.50 and center[1]>2.20)
  if leg:
   upper,poly=split(poly,np.array([0,1,0]),pos['Hip'+side],'Torso','Hip'+side,material,'hip'+side)
   if upper is not None:emit(upper,'Torso',material)
   if poly is None:continue
   upper,poly=split(poly,np.array([0,1,0]),pos['Knee'+side],'Hip'+side,'Knee'+side,material,'knee'+side)
   if upper is not None:emit(upper,'Hip'+side,material)
   if poly is None:continue
   upper,lower=split(poly,np.array([0,1,0]),pos['Ankle'+side],'Knee'+side,'Ankle'+side,material,'ankle'+side)
   if upper is not None:emit(upper,'Knee'+side,material)
   if lower is not None:emit(lower,'Ankle'+side,material)
  elif arm:
   outward=poly
   n=pos['Shoulder'+side]-pos['Elbow'+side];n/=np.linalg.norm(n)
   upper,poly=split(outward,n,pos['Elbow'+side],'Shoulder'+side,'Elbow'+side,material,'elbow'+side)
   if upper is not None:emit(upper,'Shoulder'+side,material)
   if poly is None:continue
   n=pos['Elbow'+side]-pos['Hand'+side];n/=np.linalg.norm(n)
   upper,lower=split(poly,n,pos['Hand'+side],'Elbow'+side,'Hand'+side,material,'wrist'+side)
   if upper is not None:emit(upper,'Elbow'+side,material)
   if lower is not None:emit(lower,'Hand'+side,material)
  else:emit(poly,b,material)

# Closed, recessed liners follow each actual cut contour; no exterior barrel or box.
for (b,key,material),segments in caps.items():
 keys={};edges=[]
 for aa,cc,_,_ in segments:
  for q in [aa[:3],cc[:3]]:keys.setdefault(tuple(np.round(q,5)),len(keys))
  edges.append([keys[tuple(np.round(aa[:3],5))],keys[tuple(np.round(cc[:3],5))]])
 es=np.array(edges);g=coo_matrix((np.ones(len(es)),(es[:,0],es[:,1])),shape=(len(keys),len(keys)));_,component=connected_components(g,directed=False);positions=np.array(list(keys));centers={i:positions[component==i].mean(0) for i in np.unique(component)}
 for (a,c,pivot,normal),edge in zip(segments,es):
  center=centers[component[edge[0]]]
  # A narrow bevel retains rim UVs; the cavity uses the source graphite swatch.
  innerA=a.copy();innerC=c.copy();innerA[:3]=a[:3]*.92+center*.08+normal*.012;innerC[:3]=c[:3]*.92+center*.08+normal*.012
  mid=a.copy();mid[:3]=center+normal*.025;mid[6:8]=[.8672464,.88142943];innerA[6:8]=innerC[6:8]=mid[6:8]
  for tri in [[a,c,innerC],[a,innerC,innerA],[innerA,innerC,mid]]:
   tri=np.array(tri);n=np.cross(tri[1,:3]-tri[0,:3],tri[2,:3]-tri[0,:3]);length=np.linalg.norm(n)
   if length<1e-10:continue
   if np.dot(n,-normal)<0:tri=tri[[0,2,1]];n=-n
   tri[:,3:6]=n/length;tri[:,6:8]=[.8672464,.88142943];emit(tri,b,material,False);capfaces+=1

# Compact closed graphite hinge cores. Their radii stay below the adjacent
# armour widths; no new exterior pauldron, barrel, or coloured cover plate.
for side in ['L','R']:
 for joint,radii in [('Shoulder',(.105,.12,.105)),('Elbow',(.075,.085,.08)),('Hand',(.060,.065,.065)),('Hip',(.115,.105,.12)),('Ankle',(.080,.065,.095))]:
  b=joint+side;segments=16;bands=6;verts=[np.array([0,-1,0.])]
  for ring in range(1,bands):
   theta=np.pi*ring/bands
   for q in range(segments):
    phi=2*np.pi*q/segments;verts.append(np.array([np.sin(theta)*np.cos(phi),-np.cos(theta),np.sin(theta)*np.sin(phi)]))
  end=len(verts);verts.append(np.array([0,1,0.]));ix=[]
  for q in range(segments):ix.append([0,1+q,1+(q+1)%segments])
  for ring in range(bands-2):
   for q in range(segments):
    k=1+ring*segments+q;j=1+ring*segments+(q+1)%segments;ix.extend([[k,k+segments,j],[j,k+segments,j+segments]])
  for q in range(segments):ix.append([end,end-segments+(q+1)%segments,end-segments+q])
  points=np.array(verts)*np.array(radii)+pos[b];faces=points[np.array(ix)]
  if np.einsum('ij,ij->i',faces[:,0]-pos[b],np.cross(faces[:,1]-faces[:,0],faces[:,2]-faces[:,0])).sum()<0:faces=faces[:,[0,2,1]]
  for tri in faces:
   record=np.zeros((3,8));record[:,:3]=tri;n=(tri-pos[b])/np.array(radii)**2;n/=np.linalg.norm(n,axis=1,keepdims=True);record[:,3:6]=n;record[:,6:8]=[.8672464,.88142943];emit(record,b,0,False);capfaces+=1

assert abs(beforeArea-afterArea)<.0001,(beforeArea,afterArea)
blob=bytearray(raw);parts=keepParts.copy();counts=collections.Counter()
for part in keepParts:counts[part['role']]+=part['indices']//3
for (material,label),chunks in sorted(groups.items()):
 flat=np.concatenate(chunks).reshape(-1);unique,indices=np.unique(flat,return_inverse=True);offset=len(blob);blob.extend(unique.tobytes());blob.extend(indices.astype('<u4').tobytes());parts.append(dict(node=doc['sourceNodes'][0],nodeName='Complete_'+label,primitive=0,joint='Torso',role=label.removeprefix('Liner_'),material=material,offset=offset,vertices=len(unique),indices=len(indices),generatedRepair='recessed-joint-interior-v1' if label.startswith('Liner_') else None));counts[label]+=len(indices)//3
# Original payload remains archived for exact rollback / source provenance.
doc['articulationSourceParts']=doc['parts'];doc['parts']=parts;doc['triangles']=sum(p['indices']//3 for p in parts);doc['renderRoles']={r:sum(x['indices']//3 for x in parts if x['role']==r) for r in {x['role'] for x in parts}}
doc['articulationRepair']=dict(version=5,method='original surface joint-plane subdivision; recessed source-palette cut liners',sourceBinarySha256=hashlib.sha256(raw).hexdigest(),sourceArea=float(beforeArea),preservedSurfaceArea=float(afterArea),linerTriangles=capfaces,restoredShoulderFaces=shieldShoulderFaces,restoredGripFaces=gripFaces,skin='rigid')
args.output.mkdir(parents=True,exist_ok=True);(args.output/(stem+'_rig.json')).write_text(json.dumps(doc,indent=2));(args.output/(stem+'_rig.bin')).write_bytes(blob);print(doc['articulationRepair']);print('triangles',doc['triangles'])

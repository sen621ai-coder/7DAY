"""Reproducible local panel refinements and material maps; preserves source files.
Uses numpy/scipy/Pillow. Works on the exported canonical skin, not AI imagery.
"""
import argparse,hashlib,json,struct
from pathlib import Path
import numpy as np
from PIL import Image,ImageFilter
from scipy.ndimage import gaussian_filter,maximum_filter,minimum_filter
from scipy.spatial import ConvexHull

p=argparse.ArgumentParser();p.add_argument('--mod-root',type=Path,required=True);args=p.parse_args()
r=args.mod_root/'Resources/Justice';d=json.loads((r/'justice.json').read_text());raw=(r/'justice.bin').read_bytes()
source_hash=hashlib.sha256((r/'justice.glb').read_bytes()).hexdigest();assert source_hash==d['sourceSha256']
base=np.asarray(Image.open(r/'tex_0.png').convert('RGB'),dtype=np.float32)/255
normal=np.asarray(Image.open(r/'tex_2.png').convert('RGB'),dtype=np.float32)/255
emission=np.asarray(Image.open(r/'tex_1.png').convert('RGB'),dtype=np.float32)/255
chroma=base.max(2)-base.min(2);luma=base.mean(2);paint=(chroma>.13)|(luma>.57);metal=(~paint)&(luma>.16)&(luma<.56);rubber=(~paint)&(luma<=.16)
# Material response uses source pigment regions; no global dirt/noise overlay.
edge=np.sqrt(np.sum((normal[:,:,:2]-.5)**2,axis=2));seam=np.clip((maximum_filter(luma,7)-luma)*3,0,.35)
ao=np.clip(1-seam*.55-edge*.18,.68,1)
color=base.copy();color[paint]*=.96;color[metal]*=.90
color+=np.clip(edge-.10,0,.24)[:,:,None]*paint[:,:,None]*.07
color=np.clip(color,0,1)
smooth=np.where(paint,.43,np.where(metal,.58,.23));metallic=np.where(metal,.72,np.where(paint,.04,.02))
mask=np.dstack((metallic,ao,np.zeros_like(ao),smooth));n=normal*2-1;n/=np.maximum(np.linalg.norm(n,axis=2,keepdims=True),1e-6)
# RG-or-AG normal decoding: R=1, alpha=X, G=Y; native QA compares flat/on.
packed=np.dstack((np.ones_like(ao),(1-n[:,:,1])/2,(n[:,:,2]+1)/2,(n[:,:,0]+1)/2))
def png(name,array):Image.fromarray(np.round(np.clip(array,0,1)*255).astype('uint8')).save(r/name)
png('polish_albedo.png',color);png('polish_material.png',mask);png('polish_normal.png',packed);png('polish_emission.png',emission)

dtype=np.dtype([('v','<f4',3),('n','<f4',3),('uv','<f4',2),('j','<i4',4),('w','<f4',4)])
world=[]
for bone in d['bones']:
 m=np.eye(4);m[:3,3]=bone['trs'][:3];world.append((world[bone['parent']]@m) if bone['parent']>=0 else m)
bind=np.asarray(d['bindposes']).reshape(-1,4,4).transpose(0,2,1);skin=np.asarray(world)@bind;invskin=np.linalg.inv(skin)
nodebone={b['sourceNode']:i for i,b in enumerate(d['bones'])}
# Preserve authored tangent directions at surviving source vertices. Mirroring X
# changes handedness, and reversing texture V changes it back. The normal map
# green channel is reversed above so the transformed tangent frame agrees.
source=(r/'justice.glb').read_bytes();json_size=struct.unpack_from('<I',source,12)[0];g=json.loads(source[20:20+json_size]);blob=source[28+json_size:]
def accessor(i):
 a=g['accessors'][i];view=g['bufferViews'][a['bufferView']];dim={'VEC2':2,'VEC3':3,'VEC4':4}[a['type']];dt=np.dtype('<f4')
 return np.ndarray((a['count'],dim),dt,buffer=blob,offset=view.get('byteOffset',0)+a.get('byteOffset',0),strides=(view.get('byteStride',dim*4),4)).copy()
source_primitives=[x for mesh in g['meshes'] for x in mesh['primitives']]
maximum_source=max(abs(accessor(x['attributes']['POSITION'])).max() for x in source_primitives)
maximum_export=max(abs(np.frombuffer(raw,dtype,p['vertices'],p['offset'])['v']).max() for p in d['parts']);fit=maximum_export/maximum_source
source_tangents={};orthogonal_error=[]
def tangent_key(v,n,uv):return tuple(np.round(np.r_[v,n,uv].astype(float),5))
for primitive in source_primitives:
 if 'TANGENT' not in primitive['attributes']:continue
 at=primitive['attributes'];v=accessor(at['POSITION'])*[-fit,fit,fit];n=accessor(at['NORMAL'])*[-1,1,1];uv=accessor(at['TEXCOORD_0']);t=accessor(at['TANGENT']).astype(float);t[:,0]*=-1
 orthogonal_error.extend(abs(np.sum(t[:,:3]*n,axis=1)).tolist());t[:,:3]-=n*np.sum(t[:,:3]*n,axis=1)[:,None];t[:,:3]/=np.maximum(np.linalg.norm(t[:,:3],axis=1)[:,None],1e-9)
 for i in range(len(v)):source_tangents[tangent_key(v[i],n[i],uv[i])]=t[i]
jets=json.loads((Path(__file__).parent/'Justice-Polish-Nozzles.json').read_text(encoding='utf-8'))
records=[];audit=[];parts=[];preserved_tangents=0

allowed={'Torso','Head','ArmL','ArmR','LegL','LegR','Backpack','Weapon63','Weapon64'}
metal_yx=np.argwhere(metal&(luma>.28));metal_pixel=metal_yx[len(metal_yx)//2];metal_uv=np.array([metal_pixel[1]/2047,metal_pixel[0]/2047])

def tangent(v,n,uv,ix):
 t=np.zeros_like(v);b=np.zeros_like(v)
 for tri in ix:
  a,c,e=v[tri];u,q,z=uv[tri];du=q-u;dv=z-u;det=du[0]*dv[1]-du[1]*dv[0]
  if abs(det)<1e-10:continue
  s=((c-a)*dv[1]-(e-a)*du[1])/det;bit=((e-a)*du[0]-(c-a)*dv[0])/det
  t[tri]+=s;b[tri]+=bit
 t-=n*np.sum(t*n,axis=1)[:,None];length=np.linalg.norm(t,axis=1);bad=length<1e-8
 axis=np.tile([1.,0,0],(sum(bad),1));axis[np.abs(n[bad,0])>.8]=[0,0,1];t[bad]=np.cross(n[bad],axis);t/=np.maximum(np.linalg.norm(t,axis=1)[:,None],1e-8)
 sign=np.where(np.sum(np.cross(n,t)*b,axis=1)<0,-1,1)
 return np.c_[t,sign]

for pi,part in enumerate(d['parts']):
 a=np.frombuffer(raw,dtype,count=part['vertices'],offset=part['offset']).copy();front=np.frombuffer(raw,'<i4',part['indices'],part['offset']+part['vertices']*64).reshape(-1,3)[:part['indices']//6].copy()
 source_t=np.asarray([source_tangents.get(tangent_key(x['v'],x['n'],x['uv']),[0,0,0,0]) for x in a]);preserved_tangents+=int(sum(source_t[:,3]!=0));pos=a['v'].astype(float);norm=a['n'].astype(float);uv=a['uv'].astype(float);bones=a['j'];weights=a['w'];dominant=bones[:,0]
 rest=np.zeros_like(pos)
 for k in range(4):rest+=(np.einsum('nij,nj->ni',skin[bones[:,k]],np.c_[pos,np.ones(len(pos))])[:,:3])*weights[:,k,None]
 patches=[]
 if part['role'] in allowed:
  edges={};tri_norm=[];valid=[];parent=list(range(len(front)))
  def find(i):
   while parent[i]!=i:parent[i]=parent[parent[i]];i=parent[i]
   return i
  for ti,tr in enumerate(front):
   points=rest[tr];n=np.cross(points[1]-points[0],points[2]-points[0]);length=np.linalg.norm(n);n=n/max(length,1e-12);tri_norm.append(n)
   valid.append(length>1e-7 and np.min(weights[tr,0])>.999 and np.all(dominant[tr]==dominant[tr[0]]))
   for j in range(3):
    key=tuple(sorted((tuple(np.round(points[j],5)),tuple(np.round(points[(j+1)%3],5)))))
    for ot in edges.get(key,[]):
     if valid[ti] and valid[ot] and dominant[tr[0]]==dominant[front[ot,0]] and np.dot(n,tri_norm[ot])>.9998:parent[find(ti)]=find(ot)
    edges.setdefault(key,[]).append(ti)
  groups={}
  for ti in range(len(front)):
   if valid[ti]:groups.setdefault(find(ti),[]).append(ti)
  for triangles in groups.values():
   if len(triangles)<2:continue
   used=np.unique(front[triangles]);points=rest[used];center=points.mean(0);n=tri_norm[triangles[0]];u=points[1]-points[0];u-=n*np.dot(u,n);u/=max(np.linalg.norm(u),1e-9);v=np.cross(n,u);xy=np.c_[(points-center)@u,(points-center)@v]
   if min(np.ptp(xy,axis=0))<.025:continue
   try:hull=ConvexHull(xy)
   except Exception:continue
   area=sum(np.linalg.norm(np.cross(rest[front[t,1]]-rest[front[t,0]],rest[front[t,2]]-rest[front[t,0]]))*.5 for t in triangles)
   if area<.0012 or abs(hull.volume-area)>area*.015:continue
   loop=used[hull.vertices];xy=xy[hull.vertices];count=len(loop)
   if count>14:continue
   width=min(.004,min(np.ptp(xy,axis=0))*.06);inner=[]
   for j in range(count):
    prev=xy[j]-xy[j-1];nxt=xy[(j+1)%count]-xy[j];prev/=np.linalg.norm(prev);nxt/=np.linalg.norm(nxt);n1=np.array([-prev[1],prev[0]]);n2=np.array([-nxt[1],nxt[0]]);bis=n1+n2;bis/=max(np.linalg.norm(bis),1e-9);inner.append(xy[j]+bis*width/max(np.dot(bis,n1),.25))
   inner=np.asarray(inner)
   if np.any(np.linalg.norm(inner-xy,axis=1)>.02):continue
   uvfit=np.linalg.lstsq(np.c_[(rest[loop]-center)@u,(rest[loop]-center)@v,np.ones(count)],uv[loop],rcond=None)[0]
   patches.append((triangles,loop,center,n,u,v,xy,inner,uvfit,area))
  audit.append({'role':part['role'],'sourcePart':pi,'patches':[{'triangles':x[0],'bone':int(dominant[x[1][0]]),'area':x[-1],'bevelMeters':.003} for x in patches]})
 for lod in [0,1]:
  vv=list(pos);nn=list(norm);uu=list(uv);jj=list(bones);ww=list(weights);removed=set();newfaces=[]
  def vertex(point,n,tex,bone):
   rawpoint=(invskin[bone]@np.r_[point,1])[:3];rawn=invskin[bone,:3,:3]@n;rawn/=max(np.linalg.norm(rawn),1e-9);idx=len(vv);vv.append(rawpoint);nn.append(rawn);uu.append(tex);jj.append([bone,0,0,0]);ww.append([1,0,0,0]);return idx
  for triangles,loop,center,n,u,v,xy,inner,uvfit,area in patches:
   if lod==1 and area<.10:continue
   bone=int(dominant[loop[0]]);removed.update(triangles);count=len(loop);outids=[];inids=[]
   for j in range(count):
    q=center+u*inner[j,0]+v*inner[j,1]+n*.003
    inids.append(vertex(q,n,np.r_[inner[j],1]@uvfit,bone));outids.append(vertex(rest[loop[j]],n,uv[loop[j]],bone))
   for j in range(1,count-1):newfaces.append([inids[0],inids[j],inids[j+1]])
   for j in range(count):
    k=(j+1)%count;bn=np.cross((rest[loop[k]]-rest[loop[j]]),(np.asarray(vv[inids[j]])-pos[loop[j]]))
    # A separate chamfer normal prevents the broad face from looking inflated.
    direction=center-rest[loop[j]];direction-=n*np.dot(direction,n);direction/=max(np.linalg.norm(direction),1e-9);bevelnormal=n*.7-direction*.3;bevelnormal/=np.linalg.norm(bevelnormal)
    ids=[vertex(rest[loop[x]],bevelnormal,uv[loop[x]],bone) for x in (j,k)]+[vertex(center+u*inner[x,0]+v*inner[x,1]+n*.003,bevelnormal,np.r_[inner[x],1]@uvfit,bone) for x in (k,j)]
    newfaces.extend([[ids[0],ids[1],ids[2]],[ids[0],ids[2],ids[3]]])
   # Two recessed-looking fastener caps only on sizeable armour panels.
   if lod==0 and area>.018:
    for j in [0,count//2]:
     q=center+u*(inner[j,0]*.83)+v*(inner[j,1]*.83)+n*.004
     radius=min(.0038,np.sqrt(area)*.015);c=vertex(q,n,metal_uv,bone);ring=[]
     for k in range(8):ring.append(vertex(q+radius*(u*np.cos(k*np.pi/4)+v*np.sin(k*np.pi/4)),n,metal_uv,bone))
     for k in range(8):newfaces.append([c,ring[k],ring[(k+1)%8]])
  faces=[list(tr) for ti,tr in enumerate(front) if ti not in removed]+newfaces
  # Joint collars stay inside their existing armour envelopes and share its role.
  nodes={'LegL':[36,37],'LegR':[41,42],'ArmL':[17],'ArmR':[26],'Backpack':[67,70]}.get(part['role'],[])
  specs=[(node,None) for node in nodes]+([(d['bones'][j['bone']]['sourceNode'],j) for j in jets] if part['role']=='Backpack' else [])
  for node,nozzle in specs:
   bone=nodebone[node];center=world[bone][:3,3];axis=np.array([1.,0,0]);u=np.array([0.,1,0]);v=np.array([0.,0,1]);radius=.055 if node in [17,26,67,70] else .07;length=.08;segments=20 if lod==0 else 10;rings=[]
   if nozzle is not None:center=center+np.asarray(nozzle['position']);axis=np.asarray(nozzle['direction']);u=np.cross(axis,[0.,0,1]);u/=np.linalg.norm(u);v=np.cross(axis,u);radius=.075;length=.025
   for x,rad in [(-length/2,radius*.82),(-length*.35,radius),(length*.35,radius),(length/2,radius*.82)]:
    ring=[]
    for k in range(segments):
     radial=u*np.cos(2*np.pi*k/segments)+v*np.sin(2*np.pi*k/segments);ring.append(vertex(center+axis*x+radial*rad,radial,metal_uv,bone))
    rings.append(ring)
   for j in range(3):
    for k in range(segments):a0=rings[j][k];b0=rings[j][(k+1)%segments];c0=rings[j+1][(k+1)%segments];e0=rings[j+1][k];faces.extend([[a0,b0,c0],[a0,c0,e0]])
  vv=np.asarray(vv);nn=np.asarray(nn);uu=np.asarray(uu);uu[:,1]=1-uu[:,1];faces=np.asarray(faces,dtype='<i4');used,remap=np.unique(faces,return_inverse=True);vv=vv[used];nn=nn[used];uu=uu[used];jj=np.asarray(jj)[used];ww=np.asarray(ww)[used];faces=remap.reshape(-1,3).astype('<i4');tang=tangent(vv,nn,uu,faces);original=used<len(source_t);preserve=original.copy();preserve[original]=source_t[used[original],3]!=0;tang[preserve]=source_t[used[preserve]];allfaces=np.concatenate((faces,faces[:,[0,2,1]]));uu[:,1]=1-uu[:,1]
  records.append((pi,lod,vv,nn,uu,np.asarray(jj),np.asarray(ww),tang,allfaces))
with (r/'polish.bin').open('wb') as f:
 for pi,lod,v,n,uv,j,w,t,ix in records:
  offset=f.tell()
  for k in range(len(v)):f.write(struct.pack('<8f4i8f',*v[k],*n[k],*uv[k],*j[k],*w[k],*t[k]))
  f.write(ix.tobytes());parts.append({'sourcePart':pi,'lod':lod,'offset':offset,'vertices':len(v),'indices':ix.size})
manifest={'version':2,'vertexStride':80,'sourceSha256':source_hash,'geometry':'polish.bin','albedo':'polish_albedo.png','normal':'polish_normal.png','material':'polish_material.png','emission':'polish_emission.png','normalEncoding':'AG; X in alpha, inverted Y in green for flipped V, R=1','parts':parts,'patchAudit':audit,'sourceTangentAudit':{'matchedExportVertices':preserved_tangents,'originalMaxNormalDot':max(orthogonal_error),'conversion':'mirror tangent X; preserve w after mirror plus V flip; invert normal green; orthogonalize against normal'},'lodHeights':[.35,.12,0],'lodPolicy':{'nearMinPatchArea':.0012,'mediumMinPatchArea':.10,'far':'original double-sided mesh'}, 'jets':jets}
for lod,limit in [(0,60000),(1,20000)]:
 total=sum(p['indices']//3 for p in parts if p['lod']==lod);assert total<=limit,(lod,total);print('LOD',lod,'triangles',total)
(r/'polish.json').write_text(json.dumps(manifest,separators=(',',':')),encoding='utf-8');d['polishManifest']='polish.json';d['version']=2;(r/'justice.json').write_text(json.dumps(d,separators=(',',':')),encoding='utf-8')
print('Selected convex rigid patches',sum(len(x['patches']) for x in audit),'source GLB retained',source_hash)

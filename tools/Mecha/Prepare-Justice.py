"""Deterministic Justice GLB -> four-weight Unity skin and sampled local clips.
Requires numpy/scipy/Pillow; never changes source GLB or prototype resources.
"""
import argparse,copy,hashlib,json,struct
from pathlib import Path
import numpy as np
from scipy.spatial.transform import Rotation as R,Slerp
p=argparse.ArgumentParser();p.add_argument('--source',default='F:/llm/764f572a4f3b48e6ac7a0d49a6d9b4e8.glb');p.add_argument('--out',default=str(Path(__file__).resolve().parents[2]/'ZZ-PZAEC_Mecha/Resources/Justice'));args=p.parse_args()
out=Path(args.out);out.mkdir(parents=True,exist_ok=True);raw=Path(args.source).read_bytes();size=struct.unpack_from('<I',raw,12)[0];g=json.loads(raw[20:20+size]);blob=raw[28+size:]
def read(i):
 a=g['accessors'][i];v=g['bufferViews'][a['bufferView']];dt=np.dtype({5126:'<f4',5123:'<u2',5125:'<u4',5121:'u1'}[a['componentType']]);dim={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4,'MAT4':16}[a['type']]
 return np.ndarray((a['count'],dim),dt,buffer=blob,offset=v.get('byteOffset',0)+a.get('byteOffset',0),strides=(v.get('byteStride',dim*dt.itemsize),dt.itemsize)).copy()
nodes=g['nodes'];parents={c:i for i,n in enumerate(nodes) for c in n.get('children',[])};joints=g['skins'][0]['joints'];ji={n:i for i,n in enumerate(joints)}
tracks=[]
for a in g['animations']:
 ts=[]
 for c in a['channels']:
  s=a['samplers'][c['sampler']];assert s.get('interpolation','LINEAR')=='LINEAR';ts.append((c['target']['node'],c['target']['path'],read(s['input']).ravel(),read(s['output'])))
 tracks.append(ts)
def globals_at(ai,t,ground=False):
 vals={}
 if ai is not None:
  for n,k,times,v in tracks[ai]:
   at=np.clip(t,times[0],times[-1]);b=min(np.searchsorted(times,at,side='right'),len(times)-1);a=max(0,b-1);f=0 if a==b else (at-times[a])/(times[b]-times[a])
   if k=='rotation':
    q=v[b].copy();q*=1 if np.dot(v[a],q)>=0 else -1;x=v[a]*(1-f)+q*f;x=x/np.linalg.norm(x)
   else:x=v[a]*(1-f)+v[b]*f
   vals[n,k]=x
 world=[]
 for i,n in enumerate(nodes):
  if 'matrix' in n:m=np.array(n['matrix']).reshape(4,4,order='F')
  else:
   use=not(ground and i in [10,35,36,37,40,41,42])
   def value(k,default):return vals.get((i,k),n.get(k,default)) if use else n.get(k,default)
   m=np.eye(4);m[:3,:3]=R.from_quat(value('rotation',[0,0,0,1])).as_matrix()@np.diag(value('scale',[1,1,1]));m[:3,3]=value('translation',[0,0,0])
  world.append(world[parents[i]]@m if i in parents else m)
 return np.array(world)
base=globals_at(0,0,True);ibm=read(g['skins'][0]['inverseBindMatrices']).reshape(-1,4,4).transpose(0,2,1)
# Source geometry is preserved. Establish fit from actual skinned head/foot vertices.
prim=g['meshes'][0]['primitives'][0];at=prim['attributes'];pos=read(at['POSITION']);ids=read(at['JOINTS_0']).astype(int);weights=read(at['WEIGHTS_0']);hom=np.c_[pos,np.ones(len(pos))];rest=np.zeros_like(hom,dtype=float)
for k in range(4):rest+=np.einsum('nij,nj->ni',(base[joints]@ibm)[ids[:,k]],hom)*weights[:,k,None]
footmask=np.isin(np.array(joints)[ids[:,0]],[37,42]);headmask=np.array(joints)[ids[:,0]]==14
bottom=float(rest[footmask,1].min());top=float(rest[headmask,1].max());fit=3.2/(top-bottom)
B=np.diag([-fit,fit,fit,1]);Bi=np.linalg.inv(B);offset=np.array([0,-bottom*fit,0]);origin=np.eye(4);origin[:3,3]=offset
# Canonical axes at base pose let foot IK work independently of source bone roll.
canonical=[]
for n in joints:
 m=np.eye(4);m[:3,3]=(B@base[n])[:3,3]+offset;canonical.append(m)
canonical=np.array(canonical);invbase=np.linalg.inv(base[joints]);boneparents=[]
for n in joints:
 par=parents.get(n,-1)
 while par>=0 and par not in ji:par=parents.get(par,-1)
 boneparents.append(ji.get(par,-1))
# Keep the backpack mounted to the chest in authored game poses.
boneparents[ji[65]]=ji[12]
for child,parent in [(53,27),(55,18),(63,17),(64,27)]:boneparents[ji[child]]=ji[parent]
def sample(ai,t):
 w=globals_at(ai,t);return np.array([origin@B@w[n]@invbase[k]@Bi@np.linalg.inv(origin)@canonical[k] for k,n in enumerate(joints)])
def locals_of(w):
 result=[]
 for i,m in enumerate(w):
  m=np.linalg.inv(w[boneparents[i]])@m if boneparents[i]>=0 else m
  sc=np.linalg.norm(m[:3,:3],axis=0);rot=m[:3,:3]/np.maximum(sc,1e-12);q=R.from_matrix(rot).as_quat();result.append(np.r_[m[:3,3],q,sc])
 return np.array(result)
baseLocal=locals_of(canonical)
roles={10:'Torso',14:'Head',16:'ShoulderL',17:'ElbowL',18:'HandL',25:'ShoulderR',26:'ElbowR',27:'HandR',35:'HipL',36:'KneeL',37:'AnkleL',40:'HipR',41:'KneeR',42:'AnkleR',65:'Backpack',67:'WingL',70:'WingR',53:'Sword'}
# Foot sockets are measured from source skinned vertices, not the ankle joint origin.
sockets={}
for side,n in [('L',37),('R',42)]:
 vs=(rest[np.array(joints)[ids[:,0]]==n,:3]*[-fit,fit,fit])+offset
 sole=vs[vs[:,1]<vs[:,1].min()+.02];point=np.array([(sole[:,0].min()+sole[:,0].max())/2,vs[:,1].min(),(sole[:,2].min()+sole[:,2].max())/2]);sockets['Foot'+side]={'bone':ji[n],'position':(point-canonical[ji[n],:3,3]).tolist()}
meta={'version':1,'sourceSha256':hashlib.sha256(raw).hexdigest(),'height':3.2,'soleWidth':float(np.ptp(sole[:,0])+.01),'soleDepth':float(np.ptp(sole[:,2])+.01),'sourceTriangles':4567,'nodes':joints,'bones':[{'name':roles.get(n,'JusticeNode'+str(n)),'sourceNode':n,'parent':boneparents[i],'trs':baseLocal[i].tolist()} for i,n in enumerate(joints)],'sockets':sockets,'clips':[],'parts':[]}
with (out/'justice.bin').open('wb') as f:
 for mi,mesh in enumerate(g['meshes']):
  for pr in mesh['primitives']:
   a=pr['attributes'];v=read(a['POSITION']).astype(float);norm=read(a['NORMAL']);uv=read(a['TEXCOORD_0']);js=read(a['JOINTS_0']).astype(int);ws=read(a['WEIGHTS_0']);assert np.max(abs(ws.sum(axis=1)-1))<1e-4
   # One face group per major role permits optics and independent weapon visibility.
   ix=read(pr['indices']).ravel().reshape(-1,3);groups={}
   def role(n):
    if n in [28,29,30,51,52,53,54,55,56,57,58,59,60,61,62,63,64]:return 'Weapon'+str(n)
    if n==20:return 'Shield'
    while n not in [14,16,25,35,40,65,10] and n in parents:n=parents[n]
    return {14:'Head',16:'ArmL',25:'ArmR',35:'LegL',40:'LegR',65:'Backpack'}.get(n,'Torso')
   for tri in ix:
    sums=np.bincount(js[tri].ravel(),weights=ws[tri].ravel(),minlength=len(joints));r=role(joints[int(sums.argmax())]);groups.setdefault(r,[]).append(tri)
   for roleName,tri in groups.items():
    used,rev=np.unique(np.array(tri).ravel(),return_inverse=True);off=f.tell();vv=v[used]*[-fit,fit,fit];nn=norm[used]*[-1,1,1]
    for k,u in enumerate(used):f.write(struct.pack('<8f4i4f',*vv[k],*nn[k],*uv[u],*js[u],*ws[u]))
    # Reverse front winding for handedness and retain the source's double-sided material.
    faces=rev.reshape(-1,3)[:,[0,2,1]];faces=np.concatenate([faces,faces[:,[0,2,1]]]);f.write(faces.astype('<i4').tobytes())
    meta['parts'].append({'role':roleName,'material':pr.get('material',0),'offset':off,'vertices':len(used),'indices':faces.size})
 meta['bindposes']=[(np.linalg.inv(canonical[i])@origin@B@base[n]@ibm[i]@Bi).flatten(order='F').tolist() for i,n in enumerate(joints)]
 for ai,a in enumerate(g['animations']):
  duration=max(float(t[-1]) for _,_,t,_ in tracks[ai]);count=int(round(duration*60))+1;off=f.tell()
  for frame in range(count):f.write(locals_of(sample(ai,min(frame/60,duration))).astype('<f4').tobytes())
  meta['clips'].append({'name':a['name'],'offset':off,'frames':count,'fps':60,'duration':duration})
 # Game locomotion overlays, defined in canonical joint axes and phase locked to feet.
 for name,back,turn in [('Walk',1,0),('Reverse',-1,0),('TurnLeft',0,-1),('TurnRight',0,1)]:
  off=f.tell()
  for frame in range(61):
   t=frame/60;phase=t*2*np.pi;v=baseLocal.copy()
   for n,sgn in [(16,1),(25,-1)]:v[ji[n],3:7]=R.from_euler('xyz',[np.sin(phase)*9*back*sgn,0,0],degrees=True).as_quat()
   v[ji[10],:3]+=[np.sin(phase)*.035,.015*np.cos(phase*2),0];v[ji[10],3:7]=R.from_euler('xyz',[3*abs(back),np.sin(phase)*4*back+turn*np.sin(phase)*6,np.sin(phase)*2],degrees=True).as_quat()
   for n,sgn in [(35,1),(40,-1)]:v[ji[n],3:7]=R.from_euler('xyz',[np.sin(phase)*18*back*sgn,turn*np.sin(phase)*8,0],degrees=True).as_quat()
   f.write(v.astype('<f4').tobytes())
  meta['clips'].append({'name':name,'offset':off,'frames':61,'fps':60,'duration':1})
for i,image in enumerate(g['images']):
 view=g['bufferViews'][image['bufferView']];data=blob[view.get('byteOffset',0):view.get('byteOffset',0)+view['byteLength']];(out/f'tex_{i}.png').write_bytes(data)
# Socket positions from actual skinned weapon surfaces in the matching source pose.
def weapon_points(mesh_index,node,ai,t):
 pr=g['meshes'][mesh_index]['primitives'][0];a=pr['attributes'];ps=read(a['POSITION']);jj=read(a['JOINTS_0']).astype(int);ww=read(a['WEIGHTS_0']);mask=np.array(joints)[jj[:,0]]==node;ps=ps[mask];jj=jj[mask];ww=ww[mask];h=np.c_[ps,np.ones(len(ps))];world=globals_at(ai,t);v=np.zeros_like(h,dtype=float)
 for k in range(4):v+=np.einsum('nij,nj->ni',(world[joints]@ibm)[jj[:,k]],h)*ww[:,k,None]
 return (origin@B@v.T).T[:,:3]
sw=sample(1,2);beam=weapon_points(1,54,1,2);hand=sw[ji[27],:3,3];dist=np.linalg.norm(beam-hand,axis=1);root=beam[dist.argmin()];tip=beam[dist.argmax()]
meta['bladeRoot']=(np.linalg.inv(sw[ji[53]])@np.r_[root,1])[:3].tolist();meta['bladeTip']=(np.linalg.inv(sw[ji[53]])@np.r_[tip,1])[:3].tolist()
gw=sample(2,2);gun=weapon_points(0,64,2,2);hand=gw[ji[27],:3,3];muzzle=gun[np.linalg.norm(gun-hand,axis=1).argmax()];meta['gunMuzzle']=(np.linalg.inv(gw[ji[64]])@np.r_[muzzle,1])[:3].tolist()
meta['gunGrip']=(np.linalg.inv(gw[ji[64]])@np.r_[gw[ji[27],:3,3],1])[:3].tolist()
for c in meta['clips']:
 c['loop']=c['name'] in ['00-IDLE','Walk','Reverse','TurnLeft','TurnRight'];c['mask']='locomotion' if c['name'] in ['Walk','Reverse','TurnLeft','TurnRight'] else 'source-tracks'
meta['weaponVisibility']={'saber':[53,54,63],'rifle':[63,64]}
meta['combatStages']={'windEnd':.22,'cutEnd':.44,'brakeEnd':.56,'normalSeconds':1.35,'heavySeconds':1.45,'chargeSeconds':.8}
meta['equipment']={'sword':53,'blade':54,'shield':63,'rifle':64,'backpack':65,'eye':14,'seatHeight':2.1}
(out/'justice.json').write_text(json.dumps(meta,separators=(',',':')),encoding='utf8');(out/'justice.glb').write_bytes(raw)
print('Justice exported',len(joints),'bones',len(meta['parts']),'parts; fit',fit,'foot sockets',sockets)

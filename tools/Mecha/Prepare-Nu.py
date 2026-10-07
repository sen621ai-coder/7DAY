"""RX-93 native four-weight skin, canonical gameplay joints and complete-form motion retarget.
Source is immutable. The malformed WINGS primitive is fitted per weighted panel group;
all source triangles/UVs are retained. A separate beam saber is authored for gameplay.
"""
import argparse,json,struct,hashlib,copy,io
from pathlib import Path
import numpy as np
from scipy.spatial.transform import Rotation as R
from PIL import Image
p=argparse.ArgumentParser();p.add_argument('--source',default='F:/llm/rx-93_nu_gundam.glb');p.add_argument('--out',required=True);p.add_argument('--justice',required=True);args=p.parse_args()
out=Path(args.out);out.mkdir(parents=True,exist_ok=True)
raw=Path(args.source).read_bytes();z=struct.unpack_from('<I',raw,12)[0];g=json.loads(raw[20:20+z]);blob=raw[28+z:]
def read(i):
 a=g['accessors'][i];v=g['bufferViews'][a['bufferView']];dt=np.dtype({5126:'<f4',5123:'<u2',5125:'<u4',5121:'u1'}[a['componentType']]);d={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4,'MAT4':16}[a['type']];return np.ndarray((a['count'],d),dt,buffer=blob,offset=v.get('byteOffset',0)+a.get('byteOffset',0),strides=(v.get('byteStride',d*dt.itemsize),dt.itemsize)).copy()
nodes=g['nodes'];parents={c:i for i,n in enumerate(nodes) for c in n.get('children',[])};joints=g['skins'][0]['joints'];ji={n:i for i,n in enumerate(joints)}
ib=read(g['skins'][0]['inverseBindMatrices']).reshape(-1,4,4).transpose(0,2,1);bind=np.linalg.inv(ib);cent=(bind[ji[92],:3,3]+bind[ji[98],:3,3])/2
prims=[pr for m in g['meshes'] for pr in m['primitives']]
foot=[];head=[]
for pr in prims:
 a=pr['attributes'];v=read(a['POSITION']);ids=read(a['JOINTS_0']);dominant=np.array(joints)[ids[np.arange(len(ids)),np.argmax(read(a['WEIGHTS_0']),axis=1)]]
 foot.extend(v[np.isin(dominant,[95,96,101,102])]);head.extend(v[np.isin(dominant,[27,28,30])])
bottom=np.array(foot)[:,1].min();top=np.array(head)[:,1].max();fit=3.2/(top-bottom);scale=np.array([-fit,fit,fit]);shift=-cent*scale;shift[1]=-bottom*fit
pos=bind[:,:3,3]*scale+shift
# Semantic sourceNode aliases are the animation interface, not original GLB ids.
alias={24:10,25:11,26:12,27:14,32:15,33:16,34:17,35:18,62:24,63:25,64:26,65:27,92:35,93:36,95:37,98:40,99:41,101:42,86:53,56:63,88:64,104:65,113:67,115:70}
names={24:'Torso',27:'Head',33:'ShoulderL',34:'ElbowL',35:'HandL',63:'ShoulderR',64:'ElbowR',65:'HandR',92:'HipL',93:'KneeL',95:'AnkleL',98:'HipR',99:'KneeR',101:'AnkleR',86:'Sword',104:'Backpack',113:'WingL',115:'WingR'}
# Missing source saber has no weighted surface; use its existing joint for an authored emitter.
pos[ji[86]]=pos[ji[65]]
bp=[]
for n in joints:
 par=parents.get(n,-1)
 while par>=0 and par not in ji:par=parents.get(par,-1)
 bp.append(ji.get(par,-1))
# Canonical pelvis owns both legs; original IK controllers remain archived in the rig.
for n,par in [(92,24),(98,24),(86,65),(88,65),(56,34),(104,26),(113,104),(115,104)]:bp[ji[n]]=ji[par]
# Root torso is the visual locomotion root; keep model-space coordinates on it.
bp[ji[24]]=-1
bones=[]
for i,n in enumerate(joints):
 local=pos[i]-(pos[bp[i]] if bp[i]>=0 else 0)
 bones.append(dict(name=names.get(n,'NuNode'+str(n)),sourceNode=alias.get(n,-1000-n),originalNode=n,parent=bp[i],trs=[*local,0,0,0,1,1,1,1]))
# Beam node is independent from hilt for visibility, scaled like the existing combat interface.
joints.append(-54);ji[-54]=len(bones);pos=np.vstack([pos,pos[ji[86]]]);bp.append(ji[86]);bones.append(dict(name='NuBeam',sourceNode=54,originalNode=-1,parent=ji[86],trs=[0,0,0,0,0,0,1,.01,.01,.01]))
# Runtime requires parents before children; remap preserving all original joints.
order=[]
def visit(i):
 if i in order:return
 if bp[i]>=0:visit(bp[i])
 order.append(i)
for i in range(len(bones)):visit(i)
remap={old:new for new,old in enumerate(order)};bones=[bones[i] for i in order]
for b in bones:b['parent']=remap.get(b['parent'],-1)
pos=pos[order];ji={n:remap[i] for n,i in ji.items()};base=np.array([b['trs'] for b in bones],float)
meta=dict(version=1,sourceSha256=hashlib.sha256(raw).hexdigest(),height=3.2,bones=bones,parts=[],clips=[],sockets={},materials=[],sourceTriangles=sum(len(read(p['indices']))//3 for p in prims),sourceJointCount=123,retarget='Complete-form canonical rotation tracks; Nu bind translations',wingRepair={'method':'per-panel similarity fit; source mesh coordinates malformed','panelHeightMeters':1.9},weaponVisibility={'saber':[53,54,63],'rifle':[63,64]},bladeRoot=[0,.13,0],bladeTip=[0,1.65,0])
allFoot={}
for side,ids in [('L',[95,96]),('R',[101,102])]:
 vertices=[]
 for pr in prims:
  a=pr['attributes'];js=read(a['JOINTS_0']);ws=read(a['WEIGHTS_0']);dom=np.array(g['skins'][0]['joints'])[js[np.arange(len(js)),ws.argmax(1)]];vertices.extend(read(a['POSITION'])[np.isin(dom,ids)]*scale+shift)
 v=np.array(vertices);sole=v[v[:,1]<v[:,1].min()+.025];pt=np.array([(sole[:,0].min()+sole[:,0].max())/2,v[:,1].min(),(sole[:,2].min()+sole[:,2].max())/2]);allFoot[side]=v
 meta['sockets']['Foot'+side]={'bone':ji[ids[0]],'position':(pt-pos[ji[ids[0]]]).tolist()}
meta['soleWidth']=max(float(np.ptp(v[:,0])) for v in allFoot.values())+.01;meta['soleDepth']=max(float(np.ptp(v[:,2])) for v in allFoot.values())+.01
# Translate bound foot geometry by tiny original asymmetry? Retain source geometry; IK levels both soles.
def role(n):
 if n in [56,57]:return 'Weapon63'
 if n in [88,89]:return 'Weapon64'
 if n in [86,87]:return 'Weapon53'
 if n in range(104,117):return 'Backpack'
 if n in range(27,32):return 'Head'
 if n in range(32,62):return 'ArmL'
 if n in range(62,92):return 'ArmR'
 if n in range(92,98):return 'LegL'
 if n in range(98,104):return 'LegR'
 return 'Torso'
normalScale=np.array([-1,1,1]);sourceJoints=np.array(g['skins'][0]['joints']);gunPoints=[];eyePoints=[]
with (out/'nu.bin').open('wb') as f:
 pending={}
 def write_part(v,n,uv,ids,ws,ix,mat,roleName):
  pending.setdefault((mat,roleName),[]).append((v,n,uv,ids,ws,ix))
 def flush_parts():
  for (mat,roleName),pieces in pending.items():
   v=np.concatenate([q[0] for q in pieces]);n=np.concatenate([q[1] for q in pieces]);uv=np.concatenate([q[2] for q in pieces]);ids=np.concatenate([q[3] for q in pieces]);ws=np.concatenate([q[4] for q in pieces]);indices=[];offset=0
   for q in pieces:indices.extend(np.array(q[5])+offset);offset+=len(q[0])
   ix=np.asarray(indices).reshape(-1,3);ix=np.concatenate([ix,ix[:,[0,2,1]]]).ravel()
   meta['parts'].append(dict(role=roleName,material=mat,offset=f.tell(),vertices=len(v),indices=len(ix)))
   for k in range(len(v)):f.write(struct.pack('<8f4i4f',*v[k],*n[k],*uv[k],*ids[k],*ws[k]))
   f.write(np.asarray(ix,dtype='<i4').tobytes())
 for pr in prims:
  a=pr['attributes'];v=read(a['POSITION']).astype(float);n=read(a['NORMAL']).astype(float);uv=read(a['TEXCOORD_0']);oldids=read(a['JOINTS_0']).astype(int);ws=read(a['WEIGHTS_0']);ix=read(pr['indices']).ravel().reshape(-1,3);dom=sourceJoints[oldids[np.arange(len(oldids)),ws.argmax(1)]]
  assert np.max(abs(ws.sum(1)-1))<1e-4
  if pr['material']==9:
   # Two rigid weighted assemblies; preserve their relative geometry and panel aspect ratio.
   for node in [113,115]:
    mask=dom==node;vv=v[mask];low=vv.min(0);high=vv.max(0);center=(low+high)/2;s=1.9/np.max(high-low)
    vv=(vv-center)*s;vv[:,0]*=-1
    # Source panel coordinates use Y for the long dimension. Back mounted with top above shoulders.
    vv+=pos[ji[104]]+np.array([(-.47 if node==113 else .47),.20,-.36])
    v[mask]=vv
   n*=normalScale
  else:v=v*scale+shift;n*=normalScale
  ids=np.array([[ji[int(sourceJoints[x])] for x in row] for row in oldids],np.int32)
  groups={}
  for face in ix:
   rr=role(int(dom[face[0]]));groups.setdefault(rr,[]).append(face)
  for rr,faces in groups.items():
   faces=np.array(faces);used=np.unique(faces);lookup=np.full(len(v),-1,int);lookup[used]=np.arange(len(used));idx=lookup[faces][:,[0,2,1]].ravel()
   write_part(v[used],n[used],uv[used],ids[used],ws[used],idx,pr['material'],rr)
   if rr=='Weapon64':gunPoints.extend(v[used])
   if rr=='Head' and pr['material']==6:eyePoints.extend(v[used])
 # Beam and grip cylinders, both share the sampled sword anchors.
 for rr,lo,hi,radius,mat,bone in [('Weapon53',-.12,.13,.035,5,ji[86]),('Weapon54',.13,1.65,.026,11,ji[-54])]:
  vs=[];ns=[];uv=[]
  for y in [lo,hi]:
   for k in range(12):
    a=k*np.pi/6;vs.append(pos[ji[86]]+np.array([radius*np.cos(a),y,radius*np.sin(a)])/(100 if rr=='Weapon54' else 1));ns.append([np.cos(a),0,np.sin(a)]);uv.append([k/12,y])
  faces=[]
  for k in range(12):q=(k+1)%12;faces.extend([k,12+q,q,k,12+k,12+q])
  ids=np.zeros((24,4),int);ids[:,0]=bone;ws=np.zeros((24,4));ws[:,0]=1
  write_part(np.array(vs),np.array(ns),uv,ids,ws,faces,mat,rr)
 flush_parts()
 # Existing motion rotations retarget to Nu neutral axes; preserve Nu limb lengths and offsets.
 jd=json.loads((Path(args.justice)/'justice.json').read_text());jb=(Path(args.justice)/'justice.bin').read_bytes();jmap={b['sourceNode']:i for i,b in enumerate(jd['bones'])};jbase=np.array([b['trs'] for b in jd['bones']])
 for clip in jd['clips']:
  if clip['name'] in ['04-BOOMERANG','05-LIFTER']:continue
  src=np.frombuffer(jb,dtype='<f4',count=clip['frames']*len(jd['bones'])*10,offset=clip['offset']).reshape(clip['frames'],len(jd['bones']),10)
  frames=np.repeat(base[None,:,:],clip['frames'],axis=0)
  for i,b in enumerate(bones):
   n=b['sourceNode']
   if n not in jmap:continue
   k=jmap[n]
   # Weapon/pack positions remain on Nu sockets; do not import Justice equipment trajectories.
   if n not in [53,54,63,64,65,67,70]:frames[:,i,3:7]=src[:,k,3:7]
   if n==10:frames[:,i,:3]+=src[:,k,:3]-jbase[k,:3]
  # Nu sword points forward/down at rest; blade hierarchy needs identity local rotation.
  for frameIndex in range(clip['frames']):
   time=frameIndex/clip['fps']
   if clip['name']=='01-SABER':
    # Author a complete forward cut, not the source showcase pause.
    angles=np.array([25,0,15]) if time<=1.3 else np.array([-135,0,15]) if time<=1.55 else np.array([35,0,20])
    if 1.3<time<1.55:angles=np.array([25,0,15])+(np.array([-135,0,15])-np.array([25,0,15]))*(time-1.3)/.25
    if 1.55<time<2.3:angles=np.array([-135,0,15])+(np.array([35,0,20])-np.array([-135,0,15]))*(time-1.55)/.75
    frames[frameIndex,ji[63],3:7]=R.from_euler('xyz',angles,degrees=True).as_quat()
    frames[frameIndex,ji[64],3:7]=R.from_euler('x',-25,degrees=True).as_quat()
    desired=130 if time<=1.3 else (130-120*min((time-1.3)/.25,1)) if time<=1.55 else 10+120*min((time-1.55)/.75,1)
    frames[frameIndex,ji[86],3:7]=R.from_euler('x',desired-angles[0]+25,degrees=True).as_quat()
   if clip['name']=='03-SHILD':
    frames[frameIndex,ji[33],3:7]=R.from_euler('xyz',[-60,-25,-15],degrees=True).as_quat()
    frames[frameIndex,ji[34],3:7]=R.from_euler('xyz',[-50,0,0],degrees=True).as_quat()
   # Nu panels remain attached; subtly fan out under the same flight deployment signal.
   if clip['name']=='02-RIFLE':
    for node,sign in [(113,-1),(115,1)]:frames[frameIndex,ji[node],3:7]=R.from_euler('z',sign*8*min(time/2,1),degrees=True).as_quat()
  c=copy.deepcopy(clip);c['offset']=f.tell();f.write(frames.astype('<f4').tobytes());meta['clips'].append(c)
meta['eyeOffset']=(np.array(eyePoints).mean(0)-pos[ji[27]]+[0,0,.04]).tolist();meta['missileMuzzle']=[0,.9,.05]
# Geometry is in model space with canonical identity bind orientation.
meta['bindposes']=[]
for v in pos:
 m=np.eye(4);m[:3,3]=-v;meta['bindposes'].append(m.flatten(order='F').tolist())
gun=np.array(gunPoints);grip=pos[ji[65]];muzzle=gun[gun[:,1]<=gun[:,1].min()+.015].mean(0);meta['gunGrip']=(grip-pos[ji[88]]).tolist();meta['gunMuzzle']=(muzzle-pos[ji[88]]).tolist()
# Original 4K images remain in the GLB. Runtime images are 2K; masks use Unity channel conventions.
for i,im in enumerate(g['images']):
 bv=g['bufferViews'][im['bufferView']];data=blob[bv.get('byteOffset',0):bv.get('byteOffset',0)+bv['byteLength']];im=Image.open(io.BytesIO(data));im.thumbnail((2048,2048),Image.Resampling.LANCZOS);im.save(out/f'tex_{i}.png')
for i,m in enumerate(g['materials']):
 pm=m.get('pbrMetallicRoughness',{});d={'name':m['name'],'color':pm.get('baseColorFactor',[1,1,1,1]),'metallic':pm.get('metallicFactor',1),'roughness':pm.get('roughnessFactor',1),'emission':m.get('emissiveFactor',[0,0,0]),'doubleSided':m.get('doubleSided',False)}
 d['emission']=(np.array(d['emission'])*m.get('extensions',{}).get('KHR_materials_emissive_strength',{}).get('emissiveStrength',1)).tolist()
 for key,ref in [('albedo',pm.get('baseColorTexture')),('normal',m.get('normalTexture'))]:d[key]=f"tex_{g['textures'][ref['index']]['source']}.png" if ref else None
 if 'metallicRoughnessTexture' in pm:
  idx=g['textures'][pm['metallicRoughnessTexture']['index']]['source'];a=np.asarray(Image.open(out/f'tex_{idx}.png').convert('RGBA')).copy();r=np.zeros_like(a);r[:,:,0]=(a[:,:,2].astype(float)*d['metallic']).astype('uint8');r[:,:,1]=255;r[:,:,3]=255-(a[:,:,1].astype(float)*d['roughness']).astype('uint8');file=f'metallic_{i}.png';Image.fromarray(r).save(out/file);d['mask']=file
 if d['normal']:
  a=np.asarray(Image.open(out/d['normal']).convert('RGBA')).copy();normal=np.full_like(a,255);normal[:,:,1]=255-a[:,:,1];normal[:,:,3]=a[:,:,0];file=f'normal_{i}.png';Image.fromarray(normal).save(out/file);d['normal']=file
 meta['materials'].append(d)
meta['materials'].append(dict(name='Nu gameplay beam',color=[.05,.01,.03,1],metallic=0,roughness=.2,emission=[3,.2,1.5],doubleSided=True))
(out/'nu.glb').write_bytes(raw);(out/'nu.json').write_text(json.dumps(meta,separators=(',',':'),default=lambda x:x.item()),encoding='utf8')
(out/'ATTRIBUTION.txt').write_text('RX-93 Nu Gundam by Ryanwill679 (https://sketchfab.com/Ryanwill679)\nhttps://sketchfab.com/3d-models/rx-93-nu-gundam-85c329a2565043c58999afd43506c9a9\nCC BY 4.0 https://creativecommons.org/licenses/by/4.0/\nModified: canonical gameplay rig, animation retarget, fitted malformed wing panels, generated beam saber. Original source and hash retained.\nMotion basis: Justice Gundam by nama_140, CC BY 4.0; adapted gameplay motion by PZAEC.\n',encoding='utf8')
print('Nu exported',meta['sourceTriangles'],'source triangles',len(bones),'bones',len(meta['parts']),'parts','sole',meta['soleWidth'],meta['soleDepth'])

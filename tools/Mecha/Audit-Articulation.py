"""Audit shipped rigid skin against native pose exports. Seam separation is a review
signal, NOT proof of an exposed hole: mechanical overlap and hatch motion are allowed.
No invented animation or AABB collision verdicts are used here.
"""
import argparse,itertools,json,struct
from pathlib import Path
import numpy as np
p=argparse.ArgumentParser();p.add_argument('poses',nargs='+',type=Path);p.add_argument('--output',required=True,type=Path);args=p.parse_args()
root=Path(__file__).resolve().parents[2];res=root/'ZZ-PZAEC_Mecha/Resources'
d=json.loads((res/'samurai_style_gundam_mecha_rig.json').read_text());raw=(res/'samurai_style_gundam_mecha_rig.bin').read_bytes()
dt=np.dtype([('v','<f4',8),('b','<i4',2),('w','<f4',2)]);rest=np.array([j['position'] for j in d['joints']]);names=[j['name'] for j in d['joints']]
# Inspect GLB animation metadata as distinct from our derived rig.
glb=(res/'samurai_style_gundam_mecha.glb').read_bytes();n=struct.unpack_from('<I',glb,12)[0];source=json.loads(glb[20:20+n]);groups={};triangles=0;bad=0;bone_counts={}
for part in d['parts']:
 v=np.frombuffer(raw,dt,part['vertices'],part['offset']);ix=np.frombuffer(raw,'<u4',part['indices'],part['offset']+part['vertices']*48).reshape(-1,3);triangles+=len(ix)
 bad+=int(np.count_nonzero(~(np.all(v['b'][ix][:,:,0]==v['b'][ix[:,0],0,None],axis=1))))
 assert np.all(v['b'][:,0]==v['b'][:,1]) and np.all(v['w']==[1,0]),'non-rigid armour weights'
 for b in np.unique(v['b'][:,0]):
  bone_counts[names[b]]=bone_counts.get(names[b],0)+int(np.count_nonzero(v['b'][:,0]==b))
  if not part.get('generatedRepair'):
   for q in np.unique(np.round(v['v'][v['b'][:,0]==b,:3],5),axis=0):groups.setdefault(tuple(q),set()).add(int(b))
assert bad==0,'faces split across bones'
pairs={}
for q,bones in groups.items():
 for a,b in itertools.combinations(sorted(bones),2):pairs.setdefault((a,b),[]).append(q)
poses=[]
for sourcepath in args.poses:
 for item in json.loads(sourcepath.read_text(encoding='utf-8-sig')):item['source']=str(sourcepath);poses.append(item)
mat=np.array([x['bones'] for x in poses]).reshape(len(poses),-1,3,4)
assert np.isfinite(mat).all();identity=np.eye(3);orth=float(np.max(np.abs(mat[:,:,:,:3]@mat[:,:,:,:3].transpose(0,1,3,2)-identity)))
rows=[];picked={}
for (a,b),q in pairs.items():
 q=np.array(q);pa=np.einsum('nij,kj->nki',mat[:,a,:,:3],q-rest[a])+mat[:,a,:,3,None].transpose(0,2,1);pb=np.einsum('nij,kj->nki',mat[:,b,:,:3],q-rest[b])+mat[:,b,:,3,None].transpose(0,2,1)
 distance=np.linalg.norm(pa-pb,axis=2);score=np.quantile(distance,.95,axis=1);worst=int(score.argmax());vertex=int(distance[worst].argmax());pose=poses[worst]
 row=dict(pair=[names[a],names[b]],sharedSeamPoints=len(q),p95Separation=float(score[worst]),maxSeparation=float(distance[worst,vertex]),action=pose['action'],frame=pose['frame'],source=pose['source'],pointA=pa[worst,vertex].tolist(),pointB=pb[worst,vertex].tolist())
 rows.append(row)
 if len(q)>=25:
  selected=dict(pose);selected['auditPair']=row['pair'];selected['focus']=((pa[worst,vertex]+pb[worst,vertex])/2).tolist();picked[names[a]+'-'+names[b]]=selected
for action in ['complete-rest','complete-walk','flight-hover','flight-boost','flight-landing','flight-power-loss','weight-entry','weight-exit','samurai-guard','traversal-up-yaw0-side0','traversal-down-yaw0-side0','traversal-gap-yaw0-side0']:
 matches=[x for x in poses if x['action']==action]
 if matches:
  selected=dict(matches[len(matches)//2]);selected['auditPair']=['action',action];selected['focus']=[0,1.4,0];picked['action-'+action]=selected
rows.sort(key=lambda x:-x['p95Separation']);args.output.mkdir(parents=True,exist_ok=True)
report=dict(sourceGLBAnimations=len(source.get('animations',[])),sourceGLBSkins=len(source.get('skins',[])),derivedJoints=len(names),renderedTriangles=triangles,boneVertices=bone_counts,poses=len(poses),actions=sorted(set(x['action'] for x in poses)),matrixOrthonormalError=orth,splitFaces=bad,seams=rows,verdict='REVIEW REQUIRED: seam distances are not exposed-hole widths; inspect native solid renders. This audit does not certify absence of all collisions.')
(args.output/'articulation-audit.json').write_text(json.dumps(report,indent=2));(args.output/'review-poses.json').write_text(json.dumps(list(picked.values()),indent=2))
print('source animations',report['sourceGLBAnimations'],'derived joints',len(names),'rigid triangles',triangles,'native poses',len(poses),'actions',len(report['actions']),'matrix error',orth)
for r in rows:print('/'.join(r['pair']),r['sharedSeamPoints'],'p95 %.3fm'%r['p95Separation'],r['action'],r['frame'])


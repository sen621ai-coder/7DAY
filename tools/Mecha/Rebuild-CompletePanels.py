"""Repartition the original rigid sculpture into complete armour panels.
Source attributes are retained; graph regularization routes seams along creases.
Build into an isolated output first, then run native visual acceptance.
"""
import argparse,collections,hashlib,json
from pathlib import Path
import numpy as np
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import maximum_flow,breadth_first_order,connected_components
from scipy.spatial import cKDTree
p=argparse.ArgumentParser();p.add_argument('--source',required=True,type=Path);p.add_argument('--output',required=True,type=Path);a=p.parse_args();stem='samurai_style_gundam_mecha'
d=json.loads((a.source/(stem+'_rig.json')).read_text());raw=(a.source/(stem+'_rig.bin')).read_bytes()
source=d.get('panelSourceParts',d.get('articulationSourceParts',d['parts']))
archiveEnd=max(part['offset']+48*part['vertices']+4*part['indices'] for part in source+d.get('sourceOnlyParts',[]));raw=raw[:archiveEnd]
d.pop('articulationRepair',None);d.pop('articulationSourceParts',None)
dt=np.dtype([('vertex','<f4',8),('bones','<i4',2),('weights','<f4',2)])
names=[j['name'] for j in d['joints']];ids={n:i for i,n in enumerate(names)};positions=np.array([j['position'] for j in d['joints']]);parts=[];f=[];partids=[]
for i,part in enumerate(source):
 if part.get('generatedRepair')=='mirrored-intact-wing-v1':continue
 v=np.frombuffer(raw,dt,part['vertices'],part['offset']);ix=np.frombuffer(raw,'<u4',part['indices'],part['offset']+48*part['vertices']).reshape(-1,3)
 parts.append(part);f.append(v[ix].copy());partids.extend([len(parts)-1]*len(ix))
f=np.concatenate(f);partids=np.array(partids);q=f['vertex'][:,:,:3];c=q.mean(1);x,y,z=c.T;bone=f['bones'][:,0,0].copy();initial=bone.copy();generated=np.array([bool(parts[i].get('generatedRepair')) for i in partids]);roles=np.array([parts[i]['role'] for i in partids]);protected=generated|np.isin(roles,['Head','SwordBlade'])
# Preserve independently verified weapon grip and left pauldron corrections.
mask=(roles=='Shield')&(y>2.40);bone[mask]=ids['ShoulderL']
# The shield is the oblique slab outside the forearm, not the complete volume
# containing the left fist and elbow. Recover the inner-side armour first.
shieldArm=(roles=='Shield')&(y<=2.40)&(x>-.91)
bone[shieldArm]=ids['ElbowL']
grip=positions[ids['HandR']];axis=np.array([.9407,2.1757,1.3423])-grip;axis/=np.linalg.norm(axis);along=(c-grip)@axis;radial=np.linalg.norm(c-grip-axis*along[:,None],axis=1)
# The broad archived hilt volume reaches into the upper forearm, not just the
# fist. Keep the actual narrow grip core and forward pommel on the sword;
# recover the surrounding rear-facing armour before the anatomical arm cuts.
hand=(roles=='SwordHilt')&(np.linalg.norm(c-grip,axis=1)<.36)&(along>-.22)&(along<.045)&(radial>.055);bone[hand]=ids['HandR'];protected|=(roles=='SwordHilt')|((roles=='Shield')&~(mask|shieldArm))
# The old blade/hilt role split was itself spatial: its proximal 12 cm cutoff
# also painted part of the gauntlet with BladeFinish and pulled it with Sword.
# Recover only that body-side overlap; the verified blade core/trim is outside
# this envelope and retains its original Sword ownership.
bladeArm=(roles=='SwordBlade')&~generated&(np.linalg.norm(c-grip,axis=1)<.38)&(radial>.08)&(y>1.64)&(z>.45)
bone[bladeArm]=ids['HandR'];protected[bladeArm]=False
# Rear ornaments are complete swept plates, not cross-sections of thigh/shin.
# Use the unoccluded left assembly as the symmetric template only AFTER its
# root, mid-span and lower tip have all been recovered from adjacent labels.
wing=(x<-.28)&(y<1.93)&(z<-.20)&~protected
bone[wing]=ids['WingL']
for side,sign in [('L',-1),('R',1)]:
 half=x*sign>0
 # Whole arm includes torso-owned upper-arm shell and pauldron base.
 oldarm=np.isin(bone,[ids[k+side] for k in ['Shoulder','Elbow','Hand']])
 # The left forearm rests beside the torso; its back is 18 cm farther back
 # than the raised right forearm. A symmetric depth gate amputates that shell.
 armBack=.12 if side=='L' else .30
 armInside=np.full(len(f),.585)
 bone[oldarm&(((y<1.85)&(z<armBack))|((y<2.26)&(abs(x)<armInside)))&~protected]=ids['Torso']
 arm=half&(abs(x)>np.where(y<2.26,armInside,.43))&(y>1.59)&((z>armBack)|(y>2.20))&~protected&~wing
 bone[arm]=ids['Shoulder'+side]
 for joint,upper in [('Elbow','Shoulder'),('Hand','Elbow')]:
  pivot=positions[ids[joint+side]];n=positions[ids[upper+side]]-pivot;n/=np.linalg.norm(n)
  bone[arm&((c-pivot)@n<0)]=ids[joint+side]
 # Keep the waist skirt on the pelvis; do not carry long side/rear ornaments
 # through the knee plane. Separate only the load-bearing leg column.
 leg=half&(y<1.45)&~protected&~wing&~arm
 skirt=leg&(y>.95)&((abs(x)>.61)|(z<-.12))
 bone[skirt]=ids['Torso']
 load=leg&~skirt
 bone[load]=ids['Hip'+side]
 bone[load&(y<positions[ids['Knee'+side],1])]=ids['Knee'+side]
 bone[load&(y<positions[ids['Ankle'+side],1])]=ids['Ankle'+side]
# Spatial provenance is a proposal. A crease-weighted cut removes alternating
# triangle ownership at its border while protecting the centres of each panel.
n=np.cross(q[:,1]-q[:,0],q[:,2]-q[:,0]);area=np.linalg.norm(n,axis=1)*.5;n/=np.maximum(2*area[:,None],1e-12)
_,vi=np.unique(np.round(q.reshape(-1,3),5),axis=0,return_inverse=True);vi=vi.reshape(-1,3)
edges=np.concatenate([vi[:,[0,1]],vi[:,[1,2]],vi[:,[2,0]]]);edgefaces=np.tile(np.arange(len(f)),3);ee=np.sort(edges,axis=1);order=np.lexsort((ee[:,1],ee[:,0]));es=ee[order];at=np.flatnonzero((es[1:]==es[:-1]).all(1));aa=edgefaces[order[at]];bb=edgefaces[order[at+1]]
allaa=aa.copy();allbb=bb.copy();alledges=order[at].copy()
from CompleteEquipmentPartition import shield as shield_partition
ep=np.concatenate([q[:,[0,1]],q[:,[1,2]],q[:,[2,0]]])[order[at]];edge_length=np.linalg.norm(ep[:,0]-ep[:,1],axis=1);edge_cos=np.einsum('ij,ij->i',n[aa],n[bb])
shieldDomain,shieldFaces=shield_partition(c,roles,generated,bone,ids,aa,bb,edge_length,edge_cos,area)
bone[shieldFaces]=ids['Shield'];protected[shieldDomain]=shieldFaces[shieldDomain]
# The shield cut only distinguishes equipment from body. Retain torso
# ownership rather than turning every non-shield surface into an upper arm.
arm=shieldDomain&~shieldFaces&(bone!=ids['Torso']);bone[arm]=ids['ShoulderL']
for joint,upper in [('Elbow','Shoulder'),('Hand','Elbow')]:
 pivot=positions[ids[joint+'L']];normal=positions[ids[upper+'L']]-pivot;normal/=np.linalg.norm(normal);bone[arm&((c-pivot)@normal<0)]=ids[joint+'L']
valid=(aa!=bb)&~protected[aa]&~protected[bb];aa=aa[valid];bb=bb[valid];edgeid=order[at[valid]];ep=np.concatenate([q[:,[0,1]],q[:,[1,2]],q[:,[2,0]]])[edgeid];length=np.linalg.norm(ep[:,0]-ep[:,1],axis=1);cos=np.einsum('ij,ij->i',n[aa],n[bb]);penalty=length*(.015+.985*np.clip(cos,0,1)**8)
proposal=bone.copy();seam=bone[aa]!=bone[bb];boundary=np.unique(np.r_[aa[seam],bb[seam]]);dist=cKDTree(c[boundary]).query(c)[0];fixed=protected|(dist>.085)
N=len(f);scale=1000000.;moved=0
for sweep in range(2):
 for label in np.unique(proposal[~protected]):
  # Two-label restriction at a local boundary avoids swapping unrelated limbs.
  possible=(~fixed)&(np.linalg.norm(c-positions[label],axis=1)<3.0)
  cost=area/.035
  u0=cost*(bone!=proposal);u1=cost*(label!=proposal)
  u1[~possible]=100.;u1[bone==label]=0
  E00=penalty*(bone[aa]!=bone[bb]);E10=penalty*(label!=bone[bb]);E01=penalty*(bone[aa]!=label);w=(E10+E01-E00)*.5
  np.add.at(u1,aa,E10-E00-w);np.add.at(u1,bb,E01-E00-w)
  minimum=np.minimum(u0,u1);u0-=minimum;u1-=minimum
  nodes=np.arange(N);rows=np.r_[aa,bb,np.full(N,N),nodes];cols=np.r_[bb,aa,nodes,np.full(N,N+1)];vals=np.r_[w,w,u1,u0];keep=vals>1e-10
  graph=coo_matrix((np.maximum(1,np.rint(vals[keep]*scale)).astype(np.int64),(rows[keep],cols[keep])),shape=(N+2,N+2)).tocsr()
  flow=maximum_flow(graph,N,N+1);residual=graph-flow.flow;residual.data=(residual.data>0).astype(np.int64);residual.eliminate_zeros();reachable=breadth_first_order(residual,N,directed=True,return_predecessors=False);switch=np.ones(N,bool);switch[reachable[reachable<N]]=False
  assert not np.any(switch&fixed&(bone!=label)), 'hard surface ownership constraint violated'
  moved+=int(np.count_nonzero(switch&(bone!=label)));bone[switch]=label
 print('graph sweep',sweep,'changed',np.count_nonzero(bone!=proposal),flush=True)
# Reject disconnected ownership islands: a plate fragment sharing its entire
# rim with another owner must travel with that neighbouring plate, not orbit a
# distant joint. Free ornaments without a shared rim are retained for review.
aa=allaa;bb=allbb;ep=np.concatenate([q[:,[0,1]],q[:,[1,2]],q[:,[2,0]]])[alledges];length=np.linalg.norm(ep[:,0]-ep[:,1],axis=1)
islandChanges=0
for iteration in range(3):
 proposals=[]
 for label in np.unique(bone[~generated]):
  selected=np.flatnonzero((bone==label)&~generated)
  same=(bone[aa]==label)&(bone[bb]==label)
  graph=coo_matrix((np.ones(same.sum()),(aa[same],bb[same])),shape=(N,N))
  _,component=connected_components(graph,directed=False)
  counts=np.bincount(component[selected],weights=area[selected]);main=counts.argmax()
  for comp in np.unique(component[selected]):
   chosen=selected[component[selected]==comp]
   if comp==main or area[chosen].sum()>counts[main]*.35 or protected[chosen].any():continue
   membership=np.zeros(N,bool);membership[chosen]=True
   border=membership[aa]^membership[bb]
   if not border.any():continue
   other=np.where(membership[aa[border]],bb[border],aa[border]);owners=bone[other]
   vote=np.bincount(owners,weights=length[border],minlength=len(names));winner=int(vote.argmax())
   if winner in [ids['Head'],ids['Sword'],ids['Shield']] or vote[winner]<vote.sum()*.65:continue
   proposals.append((chosen,winner))
 for chosen,winner in proposals:bone[chosen]=winner;islandChanges+=len(chosen)
 print('ownership islands',iteration,sum(len(z[0]) for z in proposals),flush=True)
# Mirror the now-complete left rear assembly; remove right source scraps only
# where they occupy the corresponding rear-fin envelope.
right=(x>.28)&(y<1.93)&(z<-.20)&~protected
keep=~right
f['bones']=bone[:,None,None];f['weights']=[1,0]
wingfaces=f[bone==ids['WingL']].copy();wingfaces['vertex'][:,:,0]*=-1;wingfaces['vertex'][:,:,3]*=-1;wingfaces=wingfaces[:,[0,2,1]];wingfaces['bones']=ids['WingR'];wingfaces['weights']=[1,0]
f=np.concatenate([f[keep],wingfaces]);labels=np.r_[bone[keep],np.full(len(wingfaces),ids['WingR'])];materials=np.r_[np.array([parts[i]['material'] for i in partids])[keep],np.zeros(len(wingfaces),int)];tags=np.r_[roles[keep],np.full(len(wingfaces),'WingR')]
# Recut the three chest leaves at authored continuous aperture edges. The old
# face-centroid mask left sawtooth slivers hanging off the moving leaves.
def clip(poly,normal,point):
 distance=poly[:,:3]@normal-np.dot(normal,point);positive=[];negative=[]
 for i,av in enumerate(poly):
  j=(i+1)%len(poly);bv=poly[j];da=distance[i];db=distance[j]
  if da>=-1e-8:positive.append(av)
  if da<=1e-8:negative.append(av)
  if da*db < -1e-16:
   vv=av+(bv-av)*da/(da-db);vv[3:6]/=max(np.linalg.norm(vv[3:6]),1e-12);positive.append(vv);negative.append(vv)
 return np.array(positive),np.array(negative)
records=[];newlabels=[];newmats=[];newtags=[]
def append_poly(poly,label,mat,tag):
 for j in range(1,len(poly)-1):
  tri=np.array([poly[0],poly[j],poly[j+1]])
  if np.linalg.norm(np.cross(tri[1,:3]-tri[0,:3],tri[2,:3]-tri[0,:3]))<1e-10:continue
  rec=np.zeros(3,dt);rec['vertex']=tri;rec['bones']=label;rec['weights']=[1,0];records.append(rec);newlabels.append(label);newmats.append(mat);newtags.append(tag)
chestids=[ids[k] for k in ['ChestL','ChestR','ChestDoor']]
def append_arm(poly,side,mat):
 if len(poly)<3:return
 for joint,upper in [('Elbow','Shoulder'),('Hand','Elbow')]:
  pivot=positions[ids[joint+side]];normal=positions[ids[upper+side]]-pivot;normal/=np.linalg.norm(normal)
  top,poly=clip(poly,normal,pivot);append_poly(top,ids[upper+side],mat,'Arm'+side)
  if len(poly)<3:return
 append_poly(poly,ids['Hand'+side],mat,'Arm'+side)
# Two small original red/white chest panels provide a symbolic entrance.
# Their boundaries were picked in an intact frontal native view; the sculpt is
# asymmetric, so never mirror one mask onto the other side.
smallPanels={
 'ChestL':[[-.040,2.205],[-.185,2.205],[-.213,2.245],[-.218,2.317],[-.130,2.343],[-.067,2.320],[-.032,2.265]],
 'ChestR':[[.127,2.208],[.240,2.215],[.279,2.252],[.269,2.318],[.207,2.343],[.167,2.327],[.125,2.296]]}
from scipy.spatial import ConvexHull
panelPlanes={}
for name,outline in smallPanels.items():
 hull=ConvexHull(np.array(outline))
 panelPlanes[name]=[(np.array([-e[0],-e[1],0]),np.array([-e[2]*e[0],-e[2]*e[1],0])) for e in hull.equations]
 panelPlanes[name].append((np.array([0,0,1]),np.array([0,0,.58])))
for face,label,mat,tag in zip(f,labels,materials,tags):
 name=names[label]
 if name.startswith(('Shoulder','Elbow','Hand')):
  append_arm(face['vertex'].copy(),name[-1],mat);continue
 if label not in [ids['Torso']]+chestids:
  records.append(face);newlabels.append(label);newmats.append(mat);newtags.append(tag);continue
 pending=[face['vertex'].copy()]
 for panel,planes in panelPlanes.items():
  remainder=[]
  for poly in pending:
   for normal,point in planes:
    poly,outside=clip(poly,normal,point)
    if len(outside)>=3:remainder.append(outside)
    if len(poly)<3:break
   if len(poly)>=3:append_poly(poly,ids[panel],mat,panel)
  pending=remainder
 for poly in pending:append_poly(poly,ids['Torso'],mat,'Torso')
# Legacy centre-door joint remains available to older pose recordings, but
# owns no exterior faces and never opens the main breastplate.
for name,point in [('ChestL',[-.218,2.27,.66]),('ChestR',[.279,2.27,.68])]:
 positions[ids[name]]=point;d['joints'][ids[name]]['position']=point
f=np.array(records);labels=np.array(newlabels);materials=np.array(newmats);tags=np.array(newtags)
# Keep existing closed blade and knee feature payloads in their original
# batches, including provenance tags used by their independent regressions.
preserved=[part for part in source if part.get('generatedRepair') and part['generatedRepair']!='mirrored-intact-wing-v1']
featureRows=[]
for part in preserved:
 vv=np.frombuffer(raw,dt,part['vertices'],part['offset']);ii=np.frombuffer(raw,'<u4',part['indices'],part['offset']+48*part['vertices']).reshape(-1,3);featureRows.append(vv[ii]['vertex'])
if featureRows:
 def rows(v):return np.ascontiguousarray(v.reshape(-1,24)).view(np.dtype((np.void,96))).reshape(-1)
 featureMask=np.isin(rows(f['vertex']),rows(np.concatenate(featureRows)))
 f=f[~featureMask];labels=labels[~featureMask];materials=materials[~featureMask];tags=tags[~featureMask]
# Source records stay archived and verifiable for repeatable regeneration.
from CompleteSeamWeights import prepare as seam_weights
from CompleteSeamSubdivision import refine as refine_seams
apply_seams=seam_weights(f,labels,names)
beforeRefinement=len(f)
f,labels,materials,tags=refine_seams(f,labels,materials,tags,apply_seams,protected=(ids['Head'],ids['Sword']))
seamRefinementFaces=len(f)-beforeRefinement
blob=bytearray(raw);result=[dict(part) for part in preserved]
def role(name):return 'Arm'+name[-1] if name.startswith(('Shoulder','Elbow','Hand')) else 'Leg'+name[-1] if name.startswith(('Hip','Knee','Ankle','Foot')) else name
batches=collections.defaultdict(list)
for label in np.unique(labels):
 name=names[label]
 for labelRole in (['SwordBlade','SwordHilt'] if name=='Sword' else [role(name)]):
  selection=(labels==label)
  if name=='Sword':selection&=tags==labelRole
  for mat in np.unique(materials[selection]):batches[(int(mat),labelRole,False)].append(f[selection&(materials==mat)])
from CompletePanelInteriors import build as interior_build
interiors,closures=interior_build(f,labels,names,positions,dt)
interiorCount=sum(len(triangles) for triangles in interiors.values())
for label,triangles in sorted(interiors.items()):batches[(0,role(names[label]),True)].append(np.array(triangles))
closureBatches=collections.defaultdict(list)
for label,triangles in closures.items():closureBatches[role(names[label])].append(np.array(triangles))
for labelRole,chunks in sorted(closureBatches.items()):
 flat=apply_seams(np.concatenate(chunks)).reshape(-1);unique,ix=np.unique(flat,return_inverse=True);off=len(blob);blob.extend(unique.tobytes());blob.extend(ix.astype('<u4').tobytes())
 result.append(dict(node=d['sourceNodes'][0],nodeName='Complete_InnerShell_'+labelRole,primitive=0,joint='Torso',role=labelRole,material=0,offset=off,vertices=len(unique),indices=len(ix),generatedRepair='source-textured-panel-back-v1'))
for (mat,labelRole,liner),chunks in sorted(batches.items()):
 flat=apply_seams(np.concatenate(chunks)).reshape(-1);unique,ix=np.unique(flat,return_inverse=True);off=len(blob);blob.extend(unique.tobytes());blob.extend(ix.astype('<u4').tobytes())
 result.append(dict(node=d['sourceNodes'][0],nodeName=('Complete_Liner_' if liner else 'Complete_Panel_')+labelRole,primitive=0,joint='Torso',role=labelRole,material=mat,offset=off,vertices=len(unique),indices=len(ix),generatedRepair='recessed-joint-interior-v1' if liner else None))
d['wingRestoration'].update(mirroredFaces=len(wingfaces),reason='complete left root, middle and lower fin recovered before mirroring');d['panelSourceParts']=source;d['parts']=result;d['triangles']=sum(x['indices']//3 for x in result);d['renderRoles']={r:sum(p['indices']//3 for p in result if p['role']==r) for r in {p['role'] for p in result}};d['panelRepair']=dict(version=1,sourceBinarySha256=hashlib.sha256(raw).hexdigest(),reassignedFaces=int(np.count_nonzero(bone!=initial)),regularizedFaces=int(np.count_nonzero(bone!=proposal)),islandFaces=islandChanges,wingFaces=len(wingfaces),interiorTriangles=interiorCount,texturedBackTriangles=sum(len(v) for v in closures.values()),sourceExterior='original surfaces and UVs retained/interpolated at mechanical seams; complete left wing mirrored after recovery')
d['panelRepair']['armSeamBlendWidths']={'shoulder':.075,'elbow':.055,'wrist':.035}
d['panelRepair']['hatchLayout']='two-small-original-chest-panels'
d['panelRepair']['smallHatchOutlines']=smallPanels
d['panelRepair']['cockpitWalls']='small source-conforming recessed backing; main breastplate fixed'
d['panelRepair']['seamRefinementFaces']=seamRefinementFaces
d['panelRepair']['shieldPartition']='surface graph with archived ornament anchors'
d['panelRepair']['gripContactLockWidth']=.045
d['bindingAudit']['rigidArmour']=False
d['bindingAudit']['rigidPlateCentresWithFlexibleArmSeams']=True
a.output.mkdir(parents=True,exist_ok=True);(a.output/(stem+'_rig.bin')).write_bytes(blob);(a.output/(stem+'_rig.json')).write_text(json.dumps(d,indent=2));print(d['panelRepair'])

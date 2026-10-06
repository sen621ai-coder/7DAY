"""Hidden thickness and recessed mechanical members for rigid armour panels.
No exterior primitive covers: shell backs follow the original surface; members
stay inside measured limb envelopes and use the source graphite swatch.
"""
import collections
import numpy as np
from scipy.spatial import cKDTree
import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'.local-tests/mecha-asset-tools'))
import pymeshlab

def build(f,labels,names,positions,dt):
 output=collections.defaultdict(list)
 closures=collections.defaultdict(list)
 small_ids=[i for i,name in enumerate(names) if name in ['ChestL','ChestR']]
 small_tree=cKDTree(f[np.isin(labels,small_ids)]['vertex'][:,:,:3].reshape(-1,3))
 uv=[.8672464,.88142943]
 def emit(tri,b,normal=None):
  tri=np.array(tri,float)
  n=np.cross(tri[1,:3]-tri[0,:3],tri[2,:3]-tri[0,:3]);length=np.linalg.norm(n)
  if length<1e-10:return
  tri[:,3:6]=n/length if normal is None else normal;tri[:,6:8]=uv
  rec=np.zeros(3,dt);rec['vertex']=tri;rec['bones']=b;rec['weights']=[1,0];output[b].append(rec)
 for b in np.unique(labels):
  name=names[b]
  if name in ['Sword','Head','Backpack'] or name.startswith('Foot'):continue
  source=f[labels==b]['vertex'];points=source[:,:,:3]
  # Positional welding leaves UV seams intact in the exterior while allowing
  # one consistent inward offset and one rim at the physical plate boundary.
  v,ix=np.unique(np.round(points.reshape(-1,3),5),axis=0,return_inverse=True);ix=ix.reshape(-1,3)
  normals=np.zeros_like(v);np.add.at(normals,ix.reshape(-1),source[:,:,3:6].reshape(-1,3));normals/=np.maximum(np.linalg.norm(normals,axis=1)[:,None],1e-12)
  thickness=.014 if name.startswith('Chest') else .008
  inside=v-normals*thickness
  small_shared=np.zeros(len(v),bool)
  if name=='Torso':
   small_shared=small_tree.query(v)[0]<2e-5
   # The fixed rim and moving plate share this real source edge. Folded
   # sculpture normals can point forward here, exposing a black spike.
   inside[small_shared]=v[small_shared]
   inside[small_shared,2]-=.006
  if name in ['ChestL','ChestR']:
   # The small front plates are shallow shells, not hollow body volumes.
   # Recess their back in depth; averaged sculpture normals can point out.
   inside=v.copy();inside[:,2]-=.006
  back=source.copy();back[:,:,:3]=inside[ix]
  if name.startswith(('Chest','Shoulder','Elbow','Hand','Wing')):
   # A thin reverse of the same surface keeps texture islands aligned. Never
   # stretch UVs over a convex bridge between unrelated armour fragments.
   for tri in back[:,[0,2,1]]:
    normal=np.cross(tri[1,:3]-tri[0,:3],tri[2,:3]-tri[0,:3]);length=np.linalg.norm(normal)
    if length<1e-10:continue
    tri[:,3:6]=normal/length;rec=np.zeros(3,dt);rec['vertex']=tri;rec['bones']=b;rec['weights']=[1,0];closures[b].append(rec)
  elif name!='Torso':
   for tri in back[:,[0,2,1]]:emit(tri,b)
  e=np.concatenate([ix[:,[0,1]],ix[:,[1,2]],ix[:,[2,0]]]);ordered=np.sort(e,axis=1);unique,first,count=np.unique(ordered,axis=0,return_index=True,return_counts=True)
  # Seal only complete planar cut loops. Ear clipping respects concavity;
  # never form a fan from an open chain or bridge two disconnected contours.
  boundary=e[first[count==1]]
  if name.startswith(('Shoulder','Elbow','Hand','Shield')):
   # Close the actual disconnected cut contours, including non-planar ones.
   # Manifold repair is performed on a temporary topology copy ONLY; no source
   # exterior vertex, normal, UV or triangle is removed or remeshed here.
   caps=pymeshlab.MeshSet();caps.add_mesh(pymeshlab.Mesh(vertex_matrix=v.astype(float),face_matrix=ix.astype(np.int32)))
   caps.meshing_remove_duplicate_faces();caps.meshing_remove_null_faces()
   caps.meshing_repair_non_manifold_edges(method='Remove Faces')
   caps.meshing_repair_non_manifold_vertices(vertdispratio=0)
   caps.meshing_close_holes(maxholesize=1500,newfaceselected=True,selfintersection=True,refinehole=False)
   capmesh=caps.current_mesh();cv=capmesh.vertex_matrix();ci=capmesh.face_matrix()[capmesh.face_selection_array()]
   distance,nearest=cKDTree(v).query(cv)
   if distance.max()>1e-6:raise ValueError('Contour closure moved source vertices: '+name)
   for corners in ci:
    xyz=inside[nearest[corners]]
    # Non-planar large automatic caps crossed neighbouring armour. Only close
    # small local contours; joint-plane loops below have their own exact cap.
    if max(np.linalg.norm(xyz-np.roll(xyz,1,axis=0),axis=1))>.08:continue
    tri=np.zeros((3,8));tri[:,:3]=xyz;emit(tri,b)
  if name.startswith(('Shoulder','Elbow','Hand')):
   side=name[-1];ids={n:i for i,n in enumerate(names)}
   for joint,upper in [('Elbow','Shoulder'),('Hand','Elbow')]:
    if name not in [joint+side,upper+side]:continue
    pivot=positions[ids[joint+side]];axis=positions[ids[upper+side]]-pivot;axis/=np.linalg.norm(axis)
    cut=boundary[np.max(abs((v[boundary]-pivot)@axis),axis=1)<3e-5]
    neighbours=collections.defaultdict(list)
    for aa,bb in cut:neighbours[int(aa)].append(int(bb));neighbours[int(bb)].append(int(aa))
    visited=set()
    for start in neighbours:
     if start in visited:continue
     component=set();todo=[start]
     while todo:
      k=todo.pop()
      if k in component:continue
      component.add(k);todo.extend(neighbours[k])
     visited.update(component)
     if len(component)<3 or any(len(neighbours[k])!=2 for k in component):continue
     loop=[start];prev=-1;cur=start
     while True:
      nxt=next(k for k in neighbours[cur] if k!=prev)
      if nxt==start:break
      loop.append(nxt);prev,cur=cur,nxt
     xyz=v[loop];u=xyz[1]-xyz[0];u/=max(np.linalg.norm(u),1e-12);vv=np.cross(axis,u);flat=np.stack([xyz@u,xyz@vv],1)
     signed=np.sum(flat[:,0]*np.roll(flat[:,1],-1)-flat[:,1]*np.roll(flat[:,0],-1))
     order=list(range(len(loop))) if signed>0 else list(range(len(loop)-1,-1,-1));triangles=[];stalled=False
     while len(order)>3:
      ear=False
      for at in range(len(order)):
       i,j,k=order[at-1],order[at],order[(at+1)%len(order)];pa,pb,pc=flat[[i,j,k]]
       cross=lambda aa,bb:aa[0]*bb[1]-aa[1]*bb[0]
       if cross(pb-pa,pc-pb)<=1e-12:continue
       others=[t for t in order if t not in [i,j,k]];pointInside=False
       for t in others:
        pp=flat[t]
        if cross(pb-pa,pp-pa)>1e-10 and cross(pc-pb,pp-pb)>1e-10 and cross(pa-pc,pp-pc)>1e-10:pointInside=True;break
       if pointInside:continue
       triangles.append([i,j,k]);order.pop(at);ear=True;break
      if not ear:stalled=True;break
     if stalled:continue
     triangles.append(order)
     inward=axis if name==upper+side else -axis
     for ti in triangles:
      tri=np.zeros((3,8));tri[:,:3]=xyz[ti]+inward*.01
      if np.dot(np.cross(tri[1,:3]-tri[0,:3],tri[2,:3]-tri[0,:3]),-inward)<0:tri=tri[[0,2,1]]
      emit(tri,b)
  for j in first[count==1]:
   a,c=e[j];quad=np.zeros((4,8));quad[:,:3]=[v[c],v[a],inside[a],inside[c]]
   if name in ['ChestL','ChestR'] or (name=='Torso' and small_shared[a] and small_shared[c]):
    # Continue the actual boundary-edge UV through this six-millimetre rim.
    # A dark material on these folded source edges looked like new tears.
    edge_corners=[(0,1),(1,2),(2,0)][j//len(source)]
    uva,uvc=source[j%len(source),list(edge_corners),6:8]
    quad[:,6:8]=[uvc,uva,uva,uvc]
    for corners in [[0,1,2],[0,2,3]]:
     tri=quad[corners];normal=np.cross(tri[1,:3]-tri[0,:3],tri[2,:3]-tri[0,:3]);length=np.linalg.norm(normal)
     if length<1e-10:continue
     tri[:,3:6]=normal/length;rec=np.zeros(3,dt);rec['vertex']=tri;rec['bones']=b;rec['weights']=[1,0];closures[b].append(rec)
   else:
    emit(quad[[0,1,2]],b);emit(quad[[0,2,3]],b)
 # Tapered twelve-sided internal links, aligned to actual limb axes. Flat
 # bearing ends and narrow collars avoid the former exposed spherical blobs.
 ids={n:i for i,n in enumerate(names)}
 def tube(owner,a,c,r0,r1):
  axis=c-a;length=np.linalg.norm(axis)
  if length<1e-6:return
  axis/=length;u=np.cross(axis,[0,0,1.])
  if np.linalg.norm(u)<.1:u=np.cross(axis,[0,1.,0])
  u/=np.linalg.norm(u);v=np.cross(axis,u);rings=[]
  for center,r in [(a,r0),(c,r1)]:
   rings.append(np.array([center+r*(u*np.cos(k*np.pi/6)+v*np.sin(k*np.pi/6)) for k in range(12)]))
  for k in range(12):
   j=(k+1)%12
   for tri in [[rings[0][k],rings[0][j],rings[1][j]],[rings[0][k],rings[1][j],rings[1][k]],[a,rings[0][j],rings[0][k]],[c,rings[1][k],rings[1][j]]]:
    rec=np.zeros((3,8));rec[:,:3]=tri;emit(rec,ids[owner])
 for side in ['L','R']:
  sh,el,ha,hi,kn,an=[positions[ids[k+side]] for k in ['Shoulder','Elbow','Hand','Hip','Knee','Ankle']]
  tube('Shoulder'+side,sh+(el-sh)*.12,el,.085,.072)
  tube('Elbow'+side,el,ha,.09,.060)
  # Small transverse axle spans the joint, not a large ball outside its armour.
  for joint,point,radius,width in [('Elbow',el,.065,.12),('Hand',ha,.045,.08),('Ankle',an,.055,.11)]:
   tube(joint+side,point+[-width*.5,0,0],point+[width*.5,0,0],radius,radius)
 # Backing is not visible detail. Simplify it per rigid owner while preserving
 # every rim, so interior thickness does not double the runtime face budget.
 for owner,triangles in list(output.items()):
  if len(triangles)<400:continue
  rec=np.array(triangles);pts,ix=np.unique(rec['vertex'][:,:,:3].reshape(-1,3),axis=0,return_inverse=True);ix=ix.reshape(-1,3)
  ms=pymeshlab.MeshSet();ms.add_mesh(pymeshlab.Mesh(vertex_matrix=pts.astype(float),face_matrix=ix.astype(np.int32)))
  ms.meshing_decimation_quadric_edge_collapse(targetfacenum=max(180,int(len(ix)*.12)),preserveboundary=True,preservenormal=True,preservetopology=True,optimalplacement=False,qualitythr=.1)
  mesh=ms.current_mesh();xyz=mesh.vertex_matrix();faces=mesh.face_matrix();normal=mesh.vertex_normal_matrix();replacement=[]
  for corners in faces:
   tri=np.zeros((3,8));tri[:,:3]=xyz[corners];tri[:,3:6]=normal[corners];tri[:,6:8]=uv
   lens=np.linalg.norm(tri[:,3:6],axis=1);fn=np.cross(tri[1,:3]-tri[0,:3],tri[2,:3]-tri[0,:3]);length=np.linalg.norm(fn)
   if length<1e-10:continue
   tri[lens<1e-8,3:6]=fn/length;tri[:,3:6]/=np.linalg.norm(tri[:,3:6],axis=1)[:,None]
   record=np.zeros(3,dt);record['vertex']=tri;record['bones']=owner;record['weights']=[1,0];replacement.append(record)
  output[owner]=replacement
 # Texture-aware reduction of back faces only. Keep UV island and physical
 # boundaries; exterior source detail is never passed through this reducer.
 texture=Path(__file__).resolve().parents[2]/'ZZ-PZAEC_Mecha/Resources/CompleteTextures/base.png'
 for owner,triangles in list(closures.items()):
  if names[owner] in ['ChestL','ChestR','Torso']:continue
  rec=np.array(triangles);vertices=rec['vertex'].reshape(-1,8);unique,ix=np.unique(vertices[:,[0,1,2,6,7]],axis=0,return_inverse=True);ix=ix.reshape(-1,3)
  ms=pymeshlab.MeshSet();ms.add_mesh(pymeshlab.Mesh(vertex_matrix=unique[:,:3].astype(float),face_matrix=ix.astype(np.int32),v_tex_coords_matrix=unique[:,3:].astype(float)))
  ms.compute_texcoord_transfer_vertex_to_wedge();ms.set_texture_per_mesh(textname=str(texture))
  ms.meshing_decimation_quadric_edge_collapse_with_texture(targetfacenum=max(120,int(len(ix)*.30)),preserveboundary=True,preservenormal=True,qualitythr=.1)
  mesh=ms.current_mesh();xyz=mesh.vertex_matrix();faces=mesh.face_matrix();tex=mesh.wedge_tex_coord_matrix().reshape(-1,3,2);normals=mesh.vertex_normal_matrix();replacement=[]
  for corners,uvs in zip(faces,tex):
   tri=np.zeros((3,8));tri[:,:3]=xyz[corners];tri[:,3:6]=normals[corners];tri[:,6:8]=uvs
   fn=np.cross(tri[1,:3]-tri[0,:3],tri[2,:3]-tri[0,:3]);length=np.linalg.norm(fn)
   if length<1e-10:continue
   lens=np.linalg.norm(tri[:,3:6],axis=1);tri[lens<1e-8,3:6]=fn/length;tri[:,3:6]/=np.linalg.norm(tri[:,3:6],axis=1)[:,None]
   record=np.zeros(3,dt);record['vertex']=tri;record['bones']=owner;record['weights']=[1,0];replacement.append(record)
  closures[owner]=replacement
 # A fixed recessed backing follows each small source panel exactly. It
 # closes the narrow revealed slit without hollowing out the whole chest.
 for name in ['ChestL','ChestR']:
  for face in f[labels==ids[name]]['vertex']:
   tri=face.copy();tri[:,2]-=.014
   rec=np.zeros(3,dt);rec['vertex']=tri;rec['bones']=ids['Torso'];rec['weights']=[1,0]
   closures[ids['Torso']].append(rec)
 return output,closures

"""Sampled mesh clearance checks for the model-only secondary weapon rig."""
import bpy,math,json,sys
import numpy as np
from pathlib import Path
from mathutils.bvhtree import BVHTree
OUT=Path(__file__).resolve().parent/'Generated/SecondaryWeapons'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'M1Abrams-SecondaryWeapons.blend'))
scene=bpy.context.scene;scene.frame_set(0)
def meshset(objects):
 vs=[];fs=[];owners=[]
 for o in objects:
  m=o.data;m.calc_loop_triangles();offset=len(vs);vs.extend([o.matrix_world@v.co for v in m.vertices]);fs.extend([tuple(offset+i for i in t.vertices) for t in m.loop_triangles]);owners.extend([o.name]*len(m.loop_triangles))
 tree=BVHTree.FromPolygons(vs,fs,all_triangles=True);return tree,np.asarray(vs)[fs],owners
def intersects(a,b):
 for ia,ib in a[0].overlap(b[0]):
  x,y=a[1][ia],b[1][ib];ex=np.roll(x,-1,axis=0)-x;ey=np.roll(y,-1,axis=0)-y;nx=np.cross(ex[0],ex[1]);ny=np.cross(ey[0],ey[1]);axes=np.array([nx,ny]+[np.cross(u,v) for u in ex for v in ey]+[np.cross(nx,u) for u in ex]+[np.cross(ny,u) for u in ey]);lens=np.linalg.norm(axes,axis=1);axes=axes[lens>1e-10]/lens[lens>1e-10,None];px=x@axes.T;py=y@axes.T
  if not np.any((px.max(0)<py.min(0)-1e-5)|(py.max(0)<px.min(0)-1e-5)):return [a[2][ia],b[2][ib],{"hit_center_blender":y.mean(0).tolist()}]
 return None
base=[bpy.data.objects[n] for n in ['Hull_LOD0','Turret_LOD0','Barrel_LOD0','GunMount_LOD0']]
addons=[o for o in bpy.data.collections['M1 Secondary Weapons'].objects if o.type=='MESH' and o.name.endswith('_LOD0')]
mg=[o for o in addons if o.parent.name in ['RoofMGYaw','RoofMGPitch']]
aa=[o for o in addons if o.parent.name=='AAPitch']
mg_yaw=bpy.data.objects['RoofMGYaw'];mg_pitch=bpy.data.objects['RoofMGPitch'];aa_pitch=bpy.data.objects['AAPitch'];results=[]
for yaw in ([] if "--quick" in sys.argv else range(-180,180,15)):
 for pitch in [-10,0,30,55,80]:
  mg_yaw.rotation_euler.z=math.radians(yaw);mg_pitch.rotation_euler.x=math.radians(-pitch);bpy.context.view_layer.update()
  hit=intersects(meshset(mg),meshset(base+aa));results.append(dict(system='MG',yaw=yaw,pitch=pitch,collision=hit))
mg_yaw.rotation_euler.z=0;mg_pitch.rotation_euler.x=0
for pitch in ([20] if "--quick" in sys.argv else range(15,66,5)):
 aa_pitch.rotation_euler.x=math.radians(-pitch);bpy.context.view_layer.update();hit=intersects(meshset(aa),meshset(base+mg));results.append(dict(system='AA',pitch=pitch,collision=hit))
# Gun body / barrel must clear its own cradle; pivot caps and feed chute intentionally meet joints.
mg_yaw.rotation_euler.z=0
for pitch in ([] if "--quick" in sys.argv else [-10,0,30,55,80]):
 mg_pitch.rotation_euler.x=math.radians(-pitch);bpy.context.view_layer.update()
 moving=[o for o in mg if o.parent.name=='RoofMGPitch' and 'FeedSegment' not in o.name]
 fixed=[o for o in mg if o.parent.name=='RoofMGYaw' and 'Pivot_' not in o.name]
 results.append(dict(system='MG-self',pitch=pitch,collision=intersects(meshset(moving),meshset(fixed))))
mg_pitch.rotation_euler.x=0
# Demonstration poses at representative turret yaw / cannon elevation / recoil, including hull-relative movement.
for yaw in ([] if "--quick" in sys.argv else range(0,360,45)):
 for cannon in [4,20]:
  for recoil in [0,10]:
   bpy.data.objects['TurretYaw'].rotation_euler.z=math.radians(yaw);bpy.data.objects['GunPitch'].rotation_euler.x=math.radians(-cannon);scene.frame_set(recoil);mg_yaw.rotation_euler.z=0;mg_pitch.rotation_euler.x=0;aa_pitch.rotation_euler.x=math.radians(-20);bpy.context.view_layer.update();hit=intersects(meshset(mg+aa),meshset(base));results.append(dict(system='combined',turret=yaw,cannon=cannon,recoil=recoil,collision=hit))
report={'samples':len(results),'collision_samples':sum(r['collision'] is not None for r in results),'scope':'Triangle surface intersections of moving secondary meshes vs original body/main cannon and other weapon; stationary mounting feet are intentional contacts. Sampled poses, not proof of continuous swept clearance or enclosed-volume containment.','poses':results}
(OUT/'clearance-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print('CLEARANCE',report['samples'],report['collision_samples']);print([r for r in results if r['collision']][:20])

assert report["collision_samples"]==0, "Secondary model has sampled mesh intersections; inspect clearance-report.json"

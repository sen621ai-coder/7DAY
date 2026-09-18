import bpy,json,math
import numpy as np
from pathlib import Path
from mathutils.bvhtree import BVHTree
OUT=Path(__file__).parent/'Generated'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'M1Abrams.blend'))
scene=bpy.context.scene
def bvh(obj):
    dg=bpy.context.evaluated_depsgraph_get();o=obj.evaluated_get(dg);m=o.to_mesh();m.calc_loop_triangles();v=[o.matrix_world@p.co for p in m.vertices];f=[list(p.vertices) for p in m.loop_triangles];tree=BVHTree.FromPolygons(v,f,all_triangles=True);tri=np.asarray(v)[f];o.to_mesh_clear();return tree,tri
def intersections(a,b):
    pairs=a[0].overlap(b[0]);hits=[]
    for ia,ib in pairs:
        x,y=a[1][ia],b[1][ib];ex=np.roll(x,-1,axis=0)-x;ey=np.roll(y,-1,axis=0)-y;nx=np.cross(ex[0],ex[1]);ny=np.cross(ey[0],ey[1])
        axes=[nx,ny]+[np.cross(u,v) for u in ex for v in ey]+[np.cross(nx,u) for u in ex]+[np.cross(ny,u) for u in ey]
        separate=False
        for axis in axes:
            length=np.linalg.norm(axis)
            if length<1e-10:continue
            axis/=length;px=x@axis;py=y@axis
            if px.max()<py.min()-1e-5 or py.max()<px.min()-1e-5:separate=True;break
        if not separate:hits.append((ia,ib))
    return hits
hull=bvh(bpy.data.objects['Hull_LOD0']);barrel=bpy.data.objects['Barrel_LOD0'];turret=bpy.data.objects['TurretYaw'];pitch=bpy.data.objects['GunPitch'];results=[]
for yaw in range(0,360,5):
    absolute=abs((yaw+180)%360-180)
    lower=-6 if absolute<=95 else 4 if absolute>=145 else -6+(absolute-95)*.20
    for elevation in [lower,max(lower,0),20]:
        for frame in [0,10]:
            scene.frame_set(frame);turret.rotation_euler.z=math.radians(yaw);pitch.rotation_euler.x=math.radians(-elevation);bpy.context.view_layer.update()
            overlap=intersections(hull,bvh(barrel));mount=intersections(hull,bvh(bpy.data.objects['GunMount_LOD0']));results.append(dict(yaw=yaw,pitch=elevation,recoil=frame==10,barrelHullIntersections=len(overlap),mountHullIntersections=len(mount)))
(OUT/'pose-test-results.json').write_text(json.dumps(dict(poses=results,checked=len(results),intersections=sum(p['barrelHullIntersections'] for p in results)),indent=2))
print('POSES',len(results),'BARREL/HULL intersections',sum(p['barrelHullIntersections'] for p in results))
assert not any(p['barrelHullIntersections'] for p in results),'Barrel crosses hull; revise pitch envelope'
assert not any(p['mountHullIntersections'] for p in results),'Gun mount crosses hull; revise pitch envelope'
print('PASS: 432 barrel/hull geometric poses. This is not a suspension or network test.')

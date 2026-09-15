"""Import user-downloaded CC BY 4.0 NOTPB model; prepare game preview FBX.
Run with Blender --background --factory-startup --disable-autoexec --python.
"""
import bpy, json
from pathlib import Path
from mathutils import Vector

SOURCE=Path('E:/soft/7DTD-Modding/Incoming/NOTPB/extracted/source/girl.glb')
OUT=Path('E:/soft/7DTD-Modding/NOTPB-Preview')
OUT.mkdir(parents=True,exist_ok=True)
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=str(SOURCE))
objects=list(bpy.context.scene.objects)
meshes=[o for o in objects if o.type=='MESH']
rigs=[o for o in objects if o.type=='ARMATURE']
if not meshes or not rigs:raise RuntimeError('Expected mesh and armature')
before=sum(len(o.data.polygons) for o in meshes)
for o in meshes:
    bpy.context.view_layer.objects.active=o
    modifier=o.modifiers.new('Preview mesh reduction','DECIMATE')
    modifier.ratio=min(1,75000/before)
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    for p in o.data.polygons:p.use_smooth=True
after=sum(len(o.data.polygons) for o in meshes)
points=[o.matrix_world@Vector(c) for o in meshes for c in o.bound_box]
lo=Vector(tuple(min(p[i] for p in points) for i in range(3)))
hi=Vector(tuple(max(p[i] for p in points) for i in range(3)))
root=bpy.data.objects.new('CharacterRoot',None);bpy.context.collection.objects.link(root)
for o in objects:
    if o.parent is None:o.parent=root
factor=1.68/(hi.z-lo.z)
root.scale=(factor,)*3;root.location=(-factor*(lo.x+hi.x)/2,-factor*(lo.y+hi.y)/2,-factor*lo.z)
texdir=OUT/'Textures';texdir.mkdir(exist_ok=True)
for im in bpy.data.images:
    if im.type=='IMAGE' and im.size[0]>0:
        im.filepath_raw=str(texdir/'NOTPB_BaseColor.png');im.file_format='PNG';im.save()
        im.filepath=str(texdir/'NOTPB_BaseColor.png')
bpy.context.view_layer.update()
bpy.ops.object.select_all(action='DESELECT')
for o in objects+[root]:o.select_set(True)
bpy.context.view_layer.objects.active=rigs[0]
bpy.ops.export_scene.fbx(filepath=str(OUT/'Sakura.fbx'),use_selection=True,object_types={'ARMATURE','MESH','EMPTY'},
    add_leaf_bones=False,bake_anim=False,path_mode='COPY',embed_textures=False,axis_forward='-Z',axis_up='Y')
stats={'source_faces':before,'preview_faces':after,'bones':sum(len(r.data.bones) for r in rigs),
       'height_metres':1.68,'source':str(SOURCE),'animations':len(bpy.data.actions),
       'attribution':'Cute Anime Girl - Fully Rigged 3D Model by NOTPB (munmyeomi), CC BY 4.0',
       'changes':'Decimated mesh, normalized height, converted texture and exported FBX. No new animation.'}
(OUT/'model-report.json').write_text(json.dumps(stats,indent=2),encoding='utf8')
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'NOTPB-preview.blend'))

# Preview uses the real imported texture and optimized geometry.
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=24
scene.render.resolution_x=850;scene.render.resolution_y=1100;scene.render.resolution_percentage=100
scene.world.color=(.22,.22,.22)
def aim(o,p):o.rotation_euler=(Vector(p)-o.location).to_track_quat('-Z','Y').to_euler()
for pos,power in [((2,-3,4),350),((-3,-1,2),240),((0,3,3),350)]:
    bpy.ops.object.light_add(type='AREA',location=pos);light=bpy.context.object;light.data.energy=power;light.data.size=4;aim(light,(0,0,1))
bpy.ops.object.camera_add(location=(2,-5,2));cam=bpy.context.object;aim(cam,(0,0,.88));cam.data.type='ORTHO';cam.data.ortho_scale=2.08;scene.camera=cam
scene.render.filepath=str(OUT/'preview-front.png');bpy.ops.render.render(write_still=True)
cam.location=(-2,5,2);aim(cam,(0,0,.88));scene.render.filepath=str(OUT/'preview-back.png');bpy.ops.render.render(write_still=True)
print('NOTPB_PREVIEW_READY',json.dumps(stats))

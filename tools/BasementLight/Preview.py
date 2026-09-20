"""Offline appearance preview only; Blender's render is not game-lighting QA."""
import bpy, math, pathlib
from mathutils import Vector
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
def material(name, color, emission=0):
    m=bpy.data.materials.new(name);m.diffuse_color=(*color,1);m.use_nodes=True
    bs=m.node_tree.nodes.get('Principled BSDF');bs.inputs['Base Color'].default_value=(*color,1)
    bs.inputs['Roughness'].default_value=.55
    bs.inputs['Emission Color'].default_value=(*color,1);bs.inputs['Emission Strength'].default_value=emission
    return m
frame=material('Charcoal metal',(.16,.18,.20));panel=material('Opal diffuser',(.82,.84,.83),.18)
def box(name,pos,size,mat):
    bpy.ops.mesh.primitive_cube_add(size=1,location=(pos[0],pos[2],pos[1]))
    o=bpy.context.object;o.name=name;o.dimensions=(size[0],size[2],size[1]);o.data.materials.append(mat)
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    bevel=o.modifiers.new('Subtle edge bevel','BEVEL');bevel.width=.003;bevel.segments=2
box('PanelFrame',(0,.92,0),(.92,.14,.92),frame)
box('MilkDiffuser',(0,.845,0),(.82,.018,.82),panel)
for x in [-.42,.42]:
    for z in [-.42,.42]:box('CornerFastener',(x,.844,z),(.025,.008,.025),panel)
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=32
scene.world.color=(.18,.18,.18)
for location,energy,size in [((1,-2,-1),100,3),((-2,0,1),150,3)]:
    bpy.ops.object.light_add(type='AREA',location=location)
    light=bpy.context.object;light.data.energy=energy;light.data.shape='DISK';light.data.size=size
    light.rotation_euler=(Vector((0,0,.9))-light.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(1.25,-1.6,-.25))
camera=bpy.context.object;camera.rotation_euler=(Vector((0,0,.9))-camera.location).to_track_quat('-Z','Y').to_euler()
camera.data.type='ORTHO';camera.data.ortho_scale=1.7;scene.camera=camera
scene.render.resolution_x=960;scene.render.resolution_y=720;scene.render.resolution_percentage=100
scene.render.film_transparent=True
scene.render.image_settings.file_format='PNG'
scene.render.filepath=str(pathlib.Path(__file__).resolve().parents[2]/'ZZZ-PZAEC_BasementLight'/'preview.png')
bpy.ops.render.render(write_still=True)

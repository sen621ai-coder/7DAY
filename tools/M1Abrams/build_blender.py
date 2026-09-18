"""blender -b --python tools/M1Abrams/build_blender.py. No source GLB edits."""
import bpy, math, json, struct, sys
from pathlib import Path
import numpy as np
from mathutils import Vector
HERE=Path(__file__).resolve().parent; OUT=HERE/'Generated'
MOD=HERE.parents[1]/'ZZ-PZAEC_M1Abrams'; RES=MOD/'Resources';RES.mkdir(parents=True,exist_ok=True)
meta=json.loads((OUT/'parts.json').read_text());data=np.load(OUT/'parts.npz')
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
scene=bpy.context.scene;scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
def xyz(v):return (float(v[0]),-float(v[2]),float(v[1]))
def design(v):return (v[0],v[2],-v[1])
nodes={}; origins={}; hierarchy=[]
def node(name,at=(0,0,0),parent='M1AbramsRoot'):
    o=bpy.data.objects.new(name,None);scene.collection.objects.link(o)
    if parent:o.parent=nodes[parent];o.location=Vector(xyz(np.array(at)-np.array(origins[parent])))
    else:o.location=xyz(at)
    nodes[name]=o;origins[name]=list(at);hierarchy.append(dict(name=name,parent=parent or '',position=list(at)));return o
node('M1AbramsRoot',parent=None);node('VisualRoot');node('TurretYaw',(.0013,1.45,.3208),'VisualRoot')
node('GunPitch',(.0065,1.7991,1.38),'TurretYaw');node('GunRecoil',(.0065,1.7991,1.38),'GunPitch')
node('Muzzle',(.0065,1.7991,5.5858),'GunRecoil');node('MuzzleFX',(.0065,1.7991,5.5858),'GunRecoil')
node('GunObstructionStart',(.0065,1.7991,1.38),'GunPitch');node('GunnerSight',(.45,2.32,.65),'TurretYaw')
node('Mounts');node('PhysicsMounts');node('Collision');
for name,at in {'SeatDriver':(0,.8,2),'SeatGunner':(.45,1.12,.2),'CameraDriver':(0,2.5,1.8),'CameraChase':(0,3.8,-8),'ExitLeft':(-2.4,.2,0),'ExitRight':(2.4,.2,0),'ExitRear':(0,.2,-4.5),'HeadlightLeft':(-.94,1.25,3.7),'HeadlightRight':(.94,1.25,3.7),'ExhaustLeft':(-.5,1.1,-3.75),'ExhaustRight':(.5,1.1,-3.75)}.items():node(name,at,'Mounts')
node('CenterOfMass',(0,.72,0),'PhysicsMounts');node('RecoilForcePoint',(0,.8,.3),'PhysicsMounts')
for side,x in [('L',-1.36),('R',1.36)]:
    for i,z in enumerate([2.146,1.297,.579,-.111,-.810,-1.503,-2.187],1):node(f'Contact_{side}_{i:02}',(x,.404,z),'PhysicsMounts')

materials=[]
for i in range(2):
    m=bpy.data.materials.new(['Hull','Track'][i]);m.use_nodes=True;tree=m.node_tree;bs=tree.nodes.get('Principled BSDF')
    def tex(key,space):
        n=tree.nodes.new('ShaderNodeTexImage');n.image=bpy.data.images.load(str(OUT/f'Textures/{key}{i}.png'));n.image.colorspace_settings.name=space;return n
    color=tex('color','sRGB');tree.links.new(color.outputs['Color'],bs.inputs['Base Color'])
    if i==1:tree.links.new(color.outputs['Alpha'],bs.inputs['Alpha']);m.surface_render_method='DITHERED'
    orm=tex('orm','Non-Color');sep=tree.nodes.new('ShaderNodeSeparateColor');tree.links.new(orm.outputs['Color'],sep.inputs['Color']);tree.links.new(sep.outputs['Green'],bs.inputs['Roughness']);tree.links.new(sep.outputs['Blue'],bs.inputs['Metallic'])
    normal=tex('normal','Non-Color');nm=tree.nodes.new('ShaderNodeNormalMap');tree.links.new(normal.outputs['Color'],nm.inputs['Color']);tree.links.new(nm.outputs['Normal'],bs.inputs['Normal']);materials.append(m)
objects=[];partmap=[]
for i,item in enumerate(meta):
    name=item['name'];v=data[f'{i}_v'];n=data[f'{i}_n'];uv=data[f'{i}_uv'];f=data[f'{i}_f']
    # The source mantlet underside penetrates the static turret-ring collar even
    # at neutral. Raise only its hidden bottom lip; keep barrel and visible roof.
    if name=='GunMount':
        v=v.copy();v[:,1]=np.maximum(v[:,1],1.66)
    if name=='Turret':parent='TurretYaw';pivot=origins[parent]
    elif name=='Barrel':parent='GunRecoil';pivot=origins[parent]
    elif name=='GunMount':parent='GunPitch';pivot=origins[parent]
    elif 'Wheel' in name:
        pivot=((v.min(0)+v.max(0))/2).tolist();parent=name;node(parent,pivot,'VisualRoot')
    else:parent='VisualRoot';pivot=[0,0,0]
    mesh=bpy.data.meshes.new(name);mesh.from_pydata([xyz(p-np.array(pivot)) for p in v],[],f.tolist());mesh.update()
    layer=mesh.uv_layers.new(name='UVMap')
    for loop in mesh.loops:layer.data[loop.index].uv=(float(uv[loop.vertex_index,0]),float(1-uv[loop.vertex_index,1]))
    mesh.normals_split_custom_set_from_vertices([xyz(p) for p in n])
    mesh.materials.append(materials[item['mat']]);o=bpy.data.objects.new(name+'_LOD0',mesh);scene.collection.objects.link(o);o.parent=nodes[parent];objects.append(o)
    partmap.append(dict(name=name,parent=parent,pivot=pivot,material=item['mat'],triangles=len(f)))
    for level,ratio in [(1,.55),(2,.25)]:
        lod=o.copy();lod.data=o.data.copy();lod.name=f'{name}_LOD{level}';scene.collection.objects.link(lod)
        bpy.context.view_layer.objects.active=lod;mod=lod.modifiers.new('SilhouetteLOD','DECIMATE');mod.ratio=ratio;mod.use_collapse_triangulate=True
        bpy.ops.object.modifier_apply(modifier=mod.name);lod.hide_render=True;lod.hide_set(True)
        objects.append(lod)

# Close the hidden turret interface with a recessed ring cap, not a full-body weld.
def cap(name,at,radius,depth,parent):
    bpy.ops.mesh.primitive_cylinder_add(vertices=32,radius=radius,depth=depth,location=xyz(at));o=bpy.context.object;o.name=name
    o.parent=nodes[parent];o.location=Vector(xyz(np.array(at)-origins[parent]));o.data.materials.append(materials[1]);return o
# Dark material for otherwise exposed hidden mechanical spaces.
dark=bpy.data.materials.new('InteriorOcclusion');dark.diffuse_color=(.055,.045,.033,1)
capobj=cap('TurretBaseCap',(.0013,1.448,.3208),.97,.025,'TurretYaw');capobj.data.materials.clear();capobj.data.materials.append(dark)
# Hidden sleeve behind the barrel allows 25 cm recoil without a daylight gap.
bpy.ops.mesh.primitive_cylinder_add(vertices=24,radius=.118,depth=.48,location=(0,0,0));sleeve=bpy.context.object;sleeve.name='RecoilSleeve';sleeve.parent=nodes['GunPitch'];sleeve.location=xyz((0,0,.42));sleeve.rotation_euler=(math.pi/2,0,0);sleeve.data.materials.append(dark)

# Model-side demonstration keys; runtime uses the same absolute-time curve.
recoil=nodes['GunRecoil'];scene.render.fps=100
for frame in range(91):
    t=frame/100
    if t<.1:offset=.25*(1-(1-t/.1)**2)
    elif t<.16:offset=.25
    elif t<.6:
        u=(t-.16)/.44;offset=.25*(1-u*u*(3-2*u))
    else:offset=0
    recoil.location=xyz((0,0,-offset));recoil.keyframe_insert('location',frame=frame)
recoil.animation_data.action.name='M1_Cannon_Recoil_0600ms';scene.frame_set(0)
scene.frame_start=0;scene.frame_end=90

def write_str(stream,s):
    b=s.encode();stream.write(struct.pack('<i',len(b)));stream.write(b)
report=[]
with (RES/'M1.meshbin').open('wb') as stream:
    stream.write(b'M1B1');stream.write(struct.pack('<i',len(meta)))
    for part in partmap:
        write_str(stream,part['name']);write_str(stream,part['parent']);stream.write(struct.pack('<3fi',*part['pivot'],part['material']))
        counts=[]
        for level in range(3):
            mesh=bpy.data.objects[f"{part['name']}_LOD{level}"].data;mesh.calc_loop_triangles();verts=[];norms=[];uvs=[];faces=[];lookup={}
            for tri in mesh.loop_triangles:
                face=[]
                for li in tri.loops:
                    loop=mesh.loops[li];v=design(mesh.vertices[loop.vertex_index].co);n=design(mesh.corner_normals[li].vector);uv=mesh.uv_layers.active.data[li].uv
                    key=tuple(round(float(x),7) for x in (*v,*n,*uv))
                    if key not in lookup:lookup[key]=len(verts);verts.append(v);norms.append(n);uvs.append(tuple(uv))
                    face.append(lookup[key])
                # These are already final numeric engine coordinates. Preserve
                # cross(edge1,edge2) aligned with normals, verified against an
                # installed Unity vehicle mesh; do not mirror a second time.
                faces.append(face)
            packed=np.c_[verts,norms,uvs].astype('<f4');indices=np.asarray(faces,dtype='<i4');stream.write(struct.pack('<ii',len(verts),len(faces)));stream.write(packed.tobytes());stream.write(indices.tobytes());counts.append(len(faces))
        report.append(dict(**part,lodTriangles=counts))
(RES/'anchors.json').write_text(json.dumps(dict(nodes=hierarchy,parts=report),indent=2))
(OUT/'build-report.json').write_text(json.dumps(dict(parts=report,triangles=[sum(p['lodTriangles'][i] for p in report) for i in range(3)]),indent=2))

# Model deliverables, editable source plus interchange. No Unity editor required.
for o in bpy.context.selected_objects:o.select_set(False)
hidden_for_export=[o for o in objects if o.hide_get()]
for o in list(nodes.values())+objects+[capobj,sleeve]:
    o.hide_set(False)
    o.select_set(True)
bpy.ops.export_scene.fbx(filepath=str(OUT/'M1Abrams.fbx'),use_selection=True,axis_forward='-Z',axis_up='Y',add_leaf_bones=False,bake_anim=True,path_mode='COPY',embed_textures=True)
for o in hidden_for_export:o.hide_set(True)
for image in bpy.data.images:
    if image.source=='FILE':image.pack()
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'M1Abrams.blend'))

# Real textured render, camera positioned in design coordinates.
scene.render.engine='CYCLES';scene.cycles.samples=20
scene.render.resolution_x=1280;scene.render.resolution_y=900;scene.render.resolution_percentage=100
scene.world.color=(.22,.22,.22)
bpy.ops.object.light_add(type='AREA',location=xyz((3,9,4)));bpy.context.object.data.energy=2300;bpy.context.object.data.shape='DISK';bpy.context.object.data.size=8
bpy.context.object.rotation_euler=(Vector(xyz((0,1,0)))-bpy.context.object.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=xyz((11,7,13)));cam=bpy.context.object;cam.rotation_euler=(Vector(xyz((0,1.5,.5)))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=12;scene.camera=cam
scene.render.film_transparent=False;scene.render.filepath=str(OUT/'M1-textured.png');bpy.ops.render.render(write_still=True)
icons=MOD/'ItemIcons';icons.mkdir(exist_ok=True)
scene.render.resolution_x=scene.render.resolution_y=256;scene.render.film_transparent=True;cam.data.ortho_scale=10.5
scene.render.filepath=str(icons/'vehicleM1AbramsPlaceable.png');bpy.ops.render.render(write_still=True)
scene.render.resolution_x=1280;scene.render.resolution_y=900;scene.render.film_transparent=False;cam.data.ortho_scale=12
nodes['TurretYaw'].rotation_euler.z=math.pi/2;nodes['GunPitch'].rotation_euler.x=math.radians(-12);scene.frame_set(10)
scene.render.filepath=str(OUT/'M1-fire-pose.png');bpy.ops.render.render(write_still=True)
nodes['TurretYaw'].rotation_euler.z=0;nodes['GunPitch'].rotation_euler.x=0;scene.frame_set(0)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'M1Abrams.blend'))
print('M1 build complete: blend, FBX, runtime mesh, anchors, LODs, renders')

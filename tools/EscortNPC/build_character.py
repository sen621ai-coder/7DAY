"""Build the original stylized adult survivor prototype, rig and FBX in Blender.
Run: blender --background --python tools/EscortNPC/build_character.py
This is an editable first model, not a shipped game asset bundle.
"""
import bpy, math
from pathlib import Path
from mathutils import Vector

OUT = Path(__file__).resolve().parents[2] / '.local-tests' / 'EscortNPC' / 'Character'
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)

def material(name, color, rough=.65):
    m=bpy.data.materials.new(name); m.diffuse_color=(*color,1); m.use_nodes=True
    bs=next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'); bs.inputs['Base Color'].default_value=(*color,1)
    bs.inputs['Roughness'].default_value=rough
    return m
skin=material('Skin peach',(.92,.63,.50)); pink=material('Hair sakura',(.73,.27,.40))
lightpink=material('Hair highlights',(.95,.53,.64)); dark=material('Ink navy',(.035,.049,.09))
coat=material('Jacket cream',(.87,.82,.72)); teal=material('Accent mint',(.17,.57,.53))
white=material('Eye whites',(.96,.98,1)); blue=material('Iris blue',(.05,.38,.78),.25)
black=material('Pupil',(.006,.012,.024)); blush=material('Cheek blush',(.88,.34,.36))
soles=material('Soles',(.68,.72,.72)); meshes=[]
def uv(name, pos, scale, mat, bone, seg=24, rings=16):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg,ring_count=rings,location=pos)
    o=bpy.context.object; o.name=name; o.scale=scale
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    o.data.materials.append(mat)
    for p in o.data.polygons:p.use_smooth=True
    meshes.append((o,bone)); return o
def tube(name, a, b, r1, r2, mat, bone):
    a,b=Vector(a),Vector(b); d=b-a
    bpy.ops.mesh.primitive_cone_add(vertices=20,radius1=r1,radius2=r2,depth=d.length,location=(a+b)/2)
    o=bpy.context.object;o.name=name;o.rotation_euler=d.to_track_quat('Z','Y').to_euler()
    o.data.materials.append(mat)
    be=o.modifiers.new('Rounded seams','BEVEL');be.width=.013;be.segments=3
    bpy.context.view_layer.objects.active=o;bpy.ops.object.modifier_apply(modifier=be.name)
    for p in o.data.polygons:p.use_smooth=True
    meshes.append((o,bone));return o
def curve(name, coords, radius, mat, bone):
    c=bpy.data.curves.new(name,'CURVE');c.dimensions='3D';c.bevel_depth=radius;c.bevel_resolution=3
    s=c.splines.new('BEZIER');s.bezier_points.add(len(coords)-1)
    for p,co in zip(s.bezier_points,coords):p.co=co;p.handle_left_type='AUTO';p.handle_right_type='AUTO'
    o=bpy.data.objects.new(name,c);bpy.context.collection.objects.link(o);o.data.materials.append(mat)
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    bpy.ops.object.convert(target='MESH');meshes.append((o,bone));return o

# 1.68 m character, front is -Y; adult torso/leg proportions with an anime head.
uv('Pelvis shorts',(0,0,.88),(.18,.11,.15),dark,'Hips')
uv('Hoodie torso',(0,0,1.16),(.205,.135,.285),coat,'Spine')
uv('Hood',(0,.09,1.355),(.22,.135,.10),coat,'Chest')
tube('Neck',(0,0,1.36),(0,0,1.46),.064,.063,skin,'Neck')
uv('Head',(0,0,1.53),(.166,.14,.205),skin,'Head',40,28)
uv('Hair back',(0,.057,1.55),(.179,.135,.21),pink,'Head',36,24)
uv('Hair crown',(0,.008,1.686),(.169,.123,.072),pink,'Head')
for s in [-1,1]:
    suffix='Left' if s<0 else 'Right'
    x=s*.091
    tube('Thigh '+suffix,(x,0,.89),(x,-.01,.53),.088,.067,dark,suffix+'UpperLeg')
    uv('Knee '+suffix,(x,-.01,.53),(.067,.067,.068),dark,suffix+'LowerLeg')
    tube('Leg '+suffix,(x,-.01,.53),(x,.015,.15),.065,.042,dark,suffix+'LowerLeg')
    uv('Shoe '+suffix,(x,-.039,.099),(.071,.122,.072),coat,suffix+'Foot')
    uv('Sole '+suffix,(x,-.04,.049),(.075,.124,.026),soles,suffix+'Foot')
    for z in [.13,.15]:curve('Lace '+suffix,[(x-.04,-.09,z),(x,-.11,z),(x+.04,-.09,z)],.006,teal,suffix+'Foot')
    shoulder=(s*.19,0,1.34); elbow=(s*.37,0,1.13); wrist=(s*.50,0,.94)
    uv('Shoulder '+suffix,shoulder,(.092,.093,.095),coat,suffix+'UpperArm')
    tube('Upper sleeve '+suffix,shoulder,elbow,.084,.071,coat,suffix+'UpperArm')
    uv('Elbow '+suffix,elbow,(.071,.071,.072),coat,suffix+'LowerArm')
    tube('Lower sleeve '+suffix,elbow,wrist,.071,.048,coat,suffix+'LowerArm')
    uv('Cuff '+suffix,wrist,(.055,.053,.047),teal,suffix+'LowerArm')
    uv('Hand '+suffix,(s*.524,-.005,.902),(.040,.031,.067),skin,suffix+'Hand')
    uv('Thumb '+suffix,(s*.489,-.029,.913),(.022,.024,.037),skin,suffix+'Hand')
    uv('Ear '+suffix,(s*.16,0,1.522),(.023,.031,.045),skin,'Head')
    uv('Eye white '+suffix,(s*.064,-.126,1.547),(.052,.020,.047),white,'Head')
    uv('Blue iris '+suffix,(s*.062,-.145,1.545),(.026,.009,.037),blue,'Head')
    uv('Pupil '+suffix,(s*.062,-.153,1.549),(.012,.004,.024),black,'Head')
    uv('Eye sparkle '+suffix,(s*.062-.009,-.157,1.562),(.009,.003,.012),white,'Head')
    curve('Upper lash '+suffix,[(s*.018,-.14,1.561),(s*.060,-.149,1.588),(s*.111,-.126,1.566)],.005,dark,'Head')
    uv('Blush '+suffix,(s*.097,-.12,1.49),(.027,.006,.012),blush,'Head')
    uv('Bob side '+suffix,(s*.155,.003,1.51),(.042,.088,.15),pink,'Head')
    curve('Side lock shine '+suffix,[(s*.155,-.065,1.62),(s*.169,-.076,1.54),(s*.154,-.065,1.44)],.008,lightpink,'Head')
    curve('Hood string '+suffix,[(s*.062,-.122,1.365),(s*.062,-.143,1.24)],.006,teal,'Chest')
    curve('Pack strap '+suffix,[(s*.135,.03,1.39),(s*.165,-.107,1.31),(s*.15,-.122,1.13)],.018,teal,'Chest')
for i in range(7):
    x=(i-3)*.039
    curve('Fringe %02d'%i,[(x*.75,-.071,1.708),(x,-.13,1.655),(x+.014,-.136,1.596+abs(i-3)*.012)],.023,pink,'Head')
uv('Nose',(0,-.142,1.510),(.013,.018,.017),skin,'Head')
curve('Smile',[(-.022,-.130,1.465),(0,-.139,1.459),(.022,-.130,1.465)],.003,blush,'Head')
curve('Zipper',[(0,-.132,1.33),(0,-.146,1.14),(0,-.11,.94)],.004,teal,'Spine')
uv('Jacket pocket',(0,-.124,1.04),(.117,.019,.047),coat,'Spine')
uv('Backpack',(0,.154,1.17),(.145,.085,.19),teal,'Chest')
uv('Backpack pocket',(0,.221,1.12),(.10,.028,.071),coat,'Chest')
# Clearly a small cat-ear hair ornament, not biological ears.
for s in [-1,1]:
    a=(.09+s*.026,-.072,1.721)
    tube('Cat ear clip',a,(a[0]+s*.008,-.07,1.77),.023,.001,teal,'Head')

bpy.ops.object.armature_add(enter_editmode=True,location=(0,0,0));rig=bpy.context.object;rig.name='SakuraRig'
eb=rig.data.edit_bones;eb.remove(eb[0])
def bone(name,head,tail,parent=None):
    b=eb.new(name);b.head=head;b.tail=tail
    if parent:b.parent=eb[parent]
bone('Hips',(0,0,.83),(0,0,1.00));bone('Spine',(0,0,1),(0,0,1.19),'Hips')
bone('Chest',(0,0,1.19),(0,0,1.37),'Spine');bone('Neck',(0,0,1.37),(0,0,1.45),'Chest')
bone('Head',(0,0,1.45),(0,0,1.69),'Neck')
for s in [-1,1]:
    q='Left' if s<0 else 'Right';x=s*.091
    bone(q+'UpperLeg',(x,0,.89),(x,-.01,.53),'Hips')
    bone(q+'LowerLeg',(x,-.01,.53),(x,.015,.15),q+'UpperLeg')
    bone(q+'Foot',(x,.015,.15),(x,-.11,.07),q+'LowerLeg')
    bone(q+'Shoulder',(s*.04,0,1.34),(s*.19,0,1.34),'Chest')
    bone(q+'UpperArm',(s*.19,0,1.34),(s*.37,0,1.13),q+'Shoulder')
    bone(q+'LowerArm',(s*.37,0,1.13),(s*.50,0,.94),q+'UpperArm')
    bone(q+'Hand',(s*.50,0,.94),(s*.54,0,.865),q+'LowerArm')
bpy.ops.object.mode_set(mode='OBJECT')
for o,b in meshes:
    group=o.vertex_groups.new(name=b);group.add(list(range(len(o.data.vertices))),1,'REPLACE')
    mod=o.modifiers.new('Skeleton','ARMATURE');mod.object=rig;o.parent=rig
rig.show_in_front=True
rig['status']='Prototype: rigid segment weights. Requires deformation refinement and game animation adaptation.'

# Export clean skeleton and geometry. Lights/camera remain in the editable .blend only.
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True)
for o,_ in meshes:o.select_set(True)
bpy.context.view_layer.objects.active=rig
bpy.ops.export_scene.fbx(filepath=str(OUT/'Sakura.fbx'),use_selection=True,object_types={'ARMATURE','MESH'},
    add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y')
bpy.ops.object.select_all(action='DESELECT')
floor=material('Backdrop',(.07,.11,.15))
bpy.ops.mesh.primitive_plane_add(size=200);bpy.context.object.data.materials.append(floor)
def aim(o,p):o.rotation_euler=(Vector(p)-o.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(2.7,-5.3,2.25));camera=bpy.context.object;aim(camera,(0,0,.93));camera.data.type='ORTHO';camera.data.ortho_scale=2.15
bpy.context.scene.camera=camera
for pos,power,size in [((2,-3,4),450,4),((-3,-1,2),300,3),((0,3,3),500,3)]:
    bpy.ops.object.light_add(type='AREA',location=pos);l=bpy.context.object;l.data.energy=power;l.data.shape='DISK';l.data.size=size;aim(l,(0,0,1))
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=32
scene.render.resolution_x=900;scene.render.resolution_y=1100;scene.render.resolution_percentage=100
scene.world.color=(.18,.18,.18);scene.view_settings.view_transform='AgX'
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Sakura.blend'))
scene.render.filepath=str(OUT/'Sakura-preview.png');bpy.ops.render.render(write_still=True)
print('ESCORT_MODEL_OK',OUT,'vertices',sum(len(o.data.vertices) for o,_ in meshes),'bones',len(rig.data.bones))

"""Build editable secondary-weapon model only. Never writes live Mods resources."""
import bpy, math, json, hashlib, sys
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
HERE=Path(__file__).resolve().parent;OUT=HERE/'Generated';DEST=OUT/'SecondaryWeapons';DEST.mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(OUT/'M1Abrams.blend'))
scene=bpy.context.scene;scene.frame_set(0)
for name in ['TurretYaw','GunPitch']:bpy.data.objects[name].rotation_euler=(0,0,0)
bpy.context.view_layer.update()
def xyz(p):return Vector((p[0],-p[2],p[1]))
def des(p):return [p[0],p[2],-p[1]]
collection=bpy.data.collections.new('M1 Secondary Weapons');scene.collection.children.link(collection)
meshes=[];nodes={};anchors=[]
def move_collection(o):
 for c in list(o.users_collection):c.objects.unlink(o)
 collection.objects.link(o)
def mat(name,color,metal=.3,rough=.5):
 m=bpy.data.materials.new(name);m.diffuse_color=(*color,1);m.use_nodes=True;b=m.node_tree.nodes.get('Principled BSDF');b.inputs['Base Color'].default_value=(*color,1);b.inputs['Metallic'].default_value=metal;b.inputs['Roughness'].default_value=rough
 return m
sand=mat('M1SW Desert armor',(.20,.175,.135),.12,.83);edge=mat('M1SW Edge and bracket',(.16,.14,.11),.18,.78);steel=mat('M1SW Gunmetal',(.035,.040,.036),.65,.64);rubber=mat('M1SW Seals',(.016,.020,.018),.05,.85);lens=mat('M1SW Optics',(.025,.07,.075),.55,.24);mark=mat('M1SW Labels',(.30,.24,.13),.05,.84)

def weathered(m,camouflage=False):
 """World-scale camouflage, dirt and paint chips, baked before FBX export."""
 nt=m.node_tree;n=nt.nodes;l=nt.links;b=n.get('Principled BSDF')
 pos=n.new('ShaderNodeNewGeometry').outputs['Position']
 def noise(scale,detail=3):
  t=n.new('ShaderNodeTexNoise');t.inputs['Scale'].default_value=scale;t.inputs['Detail'].default_value=detail;l.new(pos,t.inputs['Vector']);return t.outputs['Fac']
 def ramp(f,stops):
  r=n.new('ShaderNodeValToRGB');r.color_ramp.interpolation='LINEAR'
  for e in list(r.color_ramp.elements)[2:]:r.color_ramp.elements.remove(e)
  for i,(p,c) in enumerate(stops):
   e=r.color_ramp.elements[i] if i<2 else r.color_ramp.elements.new(p);e.position=p;e.color=(*c,1)
  l.new(f,r.inputs['Fac']);return r.outputs['Color']
 if camouflage:
  color=ramp(noise(3.4,1),[(.32,(.032,.038,.033)),(.405,(.032,.038,.033)),(.415,(.105,.11,.075)),(.47,(.105,.11,.075)),(.48,(.19,.155,.12)),(.62,(.19,.155,.12)),(.63,(.125,.082,.060)),(.75,(.125,.082,.060))])
 else:
  rgb=n.new('ShaderNodeRGB');rgb.outputs[0].default_value=b.inputs['Base Color'].default_value;color=rgb.outputs[0]
 dirt=ramp(noise(35,4),[(.23,(.24,.21,.18)),(.65,(1,1,1))])
 mix=n.new('ShaderNodeMixRGB');mix.blend_type='MULTIPLY';mix.inputs[0].default_value=.65;l.new(color,mix.inputs[1]);l.new(dirt,mix.inputs[2])
 chips=ramp(noise(210,2),[(.65,(0,0,0)),(.71,(.7,.7,.7)),(.79,(1,1,1))])
 chip=n.new('ShaderNodeMixRGB');l.new(chips,chip.inputs[0]);l.new(mix.outputs[0],chip.inputs[1]);chip.inputs[2].default_value=(.32,.29,.235,1)
 l.new(chip.outputs[0],b.inputs['Base Color'])
for material in [sand,edge,steel,mark]:weathered(material,material in [sand,edge])
def node(name,parent,at):
 o=bpy.data.objects.new(name,None);collection.objects.link(o);o.empty_display_type='ARROWS';o.empty_display_size=.12;o.parent=nodes.get(parent) or bpy.data.objects[parent];o.location=o.parent.matrix_world.inverted()@xyz(at);nodes[name]=o;bpy.context.view_layer.update();anchors.append(dict(name=name,parent=parent,position=list(at)));return o
def finish(o,name,parent,m,bevel=0):
 o.name=name+'_LOD0';move_collection(o);o.data.materials.append(m)
 if bevel:
  bpy.context.view_layer.objects.active=o;mod=o.modifiers.new('Machined edges','BEVEL');mod.width=bevel;mod.segments=2;bpy.ops.object.modifier_apply(modifier=mod.name)
 bpy.context.view_layer.update();matrix=o.matrix_world.copy();o.parent=nodes[parent];o.matrix_world=matrix;meshes.append(o);return o
def box(name,p,size,parent,m=sand,bevel=.012):
 bpy.ops.mesh.primitive_cube_add(size=1,location=xyz(p));o=bpy.context.object;o.dimensions=(size[0],size[2],size[1]);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);return finish(o,name,parent,m,bevel)
def cyl(name,a,b,r,parent,m=steel,n=20):
 a,b=xyz(a),xyz(b);d=b-a;bpy.ops.mesh.primitive_cylinder_add(vertices=n,radius=r,depth=d.length,location=(a+b)/2);o=bpy.context.object;o.rotation_mode='QUATERNION';o.rotation_quaternion=d.to_track_quat('Z','Y');return finish(o,name,parent,m)
def tube(name,a,b,outer,inner,parent,m=sand,n=24):
 a,b=xyz(a),xyz(b);axis=(b-a).normalized();side=axis.cross(Vector((1,0,0)))
 if side.length<.01:side=axis.cross(Vector((0,1,0)))
 side.normalize();up=axis.cross(side);vs=[]
 for p,r in [(a,outer),(b,outer),(a,inner),(b,inner)]:
  vs.extend([p+(side*math.cos(i*math.tau/n)+up*math.sin(i*math.tau/n))*r for i in range(n)])
 fs=[]
 for i in range(n):
  j=(i+1)%n;fs.extend([(i,j,n+j,n+i),(2*n+i,3*n+i,3*n+j,2*n+j),(n+i,n+j,3*n+j,3*n+i),(i,2*n+i,2*n+j,j)])
 me=bpy.data.meshes.new(name);me.from_pydata(vs,[],fs);me.update();uv=me.uv_layers.new(name='UVMap')
 for poly in me.polygons:
  for j,li in enumerate(poly.loop_indices):uv.data[li].uv=((j==1 or j==2)*1.0,(j>=2)*1.0)
 o=bpy.data.objects.new(name,me);scene.collection.objects.link(o);return finish(o,name,parent,m)

def plinth(name,x,z,bottom,top,width,depth,parent,material=sand,taper=.7):
 # Sloped armor panels make the mount part of the turret's visual language.
 corners=[(-1,-.65),(-.65,-1),(.65,-1),(1,-.65),(1,.65),(.65,1),(-.65,1),(-1,.65)]
 vs=[xyz((x+dx*width*.5*scale,y,z+dz*depth*.5*scale)) for y,scale in [(bottom,1),(top,taper)] for dx,dz in corners]
 faces=[tuple(reversed(range(8))),tuple(range(8,16))]+[(i,(i+1)%8,(i+1)%8+8,i+8) for i in range(8)]
 me=bpy.data.meshes.new(name);me.from_pydata(vs,[],faces);me.update();o=bpy.data.objects.new(name,me);scene.collection.objects.link(o);return finish(o,name,parent,material)
# Mount lies left of the cupola; four feet individually meet the original roof.
mgx=-1.25;mgz=.30
node('RoofMGBase','TurretYaw',(mgx,2.28,mgz));node('RoofMGYaw','RoofMGBase',(mgx,2.61,mgz));node('RoofMGPitch','RoofMGYaw',(mgx,2.89,mgz-.20));node('RoofMGMuzzle','RoofMGPitch',(mgx,2.89,mgz+1.40));node('RoofMGSight','RoofMGYaw',(mgx+.23,2.93,mgz+.20))
bpy.context.view_layer.update();turret=bpy.data.objects['Turret_LOD0'];m=turret.data;m.calc_loop_triangles();roof=BVHTree.FromPolygons([turret.matrix_world@v.co for v in m.vertices],[list(t.vertices) for t in m.loop_triangles],all_triangles=True)
feet=[]
for ix,dx in enumerate([-.17,.17]):
 for iz,dz in enumerate([-.19,.19]):
  x,z=mgx+dx,mgz+dz;hit=roof.ray_cast(xyz((x,6,z)),Vector((0,0,-1)));height=hit[0].z if hit[0] else 2.20;feet.append(height)
  box(f'MG_Foot_{ix}{iz}',(x,height+.015,z),(.11,.04,.13),'RoofMGBase',edge,.004)
  cyl(f'MG_BaseBolt_{ix}{iz}',(x,height+.04,z),(x,height+.057,z),.021,'RoofMGBase',steel,6)
plinth('MG_ArmoredPedestal',mgx,mgz,min(feet)+.015,2.555,.48,.51,'RoofMGBase',sand,.67)
box('MG_BasePlate',(mgx,2.565,mgz),(.37,.045,.40),'RoofMGBase',edge,.006)
cyl('MG_TraverseRing',(mgx,2.59,mgz),(mgx,2.68,mgz),.205,'RoofMGYaw',edge,32)
box('MG_CradleFloor',(mgx,2.72,mgz-.03),(.47,.06,.60),'RoofMGYaw',edge)
for sign in [-1,1]:
 box('MG_Yoke_'+str(sign),(mgx+sign*.20,2.87,mgz-.20),(.075,.31,.22),'RoofMGYaw')
 cyl('MG_Pivot_'+str(sign),(mgx+sign*.18,2.98,mgz-.20),(mgx+sign*.255,2.98,mgz-.20),.07,'RoofMGYaw',steel)
box('MG_Receiver',(mgx,2.89,mgz+.11),(.22,.19,.49),'RoofMGPitch',steel)
box('MG_TopCover',(mgx,3.005,mgz+.08),(.255,.055,.43),'RoofMGPitch',sand)
box('MG_RearBuffer',(mgx,2.88,mgz-.19),(.16,.13,.15),'RoofMGPitch',edge)
cyl('MG_BarrelShroud',(mgx,2.89,mgz+.35),(mgx,2.89,mgz+.75),.063,'RoofMGPitch',steel,20)
cyl('MG_Barrel',(mgx,2.89,mgz+.74),(mgx,2.89,mgz+1.28),.025,'RoofMGPitch',steel,16)
for i in range(7):
 z=mgz+.39+i*.049;cyl('MG_CoolingRing_'+str(i),(mgx,2.89,z),(mgx,2.89,z+.019),.071,'RoofMGPitch',edge,16)
tube('MG_MuzzleBrake',(mgx,2.89,mgz+1.27),(mgx,2.89,mgz+1.40),.048,.026,'RoofMGPitch',steel,20)
box('MG_AmmoBox',(mgx-.36,2.78,mgz-.015),(.25,.31,.35),'RoofMGYaw',sand)
box('MG_AmmoLid',(mgx-.36,2.95,mgz-.015),(.275,.035,.375),'RoofMGYaw',edge,.007)
box('MG_AmmoLatch',(mgx-.49,2.83,mgz+.08),(.035,.09,.06),'RoofMGYaw',steel,.005)
# Feed chute stays on the gun cradle, with a flexible visual loop to receiver.
for i in range(7):
 t=i/6;x=mgx-.25+t*.15;y=2.86+.08*math.sin(t*math.pi);box('MG_FeedSegment_'+str(i),(x,y,mgz-.025),(.025,.035,.10),'RoofMGPitch',edge,.003)
box('MG_OpticHousing',(mgx+.265,2.965,mgz+.12),(.145,.17,.21),'RoofMGYaw',sand)
cyl('MG_OpticGlass',(mgx+.265,2.99,mgz+.224),(mgx+.265,2.99,mgz+.232),.043,'RoofMGYaw',lens,20)
# Compact twin canisters with independent elevation, fixed azimuth to turret.
node('AAMount','TurretYaw',(1.10,2.18,-1.30));node('AAPitch','AAMount',(1.10,2.62,-1.92))
for j,x in enumerate([.94,1.26]):
 node('AAMuzzle'+('L' if j==0 else 'R'),'AAPitch',(x,2.72,-.43))
for i,x in enumerate([.87,1.33]):
 box('AA_BaseRail_'+str(i),(x,2.26,-1.52),(.085,.065,.84),'AAMount',edge,.005)
for j,z in enumerate([-1.78,-1.17]):
 for i,x in enumerate([.85,1.35]):
  # Mount is shifted 35 cm forward below; sample its final roof contact.
  hit=roof.ray_cast(xyz((x,6,z+.35)),Vector((0,0,-1)));height=hit[0].z if hit[0] else 2.15
  box(f'AA_RoofFoot_{j}{i}',(x,height+.012,z),(.13,.04,.15),'AAMount',edge,.004)
  plinth(f'AA_RoofSaddle_{j}{i}',x,z,height+.02,2.27,.15,.27,'AAMount',sand,.7)
for sign in [-1,1]:
 plinth('AA_Yoke_'+str(sign),1.10+sign*.315,-1.84,2.27,2.65,.09,.46,'AAMount',sand,.56)
 cyl('AA_ElevationHub_'+str(sign),(1.10+sign*.27,2.62,-1.92),(1.10+sign*.375,2.62,-1.92),.098,'AAMount',steel,24)
box('AA_Crossbar',(1.10,2.62,-1.92),(.64,.085,.32),'AAPitch',edge)
for j,x in enumerate([.94,1.26]):
 tag='L' if j==0 else 'R'
 tube('AA_Canister_'+tag,(x,2.72,-2.06),(x,2.72,-.43),.145,.123,'AAPitch',sand,8)
 for k,z in enumerate([-1.88,-1.32,-.62]):tube(f'AA_Band_{tag}_{k}',(x,2.72,z-.035),(x,2.72,z+.035),.155,.144,'AAPitch',edge,8)
 tube('AA_FrontRim_'+tag,(x,2.72,-.48),(x,2.72,-.425),.152,.118,'AAPitch',steel,16)
 cyl('AA_RearSeal_'+tag,(x,2.72,-2.065),(x,2.72,-2.08),.12,'AAPitch',rubber,16)
 cyl('AA_Interior_'+tag,(x,2.72,-.62),(x,2.72,-.60),.117,'AAPitch',rubber,20)
 # Rounded nose visible behind the rim; belongs to a separate removable mesh.
 bpy.ops.mesh.primitive_uv_sphere_add(segments=16,ring_count=8,radius=.095,location=xyz((x,2.72,-.60)));o=bpy.context.object;o.scale=(1,1.35,1);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);finish(o,'AA_LoadedMissileNose_'+tag,'AAPitch',edge)
 box('AA_WarningStripe_'+tag,(x,2.868,-1.1),(.13,.006,.12),'AAPitch',mark,.001)
box('AA_Electronics',(1.10,2.35,-1.84),(.34,.12,.25),'AAMount',sand)
for j,x in enumerate([.87,1.33]):
 for k,z in enumerate([-1.78,-1.17]):
  cyl(f'AA_RailBolt_{j}_{k}',(x,2.294,z),(x,2.309,z),.021,'AAMount',steel,6)
for tag,x in [('L',.94),('R',1.26)]:
 for k,z in enumerate([-1.88,-1.32,-.62]):
  box(f'AA_ClampLatch_{tag}_{k}',(x+.149,2.72,z),(.023,.062,.05),'AAPitch',steel,.003)
# Additional cradle clearance at the -10 degree depression limit.
nodes['RoofMGPitch'].location += xyz((0,.09,0))
for anchor in anchors:
 if anchor['name'] in ['RoofMGPitch','RoofMGMuzzle']:anchor['position'][1]+=.09
# Clear the existing right-rear antenna without modifying the original mesh.
nodes['AAMount'].location += xyz((0,0,.35))
for anchor in anchors:
 if anchor['name'].startswith('AA'):anchor['position'][2]+=.35
bpy.context.view_layer.update()
# Bake the new finish to one portable atlas; procedural Blender nodes alone
# would silently disappear in FBX. Original vehicle materials are untouched.
bpy.ops.object.select_all(action='DESELECT')
for o in meshes:o.select_set(True)
bpy.context.view_layer.objects.active=meshes[0]
bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=.008)
bpy.ops.object.mode_set(mode='OBJECT')
atlas=bpy.data.images.new('M1SW Weathered camouflage',width=2048,height=2048,alpha=False)
atlas.filepath_raw=str(DEST/'M1SW-weathered-color.png');atlas.file_format='PNG'
materials=[sand,edge,steel,rubber,lens,mark];emit_nodes=[]
for material in materials:
 nt=material.node_tree;n=nt.nodes;l=nt.links;b=n.get('Principled BSDF');out=n.get('Material Output')
 tex=n.new('ShaderNodeTexImage');tex.image=atlas;n.active=tex
 emit=n.new('ShaderNodeEmission');source=b.inputs['Base Color']
 if source.is_linked:l.new(source.links[0].from_socket,emit.inputs['Color'])
 else:emit.inputs['Color'].default_value=source.default_value
 l.new(emit.outputs[0],out.inputs['Surface']);emit_nodes.append((material,tex,emit))
scene.render.engine='CYCLES';scene.cycles.samples=1;scene.render.bake.margin=12
# Join temporary copies for a single bake dispatch, retaining authored meshes.
bpy.ops.object.select_all(action='DESELECT');bake_copies=[]
for original in meshes:
 copy=original.copy();copy.data=original.data.copy();scene.collection.objects.link(copy)
 copy.select_set(True);bake_copies.append(copy)
bpy.context.view_layer.objects.active=bake_copies[0];bpy.ops.object.join();baked=bpy.context.object
bpy.ops.object.bake(type='EMIT');atlas.save();atlas.pack();bpy.data.objects.remove(baked,do_unlink=True)
for material,tex,emit in emit_nodes:
 nt=material.node_tree;b=nt.nodes.get('Principled BSDF')
 nt.links.new(tex.outputs['Color'],b.inputs['Base Color']);nt.links.new(b.outputs[0],nt.nodes.get('Material Output').inputs['Surface'])
 # Keep export materials simple and deterministic.
 for n in list(nt.nodes):
  if n not in [b,tex,nt.nodes.get('Material Output')]:nt.nodes.remove(n)
# Authored pose envelope, not a gameplay binding. Axes use Blender Z yaw / X elevation.
nodes['RoofMGYaw']['yaw_range_degrees']='-180..180; occluded sectors must be gated'
nodes['RoofMGPitch']['elevation_range_degrees']='-10..80; validate against own geometry'
nodes['AAPitch']['elevation_range_degrees']='15..65'
# Imported base already includes LODs. Add matching independent LOD meshes.
base_meshes=list(meshes)
for o in base_meshes:
 for level,ratio in [(1,.55),(2,.25)]:
  lod=o.copy();lod.data=o.data.copy();lod.name=o.name.replace('_LOD0',f'_LOD{level}');collection.objects.link(lod);bpy.context.view_layer.objects.active=lod
  mod=lod.modifiers.new('SecondaryLOD','DECIMATE');mod.ratio=ratio;mod.use_collapse_triangulate=True;bpy.ops.object.modifier_apply(modifier=mod.name);lod.hide_render=True;lod.hide_set(True)
# Delivered default pose: forward MG, elevated launch tubes.
nodes['AAPitch'].rotation_euler.x=math.radians(-20)
bpy.context.view_layer.update()
for o in scene.objects:
 if o.type=='MESH' and ('_LOD1' in o.name or '_LOD2' in o.name):o.hide_render=True;o.hide_set(True)
# FBX full vehicle and attachment-only files preserve hierarchy and packed base textures.
def export(path,attachments=False):
 bpy.ops.object.select_all(action='DESELECT');hidden=[]
 for o in scene.objects:
  if o.type not in {'MESH','EMPTY'}:continue
  if attachments and o.name not in nodes and o.name not in collection.objects and o.name not in ['M1AbramsRoot','VisualRoot','TurretYaw']:continue
  if o.hide_get():hidden.append(o);o.hide_set(False)
  o.select_set(True)
 bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,axis_forward='-Z',axis_up='Y',add_leaf_bones=False,bake_anim=not attachments,path_mode='COPY',embed_textures=True)
 for o in hidden:o.hide_set(True)
export(DEST/'M1Abrams-SecondaryWeapons.fbx');export(DEST/'M1-SecondaryWeapons-Only.fbx',True)
for im in bpy.data.images:
 if im.source=='FILE' and not im.packed_file:im.pack()
# Studio views, neutral and elevated pose.
scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=True
scene.render.resolution_x=1440;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
scene.render.film_transparent=False
scene.world.color=(.20,.20,.20)
for o in list(scene.objects):
 if o.type=='LIGHT':bpy.data.objects.remove(o,do_unlink=True)
def light(at,power,size):
 bpy.ops.object.light_add(type='AREA',location=xyz(at));o=bpy.context.object;o.data.energy=power;o.data.shape='DISK';o.data.size=size;o.rotation_euler=(xyz((0,1.5,0))-o.location).to_track_quat('-Z','Y').to_euler()
light((3,9,4),2400,7);light((-5,6,1),1500,6);light((2,5,-6),1800,5)
cam=scene.camera
if cam is None:bpy.ops.object.camera_add();cam=bpy.context.object;scene.camera=cam
cam.data.type='ORTHO'
def render(name,at,target,scale):
 cam.location=xyz(at);cam.rotation_euler=(xyz(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale;scene.render.filepath=str(DEST/name);bpy.ops.render.render(write_still=True)
# Save with a useful solid/material viewport framing and default rig pose.
cam.location=xyz((11,8,13));cam.rotation_euler=(xyz((0,1.6,.5))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=12
bpy.ops.object.select_all(action='DESELECT');nodes['RoofMGYaw'].select_set(True);bpy.context.view_layer.objects.active=nodes['RoofMGYaw']
for screen in bpy.data.screens:
 for area in screen.areas:
  if area.type=='VIEW_3D':area.spaces.active.region_3d.view_distance=10;area.spaces.active.region_3d.view_location=xyz((0,1.5,0))
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(DEST/'M1Abrams-SecondaryWeapons.blend'))
report={'status':'model_only_no_runtime_integration','base_source':'../M1Abrams.blend','coordinate_system':'model-space X right Y up Z forward; anchor positions neutral before AAPitch +20deg pose','nodes':anchors,'roof_support_heights':feet,'meshes':[]}
for o in base_meshes:
 o.data.calc_loop_triangles();report['meshes'].append({'name':o.name,'parent':o.parent.name,'triangles':len(o.data.loop_triangles)})
report['lod_triangles']=[]
for level in range(3):
 total=0
 for o in collection.objects:
  if o.type=='MESH' and o.name.endswith('_LOD'+str(level)):o.data.calc_loop_triangles();total+=len(o.data.loop_triangles)
 report['lod_triangles'].append(total)
report['original_blend_sha256']=hashlib.sha256((OUT/'M1Abrams.blend').read_bytes()).hexdigest()
(DEST/'model-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
if '--no-render' in sys.argv:
 print('MODEL BUILD COMPLETE WITHOUT RENDER');raise SystemExit(0)
render('M1-secondary-overview.png',(11,8,13),(0,1.6,.5),12)
render('M1-secondary-roof.png',(-6,7,7),(-.1,2.5,-.4),5.6)
render('M1-secondary-launcher.png',(6,5,-7),(.55,2.4,-1),4.8)
nodes['RoofMGYaw'].rotation_euler.z=math.radians(-35);nodes['RoofMGPitch'].rotation_euler.x=math.radians(-55);nodes['AAPitch'].rotation_euler.x=math.radians(-55)
render('M1-secondary-elevated.png',(8,7,9),(0,2,.1),8)
print('MODEL-ONLY BUILD COMPLETE',DEST,report['lod_triangles'])

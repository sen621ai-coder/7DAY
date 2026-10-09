"""Blender batch icon from the actual runtime geometry and source color map."""
import bpy,struct,math
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parents[2];mod=root/'ZZ-PZAEC_FlyingSword'
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
data=(mod/'Resources/juque.mesh').read_bytes();n,k=struct.unpack_from('<II',data,4);vertices=[];uv=[]
for i in range(n):
 v=struct.unpack_from('<8f',data,12+i*32);vertices.append((v[0],v[2],v[1]));uv.append(v[6:8])
indices=struct.unpack_from('<'+str(k)+'I',data,12+n*32)
mesh=bpy.data.meshes.new('Juque original');mesh.from_pydata(vertices,[],[indices[i:i+3] for i in range(0,k,3)]);mesh.update();layer=mesh.uv_layers.new()
for face in mesh.polygons:
 for li in face.loop_indices:layer.data[li].uv=uv[mesh.loops[li].vertex_index]
obj=bpy.data.objects.new('巨阙剑',mesh);bpy.context.collection.objects.link(obj)
mat=bpy.data.materials.new('Original sword');mat.use_nodes=True;shader=mat.node_tree.nodes.get('Principled BSDF');shader.inputs['Metallic'].default_value=.6097;shader.inputs['Roughness'].default_value=.3
tex=mat.node_tree.nodes.new('ShaderNodeTexImage');tex.image=bpy.data.images.load(str(mod/'Resources/color.jpg'));mat.node_tree.links.new(tex.outputs['Color'],shader.inputs['Base Color']);obj.data.materials.append(mat)
obj.rotation_euler.z=math.radians(-35)
world=bpy.context.scene.world;world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.3,.4,.5,1);world.node_tree.nodes['Background'].inputs[1].default_value=.5
for name,position,power,size in [('Key',(1,-1,2),150,2),('Fill',(-1,1,1.5),100,2)]:
 light=bpy.data.lights.new(name,'AREA');light.energy=power;light.shape='DISK';light.size=size;ob=bpy.data.objects.new(name,light);bpy.context.collection.objects.link(ob);ob.location=position;ob.rotation_euler=(-ob.location).to_track_quat('-Z','Y').to_euler()
camera=bpy.data.cameras.new('Icon camera');ob=bpy.data.objects.new('Icon camera',camera);bpy.context.collection.objects.link(ob);ob.location=(0,0,2);ob.rotation_euler=(Vector((0,0,0))-ob.location).to_track_quat('-Z','Y').to_euler();camera.type='ORTHO';camera.ortho_scale=1.1
scene=bpy.context.scene;scene.camera=ob;scene.render.engine='CYCLES';scene.cycles.samples=32;scene.render.resolution_x=256;scene.render.resolution_y=256;scene.render.resolution_percentage=100;scene.render.film_transparent=True
out=mod/'UIAtlases/ItemIconAtlas';out.mkdir(parents=True,exist_ok=True);scene.render.filepath=str(out/'meleePZAECJuque.png');bpy.ops.render.render(write_still=True)

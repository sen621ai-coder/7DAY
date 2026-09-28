"""Export approved attachment meshes in parent-local engine coordinates."""
import bpy,json,struct,shutil
from pathlib import Path
from mathutils import Vector
HERE=Path(__file__).resolve().parent
SRC=HERE/'Generated/SecondaryWeapons';RES=HERE.parents[1]/'ZZ-PZAEC_M1Abrams/Resources'
bpy.ops.wm.open_mainfile(filepath=str(SRC/'M1Abrams-SecondaryWeapons.blend'))
bpy.context.scene.frame_set(0)
report=json.loads((SRC/'model-report.json').read_text())
for name in ['TurretYaw','GunPitch','RoofMGYaw','RoofMGPitch','AAPitch']:bpy.data.objects[name].rotation_euler=(0,0,0)
bpy.context.view_layer.update()
materials=['M1SW Desert armor','M1SW Edge and bracket','M1SW Gunmetal','M1SW Seals','M1SW Optics','M1SW Labels']
def des(v):return (v.x,v.z,-v.y)
def string(f,s):
 b=s.encode();f.write(struct.pack('<i',len(b)));f.write(b)
with (RES/'M1Secondary.meshbin').open('wb') as f:
 f.write(b'M1S1');f.write(struct.pack('<i',len(report['meshes'])))
 for part in report['meshes']:
  o=bpy.data.objects[part['name']];string(f,o.name[:-5]);string(f,o.parent.name);f.write(struct.pack('<i',materials.index(o.data.materials[0].name)))
  for level in range(3):
   o=bpy.data.objects[part['name'].replace('_LOD0',f'_LOD{level}')];m=o.data;m.calc_loop_triangles()
   transform=o.parent.matrix_world.inverted()@o.matrix_world;normal=transform.to_3x3().inverted().transposed()
   vertices=[];indices=[];lookup={}
   for triangle in m.loop_triangles:
    for li in triangle.loops:
     v=transform@m.vertices[m.loops[li].vertex_index].co;n=(normal@m.corner_normals[li].vector).normalized();uv=m.uv_layers.active.data[li].uv
     key=tuple(round(float(x),7) for x in (*des(v),*des(n),*uv))
     if key not in lookup:lookup[key]=len(vertices);vertices.append(key)
     indices.append(lookup[key])
   f.write(struct.pack('<ii',len(vertices),len(indices)//3))
   for v in vertices:f.write(struct.pack('<8f',*v))
   f.write(struct.pack('<'+'i'*len(indices),*indices))
(RES/'secondary-anchors.json').write_text(json.dumps({'nodes':report['nodes']},indent=2),encoding='utf-8')
shutil.copyfile(SRC/'M1SW-weathered-color.png',RES/'secondary-color.png')
print('Exported secondary model:',len(report['meshes']),'parts',report['lod_triangles'])

import bpy,json,hashlib
from pathlib import Path
OUT=Path(__file__).resolve().parent/'Generated/SecondaryWeapons'
report=json.loads((OUT/'model-report.json').read_text());checks=[]
def check(ok,name):
 if not ok:raise AssertionError(name)
 checks.append(name)
bpy.ops.wm.open_mainfile(filepath=str(OUT/'M1Abrams-SecondaryWeapons.blend'))
for node in report['nodes']:
 o=bpy.data.objects.get(node['name']);check(o is not None and o.type=='EMPTY',node['name']+' rig node');check(o.parent.name==node['parent'],node['name']+' hierarchy')
for item in report['meshes']:
 for level in range(3):
  o=bpy.data.objects.get(item['name'].replace('_LOD0',f'_LOD{level}'));check(o is not None and o.type=='MESH',o.name+' mesh');check(len(o.data.uv_layers)>0,o.name+' UV');check(len(o.data.materials)>0,o.name+' material')
check(bpy.data.objects['GunRecoil'].animation_data is not None,'original cannon recoil action retained')
check(hashlib.sha256((OUT.parent/'M1Abrams.blend').read_bytes()).hexdigest()==report['original_blend_sha256'],'original editable model unchanged')
def check_finish(label):
 for name in ['M1SW Desert armor','M1SW Edge and bracket','M1SW Gunmetal']:
  material=bpy.data.materials[name];bsdf=material.node_tree.nodes.get('Principled BSDF');links=bsdf.inputs['Base Color'].links
  check(bool(links) and links[0].from_node.type=='TEX_IMAGE',label+' '+name+' image-based color')
  image=links[0].from_node.image
  check(image is not None and image.size[0]==2048 and image.has_data,label+' '+name+' texture pixels available')
check_finish('blend')
for file in ['M1Abrams-SecondaryWeapons.fbx','M1-SecondaryWeapons-Only.fbx']:
 bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=str(OUT/file))
 for node in report['nodes']:check(bpy.data.objects.get(node['name']) is not None,file+' imports '+node['name'])
 check(all(bpy.data.objects.get(item['name']) is not None for item in report['meshes']),file+' all LOD0 attachment objects import')
 if 'Only' in file:check(bpy.data.objects.get('Hull_LOD0') is None,'attachment export excludes original tank meshes')
 else:check(bpy.data.objects.get('Hull_LOD0') is not None and bpy.data.objects.get('Barrel_LOD0') is not None,'full export includes original tank')
 check_finish(file)
(OUT/'export-validation.json').write_text(json.dumps({'passed':len(checks),'checks':checks},indent=2),encoding='utf-8');print('PASS',len(checks),'rig, LOD, UV, material, base-preservation and FBX roundtrip checks')

from pathlib import Path
import json,struct,math,xml.etree.ElementTree as E,csv,shutil,uuid
from PIL import Image
from secondary_config import apply
ROOT=Path(__file__).resolve().parents[2];MOD=ROOT/'ZZ-PZAEC_M1Abrams';RES=MOD/'Resources';checks=0
def check(ok,message):
 global checks
 checks+=1
 assert ok,message
anchors=json.loads((RES/'secondary-anchors.json').read_text());names={'TurretYaw'}
for a in anchors['nodes']:
 check(a['parent'] in names,'parent precedes child');check(len(a['position'])==3 and all(math.isfinite(v) for v in a['position']),'finite anchor');names.add(a['name'])
report=json.loads((ROOT/'tools/M1Abrams/Generated/SecondaryWeapons/model-report.json').read_text());counts=[0,0,0]
with (RES/'M1Secondary.meshbin').open('rb') as f:
 def integer():return struct.unpack('<i',f.read(4))[0]
 def string():return f.read(integer()).decode()
 check(f.read(4)==b'M1S1','format');count=integer();check(count==len(report['meshes']),'all approved mesh parts exported')
 for part in range(count):
  name,parent=string(),string();check(parent in names,name+' parent');check(integer() in range(6),name+' material')
  for level in range(3):
   nv,nf=integer(),integer();data=[struct.unpack('<8f',f.read(32)) for _ in range(nv)];ids=struct.unpack('<'+'i'*(nf*3),f.read(nf*12));counts[level]+=nf
   check(nv>0 and nf>0 and all(0<=i<nv for i in ids),name+' index bounds');check(all(math.isfinite(v) for row in data for v in row),name+' finite mesh');check(all(-.001<=row[6]<=1.001 and -.001<=row[7]<=1.001 for row in data),name+' baked atlas UV')
   check(all(abs(sum(v*v for v in row[3:6])-1)<.005 for row in data),name+' unit normals')
 check(not f.read(),'no trailing bytes')
check(counts==report['lod_triangles'],'approved LOD triangle totals')
check((RES/'secondary-color.png').read_bytes()==(ROOT/'tools/M1Abrams/Generated/SecondaryWeapons/M1SW-weathered-color.png').read_bytes(),'approved camouflage copied exactly')
items=E.parse(MOD/'Config/items.xml');recipes=E.parse(MOD/'Config/recipes.xml');progress=E.parse(MOD/'Config/progression.xml')
baseitems={x.get('name') for x in E.parse(ROOT.parent/'Data/Config/items.xml').findall('item')}
for name,count in [('pzM1MGBelt',1),('pzM1AAMissile',2)]:
 check(len(items.findall(f"./append/item[@name='{name}']"))==1,'unique ammunition item')
 recipe=recipes.find(f"./append/recipe[@name='{name}']");check(recipe.get('count')==str(count) and recipe.get('craft_time')=='30','craft count/time');check(recipe.get('use_ingredient_modifier')=='false','no ingredient discount')
 check(all(i.get('name') in baseitems for i in recipe),'all recipe IDs exist');check(name in progress.find('./append/passive_effect').get('tags').split(','),'unlock at craftingVehicles 100')
icons=[]
for name in ('pzM1Shell','pzM1ShellAP','pzM1AAMissile'):
 icon=items.find(f"./append/item[@name='{name}']/property[@name='CustomIcon']").get('value')
 path=MOD/'UIAtlases/ItemIconAtlas'/f'{icon}.png'
 check(path.is_file(),'dedicated icon exists: '+name)
 with Image.open(path) as image:
  image.load();check(image.format=='PNG' and image.size==(256,256) and image.getbbox() is not None,'icon decodes: '+name)
  icons.append(image.tobytes())
check(len(set(icons))==3,'all three ammunition icons differ')
check(E.parse(MOD/'ModInfo.xml').find('Version').get('value')=='0.4.17','runtime release version')
stage=ROOT/'.local-tests'/('M1-secondary-config-'+uuid.uuid4().hex);shutil.copytree(MOD/'Config',stage/'ZZ-PZAEC_M1Abrams/Config');shutil.copyfile(MOD/'ModInfo.xml',stage/'ZZ-PZAEC_M1Abrams/ModInfo.xml');apply(stage)
before={p.name:p.read_bytes() for p in (stage/'ZZ-PZAEC_M1Abrams/Config').iterdir()};apply(stage);check(all((stage/'ZZ-PZAEC_M1Abrams/Config'/p).read_bytes()==data for p,data in before.items()),'config regeneration idempotent')
print('PASS',checks,'secondary runtime mesh, texture, recipe, ID and regeneration checks')

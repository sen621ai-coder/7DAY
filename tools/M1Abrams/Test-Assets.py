"""Actual source/mesh/config validation. Does not claim Unity play-mode testing."""
import struct,json,hashlib,sys,csv,xml.etree.ElementTree as ET
from pathlib import Path
import numpy as np
from source import read,HASH,SOURCE
ROOT=Path(__file__).resolve().parents[2];MOD=ROOT/'ZZ-PZAEC_M1Abrams';OUT=Path(__file__).parent/'Generated'
checks=[]
def check(value,name):
    if not value:raise AssertionError(name)
    checks.append(name)
check(hashlib.sha256(SOURCE.read_bytes()).hexdigest()==HASH,'Original GLB SHA256 unchanged')
doc,images,source=read();original=np.concatenate([p['v'] for p in source]);meta=json.loads((OUT/'parts.json').read_text());arrays=np.load(OUT/'parts.npz')
check(sum(len(arrays[f'{i}_f']) for i in range(len(meta)))==40167,'Semantic split accounts for every original triangle')
allparts=np.concatenate([arrays[f'{i}_v'] for i in range(len(meta))]);check(np.allclose(allparts.min(0),original.min(0)) and np.allclose(allparts.max(0),original.max(0)),'Semantic split preserves original bounds')
mapping=json.loads((OUT/'PartMap.json').read_text());check(len({p['component'] for p in mapping})==len(mapping)==349,'Every source main-mesh island has exactly one semantic owner')
check(sum('RoadWheel' in p['name'] for p in meta)==14,'14 independent road wheels')
check([p['mat'] for p in meta if p['name'].startswith('Track')]==[1,1],'Track alpha material is slot 1')
check(doc['materials'][1]['alphaMode']=='BLEND','Source track alpha reviewed')
with (MOD/'Resources/M1.meshbin').open('rb') as r:
    def integer():return struct.unpack('<i',r.read(4))[0]
    def string():return r.read(integer()).decode()
    check(r.read(4)==b'M1B1','Runtime binary format marker');count=integer();check(count==24,'24 runtime motion parts');triangles=[0,0,0]
    all_v=[]
    for i in range(count):
        name,parent=string(),string();px,py,pz,mat=struct.unpack('<3fi',r.read(16));check(mat in [0,1],name+' material valid')
        for level in range(3):
            nv,nf=struct.unpack('<ii',r.read(8));v=np.frombuffer(r.read(nv*32),'<f4').reshape(-1,8);f=np.frombuffer(r.read(nf*12),'<i4').reshape(-1,3)
            check(np.isfinite(v).all() and f.min()>=0 and f.max()<nv,name+f' LOD{level} finite vertices/indices')
            check(np.max(np.abs(np.linalg.norm(v[:,3:6],axis=1)-1))<.015,name+f' LOD{level} normalized normals')
            cross=np.cross(v[f[:,1],:3]-v[f[:,0],:3],v[f[:,2],:3]-v[f[:,0],:3]);dots=(cross*v[f,3:6].mean(1)).sum(1)
            check(np.median(dots)>0,name+f' LOD{level} front-face winding agrees with native Unity meshes')
            triangles[level]+=nf
            if level==0:all_v.append(v[:,:3]+[px,py,pz])
        if name=='Barrel':check(parent=='GunRecoil','Barrel follows recoil node')
        if name=='GunMount':check(parent=='GunPitch','Mantlet isolated from barrel recoil')
    check(not r.read(),'No trailing/corrupt runtime binary bytes')
points=np.concatenate(all_v);check(abs(np.ptp(points[:,0])-3.5)<.001,'3.50 metre uniform width');check(abs(points[:,1].min())<.001,'Track ground plane at zero')
check(triangles==[40167,22074,10041],'Three measured LOD triangle budgets after mantlet clearance repair')
# Apply the new append operations to the installed vanilla roots and check all
# XPath targets, entity/item links and recipe ingredients really exist.
items=ET.parse(ROOT.parent/'Data/Config/items.xml').getroot();names={x.get('name') for x in items.findall('item')}
for file in sorted(ROOT.glob('*/Config/items.xml')):
    try:names.update(x.get('name') for x in ET.parse(file).iter('item') if x.get('name'))
    except ET.ParseError:pass
for file in sorted((MOD/'Config').glob('*.xml')):
    cfg=ET.parse(file);base=ET.parse(ROOT.parent/'Data/Config'/file.name).getroot()
    for action in cfg.getroot():
        xpath=action.get('xpath');prefix='/'+base.tag;check(xpath==prefix or bool(base.findall('.'+xpath[len(prefix):])),file.name+' XPath resolves')
for ingredient in ET.parse(MOD/'Config/recipes.xml').iter('ingredient'):check(ingredient.get('name') in names,'Recipe ingredient exists: '+ingredient.get('name'))
vehicle=ET.parse(MOD/'Config/vehicles.xml').find('./append/vehicle');check(len([p for p in vehicle.findall('property') if p.get('class','').startswith('seat')])==2,'Exactly two native seats')
item_defs=ET.parse(MOD/'Config/items.xml');vehicle_defs=ET.parse(MOD/'Config/vehicles.xml');recipe_defs=ET.parse(MOD/'Config/recipes.xml')
for i,hp in enumerate([1000000,1500000,2200000,3200000]):
    entity='vehicleM1Abrams'+('' if i==0 else 'T'+str(16+i));name=entity+'Placeable'
    item=item_defs.find(f"./append/item[@name='{name}']");v=vehicle_defs.find(f"./append/vehicle[@name='{entity}']");r=recipe_defs.find(f"./append/recipe[@name='{name}']")
    check(item is not None and v is not None and r is not None,f'T{16+i} vehicle/item/recipe closure')
    check(int(item.find("effect_group/passive_effect[@name='DegradationMax']").get('value'))==hp,f'T{16+i} approved million durability')
    check(item.find("property[@class='Action1']/property[@name='Vehicle']").get('value')==entity,f'T{16+i} placement entity')
    speed=list(map(float,v.find("property[@name='velocityMax_turbo']").get('value').split(',')))
    check(abs(speed[2]*3.6-[48,50,52,54][i])<.00001,f'T{16+i} turbo km/h conversion')
    check(len([p for p in v.findall('property') if p.get('class','').startswith('seat')])==2,f'T{16+i} exactly two seats')
    previous='vehicleTruck4x4Placeable' if i==0 else 'vehicleM1Abrams'+('' if i==1 else 'T'+str(15+i))+'Placeable'
    check(r.find(f"ingredient[@name='{previous}']").get('count')=='1',f'T{16+i} consumes previous vehicle')
    check(r.find(f"ingredient[@name='PZAECBuildPartsR{i+2}']").get('count')==str([12,12,15,18][i]),f'T{16+i} progression component count')
    check(r.get('use_ingredient_modifier')=='false',f'T{16+i} no material discount')
for name in ['pzM1Shell','pzM1ShellAP']:
    r=recipe_defs.find(f"./append/recipe[@name='{name}']");check(r.get('count')=='10',name+' batch ten')
    check(item_defs.find(f"./append/item[@name='{name}']/property[@name='Stacknumber']").get('value')=='20',name+' stack twenty')
    check(not any('PZAEC' in x.get('name') for x in r.findall('ingredient')),name+' ammo does not consume progression drops')
check(len(item_defs.findall('./append/item'))==7,'Four tanks and three supply items only')
for file in ['color0.png','color1.png','metal0.png','metal1.png','ao0.png','ao1.png','normalPacked0.png','normalPacked1.png','cannon-blast.wav','cannon-mechanism.wav','cannon-ready.wav']:
    check((MOD/'Resources'/file).stat().st_size>100,file+' exists')
with (MOD/'Config/Localization.csv').open(encoding='utf8') as f:check(all(len(row)==7 for row in csv.reader(f)),'Localization column counts')
result=dict(passed=len(checks),lodTriangles=triangles,sourceUnchanged=True,checks=checks,limitations='Offline geometry/config tests; real Unity rendering, physics and multiplayer still require in-game validation.')
(OUT/'asset-test-results.json').write_text(json.dumps(result,indent=2),encoding='utf8');print('PASS',len(checks),'source/mesh/LOD/config checks')

"""Create isolated mod XML and original synthesized sound assets."""
from pathlib import Path
import xml.etree.ElementTree as ET
import copy, json, shutil, wave
import numpy as np
ROOT=Path(__file__).resolve().parents[2];MOD=ROOT/'ZZ-PZAEC_M1Abrams';CONFIG=MOD/'Config';CONFIG.mkdir(exist_ok=True)
RES=MOD/'Resources';OUT=Path(__file__).parent/'Generated'
def write(name,element):
    ET.indent(element);ET.ElementTree(element).write(CONFIG/name,encoding='utf-8',xml_declaration=True)
def prop(node,name,value):
    p=node.find(f"property[@name='{name}']")
    if p is None:p=ET.SubElement(node,'property',name=name)
    p.set('value',str(value));return p
def config(rootname):
    cfg=ET.Element('configs');return cfg,ET.SubElement(cfg,'append',xpath='/'+rootname)
cfg,a=config('entity_classes');base=ET.parse(ROOT.parent/'Data/Config/entityclasses.xml').find("entity_class[@name='vehicleTruck4x4']");entity=copy.deepcopy(base);entity.set('name','vehicleM1Abrams');prop(entity,'Prefab','M1AbramsRuntime.prefab');a.append(entity);write('entityclasses.xml',cfg)
cfg,a=config('vehicles');base=ET.parse(ROOT.parent/'Data/Config/vehicles.xml').find("vehicle[@name='vehicleTruck4x4']");v=copy.deepcopy(base);v.set('name','vehicleM1Abrams');a.append(v)
for name,val in {'cameraDistance':'9, 12','velocityMax_turbo':'8, 4, 11, 5','motorTorque_turbo':'18000, 12000, 24000, 15000','brakeTorque':'50000','steerAngleMax':'25','tiltAngleMax':'35','upAngleMax':'45','recipeName':'vehicleM1AbramsPlaceable','m1FireKey':'Mouse0','m1CameraShake':'1'}.items():prop(v,name,val)
for p in list(v.findall('property')):
    cls=p.get('class','')
    if cls in ['seat2','seat3','seat4','seat5','plow','storage']:v.remove(p);continue
    for child in list(p):
        if child.get('name','').startswith('mod'):p.remove(child)
prop(v.find("property[@class='engine']"),'fuelKmPerL','.045');prop(v.find("property[@class='fuelTank']"),'capacity','500')
for i,position in enumerate(['0, .8, 2','.45, 1.12, .2']):
    seat=v.find(f"property[@class='seat{i}']");prop(seat,'position',position);prop(seat,'exit','-2.4,0,0 ~ 2.4,0,0 ~ 0,0,-4.5 ~ 0,0,4.5 ~ 0,3,0')
write('vehicles.xml',cfg)
cfg,a=config('items')
item=ET.SubElement(a,'item',name='vehicleM1AbramsPlaceable')
for name,val in {'Extends':'vehicleTruck4x4Placeable','Tags':'vehicle,vengine,vfuel','CustomIcon':'vehicleM1AbramsPlaceable','DescriptionKey':'vehicleM1AbramsPlaceableDesc','Meshfile':'@:Entities/Vehicles/VTruck4x4/VTruck4x4P.prefab','EconomicValue':'100000','UnlockedBy':'craftingVehicles'}.items():prop(item,name,val)
act=ET.SubElement(item,'property',{'class':'Action1'});prop(act,'Vehicle','vehicleM1Abrams');prop(act,'VehicleSize','3.5, 2.5, 7.6')
effect=ET.SubElement(item,'effect_group',name='M1 Base',tiered='false');ET.SubElement(effect,'passive_effect',name='DegradationMax',operation='base_set',value='1500000');ET.SubElement(effect,'passive_effect',name='ModSlots',operation='base_set',value='2')
ammo=ET.SubElement(a,'item',name='pzM1Shell')
for name,val in {'Extends':'resourceForgedSteel','CustomIcon':'ammoRocketHE','DescriptionKey':'pzM1ShellDesc','Stacknumber':'100','EconomicValue':'500','Group':'Ammo/Weapons','UnlockedBy':'craftingVehicles'}.items():prop(ammo,name,val)
write('items.xml',cfg)
cfg,a=config('recipes')
recipe=ET.SubElement(a,'recipe',name='vehicleM1AbramsPlaceable',count='1',craft_area='workbench',craft_time='1200',tags='learnable,workbenchCrafting')
for name,count in [('vehicleTruck4x4Placeable',1),('resourceForgedSteel',300),('resourceMechanicalParts',100),('resourceElectricParts',60),('resourceSpring',50),('resourceLegendaryParts',5)]:ET.SubElement(recipe,'ingredient',name=name,count=str(count))
recipe=ET.SubElement(a,'recipe',name='pzM1Shell',count='5',craft_area='workbench',craft_time='90',tags='learnable,workbenchCrafting')
for name,count in [('resourceRocketTip',5),('resourceRocketCasing',5),('resourceGunPowder',150),('resourceForgedSteel',15)]:ET.SubElement(recipe,'ingredient',name=name,count=str(count))
write('recipes.xml',cfg)
cfg=ET.Element('configs');a=ET.SubElement(cfg,'append',xpath="/progression/crafting_skills/crafting_skill[@name='craftingVehicles']/effect_group")
ET.SubElement(a,'passive_effect',name='RecipeTagUnlocked',operation='base_set',level='100',value='1',tags='vehicleM1AbramsPlaceable,pzM1Shell');write('progression.xml',cfg)
cfg,a=config('Sounds');sound=ET.SubElement(a,'SoundDataNode',name='pzM1CannonNoise');ET.SubElement(sound,'Noise',ID='0',noise='90',time='2',muffled_when_crouched='1',heat_map_strength='1.5',heat_map_time='180');ET.SubElement(sound,'NoiseScale',value='1');write('sounds.xml',cfg)
(MOD/'ModInfo.xml').write_text('''<?xml version="1.0" encoding="UTF-8"?>
<xml><Name value="ZZPZAECM1Abrams"/><DisplayName value="PZAEC M1 Abrams"/><Description value="M1 model, dual-seat cannon and server-authoritative cargo ammunition for local V3.2."/><Author value="Artem Goyko (model); PZAEC adaptation"/><Version value="0.1.0"/></xml>''',encoding='utf8')
(CONFIG/'Localization.csv').write_text('''Key,File,Type,UsedInMainMenu,NoTranslate,english,schinese
vehicleM1Abrams,entityclasses,Vehicle,,,M1 Abrams,M1艾布拉姆斯主战坦克
vehicleM1AbramsPlaceable,items,Vehicle,,,M1 Abrams,M1艾布拉姆斯主战坦克
vehicleM1AbramsPlaceableDesc,items,Vehicle,,,Two-seat tank. Main gun ammunition goes in cargo. Gunner takes over when seated.,双座主战坦克。主炮弹放入载具货仓；单人可驾驶并开炮；第二人坐炮手位时接管主炮。左键开火，右键瞄准。
pzM1Shell,items,Ammo,,,M1 main gun shell,M1主炮弹
pzM1ShellDesc,items,Ammo,,,Dedicated M1 ammunition. Place in vehicle cargo.,M1专用主炮弹。放入坦克货仓，每发消耗1枚；装填4.5秒。
''',encoding='utf8')
# Explicit original synthesis, not a claimed recording of a real tank cannon.
rate=44100;rng=np.random.default_rng(120)
def save(name,samples):
    samples=samples/max(float(np.max(np.abs(samples))),.001)*.88
    with wave.open(str(RES/(name+'.wav')),'wb') as w:w.setnchannels(1);w.setsampwidth(2);w.setframerate(rate);w.writeframes((samples*32767).astype('<i2').tobytes())
t=np.arange(int(rate*1.8))/rate;noise=rng.normal(0,1,len(t));low=np.convolve(noise,np.ones(90)/90,mode='same')
signal=noise*np.exp(-t*65)*.8+low*np.exp(-t*4)*3+(np.sin(2*np.pi*(62*t-12*t*t))+np.sin(2*np.pi*37*t)*.4)*np.exp(-t*7)*.25
signal*=np.minimum(t/.001,1);signal[-1000:]*=np.linspace(1,0,1000);save('cannon-blast',signal)
t=np.arange(int(rate*.62))/rate;noise=rng.normal(0,1,len(t));signal=noise*(np.exp(-t*65)*.18+np.exp(-((t-.16)/.03)**2)*.1)+np.sin(2*np.pi*130*t)*np.exp(-t*12)*.15;save('cannon-mechanism',signal)
t=np.arange(int(rate*.13))/rate;signal=np.sin(2*np.pi*620*t)*np.sin(np.pi*t/.13)**2*.15;save('cannon-ready',signal)
t=np.arange(int(rate*.55))/rate;signal=rng.normal(0,1,len(t))*np.exp(-t*80)*.7
for hz,decay in [(720,14),(1130,19),(1780,25)]:signal+=np.sin(2*np.pi*hz*t)*np.exp(-t*decay)*.14
signal*=np.minimum(t/.001,1);signal[-500:]*=np.linspace(1,0,500);save('impact-ap',signal)
for key in ['color','metal','normalPacked','ao']:
    for i in range(2):shutil.copy2(OUT/f'Textures/{key}{i}.png',RES/f'{key}{i}.png')
(MOD/'ATTRIBUTION.md').write_text('''M1 Abrams by Artem Goyko
https://sketchfab.com/3d-models/m1-abrams-2577a4eccbc74b2da6dba5bfd09b7511
License recorded in original asset: CC BY 4.0 https://creativecommons.org/licenses/by/4.0/
Changes: uniform scaling, semantic separation, pivots, LODs, runtime materials, tread animation, mechanical closure parts and cannon animation.
Original GLB remains unchanged. Sound assets are original procedural synthesis, not recordings of a real M1.
''',encoding='utf8')
print('M1 isolated configuration, textures, attribution and synthesized sound assets generated')
from balance_config import apply
apply(ROOT)

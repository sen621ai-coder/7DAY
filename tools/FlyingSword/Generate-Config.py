from pathlib import Path
import xml.etree.ElementTree as E
import json,csv
root=Path(__file__).resolve().parents[2];mod=root/'ZZ-PZAEC_FlyingSword';config=mod/'Config'
def prop(parent,name,value):return E.SubElement(parent,'property',name=name,value=str(value))
def write(name,tree):E.indent(tree);E.ElementTree(tree).write(config/name,encoding='utf-8',xml_declaration=True)
r=E.Element('configs');a=E.SubElement(r,'append',xpath='/items')
carrier=E.SubElement(a,'item',name='vehiclePZAECJuquePlaceable')
for k,v in {'Extends':'vehicleTruck4x4Placeable','CreativeMode':'None','DescriptionKey':'vehiclePZAECJuque','CustomIcon':'meleeWpnBladeT3Machete','Stacknumber':1}.items():prop(carrier,k,v)
act=E.SubElement(carrier,'property',{'class':'Action1'});prop(act,'Vehicle','vehiclePZAECJuque')
for i,t in enumerate(range(16,20)):
 n=f'meleePZAECJuqueT{t}';it=E.SubElement(a,'item',name=n)
 for k,v in {'Extends':'meleeWpnBladeT3Machete','CustomIcon':'meleePZAECJuque','CustomIconTint':'FFFFFF','DescriptionKey':n+'Desc','ShowQuality':'false','Stacknumber':1,'Tags':f'T3,machete,melee,weapon,meleeWeapon,blade,light,longShaft,bladeSkill,canHaveCosmetic,attAgility,perkDeepCuts,perkFlurryOfAgility,PZAECAdvancedWeapon,PZAECJuque,PZAECTier{t}','RepairTools':'resourceRepairKit','SellableToTrader':'false'}.items():prop(it,k,v)
 act=E.SubElement(it,'property',{'class':'Action1'});prop(act,'Class','PZAECJuqueWave, PZAEC.FlyingSword')
 effect=E.SubElement(it,'effect_group',name=n,tiered='false')
 for k,v,tags in [('EntityDamage',[1000000,2500000,5500000,12000000][i],'perkDeepCuts'),('AttacksPerMinute',120,''),('StaminaLoss',12,'primary'),('MaxRange',3,'perkDeepCuts'),('BlockDamage',1,'perkDeepCuts'),('DegradationMax',[5000,6500,8500,11000][i],''),('ModSlots',6,'')]:
  e=E.SubElement(effect,'passive_effect',name=k,operation='base_set',value=str(v))
  if tags:e.set('tags',tags)
it=E.SubElement(a,'item',name='resourcePZAECSpiritStone');prop(it,'Extends','resourceForgedSteel');prop(it,'CustomIcon','resourceDiamond');prop(it,'CustomIconTint','70EDDD');prop(it,'Stacknumber',100);prop(it,'DescriptionKey','resourcePZAECSpiritStoneDesc')
act=E.SubElement(it,'property',{'class':'Action0'});prop(act,'Class','PZAECSpiritStone, PZAEC.FlyingSword')
write('items.xml',r)
r=E.Element('configs');a=E.SubElement(r,'append',xpath='/entity_classes');e=E.SubElement(a,'entity_class',name='vehiclePZAECJuque',extends='vehicleTruck4x4')
for k,v in {'Class':'PZAEC.FlyingSword.EntityJuque, PZAEC.FlyingSword','Prefab':'JuqueRuntime.prefab','Tags':'vehicle,PZAECJuque','LootList':'vehicle4x4Truck','MapIcon':'ui_game_symbol_gyrocopter','NavObject':'gyrocopter'}.items():prop(e,k,v)
write('entityclasses.xml',r)
r=E.Element('configs');a=E.SubElement(r,'append',xpath='/vehicles');v=E.SubElement(a,'vehicle',name='vehiclePZAECJuque')
source=E.parse(root/'ZZ-PZAEC_Mecha/Config/vehicles.xml').find('.//vehicle')
import copy
for p in source:
 if p.get('class') not in ('storage',):v.append(copy.deepcopy(p))
for p in list(v):
 if p.get('name','').startswith('pzMecha'):v.remove(p)
for p in v.iter('property'):
 if p.get('name')=='recipeName':p.set('value','meleePZAECJuqueT16')
 if p.get('name')=='fuelKmPerL':p.set('value','100000')
 if p.get('name')=='capacity':p.set('value','1800')
seat=v.find("property[@class='seat0']")
for p in list(seat):seat.remove(p)
for k,val in {'class':'Seat','pose':'0','position':'0, .23, 0','rotation':'0, 0, 0','exit':'0,.4,0','IKFootLPosition':'-.15,0,.12','IKFootRPosition':'.15,0,-.12','IKFootLRotation':'0,0,0','IKFootRRotation':'0,0,0'}.items():prop(seat,k,val)
write('vehicles.xml',r)
r=E.Element('configs');a=E.SubElement(r,'append',xpath='/recipes');existing=E.parse(root/'99-AEC_T16_RuntimeFix/Config/recipes.xml')
for t in range(16,20):
 recipe=copy.deepcopy(existing.find(f".//recipe[@name='meleePZAECFaultlineHammerT{t}']"));recipe.set('name',f'meleePZAECJuqueT{t}')
 for ingredient in recipe.findall('ingredient'):
  if 'meleeWpn' in ingredient.get('name',''):ingredient.set('name','meleeWpnBladeT3Machete')
  elif 'FaultlineHammer' in ingredient.get('name',''):ingredient.set('name',f'meleePZAECJuqueT{t-1}')
 a.append(recipe)
recipe=E.SubElement(a,'recipe',name='resourcePZAECSpiritStone',count='5',craft_area='workbench',craft_time='30',always_unlocked='true')
E.SubElement(recipe,'ingredient',name='resourceLegendaryParts',count='1');E.SubElement(recipe,'ingredient',name='resourceElectricParts',count='10');write('recipes.xml',r)
with (config/'Localization.csv').open('w',encoding='utf-8-sig',newline='') as f:
 w=csv.writer(f);w.writerow(['Key','File','Type','UsedInMainMenu','NoTranslate','english','schinese'])
 for t in range(16,20):
  n=f'meleePZAECJuqueT{t}';w.writerow([n,'items','Item','','',f'Juque Sword T{t}',f'巨阙剑·T{t}']);w.writerow([n+'Desc','items','Item','','','LMB melee; hold/release RMB for spirit wave. G deploy, E ride/return. One landing fall protection on return.','左键近战；右键短按剑气、长按蓄力后松开发出月牙剑气。G御出，E上剑／任意高度下剑回栏。本次落地免摔伤。御剑时WASD驾驶，空格/C升降，Shift加速，可切换枪械。'])
 for n,en,cn in [('vehiclePZAECJuque','Juque Flying Sword','巨阙剑'),('resourcePZAECSpiritStone','Spirit Stone','灵石'),('resourcePZAECSpiritStoneDesc','Use to restore 250 spirit to a sword in your toolbelt.','使用后为快捷栏中的巨阙剑恢复250灵力，优先补充手持或已御出的剑。')]:w.writerow([n,'items','Item','','',en,cn])
(config/'Localization.txt').write_bytes((config/'Localization.csv').read_bytes())
(mod/'ModInfo.xml').write_text('<xml><Name value="PZAEC_FlyingSword"/><DisplayName value="巨阙剑"/><Description value="Juque legendary sword and flying mount"/><Author value="PZAEC; model Rew"/><Version value="0.1.9"/></xml>',encoding='utf-8')
(mod/'settings.json').write_text(json.dumps({'DeployKey':'G','ViewKey':'BackQuote','RiseKey':'Space','DescendKey':'C','Effects':1,'Cruise':26,'Boost':40,'Reverse':8,'Rise':8,'Descend':6},indent=2),encoding='utf-8')

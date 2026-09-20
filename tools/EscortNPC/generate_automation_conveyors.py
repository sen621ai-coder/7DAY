from pathlib import Path
import csv
import xml.etree.ElementTree as E
root=Path(__file__).resolve().parents[2]/'97-AutomationWorkshop'
c=root/'Config'
kinds={'Straight':'直线','Left':'左转','Right':'右转','Up':'上坡','Down':'下坡'}
def prop(n,k,v):E.SubElement(n,'property',name=k,value=str(v))
def write(n,p):E.indent(n,space='  ');E.ElementTree(n).write(p,encoding='utf-8',xml_declaration=True)
trees={n:E.parse(c/(n+'.xml')).getroot() for n in ('blocks','recipes')}
ids={'yfAutoBelt'+k for k in kinds}
for tree in trees.values():
 for app in tree:
  for node in list(app):
   if node.get('name') in ids:app.remove(node)
ba=E.SubElement(trees['blocks'],'append',xpath='/blocks');ra=E.SubElement(trees['recipes'],'append',xpath='/recipes')
rows=[]
for kind,title in kinds.items():
 name='yfAutoBelt'+kind;b=E.SubElement(ba,'block',name=name)
 for k,v in dict(Class='CompositeTileEntity',Material='Msteel',Shape='ModelEntity',Model=f'#@modfolder:Resources/automation-conveyors.unity3d?Assets/Conveyor/Conveyor{kind}.prefab',CreativeMode='Player',DescriptionKey=name+'Desc',Path='solid',WaterFlow='permitted',Place='TowardsPlacerInverted',AllowedRotations='Basic90',CustomIcon='electricwirerelay',CustomIconTint='E9B547',MaxDamage='2500',Stacknumber='50',Group='Building,advBuilding',FilterTags='MC_playerBlocks,SC_electrical',SoundPickup='cratesteel_grab',SoundPlace='cratesteel_place').items():prop(b,k,v)
 features=E.SubElement(b,'property',{'class':'CompositeFeatures'})
 prop(E.SubElement(features,'property',{'class':'TEFeatureStorage'}),'LootList','yfAutoBeltBuffer')
 E.SubElement(features,'property',{'class':'TEFeatureLockable'})
 E.SubElement(features,'property',{'class':'TEFeatureAutomationState'})
 prop(E.SubElement(b,'property',{'class':'RepairItems'}),'resourceForgedIron',2)
 E.SubElement(b,'drop',event='Destroy',name='resourceScrapIron',count='2')
 r=E.SubElement(ra,'recipe',name=name,count='1',craft_area='yfAutomationWorkbench',craft_time='5',always_unlocked='true',use_ingredient_modifier='false')
 for item,count in [('yfAutoTransport',1),('resourceForgedIron',2),('resourceMechanicalParts',1)]:E.SubElement(r,'ingredient',name=item,count=str(count))
 desc='按黄色方向标记铺设，后端取输入箱或输出箱，前端接下一段或箱子。每秒前进一段、最多16件；传送带向输出箱送货无需样品，所有未锁定格可收取任意货物。连通线路任一段4格范围内有通电供电口即可，每条最多32段。只连接同一所有者、同一16×16区块；断电、满箱、打开库存时暂停。按E查看带上货物；拆除前先取空。坡道末端连接高/低一格的下一段。'
 rows.extend([[name,'blocks','Block','','false',title+'传送带',title+'传送带'],[name+'Desc','blocks','Block','','false',desc,desc]])
for name,tree in trees.items():write(tree,c/(name+'.xml'))
loot=E.Element('configs');a=E.SubElement(loot,'append',xpath='/lootcontainers')
l=E.SubElement(a,'lootcontainer',name='yfAutoBeltBuffer',count='0',size='1,1',sound_open='UseActions/crate_steel_open',sound_close='UseActions/crate_steel_close',loot_quality_template='qualBaseTemplate');E.SubElement(l,'item',name='cobweb',count='0');write(loot,c/'loot.xml')
with (c/'Localization.csv').open(encoding='utf-8-sig',newline='') as f:old=list(csv.reader(f))
old=[r for r in old if r and r[0].removesuffix('Desc') not in ids]
for r in old:
 if r[0]=='yfAutomationWorkbenchDesc':r[-2:]=['制造自动化设备、传送带和工厂零件的专用工作台。']*2
 if r[0]=='yfAutoTransportDesc':r[-2:]=['用于制造传送带和箱间输送设备的运输组件。']*2
with (c/'Localization.csv').open('w',encoding='utf-8-sig',newline='') as f:csv.writer(f).writerows(old+rows)
info=E.parse(root/'ModInfo.xml').getroot();info.find('Version').set('value','0.5.0');write(info,root/'ModInfo.xml')

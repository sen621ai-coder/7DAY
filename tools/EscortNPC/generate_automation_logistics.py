from pathlib import Path
import xml.etree.ElementTree as E,csv
from automation_config_helpers import remove_inherited_unlocks
root=Path(__file__).resolve().parents[2]/'97-AutomationWorkshop';c=root/'Config'
def write(node,path):E.indent(node,space='  ');E.ElementTree(node).write(path,encoding='utf-8',xml_declaration=True)
def prop(node,k,v):E.SubElement(node,'property',name=k,value=str(v))
names={'yfAutoInput','yfAutoOutput','yfAutoSorter','yfAutoPowerPort'}
blocks=E.parse(c/'blocks.xml').getroot();recipes=E.parse(c/'recipes.xml').getroot()
for tree in (blocks,recipes):
 for app in tree:
  for n in list(app):
   if n.get('name') in names:app.remove(n)
ba=E.SubElement(blocks,'append',xpath='/blocks');ra=E.SubElement(recipes,'append',xpath='/recipes')
loc=[]
for id,title,tint,desc,cost in [
 ('yfAutoInput','自动化输入箱','47C5D8','紧贴分拣机放置，投入待分拣物品；需与分拣机和输出箱由同一玩家放置。打开箱子时暂停搬运。',[('yfAutoFrame',1),('resourceWood',20)]),
 ('yfAutoOutput','自动化输出箱','65CC85','第一格放一个样品，分拣机只接收相同物品类型，样品不消耗。其余格储存产物。放在分拣机水平4米、上下1米内及同一区块。多个匹配箱优先距离较近的。',[('yfAutoFrame',1),('resourceWood',20)]),
 ('yfAutoSorter','自动分拣机','E2B85B','邻接输入箱和通电的自动化供电接口，按输出箱第一格样品分类；每秒最多搬16件。箱子需同一所有者、同一16×16地图区块。显示运行状态，本体不储物。',[('yfAutoFrame',2),('yfAutoMotor',1),('yfAutoController',1),('yfAutoTransport',1)]),
 ('yfAutoPowerPort','自动化供电接口','D7B657','用接线工具连接发电机，紧贴分拣机的任意一面供电。每个接口需要10W，断电暂停；接口本身不储物。',[('resourceForgedIron',5),('resourceElectricParts',10),('yfAutoController',1)])]:
 b=E.SubElement(ba,'block',name=id)
 if id=='yfAutoPowerPort':
  for k,v in [('Extends','electricwirerelay'),('RequiredPower',10),('UnlockedBy',''),('CustomIcon','electricwirerelay')]:prop(b,k,v)
 else:
  for k,v in [('Class','CompositeTileEntity'),('Material','Msteel'),('Shape','ModelEntity'),('Model','@:Entities/LootContainers/steelWritableCratePrefab.prefab'),('WaterFlow','permitted'),('Path','solid'),('Place','TowardsPlacerInverted'),('AllowedRotations','Basic90'),('CustomIcon','cntSteelWritableCrate'),('SoundPickup','cratesteel_grab'),('SoundPlace','cratesteel_place'),('Group','Building,advBuilding'),('FilterTags','MC_playerBlocks,SC_decor')]:prop(b,k,v)
  features=E.SubElement(b,'property',{'class':'CompositeFeatures'})
  if id!='yfAutoSorter':prop(E.SubElement(features,'property',{'class':'TEFeatureStorage'}),'LootList','playerWoodWritableStorage')
  E.SubElement(features,'property',{'class':'TEFeatureLockable'})
  sign=E.SubElement(features,'property',{'class':'TEFeatureSignable'})
  for k,v in [('FontSize',75),('LineCount',3),('LineWidth',0.55),('LineSpacing',1)]:prop(sign,k,v)
  repair=E.SubElement(b,'property',{'class':'RepairItems'});prop(repair,'resourceForgedIron',10)
  E.SubElement(b,'drop',event='Destroy',name='resourceScrapIron',count='5,10')
 for k,v in [('CreativeMode','Player'),('DescriptionKey',id+'Desc'),('CustomIconTint',tint),('TintColor',tint),('MaxDamage',2500),('EconomicValue',200),('Stacknumber',10)]:prop(b,k,v)
 r=E.SubElement(ra,'recipe',name=id,count='1',craft_area='yfAutomationWorkbench',craft_time='30',always_unlocked='true',use_ingredient_modifier='false')
 for item,count in cost:E.SubElement(r,'ingredient',name=item,count=str(count))
 loc += [[id,'blocks','Block','','false',title,title],[id+'Desc','blocks','Block','','false',desc,desc]]
remove_inherited_unlocks(blocks,root.parent.parent/'Data/Config/blocks.xml')
write(blocks,c/'blocks.xml');write(recipes,c/'recipes.xml')
with (c/'Localization.csv').open(encoding='utf-8-sig',newline='') as f:rows=list(csv.reader(f))
rows=[r for r in rows if r and r[0].removesuffix('Desc') not in names]
with (c/'Localization.csv').open('w',encoding='utf-8-sig',newline='') as f:csv.writer(f).writerows(rows+loc)
p=root/'ModInfo.xml';tree=E.parse(p).getroot();tree.find('Version').set('value','0.2.0');tree.find('Description').set('value','自动化工作台、通用零件和供电驱动的样品分拣流水线。');E.SubElement(tree,'SkipWithAntiCheat',value='true') if tree.find('SkipWithAntiCheat') is None else None;write(tree,p)

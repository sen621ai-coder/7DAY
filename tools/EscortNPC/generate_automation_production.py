from pathlib import Path
import xml.etree.ElementTree as E
import csv,copy
root=Path(__file__).resolve().parents[2]/'97-AutomationWorkshop';c=root/'Config'
def write(node,path):
 E.indent(node,space='  ');E.ElementTree(node).write(path,encoding='utf-8',xml_declaration=True)
def prop(n,k,v):E.SubElement(n,'property',name=k,value=str(v))
machines=[('yfAutoKitchen','自动厨房','FFAD80',[('yfAutoFrame',2),('yfAutoController',1),('yfAutoPump',1)],'首格样品选菜品，邻接输入箱放食材及烹饪锅/烤架等工具。沿用已解锁的篝火配方，所有者在线时生产。'),
 ('yfAutoSmelter','自动冶炼机','ECA558',[('yfAutoFrame',3),('yfAutoController',1),('yfAutoMotor',1)],'输出箱首格放自动化冶炼料样品，输入同类原料。按原版冶炼重量生产；装备不熔炼。样品在自动化工作台制作。'),
 ('yfAutoForge','自动锻造机','D79565',[('yfAutoFrame',3),('yfAutoController',2),('yfAutoMotor',1)],'沿用锻炉已解锁配方。原版炉内材料用同数量自动化冶炼料代替；需要坩埚的配方仍须输入箱有坩埚。关闭原版冶炼系统时使用原版实物材料。所有者须在线。'),
 ('yfAutoRecycler','自动装备分解机','C299DB',[('yfAutoFrame',2),('yfAutoController',1),('yfAutoCutter',1)],'放入待分解装备后自动识别原版拆解产物，无需选择材料或放样品。一次分解一件，混合装备依次处理。跳过六品质及以上、带模组、装有弹药或锁定格装备。'),
 ('yfAutoFarm','自动农场控制器','8ACC72',[('yfAutoFrame',2),('yfAutoController',1),('yfAutoPump',2)],'扫描周围7×7、上下1米的成熟玩家作物，仅处理自己领地石保护内且同区块的作物。每10秒收割补种一株，输入箱消耗1颗对应种子，产出原版基础收获，不叠加玩家收割加成。先手动播种。'),
 ('yfAutoMiner','自动矿点采集机','8EA3C4',[('yfAutoFrame',4),('yfAutoController',2),('yfAutoMotor',2),('yfAutoCutter',2)],'检查下方1至8米、周围7×7内自己领地中的真实矿块。每60秒消耗1份钻头耗材产出20矿料。支持铁/铅/煤/硝石/油页岩。矿点持续产出，不破坏或耗尽矿块。'),
 ('yfAutoTransfer','箱间输送器','7DBED4',[('yfAutoFrame',1),('yfAutoMotor',1),('yfAutoTransport',2)],'邻接上游输出箱和下游输入箱，自动将产物送往下一道工序，每秒最多16件。保留上游首格样品；仅连接同一区块、同一所有者的箱子。复用箱体外观，无可见传送带动画。')]
materials=[('iron','铁','resourceScrapIron'),('brass','黄铜','resourceScrapBrass'),('lead','铅','resourceScrapLead'),('glass','玻璃','resourceCrushedSand'),('stone','石','resourceRockSmall'),('clay','黏土','resourceClayLump')]
ids={m[0] for m in machines}|{'yfAutoIngot_'+m[0] for m in materials}|{'yfAutoDrillCharge'}
trees={name:E.parse(c/(name+'.xml')).getroot() for name in ('blocks','items','recipes')}
for tree in trees.values():
 for app in tree:
  for n in list(app):
   if n.get('name') in ids:app.remove(n)
apps={name:E.SubElement(tree,'append',xpath='/'+name) for name,tree in trees.items()}
template=trees['blocks'].find(".//block[@name='yfAutoSorter']")
loc=[]
def recipe(id,cost):
 r=E.SubElement(apps['recipes'],'recipe',name=id,count='1',craft_area='yfAutomationWorkbench',craft_time='30',always_unlocked='true',use_ingredient_modifier='false')
 for item,count in cost:E.SubElement(r,'ingredient',name=item,count=str(count))
def localize(id,title,desc,kind):loc.extend([[id,kind,kind,'','false',title,title],[id+'Desc',kind,kind,'','false',desc,desc]])
for id,title,tint,cost,desc in machines:
 b=copy.deepcopy(template);b.set('name',id);apps['blocks'].append(b)
 for k,v in [('DescriptionKey',id+'Desc'),('CustomIconTint',tint),('TintColor',tint)]:b.find(f"property[@name='{k}']").set('value',v)
 E.SubElement(b.find("property[@class='CompositeFeatures']"),'property',{'class':'TEFeatureAutomationState'})
 recipe(id,cost)
 localize(id,title,desc+' 4格范围内放通电的供电接口；输入紧贴机器，输出水平4米上下1米内；同一所有者、同一区块。输出首格样品保留，缺电/满箱暂停，进度随存档保存。','blocks')
for category,title,raw in materials:
 id='yfAutoIngot_'+category;b=E.SubElement(apps['items'],'item',name=id)
 for k,v in [('Extends','resourceMechanicalParts'),('CustomIcon',raw),('CustomIconTint','E8BF6C'),('DescriptionKey',id+'Desc'),('Weight',0),('Stacknumber',30000),('EconomicValue',0),('SellableToTrader','false')]:prop(b,k,v)
 recipe(id,[(raw,1)])
 localize(id,'自动化冶炼料·'+title,'锻造流水线中间料，每件等同原版锻炉1单位。工作台配方只用于制作首格样品；批量生产请用自动冶炼机。','items')
id='yfAutoDrillCharge';b=E.SubElement(apps['items'],'item',name=id)
for k,v in [('Extends','resourceMechanicalParts'),('DescriptionKey',id+'Desc'),('CustomIcon','resourceMechanicalParts'),('CustomIconTint','83B9DF'),('Stacknumber',100),('Weight',0),('SellableToTrader','false')]:prop(b,k,v)
recipe(id,[('resourceForgedIron',1),('resourceMechanicalParts',1)])
localize(id,'钻头耗材','自动矿点采集机每次60秒作业消耗1份，产出20个对应矿料。','items')
for name,tree in trees.items():write(tree,c/(name+'.xml'))
with (c/'Localization.csv').open(encoding='utf-8-sig',newline='') as f:rows=list(csv.reader(f))
rows=[r for r in rows if r and r[0].removesuffix('Desc') not in ids]
with (c/'Localization.csv').open('w',encoding='utf-8-sig',newline='') as f:csv.writer(f).writerows(rows+loc)
p=root/'ModInfo.xml';tree=E.parse(p).getroot();tree.find('Version').set('value','0.3.0');tree.find('Description').set('value','自动化工作台、分拣输送、厨房、农场、矿点采集、装备分解及冶炼锻造。');write(tree,p)

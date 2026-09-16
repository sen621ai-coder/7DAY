from pathlib import Path
import xml.etree.ElementTree as E
import csv,copy
root=Path(__file__).resolve().parents[2]/'97-AutomationWorkshop';c=root/'Config'
def write(n,p):E.indent(n,space='  ');E.ElementTree(n).write(p,encoding='utf-8',xml_declaration=True)
def prop(n,k,v):E.SubElement(n,'property',name=k,value=str(v))
devices=[
 ('yfAutoWaterPump','自动化抽水泵','69BCE6',[('yfAutoFrame',2),('yfAutoPump',2),('yfAutoController',1)],'紧贴水体和通电的供电接口，每5秒抽取1份灌溉水，送往水平4米上下1米内的同主储水箱。无需输入箱或样品，不消耗水体。不生产饮用水。'),
 ('yfAutoWaterTank','自动化储水箱','7CAFD2',[('yfAutoFrame',2),('yfAutoPump',1)],'原生箱子存放灌溉水，水泵自动填充上限200份。无需通电，箱子被打开或锁定格时暂停取放。放在农场控制器水平4米上下1米内；每株收割补种消耗1份水，将作业时间由10秒缩短到5秒，缺水恢复原速度。不改变原版作物生长周期。'),
 ('yfAutoAmmoFeed','炮塔外接供弹器','D58B71',[('yfAutoFrame',2),('yfAutoController',2),('yfAutoTransport',2)],'紧贴原版炮塔、弹药输入箱和通电的供电接口。供弹器通电且有匹配弹药时自动启用炮塔；炮塔本身也须通电。优先使用炮塔内装弹药，耗尽后由服务器每次开火直接扣箱中1发。保持原版敌我设置。可用输出箱供弹且保留样品；开箱/锁定格暂停供弹。原版装弹面板不会显示外接箱库存。')]
ids={x[0] for x in devices}|{'yfAutoIrrigationWater'}
trees={name:E.parse(c/(name+'.xml')).getroot() for name in ('blocks','items','recipes')}
for tree in trees.values():
 for app in tree:
  for node in list(app):
   if node.get('name') in ids:app.remove(node)
apps={name:E.SubElement(tree,'append',xpath='/'+name) for name,tree in trees.items()}
loc=[]
def localize(id,title,desc,kind):loc.extend([[id,kind,kind,'','false',title,title],[id+'Desc',kind,kind,'','false',desc,desc]])
for id,title,tint,cost,desc in devices:
 template='yfAutoInput' if id=='yfAutoWaterTank' else 'yfAutoSorter'
 b=copy.deepcopy(trees['blocks'].find(f".//block[@name='{template}']"));b.set('name',id);apps['blocks'].append(b)
 for k,v in [('DescriptionKey',id+'Desc'),('CustomIconTint',tint),('TintColor',tint)]:b.find(f"property[@name='{k}']").set('value',v)
 if id=='yfAutoWaterPump':E.SubElement(b.find("property[@class='CompositeFeatures']"),'property',{'class':'TEFeatureAutomationState'})
 r=E.SubElement(apps['recipes'],'recipe',name=id,count='1',craft_area='yfAutomationWorkbench',craft_time='30',always_unlocked='true',use_ingredient_modifier='false')
 for item,count in cost:E.SubElement(r,'ingredient',name=item,count=str(count))
 localize(id,title,desc+' 关联设备和箱子须同一所有者、同一个16×16地图区块。','blocks')
b=E.SubElement(apps['items'],'item',name='yfAutoIrrigationWater')
for k,v in [('Extends','resourceMechanicalParts'),('DescriptionKey','yfAutoIrrigationWaterDesc'),('CustomIcon','drinkJarRiverWater'),('CustomIconTint','69BCE6'),('Stacknumber',200),('Weight',0),('EconomicValue',0),('SellableToTrader','false')]:prop(b,k,v)
localize('yfAutoIrrigationWater','灌溉用水','水泵从邻接水体抽取，存入储水箱。用于自动农场快速收割补种，不能饮用。','items')
for name,tree in trees.items():write(tree,c/(name+'.xml'))
with (c/'Localization.csv').open(encoding='utf-8-sig',newline='') as f:rows=list(csv.reader(f))
rows=[r for r in rows if r and r[0].removesuffix('Desc') not in ids]
with (c/'Localization.csv').open('w',encoding='utf-8-sig',newline='') as f:csv.writer(f).writerows(rows+loc)
p=root/'ModInfo.xml';tree=E.parse(p).getroot();tree.find('Version').set('value','0.4.1');write(tree,p)

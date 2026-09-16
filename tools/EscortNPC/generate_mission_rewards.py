from pathlib import Path
import xml.etree.ElementTree as E
import copy,csv,io
root=Path(__file__).resolve().parents[2]
config=root/'96-SakuraPreview/Config'
def write(tree,path):
 E.indent(tree,space='  '); E.ElementTree(tree).write(path,encoding='utf-8',xml_declaration=True)
def prop(node,name,value): E.SubElement(node,'property',name=name,value=value)
quests=E.parse(config/'quests.xml').getroot()
for app in quests:
 for q in list(app):
  if q.tag=='quest' and ('Dispatch' in q.get('id','')):app.remove(q)
items=E.Element('configs');ia=E.SubElement(items,'append',xpath='/items')
loot=E.Element('configs');la=E.SubElement(loot,'append',xpath='/lootcontainers')
loc=[]
for tier in range(16,20):
 group=E.SubElement(la,'lootgroup',name=f'SakuraMissionRewardT{tier}',count='1')
 E.SubElement(group,'item',name=f'PZAECChallengeVoucherT{tier}',count='1',prob='1')
 for kind,title in [('sakura','小樱护送'),('mint','Mint 守护')]:
  item=f'{kind}MissionBlueprintT{tier}';qid=f'{kind}DispatchT{tier}'
  E.SubElement(group,'item',name=item,count='1',prob='1')
  node=E.SubElement(ia,'item',name=item)
  for key,val in [('Extends','questMaster'),('DescriptionKey',item+'Desc'),('CustomIcon','cntBookPile08'),('Stacknumber','10'),('SellableToTrader','false')]:prop(node,key,val)
  action=E.SubElement(node,'property',{'class':'Action0'})
  for key,val in [('Class','Quest'),('QuestGiven',qid),('Delay','0.5'),('UseAnimation','false')]:prop(action,key,val)
  template=quests.find(f".//quest[@id='{('sakuraEscort' if kind=='sakura' else 'mintGuard')}T{tier}']")
  q=copy.deepcopy(template);q.set('id',qid)
  for key in ('name_key','subtitle_key'):q.find(f"property[@name='{key}']").set('value',item)
  q.find("property[@name='description_key']").set('value',item+'Desc')
  quests.find('append').append(q)
  desc=f'完成 T{tier} 商人任务时，三种任务图纸等概率随机获得一张。使用并接受后请求服务器生成角色及坐标，固定 T{tier} 难度，在线同队自动共享；已有任务时排队。到标记处对话开始。'
  loc += [[item,'items','UI','','false',f'{title}任务图纸 T{tier}',f'{title}任务图纸 T{tier}'],[item+'Desc','items','UI','','false',desc,desc]]
write(quests,config/'quests.xml');write(items,config/'items.xml');write(loot,config/'loot.xml')
write(E.Element('configs'),config/'recipes.xml')
# This patch runs in 99 after its own T16-T19 quest definitions, including affix quests.
path=root/'99-AEC_T16_RuntimeFix/Config/quests.xml';qroot=E.parse(path).getroot()
for a in list(qroot):
 if any(r.get('id','').startswith('SakuraMissionRewardT') for r in a.findall('reward')):qroot.remove(a)
write(qroot,path)
p=config/'Localization.csv'
rows=list(csv.reader(io.StringIO(p.read_text(encoding='utf-8-sig'))))
rows=[r for r in rows if not r or not r[0].startswith(('sakuraMissionBlueprint','mintMissionBlueprint','sakuraDispatch_','mintDispatch_'))]
with p.open('w',encoding='utf-8-sig',newline='') as f:csv.writer(f).writerows(rows+loc)
print('Generated: four three-choice reference pools, eight tiered blueprint items, no crafting recipes.')

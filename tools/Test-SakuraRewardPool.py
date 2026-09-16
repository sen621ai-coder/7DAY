from pathlib import Path
import xml.etree.ElementTree as E,re
root=Path(__file__).resolve().parents[1]
c=root/'96-SakuraPreview/Config';items=E.parse(c/'items.xml');quests=E.parse(c/'quests.xml');loot=E.parse(c/'loot.xml')
assert not E.parse(c/'recipes.xml').findall('.//recipe')
allids=set();allitems=set()
for path in root.glob('*/Config/quests.xml'):
 try:allids.update(q.get('id') for q in E.parse(path).findall('.//quest'))
 except E.ParseError:pass
for path in root.glob('*/Config/items.xml'):
 try:allitems.update(q.get('name') for q in E.parse(path).findall('.//item'))
 except E.ParseError:pass
runtime=E.parse(root/'99-AEC_T16_RuntimeFix/Config/quests.xml')
for t in range(16,20):
 pool=loot.find(f".//lootgroup[@name='SakuraMissionRewardT{t}']")
 assert pool is not None and pool.get('count')=='1' and len(pool)==3
 assert len(set(i.get('name') for i in pool))==3
 for i in pool:assert i.get('count')=='1' and i.get('prob')=='1' and i.get('name') in allitems
 patches=[a for a in runtime.getroot() if a.find(f"reward[@id='SakuraMissionRewardT{t}']") is not None]
 assert len(patches)==0 # Original voucher event must remain the only award path.
 event=E.parse(root/'99-AEC_T16_RuntimeFix/Config/gameevents.xml').find(f".//action_sequence[@name='PZAECGiveVoucherT{t}']")
 assert event.find("action[@class='AddItems']/property[@name='added_items']").get('value')==f'PZAECChallengeVoucherT{t}'
 assert event.find("action[@class='AddItems']/property[@name='added_item_counts']").get('value')=='1'
 for kind in ('sakura','mint'):
  node=items.find(f".//item[@name='{kind}MissionBlueprintT{t}']")
  assert node.find(".//property[@name='QuestGiven']").get('value')==f'{kind}DispatchT{t}'
  assert quests.find(f".//quest[@id='{kind}DispatchT{t}']") is not None
print('PASS: original voucher event preserved, no extra quest reward, three choices per tier, eight fixed-tier blueprints, no crafting.')

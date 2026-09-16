from pathlib import Path
import xml.etree.ElementTree as E
import csv
root=Path(__file__).resolve().parents[1]
c=root/'97-AutomationWorkshop/Config';game=root.parent/'Data/Config'
baseblocks={n.get('name') for n in E.parse(game/'blocks.xml').findall('./block')}
baseitems={n.get('name') for n in E.parse(game/'items.xml').findall('./item')}
b=E.parse(c/'blocks.xml').find('.//block');assert b.get('name')=='yfAutomationWorkbench'
assert b.find("property[@name='Class']").get('value')=='Workstation'
assert b.find("property[@name='MultiBlockDim']").get('value')=='2,2,1'
for id in ('yfAutomationWorkbench','yfAutoPowerPort'):
 block=E.parse(c/'blocks.xml').find(f".//block[@name='{id}']")
 assert block.find("property[@name='Extends']") is None, 'Must not re-inherit skill unlocks'
 assert block.find("property[@name='UnlockedBy']") is None, 'Empty unlock crashes native recipe UI'
for prop in E.parse(c/'blocks.xml').findall(".//property[@name='UnlockedBy']"):
 assert all(part.strip() for part in prop.get('value','').split(','))
assert b.find("property[@class='Workstation']/property[@name='CraftingAreaRecipes']").get('value')=='yfAutomationWorkbench'
items=E.parse(c/'items.xml').findall('.//item');assert len(items)==14
for item in items:assert item.find("property[@name='Extends']").get('value') in baseitems
names=baseitems|baseblocks|{v.get('name') for v in E.parse(c/'blocks.xml').findall('.//block')}|{i.get('name') for i in items}
recipes=E.parse(c/'recipes.xml').findall('.//recipe');assert len(recipes)==28
for r in recipes:
 assert r.get('name') in names and r.get('always_unlocked')=='true' and int(r.get('count'))==1
 assert r.get('craft_area')==('workbench' if r.get('name')==b.get('name') else 'yfAutomationWorkbench')
 for i in r:assert i.get('name') in names and int(i.get('count'))>0
windows={w.get('name') for w in E.parse(game/'XUi_InGame/windows.xml').findall('.//window')}
group=E.parse(c/'XUi_InGame/xui.xml').find('.//window_group')
assert group.get('name')==b.find("property[@name='WorkstationWindow']").get('value')
for w in group:assert w.get('name') in windows
with (c/'Localization.csv').open(encoding='utf-8-sig',newline='') as f:loc={r['Key']:r for r in csv.DictReader(f)}
for n in E.parse(c/'blocks.xml').findall('.//block')+items:
 assert n.get('name') in loc
 assert n.find("property[@name='DescriptionKey']").get('value') in loc
for p in c.rglob('*.xml'):E.parse(p)
print('PASS: workstation, 14 items, 28 recipes, vanilla materials, isolated crafting area, native UI references and localization.')

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
recipes=E.parse(c/'recipes.xml').findall('.//recipe');assert len(recipes)==33
for r in recipes:
 assert r.get('name') in names and r.get('always_unlocked')=='true' and int(r.get('count'))==1
 assert r.get('craft_area')==('workbench' if r.get('name')==b.get('name') else 'yfAutomationWorkbench')
 for i in r:assert i.get('name') in names and int(i.get('count'))>0
windows={w.get('name') for w in E.parse(game/'XUi_InGame/windows.xml').findall('.//window')}
windows |= {w.get('name') for w in E.parse(c/'XUi_InGame/windows.xml').findall('.//window')}
groups=E.parse(c/'XUi_InGame/xui.xml').findall('.//window_group')
assert len({g.get('name') for g in groups}) == len(groups), 'Duplicate UI group'
for g in groups:
 for w in g: assert w.get('name') in windows, 'Unresolved native window reference'
group=groups[0]
assert group.get('name')==b.find("property[@name='WorkstationWindow']").get('value')
for w in group:assert w.get('name') in windows
with (c/'Localization.csv').open(encoding='utf-8-sig',newline='') as f:loc={r['Key']:r for r in csv.DictReader(f)}
for n in E.parse(c/'blocks.xml').findall('.//block')+items:
 assert n.get('name') in loc
 assert n.find("property[@name='DescriptionKey']").get('value') in loc
for p in c.rglob('*.xml'):E.parse(p)
for prop in E.parse(c/'blocks.xml').findall('.//property'):
 for key in ('name','class'):
  if prop.get(key):assert '.' not in prop.get(key) and ',' not in prop.get(key), 'Invalid native dynamic property key'
for suffix in ('Straight','Left','Right','Up','Down'):
 belt=E.parse(c/'blocks.xml').find(f".//block[@name='yfAutoBelt{suffix}']")
 assert belt.find("property[@class='CompositeFeatures']/property[@class='TEFeatureAutomationState']") is not None
 assert belt.find("property[@class='CompositeFeatures']/property[@class='TEFeatureStorage']/property[@name='LootList']").get('value')=='yfAutoBeltBuffer'
 assert (root/'97-AutomationWorkshop/Resources/automation-conveyors.unity3d').is_file()
assert E.parse(c/'loot.xml').find(".//lootcontainer[@name='yfAutoBeltBuffer']").get('size')=='1,1'
for kind in ('Sorter','Kitchen','Smelter','Forge','Recycler','Farm','Miner','Transfer','WaterPump','WaterTank','AmmoFeed'):
 machine=E.parse(c/'blocks.xml').find(f".//block[@name='yfAuto{kind}']")
 assert machine.find("property[@name='Model']").get('value')==f'#@modfolder:Resources/automation-machines.unity3d?Assets/Machines/Machine{kind}.prefab'
 assert machine.find("property[@name='MultiBlockDim']") is None, 'Keep the saved single-block footprint'
 assert machine.find("property[@class='CompositeFeatures']/property[@class='TEFeatureSignable']") is not None, 'Retain persisted status feature'
assert (root/'97-AutomationWorkshop/Resources/automation-machines.unity3d').is_file()
print('PASS: workstation, 14 items, 33 recipes, vanilla materials, isolated crafting area, native UI references and localization.')

for kind in ('Sorter','Kitchen','Smelter','Forge','Recycler','Farm','Miner','Transfer','WaterPump','AmmoFeed'):
 machine=E.parse(c/'blocks.xml').find(f".//block[@name='yfAuto{kind}']")
 assert machine.find("property[@class='CompositeFeatures']/property[@class='TEFeatureStorage']/property[@name='LootList']").get('value')=='yfAutoMachineInventory'
 assert machine.find("property[@class='CompositeFeatures']/property[@class='TEFeatureMachineInventory']") is not None
assert E.parse(c/'loot.xml').find(".//lootcontainer[@name='yfAutoMachineInventory']").get('size')=='6,6'
storage=E.parse(c/'XUi_InGame/windows.xml').find(".//window[@name='windowYFAutomationStorage']")
assert storage.get('controller')=='LootWindow' and storage.find(".//*[@name='btnSort']") is None
assert storage.find(".//grid[@controller='LootContainer']") is not None
print('PASS: 10 built-in machine inventories, migration features, native loot grid and partition-safe controls.')

assert E.parse(c/'XUi_InGame/windows.xml').find(".//window[@name='windowYFAutomationRecipe']") is None
assert E.parse(c/'XUi_InGame/xui.xml').find(".//window_group[@name='yfMachineInventory']/window[@name='windowYFAutomationRecipe']") is None
print('PASS: machine inventory screen omits the center recipe panel.')

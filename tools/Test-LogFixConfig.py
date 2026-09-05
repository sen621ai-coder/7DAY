"""Check the actual merged runtime config, including inactive legacy IDs."""
from pathlib import Path
import xml.etree.ElementTree as E

root = Path(__file__).resolve().parents[1]
dump = root / '.local-tests/UserData/Saves/Navezgane/AEC_Equipment_Verification_20260905/ConfigsDump'
items = {i.get('name'): i for i in E.parse(dump/'items.xml').iter('item')}
recipes = E.parse(dump/'recipes.xml')
loot = E.parse(dump/'loot.xml')
devices = {f'itemPZAEC{f}DeviceT{t}' for f in ('Harrier','Storm','Tremor','Warden') for t in range(16,20)}
for name in devices:
    node = items[name]
    assert node.find("property[@name='CreativeMode']").get('value') == 'None'
    assert node.find("property[@class='Action0']") is None
    assert node.find('.//triggered_effect') is None
    assert node.find(".//passive_effect[@name='ModSlots']").get('value') == '6'
assert not devices.intersection(r.get('name') for r in recipes.iter('recipe'))
assert not devices.intersection(i.get('name') for i in loot.iter('item'))
progression = E.parse(dump/'progression.xml')
mastery = progression.find(".//perk[@name='perkIntellectMastery']")
groups = [g for g in mastery.findall('effect_group') if g.find("passive_effect[@name='EntityDamage'][@value='2000']") is not None]
assert len(groups) == 1
assert groups[0].find("requirement[@name='!EntityTagCompare'][@tags='miniboss,boss']") is not None
negotiator = progression.find(".//perk[@name='perkAecMasterNegotiator']")
assert negotiator is None or negotiator.find(".//passive_effect[@name='QuestRewardChoiceCount']") is None
windows = E.parse(dump/'XUi_InGame/windows.xml')
window = windows.find(".//window[@name='windowLooting']")
grid = window.find("rect[@name='content']/grid[@name='queue']")
assert (grid.get('rows'), grid.get('cols')) == ('13','15')
assert len(grid.findall('backpack_item_stack')) == 1 and not grid.findall('item_stack')
assert len(window.findall("rect[@name='header']/rect[@controller='ContainerStandardControls']")) == 1
print('PASS: merged config preserves 16 inactive legacy IDs without loot/recipes; mastery excludes bosses; quest rewards and 195-slot loot UI remain valid.')

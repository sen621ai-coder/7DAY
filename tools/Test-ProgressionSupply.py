"""Read-only regression checks for blueprint supply and alloy costs."""
from pathlib import Path
import csv
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
mod = root / '99-AEC_T16_RuntimeFix'
config = mod / 'Config'
recipes = ET.parse(config / 'recipes.xml').getroot()
quests = ET.parse(config / 'quests.xml').getroot()
items = ET.parse(config / 'items.xml').getroot()
voucher = 'resourcePZAECArmoryVoucher'
assert len(items.findall(f".//item[@name='{voucher}']")) == 1
for tier in range(16, 20):
    for kind, count in [('Challenge', '1'), ('Defense', '2')]:
        target = f'PZAEC{kind}T{tier}'
        assert len(quests.findall(f".//quest[@id='{target}']")) == 1
        patches = [p for p in quests if p.get('xpath') == f"/quests/quest[@id='{target}']"]
        rewards = [r for p in patches for r in p if r.get('id') == voucher]
        assert len(rewards) == 1 and rewards[0].get('value') == count
exchanges = [r for r in recipes.iter('recipe') if r.get('name', '').startswith('itemPZAECArmoryBlueprintCrate')]
assert len(exchanges) == 4
for r in exchanges:
    assert r.get('count') == '1' and r.get('use_ingredient_modifier') == 'false'
    assert r.get('always_unlocked') == 'true' and r.get('craft_area') == 'workbench'
    ingredients = r.findall('ingredient')
    assert len(ingredients) == 1
    i = ingredients[0]
    if i.get('name') == voucher:
        assert i.get('count') == '4' and r.get('name').endswith('T16')
    else:
        assert i.get('count') == '1'
        assert int(i.get('name')[-2:]) == int(r.get('name')[-2:]) + 1
native = ET.parse(root / '01-ProjectZ/Config/recipes.xml')
alloy = native.find(".//recipe[@name='resourceDurablAlloys']")
assert alloy.find("ingredient[@name='resourceUltraLightAlloys']").get('count') == '3'
assert alloy.find("ingredient[@name='resourceForgedSteel']").get('count') == '20'
patches = [p for p in recipes if p.get('xpath') == "/recipes/recipe[@name='resourceDurablAlloys']/ingredient[@name='resourceUltraStrengthAlloys']/@count"]
assert len(patches) == 1 and patches[0].text == '3'
source = (mod / 'Source/FusionTierUpgrade.cs').read_text(encoding='utf-8')
assert '(EquipmentFusion.Rank(source) + 1) / 2' in source
with (config / 'Localization.csv').open(encoding='utf-8-sig', newline='') as f:
    rows = list(csv.DictReader(f))
for key in (voucher, voucher + 'Desc'):
    selected = [r for r in rows if r['Key'] == key]
    assert len(selected) == 1 and selected[0]['schinese'] and None not in selected[0]
assert '升阶继承融合次数20%' not in (config / 'Localization.csv').read_text(encoding='utf-8')
print('PASS: 8 quest rewards; 4 downward/pity recipes; voucher localization; 20/3/3 alloy recipe; 50% ceiling inheritance wiring.')

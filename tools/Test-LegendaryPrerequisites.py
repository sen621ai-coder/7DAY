"""T16 recipes require one matching legendary; variants are alternatives."""
import argparse
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
mod = root / '99-AEC_T16_RuntimeFix'
sys.path.insert(0, str(mod / 'Tools'))
import legendary_prerequisites as mapping
parser = argparse.ArgumentParser()
parser.add_argument('--configs', type=Path, default=mod / 'Config')
args = parser.parse_args()
recipes = list(ET.parse(args.configs / 'recipes.xml').iter('recipe'))
defined = {i.get('name') for path in [root.parent / 'Data/Config/items.xml', *root.glob('*/Config/items.xml')] for i in ET.parse(path).iter('item')}
checked = 0
for family, predecessors in mapping.WEAPONS.items():
    prefix = 'melee' if family == 'FaultlineHammer' else 'gun'
    name = f'{prefix}PZAEC{family}T16'
    matches = [r for r in recipes if r.get('name') == name]
    assert len(matches) == len(predecessors), (name, len(matches))
    actual = []
    for recipe in matches:
        costs = {i.get('name'): int(i.get('count')) for i in recipe.findall('ingredient')}
        used = set(costs) & set(predecessors)
        assert len(used) == 1 and costs[next(iter(used))] == 1
        assert set(costs) <= defined, set(costs) - defined
        assert recipe.get('use_ingredient_modifier') == 'false'
        assert costs['resourcePZAECWeaponChassis'] == 1 and costs['resourceLegendaryParts'] == 15
        for extra, count in mapping.EXTRA.get(family, []):
            assert costs[extra] == count
        actual.extend(used)
        checked += 1
    assert sorted(actual) == sorted(predecessors), name
for family, predecessor in mapping.ARMOR.items():
    for slot in ('Helmet', 'Outfit', 'Gloves', 'Boots'):
        name = f'armorPZAEC{family}{slot}T16'
        matches = [r for r in recipes if r.get('name') == name]
        assert len(matches) == 1, name
        costs = {i.get('name'): int(i.get('count')) for i in matches[0].findall('ingredient')}
        assert costs[predecessor + slot] == 1 and predecessor + slot in defined
        assert costs['resourcePZAECArmorChassis'] == 1 and costs['resourceLegendaryParts'] == 6
        checked += 1
print(f'PASS: {checked} T16 crafting alternatives require their matching legendary equipment, counts and item references valid.')

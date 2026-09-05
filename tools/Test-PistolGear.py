"""Validate pistol definitions, all progression routes and tier-correct loot."""
import argparse
from pathlib import Path
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--configs', type=Path, default=root / '99-AEC_T16_RuntimeFix/Config')
args = parser.parse_args()
items = {i.get('name'): i for i in ET.parse(args.configs / 'items.xml').iter('item')}
recipes = list(ET.parse(args.configs / 'recipes.xml').iter('recipe'))
loot = list(ET.parse(args.configs / 'loot.xml').iter('lootgroup'))
for index, tier in enumerate(range(16, 20)):
    name = f'gunPZAECEmberPistolT{tier}'
    item = items[name]
    properties = {p.get('name'): p.get('value') for p in item.findall('property')}
    assert 'perkGunslinger' in properties['Tags'] and 'PZAECEmberPistol' in properties['Tags']
    effects = {(e.get('name'), e.get('operation')): e for e in item.findall('./effect_group/passive_effect')}
    assert float(effects['EntityDamage', 'base_set'].get('value')) == [700, 950, 1280, 1730][index]
    assert effects['EntityDamage', 'base_set'].get('tags') == 'perkGunslinger'
    assert int(effects['MagazineSize', 'base_set'].get('value')) == [32, 40, 50, 64][index]
    assert int(effects['RoundsPerMinute', 'base_set'].get('value')) == [420, 480, 540, 620][index]
    assert int(effects['ModSlots', 'base_set'].get('value')) == 6
    ammo = item.find("property[@class='Action0']/property[@name='Magazine_items']").get('value').split(',')
    assert ammo == ['ammo44MagnumBulletBall', 'ammo44MagnumBulletHP', 'ammo44MagnumBulletAP', 'ammo44MagnumBulletDU']
    group = next(g for g in loot if g.get('name') == f'PZAECExpansionWeaponT{tier}')
    assert len(group.findall('item')) == 7 and len(group.findall(f"item[@name='{name}']")) == 1
    rows = [r for r in recipes if r.get('name') == name]
    assert len(rows) == 1, (name, len(rows))
    if tier == 16:
        assert rows[0].find("ingredient[@name='gunHandgunT5Hellgun']").get('count') == '1'
    else:
        assert rows[0].find(f"ingredient[@name='gunPZAECEmberPistolT{tier-1}']").get('count') == '1'
        assert rows[0].get('count') == '1' and rows[0].get('tags') == 'upgrade'
print('PASS: four pistols, .44 ammo, exact tier stats and sockets, four recipes with legendary entry and adjacent-only upgrades, all four boss weapon pools.')

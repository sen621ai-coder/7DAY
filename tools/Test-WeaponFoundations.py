"""Guard native firing inputs that Extends does not inherit."""
import argparse
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(root / '99-AEC_T16_RuntimeFix/Tools'))
from weapon_foundations import FOUNDATIONS
parser = argparse.ArgumentParser()
parser.add_argument('--configs', type=Path, default=root / '99-AEC_T16_RuntimeFix/Config')
args = parser.parse_args()
vanilla = {i.get('name'): i for i in ET.parse(root.parent / 'Data/Config/items.xml').iter('item')}
items = [i for i in ET.parse(args.configs / 'items.xml').iter('item') if i.get('name','').startswith(('gunPZAEC','meleePZAEC'))]
assert len(items) == 28
checks = 0
for item in items:
    base = vanilla[item.find("property[@name='Extends']").get('value')]
    own = item.findall('./effect_group/passive_effect')
    for effect in base.findall('./effect_group/passive_effect'):
        if effect.get('name') not in FOUNDATIONS or effect.get('operation') != 'base_set': continue
        found = [e for e in own if e.get('name') == effect.get('name') and e.get('operation') == 'base_set']
        assert len(found) == 1, (item.get('name'), effect.get('name'), 'missing or duplicated native firing value')
        if effect.get('name') not in ('SpreadMultiplierAiming','KickDegreesVerticalMin','KickDegreesVerticalMax'):
            assert found[0].get('value') == effect.get('value'), (item.get('name'), effect.get('name'))
        checks += 1
    if 'HorizonNeedle' in item.get('name'):
        values = {e.get('name'): e.get('value') for e in own if e.get('operation') == 'base_set'}
        assert values['MaxRange'] == '150' and values['DamageFalloffRange'] == '80'
        assert values['WeaponHandling'] == '.75'
print(f'PASS: {len(items)} weapons, {checks} explicit native firing inputs; rifle range/handling restored.')

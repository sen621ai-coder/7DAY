"""Check custom item icons against installed assets, including inherited icons."""
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
TARGETS = ('98-AECxProjectZ_Tweaks', '99-AEC_T16_RuntimeFix')
icons = {p.stem for p in (ROOT.parent / 'Data/ItemIcons').glob('*.png')}
icons.update(p.stem for p in ROOT.glob('*/UIAtlases/ItemIconAtlas/*.png'))
checked = hidden = 0
errors = []
for filename, tag in [('items.xml', 'item'), ('blocks.xml', 'block'),
                      ('item_modifiers.xml', 'item_modifier')]:
    definitions = {}
    paths = [ROOT.parent / 'Data/Config' / filename, *sorted(ROOT.glob('*/Config/' + filename))]
    for path in paths:
        for element in ET.parse(path).iter(tag):
            if element.get('name'):
                definitions[element.get('name')] = element

    def inherited(element, key, seen=None):
        seen = set() if seen is None else seen
        prop = element.find(f"property[@name='{key}']")
        if prop is not None:
            return prop.get('value')
        base = element.find("property[@name='Extends']")
        name = base.get('value') if base is not None else None
        if name in definitions and name not in seen:
            return inherited(definitions[name], key, seen | {name})
        return None

    for target in TARGETS:
        for element in ET.parse(ROOT / target / 'Config' / filename).iter(tag):
            name = element.get('name')
            if not name:
                continue
            icon = inherited(element, 'CustomIcon') or name
            if icon not in icons:
                # Internal projectile entities never appear in inventory or crafting.
                if name.startswith('ammoProjectile') and inherited(element, 'CreativeMode') == 'None':
                    hidden += 1
                    continue
                errors.append(f'{target}/{filename}: {name} -> {icon}')
            checked += 1
assert not errors, '\n'.join(errors)
print(f'PASS: {checked} definitions have existing icons; {hidden} hidden projectile definitions excluded.')

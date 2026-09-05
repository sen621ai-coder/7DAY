"""Check dedicated display registration, localization and core regression cases."""
import argparse
import csv
from pathlib import Path
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
mod = root / '99-AEC_T16_RuntimeFix'
parser = argparse.ArgumentParser()
parser.add_argument('--configs', type=Path, default=mod / 'Config')
args = parser.parse_args()
ui = ET.parse(args.configs / 'ui_display.xml')
panels = [p for p in ui.iter('item_display_info') if p.get('display_type','').startswith('AECDisplay_')]
assert len(panels) == len({p.get('display_type') for p in panels}) == 148
labels = {r['Key']:r for r in csv.DictReader((mod/'Config/Localization.csv').open(encoding='utf-8-sig'))}
for panel in panels:
    entries = panel.findall('display_entry')
    assert 1 <= len(entries) <= 8
    for row in entries:
        assert row.get('name').startswith(('aecBase_','aecPercent_'))
        assert row.get('title_key') in labels
        assert row.get('tags') is not None
outfit = next(p for p in panels if p.get('display_type')=='AECDisplay_armorPZAECHarrierOutfitT19')
assert outfit.find("display_entry[@name='aecBase_HealthMax']") is not None
assert outfit.find("display_entry[@name='aecBase_StaminaMax']") is not None
if ui.getroot().tag == 'configs':
    base = ET.parse(root.parent/'Data/Config/ui_display.xml')
    for append in ui.findall('append'):
        assert base.find(append.get('xpath').replace('/ui_display_info/','./',1)) is not None
else:
    for filename, element in [('items.xml','item'),('item_modifiers.xml','item_modifier')]:
        definitions = {e.get('name'):e for e in ET.parse(args.configs/filename).iter(element)}
        for panel in panels:
            name = panel.get('display_type').removeprefix('AECDisplay_')
            if name not in definitions: continue
            props = definitions[name].findall("property[@name='DisplayType']")
            assert len(props)==1 and props[0].get('value')==panel.get('display_type'), name
print(f'PASS: {len(panels)} dedicated panels, {sum(len(p) for p in panels)} stat rows; labels, tags, health/stamina bindings and registration valid.')

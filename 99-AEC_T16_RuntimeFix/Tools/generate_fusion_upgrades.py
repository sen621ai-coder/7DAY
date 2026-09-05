"""Skip-tier upgrades pay the sum of adjacent upgrade costs, retain rank once."""
from collections import Counter
from pathlib import Path
import re
import xml.etree.ElementTree as ET

START = '<!-- BEGIN GENERATED FUSION UPGRADES -->'
END = '<!-- END GENERATED FUSION UPGRADES -->'
GEAR = re.compile(r'^(armorPZAEC(Harrier|Storm|Tremor|Warden)(Helmet|Outfit|Gloves|Boots)|gunPZAEC(EmberPistol|HorizonNeedle|StormReservoir|BastionShotgun|EchoRepeater|CounterSiege)|meleePZAECFaultlineHammer)T1[6-9]$')

def refresh(config):
    path = Path(config) / 'recipes.xml'
    text = path.read_text(encoding='utf-8-sig')
    base = re.sub(re.escape(START) + r'.*?' + re.escape(END), '', text, flags=re.S)
    recipes = {e.get('name'): e for e in ET.fromstring(base).iter('recipe')
               if GEAR.fullmatch(e.get('name', '')) and e.get('tags') == 'upgrade'}
    families = sorted({name[:-2] for name in recipes})
    append = ET.Element('append', xpath='/recipes')
    for family in families:
        for source, target in [(16, 18), (16, 19), (17, 19)]:
            steps = [recipes[family + str(tier)] for tier in range(source + 1, target + 1)]
            costs = Counter()
            for step in steps:
                costs.update({i.get('name'): int(i.get('count')) for i in step.findall('ingredient') if not GEAR.fullmatch(i.get('name', ''))})
            recipe = ET.SubElement(append, 'recipe', name=family + str(target), count='1', craft_area='workbench',
                craft_time=str(sum(int(step.get('craft_time')) for step in steps)), always_unlocked='true',
                use_ingredient_modifier='false', tags='upgrade,aecfusionjump')
            ET.SubElement(recipe, 'ingredient', name=family + str(source), count='1')
            for name, count in costs.items():
                ET.SubElement(recipe, 'ingredient', name=name, count=str(count))
    assert len(append) == 69, len(append)
    ET.indent(append, space='  ')
    section = START + '\n' + ET.tostring(append, encoding='unicode') + '\n' + END
    if START in text:
        text = re.sub(re.escape(START) + r'.*?' + re.escape(END), lambda _: section, text, flags=re.S)
    else:
        text = text.replace('</configs>', section + '\n</configs>')
    path.write_text(text, encoding='utf-8', newline='\n')

if __name__ == '__main__':
    refresh(Path(__file__).resolve().parents[1] / 'Config')

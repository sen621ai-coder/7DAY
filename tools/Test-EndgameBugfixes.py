"""Regression checks for generator ordering, device text and safe pulse explosions."""
import importlib
import pathlib
import sys
import tempfile
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[1]
MOD = ROOT / '99-AEC_T16_RuntimeFix'
sys.path.insert(0, str(MOD / 'Tools'))
arsenal = importlib.import_module('generate_endgame_arsenal')
expansion = importlib.import_module('generate_endgame_expansion')

# Existing operation order and custom suffixes must survive either generator.
with tempfile.TemporaryDirectory(dir=ROOT / '.local-tests') as scratch:
    for first, second in [(arsenal, expansion), (expansion, arsenal)]:
        path = pathlib.Path(scratch) / 'config.xml'
        initial = ('<configs>\n  ' + arsenal.BEGIN + '\n<append xpath="/items"><item name="base"/></append>\n  ' + arsenal.END +
                   '\n  ' + expansion.BEGIN + '\n<append xpath="/items/item[@name=\'base\']"><property name="ModSlots" value="1"/></append>\n  ' + expansion.END +
                   '\n<set xpath="/items/item/@custom">keep</set>\n</configs>')
        path.write_text(initial, encoding='utf-8')
        for generator in (first, second, first):
            before = path.read_text(encoding='utf-8')
            body = before.split(generator.BEGIN)[1].split(generator.END)[0].strip()
            generator.replace_generated(path, body)
            assert path.read_text(encoding='utf-8') == before, 'Generator moved operations or changed another section'

items, item_loc = arsenal.build_items()
buffs, _ = arsenal.build_buffs()
buff_root = ET.fromstring('<root>' + buffs + '</root>')
buff_map = {x.get('name'): x for x in buff_root.iter('buff')}
texts = {key: cn for key, _, _, cn in item_loc}
for family in arsenal.SETS:
    for tier in range(16, 20):
        duration = buff_map[f'buffPZAEC{family}T{tier}Active'].find('duration').get('value')
        cooldown = buff_map[f'buffPZAEC{family}T{tier}Cooldown'].find('duration').get('value')
        description = texts[f'itemPZAEC{family}DeviceT{tier}Desc']
        assert f'持续 {duration} 秒，冷却 {cooldown} 秒' in description, 'Device text disagrees with its active/cooldown buffs'

items, _ = expansion.build_items()
root = ET.fromstring('<root>' + items + '</root>')
for tier in range(16, 20):
    ammo = root.find(f".//item[@name='ammoPZAECCounterPulseT{tier}']")
    explosion = ammo.find("property[@class='Action1']/property[@class='Explosion']")
    assert explosion.find("property[@name='BlockDamage']").get('value') == '0', 'Pulse inherits HE base demolition'
    assert int(explosion.find("property[@name='EntityDamage']").get('value')) == [1200,1700,2400,3400][tier-16], 'Pulse explosion does not scale'
    assert ammo.find("effect_group/passive_effect[@name='ExplosionBlockDamage']").get('value') == '0', 'Pulse explosion damage effect no longer blocks demolition'
print('PASS: both generator orders preserve dependent operations; 16 device texts match buffs; four pulse explosion paths are explicitly non-demolishing and tier-scaled.')

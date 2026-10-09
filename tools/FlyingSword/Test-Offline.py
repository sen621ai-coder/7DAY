"""Structural checks against the installed modpack; stdlib only."""
from pathlib import Path
import csv, hashlib, json, struct, xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
MOD = ROOT / 'ZZ-PZAEC_FlyingSword'
def check(value, message):
    if not value: raise AssertionError(message)
    print('PASS', message)

items = ET.parse(MOD/'Config/items.xml')
recipes = ET.parse(MOD/'Config/recipes.xml')
reference = ET.parse(ROOT/'99-AEC_T16_RuntimeFix/Config/recipes.xml')
for tier, damage in zip(range(16,20), [1000000,2500000,5500000,12000000]):
    name = f'meleePZAECJuqueT{tier}'
    item = items.find(f'.//item[@name="{name}"]')
    check(item is not None and item.find('property[@name="Extends"]').get('value') == 'meleeWpnBladeT3Machete', f'{tier} native blade inheritance')
    check(item.find('.//passive_effect[@name="EntityDamage"]').get('value') == str(damage), f'{tier} damage')
    check(item.find('property[@class="Action1"]/property[@name="Class"]').get('value') == 'PZAECJuqueWave, PZAEC.FlyingSword', f'{tier} wave action')
    recipe = recipes.find(f'.//recipe[@name="{name}"]')
    original = reference.find(f'.//recipe[@name="meleePZAECFaultlineHammerT{tier}"]')
    check({k:v for k,v in recipe.attrib.items() if k!='name'} == {k:v for k,v in original.attrib.items() if k!='name'}, f'{tier} original recipe budget/time')
    for a,b in zip(recipe.findall('ingredient'), original.findall('ingredient')):
        expected = b.get('name')
        if expected.startswith('meleeWpn'): expected='meleeWpnBladeT3Machete'
        if 'FaultlineHammer' in expected: expected=f'meleePZAECJuqueT{tier-1}'
        check(a.get('name')==expected and a.get('count')==b.get('count'), f'{tier} ingredient {expected}')

data=(MOD/'Resources/juque.mesh').read_bytes()
magic,vertices,indices=struct.unpack_from('<4sii',data)
check(magic==b'JQ01' and len(data)==12+vertices*32+indices*4,'complete mesh binary')
check(indices//3==9689,'original triangle count')
points=[struct.unpack_from('<3f',data,12+i*32) for i in range(vertices)]
check(abs(max(p[2] for p in points)-min(p[2] for p in points)-1)<.0001,'unit length mesh')
check(hashlib.sha256((MOD/'Resources/legend_sword.glb').read_bytes()).hexdigest()=='3f58d1db3a462d6b1cace538c80ef0511b5039dd4bc62f78c9d6a0102cf07f80','unaltered source GLB')
check('Rew' in (MOD/'Resources/ATTRIBUTION.txt').read_text(encoding='utf-8'),'author attribution')
check((MOD/'Config/Localization.csv').read_bytes()==(MOD/'Config/Localization.txt').read_bytes(),'native CSV localization and compatibility mirror match')
with (MOD/'Config/Localization.csv').open(encoding='utf-8-sig',newline='') as f:
    names={r['Key']:r['schinese'] for r in csv.DictReader(f)}
check(all(names[f'meleePZAECJuqueT{t}']==f'巨阙剑·T{t}' for t in range(16,20)),'Chinese item names')
check((MOD/'PZAEC.FlyingSword.dll').read_bytes()[:2]==b'MZ','compiled DLL exists')
check((MOD/'UIAtlases/ItemIconAtlas/meleePZAECJuque.png').read_bytes()[:8]==b'\x89PNG\r\n\x1a\n','sword icon')
print('Offline checks completed. These do not replace native gameplay or multiplayer validation.')

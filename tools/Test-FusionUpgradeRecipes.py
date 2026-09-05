"""Enforce the legendary -> T16 -> T17 -> T18 -> T19 crafting chain."""
import argparse
import importlib.util
from pathlib import Path
import tempfile
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
mod = root / '99-AEC_T16_RuntimeFix'
spec = importlib.util.spec_from_file_location('fusion_upgrades', mod / 'Tools/generate_fusion_upgrades.py')
generator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(generator)
parser = argparse.ArgumentParser()
parser.add_argument('--configs', type=Path, default=mod / 'Config')
args = parser.parse_args()
raw = (args.configs / 'recipes.xml').read_bytes()
recipes = list(ET.fromstring(raw).iter('recipe'))
gear = [r for r in recipes if generator.GEAR.fullmatch(r.get('name', ''))]
families = {r.get('name')[:-2] for r in gear}
assert len(families) == 23
for family in families:
    for tier in (17, 18, 19):
        target = family + str(tier)
        rows = [r for r in gear if r.get('name') == target]
        assert len(rows) == 1, (target, 'expected exactly one recipe', len(rows))
        recipe = rows[0]
        equipment = [i for i in recipe.findall('ingredient') if generator.GEAR.fullmatch(i.get('name', ''))]
        assert len(equipment) == 1 and equipment[0].get('name') == family + str(tier-1), (target, 'must consume previous tier')
        assert equipment[0].get('count') == '1' and recipe.get('count') == '1'
        assert recipe.get('tags') == 'upgrade' and recipe.get('use_ingredient_modifier') == 'false'
        costs = {i.get('name'): int(i.get('count')) for i in recipe.findall('ingredient')}
        armor = target.startswith('armor')
        index = tier - 17
        expected = {family + str(tier-1): 1,
                    f'PZAECBuildPartsR{tier-14}': ([3,3,4] if armor else [6,8,10])[index],
                    'resourceLegendaryParts': ([6,10,16] if armor else [16,24,36])[index],
                    f'resourcePZAECSiegeCapacitorT{tier}': [2,3,4][index],
                    f'resourcePZAECMutantHeartT{tier}': [1,1,2][index]}
        assert costs == expected, (target, costs)
        assert int(recipe.get('craft_time')) == ([60,75,90] if armor else [300,420,540])[index]
assert not any('aecfusionjump' in r.get('tags','') for r in gear)
with tempfile.TemporaryDirectory(dir=root / '.local-tests') as tmp:
    path = Path(tmp) / 'recipes.xml'
    # Retired block must be removed even when upgrading an older installation.
    legacy = generator.START + '\n<append xpath="/recipes"><recipe name="obsolete" /></append>\n' + generator.END + '\n'
    path.write_text('<configs>\n' + legacy + '</configs>\n', encoding='utf-8')
    generator.refresh(Path(tmp))
    first = path.read_bytes()
    assert b'obsolete' not in first and generator.START.encode() not in first
    generator.refresh(Path(tmp))
    assert first == path.read_bytes()
print('PASS: all 69 high-tier gear recipes require exactly the previous tier; no direct or skip recipes; original adjacent costs/time preserved; legacy cleanup idempotent.')

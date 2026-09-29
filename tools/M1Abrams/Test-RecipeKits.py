"""Check visible rows, expanded costs and repeatable generation."""
import csv
import shutil
import tempfile
from pathlib import Path
import xml.etree.ElementTree as E
from recipe_kits import apply, MATERIALS
from balance_config import apply as balance

root = Path(__file__).resolve().parents[2]
cfg = root / 'ZZ-PZAEC_M1Abrams/Config'


def verify(config):
    recipes = {r.get('name'): r for r in E.parse(config / 'recipes.xml').iter('recipe')}
    items = {i.get('name') for i in E.parse(config / 'items.xml').iter('item')}
    with (config / 'Localization.csv').open(encoding='utf-8', newline='') as f:
        loc = {r[0]: r for r in csv.reader(f)}
    for recipe in recipes.values():
        assert len(recipe.findall('ingredient')) <= 6, recipe.get('name')
    for i, tier in enumerate(range(16, 20)):
        vehicle = 'vehicleM1Abrams' + ('' if i == 0 else f'T{tier}') + 'Placeable'
        kit = f'PZAECM1AssemblyKitT{tier}'
        r, k = recipes[vehicle], recipes[kit]
        assert kit in items and loc[kit][6].startswith('M1') and kit + 'Desc' in loc
        assert r.find(f"ingredient[@name='{kit}']").get('count') == '1'
        assert int(r.get('craft_time')) + int(k.get('craft_time')) == [1200, 720, 900, 1080][i]
        expected = dict(zip(MATERIALS, ([15, 300, 13, 150, 100, 60],
                                      [20, 150, 25, 75, 50, 30],
                                      [30, 200, 38, 100, 75, 40],
                                      [45, 250, 50, 125, 100, 50])[i]))
        assert {x.get('name'): int(x.get('count')) for x in k} == expected
        previous = 'vehicleTruck4x4Placeable' if i == 0 else 'vehicleM1Abrams' + ('' if i == 1 else f'T{tier-1}') + 'Placeable'
        expected_final = {previous: 1, kit: 1, f'PZAECBuildPartsR{i+2}': [3, 3, 4, 5][i],
                          f'resourcePZAECMutantHeartT{tier}': [3, 3, 4, 5][i],
                          f'resourcePZAECSiegeCapacitorT{tier}': [6, 6, 8, 10][i]}
        if i == 0:
            expected_final['smallEngine'] = 1
        assert {x.get('name'): int(x.get('count')) for x in r} == expected_final
        assert k.get('always_unlocked') == 'true'
        for recipe in (r, k):
            assert recipe.get('use_ingredient_modifier') == 'false'
            assert recipe.get('craft_area') == 'workbench'
    assert 'learnable' in recipes['vehicleM1AbramsPlaceable'].get('tags')


verify(cfg)
with tempfile.TemporaryDirectory(prefix='M1-recipe-kits-', dir=root / '.local-tests') as temp:
    stage = Path(temp)
    target = stage / cfg.parent.name
    shutil.copytree(cfg, target / 'Config')
    shutil.copy2(cfg.parent / 'ModInfo.xml', target / 'ModInfo.xml')
    apply(stage)
    first = {p.name: p.read_bytes() for p in (target / 'Config').iterdir() if p.is_file()}
    apply(stage)
    assert all((target / 'Config' / name).read_bytes() == data for name, data in first.items())
    balance(stage)
    verify(target / 'Config')
print('PASS: all M1 recipes <=6 rows; exact materials/time preserved; item/localization closure; idempotence and full regeneration')

"""Verify skip upgrades preserve every intermediate cost and regenerate cleanly."""
from collections import Counter
import importlib.util
from pathlib import Path
import tempfile
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
mod = root / '99-AEC_T16_RuntimeFix'
spec = importlib.util.spec_from_file_location('fusion_upgrades', mod / 'Tools/generate_fusion_upgrades.py')
generator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(generator)
raw = (mod / 'Config/recipes.xml').read_bytes()
recipes = list(ET.fromstring(raw).iter('recipe'))
adjacent = {r.get('name'): r for r in recipes if r.get('tags') == 'upgrade' and generator.GEAR.fullmatch(r.get('name', ''))}
skips = [r for r in recipes if r.get('tags') == 'upgrade,aecfusionjump']
assert len(adjacent) == len(skips) == 69
for skip in skips:
    inputs = skip.findall('ingredient')
    source = inputs[0].get('name')
    target = skip.get('name')
    assert source[:-2] == target[:-2] and int(target[-2:]) - int(source[-2:]) >= 2
    assert inputs[0].get('count') == '1' and skip.get('count') == '1'
    steps = [adjacent[target[:-2] + str(t)] for t in range(int(source[-2:]) + 1, int(target[-2:]) + 1)]
    expected = Counter()
    for step in steps:
        expected.update({i.get('name'): int(i.get('count')) for i in step.findall('ingredient') if not generator.GEAR.fullmatch(i.get('name', ''))})
    assert expected == Counter({i.get('name'): int(i.get('count')) for i in inputs[1:]})
    assert int(skip.get('craft_time')) == sum(int(s.get('craft_time')) for s in steps)
with tempfile.TemporaryDirectory(dir=root / '.local-tests') as tmp:
    path = Path(tmp) / 'recipes.xml'
    path.write_bytes(raw)
    generator.refresh(Path(tmp))
    first = path.read_bytes()
    generator.refresh(Path(tmp))
    assert first == path.read_bytes()
print('PASS: 69 same-family skip recipes, all intermediate materials and times retained, output count one, generator idempotent.')

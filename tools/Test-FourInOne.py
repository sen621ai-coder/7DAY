"""Independently verify generated four-in-one patches against recipe costs."""
from pathlib import Path
from collections import Counter, defaultdict
from copy import deepcopy
from decimal import Decimal as D
import csv
import os
import re
import xml.etree.ElementTree as E

root = Path(__file__).resolve().parents[1]
config = root / '99-AEC_T16_RuntimeFix/Config'
mods = {m.get('name'): m for m in E.parse(root / '04-AEC-ENDGAME_OVERHAUL/Config/item_modifiers.xml').iter('item_modifier')}
recipes = {}
for r in E.parse(root / '04-AEC-ENDGAME_OVERHAUL/Config/recipes.xml').iter('recipe'):
    parts = [i for i in r.findall('ingredient') if i.get('name', '').startswith('mod')]
    if r.get('name') in mods and sum(int(i.get('count')) for i in parts) == 4:
        recipes[r.get('name')] = [(i.get('name'), int(i.get('count'))) for i in parts]
assert len(recipes) == 88
text = (config / 'item_modifiers.xml').read_text(encoding='utf-8-sig')
block = text.split('<!-- BEGIN GENERATED FOUR IN ONE -->')[1].split('<!-- END GENERATED FOUR IN ONE -->')[0]
patch = E.fromstring('<configs>' + block + '</configs>')
merged = E.Element('item_modifiers')
for name in recipes:
    merged.append(deepcopy(mods[name]))
for op in patch:
    path = op.get('xpath')
    assert path.startswith('/item_modifiers/')
    matches = merged.findall(path[len('/item_modifiers/'):])
    assert matches, path
    if op.tag == 'remove':
        assert path.endswith('/effect_group')
        parent = merged.find(path[len('/item_modifiers/'):].rsplit('/', 1)[0])
        for match in matches:
            parent.remove(match)
    else:
        assert op.tag == 'append' and len(matches) == 1
        matches[0].extend(deepcopy(list(op)))


def canon(n):
    return n.tag, tuple(sorted(n.attrib.items())), tuple(canon(c) for c in n)


def totals(node):
    sums = defaultdict(D)
    for g in node.findall('effect_group'):
        conditions = tuple(canon(c) for c in g if c.tag in ('requirement', 'requirement_group'))
        for p in g.findall('passive_effect'):
            sums[(conditions, p.get('name'), p.get('operation'), p.get('tags', ''))] += D(p.get('value'))
    return sums


def events(node):
    return list(node.iter('triggered_effect'))


def chance(t):
    return D(t.find('requirement').get('value')) if t.find('requirement') is not None else D(100)


def covers(actual, expected):
    return all(actual.get(k) == expected.get(k) for k in ('trigger', 'action', 'buff')) and (
        actual.get('target') == expected.get('target') or (actual.get('target') == 'otherAOE' and expected.get('target') == 'other')) and all(
        D(actual.get(k, '0')) >= D(expected.get(k, '0')) for k in ('range', 'duration', 'force')) and chance(actual) >= chance(expected)


checks = 0
for item in merged:
    name = item.get('name'); original = mods[name]
    assert item.attrib == original.attrib, 'Installation/category changed: ' + name
    assert [canon(p) for p in item.findall('property')] == [canon(p) for p in original.findall('property')]
    wanted = defaultdict(D)
    for source, count in recipes[name]:
        for key, value in totals(mods[source]).items():
            wanted[key] += value * count
        for event in events(mods[source]):
            assert any(covers(e, event) for e in events(item)), (name, source, 'lost trigger')
            checks += 1
    actual = totals(item)
    old = totals(original)
    for key in actual.keys() | wanted.keys() | old.keys():
        lower = key[1] in ('DegradationPerUse', 'StaminaLoss')
        old_helpful = old.get(key, D(0))
        if (lower and old_helpful > 0) or (not lower and old_helpful < 0):
            old_helpful = D(0)
        expect = min(old_helpful, wanted.get(key, D(0))) if lower else max(old_helpful, wanted.get(key, D(0)))
        assert actual.get(key, D(0)) == expect, (name, key, actual.get(key), expect)
        checks += 1
    for event in events(original):
        assert any(covers(e, event) for e in events(item)), (name, 'lost product trigger')
    keys = [(e.get('trigger'), e.get('action'), e.get('buff')) for e in events(item)]
    assert len(keys) == len(set(keys)), (name, 'double combat callback')

# Regression examples cover the reported failure, quantity-two recipes,
# distinct melee/ranged speed effects and scoped sneak bonuses.
def get(name, stat, operation='perc_add', conditional=False):
    return sum(v for (s, n, op, tags), v in totals(merged.find(f"item_modifier[@name='{name}']")).items()
               if n == stat and op == operation and bool(s) == conditional)
assert get('modAECMutatorMegaJudgementGun', 'EntityDamage') == 50
assert get('modAECMutatorMegaJudgementGun', 'RoundsPerMinute') == 5
assert get('modAECMutatorMegaJudgementGun', 'ReloadSpeedMultiplier') == 5
assert get('modAECMutatorMegaChaosHybrid', 'EntityDamage') == 100
assert get('modAECMutatorMegaHunterHybrid', 'EntityDamage', conditional=True) == 6
assert get('modAECMutatorMegaGravHammer', 'AttacksPerMinute') == 2
assert get('modAECMutatorMegaGravHammer', 'RoundsPerMinute') == 0
assert get('modAECMutatorMegaSprintLeg', 'CrouchSpeed') == 2
assert get('modAECMutatorMegaSkyLeapLeg', 'JumpStrength') == 8
assert get('modAECMutatorMegaSkyLeapLeg', 'StaminaLoss') == 0
rows = list(csv.DictReader((config / 'Localization.csv').open(encoding='utf-8-sig', newline='')))
for item in merged:
    key = item.find("property[@name='DescriptionKey']").get('value')
    selected = [r for r in rows if r['Key'] == key]
    assert len(selected) == 1 and '四合一修正' in selected[0]['schinese'] and selected[0]['english']
assert '+5000%' in next(r['schinese'] for r in rows if r['Key'] == 'modAECMutatorMegaJudgementGunDesc')
out = root / '.local-tests/four-in-one'
out.mkdir(parents=True, exist_ok=True)
E.ElementTree(merged).write(out / 'item_modifiers.xml', encoding='utf-8', xml_declaration=True)
ingredients = E.Element('item_modifiers')
for source in sorted({s for parts in recipes.values() for s, _ in parts}):
    ingredients.append(deepcopy(mods[source]))
E.ElementTree(ingredients).write(out / 'ingredients.xml', encoding='utf-8', xml_declaration=True)
print(f'PASS: {len(recipes)} products / 22 families / 4 tiers; {checks} ingredient effect checks, existing product benefits, trigger deduplication, quantity-two and conditional effects, installation metadata and descriptions.')
print('Native-parser fixtures: ' + str(out))

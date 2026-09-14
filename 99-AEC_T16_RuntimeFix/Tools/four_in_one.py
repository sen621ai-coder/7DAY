"""Generate scoped AEC four-ingredient modifier fixes from the installed recipes.

Preserve conditional scopes and operations. Same-scope passive effects take the
better of the existing product and the ingredient total; distinct operations
remain distinct. Equivalent combat triggers merge to one stronger event.
"""
from pathlib import Path
from copy import deepcopy
from collections import Counter, OrderedDict
from decimal import Decimal as D
import csv
import io
import re
import xml.etree.ElementTree as E

ROOT = Path(__file__).resolve().parents[2]
CONFIG = ROOT / '99-AEC_T16_RuntimeFix/Config'
BEGIN = '<!-- BEGIN GENERATED FOUR IN ONE -->'
END = '<!-- END GENERATED FOUR IN ONE -->'
LOWER = {'DegradationPerUse', 'StaminaLoss'}


def canonical(node):
    return (node.tag, tuple(sorted(node.attrib.items())), tuple(canonical(c) for c in node))


def scope(group):
    return tuple(canonical(c) for c in group if c.tag in ('requirement', 'requirement_group'))


def passive_key(p):
    return tuple(sorted((k, v) for k, v in p.attrib.items() if k != 'value'))


def number(n):
    return format(D(n).normalize(), 'f')


def inputs():
    mods = {m.get('name'): m for m in E.parse(ROOT / '04-AEC-ENDGAME_OVERHAUL/Config/item_modifiers.xml').iter('item_modifier')}
    recipes = OrderedDict()
    for recipe in E.parse(ROOT / '04-AEC-ENDGAME_OVERHAUL/Config/recipes.xml').iter('recipe'):
        parts = [i for i in recipe.findall('ingredient') if i.get('name', '').startswith('mod')]
        if recipe.get('name') in mods and sum(int(i.get('count')) for i in parts) == 4:
            name = recipe.get('name')
            assert re.fullmatch(r'modAECMutator(?:Atyp|Rare|Uniq|Mega)\w+', name), name
            assert name not in recipes and recipe.get('count') == '1', name
            recipes[name] = Counter()
            for i in parts:
                recipes[name][i.get('name')] += int(i.get('count'))
    assert len(recipes) == 88, f'Review changed four-in-one recipe coverage: {len(recipes)}'
    return mods, recipes


def probability(trigger):
    children = list(trigger)
    if not children:
        return D(100)
    assert len(children) == 1 and children[0].tag == 'requirement', E.tostring(trigger)
    r = children[0]
    assert r.get('name') == 'RandomRoll' and r.get('operation') == 'LTE' and r.get('min_max') == '0,100'
    return D(r.get('value'))


def merge_trigger(a, b):
    # The known AEC ingredients use only a probability requirement. Do not
    # flatten future conditional triggers into unconditional combat effects.
    assert a.get('target') in ('other', 'otherAOE') and b.get('target') in ('other', 'otherAOE')
    chance = max(probability(a), probability(b))
    if b.get('target') == 'otherAOE':
        a.set('target', 'otherAOE')
    for k in ('range', 'duration', 'force'):
        if k in b.attrib:
            a.set(k, number(max(D(a.get(k, '0')), D(b.get(k)))))
    for r in list(a):
        a.remove(r)
    if chance < 100:
        E.SubElement(a, 'requirement', name='RandomRoll', seed_type='Random', target='self', min_max='0,100', operation='LTE', value=number(chance))


def corrected(product, ingredients, mods):
    groups = OrderedDict()
    values = {}
    triggers = {}

    def group_for(g):
        s = scope(g)
        if s not in groups:
            groups[s] = E.Element('effect_group', {'tiered': 'false'})
            for c in g:
                if c.tag in ('requirement', 'requirement_group'):
                    groups[s].append(deepcopy(c))
        return s, groups[s]

    # Keep helpful product bonuses. Remove the deliberate product penalties
    # that would otherwise subtract from inherited ingredient benefits.
    for g in product.findall('effect_group'):
        s, dest = group_for(g)
        for c in g:
            if c.tag == 'passive_effect':
                assert c.get('operation') in ('base_add', 'perc_add') and not list(c)
                v = D(c.get('value'))
                if (c.get('name') in LOWER and v > 0) or (c.get('name') not in LOWER and v < 0):
                    continue
                key = (s, passive_key(c))
                assert key not in values
                values[key] = deepcopy(c)
                dest.append(values[key])
            elif c.tag == 'triggered_effect':
                key = (s, c.get('trigger'), c.get('action'), c.get('buff'))
                assert key not in triggers
                triggers[key] = deepcopy(c)
                probability(c)
                dest.append(triggers[key])
            else:
                assert c.tag in ('requirement', 'requirement_group'), c.tag

    totals = {}
    for name, count in ingredients.items():
        for g in mods[name].findall('effect_group'):
            s, dest = group_for(g)
            for c in g:
                if c.tag == 'passive_effect':
                    assert c.get('operation') in ('base_add', 'perc_add') and not list(c)
                    key = (s, passive_key(c))
                    if key not in totals:
                        totals[key] = [deepcopy(c), D(0), dest]
                    totals[key][1] += D(c.get('value')) * count
                elif c.tag == 'triggered_effect':
                    assert count == 1, 'Repeated probabilistic ingredient requires explicit probability composition'
                    key = (s, c.get('trigger'), c.get('action'), c.get('buff'))
                    if key in triggers:
                        merge_trigger(triggers[key], c)
                    else:
                        triggers[key] = deepcopy(c)
                        probability(c)
                        dest.append(triggers[key])
                else:
                    assert c.tag in ('requirement', 'requirement_group', 'display_value'), c.tag

    for key, (source, total, dest) in totals.items():
        if key in values:
            existing = values[key]
            choose = min if source.get('name') in LOWER else max
            existing.set('value', number(choose(D(existing.get('value')), total)))
        else:
            source.set('value', number(total))
            values[key] = source
            dest.append(source)
    # Native shock display values mirror the consolidated event, avoiding
    # stale source values or duplicate callbacks just to populate the tooltip.
    for key, trigger in triggers.items():
        if trigger.get('buff') == 'buffShocked':
            group = groups[key[0]]
            E.SubElement(group, 'display_value', name='dShockChance', value=number(probability(trigger)))
            E.SubElement(group, 'display_value', name='dShockDuration', value=trigger.get('duration', '0'))
    return list(groups.values())


LABELS = {
    'EntityDamage': '实体伤害', 'BlockDamage': '方块伤害', 'AttacksPerMinute': '近战攻速',
    'RoundsPerMinute': '射速', 'ReloadSpeedMultiplier': '装填速度', 'DismemberChance': '肢解概率',
    'DamageBonus': '伤害倍率加值', 'HarvestCount': '采集量', 'DegradationPerUse': '每次使用磨损',
    'HealthChangeOT': '生命回复速率', 'StaminaChangeOT': '耐力回复速率', 'StaminaMax': '耐力上限',
    'PlayerExpGain': '经验获取', 'RunSpeed': '奔跑速度', 'WalkSpeed': '步行速度',
    'CrouchSpeed': '蹲行速度', 'JumpStrength': '跳跃强度', 'StaminaLoss': '耐力消耗',
}
ALLOWED = {'weapon': '武器', 'bow': '弓', 'gun': '枪械', 'turretRanged': '远程炮塔', 'melee': '近战武器', 'club': '棍棒',
           'perkBrawler': '拳套', 'perkArchery': '弓弩', 'tool': '工具', 'armor': '护甲',
           'armorFeet': '鞋靴', 'armorChest': '胸甲', 'armorLegs': '腿部护甲'}


def description(product, groups, chinese):
    lines = []
    for g in groups:
        prefix = ('潜行且目标未警觉时：' if chinese else 'While crouched against an unalerted target: ') if scope(g) else ''
        for p in g.findall('passive_effect'):
            v = D(p.get('value')); op = p.get('operation'); name = p.get('name')
            label = LABELS[name] if chinese else name
            if p.get('tags'):
                label += '（仅击杀）' if chinese else ' (kills only)'
            if op == 'perc_add':
                value = f'{v * 100:+g}%'
            elif name == 'DismemberChance':
                value = f'{v * 100:+g}' + ('个百分点' if chinese else ' percentage points')
            else:
                value = f'{v:+g}'
            lines.append(prefix + label + ' ' + value)
        for t in g.findall('triggered_effect'):
            action = t.get('action'); buff = t.get('buff')
            label = ('击飞' if action == 'Ragdoll' else '电击' if buff == 'buffShocked' else '击倒') if chinese else (action if action == 'Ragdoll' else buff)
            detail = f'{number(probability(t))}%'
            if t.get('target') == 'otherAOE':
                detail += ('，范围' if chinese else ', radius ') + t.get('range') + 'm'
            if t.get('duration'):
                detail += ('，持续' if chinese else ', duration ') + t.get('duration') + 's'
            if t.get('force'):
                detail += ('，力度' if chinese else ', force ') + t.get('force')
            lines.append(prefix + label + '：' + detail)
    tags = product.get('installable_tags').split(',')
    install = '、'.join(ALLOWED[x] for x in tags) if chinese else ', '.join(tags)
    intro = '四合一修正：固有效果不低于配方原料合计，保留成品更强效果，占1槽。' if chinese else 'Four-in-one: inherent effects retain the ingredient totals or better product effects; uses one slot.'
    tail = '可安装：' if chinese else 'Fits: '
    return intro + r'\n' + '；'.join(lines) + r'\n' + tail + install + ('。潜行条件、目标抗性及控制冷却仍有效。' if chinese else '. Sneak conditions, target resistance and control cooldowns still apply.')


def generate():
    mods, recipes = inputs()
    patch = E.Element('configs')
    descriptions = {}
    for name, parts in recipes.items():
        product = mods[name]
        groups = corrected(product, parts, mods)
        xpath = f"/item_modifiers/item_modifier[@name='{name}']"
        E.SubElement(patch, 'remove', xpath=xpath + '/effect_group')
        dest = E.SubElement(patch, 'append', xpath=xpath)
        dest.extend(groups)
        key = product.find("property[@name='DescriptionKey']").get('value')
        descriptions[key] = (description(product, groups, False), description(product, groups, True))
    E.indent(patch, space='  ')
    block = BEGIN + '\n' + '\n'.join(E.tostring(c, encoding='unicode').rstrip() for c in patch) + '\n' + END
    block = '\n'.join(line.rstrip() for line in block.splitlines())
    path = CONFIG / 'item_modifiers.xml'
    original = path.read_text(encoding='utf-8-sig')
    if BEGIN in original:
        text = re.sub(re.escape(BEGIN) + r'.*?' + re.escape(END), lambda _: block, original, flags=re.S)
    else:
        text = original.replace('</configs>', block + '\n</configs>')
    path.write_text(text, encoding='utf-8', newline='')
    loc = CONFIG / 'Localization.csv'
    original = loc.read_text(encoding='utf-8-sig')
    header = next(csv.reader(io.StringIO(original)))
    # Existing descriptions outside this generated family retain exact text.
    lines = [line for line in original.splitlines() if line.split(',', 1)[0] not in descriptions]
    out = io.StringIO(newline=''); writer = csv.writer(out, lineterminator='\n')
    for key, (en, zh) in descriptions.items():
        row = dict.fromkeys(header, '')
        row.update(Key=key, File='item_modifiers', Type='Mod', english=en, schinese=zh, tchinese=zh)
        writer.writerow([row[k] for k in header])
    loc.write_text('\n'.join(lines) + '\n' + out.getvalue(), encoding='utf-8', newline='')
    print(f'Generated {len(recipes)} four-in-one effect overrides and descriptions.')


if __name__ == '__main__':
    generate()

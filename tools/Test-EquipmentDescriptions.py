"""Verify all new equipped gear has readable, tier-specific localized abilities."""
import csv
from pathlib import Path
import re
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
config = root / '99-AEC_T16_RuntimeFix/Config'
rows = list(csv.DictReader((config / 'Localization.csv').open(encoding='utf-8-sig', newline='')))
keys = [r['Key'] for r in rows]
loc = {r['Key']: r['schinese'] for r in rows}
items = list(ET.parse(config / 'items.xml').iter('item')) + list(ET.parse(config / 'item_modifiers.xml').iter('item_modifier'))
gear = [i for i in items if re.fullmatch(r'(?:armorPZAEC(?:Harrier|Storm|Tremor|Warden)(?:Helmet|Outfit|Gloves|Boots)|(?:gun|melee)PZAEC\w+|itemPZAEC(?:Harrier|Storm|Tremor|Warden)Device|modPZAEC\w+)T1[6-9]', i.get('name',''))]
assert len(gear) == 148, len(gear)
descriptions = []
for item in gear:
    name = item.get('name')
    key = item.find("property[@name='DescriptionKey']").get('value')
    assert key == name + 'Desc' and keys.count(key) == 1, key
    text = loc[key]
    assert name[-3:] in text and r'\n' in text and len(text) < 900, (name, len(text))
    assert '\n' not in text and '\r' not in text and '\ufffd' not in text
    assert '同阶行为组件' not in text and '传奇进阶武器：' not in text and '真护甲组件' not in text
    assert item.get('type') == 'attachment' or '不参与装备融合' in text or '合并工作站' in text
    if name.startswith(('gun','melee','armor')):
        assert '未融合' in text and '5%' in text
        assert '对应传奇' in text if name.endswith('T16') else f'T{int(name[-2:])-1}同款' in text
    if name.startswith('armor'):
        assert '2件' in text and '3件' in text and '4件' in text and '受伤/感染等异常抗性' in text
        resist = next(e.get('value') for e in item.findall('./effect_group/passive_effect') if e.get('name') == 'PhysicalDamageResist')
        assert '物理护甲+' + resist in text
    descriptions.append((name,text))
rifle = loc['gunPZAECHorizonNeedleT19Desc']
for phrase in ('伤害1800','弹匣48','射速(发/分)430','最大射程(米)150','电击','T18同款'):
    assert phrase in rifle, phrase
for tier, damage in [(16,700),(17,950),(18,1280),(19,1730)]:
    assert f'伤害{damage}' in loc[f'gunPZAECEmberPistolT{tier}Desc']
assert '弹匣+1200%' in loc['modPZAECClosedLoopFeedT19Desc']
assert '持续 24 秒，冷却 18 秒' in loc['armorPZAECStormHelmetT19Desc']
preview = root / '99-AEC_T16_RuntimeFix/装备能力介绍预览.md'
preview.write_text('# 游戏内装备能力介绍\n\n148件新武器、护甲和组件的介绍。所列为未融合固有数值。\n\n' +
    '\n\n'.join('## ' + loc.get(name,name) + '\n\n' + text.replace(r'\n','\n\n') for name,text in descriptions), encoding='utf-8')
print(f'PASS: {len(gear)} unique tier descriptions; CSV escapes, actual values, traits, set conditions and progression rules; longest {max(len(t) for _,t in descriptions)} characters.')

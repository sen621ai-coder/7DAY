"""Player-facing Chinese descriptions rendered from final equipment XML."""
from decimal import Decimal
import re
import xml.etree.ElementTree as ET

NL = r'\n'
SETS = {'Harrier': '猎隼', 'Storm': '雷暴', 'Tremor': '震岳', 'Warden': '守望'}
ROLES = {'Harrier': '远程爆头', 'Storm': '持续射击', 'Tremor': '近战重击', 'Warden': '承伤防守'}
LABELS = {
    'MaxRange': '最大射程(米)', 'BlockRange': '方块交互距离(米)', 'BurstRoundCount': '单次连发设置',
    'SpreadDegreesVertical': '垂直散布', 'SpreadDegreesHorizontal': '水平散布',
    'SpreadMultiplierCrouching': '蹲姿散布系数', 'SpreadMultiplierWalking': '走动散布系数',
    'SpreadMultiplierRunning': '奔跑散布系数', 'IncrementalSpreadMultiplier': '连射散布增幅',
    'ModSlots': '模组槽', 'DegradationMax': '耐久', 'EntityDamage': '伤害',
    'MagazineSize': '弹匣', 'RoundsPerMinute': '射速(发/分)', 'AttacksPerMinute': '攻速(次/分)',
    'ReloadSpeedMultiplier': '换弹速度', 'EntityPenetrationCount': '实体穿透计数',
    'SpreadMultiplierAiming': '瞄准散布', 'SpreadMultiplierHip': '腰射散布',
    'KickDegreesVerticalMin': '垂直后坐力下限', 'KickDegreesVerticalMax': '垂直后坐力上限',
    'KickDegreesHorizontalMin': '水平后坐力下限', 'KickDegreesHorizontalMax': '水平后坐力上限',
    'BlockDamage': '方块伤害', 'StaminaLoss': '攻击耗耐', 'DamageModifier': '部位伤害修正参数',
    'HeadshotDamageModifier': '爆头系数', 'PhysicalDamageResist': '物理护甲',
    'ElementalDamageResist': '元素抗性', 'BuffResistance': '异常抗性',
    'DegradationPerUse': '单次磨损', 'HypothermalResist': '抗寒', 'HyperthermalResist': '耐热',
    'Mobility': '机动性', 'NoiseMultiplier': '噪音', 'PlayerExpGain': '经验获取',
    'LootStage': '搜刮阶段', 'FoodLossPerStaminaPointGained': '恢复耐力耗食',
    'WaterLossPerStaminaPointGained': '恢复耐力耗水', 'HealthMax': '生命上限',
    'StaminaMax': '耐力上限', 'CarryCapacity': '负重容量', 'HarvestCount': '采集数量',
    'LootQuantity': '战利品数量', 'ScavengingTime': '搜刮耗时', 'StaminaChangeOT': '耐力恢复',
    'RunSpeed': '奔跑速度', 'VehicleMotorTorquePer': '载具扭矩', 'VehicleVelocityMaxPer': '载具极速',
    'BlockRepairAmount': '方块修理量', 'WeaponHandling': '武器操控', 'DamageFalloffRange': '有效射程',
}


def number(value):
    return format(Decimal(str(value)).normalize(), 'f')


def effects(node):
    """Only direct passives; triggered/conditional buffs are described separately."""
    entries = {}
    visible = []
    for group in node.findall('effect_group'):
        for effect in group.findall('passive_effect'):
            # Native defaults keep the gun functional; show useful range values,
            # not every internal recoil endpoint or burst-mode switch.
            if group.get('name') == 'AEC native firing foundations' and effect.get('name') not in ('MaxRange', 'DamageFalloffRange', 'BlockRange'):
                continue
            visible.append(effect)
    visible.sort(key=lambda e: e.get('name') in ('MaxRange', 'DamageFalloffRange', 'BlockRange'))
    for effect in visible:
        name, op, tags = effect.get('name'), effect.get('operation'), effect.get('tags', '')
        if name not in LABELS:
            raise ValueError(f'Untranslated equipment effect: {name}')
        key = name, op, tags
        value = Decimal(effect.get('value'))
        if key in entries and op in ('base_add', 'perc_add', 'base_subtract', 'perc_subtract'):
            entries[key] += value
        else:
            entries[key] = value
    result = []
    for (name, op, tags), value in entries.items():
        label = LABELS[name]
        if name in ('EntityDamage', 'StaminaLoss', 'BlockDamage'):
            if tags == 'secondary': label = {'EntityDamage': '重击伤害', 'StaminaLoss': '重击耗耐', 'BlockDamage': '重击方块伤害'}[name]
            elif tags == 'primary': label = {'EntityDamage': '轻击伤害', 'StaminaLoss': '轻击耗耐', 'BlockDamage': '轻击方块伤害'}[name]
            elif tags == 'ranged': label = '远程' + label
            elif tags == 'primary,secondary': label = '轻击/重击' + label
        if name == 'ElementalDamageResist': label = ('高温/电击' if tags == 'heat,electrical' else '电击') + '抗性'
        if name == 'BuffResistance':
            label = ('受伤/感染等异常抗性' if 'buffInfectionCatch' in tags else '流血抗性' if 'Bleeding' in tags
                     else '眩晕抗性' if tags == 'buffInjuryStunned01CHTrigger' else '特殊地面状态抗性' if 'buffDeepSnowStatus' in tags else '异常抗性')
        if name == 'HarvestCount':
            label = {'SonnyRareRecources': '指定稀有采集数量', 'salvageHarvest,allHarvest': '拆解/采集数量'}.get(tags, label)
        if name == 'LootQuantity' and tags == 'food,medical': label = '食物/医疗战利品数量'
        if name == 'DamageModifier' and tags == 'head': label = '头部伤害修正参数'
        percent = op.startswith('perc_') or name == 'BuffResistance'
        if percent and name == 'RoundsPerMinute': label = '射速'
        if percent and name == 'AttacksPerMinute': label = '攻速'
        if percent: value *= 100
        if op.endswith('subtract'): value = -value
        prefix = '+' if value > 0 and (op.endswith('add') or op.endswith('subtract')) else ''
        result.append(label + prefix + number(value) + ('%' if percent else ''))
    return '；'.join(result)


def buff_map(xml):
    return {b.get('name'): b for b in ET.fromstring('<root>' + xml + '</root>').iter('buff')}


def duration(buff):
    return number(buff.find('duration').get('value'))


def fusion_rules(tier):
    craft = '制作需对应传奇装备及材料。' if tier == 16 else f'仅由T{tier-1}同款装备＋材料逐级升级。'
    upgrade = '升阶继承融合次数20%（向下取整）。' if tier < 19 else '已达最高T阶。'
    return craft + '同名同阶、同融合次数两件在合并工作站融合，固有数值强化5%，耗耐/后坐等向有利方向改善；6槽不变。' + upgrade


def active_text(family, tier, buffs):
    active = buffs[f'buffPZAEC{family}T{tier}Active']
    result = effects(active)
    if family in ('Harrier', 'Storm'): result = '持握远程武器时：' + result
    elif family == 'Tremor': result = '近战重击：' + result
    if family == 'Warden':
        aura = buffs[f'buffPZAECWardenT{tier}Aura']
        radius = active.find(".//triggered_effect[@target='selfAOE']").get('range')
        result += f'；自身与{radius}米内友方玩家获得：' + effects(aura)
    return result + f"。持续 {duration(active)} 秒，冷却 {duration(buffs[f'buffPZAEC{family}T{tier}Cooldown'])} 秒。"


def charge_text(family, tier, buffs):
    charge = buffs[f'buffPZAEC{family}T{tier}Set3'].find(".//triggered_effect[@action='ModifyCVar'][@operation='add']").get('value')
    trigger = {'Harrier': '远程爆头', 'Storm': '远程有效命中', 'Tremor': '近战重击命中', 'Warden': '受攻击'}[family]
    return f'{trigger}共鸣+{number(charge)}，上限100；失去三件套清空共鸣。'


def arsenal(root, rows, buffs):
    descriptions = {}
    for item in root.findall('item'):
        name = item.get('name')
        match = re.fullmatch(r'armorPZAEC(Harrier|Storm|Tremor|Warden)(Helmet|Outfit|Gloves|Boots)T(1[6-9])', name)
        if match:
            family, _, tier = match.groups(); tier = int(tier)
            item.find("property[@name='DescriptionKey']").set('value', name + 'Desc')
            two = buffs[f'buffPZAEC{family}T{tier}Set2']
            condition = '持握远程武器时，' if family == 'Harrier' else '持握近战武器时，' if family == 'Tremor' else ''
            descriptions[name + 'Desc'] = NL.join([
                f'T{tier} {SETS[family]}｜{ROLES[family]}。以下为未融合固有属性；最终数值受技能/模组和游戏上限影响。',
                effects(item) + '。',
                '同流派同T阶套装：2件' + condition + effects(two) + '；3件' + charge_text(family, tier, buffs),
                '4件满100共鸣且冷却结束时自动触发：' + active_text(family, tier, buffs),
                fusion_rules(tier)])
    return replace_rows(rows, descriptions)


def weapons(root, rows, buffs):
    descriptions = {}
    roles = {'EmberPistol': '重型手枪｜.44马格南弹药，单发操作', 'HorizonNeedle': '穿透狙击步枪｜7.62毫米弹药',
             'StormReservoir': '持续压制机枪｜7.62毫米弹药', 'FaultlineHammer': '近战重击锤',
             'BastionShotgun': '大弹匣自动霰弹枪｜霰弹枪弹药', 'EchoRepeater': '削甲连弩｜弩箭',
             'CounterSiege': '反攻城脉冲器｜只使用同T阶反攻城脉冲弹'}
    items = {i.get('name'): i for i in root.findall('item')}
    for name, item in items.items():
        match = re.fullmatch(r'(?:gun|melee)PZAEC(' + '|'.join(roles) + r')T(1[6-9])', name)
        if not match: continue
        family, tier = match.groups(); tier = int(tier)
        traits = []
        for trigger in item.findall('.//triggered_effect'):
            if trigger.get('action') == 'AddBuff':
                buff = trigger.get('buff')
                if buff == 'buffShocked': traits.append('命中僵尸/动物施加电击；头部修正参数不是最终爆头伤害')
                elif buff in buffs:
                    target = buffs[buff]
                    traits.append('命中目标：' + effects(target) + f'，持续{duration(target)}秒')
            if trigger.get('action') == 'Ragdoll': traits.append(f"重击命中僵尸/动物击倒{trigger.get('duration')}秒")
        if family == 'StormReservoir':
            heat = buffs[f'buffPZAECStormOverheatedT{tier}']
            traits.append('命中累积热量；达到100热时' + effects(heat) + f'，持续{duration(heat)}秒；持枪自动冷却')
        if family == 'CounterSiege':
            explosion = items[f'ammoPZAECCounterPulseT{tier}'].find("property[@class='Action1']/property[@class='Explosion']")
            damage = explosion.find("property[@name='EntityDamage']").get('value')
            traits.append(f'专属弹爆炸实体伤害{damage}，直击及爆炸方块伤害为0')
        descriptions[name + 'Desc'] = NL.join([
            f'T{tier} ' + roles[family] + '。以下为未融合武器固有数值；弹药、技能、模组和目标抗性另算。',
            effects(item) + '。', '；'.join(traits) + '。' if traits else '提高穿透与精度，后坐力随T阶降低。',
            fusion_rules(tier)])
    return replace_rows(rows, descriptions)


def modifiers(root, rows, buffs):
    descriptions = {}
    slots = {'armorHead': '头盔', 'gun': '枪械', 'melee': '近战武器', 'armorHands': '手套',
             'gun,perkArchery': '枪械/弓弩', 'gun,perkElectrocutioner': '枪械/电棍',
             'melee,shotgun': '近战武器/霰弹枪', 'armorChest': '衣装', 'armorFeet': '战靴'}
    for mod in root.findall('item_modifier'):
        name = mod.get('name')
        calibration = re.fullmatch(r'modPZAEC(Harrier|Storm|Tremor|Warden)(Stable|Overload)T19', name)
        if calibration:
            family, mode = calibration.groups()
            buff = buffs[f'buffPZAEC{family}{mode}CalibrationT19']
            text = [f"T19 {SETS[family]}{'稳定' if mode == 'Stable' else '过载'}校准｜仅装入同流派T19头盔，占1槽，稳定与过载互斥。",
                    '头盔常驻加成：' + effects(mod) + '。',
                    f'套装自动触发后额外生效{duration(buff)}秒：' + effects(buff) + '。',
                    '芯片本身不参与装备融合。']
        else:
            text = [f'T{name[-2:]}装备组件｜安装部位：' + slots[mod.get('installable_tags')] + '。',
                    effects(mod) + '。']
            for trigger in mod.findall('.//triggered_effect'):
                buff = buffs.get(trigger.get('buff'))
                if buff is not None: text.append('命中目标：' + effects(buff) + f'，持续{duration(buff)}秒。')
            text.append('同功能族高低阶互斥；组件本身不参与装备融合，已装组件加成不会再次套用宿主的融合倍率。')
        descriptions[name + 'Desc'] = NL.join(text)
    return replace_rows(rows, descriptions)


def replace_rows(rows, descriptions):
    result = [(key, kind, en, descriptions.pop(key, cn)) for key, kind, en, cn in rows]
    # Armor formerly shared a description across tiers; each now has its own key.
    result += [(key, 'items', 'Tier-specific equipment abilities. See item stats.', cn) for key, cn in descriptions.items()]
    return result

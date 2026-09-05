"""Build a Chinese equipment reference from the verified live config dump."""
import csv
import pathlib
import re
import xml.etree.ElementTree as ET
from collections import defaultdict

ROOT = pathlib.Path(__file__).resolve().parents[1]
MOD = ROOT / '99-AEC_T16_RuntimeFix'
DUMP = ROOT / '.local-tests/UserData/Saves/Navezgane/AEC_Equipment_Verification_20260905/ConfigsDump'
loc = {}
for path in [ROOT.parent / 'Data/Config/Localization.csv', *sorted(ROOT.glob('*/Config/Localization.csv'))]:
    if path.exists():
        with path.open(encoding='utf-8-sig', newline='') as stream:
            for row in csv.DictReader(stream):
                row = {k.casefold(): v for k, v in row.items() if k}
                if row.get('key'):
                    loc[row['key']] = row.get('schinese') or row.get('english') or row['key']

def title(key):
    if key.endswith(':VariantHelper') and key not in loc:
        key = key.removesuffix(':VariantHelper')
    text = loc.get(key, key)
    text = re.sub(r'\[(?:[0-9A-Fa-f]{6,8}|-)\]', '', text)
    return text.replace('|', '／').replace('\n', ' ')

def parse(filename):
    return ET.parse(DUMP / filename).getroot()

selected = set()
types = {}
live = {}
for filename, tag in [('items.xml', 'item'), ('item_modifiers.xml', 'item_modifier'), ('blocks.xml', 'block')]:
    raw = (MOD / 'Config' / filename).read_text(encoding='utf-8-sig')
    for body in re.findall(r'<!-- BEGIN GENERATED ENDGAME (?:ARSENAL|EXPANSION) -->(.*?)<!-- END GENERATED ENDGAME (?:ARSENAL|EXPANSION) -->', raw, re.S):
        for e in ET.fromstring('<root>' + body + '</root>').iter(tag):
            name = e.get('name')
            if name and not name.startswith('PZAECAblativeWallRuin'):
                selected.add(name)
    for e in parse(filename).findall(tag):
        name = e.get('name')
        live[name] = e
        types[name] = tag
assert len(selected) == 266
support = {f'modPZAEC{stem}R{rank}' for stem in ('Precision', 'Breaker', 'Barrage', 'Skirmisher') for rank in range(2, 6)}
support |= {f'PZAECBuildPartsR{rank}' for rank in range(2, 6)}
selected |= support
assert selected <= live.keys()
recipes = defaultdict(list)
used_by = defaultdict(list)
for recipe in parse('recipes.xml').findall('recipe'):
    recipes[recipe.get('name')].append(recipe)
    for ingredient in recipe.findall('ingredient'):
        if recipe.get('name') in selected:
            used_by[ingredient.get('name')].append(recipe.get('name'))
buffs = {e.get('name'): e for e in parse('buffs.xml').findall('buff')}

effects = {
    'AttacksPerMinute':'每分钟近战攻击次数', 'DamageModifier':'部位伤害修正',
    'ExplosionBlockDamage':'爆炸方块伤害', 'ExplosionEntityDamage':'爆炸实体伤害',
    'PlayerExpGain':'经验收益', 'LootStage':'搜刮阶段', 'CarryCapacity':'免负重容量',
    'LootQuantity':'战利品数量', 'ScavengingTime':'搜刮耗时', 'HarvestCount':'采集数量',
    'FoodLossPerStaminaPointGained':'恢复耐力所耗食物', 'WaterLossPerStaminaPointGained':'恢复耐力所耗水分',
    'HypothermalResist':'抗寒', 'HyperthermalResist':'耐热', 'Mobility':'机动性', 'NoiseMultiplier':'噪音',
    'VehicleMotorTorquePer':'载具扭矩', 'VehicleVelocityMaxPer':'载具最高速度',
    'ModSlots':'模组槽', 'PhysicalDamageResist':'物理护甲', 'ElementalDamageResist':'元素抗性',
    'BuffResistance':'状态抗性', 'DegradationMax':'最大耐久', 'DegradationPerUse':'每次使用磨损',
    'HeadshotDamageModifier':'爆头伤害系数', 'EntityDamage':'实体伤害', 'BlockDamage':'方块伤害',
    'StaminaMax':'最大耐力', 'HealthMax':'最大生命', 'SpreadMultiplierAiming':'瞄准散布系数',
    'SpreadMultiplierHip':'腰射散布系数', 'KickDegreesVerticalMax':'最大垂直后坐力',
    'KickDegreesHorizontalMax':'最大水平后坐力', 'RunSpeed':'奔跑速度',
    'ReloadSpeedMultiplier':'换弹速度系数', 'RoundsPerMinute':'射速/攻击速度',
    'MagazineSize':'弹匣容量', 'StaminaChangeOT':'耐力恢复', 'StaminaLoss':'耐力消耗',
    'BlockRepairAmount':'方块修理量', 'EntityPenetrationCount':'实体穿透数',
    'WeaponHandling':'武器操控', 'DamageFalloffRange':'伤害衰减距离', 'WalkSpeed':'行走速度',
}
tags = {'primary':'普通攻击','secondary':'重击','head':'头部','ranged':'远程',
        'electrical':'电击','heat':'高温','perkDeadEye':'步枪','perkMachineGunner':'机枪',
        'perkSkullCrusher':'大锤','perkBoomstick':'霰弹','perkArchery':'弓弩','perkDemolitionsExpert':'爆炸武器'}
operations = {'base_set':'设为', 'base_add':'增加', 'perc_add':'同项比例加成', 'perc_set':'比例设为',
              'perc_subtract':'同项比例减去', 'base_subtract':'减去'}
def effect_text(e):
    value = e.get('value', '')
    operation = e.get('operation', '')
    if operation.startswith('perc_'):
        try:
            value = '～'.join(f'{float(x)*100:g}%' for x in value.split(','))
        except ValueError:
            pass
    scope = e.get('tags', '')
    scope = '、'.join(tags.get(s, title(s)) for s in scope.split(',')) if scope else ''
    return f"{effects.get(e.get('name'), e.get('name'))}{operations.get(operation, operation)} {value}" + (f'〔{scope}〕' if scope else '')

def properties(e):
    return {p.get('name'):p.get('value') for p in e.findall('property') if p.get('name')}

def tier(name):
    match = re.search(r'T(1[6-9])$', name)
    return int(match[1]) if match else None

def category(name):
    if name in support: return '11 关联战术核心与组件（既有内容）'
    if name.startswith('armor'): return '01 护甲四件套'
    if re.search(r'DeviceT\d+$', name): return '02 共鸣主动装置'
    if name.startswith(('gun','melee')): return '03 高级武器'
    if name.startswith('mod'):
        return '05 T19 校准芯片' if ('Stable' in name or 'Overload' in name) else '04 高级组件'
    if name.startswith('ammo'): return '07 专属弹药'
    if types[name] == 'block':
        return '08 供电防御设备' if any(s in name for s in ('SkyguardArray','HiveRepairStation','HoundDecoyTower','ShockNetNode','ArmorBreakTurret')) else '09 要塞建筑'
    if name.startswith('resource') or 'Crate' in name: return '10 材料、底盘与选择箱'
    return '06 战场用品'

def source(name):
    t = tier(name)
    i = t-16 if t else 0
    result = []
    if recipes[name]:
        result.append('工作台制作' + ('，可用上一阶升阶' if any(r.get('tags') == 'upgrade' for r in recipes[name]) else ''))
    if name.startswith('armor'):
        result.append(f'同阶 Boss 战利品包：{[10,15,22,30][i]}% 抽取一件随机同阶护甲（16 件池）')
    elif name.startswith(('gun','melee')):
        result.append(f'同阶 Boss 战利品包：{[2,3,4,6][i]}% 抽取一件随机高级武器（6 件池）')
    elif 'StableT19' in name or 'OverloadT19' in name:
        result.append('T19 Boss 战利品包：15% 抽取一枚随机校准芯片（8 枚池）')
    elif 'ArmoryBlueprintCrate' in name:
        result.append(f'同阶 Boss 战利品包：{[8,12,18,25][i]}% 额外获得 1 个')
    elif 'ComponentChoiceCrate' in name:
        result.append(f'同阶 Boss 战利品包：{[18,25,35,50][i]}% 额外获得 1 个')
    elif 'DefenseBlueprintCrate' in name:
        result.append(f'同阶攻城材料袋，条目 prob={ [.04,.06,.09,.12][i] }；不是每只敌人的直接掉落概率')
    elif 'MutantHeart' in name:
        result.append(f'同阶 Boss 战利品包必得 1 个，另有 {[25,40,60,80][i]}% 再得 1 个')
    elif 'SiegeCapacitor' in name:
        result.append(f'同阶攻城材料袋，数量 {"1～2" if t>=18 else "1"}；先击杀对应攻城单位并掉出袋子')
    elif 'CapacitorFragment' in name:
        result.append('同阶攻城材料袋，1～3 个，条目 prob=0.75')
    elif 'CounterJammer' in name:
        result.append(f'同阶攻城材料袋，条目 prob={ [.08,.12,.18,.25][i] }')
    elif name.startswith('PZAECBuildParts'):
        result.append('对应阶传奇试炼完成奖励 3 个；完成三波据点防守奖励 5 个。失败不发放')
    if not result:
        result.append('当前配置未查到制作或已接通的常规获取入口')
    return '；'.join(result) + '。'

notes = {
    'ThreatLens':'当前效果是远程伤害加成；未发现蓄力方向提示实现。',
    'CoolingSink':'当前降低后坐力并降低操控；未发现它参与雷池热量计算。',
    'KineticRecycler':'当前降低重击耐力消耗；不是命中后返还固定耐力。',
    'RepairServo':'修理量增加，同时换弹速度系数 −5%。',
    'PhaseRangefinder':'爆头系数增加，腰射散布 +20%；当前未要求稳定瞄准。',
    'NearfieldReflex':'换弹加速、衰减距离 −5%；当前未检查近距离条件。',
    'ClosedLoopFeed':'当前只增加弹匣容量；未发现返还弹药逻辑。',
    'PulseCapacitor':'命中给目标施加 0.35 秒、奔跑速度 −20% 的增益状态；不是累计命中后放电。',
    'GatekeeperHook':'当前为常驻眩晕抗性；未要求先击杀。',
    'EmergencyLiner':'当前为常驻物理护甲；未发现低血触发、止血及 90 秒冷却实现。',
    'InsulatedTreads':'当前提供电击抗性；没有泛用陷阱减伤。',
    'HorizonNeedle':'实体穿透 +1/2/3/4；瞄准散布系数 0.08/0.07/0.06/0.05。',
    'StormReservoir':'有效攻击累积热量；持枪每秒冷却 9/10/11/12.5。热量达 100 后清零并施加 3 秒射速 −90%，不是完全禁止开火。',
    'FaultlineHammer':'重击命中给目标施加 0.45 秒奔跑 −35%；当前未发现设计表中的蓄力范围冲击波。',
    'BastionShotgun':'命中目标后奔跑速度 −10/12/14/16%，持续 4 秒。',
    'EchoRepeater':'命中目标后物理护甲 −2/3/4/5，持续 4 秒；换弹加速常驻。',
    'CounterSiege':'使用同阶反攻城脉冲弹；命中施加 5/6/7/8 秒方块伤害 −25% 状态。',
    'QuickArmorGel':'消耗品；修复准星所指 6 米内受损非地形方块 1500/2000/2600/3400 耐久，按玩家 GS 对应阶缩放。另有 20 秒修理量 +65%、方块伤害 −20%。',
    'ResonanceInjector':'消耗品；有同阶三件套时共鸣 +25，上限100。60 秒最大生命 −10%、耐力恢复 +15%。',
    'DecoyBeacon':'消耗品；25 米内普通非 Boss、非攻城敌人转向使用者当前位置；自身奔跑 +10%，15 秒。当前不是放置实体信标。',
    'EvacAnchor':'不消耗；首次使用记录位置，20 秒内再次使用尝试返回。载具、商人区及目标空间限制由服务器检查。',
    'CounterJammer':'消耗品；当前通过使用动作在自身周围 8 米施放，非实际投掷弹道。攻城目标短踉跄，状态持续3/4/5/6秒，射速−75%、方块伤害−50%。',
    'FieldRepairKit':'消耗品；4 秒使用动作，修复当前装备栏中绝对磨损量最大的一件装备，恢复其最大耐久35/45/55/65%。当前未修理手持武器；另有90秒磨损减少状态。',
    'SkyguardArray':'供电后拦截28/32/36/40米内攻城投射物，单次检查成功率70/76/82/88%。检查间隔0.15秒；累计100热停8秒，每秒散热4。自动拦截代码未扣专属弹药；原生炮塔射击仍有弹药配置。',
    'HiveRepairStation':'供电后响应10/12/14/16米内方块受损事件，修复250/330/430/560点；维修站4秒冷却、同目标12秒冷却。当前运行代码未扣维修料盒。',
    'HoundDecoyTower':'供电后影响25/28/31/35米内普通非动物敌人，排除Boss与攻城单位；每个敌人2秒检查一次。当前未扣诱导电荷。',
    'ShockNetNode':'继承原生电围栏节点机制；电网命中可施加2秒奔跑速度−35/38/42/45%状态。需要按原生方式接线供电。',
    'ArmorBreakTurret':'继承M60炮塔，使用7.62mm弹药；目标被识别为该炮塔命中后，物理护甲−8/10/12/15，持续5秒。',
    'ReactiveWall':'单次伤害超过800时，只对超出800的部分减免18/22/26/30%；每块墙12秒冷却。',
    'AblativeWall':'损毁后变为2500耐久残架；残架可用锻钢20/28/38/50修回，需4次升级敲击。残架不计入玩家新物品数量。',
    'Embrasure':'固定箭孔模型的钢制射击孔；当前未发现开闭机构。',
    'BlastGate':'继承原生供电保险库门；识别为爆炸/火箭标签的伤害乘0.75。未发现低耐久卡死逻辑。',
    'ArmoredConduit':'继承电线中继，是高耐久中继方块；不是能包覆一整段电线的独立系统。',
    'ResonanceForge':'12000耐久的工作台变体；当前新配方 craft_area 都是 workbench，普通工作台即可制作，不要求先造此台。',
    'TacticalRelay':'10000耐久、耗电15的装甲电线中继；当前未发现向其他炮塔广播目标优先级的逻辑。',
    'ArmoryBlueprintCrate':'是制作材料代币：直接制作同阶高级武器消耗1个；不会右键打开选武器，也不是读一次永久解锁。升阶配方不消耗此箱。',
    'ComponentChoiceCrate':'是制作材料代币：直接制作同阶组件消耗1个。升阶配方不消耗此箱。',
    'DefenseBlueprintCrate':'T16版本用于五类T16防御设备，每次消耗1个；T17–T19版本已存在并能掉落，但当前没有消耗它们的配方。',
    'RepairCharge':'可制作的维修料盒；当前自动维修代码未实际消耗它。',
    'DecoyCharge':'可制作的诱导电荷；当前诱导塔代码未实际消耗它。',
}

notes.update({
    'CoolingSink':'四向后坐力比例−100%，提高操控和射速；不参与雷池热量计算。后坐力达到原生下限后不会继续变负。',
    'KineticRecycler':'常驻降低普攻与重击耗耐，并提高攻速、操控；不是命中返还固定耐力。',
    'RepairServo':'常驻提高修理量与换弹速度。',
    'PhaseRangefinder':'提高爆头系数、操控并降低瞄准散布；无需等待稳定瞄准。',
    'NearfieldReflex':'常驻提高换弹、射速及伤害衰减距离。',
    'ClosedLoopFeed':'提高弹匣、换弹和射速；未实现返弹。',
    'PulseCapacitor':'提高伤害和穿透；命中减速35/45/55/65%，持续1/1.4/1.8/2.4秒。',
    'GatekeeperHook':'常驻眩晕抗性与伤害加成，无需先击杀。',
    'EmergencyLiner':'常驻护甲、生命与流血抗性；不是低血触发治疗。',
    'InsulatedTreads':'电击抗性、耐力恢复和指定地形状态抗性；不等于所有陷阱伤害免疫。',
    'HorizonNeedle':'穿透+10/12/14/16；瞄准散布0.08/0.07/0.06/0.05；增加头部伤害修正，命中僵尸/动物施加原生电击。',
    'StormReservoir':'命中累积热量，持枪每秒冷却9/10/11/12.5；100热触发3秒射速−10/8/6/4%。',
    'FaultlineHammer':'轻击650/880/1190/1610，重击1800/2400/3200/4300；重击击中僵尸/动物触发原生击倒，仍受目标条件约束。',
})

def note(name):
    if re.search(r'DeviceT\d+$', name):
        return '可重复使用；需同流派同阶四件套、共鸣充满且冷却结束。T19装置有1个校准槽。套装与充能参数见总览。'
    if 'StableT19' in name or 'OverloadT19' in name:
        return '仅装在对应流派T19主动装置，稳定/过载互斥。使用时服务器添加15秒对应校准增益；当前持续时间、冷却和范围不会按旧设计表的说法改变。'
    return next((text for stem, text in notes.items() if stem in name), '')

groups = defaultdict(list)
for name in selected:
    groups[category(name)].append(name)
lines = ['# 新物品属性、配方与获取明细', '',
         '数据日期：2026-09-05。266 个军械/扩展物品，另附16个既有战术核心和4种战术组件，共286项；不含敌人及4个自动生成的墙体残架。', '',
         '本明细使用本次真实启动导出的合并配置。所列属性为该物品显式配置；继承父物品的基础动作、弹药/技能/其他模组以及难度仍会影响实战结果。百分比通常与同项加成相加，并非最终独立倍率。', '',
         '下列均为未融合数值。Runtime 1.20.0 起，仅28件T16–T19新武器和64件新护甲支持两件同名同阶、同融合次数合一；详见[装备融合说明](装备融合说明.md)。融合使用合并工作站，不属于下方普通工作台配方。', '',
         'Runtime 1.20.1：同款跨T阶升阶保留原融合次数20%，向下取整；新增66条跨阶直升配方，支付沿途升阶材料总和。T16融合+10直升T19为融合+2，每次实际升阶只折算一次。', '',
         'Runtime 1.20.2：T16武器/护甲制作实际消耗对应传奇装备，原有其他材料仍需支付；同系列传奇词条款任选一条配方，不要求Q6。对应关系见[T16传奇制作前置](T16传奇制作前置.md)。T17–T19直接制作沿用原配方。', '',
         '全部列出的新制作配方位于工作台并直接开放，依靠材料限制进度。时长和产量为基础值；“升阶”会消耗上一阶实物。没有配方质量条件时，不额外要求原型必须Q6。', '',
         'Boss百分比按每次打开对应阶Boss战利品包计算，不是每杀一只Boss的概率。攻城袋条目prob按配置原值列出，不乘算成每杀一只敌人的概率。', '']
for group, names in sorted(groups.items()):
    lines += [f'## {group}（{len(names)}项）', '']
    for name in sorted(names):
        e = live[name]
        prop = properties(e)
        lines += [f'### {title(name)}', '', f'物品ID：`{name}`', '', f'获取：{source(name)}', '']
        if note(name): lines += [f'实际用途：{note(name)}', '']
        stat_lines = []
        for key, label in [('MaxDamage','方块耐久'),('RequiredPower','耗电'),('Stacknumber','堆叠上限'),('AmmoItem','弹药'),('EntityDamage','炮塔伤害'),('MaxDistance','攻击距离'),('BurstRoundCount','每轮连射'),('CooldownTime','射击冷却')]:
            if key in prop: stat_lines.append(f'{label}：{title(prop[key])}')
        explosion = e.find("property[@class='Action1']/property[@class='Explosion']")
        if explosion is not None:
            for p in explosion.findall('property'):
                if p.get('name') in ('BlockDamage','EntityDamage'):
                    stat_lines.append(('爆炸方块伤害' if p.get('name')=='BlockDamage' else '爆炸实体伤害')+'：'+p.get('value',''))
        if e.get('installable_tags'): stat_lines.append('安装标签：' + e.get('installable_tags'))
        for x in e.findall('./effect_group/passive_effect'):
            stat_lines.append(effect_text(x))
        if stat_lines: lines += ['属性：' + '；'.join(dict.fromkeys(stat_lines)) + '。', '']
        triggered = []
        for x in e.findall('./effect_group/triggered_effect'):
            if x.get('buff'):
                for buff_id in x.get('buff').split(','):
                    buff = buffs.get(buff_id)
                    if buff is not None and not any(s in buff_id for s in ('Set','Watcher','Ready','Cooldown','Active')):
                        duration = buff.find('duration')
                        duration_text = f"，{duration.get('value')}秒" if duration is not None else ''
                        payload = '；'.join(effect_text(p) for p in buff.findall('./effect_group/passive_effect'))
                        if payload:
                            triggered.append(f"{title(buff.get('name_key') or buff_id)}{duration_text}：{payload}")
        if triggered: lines += ['触发状态（触发条件仍按原配置）：' + '；'.join(dict.fromkeys(triggered)) + '。', '']
        if recipes[name]:
            lines += ['| 做法 | 每次产量 | 基础时间 | 所需材料 |', '| --- | ---: | ---: | --- |']
            for recipe in recipes[name]:
                recipe_tags = (recipe.get('tags') or '').split(',')
                kind = '跨阶直升' if 'aecfusionjump' in recipe_tags else '升阶' if 'upgrade' in recipe_tags else '直接制作'
                ingredients = ' + '.join(f"{title(p.get('name'))} ×{p.get('count')}" for p in recipe.findall('ingredient'))
                lines.append(f"| {kind} | {recipe.get('count','1')} | {recipe.get('craft_time','0')}秒 | {ingredients} |")
            lines += ['']
        else:
            lines += ['制作：当前没有玩家制作配方。', '']
        if category(name).startswith('10') or name.startswith('PZAECBuildParts'):
            consumers = sorted(set(used_by[name]))
            lines += [('用于制作：' + '、'.join(title(s) for s in consumers) + '。') if consumers else '配方用途：当前目录内没有消耗它的配方。', '']

output = MOD / '新物品配方与属性明细.md'
output.write_text('\n'.join(lines), encoding='utf-8')
print(f'Generated {len(selected)} entries and {sum(len(recipes[n]) for n in selected)} recipes: {output}')
for group, names in sorted(groups.items()): print(f'{group}: {len(names)}')

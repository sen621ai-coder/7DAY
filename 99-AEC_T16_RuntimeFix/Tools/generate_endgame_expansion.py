from __future__ import annotations

import csv
import io
import pathlib
import xml.etree.ElementTree as ET
import endgame_progression as progression
import legendary_prerequisites as legendary
import equipment_descriptions as descriptions
import weapon_foundations


MOD = pathlib.Path(__file__).resolve().parents[1]
CONFIG = MOD / "Config"
GAME_CONFIG = MOD.parents[1] / "Data" / "Config"
BEGIN = "<!-- BEGIN GENERATED ENDGAME EXPANSION -->"
END = "<!-- END GENERATED ENDGAME EXPANSION -->"
TIERS = (16, 17, 18, 19)
RANK = {16: "R2", 17: "R3", 18: "R4", 19: "R5"}
COLOR = {16: "66BBFF", 17: "AA88FF", 18: "FFBB44", 19: "FF6655"}
VANILLA_ITEMS = {item.get("name"): item for item in ET.parse(GAME_CONFIG / "items.xml").getroot().findall("item")}

WEAPONS = {
    "EmberPistol": ("烬火重型手枪", "Ember Heavy Pistol", "gunHandgunT3DesertVulture", "perkGunslinger", [700, 950, 1280, 1730], [32, 40, 50, 64], [420, 480, 540, 620]),
    "HorizonNeedle": ("地平线针刺步枪", "Horizon Needle Rifle", "gunRifleT3SniperRifle", "perkDeadEye", [190, 210, 232, 255], [6, 7, 8, 9], [48, 50, 52, 54]),
    "StormReservoir": ("雷池轻机枪", "Storm Reservoir LMG", "gunMGT3M60", "perkMachineGunner", [76, 84, 92, 101], [90, 100, 110, 120], [390, 410, 430, 450]),
    "FaultlineHammer": ("断层震击锤", "Faultline Shock Hammer", "meleeWpnSledgeT3SteelSledgehammer", "perkSkullCrusher", [275, 303, 333, 366], [0, 0, 0, 0], [0, 0, 0, 0]),
    "BastionShotgun": ("壁垒自动霰弹枪", "Bastion Auto Shotgun", "gunShotgunT3AutoShotgun", "perkBoomstick", [21, 23, 25, 28], [16, 18, 20, 22], [155, 160, 165, 170]),
    "EchoRepeater": ("回声连弩", "Echo Repeating Crossbow", "gunBowT3CompoundCrossbow", "perkArchery", [225, 248, 273, 300], [3, 3, 4, 4], [36, 38, 40, 42]),
    "CounterSiege": ("反攻城脉冲器", "Counter-Siege Pulser", "gunExplosivesT3RocketLauncher", "perkDemolitionsExpert", [80, 90, 100, 110], [1, 1, 1, 1], [15, 16, 17, 18]),
}

COMPONENTS = {
    "ThreatLens": ("威胁鉴别镜", "Threat Lens", "armorHead", "modGunScopeLarge", "远程伤害随阶提升。"),
    "CoolingSink": ("热沉套件", "Cooling Sink", "gun", "modGunBarrelExtender", "降低四向后坐力，提高操控和射速。"),
    "KineticRecycler": ("动能回收器", "Kinetic Recycler", "melee", "modMeleeWeightedHead", "降低普攻与重击耗耐，提高攻速和操控。"),
    "RepairServo": ("维修伺服", "Repair Servo", "armorHands", "modArmorCustomizedFittings", "提高方块修理速度和修理量。"),
    "PhaseRangefinder": ("相位测距镜", "Phase Rangefinder", "gun,perkArchery", "modGunScopeLarge", "稳定瞄准提高弱点伤害。"),
    "NearfieldReflex": ("近场反射镜", "Nearfield Reflex", "gun", "modGunReflexSight", "提高换弹、射速和伤害衰减距离。"),
    "ClosedLoopFeed": ("闭环供弹器", "Closed-Loop Feed", "gun", "modGunDrumMagazineExtender", "扩大弹匣并提高持续供弹能力。"),
    "PulseCapacitor": ("脉冲电容", "Pulse Capacitor", "gun,perkElectrocutioner", "modMeleeBunkerBuster", "命中可附加短时电击。"),
    "StanceBreaker": ("破势配重", "Stance Breaker", "melee", "modMeleeWeightedHead", "提高普攻、重击伤害与攻速。"),
    "GatekeeperHook": ("守门钩爪", "Gatekeeper Hook", "melee,shotgun", "modMeleeErgonomicGrip", "常驻提高伤害与眩晕抗性。"),
    "EmergencyLiner": ("应急止血层", "Emergency Liner", "armorChest", "modArmorPlatingReinforced", "提高护甲、生命与流血抗性。"),
    "InsulatedTreads": ("绝缘步进器", "Insulated Treads", "armorFeet", "modArmorInsulatedLinerT3", "提高电击抗性、耐力恢复和指定不良地形抗性。"),
}

DEVICE_BLOCKS = {
    "SkyguardArray": ("天穹截击阵列", "Skyguard Interceptor Array", "autoTurret", [9000, 10800, 13000, 15800], [45, 50, 55, 60]),
    "HiveRepairStation": ("蜂巢维修站", "Hive Repair Station", "electricwirerelay", [7500, 9000, 11000, 13500], [35, 40, 45, 50]),
    "HoundDecoyTower": ("猎犬诱导塔", "Hound Decoy Tower", "speaker", [6000, 7500, 9200, 11500], [20, 20, 20, 20]),
    "ShockNetNode": ("震荡网节点", "Shock Net Node", "electricfencepost", [6500, 8000, 9800, 12000], [25, 28, 31, 35]),
    "ArmorBreakTurret": ("裂甲哨戒炮", "Armor-Break Sentry", "m60Turret", [7000, 8500, 10400, 12800], [40, 45, 50, 55]),
}

FORTRESS_BLOCKS = {
    "ReactiveWall": ("反应装甲墙", "Reactive Armor Wall", "steelMaster", [18000, 22000, 27000, 33000]),
    "AblativeWall": ("可替换缓冲墙", "Replaceable Ablative Wall", "steelMaster", [12000, 15000, 19000, 24000]),
    "Embrasure": ("装甲射击孔", "Armored Embrasure", "steelMaster", [14000, 17500, 21500, 26500]),
    "BlastGate": ("分区防爆门", "Sectional Blast Gate", "vaultDoor01_Powered", [20000, 25000, 31000, 38000]),
    "ArmoredConduit": ("装甲电缆槽", "Armored Conduit", "electricwirerelay", [8000, 10000, 12500, 15500]),
}


def replace_generated(path: pathlib.Path, body: str) -> None:
    text = path.read_text(encoding="utf-8-sig")
    if BEGIN in text:
        head, rest = text.split(BEGIN, 1)
        _, tail = rest.split(END, 1)
        text = head + BEGIN + "\n" + body.rstrip() + "\n  " + END + tail
        path.write_text(text, encoding="utf-8", newline="\n")
        return
    pos = text.rfind("</configs>")
    if pos < 0:
        raise RuntimeError(f"Missing </configs> in {path}")
    text = text[:pos].rstrip() + "\n  " + BEGIN + "\n" + body.rstrip() + "\n  " + END + "\n" + text[pos:]
    path.write_text(text, encoding="utf-8", newline="\n")


def xml(element: ET.Element, indent: str = "    ") -> str:
    ET.indent(element, space="  ")
    return "\n".join(indent + line for line in ET.tostring(element, encoding="unicode").splitlines())


def prop(parent: ET.Element, name: str, value: object, **extra: str) -> ET.Element:
    return ET.SubElement(parent, "property", {"name": name, "value": str(value), **extra})


def passive(parent: ET.Element, name: str, operation: str, value: object, tags: str | None = None) -> ET.Element:
    attrs = {"name": name, "operation": operation, "value": str(value)}
    if tags:
        attrs["tags"] = tags
    return ET.SubElement(parent, "passive_effect", attrs)


def loc(rows: list[tuple[str, str, str, str]], key: str, file_name: str, en: str, cn: str) -> None:
    rows.append((key, file_name, en, cn))


def build_items() -> tuple[str, list[tuple[str, str, str, str]]]:
    root = ET.Element("append", {"xpath": "/items"})
    rows: list[tuple[str, str, str, str]] = []

    for stem, (cn, en, base, perk, damage, mags, rpms) in WEAPONS.items():
        prefix = "melee" if stem == "FaultlineHammer" else "gun"
        for i, tier in enumerate(TIERS):
            name = f"{prefix}PZAEC{stem}T{tier}"
            item = ET.SubElement(root, "item", {"name": name})
            prop(item, "Extends", base)
            prop(item, "CreativeMode", "Player")
            prop(item, "DescriptionKey", name + "Desc")
            prop(item, "CustomIcon", base)
            prop(item, "CustomIconTint", COLOR[tier])
            prop(item, "EconomicValue", 12000 + i * 5000)
            prop(item, "SellableToTrader", "false")
            prop(item, "ShowQuality", "false")
            base_tags_node = VANILLA_ITEMS[base].find("property[@name='Tags']")
            base_tags = base_tags_node.get("value") if base_tags_node is not None else "weapon"
            prop(item, "Tags", f"{base_tags},PZAECAdvancedWeapon,PZAEC{stem},PZAECTier{tier}")
            if stem == "CounterSiege":
                action = ET.SubElement(item, "property", {"class": "Action0"})
                prop(action, "Magazine_items", f"ammoPZAECCounterPulseT{tier}")
                prop(action, "Reload_time", [4.0, 3.8, 3.6, 3.4][i])
            elif stem == "EmberPistol":
                action = ET.SubElement(item, "property", {"class": "Action0"})
                prop(action, "Magazine_items", "ammo44MagnumBulletBall,ammo44MagnumBulletHP,ammo44MagnumBulletAP,ammo44MagnumBulletDU")
            eg = ET.SubElement(item, "effect_group", {"name": name, "tiered": "false"})
            passive(eg, "ModSlots", "base_set", 4)
            passive(eg, "DegradationMax", "base_set", 2800 + i * 500)
            passive(eg, "EntityDamage", "base_set", damage[i], perk)
            if stem == "FaultlineHammer":
                passive(eg, "EntityDamage", "base_set", [125, 138, 152, 167][i], "primary")
                passive(eg, "EntityDamage", "base_set", damage[i], "secondary")
                passive(eg, "StaminaLoss", "base_set", [42, 41, 40, 39][i], "secondary")
                hit = ET.SubElement(eg, "triggered_effect", {"trigger": "onSelfSecondaryActionRayHit", "action": "AddBuff", "target": "other", "buff": f"buffPZAECFaultlineImpactT{tier}"})
                ET.SubElement(hit, "requirement", {"name": "EntityTagCompare", "target": "other", "tags": "zombie,animal"})
            else:
                passive(eg, "MagazineSize", "base_set", mags[i], perk)
                passive(eg, "RoundsPerMinute", "base_set", rpms[i], perk)
            if stem == "EmberPistol":
                passive(eg, "EntityPenetrationCount", "base_set", [4, 5, 6, 7][i])
                passive(eg, "SpreadMultiplierAiming", "base_set", [0.10, 0.085, 0.07, 0.055][i], perk)
                passive(eg, "KickDegreesVerticalMin", "base_set", [1.2, 1.0, 0.8, 0.6][i], perk)
                passive(eg, "KickDegreesVerticalMax", "base_set", [1.2, 1.0, 0.8, 0.6][i], perk)
            elif stem == "HorizonNeedle":
                passive(eg, "EntityPenetrationCount", "base_add", i + 1)
                passive(eg, "BlockDamage", "perc_set", ".25")
                passive(eg, "SpreadMultiplierAiming", "base_set", [0.08, 0.07, 0.06, 0.05][i])
            elif stem == "StormReservoir":
                heat = ET.SubElement(eg, "triggered_effect", {"trigger": "onSelfAttackedOther", "action": "ModifyCVar", "cvar": f"$PZAECStormHeatT{tier}", "operation": "add", "value": "1"})
                ET.SubElement(heat, "requirement", {"name": "!HasBuff", "buff": f"buffPZAECStormOverheatedT{tier}"})
                passive(eg, "EntityPenetrationCount", "base_add", 1)
            elif stem == "BastionShotgun":
                passive(eg, "BlockDamage", "perc_set", ".15")
                ET.SubElement(eg, "triggered_effect", {"trigger": "onSelfAttackedOther", "action": "AddBuff", "target": "other", "buff": f"buffPZAECBastionStaggerT{tier}"})
            elif stem == "EchoRepeater":
                ET.SubElement(eg, "triggered_effect", {"trigger": "onSelfAttackedOther", "action": "AddBuff", "target": "other", "buff": f"buffPZAECEchoMarkedT{tier}"})
                passive(eg, "ReloadSpeedMultiplier", "perc_add", [0.08, 0.10, 0.12, 0.15][i])
            elif stem == "CounterSiege":
                passive(eg, "BlockDamage", "perc_set", 0)
                ET.SubElement(eg, "triggered_effect", {"trigger": "onSelfAttackedOther", "action": "AddBuff", "target": "other", "buff": f"buffPZAECCounterSiegeT{tier}"})
            loc(rows, name, "items", f"T{tier} {en}", f"T{tier} {cn}")
            loc(rows, name + "Desc", "items", f"Legendary succession weapon: T16 < T17 < T18 < T19. Six mod slots and tier-scaled damage, reload and durability.", f"传奇进阶武器：T16 < T17 < T18 < T19。六个模组槽，伤害、装填、弹匣和耐久随阶级提升。")

    for stem, base, cn, en in [
        ("SkyguardInterceptor", "ammo762mmBulletAP", "天穹截击弹", "Skyguard Interceptor Round"),
        ("CounterPulse", "ammoRocketHE", "反攻城脉冲弹", "Counter-Siege Pulse Round"),
    ]:
        for i, tier in enumerate(TIERS):
            name = f"ammoPZAEC{stem}T{tier}"
            item = ET.SubElement(root, "item", {"name": name})
            prop(item, "Extends", base)
            prop(item, "CreativeMode", "Player")
            prop(item, "DescriptionKey", name + "Desc")
            prop(item, "CustomIcon", base)
            prop(item, "CustomIconTint", COLOR[tier])
            prop(item, "Stacknumber", 100 if stem == "SkyguardInterceptor" else 20)
            prop(item, "SellableToTrader", "false")
            eg = ET.SubElement(item, "effect_group", {"tiered": "false"})
            if stem == "SkyguardInterceptor":
                # Dedicated turret ammunition must not match rifle ammo tags.
                # Native turret damage evaluates the loaded round's effects
                # against the turret's EntityDamage base value.
                prop(item, "Tags", f"ammo,aecSkyguardAmmoT{tier},scrap100")
                prop(item, "DisplayType", "aecSkyguardAmmo")
                passive(eg, "EntityDamage", "perc_add", 1)
                passive(eg, "BlockDamage", "perc_set", 0)
                ET.SubElement(eg, "display_value", {"name": "dSkyguardDamage", "value": str([900,1300,1800,2500][i])})
                ET.SubElement(eg, "display_value", {"name": "dSkyguardBonus", "value": "1"})
            if stem == "CounterPulse":
                # Impact damage and explosion damage are independent native
                # paths. Override inherited HE block damage explicitly.
                action = ET.SubElement(item, "property", {"class": "Action1"})
                explosion = ET.SubElement(action, "property", {"class": "Explosion"})
                prop(explosion, "BlockDamage", 0)
                prop(explosion, "EntityDamage", [1200, 1700, 2400, 3400][i])
                passive(eg, "ExplosionBlockDamage", "base_set", 0)
                passive(eg, "BlockDamage", "perc_set", 0)
                passive(eg, "EntityDamage", "base_set", [35, 40, 45, 50][i])
            loc(rows, name, "items", f"T{tier} {en}", f"T{tier} {cn}")
            if stem == "SkyguardInterceptor":
                loc(rows, name + "Desc", "items", f"Dedicated T{tier} Skyguard ammunition: +100% turret entity damage, {[900,1300,1800,2500][i]} base damage per hit. Load into the matching-tier array; not rifle ammunition. Automatic interception currently consumes no ammunition.", f"T{tier}天穹阵列专用弹。装入同阶阵列后射击伤害+100%，未计爆头和目标减伤的单发伤害为{[900,1300,1800,2500][i]}，不造成方块伤害。普通7.62毫米弹可作低成本替代；本弹不能装入步枪。自动拦截目前不扣弹药。")
            else:
                loc(rows, name + "Desc", "items", "Specialized ammunition for AEC endgame equipment.", "AEC 终局设备使用的专属弹药。")

    field = [
        ("itemPZAECQuickArmorGel", "快凝装甲胶", "Quick Armor Gel", "buffPZAECQuickArmorGel", 10, "resourceRepairKit"),
        ("itemPZAECResonanceInjector", "共鸣注射剂", "Resonance Injector", "buffPZAECResonanceInjector", 5, "medicalFirstAidKit"),
        ("itemPZAECDecoyBeacon", "诱饵信标", "Decoy Beacon", "buffPZAECDecoyBeacon", 5, "thrownGrenade"),
        ("itemPZAECEvacAnchor", "撤离锚", "Evacuation Anchor", "buffPZAECEvacAnchor", 1, "resourceLegendaryParts"),
    ]
    for name, cn, en, buff, stack, icon in field:
        item = ET.SubElement(root, "item", {"name": name})
        # A neutral base avoids inherited medical Health<100% requirements
        # and the secondary heal-other action.
        prop(item, "Extends", "resourceLegendaryParts")
        prop(item, "CreativeMode", "Player")
        prop(item, "DescriptionKey", name + "Desc")
        prop(item, "CustomIcon", icon)
        prop(item, "CustomIconTint", "55CCFF")
        prop(item, "Stacknumber", stack)
        prop(item, "SellableToTrader", "false")
        action = ET.SubElement(item, "property", {"class": "Action0"})
        prop(action, "Class", "PZAECUse,AEC.T16.RuntimeFix")
        prop(action, "UseAnimation", "false")
        prop(action, "Consume", "true" if name != "itemPZAECEvacAnchor" else "false")
        prop(action, "Delay", 1.0 if name != "itemPZAECEvacAnchor" else 0.4)
        eg = ET.SubElement(item, "effect_group", {"tiered": "false"})
        ET.SubElement(eg, "triggered_effect", {"trigger": "onSelfPrimaryActionEnd", "action": "AddBuff", "buff": buff})
        ET.SubElement(eg, "triggered_effect", {"trigger": "onSelfPrimaryActionEnd", "action": "PZAECFieldUse,AEC.T16.RuntimeFix"})
        loc(rows, name, "items", en, cn)
        instructions = {
            "itemPZAECQuickArmorGel": "快捷栏左键使用。修复准星对准的6米内受损建筑，按玩家终局阶级恢复1500／2000／2600／3400耐久；不能用于商人保护区。无有效目标不消耗，满血可用。",
            "itemPZAECResonanceInjector": "快捷栏左键使用，为已穿同阶三件套增加25共鸣，最多100；共鸣未满时消耗1个。没有套装或共鸣已满不消耗，满血可用。",
            "itemPZAECDecoyBeacon": "快捷栏左键使用，消耗1个；吸引25米内普通敌人调查当前位置。对Boss和攻城单位无效；这是原地信标，不是投掷物。满血可用。",
            "itemPZAECEvacAnchor": "快捷栏左键标记当前位置，20秒内再次左键返回标记点。可重复使用；需要下车，不能在商人保护区使用，返回点需有两格净空。",
        }
        loc(rows, name + "Desc", "items", "Use from the toolbelt. Field technology validates prerequisites before consumption.", instructions[name])

    for stem, cn, en, buff in [
        ("CounterJammer", "反制干扰弹", "Countermeasure Jammer", "buffPZAECCounterJammer"),
        ("FieldRepairKit", "战地维修包", "Field Repair Kit", "buffPZAECFieldRepair"),
    ]:
        for i, tier in enumerate(TIERS):
            name = ("thrown" if stem == "CounterJammer" else "item") + f"PZAEC{stem}T{tier}"
            item = ET.SubElement(root, "item", {"name": name})
            prop(item, "Extends", "thrownGrenade" if stem == "CounterJammer" else "resourceLegendaryParts")
            prop(item, "CreativeMode", "Player")
            prop(item, "DescriptionKey", name + "Desc")
            prop(item, "CustomIcon", "thrownGrenade" if stem == "CounterJammer" else "resourceRepairKit")
            prop(item, "CustomIconTint", COLOR[tier])
            prop(item, "Stacknumber", 10 if stem == "CounterJammer" else 5)
            prop(item, "SellableToTrader", "false")
            eg = ET.SubElement(item, "effect_group", {"tiered": "false"})
            if stem == "CounterJammer":
                prop(item, "FuseTime", 3)
                prop(item, "ExplodeOnHit", "false")
                explosion = ET.SubElement(item, "property", {"class": "Explosion"})
                for key, value in {"BlockDamage": 0, "EntityDamage": 0, "RadiusBlocks": 0,
                                   "RadiusEntities": 8, "BlastPower": 0, "Buff": ""}.items():
                    prop(explosion, key, value)
                passive(eg, "ExplosionBlockDamage", "base_set", 0)
                passive(eg, "ExplosionEntityDamage", "base_set", 0)
            else:
                action = ET.SubElement(item, "property", {"class": "Action0"})
                prop(action, "Class", "PZAECUse,AEC.T16.RuntimeFix")
                prop(action, "UseAnimation", "false")
                prop(action, "Consume", "true")
                prop(action, "Delay", 4)
                ET.SubElement(eg, "triggered_effect", {"trigger": "onSelfPrimaryActionEnd", "action": "AddBuff", "buff": f"{buff}T{tier}"})
                ET.SubElement(eg, "triggered_effect", {"trigger": "onSelfPrimaryActionEnd", "action": "PZAECFieldUse,AEC.T16.RuntimeFix"})
            loc(rows, name, "items", f"T{tier} {en}", f"T{tier} {cn}")
            detail = (f"右键拉环，左键投掷，3秒后在落点周围8米干扰敌人{[3,4,5,6][i]}秒：射速−75%、方块伤害−50%。不炸伤玩家或建筑，满血可用。" if stem == "CounterJammer"
                      else f"快捷栏左键使用，修复当前受损最重的一件已穿护甲，恢复其最大耐久的{[35,45,55,65][i]}%；消耗1个，满血可用。")
            loc(rows, name + "Desc", "items", f"Tier-scaled {en.lower()} for active Blood Moon defense.", detail)

    resource_defs = [("resourcePZAECDefenseChassis", "防御设备底盘", "Defense Equipment Chassis", "resourceMechanicalParts")]
    for tier in TIERS:
        resource_defs += [
            (f"resourcePZAECCoreFragmentT{tier}", f"T{tier} 心核碎片", f"T{tier} Core Fragment", "resourceLegendaryParts"),
            (f"resourcePZAECCapacitorFragmentT{tier}", f"T{tier} 电容碎片", f"T{tier} Capacitor Fragment", "resourceElectricParts"),
            (f"itemPZAECArmoryBlueprintCrateT{tier}", f"T{tier} 军械蓝图选择箱", f"T{tier} Armory Blueprint Choice", "bundleRifle"),
            (f"itemPZAECComponentChoiceCrateT{tier}", f"T{tier} 组件选择箱", f"T{tier} Component Choice", "resourceLegendaryParts"),
            (f"resourcePZAECRepairChargeT{tier}", f"T{tier} 维修料盒", f"T{tier} Repair Charge", "resourceRepairKit"),
            (f"resourcePZAECDecoyChargeT{tier}", f"T{tier} 诱导电荷", f"T{tier} Decoy Charge", "resourceElectricParts"),
            (f"itemPZAECDefenseBlueprintCrateT{tier}", f"T{tier} 设备蓝图选择箱", f"T{tier} Defense Blueprint Choice", "bundleBatteryBank"),
        ]
    for name, cn, en, icon in resource_defs:
        item = ET.SubElement(root, "item", {"name": name})
        prop(item, "Extends", "resourceLegendaryParts")
        prop(item, "CreativeMode", "Player")
        prop(item, "DescriptionKey", name + "Desc")
        prop(item, "CustomIcon", icon)
        tier = next((t for t in TIERS if name.endswith(f"T{t}")), 16)
        prop(item, "CustomIconTint", COLOR[tier])
        prop(item, "Stacknumber", 20 if "Crate" in name or "Choice" in name else 100)
        prop(item, "SellableToTrader", "false")
        loc(rows, name, "items", en, cn)
        loc(rows, name + "Desc", "items", "Endgame crafting and reward material. Choice tokens are spent on the exact recipe you select.", "终局制作与奖励材料；选择箱代币用于你主动选择的对应配方。")

    progression.weapons(root)
    weapon_foundations.apply(root, VANILLA_ITEMS)
    for ammo in root.findall('item'):
        if ammo.get('name', '').startswith('ammoPZAECCounterPulseT'):
            index = int(ammo.get('name')[-2:]) - 16
            for effect in ammo.findall('./effect_group/passive_effect'):
                if effect.get('name') == 'EntityDamage':
                    effect.set('value', str([1200, 1700, 2400, 3400][index]))
    rows = descriptions.weapons(root, rows, descriptions.buff_map(build_buffs()[0]))
    chunks = [xml(root)]
    return "\n".join(chunks), rows


def build_modifiers() -> tuple[str, list[tuple[str, str, str, str]]]:
    root = ET.Element("append", {"xpath": "/item_modifiers"})
    rows: list[tuple[str, str, str, str]] = []
    for set_name, cn_set in [("Harrier", "猎隼"), ("Storm", "雷暴"), ("Tremor", "震岳"), ("Warden", "守望")]:
        for mode, cn_mode in [("Stable", "稳定校准"), ("Overload", "过载校准")]:
            name = f"modPZAEC{set_name}{mode}T19"
            mod = ET.SubElement(root, "item_modifier", {"name": name, "installable_tags": f"PZAEC{set_name}CalibrationT19", "modifier_tags": "PZAECCalibration", "blocked_tags": "noMods", "type": "attachment"})
            prop(mod, "Extends", "modGeneralMaster")
            prop(mod, "DescriptionKey", name + "Desc")
            prop(mod, "MaxModsAllowed", 1)
            prop(mod, "CustomIcon", "modGunScopeLarge" if mode == "Stable" else "modGunDrumMagazineExtender")
            prop(mod, "CustomIconTint", "66DDFF" if mode == "Stable" else "FF6633")
            prop(mod, "CreativeMode", "Player")
            prop(mod, "EconomicValue", 0)
            prop(mod, "SellableToTrader", "false")
            eg = ET.SubElement(mod, "effect_group", {"tiered": "false"})
            if set_name == "Harrier": passive(eg, "HeadshotDamageModifier", "perc_add", ".10" if mode == "Stable" else ".12", "head")
            elif set_name == "Storm": passive(eg, "RoundsPerMinute", "perc_add", ".08" if mode == "Stable" else ".15")
            elif set_name == "Tremor": passive(eg, "EntityDamage", "perc_add", ".12" if mode == "Stable" else ".20", "secondary")
            else: passive(eg, "PhysicalDamageResist", "base_add", "3" if mode == "Stable" else "6")
            loc(rows, name, "items", f"{set_name} {mode} Calibration", f"{cn_set}{cn_mode}")
            loc(rows, name + "Desc", "items", "T19 matching-set helmet calibration. Only one calibration can be installed.", "装入同流派T19头盔，占用1个模组槽；稳定与过载互斥，只能装一枚。")

    for stem, (cn, en, tags, icon, desc) in COMPONENTS.items():
        for i, tier in enumerate(TIERS):
            name = f"modPZAEC{stem}T{tier}"
            mod = ET.SubElement(root, "item_modifier", {"name": name, "installable_tags": tags, "modifier_tags": f"PZAECExpansion{stem}", "blocked_tags": "noMods", "type": "attachment"})
            prop(mod, "Extends", "modGeneralMaster")
            prop(mod, "DescriptionKey", name + "Desc")
            prop(mod, "MaxModsAllowed", 1)
            prop(mod, "CustomIcon", icon)
            prop(mod, "CustomIconTint", COLOR[tier])
            prop(mod, "CreativeMode", "Player")
            prop(mod, "EconomicValue", 0)
            prop(mod, "SellableToTrader", "false")
            eg = ET.SubElement(mod, "effect_group", {"tiered": "false"})
            if stem == "ThreatLens": passive(eg, "EntityDamage", "perc_add", [0.04, 0.05, 0.06, 0.08][i], "ranged")
            elif stem == "CoolingSink":
                passive(eg, "KickDegreesVerticalMax", "perc_add", [-.10, -.14, -.18, -.22][i])
                passive(eg, "WeaponHandling", "perc_add", -.08)
            elif stem == "KineticRecycler": passive(eg, "StaminaLoss", "perc_add", [-.10, -.14, -.18, -.22][i], "secondary")
            elif stem == "RepairServo":
                passive(eg, "BlockRepairAmount", "perc_add", [0.08, 0.10, 0.12, 0.15][i])
                passive(eg, "ReloadSpeedMultiplier", "perc_add", -.05)
            elif stem == "PhaseRangefinder":
                passive(eg, "HeadshotDamageModifier", "perc_add", [0.08, 0.10, 0.12, 0.15][i], "head")
                passive(eg, "SpreadMultiplierHip", "perc_add", .20)
            elif stem == "NearfieldReflex":
                passive(eg, "ReloadSpeedMultiplier", "perc_add", [0.10, 0.12, 0.15, 0.18][i])
                passive(eg, "DamageFalloffRange", "perc_add", -.05)
            elif stem == "ClosedLoopFeed": passive(eg, "MagazineSize", "perc_add", [0.10, 0.13, 0.16, 0.20][i])
            elif stem == "PulseCapacitor":
                ET.SubElement(eg, "triggered_effect", {"trigger": "onSelfAttackedOther", "action": "AddBuff", "target": "other", "buff": f"buffPZAECPulseCapacitorT{tier}"})
            elif stem == "StanceBreaker":
                passive(eg, "EntityDamage", "perc_add", [0.12, 0.15, 0.18, 0.22][i], "secondary")
                passive(eg, "RoundsPerMinute", "perc_add", -.08, "primary")
            elif stem == "GatekeeperHook": passive(eg, "BuffResistance", "base_add", [0.10, 0.12, 0.15, 0.18][i], "buffInjuryStunned01CHTrigger")
            elif stem == "EmergencyLiner": passive(eg, "PhysicalDamageResist", "base_add", [2, 3, 4, 5][i])
            elif stem == "InsulatedTreads": passive(eg, "ElementalDamageResist", "base_add", [6, 8, 10, 12][i], "electrical")
            loc(rows, name, "items", f"T{tier} {en}", f"T{tier} {cn}")
            loc(rows, name + "Desc", "items", f"Tiered behavior component. {en}: {desc}", f"同阶行为组件。{desc}")
    progression.components(root)
    rows = descriptions.modifiers(root, rows, descriptions.buff_map(build_buffs()[0]))
    return xml(root), rows


def build_buffs() -> tuple[str, list[tuple[str, str, str, str]]]:
    root = ET.Element("append", {"xpath": "/buffs"})
    rows: list[tuple[str, str, str, str]] = []
    simple = [
        ("buffPZAECQuickArmorGel", 20, [("BlockRepairAmount", "perc_add", ".65"), ("BlockDamage", "perc_add", "-.20")], "装甲胶施工", "Armor Gel Application"),
        ("buffPZAECResonanceInjector", 60, [("HealthMax", "perc_add", "-.10"), ("StaminaChangeOT", "perc_add", ".15")], "共鸣注入", "Resonance Injection"),
        ("buffPZAECDecoyBeacon", 15, [("RunSpeed", "perc_add", ".10")], "诱饵脉冲", "Decoy Pulse"),
        ("buffPZAECEvacAnchor", 20, [("RunSpeed", "perc_add", ".05")], "撤离锚已记录", "Evacuation Anchor Armed"),
    ]
    for name, duration, effects, cn, en in simple:
        buff = ET.SubElement(root, "buff", {"name": name, "name_key": name + "Name", "description_key": name + "Desc", "icon": "ui_game_symbol_electricity", "icon_color": "80,200,255"})
        ET.SubElement(buff, "stack_type", {"value": "replace"})
        ET.SubElement(buff, "duration", {"value": str(duration)})
        eg = ET.SubElement(buff, "effect_group")
        for n, op, val in effects: passive(eg, n, op, val)
        loc(rows, name + "Name", "buffs", en, cn)
        loc(rows, name + "Desc", "buffs", "Temporary endgame field effect.", "终局战场物品产生的临时效果。")

    for i, tier in enumerate(TIERS):
        tier_buffs = [
            (f"buffPZAECFaultlineImpactT{tier}", 0.45, [("RunSpeed", "perc_add", "-.35")], "断层冲击"),
            (f"buffPZAECBastionStaggerT{tier}", 4, [("RunSpeed", "perc_add", str([-.10, -.12, -.14, -.16][i]))], "壁垒震慑"),
            (f"buffPZAECEchoMarkedT{tier}", 4, [("PhysicalDamageResist", "base_add", str(-[2, 3, 4, 5][i]))], "回声标记"),
            (f"buffPZAECCounterSiegeT{tier}", [5, 6, 7, 8][i], [("BlockDamage", "perc_add", "-.25")], "反攻城压制"),
            (f"buffPZAECStormOverheatedT{tier}", 3, [("RoundsPerMinute", "perc_add", "-.90")], "雷池过热"),
            (f"buffPZAECPulseCapacitorT{tier}", 0.35, [("RunSpeed", "perc_add", "-.20")], "脉冲电击"),
            (f"buffPZAECShockNetT{tier}", 2, [("RunSpeed", "perc_add", str([-.35, -.38, -.42, -.45][i]))], "震荡网束缚"),
            (f"buffPZAECArmorBreakT{tier}", 5, [("PhysicalDamageResist", "base_add", str(-[8, 10, 12, 15][i]))], "裂甲"),
            (f"buffPZAECCounterJammerT{tier}", [3, 4, 5, 6][i], [("RoundsPerMinute", "perc_add", "-.75"), ("BlockDamage", "perc_add", "-.50")], "反制干扰"),
            (f"buffPZAECFieldRepairT{tier}", 90, [("DegradationPerUse", "perc_add", str(-[.35, .45, .55, .65][i]))], "战地维护"),
        ]
        for name, duration, effects, cn in tier_buffs:
            buff = ET.SubElement(root, "buff", {"name": name, "name_key": name + "Name", "description_key": name + "Desc", "icon": "ui_game_symbol_electricity", "icon_color": "255,160,64"})
            ET.SubElement(buff, "stack_type", {"value": "replace"})
            ET.SubElement(buff, "duration", {"value": str(duration)})
            eg = ET.SubElement(buff, "effect_group")
            for n, op, val in effects: passive(eg, n, op, val)
            loc(rows, name + "Name", "buffs", f"T{tier} {cn}", f"T{tier} {cn}")
            loc(rows, name + "Desc", "buffs", "Tier-scaled endgame combat state.", "随终局等级成长的战斗状态。")
    calibration_effects = {
        ("Harrier", "Stable"): [("HeadshotDamageModifier", "perc_add", ".08", "head")],
        ("Harrier", "Overload"): [("HeadshotDamageModifier", "perc_add", ".12", "head")],
        ("Storm", "Stable"): [("SpreadMultiplierHip", "perc_add", "-.20", None)],
        ("Storm", "Overload"): [("RoundsPerMinute", "perc_add", ".15", None)],
        ("Tremor", "Stable"): [("StaminaLoss", "perc_add", "-.15", "secondary")],
        ("Tremor", "Overload"): [("EntityDamage", "perc_add", ".20", "secondary")],
        ("Warden", "Stable"): [("StaminaChangeOT", "perc_add", ".20", None)],
        ("Warden", "Overload"): [("PhysicalDamageResist", "base_add", "6", None)],
    }
    for (set_name, mode), effects in calibration_effects.items():
        name = f"buffPZAEC{set_name}{mode}CalibrationT19"
        buff = ET.SubElement(root, "buff", {"name": name, "name_key": name + "Name", "description_key": name + "Desc", "icon": "ui_game_symbol_armor_iron", "icon_color": "100,220,255" if mode == "Stable" else "255,90,50"})
        ET.SubElement(buff, "stack_type", {"value": "replace"})
        ET.SubElement(buff, "duration", {"value": "15"})
        eg = ET.SubElement(buff, "effect_group")
        for effect_name, operation, value, tags in effects:
            passive(eg, effect_name, operation, value, tags)
        loc(rows, name + "Name", "buffs", f"{set_name} {mode} Calibration", f"{set_name}·{'稳定' if mode == 'Stable' else '过载'}校准")
        loc(rows, name + "Desc", "buffs", "T19 helmet calibration is active for this resonance window.", "T19头盔校准在本次自动共鸣窗口内生效。")
    progression.expansion_buffs(root)
    return xml(root), rows


def repair_items(block: ET.Element, steel: int, electric: int = 0) -> None:
    group = ET.SubElement(block, "property", {"class": "RepairItems"})
    prop(group, "resourceDurablAlloys", steel)
    if electric: prop(group, "resourceElectricParts", electric)


def apply_block_colors(root: ET.Element, rows: list) -> None:
    palette = {
        "SkyguardArray": ("40C8F5", "青蓝"),
        "HiveRepairStation": ("50DA88", "翠绿"),
        "HoundDecoyTower": ("F8A449", "橙黄"),
        "ShockNetNode": ("EADB45", "亮黄"),
        "ArmorBreakTurret": ("EF6576", "玫红"),
        "ReactiveWall": ("CA4D48", "红"),
        "AblativeWall": ("DEA244", "橙"),
        "Embrasure": ("5097DF", "蓝"),
        "BlastGate": ("AC74E3", "紫"),
        "ArmoredConduit": ("8298AF", "蓝灰"),
        "ResonanceForge": ("9D78E7", "紫罗兰"),
        "TacticalRelay": ("4DE0C7", "青绿"),
    }
    # Shape/New meshes use atlas textures rather than ModelEntity's tint.
    paints = {p.get("name"): p.find("property[@name='TextureId']").get("value")
              for p in ET.parse(GAME_CONFIG / "painting.xml").getroot().findall("paint")}
    surfaces = {"ReactiveWall": "red", "AblativeWall": "orange", "Embrasure": "blue"}
    tier_markers = {16: "blue", 17: "purple", 18: "yellow", 19: "red"}
    labels = {}
    for block in root.findall("block"):
        name = block.get("name", "")
        tier = int(name[-2:]) if name.endswith(tuple(f"T{t}" for t in TIERS)) else None
        stem = name.removeprefix("PZAEC")
        if tier: stem = stem[:-3]
        if stem == "AblativeWallRuin":
            prop(block, "Texture", "355,355,356,356,356,356")
            continue
        if stem not in palette: continue
        color, label = palette[stem]
        brightness = [0.70, 0.80, 0.90, 1.0][tier-16] if tier else 1.0
        color = ''.join(f'{round(int(color[i:i+2], 16)*brightness):02X}' for i in (0,2,4))
        icon = block.find("property[@name='CustomIconTint']")
        if icon is not None: icon.set("value", color)
        prop(block, "TintColor", color)
        if stem in surfaces:
            side = paints["txName_Concrete_" + surfaces[stem]]
            marker = paints["txName_Concrete_" + tier_markers[tier]]
            prop(block, "Texture", ','.join([marker, marker, side, side, side, side]))
        labels[name + "Desc"] = label
    for i, (key, file_name, en, cn) in enumerate(rows):
        if key in labels:
            rows[i] = (key, file_name, en, cn + f" 识别色：{labels[key]}。")


def build_blocks() -> tuple[str, list[tuple[str, str, str, str]]]:
    root = ET.Element("append", {"xpath": "/blocks"})
    rows: list[tuple[str, str, str, str]] = []
    for stem, (cn, en, base, hp, power) in DEVICE_BLOCKS.items():
        for i, tier in enumerate(TIERS):
            name = f"PZAEC{stem}T{tier}"
            block = ET.SubElement(root, "block", {"name": name})
            prop(block, "Extends", base)
            prop(block, "CreativeMode", "Player")
            prop(block, "DescriptionKey", name + "Desc")
            prop(block, "CustomIcon", base)
            prop(block, "CustomIconTint", COLOR[tier])
            prop(block, "MaxDamage", hp[i])
            prop(block, "RequiredPower", power[i])
            prop(block, "TakeDelay", 30)
            prop(block, "EconomicValue", 15000 + i * 6000)
            prop(block, "SellableToTrader", "false")
            prop(block, "PZAECDefenseDevice", stem)
            prop(block, "PZAECTier", tier)
            if stem == "SkyguardArray":
                prop(block, "AmmoItem", f"ammo762mmBulletBall+tags(ammo762mm)+ammoPZAECSkyguardInterceptorT{tier}")
                prop(block, "MaxDistance", [28, 32, 36, 40][i])
                prop(block, "EntityDamage", [450, 650, 900, 1250][i])
                prop(block, "BurstRoundCount", 8)
                prop(block, "CooldownTime", 2.5)
            elif stem == "HoundDecoyTower":
                prop(block, "HeatMapStrength", 20 + i * 3)
                prop(block, "HeatMapTime", 1500)
                prop(block, "HeatMapFrequency", 300)
            elif stem == "ShockNetNode":
                prop(block, "Buff", f"buffPZAECShockNetT{tier}")
                prop(block, "DamageReceived", [.7, .65, .6, .55][i])
            elif stem == "ArmorBreakTurret":
                prop(block, "AmmoItem", "ammo762mmBulletBall+tags(ammo762mm)")
                prop(block, "Buff", f"buffPZAECArmorBreakT{tier}")
                prop(block, "BuffChance", 1)
                prop(block, "EntityDamage", [26, 30, 34, 40][i])
                prop(block, "BurstRoundCount", 36)
                prop(block, "CooldownTime", 5)
            repair_items(block, 30 + i * 10, 10 + i * 5)
            loc(rows, name, "blocks", f"T{tier} {en}", f"T{tier} {cn}")
            if stem == "SkyguardArray":
                loc(rows, name + "Desc", "blocks", f"Powered T{tier} Skyguard array. Supports 7.62mm ammunition and matching-tier interceptor rounds. Base entity damage {[450,650,900,1250][i]}; dedicated rounds double it. Fires the first nonempty ammo slot. Automatic interception consumes no ammunition.", f"需供电{power[i]}W。支持7.62毫米弹药与同阶天穹截击弹：普通标准弹单发{[450,650,900,1250][i]}，同阶截击弹单发{[900,1300,1800,2500][i]}，未计爆头和目标减伤。每轮{[30,40,50,60][i]}发，射程{[28,32,36,40][i]}米；从前往后消耗弹药槽，换弹种可取出原弹或调整槽位。另可自动拦截攻城投射物，目前拦截不扣弹药。")
            else:
                loc(rows, name + "Desc", "blocks", "Powered endgame defense device with bounded range, upkeep and downtime.", "有范围、消耗和停机限制的供电终局防御设备。")

    forge = ET.SubElement(root, "block", {"name": "PZAECResonanceForge"})
    prop(forge, "Extends", "workbench")
    prop(forge, "CreativeMode", "Player")
    prop(forge, "DescriptionKey", "PZAECResonanceForgeDesc")
    prop(forge, "WorkstationWindow", "workstation_workbench")
    prop(forge, "CustomIcon", "workbench")
    prop(forge, "CustomIconTint", "AA88FF")
    prop(forge, "MaxDamage", 12000)
    prop(forge, "TakeDelay", 45)
    repair_items(forge, 50, 25)
    loc(rows, "PZAECResonanceForge", "blocks", "Resonance Forge", "共鸣锻造台")
    loc(rows, "PZAECResonanceForgeDesc", "blocks", "A 12000-durability workbench. Interact to craft using standard workbench recipes. No fuel or power required; ordinary workbenches can also craft this equipment.", "耐久12000的高级工作台。放置后按交互键打开制作界面，可制作普通工作台配方及T16–T19装备、组件和设备；不需燃料或供电，普通工作台也能制作这些物品。")

    relay = ET.SubElement(root, "block", {"name": "PZAECTacticalRelay"})
    prop(relay, "Extends", "electricwirerelay")
    prop(relay, "CreativeMode", "Player")
    prop(relay, "DescriptionKey", "PZAECTacticalRelayDesc")
    prop(relay, "CustomIcon", "electricwirerelay")
    prop(relay, "CustomIconTint", "55CCFF")
    prop(relay, "MaxDamage", 10000)
    prop(relay, "RequiredPower", 15)
    prop(relay, "TakeDelay", 30)
    repair_items(relay, 30, 20)
    loc(rows, "PZAECTacticalRelay", "blocks", "Tactical Command Relay", "战术指挥中继")
    loc(rows, "PZAECTacticalRelayDesc", "blocks", "Armored power relay and visible priority target for siege squads.", "装甲供电中继，也是工程小队能够识别并攻击的高价值目标。")

    for stem, (cn, en, base, hp) in FORTRESS_BLOCKS.items():
        for i, tier in enumerate(TIERS):
            name = f"PZAEC{stem}T{tier}"
            block = ET.SubElement(root, "block", {"name": name})
            prop(block, "Extends", base)
            if base == "steelMaster":
                prop(block, "Shape", "New")
                prop(block, "Model", "@:Shapes/arrow_slit.fbx" if stem == "Embrasure" else "@:Shapes/Cube.fbx")
            if stem == "Embrasure":
                # Selecting the mesh does not inherit shapes.xml properties.
                # Match native arrowSlit: bullets/bolts pass; movement, melee
                # and rockets still collide. This applies in both directions.
                prop(block, "Collide", "sight,movement,melee,rocket")
            prop(block, "CreativeMode", "Player")
            prop(block, "DescriptionKey", name + "Desc")
            prop(block, "CustomIcon", "vaultDoor01" if stem == "BlastGate" else base)
            prop(block, "CustomIconTint", COLOR[tier])
            prop(block, "MaxDamage", hp[i])
            prop(block, "LPHardnessScale", 4 + i)
            prop(block, "TakeDelay", 30)
            prop(block, "EconomicValue", 8000 + i * 4000)
            prop(block, "SellableToTrader", "false")
            prop(block, "PZAECFortressType", stem)
            prop(block, "PZAECTier", tier)
            if stem == "AblativeWall":
                prop(block, "DowngradeBlock", f"PZAECAblativeWallRuinT{tier}")
            elif stem == "BlastGate":
                prop(block, "RequiredPower", 30)
            repair_items(block, 20 + i * 10, 5 if stem == "ArmoredConduit" else 0)
            loc(rows, name, "blocks", f"T{tier} {en}", f"T{tier} {cn}")
            if stem == "ReactiveWall":
                reduction = [18, 22, 26, 30][i]
                loc(rows, name + "Desc", "blocks",
                    f"Repairable reactive armor wall. Once every 12 seconds, reduces only the portion of a hit above 800 damage by {reduction}%. No constant damage reduction; hits during cooldown take full damage.",
                    f"可维修反应装甲墙。单次伤害超过800时，只对超出800的部分减伤{reduction}%；每面墙冷却12秒。没有常驻减伤，冷却中的攻击正常扣血。")
            elif stem == "Embrasure":
                loc(rows, name + "Desc", "blocks", "Repairable armored firing slit. Bullets and arrows pass in both directions; blocks movement, melee and rockets.", "可维修装甲射击孔。允许子弹和箭矢双向穿过，阻挡角色移动、近战与火箭弹；不能作为防弹墙。")
            else:
                loc(rows, name + "Desc", "blocks", "Repairable fortress block designed for active Blood Moon defense.", "为主动血夜防守设计的可维修要塞建筑。")
            if stem == "AblativeWall":
                ruin_name = f"PZAECAblativeWallRuinT{tier}"
                ruin = ET.SubElement(root, "block", {"name": ruin_name})
                prop(ruin, "Extends", "steelMaster")
                prop(ruin, "Shape", "New")
                prop(ruin, "Model", "@:Shapes/Cube.fbx")
                prop(ruin, "CreativeMode", "Dev")
                prop(ruin, "CustomIcon", "steelMaster")
                prop(ruin, "CustomIconTint", "555555")
                prop(ruin, "MaxDamage", 2500)
                up = ET.SubElement(ruin, "property", {"class": "UpgradeBlock"})
                prop(up, "ToBlock", name)
                prop(up, "Item", "resourceDurablAlloys")
                prop(up, "ItemCount", [20, 28, 38, 50][i])
                prop(up, "UpgradeHitCount", 4)
    progression.defense_blocks(root)
    apply_block_colors(root, rows)
    return xml(root), rows


def add_recipe(root: ET.Element, name: str, ingredients: list[tuple[str, int]], minutes: float = 1, tags: str | None = None) -> None:
    attrs = {"name": name, "count": "1", "craft_area": "workbench", "craft_time": str(int(minutes * 60)), "always_unlocked": "true", "use_ingredient_modifier": "false"}
    if tags: attrs["tags"] = tags
    rec = ET.SubElement(root, "recipe", attrs)
    for item, count in ingredients: ET.SubElement(rec, "ingredient", {"name": item, "count": str(count)})


def build_recipes() -> str:
    root = ET.Element("append", {"xpath": "/recipes"})
    for stem in WEAPONS:
        prefix = "melee" if stem == "FaultlineHammer" else "gun"
        for i, tier in enumerate(TIERS):
            output = f"{prefix}PZAEC{stem}T{tier}"
            if tier == 16:
                direct = [("resourcePZAECWeaponChassis", 1), ("PZAECBuildPartsR2", 8), ("resourceLegendaryParts", 15), ("resourcePZAECSiegeCapacitorT16", 2), ("resourcePZAECMutantHeartT16", 1), ("itemPZAECArmoryBlueprintCrateT16", 1)]
                for predecessor in legendary.WEAPONS[stem]:
                    add_recipe(root, output, [(predecessor, 1)] + legendary.EXTRA.get(stem, []) + direct, 6)
            if tier > 16:
                prev = f"{prefix}PZAEC{stem}T{tier-1}"
                up = [(prev, 1), (f"PZAECBuildParts{RANK[tier]}", [0, 6, 8, 10][i]), ("resourceLegendaryParts", [0, 16, 24, 36][i]), (f"resourcePZAECSiegeCapacitorT{tier}", [0, 2, 3, 4][i]), (f"resourcePZAECMutantHeartT{tier}", [0, 1, 1, 2][i])]
                add_recipe(root, output, up, [0, 5, 7, 9][i], "upgrade")

    for stem in ("SkyguardInterceptor", "CounterPulse"):
        for i, tier in enumerate(TIERS):
            output = f"ammoPZAEC{stem}T{tier}"
            ingredients = [("resourceBulletCasing", 20), ("resourceGunPowder", 40 + i * 10), (f"resourcePZAEC{('CapacitorFragment' if stem == 'CounterPulse' else 'SiegeCapacitor')}T{tier}", 1), ("resourceElectricParts", 8 + i * 3)]
            rec = ET.SubElement(root, "recipe", {"name": output, "count": "20", "craft_area": "workbench", "craft_time": "90", "always_unlocked": "true", "use_ingredient_modifier": "false"})
            for item, count in ingredients: ET.SubElement(rec, "ingredient", {"name": item, "count": str(count)})

    for stem in COMPONENTS:
        for i, tier in enumerate(TIERS):
            output = f"modPZAEC{stem}T{tier}"
            direct = [(f"PZAECBuildParts{RANK[tier]}", [2, 3, 4, 5][i]), ("resourceLegendaryParts", [8, 12, 18, 26][i]), (f"resourcePZAECSiegeCapacitorT{tier}", [1, 1, 2, 2][i]), ("resourceElectricParts", [20, 25, 30, 40][i]), (f"itemPZAECComponentChoiceCrateT{tier}", 1)]
            add_recipe(root, output, direct, 4 + i)
            if tier > 16:
                add_recipe(root, output, [(f"modPZAEC{stem}T{tier-1}", 1), (f"PZAECBuildParts{RANK[tier]}", [0, 2, 3, 3][i]), ("resourceLegendaryParts", [0, 8, 12, 17][i]), (f"resourcePZAECSiegeCapacitorT{tier}", 1), ("resourceElectricParts", [0, 16, 20, 26][i])], 3 + i, "upgrade")

    for set_name in ("Harrier", "Storm", "Tremor", "Warden"):
        for mode in ("Stable", "Overload"):
            add_recipe(root, f"modPZAEC{set_name}{mode}T19", [("PZAECBuildPartsR5", 4), ("resourceLegendaryParts", 12), ("resourcePZAECSiegeCapacitorT19", 2), ("resourceElectricParts", 30)], 4)

    add_recipe(root, "resourcePZAECDefenseChassis", [("resourceDurablAlloys", 100), ("resourceMechanicalParts", 50), ("resourceElectricParts", 75), ("resourceDuctTape", 15), ("resourceLegendaryParts", 5)], 3)
    add_recipe(root, "itemPZAECQuickArmorGel", [("resourceDurablAlloys", 8), ("resourceScrapPolymers", 12), ("resourceDuctTape", 4), ("resourceAcid", 1), ("resourcePZAECCapacitorFragmentT16", 1)], 1)
    add_recipe(root, "itemPZAECResonanceInjector", [("medicalBloodBag", 2), ("resourceAcid", 1), ("resourcePZAECCoreFragmentT16", 1), ("drugVitamins", 1)], 1)
    add_recipe(root, "itemPZAECDecoyBeacon", [("resourceElectricParts", 20), ("resourceMechanicalParts", 10), ("carBattery", 1), ("resourceGunPowder", 20)], 2)
    add_recipe(root, "itemPZAECEvacAnchor", [("resourcePZAECDeviceChassis", 1), ("resourcePZAECSiegeCapacitorT18", 2), ("resourcePZAECMutantHeartT18", 1), ("resourceElectricParts", 40)], 5)
    for i, tier in enumerate(TIERS):
        add_recipe(root, f"thrownPZAECCounterJammerT{tier}", [(f"resourcePZAEC{('SiegeCapacitor')}T{tier}", 1), ("resourceScrapIron", 20), ("resourceGunPowder", 30), ("resourceElectricParts", 10)], 1)
        add_recipe(root, f"itemPZAECFieldRepairKitT{tier}", [("resourceRepairKit", 3), ("resourceDurablAlloys", 5), ("resourceOil", 3), ("resourceLegendaryParts", 1 + i)], 1)
        add_recipe(root, f"resourcePZAECCoreFragmentT{tier}", [(f"resourcePZAECMutantHeartT{tier}", 1)], .5)
        root[-1].set("count", "4")
        add_recipe(root, f"resourcePZAECCapacitorFragmentT{tier}", [(f"resourcePZAECSiegeCapacitorT{tier}", 1)], .5)
        root[-1].set("count", "6")
        add_recipe(root, f"resourcePZAECRepairChargeT{tier}", [("resourceRepairKit", 1), (f"resourcePZAECCapacitorFragmentT{tier}", 1), ("resourceDurablAlloys", 3 + i)], 1)
        root[-1].set("count", "5")
        add_recipe(root, f"resourcePZAECDecoyChargeT{tier}", [("resourceElectricParts", 4), (f"resourcePZAECCapacitorFragmentT{tier}", 1), ("resourceGunPowder", 5)], 1)
        root[-1].set("count", "5")

    for stem in DEVICE_BLOCKS:
        for i, tier in enumerate(TIERS):
            output = f"PZAEC{stem}T{tier}"
            if tier == 16:
                costs = [("resourcePZAECDefenseChassis", 1), ("PZAECBuildPartsR2", 8), ("resourceDurablAlloys", 120), ("resourceMechanicalParts", 60), ("resourceElectricParts", 80), ("resourcePZAECSiegeCapacitorT16", 3), ("resourceLegendaryParts", 12), ("itemPZAECDefenseBlueprintCrateT16", 1)]
            else:
                costs = [(f"PZAEC{stem}T{tier-1}", 1), (f"PZAECBuildParts{RANK[tier]}", [0, 6, 8, 10][i]), (f"resourcePZAECSiegeCapacitorT{tier}", [0, 2, 3, 4][i]), (f"resourcePZAECMutantHeartT{tier}", [0, 1, 1, 2][i]), ("resourceLegendaryParts", [0, 16, 24, 36][i])]
            add_recipe(root, output, costs, 4 + i, "upgrade" if tier > 16 else None)

    add_recipe(root, "PZAECResonanceForge", [("workbench", 1), ("forge", 1), ("resourceDurablAlloys", 200), ("resourceMechanicalParts", 100), ("resourceElectricParts", 100), ("resourcePZAECSiegeCapacitorT16", 4), ("resourcePZAECMutantHeartT16", 2), ("resourceLegendaryParts", 20)], 8)
    add_recipe(root, "PZAECTacticalRelay", [("electricwirerelay", 1), ("resourceDurablAlloys", 80), ("resourceElectricParts", 60), ("resourcePZAECSiegeCapacitorT16", 2)], 3)
    for stem in FORTRESS_BLOCKS:
        for i, tier in enumerate(TIERS):
            costs = [("steelShapes:VariantHelper", 1), ("resourceDurablAlloys", [40, 55, 75, 100][i]), ("resourceScrapPolymers", [20, 25, 30, 40][i]), (f"resourcePZAECSiegeCapacitorT{tier}", [1, 1, 1, 2][i])]
            if stem == "BlastGate": costs[0] = ("vaultDoor01_Powered", 1)
            if stem == "ArmoredConduit": costs[0] = ("electricwirerelay", 1)
            add_recipe(root, f"PZAEC{stem}T{tier}", costs, 2 + i)
    return xml(root)


def build_loot() -> str:
    groups = ET.Element("insertBefore", {"xpath": "/lootcontainers/lootgroup[@name='PZAECBossLootBundleT16_Content']"})
    for tier in TIERS:
        weapons = ET.SubElement(groups, "lootgroup", {"name": f"PZAECExpansionWeaponT{tier}", "count": "1"})
        for stem in WEAPONS:
            prefix = "melee" if stem == "FaultlineHammer" else "gun"
            ET.SubElement(weapons, "item", {"name": f"{prefix}PZAEC{stem}T{tier}", "count": "1"})
        components = ET.SubElement(groups, "lootgroup", {"name": f"PZAECExpansionComponentT{tier}", "count": "1"})
        for stem in COMPONENTS: ET.SubElement(components, "item", {"name": f"modPZAEC{stem}T{tier}", "count": "1"})
        if tier == 19:
            calibration = ET.SubElement(groups, "lootgroup", {"name": "PZAECCalibrationT19", "count": "1"})
            for set_name in ("Harrier", "Storm", "Tremor", "Warden"):
                for mode in ("Stable", "Overload"):
                    ET.SubElement(calibration, "item", {"name": f"modPZAEC{set_name}{mode}T19", "count": "1"})
    chunks = [xml(groups)]
    for i, tier in enumerate(TIERS):
        boss = ET.Element("append", {"xpath": f"/lootcontainers/lootgroup[@name='PZAECBossLootBundleT{tier}_Content']"})
        ET.SubElement(boss, "item", {"name": f"itemPZAECArmoryBlueprintCrateT{tier}", "count": "1", "prob": str([.08, .12, .18, .25][i]), "force_prob": "true"})
        ET.SubElement(boss, "item", {"name": f"itemPZAECComponentChoiceCrateT{tier}", "count": "1", "prob": str([.18, .25, .35, .50][i]), "force_prob": "true"})
        ET.SubElement(boss, "item", {"group": f"PZAECExpansionWeaponT{tier}", "count": "1", "prob": str([.02, .03, .04, .06][i]), "force_prob": "true"})
        if tier == 19:
            ET.SubElement(boss, "item", {"group": "PZAECCalibrationT19", "count": "1", "prob": ".15", "force_prob": "true"})
        chunks.append(xml(boss))
        siege = ET.Element("append", {"xpath": f"/lootcontainers/lootgroup[@name='PZAECSiegeMaterialsT{tier}']"})
        ET.SubElement(siege, "item", {"name": f"resourcePZAECCapacitorFragmentT{tier}", "count": "1,3", "prob": ".75"})
        ET.SubElement(siege, "item", {"name": f"thrownPZAECCounterJammerT{tier}", "count": "1", "prob": str([.08, .12, .18, .25][i])})
        ET.SubElement(siege, "item", {"name": f"itemPZAECDefenseBlueprintCrateT{tier}", "count": "1", "prob": str([.04, .06, .09, .12][i])})
        chunks.append(xml(siege))
    return "\n".join(chunks)


def build_entities() -> tuple[str, list[tuple[str, str, str, str]]]:
    root = ET.Element("append", {"xpath": "/entity_classes"})
    rows: list[tuple[str, str, str, str]] = []
    tier_suffix = {16: "Tier16Transcendent", 17: "Tier17Ascendant", 18: "Tier18Eternal", 19: "Tier19Apocalyptic"}
    for tier in TIERS:
        for stem, base in [("Disruptor", f"PZAECSiege_acid_T{tier}"), ("Spotter", f"PZAECSiege_heavy_T{tier}"), ("Linesman", "AECZombieUtilityWorker" + tier_suffix[tier])]:
            name = f"PZAECSiege{stem}T{tier}"
            ent = ET.SubElement(root, "entity_class", {"name": name, "extends": base})
            prop(ent, "UserSpawnType", "None")
            prop(ent, "PZAECSiegeRole", stem)
            if stem == "Disruptor":
                prop(ent, "HandItem", f"meleeHandPZAECSiege_acid_T{tier}")
                prop(ent, "AITask", "PZAECSiege,AEC.T16.RuntimeFix itemType=1;cooldown=8;duration=7;minRange=8;maxRange=52;sndStart=hulkvomitwarning|BreakBlock|ApproachAndAttackTarget class=EntityPlayer,0|ApproachSpot|Look|Wander")
            elif stem == "Spotter":
                prop(ent, "HandItem", f"meleeHandPZAECSiege_heavy_T{tier}")
                prop(ent, "AITask", "PZAECSiege,AEC.T16.RuntimeFix itemType=1;cooldown=12;duration=7;minRange=10;maxRange=60;sndStart=hulkvomitwarning|BreakBlock|ApproachAndAttackTarget class=EntityPlayer,0|ApproachSpot|Look|Wander")
            else:
                prop(ent, "AITask", "BreakBlock|ApproachAndAttackTarget class=EntityPlayer,0|ApproachSpot|Look|Wander")
            role_cn = {"Disruptor": "攻城干扰者", "Spotter": "攻城校射官", "Linesman": "攻城切线者"}[stem]
            loc(rows, name, "entityclasses", f"T{tier} Siege {stem}", f"T{tier} {role_cn}")
    return xml(root), rows


def build_entitygroups() -> str:
    chunks = []
    for tier in TIERS:
        patch = ET.Element("append", {"xpath": f"/entitygroups/entitygroup[@name='PZAECBloodMoonT{tier}Fallback']"})
        for stem, weight in [("Disruptor", ".018"), ("Spotter", ".014"), ("Linesman", ".022")]:
            ET.SubElement(patch, "e", {"n": f"PZAECSiege{stem}T{tier}", "p": weight})
        chunks.append(xml(patch))
    return "\n".join(chunks)


def write_localization(rows: list[tuple[str, str, str, str]]) -> None:
    path = CONFIG / "Localization.csv"
    lines = path.read_text(encoding="utf-8-sig").splitlines()
    kept = [line for line in lines if ",EndgameExpansion," not in line]
    output = io.StringIO()
    writer = csv.writer(output, lineterminator="\n")
    for key, file_name, en, cn in rows:
        writer.writerow([key, file_name, "EndgameExpansion", "", "", "", en, "", "", "", "", "", "", "", "", "", "", "", cn, cn])
    path.write_text("\n".join(kept).rstrip() + "\n" + output.getvalue(), encoding="utf-8", newline="\n")


def main() -> None:
    item_xml, item_rows = build_items()
    mod_xml, mod_rows = build_modifiers()
    buff_xml, buff_rows = build_buffs()
    block_xml, block_rows = build_blocks()
    ammo_ui = ET.Element("append", {"xpath": "/ui_display_info/item_display"})
    panel = ET.SubElement(ammo_ui, "item_display_info", {"display_type": "aecSkyguardAmmo", "display_group": "groupAmmo"})
    ET.SubElement(panel, "display_entry", {"name": "dSkyguardDamage", "title_key": "aecSkyguardDamageTitle", "display_type": "Decimal1"})
    ET.SubElement(panel, "display_entry", {"name": "dSkyguardBonus", "title_key": "aecSkyguardBonusTitle", "display_type": "Percent", "display_leading_plus": "true"})
    loc(item_rows, "aecSkyguardDamageTitle", "ui_display", "Matching-tier turret damage", "同阶阵列单发伤害")
    loc(item_rows, "aecSkyguardBonusTitle", "ui_display", "Turret damage bonus", "阵列伤害加成")
    replace_generated(CONFIG / "ui_display.xml", xml(ammo_ui))
    replace_generated(CONFIG / "items.xml", item_xml)
    replace_generated(CONFIG / "item_modifiers.xml", mod_xml)
    replace_generated(CONFIG / "buffs.xml", buff_xml)
    replace_generated(CONFIG / "blocks.xml", block_xml)
    replace_generated(CONFIG / "recipes.xml", build_recipes())
    replace_generated(CONFIG / "loot.xml", build_loot())
    entity_xml, entity_rows = build_entities()
    replace_generated(CONFIG / "entityclasses.xml", entity_xml)
    replace_generated(CONFIG / "entitygroups.xml", build_entitygroups())
    write_localization(item_rows + mod_rows + buff_rows + block_rows + entity_rows)
    from generate_fusion_upgrades import refresh
    refresh(CONFIG)
    import equipment_display
    equipment_display.refresh(CONFIG)


if __name__ == "__main__":
    main()

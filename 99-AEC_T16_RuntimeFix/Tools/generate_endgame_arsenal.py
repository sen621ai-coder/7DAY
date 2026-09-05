from __future__ import annotations

import copy
import csv
import io
import pathlib
import xml.etree.ElementTree as ET
import endgame_progression as progression
import legendary_prerequisites as legendary


MOD = pathlib.Path(__file__).resolve().parents[1]
GAME = MOD.parents[1]
VANILLA_ITEMS = GAME / "Data" / "Config" / "items.xml"
CONFIG = MOD / "Config"
BEGIN = "<!-- BEGIN GENERATED ENDGAME ARSENAL -->"
END = "<!-- END GENERATED ENDGAME ARSENAL -->"

TIERS = {
    16: dict(rank="R2", color="66BBFF", armor=0, comp=4, legendary=6, cap=2, heart=1, electric=20,
             device_comp=6, device_legendary=10, device_cap=4, device_heart=1, active=8, cooldown=36),
    17: dict(rank="R3", color="AA88FF", armor=1, comp=5, legendary=10, cap=3, heart=1, electric=30,
             device_comp=7, device_legendary=16, device_cap=5, device_heart=2, active=9, cooldown=34),
    18: dict(rank="R4", color="FFBB44", armor=2, comp=6, legendary=16, cap=4, heart=2, electric=40,
             device_comp=8, device_legendary=24, device_cap=7, device_heart=2, active=10, cooldown=32),
    19: dict(rank="R5", color="FF6655", armor=3, comp=8, legendary=24, cap=6, heart=3, electric=50,
             device_comp=10, device_legendary=36, device_cap=9, device_heart=3, active=12, cooldown=30),
}

for tier, data in TIERS.items():
    data['active'] = progression.ACTIVE_DURATION[tier - 16]
    data['cooldown'] = progression.COOLDOWN_DURATION[tier - 16]

SETS = {
    "Harrier": dict(cn="猎隼", base="armorRanger", master="armorLightMaster", armor=[15, 16, 17, 18],
                    icon="modGunScopeLarge", charge=[10, 11, 12, 14],
                    role="精准射击", active_cn="折光标定", active_en="Refractive Designation"),
    "Storm": dict(cn="雷暴", base="armorCommando", master="armorMediumMaster", armor=[17, 18, 19, 20],
                  icon="modGunDrumMagazineExtender", charge=[4, 5, 6, 7],
                  role="持续火力", active_cn="弧光泄压", active_en="Arc Vent"),
    "Tremor": dict(cn="震岳", base="armorRaider", master="armorHeavyMaster", armor=[21, 22, 23, 24],
                   icon="modMeleeWeightedHead", charge=[16, 18, 20, 22],
                   role="近战重击", active_cn="震岳释放", active_en="Tremor Release"),
    "Warden": dict(cn="守望", base="armorNomad", master="armorHeavyMaster", armor=[20, 21, 22, 23],
                   icon="modArmorCustomizedFittings", charge=[8, 9, 10, 12],
                   role="承伤防守", active_cn="守望领域", active_en="Warden Field"),
}

SLOTS = {
    "Helmet": dict(cn="头盔", suffix="Helmet", slot="Head"),
    "Outfit": dict(cn="衣装", suffix="Outfit", slot="Chest"),
    "Gloves": dict(cn="手套", suffix="Gloves", slot="Hands"),
    "Boots": dict(cn="战靴", suffix="Boots", slot="Feet"),
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


def element_text(element: ET.Element, indent: str = "    ") -> str:
    ET.indent(element, space="  ")
    return "\n".join(indent + line for line in ET.tostring(element, encoding="unicode").splitlines())


def vanilla_reference_items() -> dict[str, ET.Element]:
    root = ET.parse(VANILLA_ITEMS).getroot()
    return {item.get("name"): item for item in root.findall("item")}


def base_armor_effect(set_name: str, tier: int, slot: str) -> ET.Element:
    spec = SETS[set_name]
    idx = tier - 16
    armor = spec["armor"][idx]
    eg = ET.Element("effect_group", {"name": f"AEC {set_name} T{tier} {slot}", "tiered": "false"})
    for name, operation, value, tags in [
        ("ModSlots", "base_set", "5", None),
        ("PhysicalDamageResist", "base_add", str(armor), None),
        ("ElementalDamageResist", "base_add", str(armor), "heat,electrical"),
        ("BuffResistance", "base_add", ".06", "buffFatiguedTrigger,buffArmSprainedCHTrigger,buffLegSprainedCHTrigger,buffAbrasionCatch,buffLaceration,buffInfectionCatch,buffInjuryStunned01CHTrigger,buffInjuryBleedingTwo,buffInjuryBleedingBarbedWire"),
        ("DegradationMax", "base_set", str(1800 + idx * 400), None),
        ("DegradationPerUse", "base_set", "1", "lightArmorDeg" if set_name == "Harrier" else ("mediumArmorDeg" if set_name == "Storm" else "heavyArmorDeg")),
    ]:
        attrs = {"name": name, "operation": operation, "value": value}
        if tags:
            attrs["tags"] = tags
        ET.SubElement(eg, "passive_effect", attrs)
    if set_name == "Storm":
        ET.SubElement(eg, "passive_effect", {"name": "Mobility", "operation": "perc_add", "value": "-.025"})
    elif set_name in ("Tremor", "Warden"):
        ET.SubElement(eg, "passive_effect", {"name": "Mobility", "operation": "perc_add", "value": "-.04" if set_name == "Tremor" else "-.03"})
    return eg


def unique_armor_effect(set_name: str, tier: int, slot: str) -> ET.Element:
    idx = tier - 16
    eg = ET.Element("effect_group", {"name": f"AEC {set_name} {slot} specialty", "tiered": "false"})
    req_tags = None
    effects: list[tuple[str, str, str, str | None]] = []
    if set_name == "Harrier":
        req_tags = "ranged"
        if slot == "Helmet": effects = [("HeadshotDamageModifier", "perc_add", [".06", ".08", ".10", ".12"][idx], "head")]
        if slot == "Outfit": effects = [("StaminaMax", "base_add", ["10", "15", "20", "25"][idx], None)]
        if slot == "Gloves": effects = [
            ("SpreadMultiplierAiming", "perc_add", ["-.04", "-.05", "-.06", "-.08"][idx], None),
            ("KickDegreesVerticalMax", "perc_add", ["-.04", "-.05", "-.06", "-.08"][idx], None),
            ("KickDegreesHorizontalMax", "perc_add", ["-.04", "-.05", "-.06", "-.08"][idx], None),
        ]
        if slot == "Boots": effects = [("RunSpeed", "perc_add", [".02", ".03", ".04", ".05"][idx], None)]
    elif set_name == "Storm":
        req_tags = "ranged"
        if slot == "Helmet": effects = [("ReloadSpeedMultiplier", "perc_add", [".05", ".07", ".09", ".12"][idx], None)]
        if slot == "Outfit": effects = [("MagazineSize", "perc_add", [".05", ".08", ".11", ".15"][idx], None)]
        if slot == "Gloves": effects = [("RoundsPerMinute", "perc_add", [".05", ".07", ".09", ".12"][idx], None)]
        if slot == "Boots": effects = [("StaminaChangeOT", "perc_add", [".08", ".10", ".12", ".15"][idx], "running")]
    elif set_name == "Tremor":
        req_tags = "melee"
        if slot == "Helmet": effects = [("BuffResistance", "base_add", [".08", ".10", ".12", ".15"][idx], "buffInjuryStunned01CHTrigger,buffInjuryBleedingTwo")]
        if slot == "Outfit": effects = [("HealthMax", "base_add", ["15", "20", "25", "35"][idx], None)]
        if slot == "Gloves": effects = [("EntityDamage", "perc_add", [".08", ".10", ".12", ".15"][idx], "secondary")]
        if slot == "Boots": effects = [("StaminaLoss", "perc_add", ["-.06", "-.08", "-.10", "-.12"][idx], "secondary")]
    else:
        if slot == "Helmet": effects = [("BuffResistance", "base_add", [".08", ".10", ".12", ".15"][idx], "buffFatiguedTrigger,buffAbrasionCatch,buffLaceration,buffInfectionCatch")]
        if slot == "Outfit": effects = [("HealthMax", "base_add", ["20", "30", "40", "50"][idx], None)]
        if slot == "Gloves": effects = [("BlockRepairAmount", "perc_add", [".10", ".15", ".20", ".25"][idx], None)]
        if slot == "Boots": effects = [("RunSpeed", "perc_add", [".02", ".03", ".04", ".05"][idx], None)]
    if req_tags and slot not in ("Outfit", "Boots"):
        ET.SubElement(eg, "requirement", {"name": "HoldingItemHasTags", "tags": req_tags})
    for name, operation, value, tags in effects:
        attrs = {"name": name, "operation": operation, "value": value}
        if tags: attrs["tags"] = tags
        ET.SubElement(eg, "passive_effect", attrs)
    return eg


def build_items() -> tuple[str, list[tuple[str, str, str, str]]]:
    refs = vanilla_reference_items()
    append = ET.Element("append", {"xpath": "/items"})
    loc: list[tuple[str, str, str, str]] = []

    resources = [
        ("resourcePZAECWeaponChassis", "Weapon Arsenal Chassis", "武器军械底盘", "weapon"),
        ("resourcePZAECArmorChassis", "Armor Arsenal Chassis", "护甲军械底盘", "armor"),
        ("resourcePZAECDeviceChassis", "Resonance Device Chassis", "共鸣装置底盘", "device"),
    ]
    for name, en, cn, kind in resources:
        item = ET.SubElement(append, "item", {"name": name})
        ET.SubElement(item, "property", {"name": "Extends", "value": "resourceLegendaryParts"})
        ET.SubElement(item, "property", {"name": "CreativeMode", "value": "Player"})
        ET.SubElement(item, "property", {"name": "DescriptionKey", "value": name + "Desc"})
        ET.SubElement(item, "property", {"name": "CustomIcon", "value": "resourceLegendaryParts"})
        ET.SubElement(item, "property", {"name": "CustomIconTint", "value": {"weapon": "FF8844", "armor": "66BBFF", "device": "AA88FF"}[kind]})
        ET.SubElement(item, "property", {"name": "Stacknumber", "value": "20"})
        ET.SubElement(item, "property", {"name": "SellableToTrader", "value": "false"})
        loc += [(name, "items", en, cn), (name + "Desc", "items", "A reusable endgame crafting frame made at a workbench.", "在工作台制作的终局装备通用底盘。")]

    for tier, data in TIERS.items():
        for stem, en, cn in [("SiegeCapacitor", f"T{tier} Siege Capacitor", f"T{tier} 攻城电容"),
                             ("MutantHeart", f"T{tier} Mutant Heart", f"T{tier} 异变心核")]:
            name = f"resourcePZAEC{stem}T{tier}"
            item = ET.SubElement(append, "item", {"name": name})
            ET.SubElement(item, "property", {"name": "Extends", "value": "resourceLegendaryParts"})
            ET.SubElement(item, "property", {"name": "CreativeMode", "value": "Player"})
            ET.SubElement(item, "property", {"name": "DescriptionKey", "value": name + "Desc"})
            ET.SubElement(item, "property", {"name": "CustomIcon", "value": "resourceElectricParts" if stem == "SiegeCapacitor" else "resourceLegendaryParts"})
            ET.SubElement(item, "property", {"name": "CustomIconTint", "value": data["color"]})
            ET.SubElement(item, "property", {"name": "Stacknumber", "value": "100" if stem == "SiegeCapacitor" else "20"})
            ET.SubElement(item, "property", {"name": "SellableToTrader", "value": "false"})
            desc_en = "Recovered from Blood Moon siege engineers." if stem == "SiegeCapacitor" else "Recovered from same-tier endgame boss caches."
            desc_cn = "由血月工程小队掉落。" if stem == "SiegeCapacitor" else "由同阶终局 Boss 奖励箱产出。"
            loc += [(name, "items", en, cn), (name + "Desc", "items", desc_en, desc_cn)]

    for set_name, spec in SETS.items():
        for tier, data in TIERS.items():
            for slot, slot_spec in SLOTS.items():
                name = f"armorPZAEC{set_name}{slot}T{tier}"
                ref = refs[spec["base"] + slot_spec["suffix"]]
                item = ET.SubElement(append, "item", {"name": name})
                ET.SubElement(item, "property", {"name": "Extends", "value": spec["master"]})
                ET.SubElement(item, "property", {"name": "CreativeMode", "value": "Player"})
                ET.SubElement(item, "property", {"name": "DescriptionKey", "value": f"armorPZAEC{set_name}{slot}Desc"})
                ET.SubElement(item, "property", {"name": "CustomIcon", "value": spec["base"] + slot_spec["suffix"]})
                ET.SubElement(item, "property", {"name": "CustomIconTint", "value": data["color"]})
                ET.SubElement(item, "property", {"name": "DisplayType", "value": ref.find("property[@name='DisplayType']").get("value")})
                ET.SubElement(item, "property", {"name": "ArmorGroup", "value": f"groupPZAEC{set_name}T{tier}"})
                tags = ref.find("property[@name='Tags']").get("value").split(",")
                tags = [x for x in tags if not x.endswith("Bonus") and x != "biomeChallenge"]
                if set_name == "Harrier":
                    tags = [{"mediumArmor": "lightArmor", "mediumArmorPenalty": "lightArmor",
                             "mediumArmorDeg": "lightArmorDeg", "armorMediumSkill": "armorLightSkill"}.get(x, x)
                            for x in tags]
                    tags = list(dict.fromkeys(tags))
                tags += [f"PZAECArmor{set_name}", f"PZAECArmorT{tier}"]
                ET.SubElement(item, "property", {"name": "Tags", "value": ",".join(tags)})
                ET.SubElement(item, "property", {"name": "EquipSlot", "value": slot_spec["slot"]})
                for prop_name in ("SoundPickup", "SoundPlace", "SoundImpactHit", "SoundImpactGraze"):
                    found = ref.find(f"property[@name='{prop_name}']")
                    if found is not None: item.append(copy.deepcopy(found))
                ET.SubElement(item, "property", {"name": "PZAECAdvancedLoot", "value": "true"})
                ET.SubElement(item, "property", {"name": "EconomicValue", "value": str(4500 + (tier - 16) * 2500)})
                ET.SubElement(item, "property", {"name": "SellableToTrader", "value": "false"})
                item.append(base_armor_effect(set_name, tier, slot))
                item.append(unique_armor_effect(set_name, tier, slot))
                sdcs = ref.find("property[@class='SDCS']")
                if sdcs is not None: item.append(copy.deepcopy(sdcs))
                loc.append((name, "items", f"T{tier} {set_name} {slot}", f"T{tier} {spec['cn']}{slot_spec['cn']}"))
                desc_key = f"armorPZAEC{set_name}{slot}Desc"
                if not any(row[0] == desc_key for row in loc):
                    loc.append((desc_key, "items", f"A true armor piece for the {set_name} {spec['role']} set. Exact-tier pieces unlock 2/3/4-piece bonuses.",
                                f"{spec['cn']}“{spec['role']}”真护甲组件。同一等级穿戴 2／3／4 件依次解锁套装效果。"))

            device = f"itemPZAEC{set_name}DeviceT{tier}"
            item = ET.SubElement(append, "item", {"name": device})
            ET.SubElement(item, "property", {"name": "Extends", "value": "resourceLegendaryParts"})
            ET.SubElement(item, "property", {"name": "CreativeMode", "value": "Player"})
            ET.SubElement(item, "property", {"name": "DescriptionKey", "value": device + "Desc"})
            ET.SubElement(item, "property", {"name": "CustomIcon", "value": spec["icon"]})
            ET.SubElement(item, "property", {"name": "CustomIconTint", "value": data["color"]})
            ET.SubElement(item, "property", {"name": "Stacknumber", "value": "1"})
            ET.SubElement(item, "property", {"name": "SellableToTrader", "value": "false"})
            action = ET.SubElement(item, "property", {"class": "Action0"})
            ET.SubElement(action, "property", {"name": "Class", "value": "Eat"})
            ET.SubElement(action, "property", {"name": "Consume", "value": "false"})
            ET.SubElement(action, "property", {"name": "Delay", "value": ".4"})
            ET.SubElement(action, "property", {"name": "Sound_start", "value": "ui_mag_read"})
            ET.SubElement(action, "requirement", {"name": "HasBuff", "buff": f"buffPZAEC{set_name}T{tier}Set4"})
            ET.SubElement(action, "requirement", {"name": "HasBuff", "buff": f"buffPZAEC{set_name}T{tier}Ready"})
            ET.SubElement(action, "requirement", {"name": "!HasBuff", "buff": f"buffPZAEC{set_name}T{tier}Cooldown"})
            eg = ET.SubElement(item, "effect_group", {"tiered": "false"})
            ET.SubElement(eg, "triggered_effect", {"trigger": "onSelfPrimaryActionEnd", "action": "AddBuff", "buff": f"buffPZAEC{set_name}T{tier}Active"})
            ET.SubElement(eg, "triggered_effect", {"trigger": "onSelfPrimaryActionEnd", "action": "AddBuff", "buff": f"buffPZAEC{set_name}T{tier}Cooldown"})
            ET.SubElement(eg, "triggered_effect", {"trigger": "onSelfPrimaryActionEnd", "action": "RemoveBuff", "buff": f"buffPZAEC{set_name}T{tier}Ready"})
            ET.SubElement(eg, "triggered_effect", {"trigger": "onSelfPrimaryActionEnd", "action": "ModifyCVar", "cvar": f"$PZAEC{set_name}T{tier}Resonance", "operation": "set", "value": "0"})
            loc += [(device, "items", f"T{tier} {spec['active_en']} Device", f"T{tier} {spec['active_cn']}装置"),
                    (device + "Desc", "items", f"Reusable. Equip the exact-tier four-piece {set_name} set, fill resonance, then use from the toolbelt. Active {data['active']}s; cooldown {data['cooldown']}s.",
                     f"可重复使用。穿齐同阶四件{spec['cn']}套装并充满共鸣后，从快捷栏启动；持续 {data['active']} 秒，冷却 {data['cooldown']} 秒。")]
    progression.armor_items(append)
    return element_text(append), loc


def add_effect(parent: ET.Element, name: str, operation: str, value: str, tags: str | None = None) -> None:
    attrs = {"name": name, "operation": operation, "value": value}
    if tags: attrs["tags"] = tags
    ET.SubElement(parent, "passive_effect", attrs)


def build_buffs() -> tuple[str, list[tuple[str, str, str, str]]]:
    check = ET.Element("append", {"xpath": "/buffs/buff[@name='buffStatusCheck02']"})
    definitions = ET.Element("append", {"xpath": "/buffs"})
    loc: list[tuple[str, str, str, str]] = []
    for set_name, spec in SETS.items():
        for tier, data in TIERS.items():
            group = f"groupPZAEC{set_name}T{tier}"
            for pieces in (2, 3, 4):
                buff = f"buffPZAEC{set_name}T{tier}Set{pieces}"
                eg = ET.SubElement(check, "effect_group")
                add = ET.SubElement(eg, "triggered_effect", {"trigger": "onSelfBuffUpdate", "action": "AddBuff", "buff": buff})
                ET.SubElement(add, "requirement", {"name": "ArmorGroupCount", "group_name": group, "operation": "GTE", "value": str(pieces)})
                ET.SubElement(add, "requirement", {"name": "!HasBuff", "buff": buff})
                remove = ET.SubElement(eg, "triggered_effect", {"trigger": "onSelfBuffUpdate", "action": "RemoveBuff", "buff": buff})
                ET.SubElement(remove, "requirement", {"name": "ArmorGroupCount", "group_name": group, "operation": "LTE", "value": str(pieces - 1)})
                ET.SubElement(remove, "requirement", {"name": "HasBuff", "buff": buff})

            two = ET.SubElement(definitions, "buff", {"name": f"buffPZAEC{set_name}T{tier}Set2", "hidden": "true"})
            ET.SubElement(two, "stack_type", {"value": "ignore"})
            eg = ET.SubElement(two, "effect_group")
            idx = tier - 16
            if set_name == "Harrier":
                ET.SubElement(eg, "requirement", {"name": "HoldingItemHasTags", "tags": "ranged"})
                add_effect(eg, "ReloadSpeedMultiplier", "perc_add", [".05", ".06", ".07", ".08"][idx])
            elif set_name == "Storm": add_effect(eg, "StaminaMax", "base_add", ["20", "25", "30", "35"][idx])
            elif set_name == "Tremor":
                ET.SubElement(eg, "requirement", {"name": "HoldingItemHasTags", "tags": "melee"})
                add_effect(eg, "StaminaLoss", "perc_add", ["-.08", "-.10", "-.12", "-.14"][idx], "secondary")
            else: add_effect(eg, "PhysicalDamageResist", "base_add", ["3", "4", "5", "6"][idx])

            cvar = f"$PZAEC{set_name}T{tier}Resonance"
            three = ET.SubElement(definitions, "buff", {"name": f"buffPZAEC{set_name}T{tier}Set3",
                "name_key": f"buffPZAEC{set_name}ResonanceName", "description_key": f"buffPZAEC{set_name}ResonanceDesc",
                "icon": "ui_game_symbol_armor_iron", "icon_color": "255,200,64"})
            ET.SubElement(three, "stack_type", {"value": "ignore"})
            ET.SubElement(three, "display_value", {"value": cvar})
            ET.SubElement(three, "display_value_key", {"value": "{0:0}/100"})
            eg = ET.SubElement(three, "effect_group")
            trigger = "onOtherAttackedSelf" if set_name == "Warden" else "onSelfAttackedOther"
            gain = ET.SubElement(eg, "triggered_effect", {"trigger": trigger, "action": "ModifyCVar", "cvar": cvar,
                "operation": "add", "value": str(spec["charge"][idx])})
            ET.SubElement(gain, "requirement", {"name": "CVarCompare", "cvar": cvar, "operation": "LT", "value": "100"})
            ET.SubElement(gain, "requirement", {"name": "!HasBuff", "buff": f"buffPZAEC{set_name}T{tier}Ready"})
            ET.SubElement(gain, "requirement", {"name": "!HasBuff", "buff": f"buffPZAEC{set_name}T{tier}Active"})
            if set_name == "Harrier":
                ET.SubElement(gain, "requirement", {"name": "HoldingItemHasTags", "tags": "ranged"})
                ET.SubElement(gain, "requirement", {"name": "HitLocation", "body_parts": "Head"})
            elif set_name == "Storm": ET.SubElement(gain, "requirement", {"name": "HoldingItemHasTags", "tags": "ranged"})
            elif set_name == "Tremor":
                ET.SubElement(gain, "requirement", {"name": "HoldingItemHasTags", "tags": "melee"})
                ET.SubElement(gain, "requirement", {"name": "IsSecondaryAttack"})
            cap = ET.SubElement(eg, "triggered_effect", {"trigger": trigger, "action": "ModifyCVar", "cvar": cvar,
                "operation": "set", "value": "100"})
            ET.SubElement(cap, "requirement", {"name": "CVarCompare", "cvar": cvar, "operation": "GTE", "value": "100"})
            ready = ET.SubElement(eg, "triggered_effect", {"trigger": trigger, "action": "AddBuff", "buff": f"buffPZAEC{set_name}T{tier}Ready"})
            ET.SubElement(ready, "requirement", {"name": "CVarCompare", "cvar": cvar, "operation": "GTE", "value": "100"})
            ET.SubElement(ready, "requirement", {"name": "!HasBuff", "buff": f"buffPZAEC{set_name}T{tier}Ready"})
            ET.SubElement(eg, "triggered_effect", {"trigger": "onSelfBuffRemove", "action": "RemoveCVar", "cvar": cvar})
            ET.SubElement(eg, "triggered_effect", {"trigger": "onSelfBuffRemove", "action": "RemoveBuff", "buff": f"buffPZAEC{set_name}T{tier}Ready"})

            ready_buff = ET.SubElement(definitions, "buff", {"name": f"buffPZAEC{set_name}T{tier}Ready", "hidden": "true"})
            ET.SubElement(ready_buff, "stack_type", {"value": "ignore"})
            four = ET.SubElement(definitions, "buff", {"name": f"buffPZAEC{set_name}T{tier}Set4", "hidden": "true"})
            ET.SubElement(four, "stack_type", {"value": "ignore"})
            four_group = ET.SubElement(four, "effect_group")
            ET.SubElement(four_group, "triggered_effect", {"trigger": "onSelfBuffRemove", "action": "RemoveBuff",
                "buff": f"buffPZAEC{set_name}T{tier}Active"})

            active = ET.SubElement(definitions, "buff", {"name": f"buffPZAEC{set_name}T{tier}Active",
                "name_key": f"buffPZAEC{set_name}ActiveName", "description_key": f"buffPZAEC{set_name}ActiveDesc",
                "icon": "ui_game_symbol_armor_iron", "icon_color": "80,200,255"})
            ET.SubElement(active, "stack_type", {"value": "replace"})
            ET.SubElement(active, "duration", {"value": str(data["active"])})
            eg = ET.SubElement(active, "effect_group")
            if set_name == "Harrier":
                ET.SubElement(eg, "requirement", {"name": "HoldingItemHasTags", "tags": "ranged"})
                add_effect(eg, "EntityDamage", "perc_add", [".08", ".10", ".12", ".15"][idx])
                add_effect(eg, "HeadshotDamageModifier", "perc_add", [".10", ".12", ".15", ".20"][idx], "head")
            elif set_name == "Storm":
                ET.SubElement(eg, "requirement", {"name": "HoldingItemHasTags", "tags": "ranged"})
                add_effect(eg, "RoundsPerMinute", "perc_add", [".15", ".20", ".25", ".30"][idx])
                add_effect(eg, "ReloadSpeedMultiplier", "perc_add", [".10", ".12", ".15", ".20"][idx])
                add_effect(eg, "EntityPenetrationCount", "base_add", "1")
                add_effect(eg, "SpreadMultiplierHip", "perc_add", ".20")
            elif set_name == "Tremor":
                ET.SubElement(eg, "requirement", {"name": "HoldingItemHasTags", "tags": "melee"})
                add_effect(eg, "EntityDamage", "perc_add", [".25", ".30", ".35", ".40"][idx], "secondary")
                add_effect(eg, "StaminaLoss", "perc_add", ["-.15", "-.20", "-.25", "-.30"][idx], "secondary")
                add_effect(eg, "BlockDamage", "perc_set", "0", "secondary")
            else:
                add_effect(eg, "RunSpeed", "perc_add", "-.15")
                # target_tags is an OR filter in 7DTD. Keep the owner explicit,
                # then filter the AOE to allied/party player entities so hostile
                # players cannot receive the defensive aura in PvP.
                ET.SubElement(eg, "triggered_effect", {"trigger": "onSelfBuffUpdate", "action": "AddBuff",
                    "buff": f"buffPZAECWardenT{tier}Aura"})
                aura = ET.SubElement(eg, "triggered_effect", {"trigger": "onSelfBuffUpdate", "action": "AddBuff",
                    "target": "selfAOE", "range": ["8", "9", "10", "12"][idx],
                    "target_tags": "ally,party", "buff": f"buffPZAECWardenT{tier}Aura"})
                ET.SubElement(aura, "requirement", {"name": "EntityTagCompare", "target": "other", "tags": "player"})
                ally = ET.SubElement(definitions, "buff", {"name": f"buffPZAECWardenT{tier}Aura", "hidden": "true"})
                ET.SubElement(ally, "stack_type", {"value": "replace"})
                ET.SubElement(ally, "duration", {"value": "1.5"})
                ally_group = ET.SubElement(ally, "effect_group")
                add_effect(ally_group, "PhysicalDamageResist", "base_add", ["8", "10", "12", "15"][idx])
                add_effect(ally_group, "StaminaChangeOT", "perc_add", [".15", ".20", ".25", ".30"][idx])

            cooldown = ET.SubElement(definitions, "buff", {"name": f"buffPZAEC{set_name}T{tier}Cooldown",
                "name_key": "buffPZAECResonanceCooldownName", "description_key": "buffPZAECResonanceCooldownDesc",
                "icon": "ui_game_symbol_armor_iron", "icon_color": "180,180,180"})
            ET.SubElement(cooldown, "stack_type", {"value": "replace"})
            ET.SubElement(cooldown, "duration", {"value": str(data["cooldown"])})

        loc += [
            (f"buffPZAEC{set_name}ResonanceName", "buffs", f"{set_name} Resonance", f"{spec['cn']}共鸣"),
            (f"buffPZAEC{set_name}ResonanceDesc", "buffs", f"Three-piece charge for {spec['role']}; reaches 100 to arm the device.", f"三件套通过{spec['role']}充能；达到 100 后可启动装置。"),
            (f"buffPZAEC{set_name}ActiveName", "buffs", spec["active_en"], spec["active_cn"]),
            (f"buffPZAEC{set_name}ActiveDesc", "buffs", f"The {set_name} resonance ability is active.", f"{spec['cn']}共鸣能力正在生效。"),
        ]
    loc += [
        ("buffPZAECResonanceCooldownName", "buffs", "Resonance Overload", "共鸣过载"),
        ("buffPZAECResonanceCooldownDesc", "buffs", "The resonance device is cooling down.", "共鸣装置正在冷却。"),
    ]
    progression.arsenal_buffs(definitions)
    return element_text(check) + "\n" + element_text(definitions), loc


def build_recipes() -> str:
    append = ET.Element("append", {"xpath": "/recipes"})
    core_patches: list[str] = []
    core_names = ("Precision", "Breaker", "Barrage", "Skirmisher")
    for tier, data in TIERS.items():
        for core in core_names:
            patch = ET.Element("append", {"xpath": f"/recipes/recipe[@name='modPZAEC{core}{data['rank']}']"})
            chassis = "resourcePZAECArmorChassis" if core == "Skirmisher" else "resourcePZAECWeaponChassis"
            ET.SubElement(patch, "ingredient", {"name": chassis, "count": "1"})
            ET.SubElement(patch, "ingredient", {"name": f"resourcePZAECSiegeCapacitorT{tier}", "count": str(tier - 15)})
            ET.SubElement(patch, "ingredient", {"name": f"resourcePZAECMutantHeartT{tier}", "count": str([1, 1, 2, 3][tier - 16])})
            core_patches.append(element_text(patch))
    chassis = {
        "resourcePZAECWeaponChassis": [("resourceDurablAlloys", 100), ("resourceMechanicalParts", 40), ("resourceElectricParts", 30), ("resourceDuctTape", 10), ("resourceLegendaryParts", 5)],
        "resourcePZAECArmorChassis": [("resourceDurablAlloys", 60), ("resourceMechanicalParts", 20), ("resourceElectricParts", 20), ("resourceSewingKit", 20), ("resourceLeather", 40), ("resourceLegendaryParts", 5)],
        "resourcePZAECDeviceChassis": [("resourceDurablAlloys", 80), ("resourceMechanicalParts", 30), ("resourceElectricParts", 50), ("resourceScrapPolymers", 40), ("resourceAcid", 5), ("resourceLegendaryParts", 5)],
    }
    for output, ingredients in chassis.items():
        rec = ET.SubElement(append, "recipe", {"name": output, "count": "1", "craft_area": "workbench", "craft_time": "60", "always_unlocked": "true", "use_ingredient_modifier": "false"})
        for name, count in ingredients: ET.SubElement(rec, "ingredient", {"name": name, "count": str(count)})
    for set_name in SETS:
        for tier, data in TIERS.items():
            for slot in SLOTS:
                output = f"armorPZAEC{set_name}{slot}T{tier}"
                rec = ET.SubElement(append, "recipe", {"name": output, "count": "1", "craft_area": "workbench", "craft_time": str(75 + (tier - 16) * 15), "always_unlocked": "true", "use_ingredient_modifier": "false"})
                ingredients = [("resourcePZAECArmorChassis", 1), (f"PZAECBuildParts{data['rank']}", data["comp"]),
                    ("resourceLegendaryParts", data["legendary"]), (f"resourcePZAECSiegeCapacitorT{tier}", data["cap"]),
                    (f"resourcePZAECMutantHeartT{tier}", data["heart"]), ("resourceElectricParts", data["electric"])]
                if tier == 16:
                    ingredients.insert(0, (legendary.ARMOR[set_name] + slot, 1))
                for name, count in ingredients: ET.SubElement(rec, "ingredient", {"name": name, "count": str(count)})
                if tier > 16:
                    up = ET.SubElement(append, "recipe", {"name": output, "count": "1", "craft_area": "workbench", "craft_time": str(60 + (tier - 17) * 15), "always_unlocked": "true", "use_ingredient_modifier": "false", "tags": "upgrade"})
                    costs = [(f"armorPZAEC{set_name}{slot}T{tier-1}", 1), (f"PZAECBuildParts{data['rank']}", 3 if tier < 19 else 4),
                        ("resourceLegendaryParts", [6, 10, 16][tier - 17]), (f"resourcePZAECSiegeCapacitorT{tier}", [2, 3, 4][tier - 17]),
                        (f"resourcePZAECMutantHeartT{tier}", [1, 1, 2][tier - 17])]
                    for name, count in costs: ET.SubElement(up, "ingredient", {"name": name, "count": str(count)})
            output = f"itemPZAEC{set_name}DeviceT{tier}"
            rec = ET.SubElement(append, "recipe", {"name": output, "count": "1", "craft_area": "workbench", "craft_time": str(90 + (tier - 16) * 30), "always_unlocked": "true", "use_ingredient_modifier": "false"})
            costs = [("resourcePZAECDeviceChassis", 1), (f"PZAECBuildParts{data['rank']}", data["device_comp"]),
                ("resourceLegendaryParts", data["device_legendary"]), (f"resourcePZAECSiegeCapacitorT{tier}", data["device_cap"]),
                (f"resourcePZAECMutantHeartT{tier}", data["device_heart"])]
            for name, count in costs: ET.SubElement(rec, "ingredient", {"name": name, "count": str(count)})
            if tier > 16:
                up = ET.SubElement(append, "recipe", {"name": output, "count": "1", "craft_area": "workbench", "craft_time": str(90 + (tier - 17) * 30), "always_unlocked": "true", "use_ingredient_modifier": "false", "tags": "upgrade"})
                costs = [(f"itemPZAEC{set_name}DeviceT{tier-1}", 1), (f"PZAECBuildParts{data['rank']}", [4, 5, 6][tier - 17]),
                    ("resourceLegendaryParts", [10, 16, 24][tier - 17]), (f"resourcePZAECSiegeCapacitorT{tier}", [4, 5, 6][tier - 17]),
                    (f"resourcePZAECMutantHeartT{tier}", [1, 2, 2][tier - 17])]
                for name, count in costs: ET.SubElement(up, "ingredient", {"name": name, "count": str(count)})
    for tier, data in TIERS.items():
        if tier == 16:
            continue
        previous_rank = TIERS[tier - 1]["rank"]
        for core in core_names:
            up = ET.SubElement(append, "recipe", {"name": f"modPZAEC{core}{data['rank']}", "count": "1",
                "craft_area": "workbench", "craft_time": str(60 + (tier - 17) * 15), "always_unlocked": "true",
                "use_ingredient_modifier": "false", "tags": "upgrade"})
            costs = [(f"modPZAEC{core}{previous_rank}", 1), (f"PZAECBuildParts{data['rank']}", 4),
                ("resourceLegendaryParts", [6, 10, 16][tier - 17]),
                (f"resourcePZAECSiegeCapacitorT{tier}", [2, 3, 4][tier - 17]),
                (f"resourcePZAECMutantHeartT{tier}", [1, 1, 2][tier - 17])]
            for name, count in costs:
                ET.SubElement(up, "ingredient", {"name": name, "count": str(count)})
    return "\n".join(core_patches) + "\n" + element_text(append)


def build_loot() -> str:
    # Loot groups must exist before the first bundle which references them;
    # LootFromXml resolves references in final document order.
    groups = ET.Element("insertBefore", {"xpath": "/lootcontainers/lootgroup[@name='PZAECBossLootBundleT16_Content']"})
    for tier in TIERS:
        group = ET.SubElement(groups, "lootgroup", {"name": f"PZAECArmorDropT{tier}", "count": "1"})
        for set_name in SETS:
            for slot in SLOTS: ET.SubElement(group, "item", {"name": f"armorPZAEC{set_name}{slot}T{tier}", "count": "1"})
        bag_group = ET.SubElement(groups, "lootgroup", {"name": f"PZAECSiegeMaterialsT{tier}", "count": "all"})
        ET.SubElement(bag_group, "item", {"name": f"resourcePZAECSiegeCapacitorT{tier}", "count": "1,2" if tier >= 18 else "1"})
        ET.SubElement(bag_group, "item", {"name": "resourceForgedSteel", "count": str(10 + (tier - 16) * 5)})
    containers = ET.Element("append", {"xpath": "/lootcontainers"})
    for tier in TIERS:
        con = ET.SubElement(containers, "lootcontainer", {"name": f"PZAECSiegeLootT{tier}", "count": "1", "size": "4,2", "sound_open": "UseActions/open_backpack", "sound_close": "UseActions/close_backpack", "open_time": "1", "ignore_loot_abundance": "true", "unmodified_lootstage": "true"})
        ET.SubElement(con, "item", {"group": f"PZAECSiegeMaterialsT{tier}"})
    patches = []
    chances = {16: ".10", 17: ".15", 18: ".22", 19: ".30"}
    extras = {16: ".25", 17: ".40", 18: ".60", 19: ".80"}
    for tier in TIERS:
        ap = ET.Element("append", {"xpath": f"/lootcontainers/lootgroup[@name='PZAECBossLootBundleT{tier}_Content']"})
        ET.SubElement(ap, "item", {"name": f"resourcePZAECMutantHeartT{tier}", "count": "1", "prob": "1", "force_prob": "true"})
        ET.SubElement(ap, "item", {"name": f"resourcePZAECMutantHeartT{tier}", "count": "1", "prob": extras[tier], "force_prob": "true"})
        ET.SubElement(ap, "item", {"group": f"PZAECArmorDropT{tier}", "count": "1", "prob": chances[tier], "force_prob": "true"})
        patches.append(element_text(ap))
    return element_text(groups) + "\n" + element_text(containers) + "\n" + "\n".join(patches)


def build_entities() -> str:
    append = ET.Element("append", {"xpath": "/entity_classes"})
    for tier in TIERS:
        bag = ET.SubElement(append, "entity_class", {"name": f"PZAECSiegeLootBagT{tier}", "extends": "EntityLootContainerStrong"})
        ET.SubElement(bag, "property", {"name": "LootList", "value": f"PZAECSiegeLootT{tier}"})
    chunks = [element_text(append)]
    chances = {16: ".35", 17: ".45", 18: ".55", 19: ".70"}
    for tier in TIERS:
        for kind in ("heavy", "acid"):
            ap = ET.Element("append", {"xpath": f"/entity_classes/entity_class[@name='PZAECSiege_{kind}_T{tier}']"})
            ET.SubElement(ap, "property", {"name": "LootDropProb", "value": chances[tier]})
            ET.SubElement(ap, "property", {"name": "LootDropEntityClass", "value": f"PZAECSiegeLootBagT{tier}"})
            chunks.append(element_text(ap))
    return "\n".join(chunks)


def write_localization(rows: list[tuple[str, str, str, str]]) -> None:
    path = CONFIG / "Localization.csv"
    text = path.read_text(encoding="utf-8-sig")
    lines = text.splitlines()
    header = lines[0]
    generated_keys = {row[0] for row in rows}
    kept = [header] + [line for line in lines[1:] if line.split(",", 1)[0] not in generated_keys]
    output = io.StringIO()
    writer = csv.writer(output, lineterminator="\n")
    for key, file_name, en, cn in rows:
        writer.writerow([key, file_name, "EndgameArsenal", "", "", "", en, "", "", "", "", "", "", "", "", "", "", "", cn, cn])
    path.write_text("\n".join(kept).rstrip() + "\n" + output.getvalue(), encoding="utf-8", newline="\n")


def main() -> None:
    # Remove a historical patch artifact which makes items.xml invalid XML.
    items_path = CONFIG / "items.xml"
    items_text = items_path.read_text(encoding="utf-8-sig").replace("\n+    <item name=\"itemPZAECBossLootBundleT17\">", "\n    <item name=\"itemPZAECBossLootBundleT17\">")
    items_path.write_text(items_text, encoding="utf-8", newline="\n")
    item_xml, item_loc = build_items()
    buff_xml, buff_loc = build_buffs()
    replace_generated(items_path, item_xml)
    replace_generated(CONFIG / "buffs.xml", buff_xml)
    replace_generated(CONFIG / "recipes.xml", build_recipes())
    replace_generated(CONFIG / "loot.xml", build_loot())
    replace_generated(CONFIG / "entityclasses.xml", build_entities())
    write_localization(item_loc + buff_loc)
    from generate_fusion_upgrades import refresh
    refresh(CONFIG)


if __name__ == "__main__":
    main()

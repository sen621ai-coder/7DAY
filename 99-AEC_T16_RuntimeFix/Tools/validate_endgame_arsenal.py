from __future__ import annotations

import csv
import pathlib
import xml.etree.ElementTree as ET


MOD = pathlib.Path(__file__).resolve().parents[1]
CONFIG = MOD / "Config"
GAME = MOD.parents[1]
TIERS = range(16, 20)
SETS = ("Harrier", "Storm", "Tremor", "Warden")
SLOTS = ("Helmet", "Outfit", "Gloves", "Boots")


def parse(name: str) -> ET.Element:
    return ET.parse(CONFIG / name).getroot()


def defined_items(root: ET.Element) -> dict[str, ET.Element]:
    result = {}
    for append in root.findall(".//append[@xpath='/items']"):
        for item in append.findall("item"):
            result[item.get("name")] = item
    return result


def defined_buffs(root: ET.Element) -> dict[str, ET.Element]:
    result = {}
    for append in root.findall(".//append[@xpath='/buffs']"):
        for buff in append.findall("buff"):
            result[buff.get("name")] = buff
    return result


def main() -> None:
    for path in CONFIG.glob("*.xml"):
        ET.parse(path)
    items_root = parse("items.xml")
    buffs_root = parse("buffs.xml")
    recipes_root = parse("recipes.xml")
    modifiers_root = parse("item_modifiers.xml")
    loot_root = parse("loot.xml")
    entities_root = parse("entityclasses.xml")
    items = defined_items(items_root)
    buffs = defined_buffs(buffs_root)
    vanilla_items = {item.get("name") for item in ET.parse(GAME / "Data" / "Config" / "items.xml").getroot().findall("item")}
    all_known_items = vanilla_items | set(items)
    modifiers = {m.get("name") for a in modifiers_root.findall(".//append[@xpath='/item_modifiers']") for m in a.findall("item_modifier")}

    expected_armor = {
        f"armorPZAEC{set_name}{slot}T{tier}"
        for set_name in SETS for slot in SLOTS for tier in TIERS
    }
    expected_devices = {
        f"itemPZAEC{set_name}DeviceT{tier}"
        for set_name in SETS for tier in TIERS
    }
    assert expected_armor <= items.keys(), expected_armor - items.keys()
    assert expected_devices <= items.keys(), expected_devices - items.keys()
    assert len(expected_armor) == 64 and len(expected_devices) == 16

    slot_names = {"Helmet": "Head", "Outfit": "Chest", "Gloves": "Hands", "Boots": "Feet"}
    for name in expected_armor:
        slot = next(slot for slot in SLOTS if slot in name)
        element = items[name]
        props = {p.get("name"): p.get("value") for p in element.findall("property") if p.get("name")}
        assert props.get("EquipSlot") == slot_names[slot], (name, props.get("EquipSlot"))
        assert props.get("ArmorGroup", "").startswith("groupPZAEC")
        assert props.get("PZAECAdvancedLoot") == "true"
        assert element.find("property[@class='SDCS']") is not None, name

    expected_buffs = set()
    for set_name in SETS:
        for tier in TIERS:
            expected_buffs.update({
                f"buffPZAEC{set_name}T{tier}Set2",
                f"buffPZAEC{set_name}T{tier}Set3",
                f"buffPZAEC{set_name}T{tier}Set4",
                f"buffPZAEC{set_name}T{tier}Ready",
                f"buffPZAEC{set_name}T{tier}Active",
                f"buffPZAEC{set_name}T{tier}Cooldown",
            })
            if set_name == "Warden":
                expected_buffs.add(f"buffPZAECWardenT{tier}Aura")
    assert expected_buffs <= buffs.keys(), expected_buffs - buffs.keys()

    recipes = [r.get("name") for a in recipes_root.findall(".//append[@xpath='/recipes']") for r in a.findall("recipe")]
    for name in expected_armor | expected_devices:
        tier = int(name[-2:])
        assert recipes.count(name) == (1 if tier == 16 else 2), (name, recipes.count(name))
    for name in ("resourcePZAECWeaponChassis", "resourcePZAECArmorChassis", "resourcePZAECDeviceChassis"):
        assert recipes.count(name) == 1
    ranks = {16: "R2", 17: "R3", 18: "R4", 19: "R5"}
    for tier, rank in ranks.items():
        for core in ("Precision", "Breaker", "Barrage", "Skirmisher"):
            name = f"modPZAEC{core}{rank}"
            assert name in modifiers
            assert recipes.count(name) == (1 if tier == 16 else 2), (name, recipes.count(name))
    assert sum(1 for a in recipes_root.findall(".//append") if a.get("xpath", "").startswith("/recipes/recipe[@name='modPZAEC")) == 16
    for append in recipes_root.findall(".//append[@xpath='/recipes']"):
        for recipe in append.findall("recipe"):
            if recipe.get("name") not in expected_armor | expected_devices | {
                "resourcePZAECWeaponChassis", "resourcePZAECArmorChassis", "resourcePZAECDeviceChassis"
            }:
                continue
            assert recipe.get("name") in all_known_items, recipe.get("name")
            for ingredient in recipe.findall("ingredient"):
                assert ingredient.get("name") in all_known_items, (recipe.get("name"), ingredient.get("name"))

    loot_groups = {g.get("name") for g in loot_root.findall(".//lootgroup")}
    loot_containers = {c.get("name") for c in loot_root.findall(".//lootcontainer")}
    entity_classes = {e.get("name") for e in entities_root.findall(".//entity_class")}
    for tier in TIERS:
        assert f"PZAECArmorDropT{tier}" in loot_groups
        assert f"PZAECSiegeMaterialsT{tier}" in loot_groups
        assert f"PZAECSiegeLootT{tier}" in loot_containers
        assert f"PZAECSiegeLootBagT{tier}" in entity_classes

    for element in list(items_root.iter()) + list(buffs_root.iter()):
        buff_name = element.get("buff")
        if buff_name and buff_name.startswith("buffPZAEC"):
            assert buff_name in buffs, buff_name

    with (CONFIG / "Localization.csv").open(encoding="utf-8-sig", newline="") as stream:
        rows = list(csv.reader(stream))
    assert all(len(row) == 20 for row in rows), {len(row) for row in rows}
    keys = [row[0] for row in rows if row]
    assert len(keys) == len(set(keys)), "Duplicate localization key"
    for name in expected_armor | expected_devices:
        assert name in keys, name
    key_set = set(keys)
    for item in [items[name] for name in expected_armor | expected_devices]:
        desc = item.find("property[@name='DescriptionKey']")
        assert desc is not None and desc.get("value") in key_set, item.get("name")
    for buff in buffs.values():
        if not buff.get("name", "").startswith("buffPZAEC"):
            continue
        for attr in ("name_key", "description_key"):
            if buff.get(attr):
                assert buff.get(attr) in key_set, (buff.get("name"), buff.get(attr))

    for file_name in ("items.xml", "buffs.xml", "recipes.xml", "loot.xml", "entityclasses.xml"):
        text = (CONFIG / file_name).read_text(encoding="utf-8-sig")
        assert text.count("BEGIN GENERATED ENDGAME ARSENAL") == 1, file_name
        assert text.count("END GENERATED ENDGAME ARSENAL") == 1, file_name
        assert not any(line.startswith("+") for line in text.splitlines()), file_name

    print("Endgame arsenal validation passed: 64 armor pieces, 16 devices, 4 tiered loot loops.")


if __name__ == "__main__":
    main()

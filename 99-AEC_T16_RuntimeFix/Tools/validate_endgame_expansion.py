from __future__ import annotations

import csv
import pathlib
import xml.etree.ElementTree as ET


MOD = pathlib.Path(__file__).resolve().parents[1]
MODS = MOD.parent
GAME_CONFIG = MODS.parent / "Data" / "Config"
CONFIG = MOD / "Config"
TIERS = range(16, 20)
WEAPONS = ("EmberPistol", "HorizonNeedle", "StormReservoir", "FaultlineHammer", "BastionShotgun", "EchoRepeater", "CounterSiege")
COMPONENTS = ("ThreatLens", "CoolingSink", "KineticRecycler", "RepairServo", "PhaseRangefinder", "NearfieldReflex", "ClosedLoopFeed", "PulseCapacitor", "StanceBreaker", "GatekeeperHook", "EmergencyLiner", "InsulatedTreads")
DEVICES = ("SkyguardArray", "HiveRepairStation", "HoundDecoyTower", "ShockNetNode", "ArmorBreakTurret")
FORTRESS = ("ReactiveWall", "AblativeWall", "Embrasure", "BlastGate", "ArmoredConduit")
WEAPON_BASES = {
    "EmberPistol": "gunHandgunT3DesertVulture",
    "HorizonNeedle": "gunRifleT3SniperRifle", "StormReservoir": "gunMGT3M60",
    "FaultlineHammer": "meleeWpnSledgeT3SteelSledgehammer", "BastionShotgun": "gunShotgunT3AutoShotgun",
    "EchoRepeater": "gunBowT3CompoundCrossbow", "CounterSiege": "gunExplosivesT3RocketLauncher",
}


def parse(path: pathlib.Path) -> ET.Element:
    return ET.parse(path).getroot()


def collect(paths: list[pathlib.Path], tag: str) -> set[str]:
    result: set[str] = set()
    for path in paths:
        try:
            for element in parse(path).iter(tag):
                if element.get("name"):
                    result.add(element.get("name"))
        except (ET.ParseError, OSError):
            pass
    return result


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def main() -> None:
    for path in CONFIG.glob("*.xml"):
        parse(path)

    items_root = parse(CONFIG / "items.xml")
    modifiers_root = parse(CONFIG / "item_modifiers.xml")
    blocks_root = parse(CONFIG / "blocks.xml")
    fortress_names = {f"PZAEC{stem}T{tier}" for stem in FORTRESS + ("AblativeWallRuin",) for tier in TIERS}
    fortress_names.update(("PZAECResonanceForge", "PZAECTacticalRelay"))
    for block in blocks_root.iter("block"):
        if block.get("name") in fortress_names:
            icon = block.find("property[@name='CustomIcon']")
            require(icon is not None, f"Missing fortress icon: {block.get('name')}")
            require((GAME_CONFIG.parent / "ItemIcons" / (icon.get("value") + ".png")).is_file(),
                    f"Missing fortress icon asset: {block.get('name')} -> {icon.get('value')}")
    buffs_root = parse(CONFIG / "buffs.xml")
    recipes_root = parse(CONFIG / "recipes.xml")
    loot_root = parse(CONFIG / "loot.xml")
    entities_root = parse(CONFIG / "entityclasses.xml")
    groups_root = parse(CONFIG / "entitygroups.xml")

    item_paths = [GAME_CONFIG / "items.xml"] + list(MODS.glob("*/Config/items.xml"))
    modifier_paths = [GAME_CONFIG / "item_modifiers.xml"] + list(MODS.glob("*/Config/item_modifiers.xml"))
    block_paths = [GAME_CONFIG / "blocks.xml"] + list(MODS.glob("*/Config/blocks.xml"))
    buff_paths = [GAME_CONFIG / "buffs.xml"] + list(MODS.glob("*/Config/buffs.xml"))
    entity_paths = [GAME_CONFIG / "entityclasses.xml"] + list(MODS.glob("*/Config/entityclasses.xml"))
    known_items = collect(item_paths, "item")
    known_modifiers = collect(modifier_paths, "item_modifier")
    known_blocks = collect(block_paths, "block")
    # Shape families expand into individual blocks at load time; their source
    # names are not concrete Extends parents (e.g. steelShapes).
    shape_families = {e.get("name") for path in block_paths for e in parse(path).iter("block") if e.get("shapes")}
    known_blocks |= {name + ":VariantHelper" for name in shape_families}
    known_buffs = collect(buff_paths, "buff")
    known_entities = collect(entity_paths, "entity_class")
    craftables = known_items | known_modifiers | known_blocks

    expected_items = set()
    for tier in TIERS:
        for stem in WEAPONS:
            expected_items.add(("melee" if stem == "FaultlineHammer" else "gun") + f"PZAEC{stem}T{tier}")
        expected_items |= {f"ammoPZAECSkyguardInterceptorT{tier}", f"ammoPZAECCounterPulseT{tier}", f"thrownPZAECCounterJammerT{tier}", f"itemPZAECFieldRepairKitT{tier}"}
        expected_items |= {
            f"resourcePZAECCoreFragmentT{tier}", f"resourcePZAECCapacitorFragmentT{tier}",
            f"itemPZAECArmoryBlueprintCrateT{tier}", f"itemPZAECComponentChoiceCrateT{tier}",
            f"resourcePZAECRepairChargeT{tier}", f"resourcePZAECDecoyChargeT{tier}",
            f"itemPZAECDefenseBlueprintCrateT{tier}",
        }
    expected_items |= {"itemPZAECQuickArmorGel", "itemPZAECResonanceInjector", "itemPZAECDecoyBeacon", "itemPZAECEvacAnchor", "resourcePZAECDefenseChassis"}

    expected_modifiers = {f"modPZAEC{stem}T{tier}" for stem in COMPONENTS for tier in TIERS}
    expected_modifiers |= {f"modPZAEC{set_name}{mode}T19" for set_name in ("Harrier", "Storm", "Tremor", "Warden") for mode in ("Stable", "Overload")}
    expected_blocks = {f"PZAEC{stem}T{tier}" for stem in DEVICES + FORTRESS for tier in TIERS}
    expected_blocks |= {"PZAECResonanceForge", "PZAECTacticalRelay"}
    expected_entities = {f"PZAECSiege{stem}T{tier}" for stem in ("Disruptor", "Spotter", "Linesman") for tier in TIERS}
    expected_calibration_buffs = {f"buffPZAEC{set_name}{mode}CalibrationT19" for set_name in ("Harrier", "Storm", "Tremor", "Warden") for mode in ("Stable", "Overload")}

    actual_items = {e.get("name") for e in items_root.iter("item")}
    actual_modifiers = {e.get("name") for e in modifiers_root.iter("item_modifier")}
    actual_blocks = {e.get("name") for e in blocks_root.iter("block")}
    actual_entities = {e.get("name") for e in entities_root.iter("entity_class")}
    require(expected_items <= actual_items, f"Missing expansion items: {sorted(expected_items - actual_items)}")
    require(expected_modifiers <= actual_modifiers, f"Missing expansion modifiers: {sorted(expected_modifiers - actual_modifiers)}")
    require(expected_blocks <= actual_blocks, f"Missing expansion blocks: {sorted(expected_blocks - actual_blocks)}")
    require(expected_entities <= actual_entities, f"Missing expansion entities: {sorted(expected_entities - actual_entities)}")
    require(len(expected_items) == 77, f"Expected 77 item definitions, got {len(expected_items)}")
    require(len(expected_modifiers) == 56, f"Expected 56 modifier definitions, got {len(expected_modifiers)}")
    require(len(expected_blocks) == 42, f"Expected 42 player-facing block definitions, got {len(expected_blocks)}")
    require(len(expected_items | expected_modifiers | expected_blocks) == 175, "Catalog total must remain 175")
    require(len(expected_entities) == 12, "Expected 12 siege support entity definitions")
    require(expected_calibration_buffs <= known_buffs, f"Missing calibration buffs: {sorted(expected_calibration_buffs - known_buffs)}")

    for element in items_root.iter("item"):
        name = element.get("name") or ""
        if name not in expected_items:
            continue
        extends = element.find("property[@name='Extends']")
        require(extends is None or extends.get("value") in known_items, f"{name} extends missing item {extends.get('value')}")
        icon = element.find("property[@name='CustomIcon']")
        require(icon is None or icon.get("value") in craftables, f"{name} uses missing custom icon {icon.get('value')}")
        for stem, base in WEAPON_BASES.items():
            if stem not in name:
                continue
            base_node = parse(GAME_CONFIG / "items.xml").find(f"item[@name='{base}']/property[@name='Tags']")
            tags_node = element.find("property[@name='Tags']")
            base_tags = set(base_node.get("value").split(","))
            actual_tags = set(tags_node.get("value").split(",")) if tags_node is not None else set()
            require(base_tags <= actual_tags, f"{name} lost inherited weapon tags: {sorted(base_tags - actual_tags)}")
    for element in modifiers_root.iter("item_modifier"):
        name = element.get("name") or ""
        if name not in expected_modifiers:
            continue
        extends = element.find("property[@name='Extends']")
        require(extends is None or extends.get("value") in known_modifiers, f"{name} extends missing modifier {extends.get('value')}")
        icon = element.find("property[@name='CustomIcon']")
        require(icon is None or icon.get("value") in craftables, f"{name} uses missing custom icon {icon.get('value')}")
    for element in blocks_root.iter("block"):
        name = element.get("name") or ""
        if name not in expected_blocks and not name.startswith("PZAECAblativeWallRuinT"):
            continue
        extends = element.find("property[@name='Extends']")
        require(extends is None or extends.get("value") in known_blocks, f"{name} extends missing block {extends.get('value')}")
        require(extends is None or extends.get("value") not in shape_families, f"{name} extends shape family instead of concrete block")
        if name.startswith("PZAECEmbrasureT"):
            require(element.find("property[@name='Model']").get("value") == "@:Shapes/arrow_slit.fbx", f"{name} must have an open firing slit")
    for element in entities_root.iter("entity_class"):
        if element.get("name") in expected_entities:
            require(element.get("extends") in known_entities, f"{element.get('name')} extends missing entity {element.get('extends')}")

    recipe_names: list[str] = []
    for recipe in recipes_root.iter("recipe"):
        output = recipe.get("name")
        if output not in expected_items | expected_modifiers | expected_blocks:
            continue
        recipe_names.append(output)
        require(output in craftables, f"Recipe output does not exist: {output}")
        for ingredient in recipe.findall("ingredient"):
            require(ingredient.get("name") in craftables, f"{output} uses missing ingredient {ingredient.get('name')}")
            require(ingredient.get("name") not in shape_families, f"{output} uses shape family instead of its craftable VariantHelper")
            require(int(ingredient.get("count", "0")) > 0, f"{output} has a non-positive ingredient count")
    for output in expected_items | expected_modifiers | expected_blocks:
        if "BlueprintCrate" in output or "ChoiceCrate" in output or output.startswith("itemPZAECArmoryBlueprint"):
            continue
        require(output in recipe_names, f"No recipe for {output}")

    for node in list(items_root.iter("triggered_effect")) + list(modifiers_root.iter("triggered_effect")):
        buff = node.get("buff")
        if buff and buff.startswith("buffPZAEC"):
            require(buff in known_buffs, f"Missing referenced buff {buff}")
    for block in blocks_root.iter("block"):
        buff = block.find("property[@name='Buff']")
        if buff is not None and block.get("name") in expected_blocks:
            require(buff.get("value") in known_buffs, f"{block.get('name')} uses missing buff {buff.get('value')}")

    for item in loot_root.iter("item"):
        name = item.get("name")
        if name and name.startswith(("gunPZAEC", "meleePZAEC", "modPZAEC", "itemPZAEC", "resourcePZAEC", "thrownPZAEC", "ammoPZAEC")):
            require(name in craftables, f"Loot references missing item {name}")
    for container in loot_root.iter("lootcontainer"):
        require(container.get("count") != "all", f"lootcontainer {container.get('name')} cannot use count=all")

    group_entries = {e.get("n") for e in groups_root.iter("e")}
    require(expected_entities <= group_entries, f"Siege support entities missing from Blood Moon groups: {sorted(expected_entities - group_entries)}")

    with (CONFIG / "Localization.csv").open(encoding="utf-8-sig", newline="") as handle:
        localization_rows = [row for row in csv.reader(handle) if row]
        localization = {row[0] for row in localization_rows}
    for name in expected_items | expected_modifiers | expected_blocks:
        require(name in localization, f"Missing localization name {name}")
        require(name + "Desc" in localization, f"Missing localization description {name}")
    for name in expected_entities:
        require(sum(1 for row in localization_rows if row[0] == name) == 1, f"Missing or duplicate siege entity localization {name}")

    print("Endgame expansion validation passed: 77 items, 56 modifiers, 42 blocks, 12 siege entities, 175 player-facing definitions.")


if __name__ == "__main__":
    main()

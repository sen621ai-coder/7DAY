"""Generate an admin-only, deterministic, nested pack of the endgame catalog.

Native OpenBundle treats Create_item_count as QUALITY for quality-bearing items.
Repeat those entries to give two separate Q6 items; do not use count=2.
"""
from __future__ import annotations

import csv
import io
import json
import pathlib
import re
import xml.etree.ElementTree as ET
from collections import defaultdict

MOD = pathlib.Path(__file__).resolve().parents[1]
CONFIG = MOD / "Config"
GAME = MOD.parent.parent
BEGIN = "<!-- BEGIN GENERATED AEC TEST BUNDLES -->"
END = "<!-- END GENERATED AEC TEST BUNDLES -->"
MASTER = "itemPZAECTestBundleAll"


def catalog():
    selected, kinds = set(), {}
    for filename, tag in (("items.xml", "item"), ("item_modifiers.xml", "item_modifier"), ("blocks.xml", "block")):
        raw = (CONFIG / filename).read_text(encoding="utf-8-sig")
        for body in re.findall(r"<!-- BEGIN GENERATED ENDGAME (?:ARSENAL|EXPANSION) -->(.*?)<!-- END GENERATED ENDGAME (?:ARSENAL|EXPANSION) -->", raw, re.S):
            for node in ET.fromstring("<root>" + body + "</root>").iter(tag):
                name = node.get("name")
                if name and not name.startswith("PZAECAblativeWallRuin"):
                    selected.add(name)
                    kinds[name] = tag
    assert len(selected) == 250, f"Catalog changed: {len(selected)}; review coverage"
    return selected, kinds


def definitions():
    result = {}
    for filename, tag in (("items.xml", "item"), ("item_modifiers.xml", "item_modifier"), ("blocks.xml", "block")):
        paths = [GAME / "Data/Config" / filename, *sorted(MOD.parent.glob("*/Config/" + filename))]
        for path in paths:
            for node in ET.parse(path).getroot().iter(tag):
                if node.get("name"):
                    result[node.get("name")] = node
    return result


def inherited(defs, name, prop, action=None, seen=()):
    assert name not in seen, name
    node = defs[name]
    query = f"property[@name='{prop}']"
    if action:
        query = f"property[@class='{action}']/" + query
    value = node.find(query)
    if value is not None:
        return value.get("value")
    parent = node.find("property[@name='Extends']")
    return inherited(defs, parent.get("value"), prop, action, (*seen, name)) if parent is not None else None


def main():
    selected, kinds = catalog()
    defs = definitions()
    support = {f"modPZAEC{stem}R{rank}" for stem in ("Precision", "Breaker", "Barrage", "Skirmisher") for rank in range(2, 6)}
    support |= {f"PZAECBuildPartsR{rank}" for rank in range(2, 6)}
    # Supply the actual compatible ammunition, including native alternatives.
    ammo = set()
    for name in selected:
        if name.startswith("gun"):
            ammo.update((inherited(defs, name, "Magazine_items", "Action0") or "").split(","))
    ammo.discard("")
    extras = {"resourceRepairKit", "meleeToolWireTool", "generatorbank", "carBattery", "smallEngine", "ammoGasCan"}
    all_names = selected | support | ammo | extras
    assert all_names <= defs.keys(), sorted(all_names - defs.keys())
    groups = defaultdict(list)
    for name in sorted(all_names):
        match = re.search(r"T(1[6-9])$", name)
        tier = match.group(1) if match else "Common"
        if name.startswith(("gun", "melee")) and name in selected:
            category = "Weapons"
        elif name.startswith("armor"):
            category = "Armor"
        elif name.startswith("mod"):
            category = "Mods"
        elif kinds.get(name) == "block":
            category = "Buildings"
        elif name.startswith("ammo"):
            category = "Ammo"
        else:
            category = "Supplies"
        quality = category in ("Weapons", "Armor") or name == "carBattery"
        desired = 6 if quality else (200 if category == "Ammo" else 20 if category == "Buildings" else 1 if category == "Mods" else 50)
        stack = int(inherited(defs, name, "Stacknumber") or "1")
        count = desired if quality else min(desired, stack)
        groups[tier, category].extend([(name, count)] * (2 if category in ("Weapons", "Armor") else 1))

    bundles, labels = {}, {}
    cn = {"Weapons": "武器双份包（6品质）", "Armor": "护甲双份包（6品质）", "Mods": "组件与芯片包", "Buildings": "建筑设备包", "Ammo": "弹药包", "Supplies": "材料与补给包"}
    branches = defaultdict(list)
    for (tier, category), entries in sorted(groups.items()):
        for page, start in enumerate(range(0, len(entries), 16), 1):
            key = f"itemPZAECTestBundle{tier}{category}{page}"
            bundles[key] = entries[start:start + 16]
            labels[key] = f"测试·{'通用' if tier == 'Common' else 'T' + tier} {cn[category]} {page}"
            branches[tier].append((key, 1))
    for tier, entries in sorted(branches.items()):
        key = f"itemPZAECTestBundle{tier}"
        bundles[key] = entries
        labels[key] = f"测试·{'通用补给' if tier == 'Common' else 'T' + tier + ' 全物品'}分包"
    bundles[MASTER] = [(f"itemPZAECTestBundle{tier}", 1) for tier in ("16", "17", "18", "19", "Common")]
    labels[MASTER] = "[66FFCC]T16–T19 全新物品测试总包[-]"
    assert max(map(len, bundles.values())) <= 16

    root = ET.Element("append", xpath="/items")
    rows = []
    for key, entries in bundles.items():
        node = ET.SubElement(root, "item", name=key)
        for prop, value in {"Extends": "questRewardBundleMaster", "CreativeMode": "Dev", "CustomIcon": "bundleRifle", "CustomIconTint": "66FFCC", "ItemTypeIcon": "bundle", "Stacknumber": "1", "DescriptionKey": key + "Desc", "SellableToTrader": "false"}.items():
            ET.SubElement(node, "property", name=prop, value=value)
        action = ET.SubElement(node, "property", {"class": "Action0"})
        for prop, value in {"Class": "OpenBundle", "Delay": "0", "Consume": "true", "Create_item": ",".join(n for n, _ in entries), "Create_item_count": ",".join(str(c) for _, c in entries)}.items():
            ET.SubElement(action, "property", name=prop, value=value)
        description = "测试专用，打开后消耗包裹。总包按阶级与类别分包，包含全部250种新增物品及关联核心、弹药与供电补给。武器、护甲每种两件，固定6品质、未融合。打开分类包前预留16个空格；背包满时物品会掉在脚下，请及时拾取。"
        rows.extend([(key, labels[key]), (key + "Desc", description)])
    ET.indent(root, space="  ")
    body = BEGIN + "\n" + ET.tostring(root, encoding="unicode") + "\n" + END
    path = CONFIG / "items.xml"
    text = path.read_text(encoding="utf-8-sig")
    if BEGIN in text:
        text = text[:text.index(BEGIN)] + body + text[text.index(END) + len(END):]
    else:
        text = text.replace("</configs>", body + "\n</configs>")
    path.write_text(text, encoding="utf-8", newline="\n")
    path = CONFIG / "Localization.csv"
    with path.open(encoding="utf-8-sig", newline="") as stream:
        old = list(csv.reader(stream))
    kept = [row for row in old if len(row) < 3 or row[2] != "AECTestBundles"]
    output = io.StringIO()
    writer = csv.writer(output, lineterminator="\n")
    writer.writerows(kept)
    for key, value in rows:
        row = [""] * len(old[0])
        for field, content in {"Key": key, "File": "items", "Type": "AECTestBundles", "english": value, "schinese": value, "tchinese": value}.items():
            row[old[0].index(field)] = content
        writer.writerow(row)
    path.write_text(output.getvalue(), encoding="utf-8", newline="\n")
    manifest = {"master": MASTER, "new_items": sorted(selected), "support_items": sorted(all_names - selected), "bundles": bundles}
    (MOD / "Tools/test_bundle_manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    (MOD / "测试总包说明.md").write_text("# 全新物品测试总包\n\n重启游戏后，按 F1 输入：\n\n```text\ngiveself " + MASTER + " 1 1 true false\n```\n\n打开总包获得 T16、T17、T18、T19、通用补给五个分包，再打开需要的分类包。覆盖全部250种新增物品，另附" + str(len(all_names - selected)) + "种关联核心、弹药与补给。武器和护甲每种两件6品质，便于测试融合。每个包最多输出16条物品，打开前预留16个空格，溢出掉落请及时拾取。\n\n测试包仅通过命令或开发者物品栏取得，没有制作配方或正常掉落；不改变逐级升级规则。补给用完可以重复获取总包。\n\n维护：更新新增物品后运行 `python 99-AEC_T16_RuntimeFix/Tools/generate_test_bundle.py`。\n", encoding="utf-8")
    print(f"Generated {len(bundles)} bundles: {len(selected)} new + {len(all_names - selected)} supporting items; maximum 16 entries per opening")


if __name__ == "__main__":
    main()

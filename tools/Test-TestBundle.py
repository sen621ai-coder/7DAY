"""Check bundle reachability, full coverage, quality encoding and inventory size."""
import collections
import csv
import pathlib
import sys
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[1]
MOD = ROOT / "99-AEC_T16_RuntimeFix"
sys.path.insert(0, str(MOD / "Tools"))
import generate_test_bundle as generator

selected, kinds = generator.catalog()
defs = generator.definitions()
items = ET.parse(MOD / "Config/items.xml").getroot()
bundles = {e.get("name"): e for e in items.iter("item") if e.get("name", "").startswith("itemPZAECTestBundle")}
visited, outputs = set(), collections.defaultdict(list)

def walk(name, parents=()):
    assert name not in parents, f"Cycle: {parents} -> {name}"
    visited.add(name)
    node = bundles[name]
    action = {p.get("name"): p.get("value") for p in node.findall("property[@class='Action0']/property")}
    assert action["Class"] == "OpenBundle" and action["Consume"] == "true"
    assert "Random_item" not in action
    names, counts = action["Create_item"].split(","), action["Create_item_count"].split(",")
    assert 0 < len(names) == len(counts) <= 16, name
    for target, count in zip(names, counts):
        assert target in defs and int(count) > 0, target
        if target in bundles:
            assert count == "1"
            walk(target, (*parents, name))
        else:
            outputs[target].append(int(count))

walk(generator.MASTER)
retired = {f'itemPZAECTestBundle{tier}Devices1' for tier in range(16, 20)}
assert visited == bundles.keys() - retired, "Unexpected unreachable bundle"
for name in retired:
    node = bundles[name]
    assert node.find("property[@name='CreativeMode']").get('value') == 'None'
    action = {p.get('name'): p.get('value') for p in node.findall("property[@class='Action0']/property")}
    assert action['Create_item'] == 'resourcePZAECDeviceChassis' and action['Create_item_count'] == '4'
assert selected <= outputs.keys(), sorted(selected - outputs.keys())
gear = {n for n in selected if n.startswith(("gun", "melee", "armor"))}
assert len(gear) == 92
for name in gear:
    assert outputs[name] == [6, 6], (name, outputs[name])
for name in selected - gear:
    assert len(outputs[name]) == 1, name
for name, counts in outputs.items():
    if name in gear or name == "carBattery":
        continue
    limit = int(generator.inherited(defs, name, "Stacknumber") or "1")
    assert all(c <= limit for c in counts), (name, counts, limit)
for name in gear:
    if name.startswith("gun"):
        ammo = generator.inherited(defs, name, "Magazine_items", "Action0")
        assert ammo and set(ammo.split(",")) <= outputs.keys(), name
with (MOD / "Config/Localization.csv").open(encoding="utf-8-sig", newline="") as stream:
    rows = list(csv.DictReader(stream))
for name in bundles:
    for key in (name, name + "Desc"):
        matches = [r for r in rows if r["Key"] == key]
        assert len(matches) == 1 and matches[0]["schinese"], key
for filename in ("recipes.xml", "loot.xml", "traders.xml"):
    path = MOD / "Config" / filename
    if path.exists():
        assert "itemPZAECTestBundle" not in path.read_text(encoding="utf-8-sig"), filename
print(f"PASS: {len(visited)} reachable bundles, 4 hidden legacy refunds; all {len(selected)} new and {len(outputs) - len(selected)} supporting types; 92 Q6 pairs; ammo coverage; <=16 slots; localized; admin-only.")

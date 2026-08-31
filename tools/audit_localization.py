#!/usr/bin/env python3
import csv
import glob
import json
import os
import re


ALLOWED = {
    "AEC", "Project", "Discord", "Boss", "POI", "HUD", "NPC", "PZ", "GS",
    "M60", "ANTIRAD", "RadProtect", "TNT", "SMG", "SUV", "LSS", "Google",
    "Defender", "Bulldog", "Zinger", "Combistick", "Gaus", "Flugen", "Predator",
    "Sonny", "URANUS", "Magnum", "LED", "ATM", "TFP", "USA", "CNR", "CTR",
    "CRT", "Dukes", "JSON", "Mini", "Rocket", "Working", "Stiff", "Power",
    "Pass", "Gas", "Pop", "Pills", "Savage", "Country", "Eraser", "Driftjack",
    "Hellglide", "Nitrojack", "Trailhunter", "Iron", "Bastion", "Mo", "Classic",
    "Service", "Truck", "Ah", "mAh", "XP", "HP", "AP", "DPS", "AOE", "DU",
    "RPG", "AK", "MP5", "III", "URL", "UI", "FAQ", "ID",
}


def clean(value):
    value = value.replace("\\n", " ")
    value = re.sub(r"https?://\S+", "", value)
    value = re.sub(r"\{[^{}]*\}|\$\([^)]*\)|%[A-Za-z]", "", value)
    value = re.sub(r"\[action:[^]]+\]", "", value)
    value = re.sub(r"\[[0-9A-Fa-f]{6}\]|\[-\]", "", value)
    return value


def unexpected(value):
    return sorted({w for w in re.findall(r"[A-Za-z]{3,}", clean(value)) if w not in ALLOWED})


def rows_for(path):
    with open(path, encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle)
        for row in reader:
            english_key = "English" if "English" in row else "english"
            chinese_key = "Schinese" if "Schinese" in row else "schinese"
            yield row.get("Key", ""), row.get(english_key, ""), row.get(chinese_key, "")


def main():
    files = sorted(glob.glob("*/Config/Localization.csv"))
    effective = {}
    widths_bad = []
    duplicates = []
    for path in files:
        with open(path, encoding="utf-8-sig", newline="") as handle:
            raw = list(csv.reader(handle))
        width = len(raw[0])
        widths_bad.extend((path, i + 1, len(row), width) for i, row in enumerate(raw[1:], 2) if len(row) != width)
        seen = set()
        for key, english, chinese in rows_for(path):
            if key in seen:
                duplicates.append((path, key))
            seen.add(key)
            effective[key] = (path, english, chinese)

    by_file = {}
    for key, (path, english, chinese) in effective.items():
        words = unexpected(chinese)
        if words:
            by_file.setdefault(path, []).append((key, english, chinese, words))

    print("CSV_WIDTH_ERRORS", len(widths_bad))
    print("DUPLICATE_KEYS", len(duplicates))
    for path, rows in by_file.items():
        print("UNEXPECTED", path, len(rows))
        for key, english, chinese, words in rows[:80]:
            print(" ", key, "|", ",".join(words), "|", chinese.replace("\n", "\\n"))

    for path in sorted(glob.glob("*/*Localization.json")):
        try:
            with open(path, encoding="utf-8-sig") as handle:
                json.load(handle)
            print("JSON_OK", path)
        except Exception as exc:
            print("JSON_ERROR", path, repr(exc))


if __name__ == "__main__":
    main()

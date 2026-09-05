"""Remove retired skip-tier recipes; generators now emit adjacent upgrades only."""
from pathlib import Path
import re

START = '<!-- BEGIN GENERATED FUSION UPGRADES -->'
END = '<!-- END GENERATED FUSION UPGRADES -->'
GEAR = re.compile(r'^(armorPZAEC(Harrier|Storm|Tremor|Warden)(Helmet|Outfit|Gloves|Boots)|gunPZAEC(EmberPistol|HorizonNeedle|StormReservoir|BastionShotgun|EchoRepeater|CounterSiege)|meleePZAECFaultlineHammer)T1[6-9]$')

def refresh(config):
    path = Path(config) / 'recipes.xml'
    text = path.read_text(encoding='utf-8-sig')
    cleaned = re.sub(re.escape(START) + r'.*?' + re.escape(END) + r'\n?', '', text, flags=re.S)
    if cleaned != text:
        path.write_text(cleaned, encoding='utf-8', newline='\n')

if __name__ == '__main__':
    refresh(Path(__file__).resolve().parents[1] / 'Config')

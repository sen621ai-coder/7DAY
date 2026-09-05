"""Source regressions for full-health use, real throwing and native sockets."""
import pathlib
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[1]
MOD = ROOT / '99-AEC_T16_RuntimeFix'
items = {i.get('name'): i for i in ET.parse(MOD / 'Config/items.xml').iter('item')}
blocks = {i.get('name'): i for i in ET.parse(MOD / 'Config/blocks.xml').iter('block')}
field = ['itemPZAECQuickArmorGel', 'itemPZAECResonanceInjector', 'itemPZAECDecoyBeacon', 'itemPZAECEvacAnchor']
field += [f'itemPZAECFieldRepairKitT{t}' for t in range(16,20)]
devices = [f'itemPZAEC{f}DeviceT{t}' for f in ('Harrier','Storm','Tremor','Warden') for t in range(16,20)]
for name in devices:
    assert items[name].find("property[@name='CreativeMode']").get('value') == 'None'
    assert items[name].find("property[@class='Action0']") is None
    assert items[name].find('.//triggered_effect') is None
for name in field:
    node = items[name]
    assert node.find("property[@name='Extends']").get('value') == 'resourceLegendaryParts', name
    action = node.find("property[@class='Action0']")
    assert action.find("property[@name='Class']").get('value') == 'PZAECUse,AEC.T16.RuntimeFix', name
    assert action.find("property[@name='UseAnimation']").get('value') == 'false', name
    assert node.find("property[@class='Action1']") is None, name
    assert node.find(".//triggered_effect[@action='PZAECFieldUse,AEC.T16.RuntimeFix']") is not None, name
for tier in range(16,20):
    grenade = items[f'thrownPZAECCounterJammerT{tier}']
    assert grenade.find("property[@name='Extends']").get('value') == 'thrownGrenade'
    explosion = grenade.find("property[@class='Explosion']")
    assert all(explosion.find(f"property[@name='{p}']").get('value') == '0' for p in ('BlockDamage','EntityDamage','RadiusBlocks','BlastPower'))
    assert explosion.find("property[@name='RadiusEntities']").get('value') == '8'
    turret = blocks[f'PZAECArmorBreakTurretT{tier}']
    assert turret.find("property[@name='Buff']").get('value') == f'buffPZAECArmorBreakT{tier}'
    assert turret.find("property[@name='BuffChance']").get('value') == '1'
for family in ('Harrier','Storm','Tremor','Warden'):
    helmet = items[f'armorPZAEC{family}HelmetT19']
    assert f'PZAEC{family}CalibrationT19' in helmet.find("property[@name='Tags']").get('value').split(',')
print('PASS: 8 field-use paths; 16 retired devices hidden and disabled; 4 T19 helmet calibration hosts; 4 native throwable jammers and armor-break turrets.')

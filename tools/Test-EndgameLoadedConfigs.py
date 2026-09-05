"""Verify endgame definitions in a real server's config dump and startup log."""
import argparse
import pathlib
import re
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--configs', type=pathlib.Path, required=True)
parser.add_argument('--log', type=pathlib.Path, required=True)
parser.add_argument('--allow-telnet-disconnect', action='store_true',
                    help='Allow only a transport-write exception preceded by the loopback Telnet client disconnect error')
args = parser.parse_args()
source = pathlib.Path(__file__).resolve().parents[1] / '99-AEC_T16_RuntimeFix' / 'Config'
log = args.log.read_text(encoding='utf-8-sig', errors='replace')
assert 'ERR XML loader' not in log, 'Native XML loader failed'
for error in re.finditer(r'^.*EXC .*$', log, re.M):
    telnet_disconnect = ('EXC Unable to write data to the transport connection:' in error[0]
                         and 'ERR IOException in TelnetClient_127.0.0.1:' in log[max(0,error.start()-800):error.start()])
    if args.allow_telnet_disconnect and telnet_disconnect:
        print('NOTE: test Telnet connection closed while startup logs were being sent; transport exception explicitly allowed.')
    else:
        raise AssertionError('Runtime exception in server log: ' + error[0])
version = ET.parse(source.parent / 'ModInfo.xml').find('Version').get('value')
assert f'Loaded Mod: AEC_T16_RuntimeFix ({version})' in log, 'Expected runtime was not loaded'
assert '[AEC-T16-Fix] Runtime fixes active.' in log, 'Runtime hooks did not initialize'
assert 'INF StartGame done' in log, 'Server has not finished starting its world'
for filename, tag in [('items.xml', 'item'), ('item_modifiers.xml', 'item_modifier'),
                      ('blocks.xml', 'block'), ('buffs.xml', 'buff'),
                      ('entityclasses.xml', 'entity_class'), ('recipes.xml', 'recipe')]:
    assert f'Loaded (local): {filename[:-4]} in ' in log, f'{filename} never finished native loading'
    expected = {e.get('name') for e in ET.parse(source / filename).iter(tag)
                if 'PZAEC' in (e.get('name') or '')}
    actual = {e.get('name') for e in ET.parse(args.configs / filename).getroot().findall(tag)}
    assert expected <= actual, f'{filename} missing: {sorted(expected - actual)}'
    print(f'PASS: {filename}: {len(expected)} endgame definitions survived live mod merging and native loading.')
assert 'Loaded (local): loot in ' in log, 'Loot did not finish native loading'
loaded_items = ET.parse(args.configs / 'items.xml').getroot()
for tier in range(16,20):
    ammo = loaded_items.find(f"item[@name='ammoPZAECCounterPulseT{tier}']")
    blast = ammo.find("property[@class='Action1']/property[@class='Explosion']")
    assert blast is not None and blast.find("property[@name='BlockDamage']").get('value') == '0', 'Merged pulse ammo still inherits HE demolition'
    assert int(blast.find("property[@name='EntityDamage']").get('value')) == [1200,1700,2400,3400][tier-16], 'Merged pulse explosion lost tier scaling'
print('PASS: four pulse rounds retain explicit zero explosion block damage in the live merged configuration.')
print('PASS: real server initialized runtime hooks, equipment, recipes and loot without loader exceptions.')
print('Client visuals, active combat and two-player gameplay are separate acceptance checks.')

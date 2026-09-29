"""Archive a completed isolated run with exact staged DLL identity and measured rates."""
import argparse
import collections
import hashlib
import json
from pathlib import Path
import re
import shutil
import xml.etree.ElementTree as E

parser = argparse.ArgumentParser()
parser.add_argument('session', type=Path)
parser.add_argument('output', type=Path)
args = parser.parse_args()
session = json.loads(args.session.read_text(encoding='utf-8-sig'))
report = Path(session['Report'])
lines = report.read_text(encoding='utf-8-sig').splitlines()
assert lines[-1].startswith('FINISHED failures=0'), 'Incomplete or failed run'
assert not any(line.startswith('FAIL ') for line in lines), 'Fixture failure'
with Path(session['Log']).open(encoding='utf-8-sig', errors='replace') as stream:
    errors = [line.strip() for line in stream if ' ERR ' in line or 'Exception:' in line]
assert not errors, '\n'.join(errors[:5])
groups = collections.defaultdict(list)
for line in lines:
    if not line.startswith('MEASURE '):
        continue
    values = dict(re.findall(r'(\w+)=([^ ]+)', line))
    key = tuple(values.get(k, '') for k in ['vehicle', 'scenario', 'step', 'angle', 'reverse', 'suspension', 'mount'])
    groups[key].append(values)
measurements = []
for key, values in groups.items():
    row = dict(zip(['vehicle', 'scenario', 'heightOrGap', 'angle', 'reverse', 'suspensionOverride', 'mountOverride'], key))
    row.update(passed=sum(v['pass'] == 'True' for v in values), trials=len(values),
               maxPitch=max(float(v['pitch']) for v in values), maxRoll=max(float(v['roll']) for v in values),
               maxUpwardVelocity=max(float(v['up']) for v in values))
    measurements.append(row)
    print(f"{key[:5]}: {row['passed']}/{row['trials']}")
mod = Path(session['QaRoot']) / 'UserData/Mods/ZZ-PZAEC_M1Abrams'
result = dict(version=E.parse(mod/'ModInfo.xml').find('Version').get('value'),
              dllSHA256=hashlib.sha256((mod/'PZAEC.M1Abrams.dll').read_bytes()).hexdigest(),
              fixtureSHA256=hashlib.sha256((mod.parent/'ZZZ-M1NativeQA/M1.NativeQA.dll').read_bytes()).hexdigest(),
              sourceReport=str(report), sourceLog=session['Log'], measurements=measurements,
              guardChecks=[line for line in lines if line.startswith('PASS ')],
              limitations='Controlled native physics, scripted wheel torque/speed governor, real vehicle shapes. Not player input, rendering, collision damage, network latency or free-form terrain acceptance.')
args.output.parent.mkdir(parents=True, exist_ok=True)
Path(str(args.output) + '.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
shutil.copyfile(report, Path(str(args.output) + '.txt'))

"""Dedicated live stat panels for new equipment; no legacy display-value aliases."""
from pathlib import Path
import csv
import io
import re
import xml.etree.ElementTree as ET
from equipment_descriptions import LABELS
from generate_fusion_upgrades import GEAR

BEGIN = '<!-- BEGIN GENERATED EQUIPMENT DISPLAY -->'
END = '<!-- END GENERATED EQUIPMENT DISPLAY -->'

def write_section(path, body):
    old = path.read_text(encoding='utf-8-sig') if path.exists() else '<configs>\n</configs>\n'
    section = BEGIN + '\n' + body + '\n' + END
    if BEGIN in old:
        new = re.sub(re.escape(BEGIN) + r'.*?' + re.escape(END), lambda _: section, old, flags=re.S)
    else:
        new = old.replace('</configs>', section + '\n</configs>')
    if new != old: path.write_text(new, encoding='utf-8', newline='\n')

def refresh(config):
    config = Path(config)
    ui = ET.Element('append', xpath='/ui_display_info/item_display')
    patches = {'items.xml': [], 'item_modifiers.xml': []}
    labels = {}
    expected = []
    for filename, element in [('items.xml','item'), ('item_modifiers.xml','item_modifier')]:
        for item in ET.parse(config / filename).iter(element):
            name = item.get('name','')
            if not (GEAR.fullmatch(name) or (name.startswith('modPZAEC') and re.search(r'T1[6-9]$', name))): continue
            effects = item.findall('./effect_group/passive_effect')
            stats = {}
            for effect in effects:
                stat, op, tags = effect.get('name'), effect.get('operation'), effect.get('tags','')
                if stat not in LABELS or stat in ('ModSlots', 'BurstRoundCount'): continue
                mode = 'Percent' if op.startswith('perc_') else 'Base'
                stats.setdefault((stat, mode, tags), []).append(effect)
            # Keep the native compact panel readable; the full description lists
            # all intrinsic abilities, conditional effects and set bonuses.
            if name.startswith('armor'):
                slot = next(s for s in ('Helmet','Outfit','Gloves','Boots') if s in name)
                priority = {'Helmet':['HeadshotDamageModifier','EntityDamage','PlayerExpGain','LootStage'],
                    'Outfit':['HealthMax','StaminaMax','CarryCapacity','MagazineSize'],
                    'Gloves':['EntityDamage','RoundsPerMinute','ReloadSpeedMultiplier','BlockRepairAmount'],
                    'Boots':['RunSpeed','StaminaMax','StaminaChangeOT','StaminaLoss']}[slot]
                priority = ['PhysicalDamageResist','DegradationMax'] + priority
            elif name.startswith('mod'):
                priority = list(dict.fromkeys(k[0] for k in stats))
            else:
                priority = ['EntityDamage','MagazineSize','RoundsPerMinute','AttacksPerMinute','MaxRange','DegradationMax','ReloadSpeedMultiplier','StaminaLoss']
            keys = [k for stat in priority for k in stats if k[0] == stat]
            # The reload base (1) is an implementation foundation; display the
            # useful percentage bonus instead. Damage rows retain action tags.
            keys = [k for k in keys if not (k[0]=='ReloadSpeedMultiplier' and k[1]=='Base')]
            keys = [k for k in keys if not (k[1]=='Percent' and (k[0],'Base',k[2]) in keys)]
            keys = keys[:8]
            panel = 'AECDisplay_' + name
            info = ET.SubElement(ui, 'item_display_info', display_type=panel, display_group='groupAttire' if name.startswith('armor') else 'groupRanged')
            for index, (stat, mode, tags) in enumerate(keys):
                label = LABELS[stat]
                if stat in ('EntityDamage','StaminaLoss') and tags in ('primary','secondary','ranged'):
                    label = {'primary':'轻击','secondary':'重击','ranged':'远程'}[tags] + label
                title = f'aecDisplay_{stat}_{mode}_{tags.replace(",","_")}'
                labels[title] = label
                ET.SubElement(info, 'display_entry', name='aec' + mode + '_' + stat, title_key=title,
                    display_type='Percent' if mode=='Percent' or stat=='BuffResistance' else 'Decimal1', display_leading_plus='true', tags=tags)
                # Independent XML-derived expected base/percent, used by the
                # native regression. All selected effects are unconditional.
                values = []
                for rank in (0,1,10):
                    base, percent = 0.0, 1.0
                    for effect in effects:
                        if effect.get('name') != stat or effect.get('tags','') not in ('',tags): continue
                        if effect.get('tags') and not tags: continue
                        op = effect.get('operation'); value = float(effect.get('value'))
                        if GEAR.fullmatch(name) and rank:
                            lower = stat.startswith(('Spread','KickDegrees')) or stat in ('StaminaLoss','DegradationPerUse','NoiseMultiplier','ScavengingTime','FoodLossPerStaminaPointGained','WaterLossPerStaminaPointGained','IncrementalSpreadMultiplier')
                            grow = not lower if op.endswith('_set') else not ((value < 0) ^ op.endswith('_subtract') ^ lower)
                            value *= (1.05 if grow else .95) ** rank
                        if op=='base_set': base=value
                        elif op=='base_add': base+=value
                        elif op=='base_subtract': base-=value
                        elif op=='perc_set': percent=value
                        elif op=='perc_add': percent+=value
                        elif op=='perc_subtract': percent-=value
                    values.append(base * percent if mode=='Base' else percent-1)
                expected.append([name,index,*values])
            xpath = f"/{'items' if element=='item' else 'item_modifiers'}/{element}[@name='{name}']"
            if item.find("property[@name='DisplayType']") is not None:
                patches[filename].append(ET.Element('remove', xpath=xpath + "/property[@name='DisplayType']"))
            append = ET.Element('append', xpath=xpath)
            ET.SubElement(append, 'property', name='DisplayType', value=panel)
            patches[filename].append(append)
    ET.indent(ui, space='  ')
    write_section(config / 'ui_display.xml', ET.tostring(ui, encoding='unicode'))
    for filename, nodes in patches.items():
        write_section(config / filename, '\n'.join(ET.tostring(n, encoding='unicode') for n in nodes))
    locpath = config / 'Localization.csv'
    lines = [line for line in locpath.read_text(encoding='utf-8-sig').splitlines() if ',AECEquipmentDisplay,' not in line]
    out = io.StringIO(); writer = csv.writer(out, lineterminator='\n')
    for key, label in sorted(labels.items()): writer.writerow([key,'ui_display','AECEquipmentDisplay','','','',label,'','','','','','','','','','','',label,label])
    locpath.write_text('\n'.join(lines).rstrip()+'\n'+out.getvalue(), encoding='utf-8', newline='\n')
    out = io.StringIO(); writer = csv.writer(out, lineterminator='\n'); writer.writerows(expected)
    (config.parent / 'Tools/equipment_display_expected.csv').write_text(out.getvalue(), encoding='utf-8', newline='\n')
    return len(ui), len(expected)

if __name__ == '__main__': print(refresh(Path(__file__).resolve().parents[1] / 'Config'))

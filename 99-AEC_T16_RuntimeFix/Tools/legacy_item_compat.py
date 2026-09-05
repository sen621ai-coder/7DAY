"""Hidden retired definitions keep old inventories and queued outputs readable."""
import csv
import io
import pathlib
import xml.etree.ElementTree as ET

BEGIN = '<!-- BEGIN RETIRED ITEM SAVE COMPATIBILITY -->'
END = '<!-- END RETIRED ITEM SAVE COMPATIBILITY -->'


def refresh(config):
    root = ET.Element('append', xpath='/items')
    rows = []
    for family, cn in [('Harrier', '猎隼'), ('Storm', '风暴'), ('Tremor', '震岳'), ('Warden', '守望')]:
        for tier in range(16, 20):
            name = f'itemPZAEC{family}DeviceT{tier}'
            node = ET.SubElement(root, 'item', name=name)
            props = {'Extends': 'resourceLegendaryParts', 'CreativeMode': 'None',
                     'Tags': f'PZAECRetiredDevice,PZAEC{family}CalibrationT19',
                     'Stacknumber': '1', 'CustomIcon': 'resourceElectricParts',
                     'CustomIconTint': '888888', 'DescriptionKey': name+'Desc',
                     'SellableToTrader': 'false', 'EconomicValue': '0'}
            for key, value in props.items():
                ET.SubElement(node, 'property', name=key, value=value)
            group = ET.SubElement(node, 'effect_group', tiered='false')
            ET.SubElement(group, 'passive_effect', name='ModSlots', operation='base_set', value='6')
            rows.extend([(name, f'旧版T{tier}{cn}装置（已停用）'),
                         (name+'Desc', '仅保留用于读取旧存档，已停止制作、掉落和手动施放。共鸣能力由四件套自动触发。旧装置中的芯片仍保留，可在改装界面取下，安装到对应套装的T19头盔。')])
    for tier in range(16, 20):
        name = f'itemPZAECTestBundle{tier}Devices1'
        node = ET.SubElement(root, 'item', name=name)
        for key, value in {'Extends': 'questRewardBundleMaster', 'CreativeMode': 'None',
                           'Stacknumber': '1', 'DescriptionKey': name+'Desc', 'SellableToTrader': 'false'}.items():
            ET.SubElement(node, 'property', name=key, value=value)
        action = ET.SubElement(node, 'property', {'class': 'Action0'})
        for key, value in {'Class': 'OpenBundle', 'Delay': '0', 'Consume': 'true',
                           'Create_item': 'resourcePZAECDeviceChassis', 'Create_item_count': '4'}.items():
            ET.SubElement(action, 'property', name=key, value=value)
        rows.extend([(name, f'旧版T{tier}装置测试包（已停用）'),
                     (name+'Desc', '旧存档兼容包，打开返还4个装置底盘。新测试总包已不再发放手动装置。')])
    ET.indent(root, space='  ')
    body = BEGIN + '\n' + ET.tostring(root, encoding='unicode') + '\n' + END
    path = config / 'items.xml'
    text = path.read_text(encoding='utf-8-sig')
    if BEGIN in text:
        text = text[:text.index(BEGIN)] + body + text[text.index(END)+len(END):]
    else:
        text = text.replace('</configs>', body+'\n</configs>')
    path.write_text(text, encoding='utf-8', newline='\n')
    path = config / 'Localization.csv'
    with path.open(encoding='utf-8-sig', newline='') as stream:
        old = list(csv.reader(stream))
    keys = {name for name, _ in rows}
    kept = [r for r in old if r and r[0] not in keys]
    header = [v.lower() for v in kept[0]]
    for key, label in rows:
        row = [''] * len(header)
        row[0:3] = [key, 'items', 'AECRetiredCompatibility']
        for lang in ('english', 'schinese', 'tchinese'):
            if lang in header: row[header.index(lang)] = label
        kept.append(row)
    output = io.StringIO()
    csv.writer(output, lineterminator='\n').writerows(kept)
    path.write_text(output.getvalue(), encoding='utf-8', newline='\n')


if __name__ == '__main__':
    refresh(pathlib.Path(__file__).resolve().parents[1] / 'Config')

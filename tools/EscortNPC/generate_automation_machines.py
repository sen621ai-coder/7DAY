"""Run after the other config generators; retain all block IDs and native features."""
from pathlib import Path
import csv
import xml.etree.ElementTree as E
root=Path(__file__).resolve().parents[2]/'97-AutomationWorkshop'
kinds=('Sorter','Kitchen','Smelter','Forge','Recycler','Farm','Miner','Transfer','WaterPump','WaterTank','AmmoFeed')
path=root/'Config/blocks.xml'
tree=E.parse(path)
for kind in kinds:
    block=tree.find(f".//block[@name='yfAuto{kind}']")
    assert block is not None, kind
    block.find("property[@name='Model']").set('value',f'#@modfolder:Resources/automation-machines.unity3d?Assets/Machines/Machine{kind}.prefab')
E.indent(tree,space='  ')
tree.write(path,encoding='utf-8',xml_declaration=True)
info=E.parse(root/'ModInfo.xml');info.find('Version').set('value','0.5.1')
E.indent(info,space='  ');info.write(root/'ModInfo.xml',encoding='utf-8',xml_declaration=True)
loc=root/'Config/Localization.csv'
with loc.open(encoding='utf-8-sig',newline='') as f:rows=list(csv.reader(f))
for row in rows:
    if row and row[0]=='yfAutoTransferDesc':
        row[-2:]=[s.replace('复用箱体外观，无可见传送带动画。','转运机械臂外观，瞄准查看状态；货物直接搬运。') for s in row[-2:]]
    if row and row[0]=='yfAutoWaterTankDesc':
        row[-2:]=[s.replace('原生箱子存放灌溉水','储水罐存放灌溉水').replace('箱子被打开','库存被打开') for s in row[-2:]]
with loc.open('w',encoding='utf-8-sig',newline='') as f:csv.writer(f).writerows(rows)

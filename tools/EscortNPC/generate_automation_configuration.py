"""Generate native machine inventory, loot window and production controls."""
from pathlib import Path
import csv
import xml.etree.ElementTree as E
import sys

workspace = Path(__file__).resolve().parents[2]
root = Path(sys.argv[1]).resolve() if len(sys.argv)>1 else workspace / '97-AutomationWorkshop/Config'

def write(node, path):
    E.indent(node, space='  ')
    E.ElementTree(node).write(path, encoding='utf-8', xml_declaration=True)

# Keep native feature names/progress intact; V18 composite saves initialize added features.
blocks=E.parse(root/'blocks.xml').getroot()
for block in blocks.findall('.//block'):
    if block.get('name') not in {'yfAuto'+n for n in ('Kitchen','Smelter','Forge','Recycler','Farm','Miner','Sorter','Transfer','WaterPump','AmmoFeed')}: continue
    features=block.find("property[@class='CompositeFeatures']")
    if features.find("property[@class='TEFeatureStorage']") is None:
        storage=E.SubElement(features,'property',{'class':'TEFeatureStorage'})
        E.SubElement(storage,'property',name='LootList',value='yfAutoMachineInventory')
    if features.find("property[@class='TEFeatureMachineInventory']") is None:
        E.SubElement(features,'property',{'class':'TEFeatureMachineInventory'})
write(blocks,root/'blocks.xml')
loot=E.parse(root/'loot.xml').getroot()
if loot.find(".//lootcontainer[@name='yfAutoMachineInventory']") is None:
    app=E.SubElement(loot,'append',xpath='/lootcontainers')
    E.SubElement(app,'lootcontainer',name='yfAutoMachineInventory',count='0',size='6,6',open_time='0',sound_open='UseActions/crate_steel_open',sound_close='UseActions/crate_steel_close',loot_quality_template='qualBaseTemplate')
write(loot,root/'loot.xml')

xui = E.parse(root / 'XUi_InGame/xui.xml').getroot()
for app in xui:
    for group in list(app):
        if group.get('name') in ('yfMachineConfiguration','yfMachineInventory'): app.remove(group)
for empty in list(xui):
    if empty.tag == 'append' and len(empty) == 0: xui.remove(empty)
app = E.SubElement(xui, 'append', xpath='/xui')
group = E.SubElement(app, 'window_group', name='yfMachineConfiguration', close_compass_on_open='true')
E.SubElement(group, 'window', name='windowYFAutomationConfiguration')
group = E.SubElement(app, 'window_group', name='yfMachineInventory', controller='XUiC_LootWindowGroup', close_compass_on_open='true')
for name in ('windowYFAutomationStorage','windowYFAutomationInventoryControls','windowYFAutomationRecipe','windowNonPagingHeader'):
    E.SubElement(group,'window',name=name)
write(xui, root / 'XUi_InGame/xui.xml')

windows=E.Element('configs');app=E.SubElement(windows,'append',xpath='/windows')
# Reuse native loot-grid and native item synchronization. Sorting is omitted because
# sorting the whole container would mix the input and output partitions.
import copy
native=workspace.parent/'Data/Config/XUi_InGame/windows.xml'
storage=copy.deepcopy(E.parse(native).find("window[@name='windowLooting']"))
storage.set('name','windowYFAutomationStorage');storage.set('panel','Right')
for node in storage.iter():
    for child in list(node):
        if child.get('name')=='btnSort': node.remove(child)
storage.set('height','312')
grid=storage.find("rect[@name='content']/grid[@name='queue']")
grid.set('rows','6');grid.set('cols','6');grid.set('cell_width','54');grid.set('cell_height','36')
for child in list(grid):grid.remove(child)
E.SubElement(grid,'backpack_item_stack',name='0')
caption=storage.find("rect[@name='header']/label[@name='lootName']")
caption.set('text','内置库存');caption.set('font_size','22')
app.append(storage)
E.SubElement(storage,'label',name='partitionHelp',text='上3行：原料 / 工具    下3行：成品',pos='3,-277',width='324',height='30',font_size='18',depth='3',color='[white]')
E.SubElement(storage.find("rect[@name='content']"),'sprite',name='partition',pos='0,-108',width='324',height='3',depth='20',sprite='menu_empty3px',color='71,197,216,255')

for name,controller,panel in [('windowYFAutomationConfiguration','YFAutomation.YFAutomationConfiguration, YF.Automation','Center'),('windowYFAutomationInventoryControls','YFAutomation.YFAutomationInventoryControls, YF.Automation','Left')]:
    w=E.SubElement(app,'window',name=name,width='430',height='752',panel=panel,cursor_area='true',controller=controller)
    E.SubElement(w,'sprite',name='background',depth='0',width='430',height='752',sprite='menu_empty3px',color='[darkGrey]',type='sliced')
    E.SubElement(w,'sprite',name='header',depth='1',width='430',height='43',sprite='ui_game_panel_header')
    def label(parent,name,text,pos,width=410,height=30,size=21):
        E.SubElement(parent,'label',name=name,text=text,pos=pos,width=str(width),height=str(height),font_size=str(size),depth='3',color='[white]',justify='left')
    def button(name,title,x,y,width=410,height=32):
        b=E.SubElement(w,'button',name=name,pos=f'{x},{y}',width=str(width),height=str(height),depth='2',sprite='menu_empty3px',defaultcolor='[mediumGrey]',hoversprite='menu_empty3px',hovercolor='[lightGrey]',type='sliced')
        label(b,name+'Text',title,'8,-3',width-16,height-4,20)
    label(w,'title','自动化设备','10,-6',size=25)
    label(w,'status','正在读取…','10,-48',height=38,size=20)
    E.SubElement(w,'textfield',name='search',pos='10,-90',width='410',height='32',font_size='22',character_limit='60',on_return='Submit',open_vk_on_open='false',focus_on_open='false',clear_button='true')
    for row in range(8):button('recipe'+str(row),'',10,-130-row*30,height=28)
    button('previous','上一页',10,-380,100,28);label(w,'pages','','125,-380',160);button('next','下一页',320,-380,100,28)
    button('product','目标产品',10,-414)
    button('mode','库存模式',10,-450)
    button('source','输入箱',10,-486);button('target','输出箱',10,-522)
    label(w,'details','','10,-486',height=66,size=18)
    button('toggle','启动 / 暂停',10,-558)
    label(w,'help','','10,-596',height=72,size=18)
    label(w,'notice','','10,-672',height=32,size=19)
    button('save','保存',10,-712,126);button('refresh','刷新',152,-712,126);button('close','关闭',294,-712,126)
recipe=E.SubElement(app,'window',name='windowYFAutomationRecipe',width='870',height='300',panel='Center',cursor_area='true',controller='YFAutomation.YFAutomationRecipePanel, YF.Automation')
E.SubElement(recipe,'sprite',name='background',width='870',height='300',depth='0',sprite='menu_empty3px',color='[darkGrey]',type='sliced')
E.SubElement(recipe,'sprite',name='header',width='870',height='43',depth='1',sprite='ui_game_panel_header')
label(recipe,'title','配方与材料','12,-6',846,34,26)
label(recipe,'body','','12,-48',846,218,20)
label(recipe,'page','','390,-270',90,30,20)
for name,text,x in [('back','上一页',12),('forward','下一页',752)]:
    b=E.SubElement(recipe,'button',name=name,pos=f'{x},-266',width='106',height='32',depth='2',sprite='menu_empty3px',defaultcolor='[mediumGrey]',hoversprite='menu_empty3px',hovercolor='[lightGrey]',type='sliced')
    label(b,name+'Text',text,'8,-4',90,25,20)
# Fit alongside Project Z's 870-wide backpack; keep readable font sizes.
for window in app.findall('window'):
    if window.get('name') not in ('windowYFAutomationConfiguration','windowYFAutomationInventoryControls'):continue
    for node in window.iter():
        if 'width' in node.attrib:node.set('width',str(round(int(node.get('width'))*350/430)))
        if 'height' in node.attrib:node.set('height',str(round(int(node.get('height'))*700/752)))
        if 'pos' in node.attrib:
            x,y=node.get('pos').split(',');node.set('pos',f'{round(int(x)*350/430)},{round(int(y)*700/752)}')
    window.find("label[@name='help']").set('font_size','15')
    window.find("label[@name='details']").set('font_size','16')
from automation_panel_layout import apply
apply(app, workspace)
write(windows,root/'XUi_InGame/windows.xml')

path = root / 'Localization.csv'
with path.open(encoding='utf-8-sig', newline='') as f:
    reader = csv.DictReader(f)
    fields = reader.fieldnames
    rows = [r for r in reader if r['Key'] != 'yfConfigureMachine']
row = {f: '' for f in fields}
row.update(Key='yfConfigureMachine', english='Configure machine')
for field in fields:
    if field in ('schinese', 'tchinese'): row[field] = '配置机器'
rows.append(row)
with path.open('w', encoding='utf-8-sig', newline='') as f:
    writer = csv.DictWriter(f, fieldnames=fields)
    writer.writeheader()
    writer.writerows(rows)

"""Idempotent M1 module configuration, also applied after balance regeneration."""
from pathlib import Path
import csv,xml.etree.ElementTree as E
MODULES=[
('Powertrain','动力优化','Powertrain','modVehicleSuperCharger','m1Drive','扭矩+30%，前进/加速/倒车上限+25%，无额外油耗倍率。与M1经济巡航互斥。',[25,2,15,5,5,0]),
('Economy','经济巡航','Economy','modVehicleFuelSaver','m1Drive','油耗降低40%，不降低动力或速度。与M1动力优化互斥。',[15,1,10,10,0,0]),
('Armor','附加装甲','Armor','modVehicleArmor','m1Armor','正面/顶部减伤+4个百分点，侧面+12，后部+10，最高80%。不减速，底部/未知方向不变。',[40,3,10,0,0,0]),
('Corrosion','抗蚀衬层','Corrosion Liner','modArmorPlatingBasic','m1Corrosion','酸蚀享受完整区域装甲减伤，仍非免疫。不会清除酸蚀或辐射等状态。',[15,3,5,5,0,5]),
('Stabilizer','稳定火控','Stabilizer','modGunBipod','m1FireControl','炮塔及炮管追踪速度+50%，车体开炮反冲降低60%。不自动瞄准。与M1辅助装填互斥。',[15,2,10,20,5,0]),
('Autoloader','辅助装填','Autoloader','modGunMagazineExtender','m1FireControl','装填与切弹时间降低25%，持续射速约+33.3%。不降低追踪速度。与M1稳定火控互斥。',[25,2,20,10,10,0])]
def apply(root):
    cfg=root/'ZZ-PZAEC_M1Abrams/Config'
    def write(file,node):
        E.indent(node);E.ElementTree(node).write(cfg/file,encoding='utf-8',xml_declaration=True)
    def prop(node,key,value):E.SubElement(node,'property',name=key,value=str(value))
    items=E.parse(cfg/'items.xml').getroot()
    for i in range(4):
        name='vehicleM1Abrams'+('' if i==0 else f'T{16+i}')+'Placeable'
        item=items.find(f"./append/item[@name='{name}']")
        item.find("effect_group/passive_effect[@name='ModSlots']").set('value',str(2 if i<2 else 3))
        item.find("property[@name='Tags']").set('value','vehicle,PZAECM1')
    write('items.xml',items)
    mods=E.Element('configs');append=E.SubElement(mods,'append',xpath='/item_modifiers')
    recipes=E.parse(cfg/'recipes.xml').getroot();ra=recipes.find('append')
    locfile=cfg/'Localization.csv'
    with locfile.open(encoding='utf-8-sig',newline='') as f:rows=list(csv.reader(f))
    rows=[r for r in rows if not r[0].startswith('modPZAECM1')]
    ids=[]
    resources=['resourceForgedSteel','resourceDurablAlloys','resourceMechanicalParts','resourceElectricParts','resourceSpring','resourceDuctTape']
    for suffix,zh,en,icon,group,desc,cost in MODULES:
        name='modPZAECM1'+suffix;ids.append(name)
        node=E.SubElement(append,'item_modifier',name=name,installable_tags='PZAECM1',modifier_tags=group,blocked_tags='noMods',type='attachment')
        for key,value in {'Extends':'modGeneralMaster','CreativeMode':'Player','CustomIcon':icon,'DescriptionKey':name+'Desc','UnlockedBy':'craftingVehicles','ShowQuality':'false','Stacknumber':'1','EconomicValue':'1000','MaxModsAllowed':'1'}.items():prop(node,key,value)
        # Runtime applies validated combinations; XML cannot bypass server validation.
        E.SubElement(node,'effect_group',tiered='false')
        for old in list(ra):
            if old.get('name')==name:ra.remove(old)
        r=E.SubElement(ra,'recipe',name=name,count='1',craft_area='workbench',craft_time='120',use_ingredient_modifier='false',tags='learnable,workbenchCrafting')
        for key,count in zip(resources,cost):
            if count:E.SubElement(r,'ingredient',name=key,count=str(count))
        rows.extend([[name,'items','Item','','','M1 '+en,'M1'+zh],[name+'Desc','items','Item','','',desc,'M1专用；每种限装1件。'+desc]])
    write('item_modifiers.xml',mods);write('recipes.xml',recipes)
    progression=E.parse(cfg/'progression.xml').getroot()
    effect=progression.find('./append/passive_effect');tags=[x for x in effect.get('tags').split(',') if not x.startswith('modPZAECM1')];effect.set('tags',','.join(tags+ids));write('progression.xml',progression)
    with locfile.open('w',encoding='utf-8',newline='') as f:csv.writer(f).writerows(rows)
if __name__=='__main__':apply(Path(__file__).resolve().parents[2])

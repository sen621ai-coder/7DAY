"""Build the wrist cleaner's local V3.2 XML integration."""
from pathlib import Path
import csv
import xml.etree.ElementTree as E

ROOT = Path(__file__).resolve().parents[2]
MOD = ROOT / 'ZZZ-PZAEC_PortableShower'
CONFIG = MOD / 'Config'
CONFIG.mkdir(parents=True, exist_ok=True)
NAME = 'modPZAECWristShower'

def save(name, root):
    E.indent(root, space='  ')
    E.ElementTree(root).write(CONFIG / name, encoding='utf-8', xml_declaration=True)

root = E.Element('xml')
for name, value in [('Name','PZAECPortableShower'),('DisplayName','PZAEC 腕式便携清洁模块'),('Description','Project Z hygiene integration; glove attachment with water reserve and combat pause.'),('Author','PZAEC'),('Version','1.0.0')]:
    E.SubElement(root, name, value=value)
E.ElementTree(root).write(MOD/'ModInfo.xml', encoding='utf-8', xml_declaration=True)

root = E.Element('configs')
item = E.SubElement(E.SubElement(root,'append',xpath='/item_modifiers'),'item_modifier',name=NAME,installable_tags='armorHands',modifier_tags='PZAECWristShower',blocked_tags='noMods',type='attachment')
for name, value in [('Extends','modGeneralMaster'),('CreativeMode','Player'),('CustomIcon','modArmorWaterPurifier'),('CustomIconTint','85,210,240'),('DescriptionKey',NAME+'Desc'),('UnlockedBy','craftingArmor'),('EconomicValue','1500'),('Material','Mmetal'),('Group','Mods,Armor')]:
    E.SubElement(item,'property',name=name,value=value)
save('item_modifiers.xml',root)

root=E.Element('configs')
recipe=E.SubElement(E.SubElement(root,'append',xpath='/recipes'),'recipe',name=NAME,count='1',craft_area='workbench',craft_time='120',tags='learnable,workbenchCrafting',use_ingredient_modifier='false')
for name,count in [('resourceForgedSteel',10),('resourceMechanicalParts',8),('resourceElectricParts',8),('resourceScrapPolymers',20),('resourceMetalPipe',4),('resourceDuctTape',5)]:
    E.SubElement(recipe,'ingredient',name=name,count=str(count))
save('recipes.xml',root)

root=E.Element('configs')
path="/progression/crafting_skills/crafting_skill[@name='craftingArmor']"
entry=E.SubElement(E.SubElement(root,'append',xpath=path),'display_entry',icon='modArmorWaterPurifier',name_key=NAME,has_quality='false',unlock_level='50')
E.SubElement(entry,'unlock_entry',item=NAME,unlock_tier='1')
E.SubElement(E.SubElement(root,'append',xpath=path+'/effect_group[1]'),'passive_effect',name='RecipeTagUnlocked',operation='base_set',level='50,100',value='1',tags=NAME)
save('progression.xml',root)

root=E.Element('configs')
group=E.SubElement(E.SubElement(root,'append',xpath="/buffs/buff[@name='buffStatusCheck01']"),'effect_group')
E.SubElement(group,'triggered_effect',trigger='onSelfBuffUpdate',action='AddBuff',buff='buffPZAECShowerController')
for trigger in ['onSelfEnteredGame','onSelfRespawn','onCombatEntered','onOtherAttackedSelf','onOtherDamagedSelf','onSelfDamagedSelf','onSelfPrimaryActionStart','onSelfSecondaryActionStart','onSelfPrimaryActionRayHit','onSelfPrimaryActionRayMiss','onSelfSecondaryActionRayHit','onSelfSecondaryActionRayMiss','onSelfRangedBurstShotStart','onSelfRangedBurstShotEnd','onSelfAttackedOther','onSelfDamagedOther','onSelfExplosionAttackedOther','onSelfExplosionDamagedOther','onSelfVehicleAttackedOther']:
    E.SubElement(group,'triggered_effect',trigger=trigger,action='PZAECShowerCombat,PZAEC.PortableShower')
append=E.SubElement(root,'append',xpath='/buffs')
controller=E.SubElement(append,'buff',name='buffPZAECShowerController',hidden='true',remove_on_death='false')
for tag,value in [('stack_type','ignore'),('duration','0'),('update_rate','1')]: E.SubElement(controller,tag,value=value)
E.SubElement(E.SubElement(controller,'effect_group'),'triggered_effect',trigger='onSelfBuffUpdate',action='PZAECShowerTick,PZAEC.PortableShower')
for name,color in [('buffPZAECShowerActive','85,210,240'),('buffPZAECShowerEmpty','240,180,80')]:
    buff=E.SubElement(append,'buff',name=name,name_key=name,description_key=name+'Desc',tooltip_key=name+'Desc',icon='ui_game_symbol_swim',icon_color=color)
    E.SubElement(buff,'stack_type',value='replace')
    E.SubElement(buff,'duration',value='2')
save('buffs.xml',root)

rows=[
    (NAME,'Wrist Shower Module','腕式便携清洁模块'),
    (NAME+'Desc','Glove attachment. Armor crafting 50; workbench 120 seconds. Cleans hygiene over about 5 minutes of active use. Wait 30 seconds after combat or weapon/tool use. Pauses while running, swimming, riding or bathing. Each ordinary bottled water supplies 60 seconds; consumes backpack water only and reserves the last 2 bottles. Remaining spray time is saved on the player. No stacking or disease treatment.','安装在手部护甲，占用1个模组槽。护甲制作50级解锁，工作台基础制作120秒。脱战30秒后自动缓慢清洁，约5分钟有效喷淋恢复一整条卫生值（自然变脏会延长时间）。攻击、受击及武器/工具操作重置脱战计时；奔跑、游泳、乘车、固定沐浴时暂停。每瓶普通清水提供60秒有效喷淋，只从背包补水并保留最后2瓶。剩余水量随角色保存；拆装不重置，多个不叠加。不治疗感染、伤口或辐射病。'),
    ('buffPZAECShowerActive','Portable cleaning','便携清洁中'),
    ('buffPZAECShowerActiveDesc','Cleaning hygiene using stored water. Pauses automatically outside safe conditions.','正在消耗已存水量，缓慢恢复卫生值。进入战斗或不满足清洁条件时自动暂停，保留剩余水量。'),
    ('buffPZAECShowerEmpty','Cleaner: water reserved or missing','清洁模块：清水不足'),
    ('buffPZAECShowerEmptyDesc','Place at least 3 ordinary bottled waters in your backpack to refill. The last 2 are reserved for drinking.','储水已用完。背包中需要至少3瓶普通清水才能自动补水；最后2瓶保留饮用。快捷栏、载具和箱子内的清水不参与补水。'),
]
with (CONFIG/'Localization.csv').open('w',encoding='utf-8',newline='') as f:
    writer=csv.writer(f);writer.writerow(['Key','File','Type','UsedInMainMenu','NoTranslate','english','schinese','tchinese'])
    for key,en,zh in rows: writer.writerow([key,'item_modifiers' if key.startswith('mod') else 'buffs','',False,'',en,zh,zh])
print('PortableShower XML and localization generated.')

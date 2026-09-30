from pathlib import Path
import csv, shutil, uuid, xml.etree.ElementTree as E
from module_config import apply, MODULES
from balance_config import apply as balance
root=Path(__file__).resolve().parents[2];cfg=root/'ZZ-PZAEC_M1Abrams/Config';n=0
def check(ok,label):
 global n
 n+=1
 assert ok,label
items=E.parse(cfg/'items.xml');mods=E.parse(cfg/'item_modifiers.xml');recipes=E.parse(cfg/'recipes.xml');prog=E.parse(cfg/'progression.xml')
with (cfg/'Localization.csv').open(encoding='utf-8',newline='') as f:loc={r[0]:r for r in csv.reader(f)}
vanilla=E.parse(root.parent/'Data/Config/item_modifiers.xml');known_icons={x.get('name') for x in vanilla.iter('item_modifier')}
check(len(list(mods.iter('item_modifier')))==6,'exactly six modules')
check(len({m.get('modifier_tags') for m in mods.iter('item_modifier')})==6,'native module tags allow all six to coexist')
vanilla_ui=E.parse(root.parent/'Data/Config/XUi_InGame/windows.xml')
check(any(int(grid.get('rows'))>=6 for grid in vanilla_ui.iter('grid') if grid.get('controller')=='ItemPartStackGrid'),'native modifier window exposes six rows')
for i in range(4):
 name='vehicleM1Abrams'+('' if i==0 else f'T{16+i}')+'Placeable';item=items.find(f"./append/item[@name='{name}']")
 check(item.find("effect_group/passive_effect[@name='ModSlots']").get('value')=='6','slots')
 check(item.find("property[@name='Tags']").get('value')=='vehicle,PZAECM1','no vanilla engine/fuel compatibility tags')
for suffix,zh,en,icon,group,desc,cost in MODULES:
 name='modPZAECM1'+suffix;m=mods.find(f"./append/item_modifier[@name='{name}']");r=recipes.find(f"./append/recipe[@name='{name}']")
 check(m is not None and r is not None,'module/recipe closure')
 check(m.get('installable_tags')=='PZAECM1' and m.get('modifier_tags')==group,'native compatibility/exclusion')
 check(m.find("property[@name='CustomIcon']").get('value') in known_icons,'existing icon')
 check(not list(m.iter('passive_effect')),'effects only through validated runtime')
 check(loc[name][5].startswith('M1 ') and loc[name][6]=='M1'+zh,'English and Chinese M1 prefix')
 check(r.get('craft_time')=='120' and r.get('craft_area')=='workbench' and r.get('use_ingredient_modifier')=='false','craft defaults')
 check('learnable' in r.get('tags') and name in prog.find('./append/passive_effect').get('tags').split(','),'unlock tags')
 check([int(x.get('count')) for x in r]==[x for x in cost if x],'approved low material cost')
check(prog.find('./append/passive_effect').get('level')=='100','vehicles 100 unlock')
# Regeneration in an isolated copy must preserve module configs and their recipes/localization.
stage=root/'.local-tests'/('M1-module-regeneration-'+uuid.uuid4().hex);out=stage/'ZZ-PZAEC_M1Abrams';out.mkdir(parents=True)
shutil.copytree(cfg,out/'Config');shutil.copy2(cfg.parent/'ModInfo.xml',out/'ModInfo.xml')
tracked=['items.xml','item_modifiers.xml','recipes.xml','progression.xml','Localization.csv']
apply(stage);first={p:(out/'Config'/p).read_bytes() for p in tracked};apply(stage)
check(all((out/'Config'/p).read_bytes()==first[p] for p in tracked),'module generator idempotent')
balance(stage)
for suffix,*_ in MODULES:
 name='modPZAECM1'+suffix
 check(E.parse(out/'Config/recipes.xml').find(f"./append/recipe[@name='{name}']") is not None,'balance generator retains modules')
check(E.parse(out/'ModInfo.xml').find('Version').get('value')=='0.4.12','regeneration version')
print('PASS',n,'module config, costs, localization, native tags and regeneration checks')

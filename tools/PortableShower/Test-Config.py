"""Validate against the installed patch stack, not just standalone XML."""
from pathlib import Path
from copy import deepcopy
from lxml import etree as E
import csv
import re

ROOT=Path(__file__).resolve().parents[2]
MOD=ROOT/'ZZZ-PZAEC_PortableShower'
parser=E.XMLParser(remove_comments=True)
checks=0
def check(ok,label):
    global checks
    checks+=1
    assert ok,label

def merged(filename):
    tree=E.parse(str(ROOT.parent/'Data/Config'/filename),parser)
    def operations(parent):
        for op in parent:
            if op.tag=='conditional':
                for branch in op:
                    match=re.fullmatch(r"mod_loaded\('([^']+)'\)",branch.get('cond',''))
                    assert branch.tag=='if' and match,'Unhandled conditional'
                    loaded={E.parse(str(p),parser).find('Name').get('value') for p in ROOT.glob('*/ModInfo.xml')}
                    if match[1] in loaded:yield from operations(branch)
            else:yield op
    for file in sorted(ROOT.glob('*/Config/'+filename),key=lambda p:p.parent.parent.name.casefold()):
        for op in operations(E.parse(str(file),parser).getroot()):
            targets=tree.xpath(op.get('xpath'))
            if file.parent.parent==MOD:check(bool(targets),'XPath resolves '+op.get('xpath'))
            for target in targets:
                if op.tag=='remove':target.getparent().remove(target)
                elif op.tag=='append':
                    for child in op:target.append(deepcopy(child))
                elif op.tag in ('insertBefore','insertAfter'):
                    index=target.getparent().index(target)+(op.tag=='insertAfter')
                    for child in op:target.getparent().insert(index,deepcopy(child));index+=1
                elif op.tag=='setattribute':target.set(op.get('name'),op.text or '')
                elif op.tag=='removeattribute':target.attrib.pop(op.get('name'),None)
                elif op.tag=='set':
                    if isinstance(target,E._ElementUnicodeResult) and target.is_attribute:target.getparent().set(target.attrname,op.text or '')
                    else:target.text=op.text or ''
                else:raise AssertionError('Unhandled operation '+op.tag)
    return tree

items=merged('items.xml');mods=merged('item_modifiers.xml');recipes=merged('recipes.xml');progression=merged('progression.xml');buffs=merged('buffs.xml')
name='modPZAECWristShower'
item=mods.xpath('/item_modifiers/item_modifier[@name=$name]',name=name)
check(len(item)==1,'Unique module ID')
item=item[0]
check(item.get('installable_tags')=='armorHands','Hands only')
icon=item.find("property[@name='CustomIcon']").get('value')
check((ROOT.parent/'Data/ItemIcons'/f'{icon}.png').is_file(),'Native icon exists')
recipe=recipes.xpath('/recipes/recipe[@name=$name]',name=name)
check(len(recipe)==1,'Unique recipe');recipe=recipe[0]
check(recipe.get('craft_area')=='workbench' and recipe.get('craft_time')=='120','Workbench 120 seconds')
check('learnable' in recipe.get('tags'),'Recipe requires unlock')
for ingredient in recipe:
    check(bool(items.xpath('/items/item[@name=$name]',name=ingredient.get('name'))),'Ingredient exists '+ingredient.get('name'))
check({i.get('name'):int(i.get('count')) for i in recipe}==dict(resourceForgedSteel=10,resourceMechanicalParts=8,resourceElectricParts=8,resourceScrapPolymers=20,resourceMetalPipe=4,resourceDuctTape=5),'Approved materials and quantities')
unlock=progression.xpath("/progression/crafting_skills/crafting_skill[@name='craftingArmor']/effect_group/passive_effect[@name='RecipeTagUnlocked' and @tags=$name]",name=name)
check(len(unlock)==1 and unlock[0].get('level')=='50,100','Armor crafting 50 unlock survives patch stack')
for name in ['buffPZAECShowerController','buffPZAECShowerActive','buffPZAECShowerEmpty']:
    check(len(buffs.xpath('/buffs/buff[@name=$name]',name=name))==1,'Unique buff '+name)
for effect in E.parse(str(MOD/'Config/buffs.xml')).xpath('//triggered_effect[@buff]'):
    check(bool(buffs.xpath('/buffs/buff[@name=$name]',name=effect.get('buff'))),'Referenced buff exists')
controller=buffs.xpath("/buffs/buff[@name='buffPZAECShowerController']")[0]
for name in ['buffPZAECShowerActive','buffPZAECShowerEmpty']:
    check(buffs.xpath('/buffs/buff[@name=$name]',name=name)[0].get('hidden')=='true','Frequent status is silent '+name)
notice=buffs.xpath("/buffs/buff[@name='buffPZAECShowerRefilled']")
check(len(notice)==1 and notice[0].get('hidden')!='true' and notice[0].find('duration').get('value')=='4','One short visible refill notice')
check(controller.find('update_rate').get('value')=='1','One-second controller')
check((MOD/'PZAEC.PortableShower.dll').stat().st_size>0,'Runtime DLL installed')
with (MOD/'Config/Localization.csv').open(encoding='utf-8') as f:
    rows=list(csv.DictReader(f))
check(all(row.get('schinese') and row.get('english') and None not in row for row in rows),'Chinese and English strings valid')
print(f'PASS {checks} merged configuration, recipe, icon and localization checks.')

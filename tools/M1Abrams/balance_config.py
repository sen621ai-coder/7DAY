"""Four fixed tiers; invoked after the common prototype configuration is generated."""
from pathlib import Path
import copy,csv,xml.etree.ElementTree as ET

def apply(root):
    mod=root/'ZZ-PZAEC_M1Abrams';cfg=mod/'Config'
    def prop(n,k,v):
        p=n.find(f"property[@name='{k}']")
        if p is None:p=ET.SubElement(n,'property',name=k)
        p.set('value',str(v))
    def write(name,n):
        ET.indent(n);ET.ElementTree(n).write(cfg/name,encoding='utf-8',xml_declaration=True)
    def name(i):return 'vehicleM1Abrams'+('' if i==0 else f'T{16+i}')
    health=[1000000,1500000,2200000,3200000]
    entities=ET.parse(cfg/'entityclasses.xml').getroot();vehicles=ET.parse(cfg/'vehicles.xml').getroot();items=ET.parse(cfg/'items.xml').getroot()
    base_e=copy.deepcopy(entities.find('./append/entity_class'));base_v=copy.deepcopy(vehicles.find('./append/vehicle'));base_i=copy.deepcopy(items.find('./append/item'))
    entities.find('append').clear();entities.find('append').set('xpath','/entity_classes')
    vehicles.find('append').clear();vehicles.find('append').set('xpath','/vehicles')
    items.find('append').clear();items.find('append').set('xpath','/items')
    loc=[]
    for i in range(4):
        n=name(i);itemname=n+'Placeable';tier=16+i
        e=copy.deepcopy(base_e);e.set('name',n);entities.find('append').append(e)
        v=copy.deepcopy(base_v);v.set('name',n);vehicles.find('append').append(v)
        for k,val in {'recipeName':itemname,'velocityMax_turbo':', '.join(f'{s/3.6:.6f}' for s in ([36,38,40,42][i],[14,15,16,18][i],[48,50,52,54][i],[14,15,16,18][i])),
                      'motorTorque_turbo':', '.join(str(round(t*(1+i*.10))) for t in [18000,12000,24000,15000]),'m1Horsepower':[1500,1650,1800,2000][i]}.items():prop(v,k,val)
        prop(v.find("property[@class='fuelTank']"),'capacity',500+50*i)
        prop(v.find("property[@class='engine']"),'fuelKmPerL',f'{[18,19,20,21][i]/(500+50*i):.7f}')
        item=copy.deepcopy(base_i);item.set('name',itemname);items.find('append').append(item)
        prop(item,'DescriptionKey',itemname+'Desc');prop(item,'Stacknumber',1)
        prop(item,'RepairTools','pzM1RepairKit');prop(item,'EconomicValue',100000*(i+1));prop(item,'Tags','vehicle,vengine,vfuel,PZAECM1')
        if i:prop(item,'UnlockedBy','')
        prop(item.find("property[@class='Action1']"),'Vehicle',n)
        item.find("effect_group/passive_effect[@name='DegradationMax']").set('value',str(health[i]))
        item.find("effect_group/passive_effect[@name='ModSlots']").set('value','0')
        loc += [[n,'entityclasses','Vehicle','','',f'M1 Abrams T{tier}',f'M1艾布拉姆斯 T{tier}'],[itemname,'items','Vehicle','','',f'M1 Abrams T{tier}',f'M1艾布拉姆斯 T{tier}'],
                [itemname+'Desc','items','Vehicle','','',f'T{tier} tank. HE/AP in cargo. R switches shell. Hold G outside for cargo repair kit.',f'T{tier}主战坦克，耐久{health[i]//10000}万。货仓装填HE/AP；左键开火，右键瞄准，R切弹。车外对准坦克按住G维修，维修包放货仓。升级前满修、清空燃油、拆下改装并收起车辆。']]
    for n,title,icon in [('pzM1Shell','M1高爆榴弹 HE','ammoRocketHE'),('pzM1ShellAP','M1穿甲弹 AP','ammoRocketFrag'),('pzM1RepairKit','M1装甲维修包','resourceRepairKit')]:
        item=ET.SubElement(items.find('append'),'item',name=n)
        for k,val in {'Extends':'resourceForgedSteel','CustomIcon':icon,'DescriptionKey':n+'Desc','Stacknumber':20,'EconomicValue':500,'Group':'Ammo/Weapons'}.items():prop(item,k,val)
        desc='放入M1货仓使用。HE范围清群，AP单体穿甲；伤害取决于整车型号。' if n!='pzM1RepairKit' else '放入坦克货仓。车外对准坦克按住G 8秒；停车停火且10秒内未受伤，每包将坦克耐久完全修满。受击或松开中断不扣包；满耐久不消耗。'
        loc.extend([[n,'items','Item','','',n,title],[n+'Desc','items','Item','','',desc,desc]])
    for filename,node in [('entityclasses.xml',entities),('vehicles.xml',vehicles),('items.xml',items)]:write(filename,node)
    recipes=ET.Element('configs');ap=ET.SubElement(recipes,'append',xpath='/recipes')
    def recipe(n,count,seconds,ingredients,locked=False):
        r=ET.SubElement(ap,'recipe',name=n,count=str(count),craft_area='workbench',craft_time=str(seconds),use_ingredient_modifier='false',tags='learnable,workbenchCrafting' if locked else 'workbenchCrafting')
        if not locked:r.set('always_unlocked','true')
        for key,num in ingredients:ET.SubElement(r,'ingredient',name=key,count=str(num))
    for i in range(4):
        tier=16+i
        ingredients=[('vehicleTruck4x4Placeable' if i==0 else name(i-1)+'Placeable',1),(f'PZAECBuildPartsR{i+2}',[12,12,15,18][i]),
            (f'resourcePZAECMutantHeartT{tier}',[3,3,4,5][i]),(f'resourcePZAECSiegeCapacitorT{tier}',[6,6,8,10][i]),
            ('resourceLegendaryParts',[30,40,60,90][i]),('resourceForgedSteel',[1200,600,800,1000][i]),('resourceDurablAlloys',[50,100,150,200][i]),
            ('resourceMechanicalParts',[300,150,200,250][i]),('resourceElectricParts',[200,100,150,200][i]),('resourceSpring',[120,60,80,100][i])]
        if i==0:ingredients.append(('smallEngine',2))
        recipe(name(i)+'Placeable',1,[1200,720,900,1080][i],ingredients,i==0)
    recipe('pzM1Shell',10,90,[('resourceRocketCasing',10),('resourceRocketTip',10),('resourceForgedSteel',40),('resourceGunPowder',400),('resourceMechanicalParts',10)])
    recipe('pzM1ShellAP',10,120,[('resourceRocketCasing',10),('resourceForgedSteel',100),('resourceScrapLead',200),('resourceGunPowder',600),('resourceMechanicalParts',20)])
    recipe('pzM1RepairKit',1,30,[('resourceForgedSteel',20),('resourceDurablAlloys',2),('resourceMechanicalParts',5),('resourceDuctTape',2)])
    write('recipes.xml',recipes)
    with (cfg/'Localization.csv').open('w',encoding='utf8',newline='') as f:
        w=csv.writer(f);w.writerow(['Key','File','Type','UsedInMainMenu','NoTranslate','english','schinese']);w.writerows(loc)
    info=ET.parse(mod/'ModInfo.xml');info.find('Version').set('value','0.2.3');info.write(mod/'ModInfo.xml',encoding='utf8',xml_declaration=True)

if __name__=='__main__':apply(Path(__file__).resolve().parents[2])

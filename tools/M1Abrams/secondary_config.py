"""Idempotent secondary-weapons configuration; run after base/module generators."""
from pathlib import Path
import csv,xml.etree.ElementTree as E
def apply(root):
 mod=Path(root)/'ZZ-PZAEC_M1Abrams';cfg=mod/'Config'
 def write(name,node):E.indent(node);E.ElementTree(node).write(cfg/name,encoding='utf-8',xml_declaration=True)
 specs=[('pzM1MGBelt','ammo762mmBulletBall',100,'M1机枪弹链','100发弹链；放入M1货仓。打空自动换链4秒，已装余弹随车辆保存。',1,[('resourceBulletTip',100),('resourceBulletCasing',100),('resourceGunPowder',200),('resourceMechanicalParts',2)]),('pzM1AAMissile','pzM1AAMissile',20,'M1防空导弹','仅锁定40～600米飞行敌人；1.5秒锁定，每管12秒冷却，忽略50%物理护甲。放入M1货仓。',2,[('resourceRocketCasing',2),('resourceRocketTip',2),('resourceForgedSteel',10),('resourceElectricParts',4),('resourceGunPowder',100)])]
 items=E.parse(cfg/'items.xml').getroot();recipes=E.parse(cfg/'recipes.xml').getroot()
 for name,icon,stack,title,desc,count,ingredients in specs:
  for rootnode,tag in [(items,'item'),(recipes,'recipe')]:
   for parent in rootnode:
    for node in list(parent):
     if node.tag==tag and node.get('name')==name:parent.remove(node)
  n=E.SubElement(items.find('append'),'item',name=name)
  for key,value in [('Extends','resourceForgedSteel'),('Tags','PZAECM1Ammo'),('CustomIcon',icon),('DescriptionKey',name+'Desc'),('Stacknumber',str(stack)),('EconomicValue','300'),('Group','Ammo/Weapons')]:E.SubElement(n,'property',name=key,value=value)
  n=E.SubElement(recipes.find('append'),'recipe',name=name,count=str(count),craft_area='workbench',craft_time='30',use_ingredient_modifier='false',tags='learnable,workbenchCrafting')
  for resource,amount in ingredients:E.SubElement(n,'ingredient',name=resource,count=str(amount))
 write('items.xml',items);write('recipes.xml',recipes)
 progression=E.parse(cfg/'progression.xml').getroot();effect=progression.find('./append/passive_effect');tags=effect.get('tags').split(',')
 for spec in specs:
  if spec[0] not in tags:tags.append(spec[0])
 effect.set('tags',','.join(tags));write('progression.xml',progression)
 sounds=E.parse(cfg/'sounds.xml').getroot()
 for name,noise,heat in [('pzM1MGNoise','45','.015'),('pzM1AANoise','65','.5')]:
  parent=sounds.find('append')
  for n in list(parent):
   if n.get('name')==name:parent.remove(n)
  n=E.SubElement(parent,'SoundDataNode',name=name);E.SubElement(n,'Noise',ID='0',noise=noise,time='1',muffled_when_crouched='1',heat_map_strength=heat,heat_map_time='120');E.SubElement(n,'NoiseScale',value='1')
 write('sounds.xml',sounds)
 loc=cfg/'Localization.csv'
 with loc.open(encoding='utf-8-sig',newline='') as f:rows=list(csv.reader(f))
 replace={s[0]:s[3] for s in specs};replace.update({s[0]+'Desc':s[4] for s in specs})
 replace['modPZAECM1StabilizerDesc']='炮塔、炮管、机枪与导弹架追踪速度+50%；机枪散布降低25%；主炮车体反冲降低60%。不提高导弹制导能力。可与M1辅助装填同装。'
 replace['modPZAECM1AutoloaderDesc']='主炮装填/切弹、机枪换链、每管导弹冷却降低25%（机枪3秒、导弹9秒）。不降低热量或锁定时间。可与M1稳定火控同装。'
 rows=[r for r in rows if r[0] not in replace]
 for key,value in replace.items():rows.append([key,'items','Item','','',value,value])
 with loc.open('w',encoding='utf-8',newline='') as f:csv.writer(f).writerows(rows)
 info=E.parse(mod/'ModInfo.xml');info.find('Version').set('value','0.4.17');info.find('Description').set('value','M1 cannon, independent roof MG and lock-on AA; dual-seat authority, cargo ammunition and saved weapon state.');info.write(mod/'ModInfo.xml',encoding='utf-8',xml_declaration=True)
if __name__=='__main__':apply(Path(__file__).resolve().parents[2])

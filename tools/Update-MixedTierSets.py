"""Keep tier-specific bonus strengths while counting a family across tiers."""
from pathlib import Path
import re,csv,io
from lxml import etree as E
ROOT=Path(__file__).resolve().parents[1]
MOD=ROOT/'99-AEC_T16_RuntimeFix'
FAMILIES=('Harrier','Storm','Tremor','Warden')
path=MOD/'Config/buffs.xml'
text=path.read_text(encoding='utf8')
text=re.sub(r'\$PZAEC(Harrier|Storm|Tremor|Warden)T(?:16|17|18|19)Resonance',r'$PZAEC\1Resonance',text)
begin='<!-- BEGIN MIXED TIER FAMILY SETS -->';end='<!-- END MIXED TIER FAMILY SETS -->'
text=re.sub(re.escape(begin)+r'.*?'+re.escape(end),'',text,flags=re.S)
patch=E.Element('configs')
for family in FAMILIES:
 for tier in range(16,20):
  stem=f'buffPZAEC{family}T{tier}'
  # Old tier Set3 removal must not erase the shared charge during an upgrade.
  op=E.SubElement(patch,'append',xpath=f"/buffs/buff[@name='{stem}Set3']/effect_group/triggered_effect[@trigger='onSelfBuffRemove' and @action='RemoveCVar']")
  E.SubElement(op,'requirement',name='ArmorGroupCount',group_name=f'groupPZAEC{family}',operation='LTE',value='2')
  # Native buff refresh is periodic. Invalidate old-tier effects immediately,
  # keeping on-remove cleanup free to run and preventing temporary double buffs.
  for suffix,pieces in [('Set2',2),('Set3',3),('Active',4)]:
   xp=f"/buffs/buff[@name='{stem}{suffix}']/effect_group/"
   op=E.SubElement(patch,'append',xpath=xp+"passive_effect | "+xp+"triggered_effect[not(@trigger='onSelfBuffRemove' or @trigger='onSelfBuffFinish' or @trigger='onSelfDied' or @trigger='onSelfLeaveGame')]")
   E.SubElement(op,'requirement',name='ArmorGroupCount',group_name=f'groupPZAEC{family}T{tier}',operation='GTE',value=str(pieces))
block='\n'+begin+'\n'+''.join(E.tostring(n,encoding='unicode',pretty_print=True) for n in patch)+end+'\n'
path.write_text(text[:text.rfind('</configs>')].rstrip()+block+'</configs>\n',encoding='utf8')

path=MOD/'Config/Localization.csv';rows=list(csv.reader(io.StringIO(path.read_text(encoding='utf-8-sig'))));header=rows[0]
note='同系列T16–T19可混穿，套装加成按所穿最低阶生效；共鸣与冷却按系列共享。'
for row in rows[1:]:
 key=row[0] if row else ''
 if (re.fullmatch(r'armorPZAEC(Harrier|Storm|Tremor|Warden)(Helmet|Outfit|Gloves|Boots)T(16|17|18|19)Desc',key)
     or re.fullmatch(r'buffPZAEC(Harrier|Storm|Tremor|Warden)ResonanceDesc',key)
     or key=='itemPZAECResonanceInjectorDesc'):
  for lang in ['schinese','tchinese']:
   i=header.index(lang)
   if i<len(row):
    row[i]=row[i].replace('同阶三件套','同系列三件套').replace('同阶四件套','同系列四件套')
    if note not in row[i]:row[i]+='\\n'+note
out=io.StringIO(newline='');csv.writer(out,lineterminator='\n').writerows(rows);path.write_text(out.getvalue(),encoding='utf8')
path=ROOT/'tools/Build-EquipmentCatalog.py'
path.write_text(path.read_text(encoding='utf8').replace('有同阶三件套时共鸣','有同系列三件套（可跨阶混穿）时共鸣'),encoding='utf8')
print('Updated four family sets: cross-tier count, shared charge, guarded cleanup and descriptions.')

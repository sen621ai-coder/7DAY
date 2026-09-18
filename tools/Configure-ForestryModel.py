"""Install the forestry-only XML definition after the mesh and runtime build pass."""
from pathlib import Path
from copy import deepcopy
import re
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]
path=ROOT/'98-AECxProjectZ_Tweaks/Config/blocks.xml'
text=path.read_text(encoding='utf-8-sig')
tree=ET.fromstring(text)
existing=tree.find(".//block[@name='yfAutoForestry']")
assert existing is not None
base=ET.parse(ROOT/'01-ProjectZ/Config/blocks.xml').find(".//block[@name='AutoMinerIron']")
assert base is not None
block=deepcopy(base); block.set('name','yfAutoForestry')
for prop in existing:
    if prop.tag!='property' or not prop.get('name') or prop.get('name')=='Extends': continue
    old=block.find("property[@name='%s']"%prop.get('name'))
    if old is not None: block.remove(old)
    block.append(deepcopy(prop))
# Explicit Collector definition avoids inheriting backhoe imposter IDs or
# model-specific transform names. All production/repair/drop rules are retained.
for name in ('Extends','ImposterExchange','TintColor'):
    for old in block.findall("property[@name='%s']"%name): block.remove(old)
values={
 'Model':'yfAutoForestryRuntime.prefab','ModelOffset':'0,0,0',
 'MultiBlockDim':'10,4,6','OversizedBounds':'(-0.5,1.9,-0.5),(10,4.8,6)',
 'CustomIcon':'yfAutoForestry','CustomIconTint':'FFFFFF',
 'ItemTypeIcon':'farming','ModTransformEnableNames':'ForestrySpeed,ForestryPacker,ForestrySiren',
 'ModTransformDisableNames':',,',
}
for name,value in values.items():
    old=block.find("property[@name='%s']"%name)
    if old is None: old=ET.SubElement(block,'property',{'name':name})
    old.set('value',value)
ET.indent(block,space='  ',level=2)
replacement=ET.tostring(block,encoding='unicode').strip()
text,count=re.subn(r'<block name="yfAutoForestry">.*?</block>',lambda _:replacement,text,count=1,flags=re.S)
assert count==1
path.write_text(text,encoding='utf-8',newline='\n')
print('Configured forestry only: standalone Collector, 10x6 footprint, original production and upgrade items.')

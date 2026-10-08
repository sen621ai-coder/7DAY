"""Nu asset/config integrity checks; all source geometry retained plus two authored saber surfaces."""
import argparse,json,struct,hashlib,xml.etree.ElementTree as ET
from pathlib import Path
import numpy as np
p=argparse.ArgumentParser();p.add_argument('--mod-root',required=True);a=p.parse_args();root=Path(a.mod_root);d=root/'Resources/Nu';m=json.loads((d/'nu.json').read_text());b=(d/'nu.bin').read_bytes();checks=0
def check(ok,label):
 global checks
 assert ok,label
 checks+=1;print('PASS',label)
check(hashlib.sha256((d/'nu.glb').read_bytes()).hexdigest()==m['sourceSha256'],'immutable GLB hash')
check(len(m['bones'])==124 and len({x['originalNode'] for x in m['bones'] if x['originalNode']>=0})==123,'123 original joints plus gameplay beam')
check(all(x['parent']<i for i,x in enumerate(m['bones'])),'parent precedes child')
check(len({x['name'] for x in m['bones']})==124,'unique canonical joint names')
check(m['sourceTriangles']==115814,'source triangle count')
check(sum(x['indices']//6 for x in m['parts'])==115814+48,'all source triangles plus saber, explicit double sided')
check(.1<m['soleWidth']<.6 and .4<m['soleDepth']<1.2,'measured foot dimensions')
check(all(x in m['sockets'] for x in ['FootL','FootR']),'both sole anchors')
check(np.isfinite(np.array(m['bindposes'])).all(),'finite bind matrices')
for part in m['parts']:
 at=part['offset'];v=[]
 for i in range(part['vertices']):
  row=struct.unpack_from('<8f4i4f',b,at+i*64);assert np.isfinite(row).all();assert min(row[8:12])>=0 and max(row[8:12])<124;assert abs(sum(row[12:])-1)<1e-4;v.append(row[:3])
 ix=np.frombuffer(b,dtype='<i4',count=part['indices'],offset=at+part['vertices']*64);assert ix.min()>=0 and ix.max()<len(v);v=np.array(v);assert np.ptp(v,axis=0).max()<5
check(True,'all vertex weights, indices and geometry bounds')
for c in m['clips']:
 frames=np.frombuffer(b,dtype='<f4',count=c['frames']*124*10,offset=c['offset']).reshape(c['frames'],124,10);assert np.isfinite(frames).all();assert abs(np.linalg.norm(frames[:,:,3:7],axis=2)-1).max()<1e-4
check(True,'animation quaternion normalization and track bounds')
check({'00-IDLE','01-SABER','02-RIFLE','03-SHILD','Walk','Reverse','TurnLeft','TurnRight'}<=set(c['name'] for c in m['clips']),'complete gameplay motion track set')
for material in m['materials']:
 for key in ['albedo','normal','mask']:
  if material.get(key):assert (d/material[key]).is_file()
check(True,'all referenced textures exist')
for file,node,name in [('entityclasses','entity_class','vehiclePZAECMechaUltimate'),('vehicles','vehicle','vehiclePZAECMechaUltimate'),('items','item','vehiclePZAECMechaUltimatePlaceable'),('recipes','recipe','vehiclePZAECMechaUltimatePlaceable')]:
 tree=ET.parse(root/'Config'/f'{file}.xml');check(len(tree.findall(f'.//{node}[@name="{name}"]'))==1,file+' ultimate registration');check(tree.find(f'.//{node}[@name="'+name.replace('vehiclePZAECMechaUltimate','vehicleCombatRobotComplete')+'"]') is not None,file+' complete preserved')
check('初号机（究极体）' in (root/'Config/Localization.csv').read_text(encoding='utf-8-sig'),'display name')
check('vehiclePZAECMechaUltimatePlaceable' in (root/'Config/progression.xml').read_text(encoding='utf-8-sig'),'recipe unlock')
from PIL import Image
icon=Image.open(root/'ItemIcons/vehiclePZAECMechaUltimatePlaceable.png')
check(icon.size==(256,256) and icon.mode=='RGBA' and icon.getchannel('A').getextrema()==(0,255),'native rendered transparent inventory icon')
check((root/'UIAtlases/ItemIconAtlas/vehiclePZAECMechaUltimatePlaceable.png').read_bytes()==(root/'ItemIcons/vehiclePZAECMechaUltimatePlaceable.png').read_bytes(),'ultimate icon deployed to game item atlas')
print('Nu checks passed:',checks)

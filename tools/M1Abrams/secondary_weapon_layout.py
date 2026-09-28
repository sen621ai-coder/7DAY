"""Model-space secondary weapon layout study; does not alter live assets."""
from pathlib import Path
import json
import numpy as np
from PIL import Image, ImageDraw, ImageFont
root=Path(__file__).resolve().parent;out=root/'Generated';meta=json.loads((out/'parts.json').read_text());data=np.load(out/'parts.npz')
# Model coordinates: X right, Y up, Z forward. Candidate geometry, not final production meshes.
boxes=[('MG pedestal',[-.78,2.20,-.08],[-.32,2.65,.38],'#28d7c6'),('MG receiver',[-.71,2.58,.08],[-.39,2.84,.65],'#28d7c6'),('MG barrel',[-.59,2.66,.65],[-.51,2.74,1.45],'#28d7c6'),('SAM cradle',[.85,2.12,-1.95],[1.45,2.43,-.75],'#ffb454'),('SAM tube A',[.88,2.43,-2.05],[1.12,2.67,-.45],'#ffb454'),('SAM tube B',[1.18,2.43,-2.05],[1.42,2.67,-.45],'#ffb454')]
im=Image.new('RGB',(1800,1050),'#101b29');draw=ImageDraw.Draw(im)
font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',23);small=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',18);large=ImageFont.truetype('C:/Windows/Fonts/arialbd.ttf',34)
draw.text((45,25),'M1 SECONDARY WEAPONS / CANDIDATE LAYOUT',font=large,fill='white')
draw.text((45,85),'TEAL: roof machine gun     |     AMBER: twin surface-to-air launcher',font=font,fill='#d1dfec')
views=[('TOP: +Z forward',0,2,1),('SIDE: +Z forward',2,1,0),('FRONT: +X right',0,1,2)]
allv=np.concatenate([data[f'{i}_v'] for i in range(len(meta))])
for column,(title,u,v,depth) in enumerate(views):
 left=column*600+25;top=170;w=550;h=700;low=allv[:,[u,v]].min(0);high=allv[:,[u,v]].max(0);scale=min(w/(high[0]-low[0]),h/(high[1]-low[1]))*.9
 center=(low+high)/2
 def screen(point):return (left+w/2+(point[0]-center[0])*scale,top+h/2-(point[1]-center[1])*scale)
 polys=[]
 for i,part in enumerate(meta):
  triangles=data[f'{i}_v'][data[f'{i}_f']]
  for tri in triangles:polys.append((float(tri[:,depth].mean()),tri[:,[u,v]],'#64798a' if part['name']=='Turret' else '#354959'))
 for _,tri,col in sorted(polys,key=lambda x:x[0]):draw.polygon([screen(t) for t in tri],fill=col)
 for name,lo,hi,col in boxes:
  p0=screen([lo[u],hi[v]]);p1=screen([hi[u],lo[v]]);draw.rectangle([p0,p1],fill=col,outline='white',width=1)
 draw.text((left,135),title,font=font,fill='white')
 draw.text((left,900),'Projected axes: '+'XYZ'[u]+' / '+'XYZ'[v]+'; dimensions in metres',font=small,fill='#9cb2c8')
 draw.line([(left,940),(left+scale,940)],fill='white',width=3);draw.text((left,949),'1 m',font=small,fill='white')
draw.text((45,1005),'Existing mesh + candidate volumes. Neutral pose only; full motion clearance NOT verified.',font=font,fill='#d1dfec')
im.save(out/'secondary-weapons-layout.png')
anchors=[('RoofMGBase','TurretYaw',[-.55,2.20,.15]),('RoofMGYaw','RoofMGBase',[-.55,2.65,.15]),('RoofMGPitch','RoofMGYaw',[-.55,2.70,.25]),('RoofMGMuzzle','RoofMGPitch',[-.55,2.70,1.45]),('AAMount','TurretYaw',[1.15,2.12,-1.30]),('AAPitch','AAMount',[1.15,2.43,-1.30]),('AAMuzzleL','AAPitch',[1.00,2.55,-.45]),('AAMuzzleR','AAPitch',[1.30,2.55,-.45])]
(out/'secondary-weapons-anchors.candidate.json').write_text(json.dumps({'status':'design_only_not_runtime','coordinates':'model-space metres, X right Y up Z forward; positions are absolute in neutral pose, not parent-local offsets','clearance':'not verified','nodes':[dict(name=n,parent=p,position=v) for n,p,v in anchors],'boundingVolumes':[dict(name=n,min=lo,max=hi) for n,lo,hi,c in boxes]},indent=2),encoding='utf-8')
print('Generated candidate anchors and three-view layout. Live model/config unchanged.')

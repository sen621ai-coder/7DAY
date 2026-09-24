"""Generate distinct automation inventory pictograms; Pillow, no external artwork."""
from pathlib import Path
import math
import re
import xml.etree.ElementTree as ET
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
MOD = ROOT / '97-AutomationWorkshop'
OUT = MOD / 'UIAtlases/ItemIconAtlas'
S = 4
INK = '#152330'
WHITE = '#EEF6FA'
GRAY = '#8297A8'
LABELS = {
'yfAutomationWorkbench':'设备制造台','yfAutoInput':'输入箱','yfAutoOutput':'输出箱',
'yfAutoSorter':'过滤分拣机','yfAutoRouter':'三路分拣箱','yfAutoPowerPort':'供电接口',
'yfAutoKitchen':'自动厨房','yfAutoSmelter':'自动冶炼机','yfAutoForge':'自动锻造机',
'yfAutoWorkbench':'自动制作台','yfAutoChemistry':'化学合成台','yfAutoRecycler':'自动分解机',
'yfAutoFarm':'自动农场','yfAutoMiner':'自动采矿机','yfAutoTransfer':'箱间输送机',
'yfAutoWaterPump':'自动水泵','yfAutoWaterTank':'储水罐','yfAutoAmmoFeed':'弹药供给器',
'yfAutoBeltStraight':'直行带','yfAutoBeltLeft':'左转带','yfAutoBeltRight':'右转带',
'yfAutoBeltUp':'上坡带','yfAutoBeltDown':'下坡带','yfAutoBeltMerge':'T 字合流带',
'yfCargoHub':'货运枢纽','yfCargoEntranceBeacon':'入口信标','yfCargoRecoveryCrate':'回收货箱'}

class Icon:
    def __init__(self):
        self.im=Image.new('RGBA',(160*S,160*S))
        self.d=ImageDraw.Draw(self.im)
    def pts(self,p):return [(round(x*S),round(y*S)) for x,y in p]
    def line(self,p,c=WHITE,w=8):
        self.d.line(self.pts(p),fill=c,width=w*S,joint='curve')
        r=w*S/2
        for x,y in self.pts(p):self.d.ellipse((x-r,y-r,x+r,y+r),fill=c)
    def poly(self,p,c,outline=INK,w=4):
        self.d.polygon(self.pts(p),fill=c)
        if outline:self.line(p+[p[0]],outline,w)
    def rect(self,b,c,outline=INK,w=4,r=5):
        self.d.rounded_rectangle(tuple(int(v*S) for v in b),radius=r*S,fill=c,outline=outline,width=w*S)
    def ellipse(self,b,c,outline=INK,w=4):
        self.d.ellipse(tuple(int(v*S) for v in b),fill=c,outline=outline,width=w*S)
    def arrow(self,p,c=WHITE,w=9,head=16):
        self.line(p,INK,w+6);self.line(p,c,w)
        x,y=p[-1];px,py=p[-2];a=math.atan2(y-py,x-px)
        points=[(x,y),(x-head*math.cos(a)+head*.65*math.sin(a),y-head*math.sin(a)-head*.65*math.cos(a)),(x-head*math.cos(a)-head*.65*math.sin(a),y-head*math.sin(a)+head*.65*math.cos(a))]
        self.poly(points,c,w=3)
    def gear(self,x,y,r,c):
        p=[]
        for i in range(40):
            a=i*math.pi/20;rr=r if i%4 in (1,2) else r*.79;p.append((x+rr*math.cos(a),y+rr*math.sin(a)))
        self.poly(p,c);self.ellipse((x-r*.34,y-r*.34,x+r*.34,y+r*.34),INK)
    def crate(self,x=31,y=50,c='#4E9CBE'):
        self.rect((x,y,x+94,y+78),c);self.rect((x+8,y+9,x+86,y+68),'#263D4D',w=3)
        self.line([(x+13,y+15),(x+80,y+60)],GRAY,5);self.line([(x+80,y+15),(x+13,y+60)],GRAY,5)
    def finish(self):return self.im.resize((160,160),Image.Resampling.LANCZOS)

def make(name):
    q=Icon();blue='#61CBE8';green='#7DDB9C';gold='#F7C858';orange='#FF9956';purple='#C7A0EF'
    if name.startswith('yfAutoBelt'):
        kind=name.removeprefix('yfAutoBelt')
        if kind in ('Up','Down'):
            q.poly([(20,123),(20,104),(52,104),(52,81),(83,81),(83,58),(115,58),(115,35),(140,35),(140,133),(20,133)],'#405969')
            q.arrow([(30,104),(125,29)] if kind=='Up' else [(30,29),(125,104)],blue if kind=='Up' else orange,11,21)
        elif kind=='Merge':
            q.line([(23,85),(137,85)],INK,34);q.line([(80,85),(80,25)],INK,34)
            q.line([(23,85),(137,85)],GRAY,25);q.line([(80,85),(80,25)],GRAY,25)
            q.arrow([(21,85),(64,85)],gold,7,12);q.arrow([(139,85),(96,85)],gold,7,12);q.arrow([(80,85),(80,22)],WHITE,9,17)
        else:
            paths={'Straight':[(80,137),(80,24)],'Left':[(105,137),(105,65),(26,65)],'Right':[(55,137),(55,65),(134,65)]}
            p=paths[kind];q.line(p,INK,42);q.line(p,GRAY,33);q.line(p,'#273D4A',24);q.arrow(p,gold,9,21)
    elif name in ('yfAutoInput','yfAutoOutput','yfCargoRecoveryCrate'):
        q.crate(c=blue if name=='yfAutoInput' else green if name=='yfAutoOutput' else gold)
        if name=='yfAutoInput':q.arrow([(78,19),(78,87)],blue,12,21)
        elif name=='yfAutoOutput':q.arrow([(78,85),(78,18)],green,12,21)
        else:q.arrow([(49,76),(49,27),(112,27),(112,71)],gold,7,15)
    elif name=='yfAutoSorter':
        q.poly([(22,35),(138,35),(96,85),(96,128),(64,141),(64,85)],gold)
        for x,c in [(40,blue),(80,green),(120,orange)]:q.rect((x-9,13,x+9,29),c,w=3)
        q.line([(44,47),(116,47)],INK,5)
    elif name=='yfAutoRouter':
        q.rect((51,65,109,113),'#526877');q.arrow([(80,143),(80,98)],WHITE,8,14)
        q.arrow([(55,84),(17,84)],blue,9,15);q.arrow([(105,84),(143,84)],green,9,15);q.arrow([(80,67),(80,17)],gold,9,15)
    elif name=='yfAutoPowerPort':
        q.rect((35,47,123,119),'#485A69');q.rect((47,119,112,137),GRAY)
        q.line([(56,23),(56,48)],WHITE,10);q.line([(102,23),(102,48)],WHITE,10)
        q.poly([(87,50),(59,91),(79,91),(69,119),(104,76),(83,76)],gold,w=3)
    elif name=='yfAutoKitchen':
        q.rect((32,66,128,124),orange,r=20);q.line([(21,74),(139,74)],WHITE,8)
        q.line([(32,63),(128,63)],GRAY,8);q.rect((66,49,94,60),WHITE,r=4)
        for x in (53,80,107):q.line([(x,38),(x-6,28),(x+2,16)],WHITE,5)
        q.rect((40,128,121,139),'#485969')
    elif name=='yfAutoSmelter':
        q.poly([(28,50),(124,50),(112,126),(46,126)],GRAY);q.line([(33,50),(119,50)],WHITE,10)
        q.poly([(56,97),(54,71),(79,25),(88,60),(108,39),(106,88),(86,110)],orange)
        q.poly([(73,93),(83,64),(94,91),(84,107)],gold,w=2)
        q.rect((36,132,119,143),'#516474')
    elif name=='yfAutoForge':
        q.poly([(21,79),(136,79),(117,100),(91,101),(98,120),(119,128),(119,140),(42,140),(42,129),(63,120),(69,101),(38,101)],GRAY)
        q.line([(90,30),(64,70)],'#B88253',12);q.poly([(84,15),(126,39),(115,58),(73,34)],orange)
    elif name in ('yfAutoWorkbench','yfAutomationWorkbench'):
        q.rect((24,87,136,103),blue);q.rect((32,102,46,141),GRAY);q.rect((114,102,128,141),GRAY)
        if name=='yfAutomationWorkbench':q.gear(80,49,34,gold)
        else:
            q.line([(57,75),(100,29)],GRAY,14);q.poly([(88,20),(109,16),(104,33),(117,43),(132,32),(125,56),(107,58)],WHITE,w=3)
            q.line([(39,32),(79,74)],orange,10);q.poly([(30,18),(50,18),(58,30),(45,43),(32,34)],gold,w=3)
    elif name=='yfAutoChemistry':
        q.poly([(61,18),(99,18),(99,64),(133,120),(126,139),(34,139),(27,120),(61,64)],'#426C67')
        q.poly([(47,101),(112,101),(128,126),(122,132),(39,132),(33,126)],green,w=2)
        q.line([(60,19),(100,19)],WHITE,9);q.ellipse((72,70,85,83),green,w=2);q.ellipse((82,45,93,56),green,w=2)
    elif name=='yfAutoRecycler':
        q.arrow([(42,99),(22,69),(53,34),(95,34)],purple,12,21)
        q.arrow([(103,45),(137,79),(114,119),(71,126)],green,12,21)
        q.gear(80,80,23,GRAY)
    elif name=='yfAutoFarm':
        q.poly([(24,108),(80,86),(138,108),(138,130),(80,148),(24,130)],'#8A653E')
        q.line([(80,108),(80,44)],green,9)
        q.poly([(79,77),(51,77),(31,49),(62,48),(79,61)],green)
        q.poly([(82,58),(82,39),(103,21),(134,21),(118,50)],'#A0E67C')
    elif name=='yfAutoMiner':
        q.rect((47,17,113,49),orange);q.rect((59,46,101,112),GRAY)
        for y in (56,73,90):q.line([(56,y+13),(104,y)],WHITE,7)
        q.poly([(59,111),(101,111),(80,140)],orange);q.poly([(20,142),(34,123),(54,140)],GRAY);q.poly([(108,142),(127,121),(141,142)],GRAY)
    elif name=='yfAutoTransfer':
        q.rect((17,57,60,111),blue);q.rect((101,57,144,111),green)
        q.arrow([(35,42),(124,42)],WHITE,9,18);q.arrow([(124,129),(35,129)],gold,9,18)
    elif name=='yfAutoWaterPump':
        q.line([(21,123),(21,88),(57,88)],GRAY,18);q.line([(104,87),(133,87),(133,53)],GRAY,18)
        q.ellipse((43,58,117,132),blue);q.ellipse((65,80,95,110),INK)
        q.poly([(84,14),(105,45),(102,57),(93,66),(76,64),(66,54),(65,44)],blue,w=3)
    elif name=='yfAutoWaterTank':
        q.rect((35,31,125,126),'#466B82',r=10);q.ellipse((35,19,125,49),blue)
        q.rect((44,68,116,116),blue,outline=None);q.line([(47,67),(72,73),(97,67),(116,71)],WHITE,4)
        q.rect((45,129,58,142),GRAY);q.rect((103,129,116,142),GRAY)
    elif name=='yfAutoAmmoFeed':
        for x in (34,67,100):
            q.poly([(x,93),(x,43),(x+11,22),(x+22,43),(x+22,93)],gold,w=3);q.rect((x,73,x+22,99),'#BC8750',w=3)
        q.rect((23,96,137,138),'#5C6A46');q.arrow([(44,118),(119,118)],WHITE,7,14)
    elif name=='yfCargoHub':
        q.crate(42,72,'#9DAFBD')
        q.line([(37,34),(79,52),(122,34)],GRAY,9);q.line([(79,52),(79,79)],GRAY,9)
        q.ellipse((14,20,63,42),blue);q.ellipse((99,20,148,42),blue)
    elif name=='yfCargoEntranceBeacon':
        q.rect((63,91,97,132),GRAY);q.rect((43,129,117,143),GRAY)
        q.line([(80,104),(80,56)],WHITE,9);q.ellipse((69,41,91,63),blue)
        for r in (31,49):
            q.d.arc(((80-r)*S,(53-r)*S,(80+r)*S,(53+r)*S),205,335,fill=blue,width=6*S)
    else:raise ValueError(name)
    return q.finish()

def main():
    OUT.mkdir(parents=True,exist_ok=True)
    icons={name:make(name) for name in LABELS}
    for name,im in icons.items():im.save(OUT/(name+'.png'))
    # Preserve surrounding XML and unrelated concurrent changes.
    path=MOD/'Config/blocks.xml';text=path.read_text(encoding='utf-8-sig')
    for name in LABELS:
        pattern=r'(<block name="'+name+r'">)(.*?)(</block>)'
        def change(m):
            body=m[2]
            value=f'<property name="CustomIcon" value="{name}" />'
            if 'name="CustomIcon"' in body:body=re.sub(r'<property name="CustomIcon" value="[^"]*"\s*/>',value,body)
            else:body='\n      '+value+body
            body=re.sub(r'<property name="CustomIconTint" value="[^"]*"\s*/>','<property name="CustomIconTint" value="FFFFFF" />',body)
            return m[1]+body+m[3]
        text,n=re.subn(pattern,change,text,flags=re.S)
        if n!=1:raise ValueError('Expected one block: '+name)
    path.write_text(text,encoding='utf-8')
    preview=ROOT/'.local-tests/AutomationIcons';preview.mkdir(parents=True,exist_ok=True)
    sheet=Image.new('RGB',(7*180,4*205),'#243240');d=ImageDraw.Draw(sheet)
    font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',17)
    for i,(name,im) in enumerate(icons.items()):
        x=(i%7)*180;y=(i//7)*205
        sheet.paste(im,(x+10,y+7),im);d.text((x+90,y+175),LABELS[name],font=font,fill='#F0F4F8',anchor='mt')
    sheet.save(preview/'automation-icons.png')
    print(f'Generated {len(icons)} distinct icons in {OUT}')
    print(preview/'automation-icons.png')

if __name__=='__main__':main()

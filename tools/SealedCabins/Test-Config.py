"""Resolve the installed XML patch stack and check the real cabin patch targets."""
from pathlib import Path
from copy import deepcopy
from lxml import etree as E
from configure import ROOT,MOD,INSIDE,OUTSIDE,EXPOSURE,STAGES,HAZARDS

parser=E.XMLParser(remove_comments=True)
tree=E.parse(str(ROOT.parent/'Data/Config/buffs.xml'),parser)
checks=0
def check(ok,label):
    global checks
    checks+=1
    assert ok,label

def apply(path,strict=False):
    count=0
    for op in E.parse(str(path),parser).getroot():
        targets=tree.xpath(op.get('xpath'))
        if strict:check(bool(targets),'Cabin XPath resolves: '+op.get('xpath'))
        for node in targets:
            count+=1
            if op.tag=='remove':node.getparent().remove(node)
            elif op.tag=='append':
                for child in op:node.append(deepcopy(child))
            elif op.tag in ('insertBefore','insertAfter'):
                at=node.getparent().index(node)+(op.tag=='insertAfter')
                for child in op:node.getparent().insert(at,deepcopy(child));at+=1
            elif op.tag=='set':
                if isinstance(node,E._ElementUnicodeResult):
                    if node.is_attribute:node.getparent().set(node.attrname,op.text or '')
                    else:node.getparent().text=op.text or ''
                else:node.text=op.text or ''
            else:raise AssertionError('Unhandled patch operation '+op.tag)
    return count

for f in sorted(ROOT.glob('*/Config/buffs.xml'),key=lambda p:p.parent.parent.name.casefold()):
    if f.parent.parent!=MOD:apply(f)
before=deepcopy(tree)
targets=apply(MOD/'Config/buffs.xml',True)
def buff(name):return tree.xpath('/buffs/buff[@name="'+name+'"]')[0]
def guarded(node,inside=False):return bool(node.xpath('./requirement[@name=$r]',r=INSIDE if inside else OUTSIDE))

for name in EXPOSURE:
    for node in buff(name).xpath('./effect_group/passive_effect'):
        check(guarded(node),'Active exposure passive gated: '+name)
for name in HAZARDS:
    for node in buff(name).xpath('.//triggered_effect[@action="AddHealth" or @action="ModifyStats"]'):
        check(guarded(node),'Direct environment health/stamina damage gated: '+name)
for name in STAGES:
    check(not tree.xpath('/buffs/buff[@name=$name]',name=name),'AEC disabled native storm remains absent '+name)

status=buff('buffStatusCheck01')
reset=status.find('effect_group')
check(reset.get('name')=='PZAEC occupied cabin exposure reset' and guarded(reset,True),'Exposure cleared before native/PZ update groups')
check({n.get('cvar') for n in reset.findall('triggered_effect') if n.get('action')=='ModifyCVar'}=={'$HotStatus','$ColdStatus','$ActiveRadiationStatus'},'Reset only exposure, never accumulated illness')
check(set(reset.find("triggered_effect[@action='RemoveBuff']").get('buff').split(','))==set(EXPOSURE),'Only exposure buffs removed')

# These exact buff trees must remain untouched: treatment, accumulated radiation
# illness, direct-hit radiation and unrelated injuries are not cabin effects.
for name in ['SmallMiniBossHitRad','MiniBossHitRad','BossHitRad','buffstimIrradiatedShell',
             'buffIrradiationIncreasing1','buffIrradiationIncreasing2','buffIrradiationIncreasing3',
             'buffIrradiationDescreasing2','buffThermoplegiaDesk1','buffFrostbiteDesk1']:
    old=before.xpath('/buffs/buff[@name="'+name+'"]')[0]
    check(E.tostring(old)==E.tostring(buff(name)),'Preserve illness/drug/direct-hit/recovery tree '+name)

# Execute actual patched status writes and the existing recovery groups with a
# tiny requirement/CVar evaluator. This is not a Unity or multiplayer simulation.
def run(inside):
    cv={'$HotStatus':1.,'$ColdStatus':1.,'$ActiveRadiationStatus':30.,'$ActiveRadiationCurrent':30.,
        '$IRradiationStatus':44.,'$ThermoplegiaStatus':30.,'$FrostbiteStatus':20.}
    has=set(EXPOSURE)
    def value(s):return cv.get(s[1:],0.) if s.startswith('@') else float(s)
    def requirement(n):
        if n.tag=='requirement_group':
            result=[requirement(x) for x in n]
            return any(result) if n.get('op')=='or' else all(result)
        name=n.get('name')
        if name==INSIDE:return inside
        if name==OUTSIDE:return not inside
        if name in ('HasBuff','!HasBuff','NotHasBuff'):
            match=any(x in has for x in n.get('buff','').split(','));return match if name=='HasBuff' else not match
        if name=='CVarCompare':
            a=cv.get(n.get('cvar'),0.);b=value(n.get('value'));op=n.get('operation')
            return {'Equals':a==b,'EQ':a==b,'GT':a>b,'GTE':a>=b,'LT':a<b,'LTE':a<=b,'NotEquals':a!=b}[op]
        if name in ('ProgressionLevel','IsIndoors'):return False
        raise AssertionError('Unmodelled selected-group requirement '+name)
    def allowed(n):return all(requirement(x) for x in n if x.tag in ('requirement','requirement_group'))
    def group(g):
        if not allowed(g):return
        for n in g.findall('triggered_effect'):
            if not allowed(n):continue
            if n.get('action')=='RemoveBuff':has.difference_update(n.get('buff').split(','))
            elif n.get('action')=='ModifyCVar':
                key=n.get('cvar');x=value(n.get('value'));old=cv.get(key,0);op=n.get('operation')
                cv[key]={'set':lambda:x,'add':lambda:old+x,'subtract':lambda:old-x,'multiply':lambda:old*x,'divide':lambda:old/x}[op]()
    group(reset)
    # Only the final active exposure assignment from the existing AR calculation.
    ar=E.Element('effect_group')
    for n in status.xpath("./effect_group[@name='AR_Calculation']/triggered_effect[@cvar='$ActiveRadiationStatus']"):ar.append(deepcopy(n))
    group(ar)
    for name in ['Regeneration Thermoplegia','Regeneration Frostbite']:
        gs=status.xpath('./effect_group[@name=$name]',name=name);check(len(gs)==1,'Recovery group uniquely resolved '+name);group(gs[0])
    if inside:
        check(cv['$ActiveRadiationStatus']==0 and cv['$HotStatus']==0 and cv['$ColdStatus']==0,'Cabin prevents stale exposure rewrite')
        check(cv['$IRradiationStatus']==44,'Entering cabin does not cure cumulative radiation')
        check(0<cv['$ThermoplegiaStatus']<30 and 0<cv['$FrostbiteStatus']<20,'Heat/cold illness recovers gradually using original rules')
        check(not has,'Old immediate exposure buffs cleaned up')
    else:
        check(cv['$ActiveRadiationStatus']==30 and len(has)==len(EXPOSURE),'Outside updates restore normal exposure')
        check(cv['$ThermoplegiaStatus']==30 and cv['$FrostbiteStatus']==20,'No free recovery while still exposed outside')
run(True);run(False)
print(f'PASS {checks} merged-config/recovery checks; {targets} cabin patch targets across installed buff stack')

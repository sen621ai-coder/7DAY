"""Generate a late-load, targeted patch; never edit Project Z or vanilla files."""
from pathlib import Path
from lxml import etree as E

ROOT=Path(__file__).resolve().parents[2]
MOD=ROOT/'ZZZ-PZAEC_SealedCabins'
INSIDE='PZAEC.SealedCabins.CabinProtected, PZAEC.SealedCabins'
OUTSIDE='!'+INSIDE
EXPOSURE=['buffElementHot','buffElementSweltering','buffElementCold','buffElementFreezing',
          'buffRadiation01','buffRadiation03','buffRadiationPool',
          'buffActiveRadiationImpact1','buffActiveRadiationImpact2','buffActiveRadiationImpact3']
STAGES=[f'buff{biome}_Storm_Stage0{stage}' for biome in ['Desert','Snow','Wasteland'] for stage in [1,2]]
HAZARDS=[f'buff{biome}_Hazard0{stage}' for biome in ['Desert','Snow','Wasteland'] for stage in [1,2]]

def names(values):return ' or '.join(f"@name='{v}'" for v in values)
def req(parent,inside=False):E.SubElement(parent,'requirement',name=INSIDE if inside else OUTSIDE)
def append_req(root,xpath,inside=False):req(E.SubElement(root,'append',xpath=xpath),inside)

def build():
    root=E.Element('configs')
    root.append(E.Comment('Live seat requirements; cumulative illness and direct-hit/drug effects remain intact.'))
    # Cleanup BEFORE native/PZ status evaluation; do not zero accumulated illness.
    op=E.SubElement(root,'insertBefore',xpath="/buffs/buff[@name='buffStatusCheck01']/effect_group[1]")
    group=E.SubElement(op,'effect_group',name='PZAEC occupied cabin exposure reset');req(group,True)
    for name in ['$HotStatus','$ColdStatus','$ActiveRadiationStatus']:
        E.SubElement(group,'triggered_effect',trigger='onSelfBuffUpdate',action='ModifyCVar',cvar=name,operation='set',value='0')
    E.SubElement(group,'triggered_effect',trigger='onSelfBuffUpdate',action='RemoveBuff',buff=','.join(EXPOSURE))

    status="/buffs/buff[@name='buffStatusCheck01']"
    # Existing zero setters and illness recovery keep running normally.
    append_req(root,status+"/effect_group/triggered_effect[@action='ModifyCVar' and (@cvar='$HotStatus' or @cvar='$ColdStatus') and @value='1']")
    append_req(root,status+"/effect_group[@name='AR_Calculation']/triggered_effect[@cvar='$ActiveRadiationStatus']")
    append_req(root,status+"/effect_group[@name='IRRADIATION_Impact']/triggered_effect[@cvar='$IRradiationStatus' and @operation='add']")

    # Already active exposure buffs must stop causing damage immediately, before
    # their next removal update. Keep on-remove cleanup effects unrestricted.
    exposure=f'/buffs/buff[{names(EXPOSURE)}]'
    append_req(root,exposure+'/effect_group/passive_effect')
    append_req(root,exposure+"/effect_group/triggered_effect[@action='ModifyStats' or @action='AddHealth' or @action='PlaySound' or (@action='ModifyScreenEffect' and @intensity!='0')]")
    # Buff-based native spawners also skip protected players; the DLL catches
    # direct native callers such as radioactive pools and network AddBuff calls.
    append_req(root,"/buffs/buff/effect_group/triggered_effect[@action='AddBuff' and ("+' or '.join(f"@buff='{b}'" for b in EXPOSURE)+')]' )

    # AEC Endgame removes native Storm/Stage/Recover buffs from the final stack.
    # Do not resurrect them or emit unresolved XPath patches. The biome hazards
    # below remain active and still have direct AddHealth damage outside a cabin.
    hazards=f'/buffs/buff[{names(HAZARDS)}]'
    append_req(root,hazards+"/effect_group/triggered_effect[@action='AddHealth' or @action='ModifyStats' or @action='PlaySound' or @action='AttachParticleEffectToEntity']")
    # Pause these biomes' exposure countdown while seated; never grant biome
    # progression completion or remove the underlying biome/controller buff.
    append_req(root,"/buffs/buff["+names(['buffDesert_Hazard','buffSnow_Hazard','buffWasteland_Hazard'])+"]/effect_group/triggered_effect[@action='ModifyCVar' and @operation='subtract' and (@cvar='$DesertHazardTimer' or @cvar='$SnowHazardTimer' or @cvar='$WastelandHazardTimer')]")

    inc=[f'buff{kind}Inc{i}' for kind in ['Thermoplegia','Frostbite'] for i in [1,2,3]]
    append_req(root,f'/buffs/buff[{names(inc)}]/effect_group/passive_effect')
    append_req(root,f'/buffs/buff[{names(inc)}]'+"/effect_group/triggered_effect[@action='AddHealth' or @action='ModifyStats' or @action='PlaySound' or (@action='AddBuff' and @buff='buffInjuryKnockdown01')]")
    path=MOD/'Config/buffs.xml';path.parent.mkdir(parents=True,exist_ok=True)
    E.ElementTree(root).write(str(path),encoding='utf-8',xml_declaration=True,pretty_print=True)
    print('Generated',path)

if __name__=='__main__':build()

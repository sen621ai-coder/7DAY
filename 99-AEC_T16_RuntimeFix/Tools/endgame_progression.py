"""Fixed T16-T19 combat progression above the installed Q6 legendary baseline.

Six usable sockets retain the native assembly UI's six-row capacity. The runtime
migrates earlier four/five-slot saved equipment without changing item identity.
"""
import xml.etree.ElementTree as ET

WEAPON_STATS = {
    'EmberPistol': ([700, 950, 1280, 1730], [32, 40, 50, 64], [420, 480, 540, 620]),
    # damage, magazine, rpm; base values before ammunition and shared buffs
    'HorizonNeedle': ([750, 1000, 1350, 1800], [24, 30, 38, 48], [300, 340, 380, 430]),
    'StormReservoir': ([550, 750, 1000, 1350], [180, 220, 270, 330], [1250, 1400, 1550, 1750]),
    'FaultlineHammer': ([1800, 2400, 3200, 4300], [0]*4, [80, 88, 96, 104]),
    'BastionShotgun': ([450, 600, 810, 1100], [80, 100, 125, 155], [300, 330, 360, 390]),
    'EchoRepeater': ([950, 1280, 1730, 2340], [8, 11, 15, 20], [160, 176, 194, 214]),
    'CounterSiege': ([1500, 2050, 2800, 3800], [4, 5, 6, 8], [90, 100, 110, 120]),
}
LIGHT_HIT = [650, 880, 1190, 1610]
WEAPON_DURABILITY = [5000, 6500, 8500, 11000]
ARMOR_DURABILITY = [3000, 4200, 5800, 8000]
ACTIVE_DURATION = [12, 16, 20, 24]
COOLDOWN_DURATION = [30, 26, 22, 18]
ARMOR = {'Helmet':[26,27,28,29], 'Outfit':[34,35,36,37], 'Gloves':[13,14,15,16], 'Boots':[13,14,15,16]}

def passive(group, name, operation, value, tags=None):
    attrs = {'name': name, 'operation': operation, 'value': str(value)}
    if tags: attrs['tags'] = tags
    return ET.SubElement(group, 'passive_effect', attrs)

def set_prop(item, name, value, parent=None):
    parent = item if parent is None else parent
    node = parent.find(f"property[@name='{name}']")
    if node is None: node = ET.SubElement(parent, 'property', {'name':name})
    node.set('value', str(value))

def armor_items(root):
    for item in root.findall('item'):
        name = item.get('name','')
        if not name.startswith('armorPZAEC'): continue
        i = int(name[-2:])-16
        slot = next(s for s in ARMOR if s in name)
        # Replace base and specialty effects, not the native wear/set triggers.
        for group in list(item.findall('effect_group')):
            if group.get('name','').startswith('AEC '): item.remove(group)
        group = ET.SubElement(item,'effect_group',{'name':'AEC Legendary succession','tiered':'false'})
        passive(group,'ModSlots','base_set',6)
        passive(group,'DegradationMax','base_set',ARMOR_DURABILITY[i])
        passive(group,'PhysicalDamageResist','base_add',ARMOR[slot][i])
        passive(group,'ElementalDamageResist','base_add',ARMOR[slot][i],'heat,electrical')
        passive(group,'BuffResistance','base_add',[.20,.25,.30,.35][i],
                'buffFatiguedTrigger,buffArmSprainedCHTrigger,buffLegSprainedCHTrigger,buffAbrasionCatch,buffLaceration,buffInfectionCatch,buffInjuryStunned01CHTrigger,buffInjuryBleedingTwo,buffInjuryBleedingBarbedWire')
        deg = 'lightArmorDeg' if 'Harrier' in name else 'mediumArmorDeg' if 'Storm' in name else 'heavyArmorDeg'
        passive(group,'DegradationPerUse','base_set',[.60,.50,.40,.30][i],deg)
        passive(group,'HypothermalResist','base_add',[9,12,16,22][i])
        passive(group,'HyperthermalResist','base_add',[9,12,16,22][i])
        passive(group,'Mobility','perc_add',[.02,.03,.04,.05][i])
        passive(group,'NoiseMultiplier','perc_add',[-.05,-.08,-.12,-.16][i])
        # Common combat floor beats Q6 legacy single-piece offensive passives.
        if slot == 'Helmet':
            passive(group,'HeadshotDamageModifier','perc_add',[.65,.85,1.10,1.40][i],'head')
            passive(group,'PlayerExpGain','perc_add',[.20,.30,.45,.65][i])
            passive(group,'LootStage','base_add',[25,35,50,70][i])
            passive(group,'LootStage','perc_add',[.25,.35,.50,.70][i])
            for loss in ('FoodLossPerStaminaPointGained','WaterLossPerStaminaPointGained'):
                passive(group,loss,'perc_add',[-.65,-.70,-.75,-.80][i])
        elif slot == 'Outfit':
            passive(group,'HealthMax','base_add',[100,160,240,350][i])
            passive(group,'StaminaMax','base_add',[120,180,260,360][i])
            passive(group,'CarryCapacity','base_add',[12,16,22,30][i])
            passive(group,'HarvestCount','base_add',[7,9,12,16][i],'SonnyRareRecources')
        elif slot == 'Gloves':
            passive(group,'EntityDamage','perc_add',[.80,1.05,1.40,1.85][i])
            passive(group,'RoundsPerMinute','perc_add',[.65,.85,1.10,1.40][i])
            passive(group,'AttacksPerMinute','perc_add',[.65,.85,1.10,1.40][i])
            passive(group,'ReloadSpeedMultiplier','perc_add',[.65,.85,1.10,1.40][i])
            passive(group,'LootQuantity','perc_add',[.40,.55,.75,1][i],'food,medical')
            passive(group,'ScavengingTime','perc_add',[-.30,-.40,-.50,-.60][i])
            passive(group,'HarvestCount','base_add',[7,9,12,16][i],'salvageHarvest,allHarvest')
        else:
            passive(group,'StaminaChangeOT','perc_add',[1.20,1.60,2.10,2.70][i])
            passive(group,'RunSpeed','perc_add',[.12,.16,.20,.25][i])
            passive(group,'StaminaLoss','perc_add',[-.40,-.50,-.60,-.70][i])
            passive(group,'StaminaMax','base_add',[60,90,130,180][i])
            passive(group,'VehicleMotorTorquePer','perc_add',[.30,.40,.55,.75][i])
            passive(group,'VehicleVelocityMaxPer','perc_add',[.40,.50,.65,.85][i])
        # Keep an additional specialization on top of the shared floor.
        if 'Harrier' in name and slot == 'Helmet': passive(group,'EntityDamage','perc_add',[.40,.60,.85,1.15][i],'ranged')
        if 'Storm' in name and slot == 'Outfit': passive(group,'MagazineSize','perc_add',[.60,.85,1.15,1.50][i])
        if 'Tremor' in name and slot == 'Gloves': passive(group,'EntityDamage','perc_add',[.60,.85,1.15,1.50][i],'secondary')
        if 'Warden' in name and slot == 'Gloves': passive(group,'BlockRepairAmount','perc_add',[.80,1.20,1.70,2.30][i])

def weapons(root):
    for item in root.findall('item'):
        name = item.get('name','')
        stem = next((s for s in WEAPON_STATS if name.startswith(('gunPZAEC'+s,'meleePZAEC'+s))),None)
        if stem is None: continue
        i = int(name[-2:])-16
        stats = WEAPON_STATS[stem]
        for effect in item.findall('./effect_group/passive_effect'):
            effect_name = effect.get('name')
            if effect_name == 'ModSlots': effect.set('value','6')
            elif effect_name == 'DegradationMax': effect.set('value',str(WEAPON_DURABILITY[i]))
            elif effect_name == 'EntityDamage' and effect.get('operation') == 'base_set':
                effect.set('value',str(LIGHT_HIT[i] if effect.get('tags') == 'primary' else stats[0][i]))
            elif effect_name == 'MagazineSize': effect.set('value',str(stats[1][i]))
            elif effect_name == 'RoundsPerMinute': effect.set('value',str(stats[2][i]))
            elif effect_name == 'StaminaLoss': effect.set('value',str([28,25,22,19][i]))
            elif effect_name == 'EntityPenetrationCount': effect.set('value',str(([10,12,14,16] if stem=='HorizonNeedle' else [4,5,6,7])[i]))
        group = ET.SubElement(item,'effect_group',{'name':'AEC Legendary succession','tiered':'false'})
        passive(group,'ReloadSpeedMultiplier','perc_add',[.65,.90,1.20,1.60][i])
        if stem == 'FaultlineHammer':
            passive(group,'AttacksPerMinute','base_set',stats[2][i])
            passive(group,'StaminaLoss','base_set',[18,16,14,12][i],'primary')
            knock = ET.SubElement(group,'triggered_effect',{'trigger':'onSelfSecondaryActionRayHit','action':'Ragdoll','target':'other','duration':str(4+i),'force':'500'})
            ET.SubElement(knock,'requirement',{'name':'EntityTagCompare','target':'other','tags':'zombie,animal'})
        if stem in ('HorizonNeedle','EchoRepeater'):
            passive(group,'DamageModifier','base_add',[2500,3000,3600,4300][i],'head')
            shock = ET.SubElement(group,'triggered_effect',{'trigger':'onSelfPrimaryActionRayHit','action':'AddBuff','target':'other','buff':'buffShocked'})
            ET.SubElement(shock,'requirement',{'name':'EntityTagCompare','target':'other','tags':'zombie,animal'})
        if stem == 'CounterSiege':
            set_prop(item,'Reload_time',[2.4,2.1,1.8,1.5][i],item.find("property[@class='Action0']"))
        set_prop(item,'RepairTools','resourceRepairKit')

def components(root):
    for item in root.findall('item_modifier'):
        name = item.get('name','')
        if not name.startswith('modPZAEC') or not name[-2:].isdigit(): continue
        i = int(name[-2:])-16
        if not 0 <= i <= 3: continue
        if 'Stable' in name or 'Overload' in name: continue
        group = item.find('effect_group')
        # Preserve trigger definitions; replace the former low-impact passives.
        for p in list(group.findall('passive_effect')): group.remove(p)
        def add(n,op,values,tags=None): passive(group,n,op,values[i] if isinstance(values,list) else values,tags)
        if 'ThreatLens' in name:
            add('EntityDamage','perc_add',[2.25,3,4,5.5],'ranged')
        elif 'CoolingSink' in name:
            for axis in ('VerticalMin','VerticalMax','HorizontalMin','HorizontalMax'): add('KickDegrees'+axis,'perc_add',-1)
            add('WeaponHandling','perc_add',[1,1.25,1.60,2])
            add('RoundsPerMinute','perc_add',[1.10,1.40,1.80,2.30])
        elif 'KineticRecycler' in name:
            add('StaminaLoss','perc_add',[-.40,-.50,-.60,-.70],'primary,secondary')
            add('AttacksPerMinute','perc_add',[.65,.85,1.10,1.40])
            add('RoundsPerMinute','perc_add',[.65,.85,1.10,1.40])
            add('WeaponHandling','perc_add',[.35,.50,.70,.90])
        elif 'RepairServo' in name:
            add('BlockRepairAmount','perc_add',[1,1.5,2.2,3])
            add('ReloadSpeedMultiplier','perc_add',[2.75,3.5,4.5,6])
        elif 'PhaseRangefinder' in name:
            add('HeadshotDamageModifier','perc_add',[2.25,3,4,5.5],'head')
            add('SpreadMultiplierAiming','perc_add',[-.65,-.72,-.80,-.88])
            add('WeaponHandling','perc_add',[1,1.25,1.60,2])
        elif 'NearfieldReflex' in name:
            add('ReloadSpeedMultiplier','perc_add',[2.75,3.5,4.5,6])
            add('DamageFalloffRange','perc_add',[.15,.25,.40,.60])
            add('RoundsPerMinute','perc_add',[.65,.85,1.10,1.40])
        elif 'ClosedLoopFeed' in name:
            add('MagazineSize','perc_add',[5.5,7,9,12])
            add('ReloadSpeedMultiplier','perc_add',[2.75,3.5,4.5,6])
            add('RoundsPerMinute','perc_add',[.20,.30,.45,.65])
        elif 'PulseCapacitor' in name:
            add('EntityDamage','perc_add',[2.25,3,4,5.5])
            add('EntityPenetrationCount','base_add',[2,3,4,5])
        elif 'StanceBreaker' in name:
            add('EntityDamage','perc_add',[2.25,3,4,5.5],'primary,secondary')
            add('AttacksPerMinute','perc_add',[.65,.85,1.10,1.40])
        elif 'GatekeeperHook' in name:
            add('BuffResistance','base_add',1,'buffInjuryStunned01CHTrigger')
            add('EntityDamage','perc_add',[2.25,3,4,5.5])
        elif 'EmergencyLiner' in name:
            add('PhysicalDamageResist','base_add',[3,4,5,6])
            add('HealthMax','base_add',[80,120,180,260])
            add('BuffResistance','base_add',1,'buffInjuryBleedingTwo,buffInjuryBleedingBarbedWire')
        elif 'InsulatedTreads' in name:
            add('ElementalDamageResist','base_add',[15,20,26,34],'electrical')
            add('StaminaChangeOT','perc_add',[.50,.75,1.10,1.60])
            add('BuffResistance','base_set',1,'buffDeepSnowStatus,buffSandStatus,buffDesertGroundStatus,buffRoughGroundStatus,buffDestroyedStoneStatus')

def arsenal_buffs(root):
    for buff in root.findall('buff'):
        name = buff.get('name','')
        import re
        match = re.search(r'T(1[6-9])',name)
        if not match: continue
        i = int(match[1])-16
        if name.endswith('Active'):
            buff.find('duration').set('value',str(ACTIVE_DURATION[i]))
            for p in buff.findall('./effect_group/passive_effect'):
                n=p.get('name')
                if n in ('EntityDamage','HeadshotDamageModifier'): p.set('value',str([1,1.40,1.90,2.60][i]))
                elif n in ('RoundsPerMinute','ReloadSpeedMultiplier'): p.set('value',str([.65,.90,1.20,1.60][i]))
                elif n=='SpreadMultiplierHip': p.set('value','-.10')
                elif n=='RunSpeed': p.set('value',str([.05,.08,.12,.16][i]))
        elif name.endswith('Cooldown'):
            buff.find('duration').set('value',str(COOLDOWN_DURATION[i]))
        elif name.endswith('Aura'):
            for p in buff.findall('./effect_group/passive_effect'):
                if p.get('name')=='StaminaChangeOT': p.set('value',str([.60,.90,1.30,1.80][i]))
            passive(buff.find('effect_group'),'HealthMax','base_add',[80,120,180,260][i])
        elif name.endswith('Set2'):
            passive(buff.find('effect_group'),'EntityDamage','perc_add',[.80,1.10,1.50,2][i])
        elif name.endswith('Set3'):
            for p in buff.findall('.//triggered_effect'):
                if p.get('action')=='ModifyCVar' and p.get('operation')=='add':
                    p.set('value',str([16,20,25,34][i] if 'Harrier' in name else [8,10,13,17][i] if 'Storm' in name else [25,34,50,100][i]))

def expansion_buffs(root):
    for buff in root.findall('buff'):
        name = buff.get('name','')
        if 'StormOverheated' in name:
            i=int(name[-2:])-16
            for p in buff.findall('./effect_group/passive_effect'):
                if p.get('name')=='RoundsPerMinute': p.set('value',str([-.10,-.08,-.06,-.04][i]))
        elif 'PulseCapacitor' in name:
            i=int(name[-2:])-16
            buff.find('duration').set('value',str([1,1.4,1.8,2.4][i]))
            for p in buff.findall('./effect_group/passive_effect'): p.set('value',str([-.35,-.45,-.55,-.65][i]))

def defense_blocks(root):
    for block in root.findall('block'):
        name=block.get('name','')
        if not name.endswith(('T16','T17','T18','T19')) or 'Ruin' in name: continue
        i=int(name[-2:])-16
        maximum=block.find("property[@name='MaxDamage']")
        if maximum is not None: maximum.set('value',str(round(int(maximum.get('value')) * [1.5,1.8,2.2,2.7][i])))
        if 'ArmorBreakTurret' in name:
            set_prop(block,'EntityDamage',[180,250,350,490][i])
            set_prop(block,'BurstRoundCount',[60,75,90,110][i])
            set_prop(block,'CooldownTime',[2,1.8,1.6,1.4][i])
        elif 'SkyguardArray' in name:
            set_prop(block,'EntityDamage',[180,250,350,490][i])
            set_prop(block,'BurstRoundCount',[30,40,50,60][i])

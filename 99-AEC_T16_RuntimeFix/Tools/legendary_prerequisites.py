"""T16 crafting consumes one matching legendary predecessor, any quality."""

WEAPONS = {
    'EmberPistol': ['gunHandgunT5Hellgun'],
    'HorizonNeedle': ['gunRifleT5SniperRifleGaus'] + ['GausLeg' + v for v in ('Arsonist', 'Tesla', 'URANUS', 'Unkillable')],
    'StormReservoir': ['gunMGT5M60Bulldog'] + ['BulldogLeg' + v for v in ('Arsonist', 'URANUS', 'Breeze', 'Unkillable', 'Avenger', 'Berserk')],
    'FaultlineHammer': ['meleeWpnSledgeT5SteelSledgehammerDestructor'] + ['DestructorLeg' + v for v in ('Crisis', 'Crusher', 'Masterpiece', 'Guardian')],
    'BastionShotgun': ['gunShotgunT5AutoShotgunEraser'] + ['EraserLeg' + v for v in ('Arsonist', 'URANUS', 'Unkillable', 'Avenger', 'Berserk')],
    'EchoRepeater': ['gunBowT5CompoundCrossbowMantis'],
}

# ProjectZ has no legendary rocket launcher; retain its improved launcher
# hardware and consume a legendary precision weapon as the pulse core.
WEAPONS['CounterSiege'] = list(WEAPONS['HorizonNeedle'])
EXTRA = {'CounterSiege': [('gunExplosivesT4RocketLauncherImp', 1)]}

ARMOR = {'Harrier': 'armorPredator', 'Storm': 'armorPlunderer', 'Tremor': 'armorMaus', 'Warden': 'armorRescuer'}

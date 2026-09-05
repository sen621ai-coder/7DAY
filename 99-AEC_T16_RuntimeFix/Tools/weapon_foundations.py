"""Extends copies properties, not passive effects. Preserve native firing inputs."""
import copy
import xml.etree.ElementTree as ET

FOUNDATIONS = {
    'MaxRange', 'BlockRange', 'DamageFalloffRange', 'BurstRoundCount',
    'ReloadSpeedMultiplier', 'SpreadDegreesVertical', 'SpreadDegreesHorizontal',
    'SpreadMultiplierAiming', 'SpreadMultiplierCrouching', 'SpreadMultiplierWalking',
    'SpreadMultiplierRunning', 'KickDegreesVerticalMin', 'KickDegreesVerticalMax',
    'KickDegreesHorizontalMin', 'KickDegreesHorizontalMax', 'IncrementalSpreadMultiplier',
    'WeaponHandling', 'DegradationPerUse',
}


def apply(root, vanilla):
    for item in root.findall('item'):
        name = item.get('name', '')
        if not name.startswith(('gunPZAEC', 'meleePZAEC')): continue
        base = vanilla[item.find("property[@name='Extends']").get('value')]
        existing = {e.get('name') for e in item.findall('./effect_group/passive_effect') if e.get('operation') == 'base_set'}
        group = ET.Element('effect_group', name='AEC native firing foundations', tiered='false')
        for effect in base.findall('./effect_group/passive_effect'):
            effect_name = effect.get('name')
            if effect_name not in FOUNDATIONS or effect_name in existing or effect.get('operation') != 'base_set': continue
            # Only fixed native handling parameters. Never copy random damage,
            # quality scaling, socket count or the original weapon's triggers.
            if ',' in effect.get('value') or effect.get('tier'):
                raise ValueError(f'Non-fixed firing foundation: {name}/{effect_name}')
            group.append(copy.deepcopy(effect))
        item.insert(0, group)

"""Keep M1 recipes within ProjectZ's six visible ingredient rows."""
import csv
import xml.etree.ElementTree as E
from pathlib import Path

MATERIALS = ('resourceLegendaryParts', 'resourceForgedSteel', 'resourceDurablAlloys',
             'resourceMechanicalParts', 'resourceElectricParts', 'resourceSpring')


def apply(root):
    cfg = Path(root) / 'ZZ-PZAEC_M1Abrams/Config'
    recipes = E.parse(cfg / 'recipes.xml')
    items = E.parse(cfg / 'items.xml')
    with (cfg / 'Localization.csv').open(encoding='utf-8-sig', newline='') as f:
        rows = list(csv.reader(f))
    for tier in range(16, 20):
        vehicle = 'vehicleM1Abrams' + ('' if tier == 16 else f'T{tier}') + 'Placeable'
        kit = f'PZAECM1AssemblyKitT{tier}'
        recipe = recipes.find(f"./append/recipe[@name='{vehicle}']")
        materials = [x for x in recipe.findall('ingredient') if x.get('name') in MATERIALS]
        if materials:
            assert len(materials) == 6, f'Unexpected T{tier} materials'
            assert recipes.find(f"./append/recipe[@name='{kit}']") is None
            packed = E.SubElement(recipes.find('append'), 'recipe', name=kit, count='1',
                                  craft_area='workbench', craft_time='30',
                                  use_ingredient_modifier='false', tags='workbenchCrafting',
                                  always_unlocked='true')
            for ingredient in materials:
                recipe.remove(ingredient)
                packed.append(ingredient)
            E.SubElement(recipe, 'ingredient', name=kit, count='1')
            recipe.set('craft_time', str(int(recipe.get('craft_time')) - 30))
        assert recipe.find(f"ingredient[@name='{kit}']") is not None
        if items.find(f"./append/item[@name='{kit}']") is None:
            item = E.SubElement(items.find('append'), 'item', name=kit)
            for key, value in {'Extends': 'resourceForgedSteel', 'CustomIcon': 'resourceRepairKit',
                               'DescriptionKey': kit + 'Desc', 'Stacknumber': '10',
                               'EconomicValue': '0', 'Group': 'Resources'}.items():
                E.SubElement(item, 'property', name=key, value=value)
        rows = [r for r in rows if r[0] not in (kit, kit + 'Desc')]
        rows.extend([
            [kit, 'items', 'Item', '', '', f'M1 T{tier} Assembly Kit', f'M1 T{tier}整备套件'],
            [kit + 'Desc', 'items', 'Item', '', '',
             f'Workbench materials for M1 T{tier} assembly. Craft one kit per tank. Not a repair kit.',
             f'用于制造或升级至M1 T{tier}，每辆需要1套。先在工作台合成，再与前阶车辆、战术组件、心核和电容组装；T16另需发动机。整合原配方普通材料，总用量不变。不能用于维修。']])
    for filename, tree in [('recipes.xml', recipes), ('items.xml', items)]:
        E.indent(tree, space='  ')
        tree.write(cfg / filename, encoding='utf-8', xml_declaration=True)
    with (cfg / 'Localization.csv').open('w', encoding='utf-8', newline='') as f:
        csv.writer(f).writerows(rows)


if __name__ == '__main__':
    apply(Path(__file__).resolve().parents[2])

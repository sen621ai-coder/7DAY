import copy
import xml.etree.ElementTree as E

def remove_inherited_unlocks(blocks, game_blocks):
    """Keep native workstation/relay properties without inheriting a skill unlock.

    Empty UnlockedBy is NOT an empty list in V3.2: it constructs one invalid
    RecipeUnlockData and crashes the crafting information panel.
    """
    vanilla = E.parse(game_blocks)
    for name, parent in [('yfAutomationWorkbench', 'workbench'), ('yfAutoPowerPort', 'electricwirerelay')]:
        block = blocks.find(f".//block[@name='{name}']")
        if block is None:
            continue
        inherited = block.find("property[@name='Extends']")
        if inherited is not None:
            assert inherited.get('value') == parent
            keys = {(n.tag, n.get('name'), n.get('class')) for n in block if n.tag == 'property'}
            existing_tags = {n.tag for n in block if n.tag != 'property'}
            for child in vanilla.find(f"block[@name='{parent}']"):
                if child.tag == 'property':
                    if child.get('name') in ('Extends', 'UnlockedBy'):
                        continue
                    if (child.tag, child.get('name'), child.get('class')) in keys:
                        continue
                elif child.tag in existing_tags:
                    continue
                block.append(copy.deepcopy(child))
            block.remove(inherited)
        for unlock in block.findall("property[@name='UnlockedBy']"):
            block.remove(unlock)

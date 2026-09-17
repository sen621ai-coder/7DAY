"""Automation-only layout. Native templates are copied, never globally replaced."""
import copy
import xml.etree.ElementTree as E

def apply(app, workspace):
    def label(parent, name, text, x, y, width, height=28, size=20):
        return E.SubElement(parent, 'label', name=name, text=text, pos=f'{x},{y}', width=str(width), height=str(height), font_size=str(size), depth='4', color='[white]')
    def button(parent, name, text, x, y, width):
        b=E.SubElement(parent,'button',name=name,pos=f'{x},{y}',width=str(width),height='32',depth='3',sprite='menu_empty3px',defaultcolor='[mediumGrey]',hovercolor='[lightGrey]',type='sliced')
        label(b,name+'Text',text,6,-3,width-12)
        return b
    def panel(parent,name,x,y,width,height,title):
        p=E.SubElement(parent,'rect',name=name,pos=f'{x},{y}',width=str(width),height=str(height))
        E.SubElement(p,'sprite',name='background',width=str(width),height=str(height),sprite='menu_empty3px',color='[darkGrey]',type='sliced')
        E.SubElement(p,'sprite',name='header',width=str(width),height='43',sprite='ui_game_panel_header',depth='1')
        label(p,name+'Title',title,8,-8,width-16,30,23)
        return p
    templates=E.parse(workspace.parent/'Data/Config/XUi_InGame/templates.xml')
    # Native loot lifecycle, all 36 native ItemStack controllers and slot indices stay intact.
    storage=app.find("window[@name='windowYFAutomationStorage']")
    storage.clear()
    storage.attrib.update(name='windowYFAutomationStorage',width='324',height='747',panel='Right',cursor_area='true',controller='YFAutomation.YFAutomationStorageWindow, YF.Automation')
    grid=E.SubElement(storage,'grid',name='queue',rows='6',cols='6',cell_width='54',cell_height='36',repeat_content='false',controller='YFAutomation.YFAutomationSplitContainer, YF.Automation')
    for part,title in enumerate(('材料 / 工具','成品')):
        p=panel(grid,'part'+str(part),0,0,324,365,title)
        # Native toolbar controller uses partition masks supplied by this window.
        bar=E.SubElement(p,'rect',name='toolbar'+str(part),pos='0,-45',width='324',height='38',controller='ContainerStandardControls')
        for name,icon,x,tip in [('deposit','store_all_down',28,'存入背包物品到此区域'),('btnSort','sort',88,'仅整理此区域'),('btnMoveAll','store_all_up',148,'取出此区域物品'),('btnToggleLockMode','lock',208,'锁定 / 解锁格子')]:
            E.SubElement(bar,'button',name=name,depth='3',sprite='ui_game_symbol_'+icon,tooltip=tip,pos=f'{x},-18',style='icon32px, press, hover',pivot='center',sound='[paging_click]')
        slots=E.SubElement(p,'grid',name='slots'+str(part),pos='0,-93',rows='3',cols='6',cell_width='54',cell_height='36',repeat_content='true')
        E.SubElement(slots,'backpack_item_stack',name='0')
        label(p,'hint'+str(part),'放入原料和工具；传送带送入此处。' if part==0 else '自动生产到此处；传送带从此取走。',8,-228,308,60,18)
        label(p,'modeHint'+str(part),'外接模式下，这里的物品不参与加工。',8,-300,308,50,17)
    for name in ('windowYFAutomationConfiguration','windowYFAutomationInventoryControls'):
        w=app.find(f"window[@name='{name}']")
        controller=w.get('controller'); side=w.get('panel');w.clear()
        w.attrib.update(name=name,width='350',height='747',panel=side,cursor_area='true',controller=controller)
        top=panel(w,'list',0,0,350,435,'物品列表')
        label(top,'title','自动化设备',8,-45,334,24,19)
        E.SubElement(top,'textfield',name='search',pos='8,-74',width='334',height='30',font_size='20',character_limit='60',on_return='Submit',clear_button='true',focus_on_open='false')
        for row in range(8):
            entry=copy.deepcopy(templates.find('recipe_entry/rect'))
            entry.set('name','recipe'+str(row));entry.set('pos',f'6,{-112-row*35}');entry.set('width','338');entry.set('height','33')
            entry.set('controller','YFAutomation.YFAutomationProductEntry, YF.Automation')
            for child in list(entry):
                if child.get('name') not in ('background','Icon','Name'):entry.remove(child)
            entry.find("sprite[@name='background']").set('height','33')
            icon=entry.find("sprite[@name='Icon']");icon.set('pos','22,-16');icon.set('size','30,30')
            text=entry.find("label[@name='Name']");text.set('pos','185,-16');text.set('width','280');text.set('font_size','20')
            top.append(entry)
        button(top,'previous','上一页',8,-399,90);label(top,'pages','',128,-402,110);button(top,'next','下一页',252,-399,90)
        bottom=panel(w,'operations',0,-445,350,302,'操作')
        button(bottom,'save','保存',8,-48,100);button(bottom,'toggle','启动',118,-48,224)
        button(bottom,'mode','库存模式',8,-86,334)
        button(bottom,'source','输入箱',8,-124,334);button(bottom,'target','输出箱',8,-160,334)
        label(bottom,'status','',8,-200,334,36,17);label(bottom,'notice','',8,-238,220,50,17)
        button(bottom,'refresh','刷新',242,-257,100)
        # Compatibility controls for standalone configuration; hidden on dedicated screen.
        for id in ('product','close'):button(bottom,id,'',0,0,1).set('visible','false')
        for id in ('help','details'):label(bottom,id,'',0,0,1).set('visible','false')
    recipe=app.find("window[@name='windowYFAutomationRecipe']")
    recipe.clear();recipe.attrib.update(name='windowYFAutomationRecipe',width='870',height='300',panel='Center',cursor_area='true',controller='YFAutomation.YFAutomationRecipePanel, YF.Automation')
    panel(recipe,'recipeFrame',0,0,870,300,'物品配方')
    E.SubElement(recipe,'sprite',name='productIcon',pos='58,-103',size='80,80',pivot='center',atlas='ItemIconAtlas',depth='5')
    label(recipe,'productName','选择左侧物品',120,-51,730,32,24)
    label(recipe,'body','',12,-154,310,125,17)
    # Reuse native ingredient template with machine-specific bindings and counts.
    for row in range(4):
        entry=copy.deepcopy(templates.find('ingredient_row/rect'))
        entry.set('name','material'+str(row));entry.set('pos',f'358,{-90-row*43}');entry.set('height','41')
        entry.set('controller','YFAutomation.YFAutomationMaterialEntry, YF.Automation')
        for n in entry.iter():
            if n.get('font_size'):n.set('font_size','19')
            if n.get('height')=='48':n.set('height','41')
            if n.get('height')=='53':n.set('height','46')
        recipe.append(entry)
    label(recipe,'columnHint','材料 / 工具                         已有 / 需要',366,-54,485,30,20)
    label(recipe,'page','',600,-269,80,25,18)
    button(recipe,'back','上一页',360,-264,95);button(recipe,'forward','下一页',715,-264,95)

"""Asset/profile checks for Complete Form; Python standard library only."""
import csv,hashlib,json,pathlib,struct,math,xml.etree.ElementTree as E
ROOT=pathlib.Path(__file__).resolve().parents[2];MOD=ROOT/'ZZ-PZAEC_Mecha';RES=MOD/'Resources'
stem='samurai_style_gundam_mecha';doc=json.loads((RES/(stem+'_rig.json')).read_text());blob=(RES/(stem+'_rig.bin')).read_bytes()
assert hashlib.sha256((RES/(stem+'.glb')).read_bytes()).hexdigest()==doc['sourceSha256']
assert doc['skinned'] and doc['sourceParts']==17 and doc['sourceTriangles']==2000000
assert 150000<doc['triangles']<250000
assert len(doc.get('sourceNodes',list({p['node'] for p in doc['parts']})))==17
count=0
for p in doc['parts']:
    assert p['offset']+p['vertices']*48+p['indices']*4<=len(blob)
    for v in struct.iter_unpack('<8f2i2f',blob[p['offset']:p['offset']+p['vertices']*48]):
        assert all(math.isfinite(x) for x in v)
        assert -.02<=v[1]<=3.3 and abs(v[0])<2 and abs(v[2])<2
        assert abs(sum(x*x for x in v[3:6])-1)<.01
        assert all(0<=b<len(doc['joints']) for b in v[8:10])
        assert min(v[10:])>=0 and abs(sum(v[10:])-1)<.001
    indices=struct.unpack_from('<'+'I'*p['indices'],blob,p['offset']+p['vertices']*48)
    assert max(indices)<p['vertices'];count+=len(indices)//3
assert count==doc['triangles']
print('PASS full source identity, 17 chunks, skin weights, normals, optimized geometry and indices')
joints={j['name']:i for i,j in enumerate(doc['joints'])}
for name in ['WingL','WingR']:
    joint=doc['joints'][joints[name]]
    assert joint['parent']=='Backpack'
    assert joint['position'][0]*(-1 if name=='WingL' else 1)>0
counts={joints['WingL']:0,joints['WingR']:0}
for p in doc['parts']:
    records=list(struct.iter_unpack('<8f2i2f',blob[p['offset']:p['offset']+p['vertices']*48]))
    ix=struct.unpack_from('<'+'I'*p['indices'],blob,p['offset']+p['vertices']*48)
    for record in records:
        if record[8] in counts:
            assert record[8]==record[9] and record[10:]==(1.0,0.0)
            counts[record[8]]+=1
    for i in range(0,len(ix),3):
        bones=[records[ix[i+k]][8] for k in range(3)]
        if any(b in counts for b in bones):assert bones[0]==bones[1]==bones[2],('split wing triangle',p['nodeName'],bones)
assert min(counts.values())>500,counts
print('PASS independent rigid wing roots, complete face binding and real wing geometry',counts)

assert doc['bindingAudit']['rigidArmour'] and doc['renderRoles']['Backpack']>500
assert doc['renderRoles']['Shield']>500 and doc['bindingAudit']['swordCapsuleRejectedFaces']>500
for p in doc['parts']:
    records=list(struct.iter_unpack('<8f2i2f',blob[p['offset']:p['offset']+p['vertices']*48]))
    ix=struct.unpack_from('<'+'I'*p['indices'],blob,p['offset']+p['vertices']*48)
    for i in range(0,len(ix),3):
        corners=[records[ix[i+k]] for k in range(3)]
        assert all(v[8]==v[9] and v[10:]==(1.,0.) for v in corners),('blended mechanical face',p['role'],i//3)
        assert len({v[8] for v in corners})==1,('split mechanical face',p['role'],i//3)
        if p['role']=='Backpack':
            assert corners[0][8]==joints['Backpack']
            assert sum(v[1] for v in corners)/3<2.63,('head antenna attached to backpack',i//3)
        if p['role'].startswith('Sword'):
            center=[sum(v[k] for v in corners)/3 for k in range(3)]
            normal=doc['swordAnchors']['bladeNormal'];origin=(.3950305,.820023,-.5686091)
            plane=sum((center[k]-origin[k])*normal[k] for k in range(3))
            assert abs(plane)<(.16 if center[1]>1.55 else .14)+1e-6,('body spur inside sword',i//3,plane)
print('PASS complete rigid armour faces, independent back/shield roles and calibrated sword plane')

items=E.parse(MOD/'Config/items.xml').getroot().find('append');items={x.get('name'):x for x in items}
assert items['vehicleCombatRobotCompletePlaceable'].find(".//passive_effect[@name='DegradationMax']").get('value')=='3000000'
vehicles=E.parse(MOD/'Config/vehicles.xml').getroot().find('append');vehicles={x.get('name'):x for x in vehicles}
assert vehicles['vehicleCombatRobotComplete'].find(".//property[@name='capacity']").get('value')=='450'
recipes=E.parse(MOD/'Config/recipes.xml').getroot().find('append');recipes={x.get('name'):x for x in recipes}
for key in ['vehicleCombatRobotCompleteChassis','vehicleCombatRobotCompletePlaceable']:
    assert key in recipes and key in items
print('PASS independent recipes, 3M hull and 450L tank')
baselines=sorted((ROOT/'.local-tests').glob('Mecha-before-0.12.0-*/ZZ-PZAEC_Mecha'))
backup=baselines[-1] if baselines else ROOT/'.local-tests/Mecha-0.7.3-before-complete/ZZ-PZAEC_Mecha'
def normalized(e):return e.tag,sorted(e.attrib.items()),(e.text or '').strip(),[normalized(c) for c in e]
if backup.exists():
    for name in ['items.xml','recipes.xml','vehicles.xml','entityclasses.xml']:
        a={e.get('name'):e for e in E.parse(MOD/'Config'/name).getroot().find('append')}
        for b in E.parse(backup/'Config'/name).getroot().find('append'):assert normalized(a[b.get('name')])==normalized(b),(name,b.get('name'))
    def loc(p):return {r['Key']:r for r in csv.DictReader(p.open(encoding='utf-8-sig',newline=''))}
    a=loc(MOD/'Config/Localization.csv');b=loc(backup/'Config/Localization.csv')
    for key in b:
        if key.startswith('ammo'):assert a[key]==b[key]
    print('PASS original gameplay XML entries and shared ammunition unchanged against backup')

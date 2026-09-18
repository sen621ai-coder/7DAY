"""Validate shipped mesh topology, textures, bounds and the XML integration."""
from pathlib import Path
import hashlib,json,struct,xml.etree.ElementTree as ET
import numpy as np
from PIL import Image

root=Path(__file__).resolve().parents[1]
assets=root/'98-AECxProjectZ_Tweaks/Resources/Forestry'
report=json.loads((assets/'model-report.json').read_text())
raw=(assets/'sawmill.meshbin').read_bytes()
assert hashlib.sha256(raw).hexdigest()==report['meshSha256']
assert raw[:4]==b'YFF1'
materials,parts=struct.unpack_from('<II',raw,4); offset=12; triangles=0
assert materials==6 and parts==report['parts']==6
for p in range(parts):
    material,n,k=struct.unpack_from('<III',raw,offset); offset+=12
    assert 0<=material<materials and n>0 and k%3==0
    vertices=np.frombuffer(raw,dtype='<f4',count=n*8,offset=offset).reshape(n,8); offset+=n*32
    indices=np.frombuffer(raw,dtype='<i4',count=k,offset=offset).reshape(-1,3); offset+=k*4
    assert np.isfinite(vertices).all() and indices.min()>=0 and indices.max()<n
    pos=vertices[:,:3]; normal=vertices[:,3:6]
    assert (np.abs(pos[:,0])<=5).all() and (np.abs(pos[:,2])<=3).all()
    assert (pos[:,1]>=0).all() and (pos[:,1]<=4).all()
    assert np.allclose(np.linalg.norm(normal,axis=1),1,atol=.001)
    # Backfaces must duplicate exact positions and reverse both winding and normals.
    half=n//2; front=len(indices)//2
    assert np.array_equal(pos[:half],pos[half:])
    assert np.allclose(normal[:half],-normal[half:])
    assert np.array_equal(indices[front:],indices[:front][:,[0,2,1]]+half)
    triangles+=len(indices)
assert offset==len(raw) and triangles==report['triangles']
for m in range(materials):
    sizes=[]
    for kind in ('color','metal','normal'):
        image=Image.open(assets/f'{kind}{m}.png'); image.load(); sizes.append(image.size)
        assert image.mode=='RGBA'
    assert sizes[1]==sizes[2] and all(w>=627 and h>=627 for w,h in sizes)
    assert list(sizes[0])==report['albedoSizes'][m]
    if m>=2:
        ao=Image.open(assets/f'ao{m}.png'); ao.load()
        assert ao.size==sizes[0] and ao.mode=='RGBA'
assert report['detailAlbedo'] and report['albedoSizes'][1]==[1254,1254]
assert triangles==23706, 'UV splitting must preserve every source triangle'
lod=(assets/'sawmill-lod.meshbin').read_bytes()
assert hashlib.sha256(lod).hexdigest()==report['lodSha256']
assert lod[:4]==b'YFF1' and struct.unpack_from('<II',lod,4)==(6,6)
offset=12;lod_triangles=0
for p in range(6):
    material,n,k=struct.unpack_from('<III',lod,offset);offset+=12
    v=np.frombuffer(lod,dtype='<f4',count=n*8,offset=offset).reshape(n,8);offset+=n*32
    f=np.frombuffer(lod,dtype='<i4',count=k,offset=offset).reshape(-1,3);offset+=k*4
    assert np.isfinite(v).all() and f.min()>=0 and f.max()<n
    assert (np.abs(v[:,0])<=5).all() and (np.abs(v[:,2])<=3).all()
    assert (v[:,1]>=0).all() and (v[:,1]<=4).all()
    assert np.allclose(np.linalg.norm(v[:,3:6],axis=1),1,atol=.001)
    assert np.array_equal(v[:n//2,:3],v[n//2:,:3])
    assert np.allclose(v[:n//2,3:6],-v[n//2:,3:6])
    assert np.array_equal(f[len(f)//2:],f[:len(f)//2][:,[0,2,1]]+n//2)
    assert (np.linalg.norm(np.cross(v[f[:,1],:3]-v[f[:,0],:3],v[f[:,2],:3]-v[f[:,0],:3]),axis=1)>1e-10).all()
    lod_triangles+=len(f)
assert offset==len(lod) and lod_triangles==report['lodTriangles'] and lod_triangles<triangles*.5
timber=Image.open(assets/'timber.png'); timber.load()
assert timber.width==2*timber.height
icon=Image.open(root/'98-AECxProjectZ_Tweaks/UIAtlases/ItemIconAtlas/yfAutoForestry.png')
assert icon.size==(256,256) and icon.getextrema()[3]==(0,255)
tree=ET.parse(root/'98-AECxProjectZ_Tweaks/Config/blocks.xml')
block=tree.find(".//block[@name='yfAutoForestry']")
props={p.get('name'):p.get('value') for p in block.findall('property') if p.get('name')}
assert len(props)==len([p for p in block.findall('property') if p.get('name')])
assert props['Model']=='yfAutoForestryRuntime.prefab' and props['MultiBlockDim']=='10,4,6'
assert props['OversizedBounds']=='(-0.5,1.9,-0.5),(10,4.8,6)'
assert report['footprint']==[10,4,6] and 3.58<report['modelSize'][1]<3.60 and report['heightScale']==1.36
assert props['Class']=='Collector' and props['AllowedRotations']=='Basic90'
assert 'Extends' not in props and 'ImposterExchange' not in props and 'TintColor' not in props
assert props['Outputs'].split(',')==['IronBundle']*6
assert props['OutputTypes']=='{IronBundle,GasCan,1000,0,1000,yfForestryWoodBundle,yfForestryWoodBundle,2,2,48000,48000,collector_complete_item}'
assert props['ModTypes']=='Speed,Count,Modify'
assert props['ModTransformEnableNames']=='ForestrySpeed,ForestryPacker,ForestrySiren'
assert props['ModTransformDisableNames']==',,' and props['RequiredModsOnly']=='true'
assert props['CloseEvent']=='block_autominer_uw' and props['MaxDamage']=='2500'
source=(root/'99-AEC_T16_RuntimeFix/Source/AutoForestryModel.cs').read_text()
for name in props['ModTransformEnableNames'].split(','): assert '"'+name+'"' in source
assert len(block.findall("property[@class='RepairItems']"))==1
print(f'PASS: {parts} meshes, {triangles} near / {lod_triangles} distant triangles, bounds, textures, icon, Collector fuel/storage/mod wiring.')

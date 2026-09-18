"""Semantic split of this exact M1 source; preserves every original triangle/UV."""
from source import *
from PIL import ImageDraw, ImageFont
OUT=Path(__file__).parent/'Generated'; OUT.mkdir(exist_ok=True)
doc,images,parts=read(); main=next(p for p in parts if p['mesh']==1); tracks=next(p for p in parts if p['mesh']==0)
groups=components(main)
# Reviewed whole-island assignment. These indices are pinned by source hash and
# deterministic sort, rather than an arbitrary height cut through triangle faces.
turret=[24,25,28,33,34,35,36,37,38,39,40,41,49,50,51,53,54,55,56,57,59,60,66,69,72,
        *range(78,95),101,102,133,134,135,136,137,138,140,143,144,145,147,148,149,150,151,152,
        157,158,160,161,162,163,164,165,174,191,192,193,221,222,223,224,225,226,227,230,231,
        234,235,236,237,238,239,240,251,252,293,295,300,303,304,305,306,307,309,348]
barrel=[2,58,204,298,299,301,302]; mount=[31,48,297]
mapping={i:'Turret' for i in turret}; mapping.update({i:'Barrel' for i in barrel});mapping.update({i:'GunMount' for i in mount})
for side,ids in [('L',range(3,10)),('R',range(10,17))]:
    for number,i in enumerate(reversed(list(ids)),1):mapping[i]=f'RoadWheel_{side}_{number:02}'
mapping.update({0:'EndWheel_L_Rear',1:'EndWheel_R_Rear',17:'EndWheel_L_Front',18:'EndWheel_R_Front'})
semantic={}; assignment=[]
for i,ids in enumerate(groups):
    name=mapping.get(i,'Hull');semantic.setdefault(name,[]).extend(ids)
    assignment.append(dict(component=i,part=name,triangles=len(ids)))
meshparts=[]
for name,ids in semantic.items():
    used,inv=np.unique(main['f'][ids],return_inverse=True)
    meshparts.append(dict(name=name,v=main['v'][used],n=main['n'][used],uv=main['uv'][used],f=inv.reshape(-1,3),mat=main['mat']))
for side,negative in [('L',True),('R',False)]:
    mask=(tracks['v'][tracks['f']].mean(1)[:,0]<0)==negative
    used,inv=np.unique(tracks['f'][mask],return_inverse=True)
    meshparts.append(dict(name='Track'+side,v=tracks['v'][used],n=tracks['n'][used],uv=tracks['uv'][used],f=inv.reshape(-1,3),mat=tracks['mat']))

def render(items,title,eye=(1,.5,1),size=(900,650),semantic_color=False):
    eye=np.array(eye,float);eye/=np.linalg.norm(eye);right=np.cross([0,1,0],eye);right/=np.linalg.norm(right);up=np.cross(eye,right);rot=np.array([right,up,eye]).T
    pts=np.concatenate([p['v'] for p in items])@rot;low=pts[:,:2].min(0);high=pts[:,:2].max(0)
    scale=min((size[0]-50)/max(high[0]-low[0],.1),(size[1]-90)/max(high[1]-low[1],.1));center=(low+high)/2
    im=Image.new('RGB',size,'#17212d');draw=ImageDraw.Draw(im);polys=[]
    palette={'Hull':[110,170,185],'Turret':[220,170,75],'GunMount':[170,100,200],'Barrel':[225,90,75]}
    for p in items:
        q=p['v']@rot;xy=(q[:,:2]-center)*[scale,-scale]+[size[0]/2,size[1]/2+15];f=p['f']
        t=p['v'][f];n=np.cross(t[:,1]-t[:,0],t[:,2]-t[:,0]);n/=np.maximum(np.linalg.norm(n,axis=1,keepdims=True),1e-10)
        light=.40+.60*np.abs(n@np.array([.4,.85,.34]))
        mat=doc['materials'][p['mat']];ti=mat['pbrMetallicRoughness']['baseColorTexture']['index'];tex=np.array(images[doc['textures'][ti]['source']]);uv=p['uv'][f].mean(1)%1;h,w=tex.shape[:2]
        color=tex[(uv[:,1]*(h-1)).astype(int),(uv[:,0]*(w-1)).astype(int),:3].astype(float)
        if semantic_color:color[:]=palette.get(p['name'],[125,130,135])
        color=np.clip(color*light[:,None],0,255).astype('uint8')
        for i in range(len(f)):polys.append((q[f[i],2].mean(),xy[f[i]].flatten().tolist(),tuple(color[i])))
    for _,xy,c in sorted(polys,key=lambda p:p[0]):draw.polygon(xy,fill=c)
    draw.text((20,15),title,fill='white',font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',22))
    return im

if __name__=='__main__':
    for mi,m in enumerate(doc['materials']):
        texdir=OUT/'Textures';texdir.mkdir(exist_ok=True)
        for key,ti in [('color',m['pbrMetallicRoughness']['baseColorTexture']['index']),('orm',m['pbrMetallicRoughness']['metallicRoughnessTexture']['index']),('normal',m['normalTexture']['index'])]:
            im=images[doc['textures'][ti]['source']].copy();im.thumbnail((2048,2048))
            im.save(texdir/f'{key}{mi}.png')
        orm=np.asarray(Image.open(texdir/f'orm{mi}.png'));packed=np.zeros_like(orm);packed[:,:,0]=orm[:,:,2];packed[:,:,3]=255-orm[:,:,1]
        Image.fromarray(packed).save(texdir/f'metal{mi}.png')
        packed[:]=255;packed[:,:,1]=orm[:,:,0];Image.fromarray(packed).save(texdir/f'ao{mi}.png')
        normal=np.asarray(Image.open(texdir/f'normal{mi}.png'));packed[:]=255;packed[:,:,1]=normal[:,:,1];packed[:,:,3]=normal[:,:,0]
        Image.fromarray(packed).save(texdir/f'normalPacked{mi}.png')
    np.savez_compressed(OUT/'parts.npz',**{f'{i}_{k}':p[k] for i,p in enumerate(meshparts) for k in ['v','n','uv','f']})
    meta=[]
    for p in meshparts:
        meta.append({k:p[k] for k in ['name','mat']})
    (OUT/'parts.json').write_text(json.dumps(meta,indent=2))
    (OUT/'PartMap.json').write_text(json.dumps(assignment,indent=2))
    sheet=Image.new('RGB',(1800,1300))
    sets=[(meshparts,'Whole model - semantic groups'),([p for p in meshparts if p['name'] in ['Turret','Barrel','GunMount']],'Turret / cradle / barrel'),([p for p in meshparts if p['name']=='Hull'],'Hull - no turret or rotating wheels'),([p for p in meshparts if p['name'] in ['GunMount','Barrel']],'Gun mount and barrel')]
    for i,(items,title) in enumerate(sets):sheet.paste(render(items,title,semantic_color=True),((i%2)*900,(i//2)*650))
    sheet.save(OUT/'semantic-review.png')
    for i in [66,185,48]:
        p=dict(main);p['f']=main['f'][groups[i]];p['v']=main['v'];p['name']=str(i)
        # Crop extent to selected component.
        used,inv=np.unique(p['f'],return_inverse=True)
        p['v']=p['v'][used];p['uv']=p['uv'][used];p['f']=inv.reshape(-1,3)
        render([p],f'Component {i}',eye=(.1,1,.1)).save(OUT/f'component-{i}.png')
    print('Prepared',len(meshparts),'parts,',sum(len(p['f']) for p in meshparts),'triangles')

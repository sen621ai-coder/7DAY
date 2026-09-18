"""Separate authored surface tiles, remap UVs and build a welded distant mesh.

Derived normal/smoothness/AO are estimates from albedo, not high-poly bakes.
"""
import numpy as np
from PIL import Image, ImageFilter

def surfaces(root, out, images, doc):
    atlas=Image.open(root/'tools/Forestry/Textures/surface-atlas.png').convert('RGBA')
    w,h=atlas.size; sizes=[]
    for slot,(x,y) in enumerate(((0,0),(1,0),(0,1),(1,1)),2):
        tile=atlas.crop((x*w//2,y*h//2,(x+1)*w//2,(y+1)*h//2))
        tile.save(out/f'color{slot}.png'); sizes.append(list(tile.size))
        luminance=np.asarray(tile.convert('L').filter(ImageFilter.GaussianBlur(.6)),dtype=float)/255
        # Bounded small-scale relief; keep large lighting differences out of normals.
        low=np.asarray(tile.convert('L').filter(ImageFilter.GaussianBlur(5)),dtype=float)/255
        height=luminance-low
        dx=(np.roll(height,-1,1)-np.roll(height,1,1))*1.2
        # Image rows run down, while the engine's texture V runs up.
        dy=(np.roll(height,1,0)-np.roll(height,-1,0))*1.2
        n=np.dstack((-dx,-dy,np.ones_like(dx))); n/=np.linalg.norm(n,axis=2,keepdims=True)
        packed=np.full((*dx.shape,4),255,dtype=np.uint8)
        packed[:,:,3]=np.clip((n[:,:,0]*.5+.5)*255,0,255)
        packed[:,:,1]=np.clip((n[:,:,1]*.5+.5)*255,0,255)
        Image.fromarray(packed).save(out/f'normal{slot}.png')
        packed[:]=0; packed[:,:,0]=90 if slot==5 else 0
        packed[:,:,3]=np.clip((.24 if slot==5 else .10)+height*.15,0,1)*255
        Image.fromarray(packed).save(out/f'metal{slot}.png')
        packed[:]=255; packed[:,:,1]=np.clip(1+np.minimum(height,0)*.35,.8,1)*255
        Image.fromarray(packed).save(out/f'ao{slot}.png')
        doc['textures'].append({'source':len(images)}); images.append(tile)
        doc['materials'].append({'pbrMetallicRoughness':{'baseColorTexture':{'index':len(doc['textures'])-1}}})
    return sizes

def remap(parts):
    result=[]
    for pos,norm,uv,faces,mat in parts:
        if mat!=1:
            result.append([pos,norm,uv,faces,mat]); continue
        fuv=uv[faces]; u=fuv[:,:,0]; v=fuv[:,:,1]
        labels=np.ones(len(faces),dtype=int)
        labels[((u<=.507)&(v<=.49)).all(1)]=2
        labels[((u>=.507)&(v<=.49)).all(1)]=3
        wood=((u<.071)|(u>.255)|((u>.144)&(v>.603)))&(v>=.495)
        labels[wood.all(1)]=4
        metal=(((u>=.073)&(u<=.143))|((u>=.145)&(u<=.253)&(v<=.592)))&(v>=.495)
        labels[metal.all(1)]=5
        for label in np.unique(labels):
            selected=faces[labels==label]
            if label==1:
                ids,idx=np.unique(selected,return_inverse=True)
                result.append([pos[ids],norm[ids],uv[ids],idx.reshape(-1,3),int(label)]); continue
            p=pos[selected]; n=norm[selected]
            # World-space planar projections keep a consistent physical texel scale.
            # Use geometric normals, not averaged bevel normals, to select each plane.
            geometric=np.cross(p[:,1]-p[:,0],p[:,2]-p[:,0]); axis=np.abs(geometric).argmax(1)
            newuv=np.empty((len(p),3,2))
            for major,axes in ((0,(2,1)),(1,(0,2)),(2,(0,1))):
                mask=axis==major
                newuv[mask]=p[mask][:,:,axes]
            metres={2:1.6,3:1.8,4:1.15,5:1.0}[int(label)]
            newuv/=metres
            vertices=np.c_[p.reshape(-1,3),n.reshape(-1,3),newuv.reshape(-1,2)]
            unique,indices=np.unique(np.round(vertices,6),axis=0,return_inverse=True)
            result.append([unique[:,:3],unique[:,3:6],unique[:,6:],indices.reshape(-1,3),int(label)])
    merged=[]
    for material in sorted(set(p[4] for p in result)):
        entries=[p for p in result if p[4]==material]; offsets=np.cumsum([0]+[len(p[0]) for p in entries])
        merged.append([np.concatenate([p[i] for p in entries]) for i in range(3)]+
            [np.concatenate([p[3]+offsets[j] for j,p in enumerate(entries)]),material])
    return merged

def simplify(parts,cell=.11):
    """Spatial clustering per material/normal/UV island; never drop arbitrary faces."""
    result=[]
    for pos,norm,uv,faces,mat in parts:
        # Preserve hard corners and UV islands while welding short edges.
        key=np.c_[np.rint(pos/cell),np.rint(norm*3),np.floor(uv*4)]
        _,inverse=np.unique(key,axis=0,return_inverse=True)
        count=np.bincount(inverse); size=len(count)
        avg=lambda a: np.array([np.bincount(inverse,weights=a[:,i],minlength=size)/count for i in range(a.shape[1])]).T
        p=avg(pos); n=avg(norm); n/=np.maximum(np.linalg.norm(n,axis=1,keepdims=True),1e-9); t=avg(uv)
        f=inverse[faces]; keep=(f[:,0]!=f[:,1])&(f[:,0]!=f[:,2])&(f[:,1]!=f[:,2]); f=f[keep]
        area=np.linalg.norm(np.cross(p[f[:,1]]-p[f[:,0]],p[f[:,2]]-p[f[:,0]]),axis=1)
        f=f[area>1e-9]
        if len(f): result.append([p,n,t,f,mat])
    return result

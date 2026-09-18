"""Render exported current geometry and albedo, with studio light/shadow.
Not a Unity screenshot: no native postprocessing, normal-map or specular passes.
"""
from pathlib import Path
import struct,json
import numpy as np
from PIL import Image,ImageDraw,ImageFont
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'tools/Forestry/Renders';ASSETS=ROOT/'98-AECxProjectZ_Tweaks/Resources/Forestry'

def load():
    raw=(OUT/'scene.meshbin').read_bytes();offset=4
    def integers(n):
        nonlocal offset
        v=struct.unpack_from('<'+'i'*n,raw,offset);offset+=4*n;return v
    def floats(n):
        nonlocal offset
        v=np.frombuffer(raw,dtype='<f4',count=n,offset=offset).copy();offset+=4*n;return v
    def string():
        nonlocal offset
        n=integers(1)[0];s=raw[offset:offset+n].decode();offset+=n;return s
    assert raw[:4]==b'YFR1';nm,np_=integers(2);materials=[];parts=[]
    for _ in range(nm):
        name,texture=string(),string();values=floats(8)
        image=np.array(Image.open(ASSETS/texture).convert('RGBA'),dtype=np.float32)/255 if texture else np.ones((2,2,4),dtype=np.float32)
        materials.append((name,image,values[:4],values[4:6],values[6:8]))
    for _ in range(np_):
        name=string();m,n,k=integers(3);v=floats(n*8).reshape(n,8);f=np.array(integers(k)).reshape(-1,3)
        parts.append((name,v[:,:3],v[:,3:6],v[:,6:],f,m))
    assert offset==len(raw)
    return parts,materials

def basis(direction):
    d=np.array(direction,dtype=float);d/=np.linalg.norm(d)
    r=np.cross([0,1,0],d);r/=np.linalg.norm(r);return np.array([r,np.cross(d,r),d])

def fragments(screen,face,w,h):
    p=screen[face];lo=np.maximum(np.floor(p[:,:2].min(0)).astype(int),0);hi=np.minimum(np.ceil(p[:,:2].max(0)).astype(int),[w-1,h-1])
    if (hi<lo).any():return None
    x0,y0=lo;x1,y1=hi
    den=(p[1,1]-p[2,1])*(p[0,0]-p[2,0])+(p[2,0]-p[1,0])*(p[0,1]-p[2,1])
    if abs(den)<1e-8:return None
    yy,xx=np.mgrid[y0:y1+1,x0:x1+1];xx=xx+.5;yy=yy+.5
    a=((p[1,1]-p[2,1])*(xx-p[2,0])+(p[2,0]-p[1,0])*(yy-p[2,1]))/den
    b=((p[2,1]-p[0,1])*(xx-p[2,0])+(p[0,0]-p[2,0])*(yy-p[2,1]))/den;c=1-a-b
    z=a*p[0,2]+b*p[1,2]+c*p[2,2]
    return (slice(y0,y1+1),slice(x0,x1+1)),a,b,c,z,(a>=0)&(b>=0)&(c>=0)

def shadow(parts,light):
    B=basis(light);n=1500;allp=np.concatenate([p[1] for p in parts])@B.T
    lo=allp[:,:2].min(0)-.5;hi=allp[:,:2].max(0)+.5;scale=(n-1)/max(hi-lo);center=(lo+hi)/2
    depth=np.full((n,n),-np.inf,dtype=np.float32)
    for _,pos,norm,uv,faces,mat in parts:
        p=pos@B.T;p[:,:2]=(p[:,:2]-center)*scale+n/2
        for face in faces:
            hit=fragments(p,face,n,n)
            if hit is None:continue
            region,a,b,c,z,mask=hit;view=depth[region];mask&=z>view;view[mask]=z[mask]
    return B,center,scale,depth

def render(parts,materials,direction,filename,title):
    width,height=2400,1650;B=basis(direction);allp=np.concatenate([p[1] for p in parts])@B.T
    lo=allp[:,:2].min(0);hi=allp[:,:2].max(0);scale=min((width-200)/(hi[0]-lo[0]),(height-330)/(hi[1]-lo[1]));center=(lo+hi)/2
    light=B[2]*.45+np.array([0,.90,0]);light/=np.linalg.norm(light)
    sb,sc,ss,sd=shadow(parts,light)
    print('Shadow ready',filename,flush=True)
    canvas=np.full((height,width,3),[.91,.91,.89],dtype=np.float32);depth=np.full((height,width),-np.inf)
    # Neutral ground is a render-only studio surface, below the actual foundation.
    ground=np.array([[-7,-.02,-5],[7,-.02,-5],[7,-.02,5],[-7,-.02,5]])
    drawparts=parts+[('StudioGround',ground,np.tile([0,1,0],(4,1)),np.zeros((4,2)),np.array([[0,2,1],[0,3,2]]),len(materials))]
    mats=materials+[('Ground',np.ones((2,2,4)),np.array([.91,.91,.89,1]),np.ones(2),np.zeros(2))]
    for name,pos,norm,uv,faces,mat in drawparts:
        projected=pos@B.T;screen=projected.copy();screen[:,:2]=(screen[:,:2]-center)*scale;screen[:,1]*=-1;screen[:,:2]+=[width/2,height/2+40]
        material_name,tex,tint,uvscale,uvoffset=mats[mat];th,tw=tex.shape[:2]
        for face in faces:
            hit=fragments(screen,face,width,height)
            if hit is None:continue
            region,a,b,c,z,mask=hit;view=depth[region];mask&=z>view
            if not mask.any():continue
            wa=a[mask,None];wb=b[mask,None];wc=c[mask,None]
            coords=(wa*uv[face[0]]+wb*uv[face[1]]+wc*uv[face[2]])*uvscale+uvoffset
            tx=(coords[:,0]%1)*tw-.5;ty=((1-coords[:,1])%1)*th-.5
            ix=np.floor(tx).astype(int);iy=np.floor(ty).astype(int);fx=(tx-ix)[:,None];fy=(ty-iy)[:,None]
            color=((tex[iy%th,ix%tw]*(1-fx)+tex[iy%th,(ix+1)%tw]*fx)*(1-fy)+(tex[(iy+1)%th,ix%tw]*(1-fx)+tex[(iy+1)%th,(ix+1)%tw]*fx)*fy)*tint
            opaque=color[:,3]>.4 if material_name=='ForestrySurface0' else np.ones(len(color),dtype=bool)
            normals=wa*norm[face[0]]+wb*norm[face[1]]+wc*norm[face[2]];normals/=np.maximum(np.linalg.norm(normals,axis=1,keepdims=True),1e-8)
            # Match the source's explicitly double-sided appearance.
            normals*=np.where(normals@B[2]<0,-1,1)[:,None]
            world=wa*pos[face[0]]+wb*pos[face[1]]+wc*pos[face[2]];lp=world@sb.T;xy=(lp[:,:2]-sc)*ss+len(sd)/2
            sx=np.clip(xy[:,0].astype(int),0,len(sd)-1);sy=np.clip(xy[:,1].astype(int),0,len(sd)-1)
            visibility=np.zeros(len(sx))
            for dx,dy in ((0,0),(1,0),(-1,0),(0,1),(0,-1)):
                visibility+=(lp[:,2]+.025>=sd[np.clip(sy+dy,0,len(sd)-1),np.clip(sx+dx,0,len(sd)-1)])/5
            lambert=np.maximum(0,normals@light);shade=.65+.38*lambert*visibility
            if name=='StudioGround':shade=.85+.15*visibility
            rgb=np.clip(color[:,:3]*shade[:,None],0,1)
            yy,xx=np.nonzero(mask);canvas[region][yy[opaque],xx[opaque]]=rgb[opaque];view[yy[opaque],xx[opaque]]=z[mask][opaque]
    image=Image.fromarray((canvas*255).astype('uint8')).resize((1600,1100),Image.Resampling.LANCZOS)
    d=ImageDraw.Draw(image);font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',30);small=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',17)
    d.text((44,25),title,font=font,fill='#29343b')
    d.text((44,72),'当前几何与贴图 · 满库存 / 全配件展示 · 离线静态渲染',font=small,fill='#59666a')
    d.text((44,1055),'不含游戏内后处理、法线贴图与动态特效；光照为离线预览。',font=small,fill='#59666a')
    image.save(OUT/filename);print('Saved',filename,flush=True)

if __name__=='__main__':
    parts,materials=load()
    print('Scene',len(parts),'parts',sum(len(p[4]) for p in parts),'triangles',flush=True)
    render(parts,materials,[6,5,-10],'forestry-front.png','自动林场 · 正面')
    render(parts,materials,[-6,5,10],'forestry-back.png','自动林场 · 背面')

import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / ".local-tests/apache-research/python"))
import UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler

BUNDLE = ROOT / "ZZ-PZAEC_ApacheFlight/Resources/ApacheHelicopterPrefab.unity3d"
OUTPUT = ROOT / "tools/Renders/Apache-current-model.png"
SCALE = 1.5
WIDTH, HEIGHT = 1600, 1000


def quat_matrix(q):
    x, y, z, w = q["x"], q["y"], q["z"], q["w"]
    return np.array([
        [1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w), 0],
        [2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w), 0],
        [2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y), 0],
        [0, 0, 0, 1],
    ], dtype=float)


def trs(t):
    p, s = t["m_LocalPosition"], t["m_LocalScale"]
    m = quat_matrix(t["m_LocalRotation"])
    m[:3, :3] = m[:3, :3] @ np.diag([s["x"], s["y"], s["z"]])
    m[:3, 3] = [p["x"], p["y"], p["z"]]
    return m


def euler_matrix(x=0, y=0, z=0):
    x, y, z = np.radians([x, y, z])
    rx = np.array([[1,0,0],[0,np.cos(x),-np.sin(x)],[0,np.sin(x),np.cos(x)]])
    ry = np.array([[np.cos(y),0,np.sin(y)],[0,1,0],[-np.sin(y),0,np.cos(y)]])
    rz = np.array([[np.cos(z),-np.sin(z),0],[np.sin(z),np.cos(z),0],[0,0,1]])
    return rz @ ry @ rx


def transformed(vertices, center, scale, angles=(0,0,0)):
    return np.asarray(vertices, float) * np.asarray(scale) @ euler_matrix(*angles).T + np.asarray(center)


def cube(center, size, angles=(0,0,0)):
    v=np.array([[-.5,-.5,-.5],[.5,-.5,-.5],[.5,.5,-.5],[-.5,.5,-.5],[-.5,-.5,.5],[.5,-.5,.5],[.5,.5,.5],[-.5,.5,.5]])
    f=np.array([[0,2,1],[0,3,2],[4,5,6],[4,6,7],[0,1,5],[0,5,4],[2,3,7],[2,7,6],[1,2,6],[1,6,5],[3,0,4],[3,4,7]])
    return transformed(v,center,size,angles),f


def cylinder(center, scale, angles=(0,0,0), sides=16):
    v=[]
    for y in (-1,1):
        for i in range(sides):
            a=2*math.pi*i/sides;v.append((.5*math.cos(a),y,.5*math.sin(a)))
    v.extend([(0,-1,0),(0,1,0)]); f=[]
    for i in range(sides):
        j=(i+1)%sides
        f.extend([(i,j,sides+j),(i,sides+j,sides+i),(2*sides,i,j),(2*sides+1,sides+j,sides+i)])
    return transformed(v,center,scale,angles),np.asarray(f)


def rod(a,b,radius,sides=12):
    a,b=np.asarray(a,float),np.asarray(b,float); axis=b-a; length=np.linalg.norm(axis); y=axis/length
    helper=np.array([0,0,1.]) if abs(y[2])<.9 else np.array([1.,0,0])
    x=np.cross(helper,y);x/=np.linalg.norm(x);z=np.cross(y,x)
    v=[]
    for end in (a,b):
        for i in range(sides):
            q=2*math.pi*i/sides;v.append(end+x*math.cos(q)*radius+z*math.sin(q)*radius)
    f=[]
    for i in range(sides):
        j=(i+1)%sides;f.extend([(i,j,sides+j),(i,sides+j,sides+i)])
    return np.asarray(v),np.asarray(f)


def main():
    env=UnityPy.load(str(BUNDLE)); objects={o.path_id:o for o in env.objects}
    typed={o.path_id:o.read_typetree() for o in env.objects if o.type.name in ("Transform","GameObject","MeshFilter","MeshRenderer","Material")}
    transforms={i:d for i,d in typed.items() if objects[i].type.name=="Transform"}
    gameobjects={i:d for i,d in typed.items() if objects[i].type.name=="GameObject"}
    go_transform={d["m_GameObject"]["m_PathID"]:i for i,d in transforms.items()}
    world_cache={}
    def world_matrix(i):
        if i in world_cache:return world_cache[i]
        t=transforms[i]; p=t["m_Father"]["m_PathID"]
        world_cache[i]=(world_matrix(p) if p in transforms else np.eye(4)) @ trs(t)
        return world_cache[i]
    def path(i):
        t=transforms[i];go=gameobjects[t["m_GameObject"]["m_PathID"]];p=t["m_Father"]["m_PathID"]
        return (path(p)+"/" if p in transforms else "")+go["m_Name"]

    textures={}
    for o in env.objects:
        if o.type.name=="Texture2D":
            try:textures[o.path_id]=np.asarray(o.read().image.convert("RGB"))
            except Exception:pass
    body_texture=textures.get(9115546322405214032)
    meshes=[]
    for oid,renderer in typed.items():
        if objects[oid].type.name!="MeshRenderer":continue
        goid=renderer["m_GameObject"]["m_PathID"]; tid=go_transform.get(goid)
        if tid is None:continue
        mesh_id=0
        for c in gameobjects[goid]["m_Component"]:
            cid=c["component"]["m_PathID"]
            if cid in objects and objects[cid].type.name=="MeshFilter":mesh_id=typed[cid]["m_Mesh"]["m_PathID"]
        if mesh_id not in objects:continue
        mesh=objects[mesh_id].read();h=MeshHandler(mesh);h.process()
        verts=np.asarray(h.m_Vertices,float); verts=np.c_[verts,np.ones(len(verts))] @ world_matrix(tid).T;verts=verts[:,:3]
        uv=np.asarray(h.m_UV0,float) if h.m_UV0 else None
        material_id=renderer.get("m_Materials",[{}])[0].get("m_PathID",0) if renderer.get("m_Materials") else 0
        material=typed.get(material_id,{}).get("m_Name","")
        meshes.append((path(tid),material,verts,uv,[np.asarray(x,int) for x in h.get_triangles()]))

    triangles=[]
    # The runtime now preserves the original grey camouflage texture. These
    # neutral fallbacks are used only where a mesh has no readable UV sample.
    body=np.array([186,190,188.]);hardware=np.array([74,78,78.]);rotor=np.array([31,36,34.]);glass=np.array([27,48,55.])
    for name,material,verts,uv,submeshes in meshes:
        is_glass=material.lower()=="windows"
        base=glass if is_glass else rotor if ("Propeller" in name or "Rotor" in name) else hardware if any(x in name for x in ("Weapon","Wheel","Rocket","Missile")) else body
        for faces in submeshes:
            for face in faces:
                tri=verts[face]; color=base.copy()
                if body_texture is not None and uv is not None and not is_glass:
                    q=uv[face].mean(axis=0);tx=int((q[0]%1)*(body_texture.shape[1]-1));ty=int(((1-q[1])%1)*(body_texture.shape[0]-1))
                    sample=body_texture[ty,tx].astype(float)
                    color=sample
                triangles.append([tri,color,is_glass])

    # Current runtime-generated compact mount and M230-style gun.
    anchor=np.array([0,.48,3.58]); proc=[]
    def add(shape,color): proc.append((shape[0]+anchor,shape[1],np.array(color,float)))
    gunmetal=(69,74,74);steel=(51,56,59);dark=(17,20,21);fairing=(107,112,110)
    add(cube((0,.57,-.10),(.38,.055,.31)),fairing)
    add(cylinder((0,.43,-.10),(.15,.13,.15)),steel);add(cylinder((0,.26,-.10),(.27,.05,.27)),steel);add(cylinder((0,.13,-.10),(.18,.08,.18)),dark)
    for side in (-1,1):
        add(rod((side*.24,.54,-.10),(side*.16,.28,-.10),.013),steel);add(cylinder((side*.16,.28,-.10),(.045,.018,.045),(0,0,90)),dark)
    for shape,color in [
        (cube((0,.01,.08),(.22,.17,.34)),gunmetal),(cube((0,0,-.18),(.19,.14,.17)),dark),(cube((0,.115,.05),(.18,.035,.28)),steel),
        (cube((-.145,.005,-.05),(.09,.12,.22),(0,0,-10)),gunmetal),(cube((.14,0,.04),(.08,.11,.22)),steel),
        (cylinder((.185,0,-.05),(.075,.035,.075),(0,0,90)),dark)]:add(shape,color)
    for side in (-1,1):
        add(cube((side*.16,.07,-.09),(.04,.23,.20),(10,0,0)),gunmetal);add(cylinder((side*.20,.055,-.05),(.10,.03,.10),(0,0,90)),steel);add(cylinder((side*.232,.055,-.05),(.04,.014,.04),(0,0,90)),dark)
    add(rod((-.16,.02,-.20),(-.22,.13,-.34),.019),dark);add(rod((-.22,.13,-.34),(-.10,.22,-.40),.019),steel)
    add(cylinder((0,0,.26),(.13,.045,.13),(90,0,0)),dark);add(cylinder((0,0,.48),(.105,.21,.105),(90,0,0)),steel);add(cylinder((0,0,.89),(.055,.31,.055),(90,0,0)),dark)
    for i in range(3):add(cylinder((0,0,.31+i*.13),(.12,.025,.12),(90,0,0)),dark)
    add(cylinder((0,0,1.17),(.075,.055,.075),(90,0,0)),steel)
    for verts,faces,color in proc:
        for face in faces:triangles.append([verts[face],color,False])

    all_points=np.concatenate([t[0] for t in triangles]);target=np.array([0,1.15,.35]);camera=np.array([-14.5,4.25,16.5])
    forward=target-camera;forward/=np.linalg.norm(forward);right=np.cross(forward,[0,1,0]);right/=np.linalg.norm(right);up=np.cross(right,forward)
    projected=np.stack([(all_points-target)@right,(all_points-target)@up],axis=1)
    pmin,pmax=projected.min(axis=0),projected.max(axis=0);pcenter=(pmin+pmax)*.5
    extent=(pmax-pmin)*.5;scale=min(WIDTH*.455/extent[0],HEIGHT*.435/extent[1])*SCALE
    cx,cy=WIDTH*SCALE*.5,HEIGHT*SCALE*.485
    light=np.array([-1.1,1.7,1.4]);light/=np.linalg.norm(light)
    draw_items=[]
    for tri,base,is_glass in triangles:
        rel=tri-target;pts=np.stack([rel@right,rel@up],axis=1)-pcenter;screen=np.stack([cx+pts[:,0]*scale,cy-pts[:,1]*scale],axis=1)
        depth=((tri-camera)@forward).mean();n=np.cross(tri[1]-tri[0],tri[2]-tri[0]);ln=np.linalg.norm(n)
        if ln<1e-8:continue
        n/=ln
        if np.dot(n,camera-tri.mean(axis=0))<0:n=-n
        lum=.70+.52*max(0,float(np.dot(n,light)))
        color=np.clip(base*lum+np.array([15,17,13]),0,255).astype(int)
        if is_glass:color=np.clip(color*1.32+np.array([8,20,24]),0,255).astype(int)
        draw_items.append((depth,screen,tuple(color),is_glass))

    sw,sh=int(WIDTH*SCALE),int(HEIGHT*SCALE)
    yy=np.linspace(0,1,sh)[:,None,None];top=np.array([28,35,38]);bottom=np.array([105,103,91]);bg=(top*(1-yy)+bottom*yy);bg=np.repeat(bg,sw,axis=1).astype(np.uint8)
    image=Image.fromarray(bg,"RGB");shadow=Image.new("RGBA",(sw,sh));sd=ImageDraw.Draw(shadow)
    sd.ellipse((sw*.18,sh*.69,sw*.84,sh*.89),fill=(0,0,0,110));shadow=shadow.filter(ImageFilter.GaussianBlur(36*SCALE));image=Image.alpha_composite(image.convert("RGBA"),shadow)
    layer=Image.new("RGBA",(sw,sh));d=ImageDraw.Draw(layer)
    for _,pts,color,is_glass in sorted(draw_items,key=lambda x:x[0],reverse=True):
        d.polygon([tuple(p) for p in pts],fill=(*color,205 if is_glass else 255))
    image=Image.alpha_composite(image,layer)
    # Warm marker for the actual nose spotlight node; subtle enough to keep geometry readable.
    lamp=np.array([0,1.575,4.092]);r=(lamp-target)@right-pcenter[0];u=(lamp-target)@up-pcenter[1];lx,ly=cx+r*scale,cy-u*scale
    glow=Image.new("RGBA",(sw,sh));gd=ImageDraw.Draw(glow);gd.ellipse((lx-16*SCALE,ly-16*SCALE,lx+16*SCALE,ly+16*SCALE),fill=(255,224,150,110));glow=glow.filter(ImageFilter.GaussianBlur(11*SCALE));image=Image.alpha_composite(image,glow)
    image=image.convert("RGB").resize((WIDTH,HEIGHT),Image.Resampling.LANCZOS)
    OUTPUT.parent.mkdir(parents=True,exist_ok=True);image.save(OUTPUT,quality=96)
    print(f"Rendered {len(draw_items)} triangles to {OUTPUT}")


if __name__ == "__main__":
    main()

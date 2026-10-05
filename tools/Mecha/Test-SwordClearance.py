"""Check the actual weighted armour surfaces against the native blade capsule.
Consumes poses exported by MotionNativeQA; does not reproduce the pose solver.
"""
import argparse, json, pathlib, numpy as np
p=argparse.ArgumentParser();p.add_argument('poses',type=pathlib.Path);p.add_argument('--resources',type=pathlib.Path);args=p.parse_args()
root=pathlib.Path(__file__).resolve().parents[2];res=root/'ZZ-PZAEC_Mecha/Resources';stem='samurai_style_gundam_mecha'
if args.resources:res=args.resources
doc=json.loads((res/(stem+'_rig.json')).read_text());raw=(res/(stem+'_rig.bin')).read_bytes();poses=json.loads(args.poses.read_text())
dtype=np.dtype([('data','<f4',8),('bones','<i4',2),('weights','<f4',2)])
rest=np.array([j['position'] for j in doc['joints']],dtype=np.float32);parts=[]
for part in doc['parts']:
    if part['role'].startswith('Sword'):continue
    records=np.frombuffer(raw,dtype=dtype,count=part['vertices'],offset=part['offset']);ix=np.frombuffer(raw,dtype='<u4',count=part['indices'],offset=part['offset']+48*part['vertices']).reshape(-1,3)
    local=records['data'][:,:3][:,None,:]-rest[records['bones']];boundBones=[];bounds=[]
    for bone in np.unique(records['bones']):
        selected=(records['bones']==bone)&(records['weights']>0)
        if not selected.any():continue
        pts=local[selected];lo=pts.min(0);hi=pts.max(0);boundBones.append(bone);bounds.append([[x,y,z] for x in (lo[0],hi[0]) for y in (lo[1],hi[1]) for z in (lo[2],hi[2])])
    # Weighted skin points lie inside the union of these transformed joint
    # boxes. Use that conservative bound to skip distant parts, never to
    # declare a nearby surface collision.
    parts.append((part['role'],records,ix,local,np.array(boundBones),np.array(bounds,dtype=np.float32)))

def capsule_distance(a,b,tri):
    c,d,e=tri[:,0],tri[:,1],tri[:,2];n=np.cross(d-c,e-c);nn=np.linalg.norm(n,axis=1);n/=np.maximum(nn[:,None],1e-12)
    da=((a-c)*n).sum(1);db=((b-c)*n).sum(1);cross=da*db<=0
    t=np.where(cross,da/np.where(abs(da-db)>1e-12,da-db,1),np.where(abs(da)<abs(db),0,1));t=np.clip(t,0,1)
    s=a+(b-a)*t[:,None];dp=((s-c)*n).sum(1);q=s-n*dp[:,None]
    inside=(np.cross(d-c,q-c)*n).sum(1)>=-1e-8
    inside&=(np.cross(e-d,q-d)*n).sum(1)>=-1e-8;inside&=(np.cross(c-e,q-e)*n).sum(1)>=-1e-8;inside&=nn>1e-10
    best=np.where(inside,abs(dp),np.inf)
    u=b-a;aa=u@u
    for first,last in ((c,d),(d,e),(e,c)):
        v=last-first;w=a-first;bb=v@u;cc=(v*v).sum(1);dd=w@u;ee=(v*w).sum(1);den=aa*cc-bb*bb
        s=np.clip(np.where(den>1e-12,(bb*ee-cc*dd)/np.maximum(den,1e-12),0),0,1)
        t=np.clip((bb*s+ee)/np.maximum(cc,1e-12),0,1);s=np.clip((bb*t-dd)/max(aa,1e-12),0,1);t=np.clip((bb*s+ee)/np.maximum(cc,1e-12),0,1)
        dist=np.linalg.norm(a+u*s[:,None]-first-v*t[:,None],axis=1);best=np.minimum(best,dist)
    return best

fail=[];minimum=100.;radius=.065;hand=next(i for i,j in enumerate(doc['joints']) if j['name']=='HandR')
hiltLocal=np.array(doc['swordAnchors']['hiltTip'])-np.array(doc['swordAnchors']['grip'])
for pose in poses:
    mats=np.array(pose['bones'],dtype=np.float32).reshape(-1,3,4);a=np.array(pose['root']);b=np.array(pose['tip']);hiltA=mats[hand,:,3];hiltB=mats[hand,:,:3]@hiltLocal+hiltA
    for role,v,ix,local,boundBones,bounds in parts:
        box=np.einsum('ijk,ilk->ilj',mats[boundBones,:,:3],bounds)+mats[boundBones,:,3][:,None,:];lo=box.min((0,1))-.001;hi=box.max((0,1))+.001
        near=False
        for start,end in ((a,b),(hiltA,hiltB)):
            if (hi>=np.minimum(start,end)-radius).all() and (lo<=np.maximum(start,end)+radius).all():near=True;break
        if not near:continue
        bone=v['bones']
        xyz=(np.einsum('nijk,nik->nij',mats[bone,:,:3],local)+mats[bone,:,3])
        xyz=(xyz*v['weights'][:,:,None]).sum(1);tri=xyz[ix]
        for proxy,start,end in [('blade',a,b),('hilt',hiltA,hiltB)]:
            # The first 28 cm contains the gripping hand. Check the remaining
            # counterweight against the real right forearm and upper armour.
            if proxy=='hilt' and role=='ArmR':start=hiltA+(hiltB-hiltA)*.4
            lo=np.minimum(start,end)-radius;hi=np.maximum(start,end)+radius;mask=(tri.max(1)>=lo).all(1)&(tri.min(1)<=hi).all(1)
            if mask.any():
                distances=capsule_distance(start,end,tri[mask]);nearest=int(np.flatnonzero(mask)[distances.argmin()]);distance=float(distances.min());minimum=min(minimum,distance)
                if distance<radius-.005:fail.append(dict(action=pose['action'],frame=pose['frame'],role=role,proxy=proxy,distance=distance,triangle=nearest,restCenter=v['data'][ix[nearest],:3].mean(0).tolist(),bones=v['bones'][ix[nearest]].tolist(),weights=v['weights'][ix[nearest]].tolist()))
report=dict(samples=len(poses),capsuleRadius=radius,minimumSurfaceDistance=minimum,failures=fail)
args.poses.with_name('sword-clearance.json').write_text(json.dumps(report,indent=2))
if fail:
    print('FAIL actual armour surface contacts',len(fail));print(json.dumps(fail[:12],indent=2));raise SystemExit(1)
print('PASS actual skinned armour / shield / wing surfaces:',len(poses),'poses, capsule radius',radius,'minimum distance',minimum)

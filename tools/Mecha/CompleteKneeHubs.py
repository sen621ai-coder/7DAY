"""Closed rigid hinge drums inside the two open armour seams.

The source is a static sculpture: rotating its separated thigh/shin shells
exposes their empty interiors. A bevelled axle fills that seam without skin
blending or stretching hard armour across the knee.
"""
import numpy as np


def build(dtype, bone, pivot):
    segments=24
    rings=[(-.145,.12),(-.105,.16),(.105,.16),(.145,.12)]
    points=[]
    for x,radius in rings:
        for i in range(segments):
            angle=2*np.pi*i/segments
            points.append(np.array(pivot)+[x,radius*np.cos(angle),radius*np.sin(angle)])
    points=np.array(points);triangles=[]
    for ring in range(3):
        for i in range(segments):
            a=ring*segments+i;b=ring*segments+(i+1)%segments
            triangles.extend([(a,b,b+segments),(a,b+segments,a+segments)])
    for i in range(1,segments-1):
        triangles.extend([(0,i+1,i),(72,72+i,72+i+1)])
    faces=points[np.array(triangles)]
    if np.sum(np.einsum('ij,ij->i',faces[:,0],np.cross(faces[:,1],faces[:,2])))<0:
        faces=faces[:,[0,2,1]]
    n=np.cross(faces[:,1]-faces[:,0],faces[:,2]-faces[:,0]);n/=np.linalg.norm(n,axis=1)[:,None]
    records=np.zeros((len(faces),3),dtype=dtype)
    records['vertex'][:,:,:3]=faces;records['vertex'][:,:,3:6]=n[:,None,:]
    records['vertex'][:,:,6:8]=[.8672464,.88142943] # graphite metal from shin texture
    records['bones']=bone;records['weights']=[1,0]
    return records

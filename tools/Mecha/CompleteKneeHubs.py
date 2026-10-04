"""Compact internal ball joint with faceted white/red/gold patella armour.

Every component is closed and rigid. The original sculpture is untouched;
rounded graphite stays behind the thin pointed plates instead of presenting
an exposed flat-ended barrel during a deep crouch.
"""
import numpy as np

PALETTE = dict(core=(.8672464,.88142943),white=(.86444962,.95656884),
               red=(.30808157,.38221270),gold=(.82415450,.44282442))


def build(dtype, bone, pivot):
    chunks=[]
    def emit(points, triangles, color, smooth=False):
        points=np.array(points,dtype=float);faces=points[np.array(triangles)]
        if np.sum(np.einsum('ij,ij->i',faces[:,0],np.cross(faces[:,1],faces[:,2])))<0:
            faces=faces[:,[0,2,1]]
        normals=np.cross(faces[:,1]-faces[:,0],faces[:,2]-faces[:,0])
        normals/=np.linalg.norm(normals,axis=1)[:,None]
        records=np.zeros((len(faces),3),dtype=dtype)
        records['vertex'][:,:,:3]=faces+np.array(pivot)
        if smooth:
            normals=faces/np.array([.100**2,.140**2,.140**2])
            normals/=np.linalg.norm(normals,axis=2)[:,:,None]
            records['vertex'][:,:,3:6]=normals
        else:records['vertex'][:,:,3:6]=normals[:,None,:]
        records['vertex'][:,:,6:8]=PALETTE[color]
        records['bones']=bone;records['weights']=[1,0];chunks.append(records)
    # Ellipsoid poles lie along the hinge axis. No flat circular end caps.
    segments,bands=24,8;points=[[-.100,0,0]]
    for ring in range(1,bands):
        theta=np.pi*ring/bands
        for i in range(segments):
            phi=2*np.pi*i/segments
            points.append([-.100*np.cos(theta),.140*np.sin(theta)*np.cos(phi),.140*np.sin(theta)*np.sin(phi)])
    end=len(points);points.append([.100,0,0]);triangles=[]
    for i in range(segments):triangles.append((0,1+(i+1)%segments,1+i))
    for ring in range(bands-2):
        for i in range(segments):
            a=1+ring*segments+i;b=1+ring*segments+(i+1)%segments
            triangles.extend([(a,b,b+segments),(a,b+segments,a+segments)])
    start=1+(bands-2)*segments
    for i in range(segments):triangles.append((end,start+i,start+(i+1)%segments))
    emit(points,triangles,'core',True)

    def plate(outline, edge, ridge, back, color, tilt=0):
        n=len(outline);cx=sum(x for x,y in outline)/n;cy=sum(y for x,y in outline)/n
        points=[[x,y,edge] for x,y in outline]+[[cx,cy,ridge]]
        points += [[x,y,back] for x,y in outline]+[[cx,cy,back]]
        triangles=[]
        for i in range(n):
            j=(i+1)%n;a=n+1+i;b=n+1+j
            triangles.extend([(n,i,j),(2*n+1,b,a),(i,a,b),(i,b,j)])
        if tilt:
            angle=np.deg2rad(tilt);c,s=np.cos(angle),np.sin(angle)
            points=np.array(points)@np.array([[1,0,0],[0,c,s],[0,-s,c]])
        emit(points,triangles,color)
    plate([(0,.145),(-.085,.07),(-.092,-.025),(0,-.155),(.092,-.025),(.085,.07)],.130,.170,.116,'white')
    plate([(0,.090),(-.026,.018),(0,-.083),(.026,.018)],.168,.179,.150,'red')
    plate([(0,-.105),(-.020,-.116),(0,-.148),(.020,-.116)],.139,.148,.126,'gold')
    # Upper overlapping lamella faces forward as the lower leg folds.
    plate([(0,.100),(-.080,.045),(-.088,-.025),(0,-.095),(.088,-.025),(.080,.045)],.137,.165,.125,'white',-55)
    return np.concatenate(chunks)

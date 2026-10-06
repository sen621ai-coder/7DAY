"""Conforming refinement only where narrow joint blends cross coarse triangles."""
import numpy as np

def refine(f, labels, materials, tags, apply, protected=()):
    weighted=apply(f.copy())
    q=f['vertex'][:,:,:3]
    edges=[(0,1),(1,2),(2,0)]
    def key(a,b):
        a=tuple(np.round(a,5));b=tuple(np.round(b,5))
        return tuple(sorted((a,b)))
    marked=set()
    for a,b in edges:
        length=np.linalg.norm(q[:,a]-q[:,b],axis=1)
        difference=abs(weighted['weights'][:,a,1]-weighted['weights'][:,b,1])
        for k in np.flatnonzero((length>.06)&(difference>.15)):
            marked.add(key(q[k,a],q[k,b]))
    out=[];owners=[];mats=[];roles=[]
    for face,label,mat,tag in zip(f,labels,materials,tags):
        if label in protected:
            out.append(face);owners.append(label);mats.append(mat);roles.append(tag);continue
        points=list(face['vertex']);mask=[]
        for a,b in edges:
            if key(points[a][:3],points[b][:3]) in marked:
                mid=(points[a]+points[b])*.5;mid[3:6]/=max(np.linalg.norm(mid[3:6]),1e-12)
                mask.append(len(points));points.append(mid)
            else:mask.append(None)
        # Split each marked boundary edge in all incident triangles. This is
        # conforming across duplicated UV vertices and separate bone owners.
        triangles=[[0,1,2]]
        for (a,b),mid in zip(edges,mask):
            if mid is None:continue
            replacement=[]
            for tri in triangles:
                if a in tri and b in tri:
                    for i in range(3):
                        x,y,z=tri[i],tri[(i+1)%3],tri[(i+2)%3]
                        if {x,y}=={a,b}:
                            replacement.extend([[x,mid,z],[mid,y,z]]);break
                else:replacement.append(tri)
            triangles=replacement
        for ix in triangles:
            rec=np.zeros(3,f.dtype);rec['vertex']=np.array(points)[ix];rec['bones']=label;rec['weights']=[1,0]
            out.append(rec);owners.append(label);mats.append(mat);roles.append(tag)
    return np.array(out),np.array(owners),np.array(mats),np.array(roles)

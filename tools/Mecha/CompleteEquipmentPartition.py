"""Separate the fused shield along surface connectivity, not a slicing box."""
import numpy as np
from scipy.spatial import cKDTree
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import maximum_flow,breadth_first_order

def shield(c,roles,generated,bone,ids,aa,bb,length,cos,area):
 x,y,z=c.T
 # The rear red/white crest projects beyond the old shield bounding box.
 # Keep its surface in the equipment partition instead of leaving it on the shoulder.
 crest=(x<-.90)&(y>2.10)&(y<2.38)&(z>-.40)&(z<-.18)
 domain=~generated&((roles=='Shield')|(((x<-.52)&(y>1.30)&(y<2.80)&(z>-.25)&(z<.85)|crest)&np.isin(bone,[ids[n] for n in ['Torso','ShoulderL','ElbowL','HandL']])))
 # Front-view source picks: the white plate AND its top handle are shield,
 # including faces above the old y=2.40 shoulder cutoff. Coordinates were
 # traced from the intact bind-pose surface, not inferred from paint colour.
 anchors=np.array([[-.88517,2.50817,.08163],[-.91029,2.47340,.00574],[-.87026,2.54724,.13586],[-.82955,2.67557,.14893],[-.90986,2.54275,-.15516],[-.86100,2.59983,.14029],[-.86060,2.44988,.23691],[-.81648,2.35889,.43825]])
 front=cKDTree(anchors).query(c)[0]<.065
 exterior=domain&(((y<1.35)&(x<-.90))|((x<-.99)&(y<2.30))|crest|front)
 pauldron=np.linalg.norm(c-[-.74376,2.61685,.48494],axis=1)<.055
 limb=domain&((x>-.73)|pauldron)
 assert exterior.sum()>20 and limb.sum()>20
 ds=cKDTree(c[exterior]).query(c)[0];da=cKDTree(c[limb]).query(c)[0]
 cost_s=area*.25*ds/np.maximum(ds+da,1e-8)
 cost_a=area*.25*da/np.maximum(ds+da,1e-8)
 # Disconnected decorative islands have no graph path to the shield seed.
 # Retain their archived shield ownership unless actual limb evidence wins.
 cost_a[roles=='Shield']+=area[roles=='Shield']*.15
 cost_s[~domain|limb]=100.;cost_a[exterior]=100.
 edge=domain[aa]&domain[bb];a,b=aa[edge],bb[edge]
 weight=length[edge]*(.05+.95*np.clip(cos[edge],0,1)**4)*.1
 n=len(c);nodes=np.arange(n);row=np.r_[a,b,np.full(n,n),nodes];col=np.r_[b,a,nodes,np.full(n,n+1)];val=np.r_[weight,weight,cost_s,cost_a]
 take=val>1e-12;graph=coo_matrix((np.maximum(1,np.rint(val[take]*1e6)).astype(np.int64),(row[take],col[take])),shape=(n+2,n+2)).tocsr()
 flow=maximum_flow(graph,n,n+1);residual=graph-flow.flow;residual.data=(residual.data>0).astype(np.int64);residual.eliminate_zeros();reachable=breadth_first_order(residual,n,directed=True,return_predecessors=False)
 selected=np.ones(n,bool);selected[reachable[reachable<n]]=False;selected&=domain
 assert selected[exterior].all() and not selected[limb].any()
 return domain,selected

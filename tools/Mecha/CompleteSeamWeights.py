"""Narrow flexible seams in a formerly unrigged continuous sculpture.

Only physically shared arm seams blend. Plate centres, weapons, wings and the
hatch remain rigid. Identical rest positions have identical deformation at a
shared seam, including UV-split duplicates in separate render batches.
"""
import numpy as np
from scipy.spatial import cKDTree

def prepare(f, labels, names):
    ids = {name: i for i, name in enumerate(names)}
    seams = []
    points = f['vertex'][:, :, :3]
    for side in ['L', 'R']:
        for upper, lower, width in [('Torso', 'Shoulder'+side, .075),
                                     ('Shoulder'+side, 'Elbow'+side, .055),
                                     ('Elbow'+side, 'Hand'+side, .035)]:
            a, b = ids[upper], ids[lower]
            pa = np.unique(np.round(points[labels == a].reshape(-1, 3), 5), axis=0)
            pb = np.unique(np.round(points[labels == b].reshape(-1, 3), 5), axis=0)
            if not len(pa) or not len(pb): continue
            distance, near = cKDTree(pa).query(pb)
            shared = pb[distance < 1e-7]
            if len(shared): seams.append((a, b, width, cKDTree(shared)))

    # Keep the actual fingers touching the grip on the hand transform. A wrist
    # blend reaching those UV-split contact vertices made fingers lag behind
    # the sword even though the arm-only seam test passed.
    hand_points=np.unique(points[labels==ids['HandR']].reshape(-1,3),axis=0)
    sword_points=np.unique(points[labels==ids['Sword']].reshape(-1,3),axis=0)
    distance,_=cKDTree(sword_points).query(hand_points)
    grip_tree=cKDTree(hand_points[distance<2e-6])

    def apply(records):
        flat = records.reshape(-1)
        owner = flat['bones'][:, 0].copy()
        best = np.full(len(flat), np.inf)
        for a, b, width, tree in seams:
            selected = np.flatnonzero((owner == a) | (owner == b))
            distance, _ = tree.query(flat['vertex'][selected, :3])
            take = (distance < width) & (distance / width < best[selected])
            selected, distance = selected[take], distance[take]
            t = np.clip(distance / width, 0, 1)
            # Smooth derivative at both ends avoids a visible crease in the band.
            weight = .5 + .5 * t * t * (3 - 2*t)
            flat['bones'][selected, 1] = np.where(owner[selected] == a, b, a)
            flat['weights'][selected, 0] = weight
            flat['weights'][selected, 1] = 1-weight
            best[selected] = t
        selected=np.flatnonzero((owner==ids['HandR'])|(owner==ids['ElbowR']))
        distance,_=grip_tree.query(flat['vertex'][selected,:3]);take=distance<.045
        selected,distance=selected[take],distance[take]
        hand_weight=((flat['bones'][selected]==ids['HandR'])*flat['weights'][selected]).sum(1)
        t=np.clip(1-distance/.045,0,1);lock=t*t*(3-2*t)
        hand_weight+=lock*(1-hand_weight)
        hand_owner=owner[selected]==ids['HandR']
        flat['bones'][selected,1]=np.where(hand_owner,ids['ElbowR'],ids['HandR'])
        flat['weights'][selected,0]=np.where(hand_owner,hand_weight,1-hand_weight)
        flat['weights'][selected,1]=1-flat['weights'][selected,0]
        rigid=flat['weights'][:,1]==0
        flat['bones'][rigid,1]=flat['bones'][rigid,0]
        return records
    return apply

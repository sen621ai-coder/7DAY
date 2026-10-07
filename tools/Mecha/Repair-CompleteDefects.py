"""Restore surfaces lost to decimation and remove off-surface spike triangles.

Pass A removes exterior triangles whose centroid floats far off the original
GLB surface (decimation spikes). Pass B reinserts original GLB triangles that
have no candidate exterior coverage (true see-through holes, verified by the
multi-view pixel detector), rigidly bound per-vertex to the nearest candidate
bone. Runs as a post-process after Rebuild-CompletePanels.py; provenance is
recorded in rig.json under 'defectPatch'.
"""
import argparse, collections, hashlib, json, struct, sys
from pathlib import Path
import numpy as np
from scipy.spatial import cKDTree

p = argparse.ArgumentParser()
p.add_argument('--resources', required=True, type=Path)
p.add_argument('--spike-mm', type=float, default=12.0)
p.add_argument('--cover-mm', type=float, default=4.0)
p.add_argument('--dry-run', action='store_true')
p.add_argument('--lost', default='.local-tests/Mecha-0.17.0/lost-faces.json')
a = p.parse_args()
stem = 'samurai_style_gundam_mecha'
res = a.resources
doc = json.loads((res / f'{stem}_rig.json').read_text())
prior = doc.pop('defectPatch', None)
if prior:
    print('appending to existing defect patch:', prior['reinsertedFaces'], 'faces already in')
blob = bytearray((res / f'{stem}_rig.bin').read_bytes())
dt = np.dtype([('v', '<f4', 8), ('b', '<i4', 2), ('w', '<f4', 2)])
names = [j['name'] for j in doc['joints']]

parts = doc['parts']
exterior_idx = [i for i, p_ in enumerate(parts)
                if not p_['nodeName'].startswith(('Complete_Liner_', 'Complete_InnerShell_'))]
pf, powner, pgen = [], [], []
for i in exterior_idx:
    part = parts[i]
    v = np.frombuffer(blob, dt, part['vertices'], part['offset'])
    ix = np.frombuffer(blob, '<u4', part['indices'], part['offset'] + 48 * part['vertices']).reshape(-1, 3)
    pf.append(v[ix].copy())
    powner += [i] * len(ix)
    pgen += [bool(part.get('generatedRepair'))] * len(ix)
pf = np.concatenate(pf)
powner = np.array(powner)
pgen = np.array(pgen)
pq = pf['v'][:, :, :3]
pbone = pf['b'][:, 0, 0]

# ---- original GLB surface
raw_glb = (res / f'{stem}.glb').read_bytes()
length = struct.unpack_from('<I', raw_glb, 12)[0]
g = json.loads(raw_glb[20:20 + length])
base = length + 28
world = {}
def walk(i, parent):
    node = g['nodes'][i]
    world[i] = parent @ np.array(node.get('matrix', np.eye(4).flatten(order='F'))).reshape(4, 4, order='F')
    for child in node.get('children', []):
        walk(child, world[i])
for i in g['scenes'][g.get('scene', 0)]['nodes']:
    walk(i, np.eye(4))
def accessor(i):
    x = g['accessors'][i]
    v = g['bufferViews'][x['bufferView']]
    n = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3}[x['type']]
    dtype = np.dtype({5126: '<f4', 5123: '<u2', 5125: '<u4'}[x['componentType']])
    return np.ndarray((x['count'], n), dtype, raw_glb, base + v.get('byteOffset', 0) + x.get('byteOffset', 0),
                      strides=(v.get('byteStride', n * dtype.itemsize), dtype.itemsize)).copy()
ov, on_, ouv, oidx = [], [], [], []
for i, node in enumerate(g['nodes']):
    if 'mesh' not in node:
        continue
    for primitive in g['meshes'][node['mesh']]['primitives']:
        at = primitive['attributes']
        v = accessor(at['POSITION']).astype(float) @ world[i][:3, :3].T + world[i][:3, 3]
        v[:, 0] *= -1
        nrm = accessor(at['NORMAL']).astype(float) @ np.linalg.inv(world[i][:3, :3])
        nrm[:, 0] *= -1
        uv = accessor(at['TEXCOORD_0']).astype(float)
        uv[:, 1] = 1 - uv[:, 1]
        ix = accessor(primitive['indices']).reshape(-1, 3)[:, [0, 2, 1]]
        ov.append(v)
        on_.append(nrm)
        ouv.append(uv)
        oidx.append(ix + sum(len(x) for x in ov[:-1]))
ov = np.concatenate(ov)
on_ = np.concatenate(on_)
ouv = np.concatenate(ouv)
oidx = np.concatenate(oidx)
floor = ov[:, 1].min()
scale3d = 3.2 / (ov[:, 1].max() - floor)
ov[:, 1] = (ov[:, 1] - floor) * scale3d + .05
ov[:, [0, 2]] *= scale3d
of = ov[oidx]  # (N,3,3) faces
otree = cKDTree(ov)
print('original verts', len(ov), 'faces', len(of), flush=True)

# ---- pass A: spike removal (off-surface distance via original vertex planes)
fc = pq.mean(1)
knn = 8
dd, ii = otree.query(fc, k=knn)
plane_off = np.abs(((fc[:, None, :] - ov[ii]) * on_[ii]).sum(-1)).min(1)
vert_near = dd[:, 0]
jp = {j['name']: np.array(j['position']) for j in doc['joints']}
region = ((np.abs(fc[:, 0]) < .45) & (fc[:, 1] > 2.10) & (fc[:, 1] < 2.60) & (fc[:, 2] > .40)  # chest
          | (np.linalg.norm(fc - jp['ShoulderL'], axis=1) < .32)
          | (np.linalg.norm(fc - jp['ShoulderR'], axis=1) < .32)
          | ((fc[:, 0] < -.55) & (fc[:, 0] > -1.05) & (fc[:, 1] > 1.55) & (fc[:, 1] < 2.25) & (fc[:, 2] < .45))  # forearm L back
          | (np.linalg.norm(fc - jp['HandR'], axis=1) < .45))  # hand / guard
roleWingR = np.array([parts[i]['role'] == 'WingR' for i in powner])
protected = pgen | roleWingR | np.isin(pbone, [i for i, n in enumerate(names) if n in ('Head', 'Sword')])
spike = region & ~protected & (plane_off > a.spike_mm * 1e-3) & (vert_near < .03)
print('spikes removed', int(spike.sum()), 'of', len(pf))
if spike.sum():
    by_part = collections.Counter(powner[spike])
    print('  by part', {parts[k]['nodeName']: v for k, v in by_part.items()})

# rebuild exterior face list without spikes
keep = ~spike
pf2 = pf[keep]
powner2 = powner[keep]
pq2 = pf2['v'][:, :, :3]
pbone2 = pf2['b'][:, 0, 0]
cc = pq2.mean(1)
cn = np.cross(pq2[:, 1] - pq2[:, 0], pq2[:, 2] - pq2[:, 0])
cn /= np.maximum(np.linalg.norm(cn, axis=1)[:, None], 1e-12)
ctree = cKDTree(cc)
# Binding sources exclude equipment (sword, shield, wings): cavity/plate holes
# belong to body armour; a patch bound to moving equipment gets dragged along.
equip = {i for i, n in enumerate(names) if n in ('Sword', 'Shield', 'WingL', 'WingR')}
bodymask = ~np.isin(pbone2, list(equip))
pq2v = pq2.reshape(-1, 3)
bodyvert = np.repeat(bodymask, 3)
cvtree = cKDTree(pq2v[bodyvert])
cvt_b0 = np.repeat(pf2['b'][:, 0, 0], 3)[bodyvert]
cvt_b1 = np.repeat(pf2['b'][:, 0, 1], 3)[bodyvert]
cvt_w0 = np.repeat(pf2['w'][:, 0, 0], 3)[bodyvert]
cvt_w1 = np.repeat(pf2['w'][:, 0, 1], 3)[bodyvert]
cvt_bone = np.repeat(pbone2, 3)[bodyvert]

# ---- pass B: reinsert uncovered original faces
lost_json = Path(a.lost)
lost_counts = {int(k): int(v) for k, v in json.loads(lost_json.read_text()).items()}
flagged = np.array(sorted(lost_counts), dtype=np.int64)
# spikes hid their underlying surface from the pixel detector; also test
# original faces around every deleted spike
if spike.sum():
    octree = cKDTree(of.mean(1))
    near_spike = set()
    for tri in pq[spike]:
        for pt in list(tri) + [tri.mean(0)]:
            near_spike.update(octree.query_ball_point(pt, .02))
    extra = np.array(sorted(near_spike), dtype=np.int64)
    flagged = np.union1d(flagged, extra)
    print('flagged extended by spike neighbourhood to', len(flagged))
cand = of[flagged]
cc_o = cand.mean(1)
cover = a.cover_mm * 1e-3
uncovered = np.zeros(len(cand), bool)
for k in range(len(cand)):
    near = ctree.query_ball_point(cc_o[k], .03)
    ok = False
    for j in near:
        d = float((cc_o[k] - cc[j]) @ cn[j])
        if abs(d) > cover:
            continue
        pnt = cc_o[k] - cn[j] * d
        qq = pq2[j]
        if ((np.cross(qq[1] - qq[0], pnt - qq[0]) @ cn[j] >= -1e-6)
                and (np.cross(qq[2] - qq[1], pnt - qq[1]) @ cn[j] >= -1e-6)
                and (np.cross(qq[0] - qq[2], pnt - qq[2]) @ cn[j] >= -1e-6)):
            ok = True
            break
    uncovered[k] = not ok
print('flagged original faces', len(cand), 'uncovered', int(uncovered.sum()))

# cluster uncovered by shared original vertices
uf_idx = flagged[uncovered]
_, vi = np.unique(oidx[uf_idx].reshape(-1), return_inverse=True)
vi = vi.reshape(-1, 3)
by_vert = collections.defaultdict(list)
for k in range(len(uf_idx)):
    for vtx in np.unique(vi[k]):
        by_vert[int(vtx)].append(k)
seen = set()
clusters = []
for s in range(len(uf_idx)):
    if s in seen:
        continue
    comp, todo = [], [s]
    while todo:
        k = todo.pop()
        if k in seen:
            continue
        seen.add(k)
        comp.append(k)
        for vtx in np.unique(vi[k]):
            todo.extend(by_vert[vtx])
    clusters.append(np.array(comp))

# per cluster: accept only when nearby candidate surface is dominated by one
# bone, or copy the full (possibly blended) binding of the nearest vertex for
# seam-spanning patches so deformation matches the surrounding panels
accepted, rejected = [], []
for comp in clusters:
    faces_o = of[uf_idx[comp]]
    centroid = faces_o.reshape(-1, 3).mean(0)
    ring = cvtree.query_ball_point(centroid, .05)
    if not ring:
        # large missing plate: fall back to the nearest candidate vertex
        _, nv = cvtree.query(centroid)
        ring = [nv]
    bones = cvt_bone[ring]
    votes = np.bincount(bones, minlength=len(names))
    # exterior plate loss: flush with the surrounding panel, one dominant bone,
    # and at least one pixel-verified hole ray. Spike-neighbourhood faces stay
    # dark recessed backing; only camera-confirmed holes earn source texture.
    hits = sum(lost_counts.get(int(g), 0) for g in uf_idx[comp])
    dd_ring, _ = cvtree.query(faces_o.reshape(-1, 3), k=1)
    exterior = bool(votes.max() >= votes.sum() * .95) and float(np.median(dd_ring)) <= .006 and hits > 0
    accepted.append((comp, exterior))
print('clusters', len(clusters), 'accepted', len(accepted), 'rejected', len(rejected))
rej_reasons = collections.Counter(r for _, r in rejected)
print('  reject reasons', dict(rej_reasons))

# per-face binding copied from the nearest body-armour vertex (both bone slots
# and blend weights), so patches follow the surrounding panel deformation
graphite_uv = [.8672464, .88142943]
flagset = set(int(x) for x in flagged)
# Cavity fills compete for the native 250K triangle budget: rank clusters by
# recorded pixel hits and keep the most visible ones within a raw-face cap.
CAVITY_RAW_CAP = 5500
cav_sel = sorted(((c, e) for c, e in accepted if not e),
                 key=lambda ce: -sum(lost_counts.get(int(g), 0) for g in uf_idx[ce[0]]))
kept = []
total = 0
for c, e in cav_sel:
    n = len(uf_idx[c])
    if total + n > CAVITY_RAW_CAP:
        continue
    kept.append((c, e))
    total += n
dropped = len(cav_sel) - len(kept)
if dropped:
    print('cavity clusters trimmed by budget:', dropped, 'dropped, raw kept', total)
accepted = [(c, e) for c, e in accepted if e] + kept
ext_records = []
cav_records = []
for comp, exterior in accepted:
    faces_o = of[uf_idx[comp]]
    if exterior:
        for face_vi in oidx[uf_idx[comp]]:
            rec = np.zeros(3, dt)
            rec['v'][:, :3] = ov[face_vi]
            rec['v'][:, 3:6] = on_[face_vi]
            rec['v'][:, 6:8] = ouv[face_vi]
            _, nv = cvtree.query(ov[face_vi].mean(0))
            rec['b'][:] = [cvt_b0[nv], cvt_b1[nv]]
            rec['w'][:] = [cvt_w0[nv], cvt_w1[nv]]
            ext_records.append(rec)
    else:
        # orient each face like the nearest candidate surface (that panel faces
        # outward from the same body), recess 8 mm inward, graphite swatch
        for gid, face_vi in zip(uf_idx[comp], oidx[uf_idx[comp]]):
            if int(gid) not in flagset:
                continue  # deeper cavity walls stay closed in pixel tests already
            pos = ov[face_vi].copy()
            nrm = on_[face_vi].copy()
            fn = np.cross(pos[1] - pos[0], pos[2] - pos[0])
            ln = np.linalg.norm(fn)
            if ln < 1e-10:
                continue
            fn /= ln
            nj = ctree.query(pos.mean(0))[1]
            if fn @ cn[nj] < 0:
                pos = pos[[0, 2, 1]]
                nrm = nrm[[0, 2, 1]]
                fn = -fn
            nrm = np.where(((nrm * fn).sum(1) < 0)[:, None], -nrm, nrm)
            pos -= fn * .008
            rec = np.zeros(3, dt)
            rec['v'][:, :3] = pos
            rec['v'][:, 3:6] = nrm
            rec['v'][:, 6:8] = graphite_uv
            _, nv = cvtree.query(ov[face_vi].mean(0))
            rec['b'][:] = [cvt_b0[nv], cvt_b1[nv]]
            rec['w'][:] = [cvt_w0[nv], cvt_w1[nv]]
            cav_records.append(rec)
# Cavity backing is hidden filler: collapse it hard with the boundary locked
# so the rim stays closed against the surrounding panels.
cav_records = np.array(cav_records) if cav_records else np.zeros((0, 3), dt)
if len(cav_records) > 400:
    sys.path.insert(0, str(Path(__file__).resolve().parents[2] / '.local-tests/mecha-asset-tools'))
    import pymeshlab
    pts, dix = np.unique(cav_records['v'][:, :, :3].reshape(-1, 3), axis=0, return_inverse=True)
    dix = dix.reshape(-1, 3)
    ms = pymeshlab.MeshSet()
    ms.add_mesh(pymeshlab.Mesh(vertex_matrix=pts.astype(float), face_matrix=dix.astype(np.int32)))
    ms.meshing_remove_duplicate_faces()
    ms.meshing_remove_null_faces()
    ms.meshing_repair_non_manifold_edges(method='Remove Faces')
    ms.meshing_repair_non_manifold_vertices(vertdispratio=0)
    pre = ms.current_mesh()
    pts, dix = pre.vertex_matrix(), pre.face_matrix()
    ms.meshing_decimation_quadric_edge_collapse(targetfacenum=max(300, int(len(dix) * .12)),
                                                preserveboundary=True, preservetopology=False,
                                                optimalplacement=False, qualitythr=.3)
    mesh = ms.current_mesh()
    xyz = mesh.vertex_matrix()
    dec = []
    for corners in mesh.face_matrix():
        tri = np.zeros((3, 8))
        tri[:, :3] = xyz[corners]
        fn = np.cross(tri[1, :3] - tri[0, :3], tri[2, :3] - tri[0, :3])
        ln = np.linalg.norm(fn)
        if ln < 1e-10:
            continue
        tri[:, 3:6] = fn / ln
        tri[:, 6:8] = graphite_uv
        rec = np.zeros(3, dt)
        rec['v'] = tri
        _, nv = cvtree.query(tri[:, :3].mean(0))
        rec['b'][:] = [cvt_b0[nv], cvt_b1[nv]]
        rec['w'][:] = [cvt_w0[nv], cvt_w1[nv]]
        dec.append(rec)
    print('cavity decimated', len(cav_records), '->', len(dec))
    cav_records = np.array(dec) if dec else np.zeros((0, 3), dt)
ext_records = np.array(ext_records) if ext_records else np.zeros((0, 3), dt)
new_records = np.concatenate([ext_records, cav_records]) if len(ext_records) or len(cav_records) else np.zeros((0, 3), dt)
print('reinserted faces', len(new_records))

if a.dry_run:
    print('dry run; not writing')
    sys_exit = None
else:
    # write patched exterior parts back (spikes removed) + appended patch part
    new_parts = []
    for i, part in enumerate(parts):
        if i not in set(exterior_idx):
            new_parts.append(part)
            continue
        sel = (powner == i) & keep
        rec = pf[sel]
        flat = rec.reshape(-1)
        unique, ix = np.unique(flat, return_inverse=True)
        part = dict(part)
        part.update(offset=len(blob), vertices=len(unique), indices=len(ix))
        blob.extend(unique.tobytes())
        blob.extend(ix.astype('<u4').tobytes())
        new_parts.append(part)
    if len(new_records):
        # one batch per rigid-owner role: the native equipment-isolation check
        # audits vertex motion per renderer role, so a patch face must render
        # under the role of the bone actually driving it
        def role_of(bone_name):
            if bone_name.startswith(('Shoulder', 'Elbow', 'Hand')):
                return 'Arm' + bone_name[-1]
            if bone_name.startswith(('Hip', 'Knee', 'Ankle', 'Foot')):
                return 'Leg' + bone_name[-1]
            return bone_name
        by_role = collections.defaultdict(list)
        for rec in new_records:
            by_role[role_of(names[rec['b'][0][0]])].append(rec)
        for role, recs in sorted(by_role.items()):
            chunk = np.array(recs)
            flat = chunk.reshape(-1)
            unique, ix = np.unique(flat, return_inverse=True)
            off = len(blob)
            blob.extend(unique.tobytes())
            blob.extend(ix.astype('<u4').tobytes())
            new_parts.append(dict(node=doc['sourceNodes'][0], nodeName='Complete_DefectPatch_' + role, primitive=0,
                                  joint='Torso', role=role, material=0, offset=off,
                                  vertices=len(unique), indices=len(ix),
                                  generatedRepair='original-surface-reinsert-v1'))
        print('patch parts by role:', {k: len(v) for k, v in sorted(by_role.items())})
    doc['parts'] = new_parts
    doc['triangles'] = sum(p_['indices'] // 3 for p_ in new_parts)
    doc['defectPatch'] = dict(version=1, spikesRemoved=int(spike.sum()),
                              spikeThresholdMm=a.spike_mm, coverThresholdMm=a.cover_mm,
                              reinsertedFaces=int(len(new_records)),
                              clustersAccepted=len(accepted), clustersRejected=len(rejected),
                              exteriorClusters=sum(1 for _, e in accepted if e),
                              cavityClusters=sum(1 for _, e in accepted if not e),
                              rejectReasons=dict(rej_reasons),
                              sourceSha256=doc['sourceSha256'])
    (res / f'{stem}_rig.bin').write_bytes(blob)
    (res / f'{stem}_rig.json').write_text(json.dumps(doc, indent=2))
    print(json.dumps(doc['defectPatch'], indent=1))
    print('total triangles', doc['triangles'])

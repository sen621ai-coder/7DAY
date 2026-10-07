"""Locate surface lost by decimation: original GLB covers it, candidate does not.

For each camera, render the original GLB mask and the candidate mask. Lost
pixels = original & fill_holes(candidate silhouette) & ~candidate. They are
back-projected to exact original triangles (node, primitive, triangle id) so a
repair pass can reinsert precisely those faces with original attributes.
"""
import json, struct, sys, collections
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage
from scipy.spatial import cKDTree

res = Path(sys.argv[1])  # candidate Resources directory
outstem = sys.argv[2]  # output prefix for lost-faces.json and overlay captures
stem = 'samurai_style_gundam_mecha'

# ---- candidate triangles
doc = json.loads((res / f'{stem}_rig.json').read_text())
raw = (res / f'{stem}_rig.bin').read_bytes()
dt = np.dtype([('v', '<f4', 8), ('b', '<i4', 2), ('w', '<f4', 2)])
cf = []
for p in doc['parts']:
    v = np.frombuffer(raw, dt, p['vertices'], p['offset'])
    ix = np.frombuffer(raw, '<u4', p['indices'], p['offset'] + 48 * p['vertices']).reshape(-1, 3)
    cf.append(v[ix]['v'][:, :, :3])
cf = np.concatenate(cf)

# ---- original GLB triangles in model (assembly) coordinates
blob = (res / f'{stem}.glb').read_bytes()
length = struct.unpack_from('<I', blob, 12)[0]
g = json.loads(blob[20:20 + length])
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
    return np.ndarray((x['count'], n), dtype, blob, base + v.get('byteOffset', 0) + x.get('byteOffset', 0),
                      strides=(v.get('byteStride', n * dtype.itemsize), dtype.itemsize)).copy()
of, onode = [], []
for i, node in enumerate(g['nodes']):
    if 'mesh' not in node:
        continue
    for primitive in g['meshes'][node['mesh']]['primitives']:
        v = accessor(primitive['attributes']['POSITION']).astype(float)
        v = v @ world[i][:3, :3].T + world[i][:3, 3]
        v[:, 0] *= -1
        ix = accessor(primitive['indices']).reshape(-1, 3)
        of.append(v[ix])
        onode += [i] * len(ix)
floor = min(x[:, :, 1].min() for x in of)
ceil = max(x[:, :, 1].max() for x in of)
scale3d = 3.2 / (ceil - floor)
of = np.concatenate(of)
of[:, :, 1] = (of[:, :, 1] - floor) * scale3d + .05
of[:, :, [0, 2]] *= scale3d
onode = np.array(onode)
print('original faces', len(of), 'candidate faces', len(cf), flush=True)

W = H = 512
center = np.array([0., 1.6, 0.05])
cams = [(np.radians(e), np.radians(az)) for e in (-25, 5, 35) for az in range(0, 360, 45)]

def render(f, eye, forward, right, up, cull_back=True):
    delta = f - eye
    z = delta @ forward
    vis = z.min(1) > 1e-3
    idx = np.flatnonzero(vis)
    q = f[idx]
    dz = z[idx]
    scale = (H / 2) / np.tan(np.deg2rad(30))
    sx = W / 2 + (delta[idx] @ right) / dz * scale
    sy = H / 2 - (delta[idx] @ up) / dz * scale
    img = Image.new('1', (W, H), 0)
    dr = ImageDraw.Draw(img)
    for k in range(len(idx)):
        dr.polygon([(sx[k, 0], sy[k, 0]), (sx[k, 1], sy[k, 1]), (sx[k, 2], sy[k, 2])], fill=1, outline=1)
    return np.array(img, bool), idx, sx, sy

lost_faces = collections.defaultdict(int)
tot = 0
for ci, (elev, az) in enumerate(cams):
    eye = center + 2.6 * np.array([np.cos(elev) * np.sin(az), np.sin(elev), np.cos(elev) * np.cos(az)])
    forward = center - eye
    forward /= np.linalg.norm(forward)
    right = np.cross(forward, [0, 1, 0])
    right /= np.linalg.norm(right)
    up = np.cross(right, forward)
    cmask, _, _, _ = render(cf, eye, forward, right, up)
    omask, oidx, osx, osy = render(of, eye, forward, right, up)
    lost = omask & ndimage.binary_fill_holes(cmask) & ~cmask
    lab, n = ndimage.label(lost)
    sizes = ndimage.sum(lost, lab, range(1, n + 1))
    lost &= np.isin(lab, np.flatnonzero(sizes >= 4) + 1)
    tot += lost.sum()
    print('cam', ci, 'lost px', int(lost.sum()), flush=True)
    if lost.sum() >= 100:
        ov = np.zeros((H, W, 3), np.uint8)
        ov[cmask] = (60, 64, 72)
        ov[omask & ~cmask] = (40, 80, 40)
        ov[lost] = (255, 40, 40)
        Image.fromarray(ov).save(f'{outstem}-cam{ci:02d}.png')
    if not lost.any():
        continue
    px = np.argwhere(lost)
    # exact ray-triangle intersection: for each hole pixel, collect original
    # faces whose screen bbox reaches the pixel, then Möller–Trumbore
    sc = np.vstack([np.stack([osx[:, k], osy[:, k]], 1) for k in range(3)])
    tree = cKDTree(sc)
    kd, kn = tree.query(px.astype(float), k=60, distance_upper_bound=60)
    cand_sets = [[int(x) for x, dd in zip(row, d) if np.isfinite(dd)] for row, d in zip(kn, kd)]
    rays = np.stack([px[:, 1].astype(float), px[:, 0].astype(float)], 1)
    # pixel -> world ray: p = eye + t * d
    focal = (H / 2) / np.tan(np.deg2rad(30))
    d3 = forward[None, :] * focal + right[None, :] * (px[:, 1] - W / 2)[:, None] + up[None, :] * (H / 2 - px[:, 0])[:, None]
    d3 /= np.linalg.norm(d3, axis=1)[:, None]
    for pi, cand in enumerate(cand_sets):
        if not cand:
            continue
        fidx = np.unique(np.array(cand) % len(oidx))
        tri = of[oidx[fidx]]
        e1 = tri[:, 1] - tri[:, 0]
        e2 = tri[:, 2] - tri[:, 0]
        pv = np.cross(d3[pi], e2)
        det = (e1 * pv).sum(1)
        ok = np.abs(det) > 1e-12
        if not ok.any():
            continue
        fidx, tri, e1, e2, pv, det = fidx[ok], tri[ok], e1[ok], e2[ok], pv[ok], det[ok]
        tv = eye - tri[:, 0]
        u = (tv * pv).sum(1) / det
        qv = np.cross(tv, e1)
        v = (d3[pi] * qv).sum(1) / det
        t = (e2 * qv).sum(1) / det
        hit = (u >= -1e-6) & (v >= -1e-6) & (u + v <= 1 + 1e-6) & (t > 1e-4)
        for fi in fidx[hit]:
            lost_faces[int(oidx[fi])] += 1
print('total lost px', tot, 'original faces flagged', len(lost_faces))
Path(outstem+'-faces.json').write_text(json.dumps({str(k): v for k, v in lost_faces.items()}))
sel = np.array(sorted(lost_faces))
pts = of[sel]
lo, hi = pts.reshape(-1, 3).min(0), pts.reshape(-1, 3).max(0)
print('lost extent lo', lo.round(3).tolist(), 'hi', hi.round(3).tolist())
hist = collections.Counter(onode[sel])
print('by node', dict(sorted(hist.items())))

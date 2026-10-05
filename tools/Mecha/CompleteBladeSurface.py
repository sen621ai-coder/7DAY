"""Closed metal surface for the blade/fin overlap, separate from source faces."""
import numpy as np


def build(dtype, sword_bone):
    tip = np.array([.23, .50, -1.10])
    axis = np.array([.73, 1.57, .30]) - tip
    axis /= np.linalg.norm(axis)
    normal = np.array([.96168816, -.0582994, -.267875])
    normal -= axis * np.dot(normal, axis)
    normal /= np.linalg.norm(normal)
    across = np.cross(normal, axis)
    # End rings overlap the intact tip/root. The middle restores both faces
    # and the spine exposed when the intersecting rear fin is separated.
    rings = [(.54, .30), (.64, 1.), (1.68, 1.), (1.83, .30)]
    points = []
    for along, taper in rings:
        # Recess behind the recovered original gold faces on BOTH sides.
        # Their complete triangles lie below -.061 or above -.039 in this
        # frame, so the core cannot hide them or create coplanar flicker.
        depth = -.050
        low, high = .006 + .012 * along, .183 + .018 * along
        middle = (low + high) / 2
        low, high = middle + (low-middle)*taper, middle + (high-middle)*taper
        thick, bevel = .009 * taper, .025 * taper
        for d, w in [(depth, low), (depth+thick, low+bevel),
                     (depth+thick, high-bevel), (depth, high),
                     (depth-thick, high-bevel), (depth-thick, low+bevel)]:
            points.append(tip + axis*along + normal*d + across*w)
    points = np.array(points)
    triangles = []
    for ring in range(len(rings)-1):
        for side in range(6):
            a, b = ring*6+side, ring*6+(side+1)%6
            triangles += [(a, b, b+6), (a, b+6, a+6)]
    triangles += [(0, i+1, i) for i in range(1, 5)]
    triangles += [(18, 18+i, 18+i+1) for i in range(1, 5)]
    faces = points[np.array(triangles)]
    volume = np.sum(np.einsum('ij,ij->i', faces[:, 0],
                    np.cross(faces[:, 1], faces[:, 2]))) / 6
    if volume < 0:
        faces = faces[:, [0, 2, 1]]
    normals = np.cross(faces[:, 1]-faces[:, 0], faces[:, 2]-faces[:, 0])
    normals /= np.linalg.norm(normals, axis=1)[:, None]
    records = np.zeros((len(faces), 3), dtype=dtype)
    records['vertex'][:, :, :3] = faces
    records['vertex'][:, :, 3:6] = normals[:, None, :]
    # Neutral silver sampled from the existing blade, not the adjacent fin.
    records['vertex'][:, :, 6:8] = [.52093643, .07936847]
    records['bones'] = sword_bone
    records['weights'] = [1, 0]
    return records

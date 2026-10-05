"""Recover original gold edge surfaces from the archived sword/fin overlap.

Use complete triangle bounds and outward face normals, not just a centroid
inside the blade. The protruding gold fin tips must remain outside the sword.
"""
import numpy as np
from PIL import Image


def select(records, candidates, texture):
    tip = np.array([.23, .50, -1.10])
    axis = np.array([.73, 1.57, .30]) - tip
    axis /= np.linalg.norm(axis)
    normal = np.array([.96168816, -.0582994, -.267875])
    normal -= axis * np.dot(normal, axis)
    normal /= np.linalg.norm(normal)
    across = np.cross(normal, axis)
    points = records['vertex'][:, :, :3]
    local = (points-tip) @ np.stack([axis, across, normal], axis=1)
    face_normal = np.cross(points[:, 1]-points[:, 0], points[:, 2]-points[:, 0])
    area = np.linalg.norm(face_normal, axis=1)
    facing = (face_normal / np.maximum(area[:, None], 1e-12)) @ normal
    image = np.asarray(Image.open(texture).convert('RGB')).astype(float)
    uv = records['vertex'][:, :, 6:8].mean(1)
    color = image[np.clip(((1-uv[:, 1])*image.shape[0]).astype(int), 0, image.shape[0]-1),
                  np.clip((uv[:, 0]*image.shape[1]).astype(int), 0, image.shape[1]-1)]
    gold = (color[:, 0] > 85) & (color[:, 0] > color[:, 2]*1.2) & (color[:, 1] > color[:, 2]*1.1)
    bounds = ((local[:, :, 0].min(1) > .64) & (local[:, :, 0].max(1) < 1.68)
              & (local[:, :, 1].min(1) > .015) & (local[:, :, 1].max(1) < .215)
              & (local[:, :, 2].min(1) > -.105) & (local[:, :, 2].max(1) < .01))
    # Keep each restored original face wholly outside the recessed silver core,
    # on the side its normal faces. This leaves >=2 mm separation everywhere.
    outward = ((facing > .55) & (local[:, :, 2].min(1) > -.039)) | ((facing < -.55) & (local[:, :, 2].max(1) < -.061))
    return candidates & gold & bounds & outward & (area > 1e-7)

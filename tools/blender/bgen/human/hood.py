"""A hood sculpted as a distance field in head-local units (like the head and headwear.py): cloth resting on the crown
with a soft point at the back, standing off the cheeks round an oval face opening, and falling past the jaw into a
cowl that lies on the shoulders and chest of whatever is under it (the body and the garments there, sampled into a
coarse distance grid), in loose folds that deepen toward a turned hem.
"""
import hashlib

import numpy as np

from . import fieldmesh
from .headwear import _cranium, _smooth
from .sdf import F, carve, ellipsoid, round_cone, smax, smin

STEP = 0.007                                    # the drape grid's spacing (head-local)
LO, HI = (-0.25, -0.21, -0.37), (0.25, 0.19, -0.05)
BOUNDS = ((-0.25, -0.21, -0.37), (0.25, 0.19, 0.16))


def drape(head, under):
    """The signed distance (head-local) to the meshes ``under`` the cowl, softened so the cloth bridges hollows, as a
    field and a digest of its samples (for the mesh cache)."""
    import bpy
    from mathutils import Vector
    from mathutils.bvhtree import BVHTree
    verts, polys = [], []
    for o in under:
        base = len(verts)
        verts += [o.matrix_world @ v.co for v in o.data.vertices]
        polys += [[base + i for i in poly.vertices] for poly in o.data.polygons]
    tree = BVHTree.FromPolygons(verts, polys)
    axes = [np.arange(LO[i], HI[i] + STEP * 0.5, STEP, dtype=np.float32) for i in range(3)]
    grid = np.stack(np.meshgrid(*axes, indexing="ij"), -1)
    c, h = Vector(head.c), head.h
    flat = grid.reshape(-1, 3)
    out = np.empty(len(flat), dtype=np.float32)
    for n, q in enumerate(flat):
        w = c + Vector(q.tolist()) * h
        loc, nor, _, dist = tree.find_nearest(w)
        out[n] = dist / h if (w - loc).dot(nor) >= 0 else -dist / h
    f = out.reshape(grid.shape[:3])
    kernel = np.array((1, 4, 6, 4, 1), dtype=np.float32) / 16
    for axis in range(3):
        f = np.apply_along_axis(lambda r: np.convolve(np.pad(r, 2, mode="edge"), kernel, "valid"), axis, f)
    digest = hashlib.sha1(np.round(f * 4000).astype(np.int32).tobytes()).hexdigest()[:16]
    near = fieldmesh.grid_field(f.astype(np.float32), LO, STEP)
    lo, hi = np.asarray(LO, dtype=np.float32), np.asarray(HI, dtype=np.float32)

    def field(p):           # (positive beyond the grid: above it the head takes over)
        return np.maximum(near(p), np.max(np.maximum(lo - p, p - hi), axis=1))
    return field, digest


def hood_field(shape, under, shoulder_z):
    """The hood over the head ``shape`` and the drape field ``under`` it; the hem hangs a little below the shoulder
    joints (head-local height ``shoulder_z``)."""
    fem = shape.fem

    def field(p):
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        # A soft volume under the cloth: the skull with a brow it overhangs, the jaw and neck it falls straight past
        # (a cone from the temples to the shoulders), then whatever lies under the cowl.
        head = smin(_cranium(p, fem), ellipsoid(p, (0, 0.012, -0.05), (0.071, 0.08, 0.082)), 0.04)
        head = smin(head, ellipsoid(p, (0, 0.066, 0.052), (0.058, 0.034, 0.03)), 0.03)
        head = smin(head, round_cone(p, (0, -0.008, -0.03), (0, -0.022, shoulder_z + 0.03), 0.07, 0.105), 0.04)
        vol = smin(head, under(p), 0.07)
        # Folds fall from below the ears, deepening toward the hem, and in front swag under the chin: they lift the
        # whole cloth off what is under it (never press into it).
        ang = np.arctan2(x, -y)
        fall = _smooth(F(-0.05), F(-0.24), z) * (F(1) - _smooth(F(0.3), F(0.7), y / F(0.1)))
        folds = np.sin(ang * F(6.0) + F(0.7) * np.sin(ang * F(2.0))) + F(0.45) * np.sin(ang * F(13.0) + F(1.3))
        swag = _smooth(F(-0.13), F(-0.2), z) * _smooth(F(0.02), F(0.07), y) * np.sin((z + F(0.13)) * F(70.0))
        vol = vol - F(0.0024) * (folds + F(1.45)) * fall - F(0.0014) * (swag + F(1)) * _smooth(F(-0.12), F(-0.16), z)
        # The cloth rests on the crown, stands off the cheeks and settles onto the shoulders.
        t = F(0.0095) + F(0.007) * _smooth(F(0.05), F(-0.03), z) - F(0.002) * _smooth(F(-0.15), F(-0.25), z)
        outer = smin(vol - t, ellipsoid(p, (0, -0.1, 0.042), (0.024, 0.04, 0.032)), 0.024)   # the point at the back
        d = carve(outer, vol - (t - F(0.006)), 0.0015)
        # The face opening: an oval, wider at the brow than the chin, cut only in front of the ears. The cowl ends
        # round the shoulders' tops and hangs lowest in front and behind. Both hems are turned, so a little thicker.
        u = (z + F(0.028)) / F(0.096)
        oval = (np.sqrt((x / (F(0.059) + F(0.008) * u)) ** 2 + u ** 2) - F(1)) * F(0.06)
        oval = np.maximum(oval, F(0.014) - y)
        hem_z = F(shoulder_z - 0.04) - F(0.034) * np.clip(F(1) - np.abs(x) / F(0.16), 0, 1) + F(0.005) * np.sin(ang * F(9.0))
        rim = np.sqrt(x * x + (y + F(0.02)) ** 2 * F(1.6)) - F(0.185)
        cut = smax(hem_z - z, rim, 0.02)
        edge = np.minimum(np.abs(oval), np.abs(cut)) - F(0.005)
        hem = np.maximum(carve(outer + F(0.002), vol - (t - F(0.0085)), 0.0015), edge)
        d = smin(d, hem, 0.0012)
        d = carve(d, oval, 0.0025)
        return smax(d, cut, 0.003)
    return field


def mesh(head, under):
    """The hood's mesh object in head-local units."""
    shape = head.shape
    shoulder_z = round(float(head.local(head.frame.j["shoulder.R"]).z), 4)
    field, digest = drape(head, under)
    return fieldmesh.build(hood_field(shape, field, shoulder_z), BOUNDS, 0.0016, 28000, "hat_hood",
                           ("hood", shape.key(), shoulder_z, digest), ("headshape.py", "earshape.py", "headwear.py", "hood.py"),
                           "headwear")

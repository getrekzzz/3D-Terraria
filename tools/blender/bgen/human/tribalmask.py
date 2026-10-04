"""A carved wooden tribal mask (``tribalMask``: the wood's colour), sculpted as a distance field in head-local units
like the head: a thick curved plate standing clear of the face, shaped like a long shield from over the brow to a
point below the chin, with a jutting ridge for a brow, a long nose, round eye holes in raised rims, cheek bosses and a
wide open mouth. Painted in red, white and black over the wood: stripes up the forehead, white rings round the eyes,
a white zigzag of teeth round the mouth and bars across the cheeks.
"""
import numpy as np

from . import fieldmesh
from .sdf import F, capsule, carve, ellipsoid, smax, smin, tube

CENTRE, RADII = (0.0, 0.0, -0.03), (0.085, 0.113, 0.132)     # the plate follows this egg's front
EYES = {sx: (sx * 0.031, -0.002) for sx in (1, -1)}             # (x, z) of the eye holes
MOUTH_Z = -0.08
BOUNDS = ((-0.1, -0.01, -0.19), (0.1, 0.15, 0.11))


def front_y(x, z):
    """The egg's front surface at (x, z)."""
    q = 1 - (x / RADII[0]) ** 2 - ((z - CENTRE[2]) / RADII[2]) ** 2
    return CENTRE[1] + RADII[1] * np.sqrt(np.maximum(q, 0.0))


def _on(x, z, out=0.0):
    return (x, float(front_y(x, z)) + out, z)


def outline(x, z):
    """Negative inside the shield's outline (front view): broad at the brow and cheeks, tapering to a point under
    the chin."""
    u = np.clip((z - 0.085) / -0.235, 0, 1)                      # 0 at the top .. 1 at the point
    half = 0.071 * np.sqrt(np.maximum(1 - ((u - 0.38) / 0.62) ** 2 * (u > 0.38), 0)) * (1 - 0.2 * (u < 0.1) * (0.1 - u) / 0.1)
    top = 0.085 + 0.012 * np.cos(np.clip(x / 0.07, -1, 1) * np.pi / 2) - 0.012
    return np.maximum.reduce([np.abs(x) - half, z - top, -0.15 - z])


def field(p):
    x, y, z = p[:, 0], p[:, 1], p[:, 2]
    egg = ellipsoid(p, CENTRE, RADII)
    plate = smax(egg - F(0.008), -(egg + F(0.0005)), 0.002)          # between the egg's surface and 8 mm out
    # Relief on the front: the brow ridge, the nose, cheek bosses, rims round the eyes and the mouth.
    relief = tube(p, [_on(-0.058, 0.03), _on(-0.03, 0.036, 0.004), _on(0.0, 0.032, 0.006), _on(0.03, 0.036, 0.004),
                      _on(0.058, 0.03)], [0.006, 0.0085, 0.008, 0.0085, 0.006])
    relief = smin(relief, capsule(p, _on(0, 0.02, 0.006), _on(0, -0.045, 0.014), 0.0085), 0.01)
    relief = smin(relief, ellipsoid(p, _on(0, -0.05, 0.008), (0.014, 0.012, 0.009)), 0.008)
    for sx in (1, -1):
        relief = smin(relief, ellipsoid(p, _on(sx * 0.047, -0.035, 0.0), (0.016, 0.012, 0.015)), 0.01)
        ex, ez = EYES[sx]
        ring = np.abs(np.sqrt((x - F(ex)) ** 2 + (z - F(ez)) ** 2) - F(0.0165)) - F(0.0035)
        relief = smin(relief, np.maximum(ring, np.abs(y - front_y(x, z) - F(0.008)) - F(0.004)), 0.004)
    mouth_ring = np.abs(np.sqrt((x / F(1.9)) ** 2 + (z - F(MOUTH_Z)) ** 2) - F(0.0125)) - F(0.0035)
    relief = smin(relief, np.maximum(mouth_ring, np.abs(y - front_y(x, z) - F(0.007)) - F(0.0045)), 0.004)
    d = smin(plate, smax(relief, -(egg + F(0.0005)), 0.002), 0.004)
    d = smax(d, F(0.03) - y, 0.004)                               # only in front
    d = smax(d, outline(x, z), 0.003)
    for sx in (1, -1):                                            # round eye holes
        ex, ez = EYES[sx]
        d = carve(d, np.sqrt((x - F(ex)) ** 2 + (z - F(ez)) ** 2) - F(0.0118), 0.0015)
    d = carve(d, np.sqrt((x / F(1.9)) ** 2 + (z - F(MOUTH_Z)) ** 2) - F(0.0088), 0.0015)
    return d


def colour(q, wood):
    """The paint at head-local ``q`` over the ``wood`` colour."""
    x, y, z = q
    red, white, black = (0.62, 0.1, 0.07), (0.9, 0.86, 0.74), (0.06, 0.05, 0.04)
    grain = 0.9 + 0.1 * np.sin(z * 260 + 3 * np.sin(x * 90))
    c = tuple(w * grain for w in wood)
    for sx in (1, -1):
        ex, ez = EYES[sx]
        r = np.hypot(x - ex, z - ez)
        if r < 0.0135:
            return black
        if r < 0.021:
            return white
        if abs(x - sx * 0.05) < 0.016 and abs(z + 0.03 - 0.25 * (abs(x) - 0.05)) < 0.005 + 0.004 * ((x * sx * 1000) % 8 < 4):
            c = red                                               # bars across the cheeks
    m = np.hypot(x / 1.9, z - MOUTH_Z)
    if m < 0.0098:
        return black
    if m < 0.017:
        zig = abs(((np.arctan2(z - MOUTH_Z, x / 1.9) / np.pi * 9) % 2) - 1)
        return white if (m - 0.0098) / 0.0072 < zig else red
    if z > 0.045 and abs(x) < 0.04 and (abs(x) // 0.0095) % 2 == 0:
        c = red                                                   # stripes up the forehead
    if abs(z - 0.034) < 0.005 and abs(x) < 0.06:
        c = black if abs(x) > 0.012 else c
    if z < -0.115:
        c = red if (z * 1000) % 10 < 5 else c                     # bands down the point of the chin
    return c


def mesh(head):
    """The mask (world coordinates), not yet coloured."""
    obj = fieldmesh.build(field, BOUNDS, 0.00085, 12000, "mask_tribal", ("tribalmask",), ("tribalmask.py",), "headwear")
    me = obj.data
    co = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", co)
    me.vertices.foreach_set("co", (co.reshape(-1, 3) * head.h + np.asarray(head.c, dtype=np.float32)).ravel())
    me.update()
    return obj

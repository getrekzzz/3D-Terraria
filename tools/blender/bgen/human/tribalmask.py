"""A carved wooden tribal mask (``tribalMask``: the wood's colour), sculpted as a distance field in head-local units
like the head: a thick shell laid over the bare face (brow, cheekbones, jaw and chin without the soft features), so it
sits on the face and follows it down to the chin, cut to a long shield from over the brow to a point at the chin. The
carving rises out of the shell: a jutting ridge for a brow, a long straight nose, round eye holes in raised rims,
cheek bosses and a wide open mouth in a raised ring. Painted in red, white and black over the wood: stripes up the
forehead, white rings round the eyes, a white zigzag of teeth round the mouth and bars across the cheeks.
"""
import numpy as np

from . import fieldmesh
from .sdf import F, carve, ellipsoid, round_cone, smax, smin, sphere

EYES = {sx: (sx * 0.031, -0.002) for sx in (1, -1)}             # (x, z) of the eye holes
MOUTH_Z = -0.077
CHIN = -0.126                                                   # the shield's point, at the chin
BOUNDS = ((-0.085, -0.01, -0.15), (0.085, 0.15, 0.11))
SHELL = (0.0035, 0.0095)                                        # inner and outer offsets from the bare face


def _gauss(x, w):
    return np.exp(-(x / F(w)) ** 2)


def outline(x, z):
    """Negative inside the shield (front view): broad over the brow and cheekbones, narrowing below the mouth to a
    rounded point at the chin."""
    u = np.clip((z - F(-0.03)) / F(CHIN + 0.03), 0, 1)           # 0 down to the cheekbones .. 1 at the chin
    half = F(0.064) * (F(1) - F(0.78) * u ** 1.6)
    top = F(0.08) - F(0.012) * (x / F(0.064)) ** 2
    return np.maximum.reduce([np.abs(x) - half, z - top, F(CHIN) - z])


def relief(x, z):
    """How far the carving stands out of the shell at (x, z)."""
    h = F(0.0045) * _gauss(z - F(0.031) + F(3.0) * x * x, 0.0055) * (np.abs(x) < F(0.062))        # brow ridge
    for sx in (1, -1):
        ex, ez = EYES[sx]
        r = np.sqrt((x - F(ex)) ** 2 + (z - F(ez)) ** 2)
        h = h + F(0.0032) * _gauss(r - F(0.0168), 0.0032)                                        # eye rims
        h = h + F(0.0045) * _gauss(np.sqrt((x - F(sx * 0.047)) ** 2 + (z + F(0.036)) ** 2), 0.012)  # cheek bosses
    m = np.sqrt((x / F(1.8)) ** 2 + (z - F(MOUTH_Z)) ** 2)
    return h + F(0.0035) * _gauss(m - F(0.0125), 0.0032)                                         # mouth ring


def mask_field(shape):
    def field(p):
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        base = shape.field(p, ears=False, face=False)
        for sx in (1, -1):                                        # filled over the eye sockets
            base = smin(base, sphere(p, shape.eyes[sx], shape.Rs + 0.002), 0.012)
        base = smin(base, round_cone(p, (0, 0.082, 0.03), (0, 0.108, -0.045), 0.006, 0.0105), 0.01)  # a long nose
        base = smin(base, ellipsoid(p, (0, 0.078, -0.072), (0.03, 0.026, 0.024)), 0.016)            # over the mouth
        cut = smax(outline(x, z), F(0.028) - y, 0.004)            # the shield, and only in front of the ears
        rim = np.clip(-cut / F(0.01), 0, 1)
        rim = rim * (F(2) - rim)                                  # 0 at the edge .. 1 inside: a rounded edge
        outer = base - F(SHELL[0]) - (F(SHELL[1] - SHELL[0]) + relief(x, z)) * rim
        d = carve(outer, base - F(SHELL[0]), 0.001)
        d = smax(d, cut, 0.0015)
        for sx in (1, -1):                                        # round eye holes
            ex, ez = EYES[sx]
            d = carve(d, np.sqrt((x - F(ex)) ** 2 + (z - F(ez)) ** 2) - F(0.0112), 0.0015)
        return carve(d, np.sqrt((x / F(1.8)) ** 2 + (z - F(MOUTH_Z)) ** 2) - F(0.0085), 0.0015)
    return field


def colour(q, wood):
    """The paint at head-local ``q`` over the ``wood`` colour."""
    x, y, z = q
    red, white, black = (0.62, 0.1, 0.07), (0.9, 0.86, 0.74), (0.06, 0.05, 0.04)
    grain = 0.9 + 0.1 * np.sin(z * 260 + 3 * np.sin(x * 90))
    c = tuple(w * grain for w in wood)
    for sx in (1, -1):
        ex, ez = EYES[sx]
        r = np.hypot(x - ex, z - ez)
        if r < 0.013:
            return black
        if r < 0.0205:
            return white
        if abs(x - sx * 0.05) < 0.015 and abs(z + 0.034 - 0.25 * (abs(x) - 0.05)) < 0.005 + 0.004 * ((x * sx * 1000) % 8 < 4):
            c = red                                               # bars across the cheeks
    m = np.hypot(x / 1.8, z - MOUTH_Z)
    if m < 0.0095:
        return black
    if m < 0.0165:
        zig = abs(((np.arctan2(z - MOUTH_Z, x / 1.8) / np.pi * 9) % 2) - 1)
        return white if (m - 0.0095) / 0.007 < zig else red
    if z > 0.045 and abs(x) < 0.04 and (abs(x) // 0.0095) % 2 == 0:
        c = red                                                   # stripes up the forehead
    if abs(z - 0.031 + 3.0 * x * x) < 0.004 and abs(x) < 0.06:
        c = black if abs(x) > 0.012 else c                        # the brow ridge picked out in black
    if z < -0.104:
        c = red if (z * 1000) % 8 < 4 else c                      # bands across the chin
    return c


def mesh(head):
    """The mask (world coordinates), not yet coloured."""
    shape = head.shape
    obj = fieldmesh.build(mask_field(shape), BOUNDS, 0.0008, 12000, "mask_tribal", ("tribalmask", shape.key()),
                          ("headshape.py", "earshape.py", "creature.py", "tribalmask.py"), "headwear")
    me = obj.data
    co = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", co)
    me.vertices.foreach_set("co", (co.reshape(-1, 3) * head.h + np.asarray(head.c, dtype=np.float32)).ravel())
    me.update()
    return obj

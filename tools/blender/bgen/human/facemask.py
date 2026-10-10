"""A smooth, stylised face mask as a distance field in head-local units (like the head): a rigid shell over the bare
face (brow, cheekbones, jaw and chin without the soft features), domed over the eyes, with a straight ridge for the
nose and a plain plane over the mouth, cut to an oval from the forehead to under the chin with almond eye holes and
rounded edges. It stands clear of the face under it, so no feature pokes through, and settles onto it at the edge.
"""
import numpy as np

from . import fieldmesh
from .sdf import F, carve, ellipsoid, round_cone, smax, smin, sphere

OUT, IN = (0.0044, 0.0031), (0.0018, 0.0024)   # the shell's outer and inner offsets from the base: rim + middle
BOUNDS = ((-0.085, -0.01, -0.14), (0.085, 0.14, 0.11))


def mask_field(shape):
    def field(p):
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        base = shape.field(p, ears=False, face=False)
        for sx in (1, -1):
            base = smin(base, sphere(p, shape.eyes[sx], shape.Rs + 0.0022), 0.012)
        base = smin(base, round_cone(p, (0, 0.0805, 0.026), (0, 0.1015, -0.035), 0.0062, 0.0086), 0.012)   # nose ridge
        base = smin(base, ellipsoid(p, (0, 0.082, -0.05), (0.022, 0.017, 0.012)), 0.014)                 # under the nose
        base = smin(base, ellipsoid(p, (0, 0.076, -0.071), (0.031, 0.027, 0.024)), 0.018)                # over the mouth
        # Outline: an oval face, broad at the brow and cheekbones, narrowing to the chin; only in front of the ears.
        u = (z + F(0.019)) / F(0.094)
        half = F(0.057) + F(0.006) * np.clip(u, -1, 1)
        oval = (np.sqrt((x / half) ** 2 + u * u) - F(1)) * F(0.06)
        cut = smax(oval, F(0.024) - y, 0.01)
        rim = np.clip(-cut / F(0.014), 0, 1)
        rim = rim * (F(2) - rim)                  # 0 at the edge, 1 well inside: the edge settles onto the face
        d = carve(base - F(OUT[0]) - F(OUT[1]) * rim, base - F(IN[0]) - F(IN[1]) * rim, 0.0012)
        d = smax(d, cut, 0.002)
        # Almond eye holes, the outer corners a little raised.
        for sx in (1, -1):
            e = shape.eyes[sx]
            hx = (x - F(e[0])) * F(sx) + F(0.001)
            hz = z - F(e[2]) - F(0.12) * hx
            w = F(0.0145)
            hole = (np.sqrt((hx / w) ** 2 + (hz / (F(0.0074) * (F(1) - F(0.25) * np.clip(hx / w, -1, 1)))) ** 2) - F(1)) * F(0.007)
            d = carve(d, hole, 0.0016)
        return d
    return field


def mesh(head):
    """The mask's mesh object, placed on the head (world coordinates)."""
    shape = head.shape
    obj = fieldmesh.build(mask_field(shape), BOUNDS, 0.0008, 7000, "mask", ("mask", shape.key()),
                          ("headshape.py", "earshape.py", "creature.py", "facemask.py"), "headwear")
    me = obj.data
    co = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", co)
    me.vertices.foreach_set("co", (co.reshape(-1, 3) * head.h + np.asarray(head.c, dtype=np.float32)).ravel())
    me.update()
    return obj

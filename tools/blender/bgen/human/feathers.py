"""A single flight feather as a thin closed solid (the Witch Doctor's headdress, tribal.py): a bare quill at the root,
then two vanes either side of the shaft, the leading one narrower, long and slowly narrowing, rounding off into a
soft tip, with a split in the trailing vane. Fluffy pale down at the base, the feather's colour crossed by fine
slanting barbs, a dark band near the tip and a pale shaft down the middle. The vanes cup a little and the tip
droops away from the face of the feather.
"""
import math

from .head import smooth

ACROSS = (-1.0, -0.6, -0.25, 0.0, 0.25, 0.6, 1.0, 0.6, 0.25, 0.0, -0.25, -0.6)
TOP = (1, 1, 1, 1, 1, 1, 1, -1, -1, -1, -1, -1)
CREAM, DARK = (0.92, 0.89, 0.8), (0.12, 0.1, 0.08)


def _width(u, v, k):
    rise = smooth(0.05, 0.26, u)
    tip = math.sqrt(max(0.0, 1 - ((max(u, 0.74) - 0.74) / 0.26) ** 2))
    w = 0.07 + 0.93 * rise * tip * (1 - 0.22 * u)
    if v < 0:
        w *= 0.72                                                # the narrow leading vane
    elif abs(v) > 0.5:
        u0 = 0.46 + 0.08 * math.sin(k * 1.7)                     # a split in the trailing vane
        w *= 1 - 0.4 * math.exp(-((u - u0) / 0.022) ** 2)
    return w


def _colour(u, v, under, col):
    if abs(v) < 0.1:
        return tuple(c * 0.55 + q * 0.45 for c, q in zip(col, CREAM))           # the shaft
    down = smooth(0.32, 0.16, u)
    c = tuple(x + (q - x) * down for x, q in zip(col, CREAM))
    barb = 0.9 + 0.1 * math.sin(u * 95 - abs(v) * 13)
    band = smooth(0.8, 0.9, u) * (1 - smooth(0.95, 1.0, u) * 0.5)
    c = tuple((x + (d - x) * band) * barb for x, d in zip(c, DARK))
    return tuple(x * (0.84 if under else 1.0) for x in c)


def feather(bm, layer, base, d, n, length, width, thick, col, k=0, rows=14):
    """Adds a feather pointing along ``d`` from ``base`` with its face toward ``n`` to ``bm`` (colours in
    ``layer``); ``k`` varies the split. Returns its faces."""
    d = d.normalized()
    side = n.cross(d).normalized()
    n = d.cross(side)

    def at(u, v, top):
        w = width * _width(u, v, k)
        lift = 0.09 * length * u * u + 0.14 * w * abs(v) + top * 0.5 * thick * (1.0 - 0.75 * abs(v)) * (1 - 0.6 * u)
        return base + d * (length * u) + side * (v * w) + n * lift

    meta = {}

    def new(u, v, top):
        vert = bm.verts.new(at(u, v, top))
        meta[vert] = (u, v, top < 0)
        return vert

    root, tip = new(0.0, 0.0, 1), new(1.0, 0.0, 1)
    rings = [[new(i / rows, v, t) for v, t in zip(ACROSS, TOP)] for i in range(1, rows)]
    m = len(ACROSS)
    faces = []
    for r0, r1 in zip(rings, rings[1:]):
        for j in range(m):
            faces.append(bm.faces.new((r0[j], r0[(j + 1) % m], r1[(j + 1) % m], r1[j])))
    for j in range(m):
        faces.append(bm.faces.new((rings[0][(j + 1) % m], rings[0][j], root)))
        faces.append(bm.faces.new((rings[-1][j], rings[-1][(j + 1) % m], tip)))
    for f in faces:
        for loop in f.loops:
            loop[layer] = (*_colour(*meta[loop.vert], col), 1.0)
    return faces

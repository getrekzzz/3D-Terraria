"""A single flight feather as a thin closed solid (the Witch Doctor's headdress, tribal.py): a bare quill at the root
running on as a raised round shaft (the rachis) up the middle to near the tip, and the two vanes either side of it, the
leading one narrower, thin sheets that are thickest by the shaft and thin to nothing at the edge, long and slowly
narrowing to a soft rounded tip. The vanes are parted in a few places, the barbs separating from the edge in toward the
shaft and slanting to the tip. Fluffy pale down at the base, the feather's colour crossed by slanting barbs, a dark
band near the tip and a pale shaft. The feather curves back along its length and its vanes arch over the shaft.
"""
import math

from .head import smooth

TOP = (-1.0, -0.7, -0.4, -0.12, -0.04, 0.0, 0.04, 0.12, 0.4, 0.7, 1.0)     # across the upper face, edge to edge
UNDER = (0.7, 0.4, 0.1, 0.0, -0.1, -0.4, -0.7)                              # and back along the underside
ROWS = 24
CREAM, DARK = (0.92, 0.89, 0.8), (0.12, 0.1, 0.08)


def _u(i):
    return 0.5 - 0.5 * math.cos(math.pi * i / ROWS)            # rows close together at the quill and the tip


def _splits(k):
    """(u, depth, side) of the places the vane is parted: two in the trailing vane, one in the leading."""
    return ((0.42 + 0.06 * math.sin(1.7 * k), 0.5, 1), (0.66 + 0.05 * math.sin(2.3 * k + 1.0), 0.4, 1),
            (0.55 + 0.07 * math.sin(3.1 * k + 2.0), 0.45, -1))


def _vane(u, v, k):
    """How far out the vane reaches on v's side at u (a fraction of the width)."""
    rise = smooth(0.05, 0.22, u)
    tip = math.sqrt(max(0.0, 1 - ((max(u, 0.72) - 0.72) / 0.28) ** 2))
    w = rise * tip * (1 - 0.2 * u) * (0.68 if v < 0 else 1.0)
    for u0, depth, side in _splits(k):
        if side * v > 0:
            w *= 1 - depth * math.exp(-((u - u0 - 0.05 * abs(v)) / 0.02) ** 2)   # slanting toward the tip
    return w


def _colour(u, v, shaft, under, col):
    if shaft:
        c = tuple(x * 0.5 + q * 0.5 for x, q in zip(col, CREAM))
    else:
        down = smooth(0.3, 0.12, u)
        c = tuple(x + (q - x) * down for x, q in zip(col, CREAM))
        barb = 0.9 + 0.1 * math.sin(u * 55 - abs(v) * 8)
        c = tuple(x * barb * (1 - 0.12 * v ** 4) for x in c)
    band = smooth(0.78, 0.9, u) * (1 - 0.4 * smooth(0.96, 1.0, u))
    c = tuple(x + (d - x) * band for x, d in zip(c, DARK))
    return tuple(x * (0.84 if under else 1.0) for x in c)


def feather(bm, layer, base, d, n, length, width, thick, col, k=0):
    """Adds a feather pointing along ``d`` from ``base`` with its upper face toward ``n`` to ``bm`` (colours in
    ``layer``); ``width`` is the trailing vane's reach, ``k`` varies where the vanes part. Returns its faces."""
    d = d.normalized()
    side = n.cross(d).normalized()
    n = d.cross(side)
    meta = {}

    def new(u, v, top):
        r = width * 0.05 * (1 - 0.75 * u)                       # the shaft's radius
        reach = max(_vane(u, v, k), r / width)
        y = v * width * reach
        bump = 0.9 * r * math.exp(-(y / r) ** 2) * (1 - smooth(0.85, 1.0, u))
        sheet = 0.5 * thick * (1 - v * v) * (1 - 0.5 * u)
        h = (sheet + bump) if top else -(sheet + 0.6 * bump)
        lift = 0.1 * length * u * u - 0.06 * width * reach * v * v + h     # curving back; the vanes arch over
        vert = bm.verts.new(base + d * (length * u) + side * y + n * lift)
        meta[vert] = (u, v, abs(y) < 1.3 * r, not top)
        return vert

    root, tip = new(0.0, 0.0, True), new(1.0, 0.0, True)
    ring = [(v, True) for v in TOP] + [(v, False) for v in UNDER]
    rings = [[new(_u(i), v, t) for v, t in ring] for i in range(1, ROWS)]
    m = len(ring)
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

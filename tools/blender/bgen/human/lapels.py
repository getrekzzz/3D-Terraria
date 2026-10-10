"""A coat's open front (garments.Wardrobe.front, the ``lapels`` parameter): a V from the collar down to the chest
cut out of the coat, so the shirt's collar and the top of its front show; the V's edges are turned back into lapels
lying on the coat, widening from the V's foot and narrowing again where a turned-down collar takes over round the
back of the neck. Below the V the coat closes over a short row of buttons (finishing.py).

The lapels are built over the finished coat: across each edge a strip of rows (rolled over the edge, then lying on
the cloth, its outer edge rounded down) is laid onto the coat's surface by casting down onto it, so it follows the
chest however the cloth curves, and carries the skin weights of the edge it turns back.
"""
import math

import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

# The turned-down collar, swept round the neck like finishing's collars: (u up the neck, w out from it) in units of
# the frame scale, from inside the neckline up over the roll and down outside onto the shoulders.
COLLAR = ((-0.006, -0.002), (0.002, -0.0025), (0.011, -0.0012), (0.0145, 0.0018), (0.013, 0.0056), (0.006, 0.0082),
          (-0.004, 0.0098), (-0.012, 0.0104), (-0.0145, 0.0084))
# Across a lapel from the coat's edge: (offset in units of the frame scale, fraction of the lapel's width, lift off
# the coat, shade of the strip from this row to the next): rolled over the edge, lying on the coat, rounded down.
ACROSS = ((-0.004, 0.0, -0.0018, 0.8), (0.0, 0.0, 0.0016, 0.92), (0.0, 0.12, 0.003, 1.0), (0.0, 0.94, 0.0026, 1.0),
          (0.0, 1.0, 0.0016, 0.86), (0.0015, 1.0, 0.0002, 0.8))


class Front:
    """The V of an open coat front: from ``bottom`` (just above the chest's middle) to the collar, ``half`` wide there."""

    def __init__(self, w):
        j, s = w.f.j, w.f.s
        self.s = s
        self.bottom = j["chest"].z - 0.02 * s
        self.top = w.collar_z
        self.half = 0.075 * s
        self.k = self.half / max(self.top - self.bottom, 1e-6)

    def inside(self, co, margin=0.0):
        """Whether a rest position lies in the opening (in front, inside the V, ``margin`` wider)."""
        return co.y > 0 and co.z > self.bottom and abs(co.x) < self.k * (co.z - self.bottom) + margin

    def depth(self, X):
        """How far each point lies inside the V across it (negative outside it; -1 behind and below)."""
        d = self.k * (X[:, 2] - self.bottom) - np.abs(X[:, 0])
        return np.where((X[:, 1] > 0) & (X[:, 2] > self.bottom - 0.03 * self.s), d, -1.0)

    def cuts(self):
        """The V's two edges as cutting planes (point, normal, face filter), for garments.Wardrobe.shell."""
        s, base = self.s, Vector((0, 0, self.bottom))
        return [(base, Vector((sx, 0, -self.k)).normalized(),
                 lambda c, sx=sx: c.y > 0 and c.z > self.bottom - 0.02 * s and c.x * sx > -0.01 * s) for sx in (1, -1)]

    def on_edge(self, co):
        return co.y > 0 and co.z < self.top - 0.004 * self.s and abs(self.k * (co.z - self.bottom) - abs(co.x)) < 0.012 * self.s

    def width(self, z):
        """A lapel's width at height z: widening from the V's foot, narrowing under the collar."""
        t = (z - self.bottom) / max(self.top - self.bottom, 1e-6)
        rise = min(max(t / 0.75, 0.0), 1.0)
        fall = min(max((t - 0.82) / 0.18, 0.0), 1.0)
        return self.s * (0.004 + 0.046 * math.sin(rise * math.pi / 2) ** 0.7 * (1.0 - 0.75 * fall * fall))


def split(loop, closed, front):
    """The coat's neckline loop split into the collar's run round the back of the neck and the two lapel edges
    (each from the V's foot upward)."""
    on = [front.on_edge(v.co) for v in loop]
    if not any(on) or all(on):
        return None
    n = len(loop)
    start = next(i for i in range(n) if on[i] and not on[i - 1]) if closed else 0
    order = [loop[(start + i) % n] for i in range(n)]
    flags = [on[(start + i) % n] for i in range(n)]
    edge = [v for v, f in zip(order, flags) if f]
    runs, cur = [], []                      # (the collar: the longest run off the V's edges)
    for v, f in zip(order, flags):
        if f:
            if cur:
                runs.append(cur)
            cur = []
        else:
            cur.append(v)
    collar = max(runs + [cur], key=len)
    right = sorted([v for v in edge if v.co.x > 0], key=lambda v: v.co.z)
    left = sorted([v for v in edge if v.co.x <= 0], key=lambda v: v.co.z)
    return collar, right, left


def lapel(bm, layer, deform, edge, front, color, s):
    """A lapel turned back from the V edge ``edge`` (vertices from the foot up), laid onto the coat (bm)."""
    if len(edge) < 3:
        return
    bm.faces.ensure_lookup_table()
    tree = BVHTree.FromBMesh(bm)
    pts = [v.co.copy() for v in edge]
    m = len(edge)                           # (normals averaged along the edge: a crumpled corner can't crack it)
    nrm = [sum((edge[j].normal for j in range(max(i - 4, 0), min(i + 5, m))), Vector()) for i in range(m)]
    nrm = [n.normalized() if n.length > 1e-6 else Vector((0, 1, 0)) for n in nrm]
    for _ in range(3):                      # even out the decimated edge
        pts = [pts[0]] + [(pts[i - 1] + 2 * pts[i] + pts[i + 1]) * 0.25 for i in range(1, len(pts) - 1)] + [pts[-1]]
    length = [0.0]
    for a, b in zip(pts, pts[1:]):
        length.append(length[-1] + (b - a).length)
    count = max(int(length[-1] / (0.006 * s)), 3)
    rows, i = [], 0
    for k in range(count + 1):
        d = length[-1] * k / count
        while i < len(pts) - 2 and length[i + 1] < d:
            i += 1
        a = (d - length[i]) / max(length[i + 1] - length[i], 1e-9)
        p = pts[i].lerp(pts[i + 1], a)
        src = edge[i] if a < 0.5 else edge[i + 1]
        n = nrm[i].lerp(nrm[i + 1], a).normalized()
        tan = (pts[i + 1] - pts[i]).normalized()
        u = n.cross(tan).normalized()
        if u.x * (1 if p.x > 0 else -1) < 0:
            u = -u
        wd = front.width(p.z)
        row = []
        for off, frac, lift, _ in ACROSS:
            q = p + u * (off * s + frac * wd)
            hit = tree.ray_cast(q + n * 0.06 * s, -n, 0.12 * s) if off + frac > 0 else (None,)
            out, w = n, src
            if hit[0] is not None:          # laid on the coat, lifted along its surface, skinned like it there
                q, out = hit[0], (hit[1] if hit[1].dot(n) > 0 else -hit[1])
                w = min(bm.faces[hit[2]].verts, key=lambda v: (v.co - q).length)
            nv = bm.verts.new(q + out * (lift * s + 0.0006 * s))
            for g, wt in w[deform].items():
                nv[deform][g] = wt
            row.append(nv)
        rows.append(row)
    faces = []
    for r0, r1 in zip(rows, rows[1:]):
        for m in range(len(ACROSS) - 1):
            f = bm.faces.new((r0[m], r1[m], r1[m + 1], r0[m + 1]))
            k = ACROSS[m][3]
            for lp in f.loops:
                lp[layer] = (color[0] * k, color[1] * k, color[2] * k, 1.0)
            faces.append(f)
    bm.normal_update()
    probe = faces[(len(rows) // 2) * (len(ACROSS) - 1) + 2]         # (the middle row, lying on the coat)
    if probe.normal.dot(edge[len(edge) // 2].normal) < 0:
        for f in faces:
            f.normal_flip()

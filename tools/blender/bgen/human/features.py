"""Creature features: horns swept along smooth curves, tapering to a point and ringed with growth ridges, plus the
helpers the wings, tails and wreaths share: a Catmull-Rom curve resampled evenly and a sweep that builds a closed tube
along it with rotation-minimising frames, a cross-section that may vary along it and rounded ends."""
import math

import bmesh
import numpy as np
from mathutils import Vector

from .garments import mesh_from, set_slot
from .rig import rigid


def spline(points, n):
    """``n`` points evenly spaced by arc length along a Catmull-Rom curve through ``points``."""
    P = [Vector(p) for p in points]
    P = [P[0] * 2 - P[1]] + P + [P[-1] * 2 - P[-2]]
    dense = []
    for i in range(1, len(P) - 2):
        p0, p1, p2, p3 = P[i - 1:i + 3]
        for k in range(24):
            t = k / 24
            dense.append(0.5 * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (3 * p1 - p0 - 3 * p2 + p3) * t ** 3))
    dense.append(P[-2])
    acc = [0.0]
    for a, b in zip(dense, dense[1:]):
        acc.append(acc[-1] + (b - a).length)
    out, j = [], 0
    for k in range(n):
        s = acc[-1] * k / (n - 1)
        while j < len(acc) - 2 and acc[j + 1] < s:
            j += 1
        t = (s - acc[j]) / max(acc[j + 1] - acc[j], 1e-12)
        out.append(dense[j].lerp(dense[j + 1], t))
    return out


def frames(path, up=None):
    """Tangent, normal and binormal at each point, the normal carried along without twisting."""
    T = []
    for i in range(len(path)):
        T.append((path[min(i + 1, len(path) - 1)] - path[max(i - 1, 0)]).normalized())
    n = (Vector(up) if up is not None else T[0].orthogonal())
    n = (n - T[0] * n.dot(T[0])).normalized()
    out = []
    for i, t in enumerate(T):
        if i:
            n = T[i - 1].rotation_difference(t) @ n
            n = (n - t * n.dot(t)).normalized()
        out.append((t, n, t.cross(n)))
    return out


def sweep(bm, path, section, segs, up=None, caps=(True, True)):
    """A closed tube along ``path``: ``section(t, a) -> (x, y)`` gives the offset of the point at angle ``a`` round
    the ring at ``t`` (0..1 along the path) in the ring's (normal, binormal) plane. Ends whose section shrinks to
    nothing close on a point; others are closed with a domed cap. Returns [(vert, t, a)]."""
    fr = frames(path, up)
    out, rings = [], []
    for i, (p, (t, n, b)) in enumerate(zip(path, fr)):
        u = i / (len(path) - 1)
        ring = []
        for k in range(segs):
            a = 2 * math.pi * k / segs
            x, y = section(u, a)
            v = bm.verts.new(p + n * x + b * y)
            ring.append(v)
            out.append((v, u, a))
        rings.append(ring)
    for r0, r1 in zip(rings, rings[1:]):
        for k in range(segs):
            bm.faces.new((r0[k], r0[(k + 1) % segs], r1[(k + 1) % segs], r1[k]))
    for end, ring, sign in ((0, rings[0], -1), (1, rings[-1], 1)):
        if not caps[end]:
            continue
        p, (t, _, _) = path[-end], fr[-end]
        size = max((v.co - p).length for v in ring)
        tip = bm.verts.new(p + t * sign * size * 0.6)
        out.append((tip, float(end), 0.0))
        for k in range(segs):
            f = (ring[k], ring[(k + 1) % segs], tip)
            bm.faces.new(f if sign > 0 else f[::-1])
    return out


def round_section(radius, profile=None):
    """A round section of radius ``radius(t)``, scaled by ``profile(t, a)`` when given."""
    def section(t, a):
        r = radius(t) * (profile(t, a) if profile else 1.0)
        return math.cos(a) * r, math.sin(a) * r
    return section


def blade(bm, layer, base, d, n, length, width, color, fold=0.3, curl=-0.12, thick=0.0011, rows=6, round_tip=False,
          skew=0.0):
    """A thin closed blade (a leaf, a petal, a feather): pointing along ``d`` and facing ``n``, widest near the
    middle, folded along its midrib by ``fold``, curling by ``curl`` and thickest along the midrib; ``skew`` makes
    one side narrower (a feather's leading vane). ``color(u, v, under)`` colours it (u along it, v across)."""
    d = d.normalized()
    side = n.cross(d).normalized()
    n = d.cross(side)
    vs = (-1, -0.5, 0, 0.5, 1, 0.5, 0, -0.5)
    top = (1, 1, 1, 1, 1, -1, -1, -1)

    def at(u, v, s):
        w = width * math.sin(math.pi * u ** (0.6 if round_tip else 0.8)) ** (0.55 if round_tip else 0.9)
        w *= 1 - skew if v < 0 else 1
        lift = fold * abs(v) * w + curl * length * u * u + s * thick * (1 - v * v) * 0.5 * (1 - u * 0.6)
        return base + d * (length * u) + side * (v * w) + n * lift

    meta = {}

    def new(u, v, s):
        vert = bm.verts.new(at(u, v, s))
        meta[vert] = (u, v, s < 0)
        return vert

    tip, root = new(1.0, 0, 1), new(0.0, 0, 1)
    rings = [[new(i / rows, v, s) for v, s in zip(vs, top)] for i in range(1, rows)]
    faces = []
    for r0, r1 in zip(rings, rings[1:]):
        for k in range(8):
            faces.append(bm.faces.new((r0[k], r0[(k + 1) % 8], r1[(k + 1) % 8], r1[k])))
    for k in range(8):
        faces.append(bm.faces.new((rings[0][(k + 1) % 8], rings[0][k], root)))
        faces.append(bm.faces.new((rings[-1][k], rings[-1][(k + 1) % 8], tip)))
    for f in faces:
        for loop in f.loops:
            loop[layer] = (*color(*meta[loop.vert]), 1.0)
    return faces


def paint(bm, verts, color):
    """Vertex colours from ``color(t, a) -> rgb`` for the verts a sweep returned."""
    layer = bm.loops.layers.color.get("Color") or bm.loops.layers.color.new("Color")
    tone = {v: color(t, a) for v, t, a in verts}
    for v, c in tone.items():
        for loop in v.link_loops:
            loop[layer] = (*c, 1.0)


# horn shapes: control points (head-local, right side), base and tip radius, ridges along it, ridge depth, keel
HORNS = {
    "imp": ([(0.026, 0.05, 0.058), (0.034, 0.058, 0.08), (0.046, 0.05, 0.106), (0.054, 0.028, 0.126), (0.056, 0.002, 0.136),
             (0.052, -0.02, 0.138)], 0.0115, 0.0012, 7, 0.05, 0.0),
    "demon": ([(0.034, 0.03, 0.068), (0.046, 0.04, 0.094), (0.064, 0.042, 0.124), (0.082, 0.028, 0.152), (0.092, 0.002, 0.172),
               (0.092, -0.03, 0.184), (0.084, -0.058, 0.186)], 0.016, 0.0016, 11, 0.06, 0.08),
    "goat": ([(0.03, 0.026, 0.07), (0.036, 0.03, 0.105), (0.048, 0.012, 0.146), (0.064, -0.028, 0.176), (0.08, -0.078, 0.188),
             (0.093, -0.126, 0.176), (0.1, -0.16, 0.148)], 0.03, 0.004, 15, 0.09, 0.14),
}


def horns(head, color, shape="imp"):
    """A pair of horns rising from the skull (their roots sunk into it), curving and tapering to a rounded point,
    ringed with growth ridges and darker at the root."""
    from .paint import hex3
    pts, r0, r1, ridges, depth, keel = HORNS.get(shape, HORNS["imp"])
    base = hex3(color)
    bm = bmesh.new()
    for sx in (1, -1):
        path = spline([head.at((sx * x, y, z)) for x, y, z in pts], 48)
        h = head.h

        def radius(t):
            flare = 1 + 0.22 * max(0.0, 1 - t / 0.1) ** 2
            return h * (r1 + (r0 - r1) * (1 - t) ** 1.25) * flare

        def profile(t, a, sx=sx):
            ridge = 1 - depth * max(0.0, math.sin(math.pi * t * ridges)) ** 6 * min(1.0, t * 6) * (1 - t)
            return ridge * (1 + keel * math.cos(a) ** 2 * (1 if math.cos(a) > 0 else 0.4))

        up = head.at((sx * 1.0, 0, 0)) - head.at((0, 0, 0))
        verts = sweep(bm, path, round_section(radius, profile), 16, up=up)

        def tone(t, a):
            groove = max(0.0, math.sin(math.pi * t * ridges)) ** 6 * (1 - t)
            k = (0.62 + 0.45 * t ** 0.8) * (1 - 0.25 * groove) * (0.96 + 0.04 * math.sin(a * 3 + t * 20))
            return tuple(min(1.0, c * k) for c in base)
        paint(bm, verts, tone)
    obj = mesh_from(bm, "horns")
    set_slot(obj, "char:bone")
    rigid(obj, "head")
    return obj

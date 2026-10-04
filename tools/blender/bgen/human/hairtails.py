"""Tied hair (head-local units): the ponytail, the bun and the band that ties them.

- A ponytail: the hair combed back is gathered into the tie, springs out of it in a full bundle, droops over the back
  of the head and lies down the nape and the back (drape). The bundle is a handful of locks twisting a little round
  it, each tapering to its own tip, fanning apart toward the end.
- A bun: a rope of three twisted locks coiled round itself from the scalp up over a full core.
"""
import math

import bmesh
import numpy as np

from .hair import mesh_from
from .hairsdf import Strands

F = np.float32
DOWN = np.asarray((0.0, 0.0, -1.0), dtype=F)


def _ss(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def _unit(v):
    return v / np.maximum(np.linalg.norm(v, axis=-1, keepdims=True), 1e-9)


def _frames(P, n0):
    """Tangents and a normal carried along a polyline without twisting (parallel transport), starting near n0."""
    T = _unit(np.gradient(P, axis=0))
    N = np.zeros_like(P)
    n = n0 - T[0] * (n0 @ T[0])
    for k in range(len(P)):
        n = _unit(n - T[k] * (n @ T[k]))
        N[k] = n
    return T, N, np.cross(T, N)


def _resample(P, s, along):
    """Values (rings x 3) at fractions ``s`` of the length of the polyline ``along``."""
    arc = np.concatenate([[0.0], np.cumsum(np.linalg.norm(np.diff(along, axis=0), axis=1))])
    t = np.asarray(s) * arc[-1]
    return np.stack([np.interp(t, arc, P[:, j]) for j in range(3)], -1)


def _tie(g, lift):
    """The tie on the hair's surface, and the way out of the head there."""
    p = g._onto(np.asarray([g.tie], dtype=F), np.asarray([g.st.cap + lift], dtype=F))
    return p[0], g._normal(p)[0]


def ponytail(g):
    """The tail's locks, the gather of hair into the tie (a solid for the sculpt) and the band round it."""
    tie, out = _tie(g, 0.002)
    r0 = 0.012                                          # the bundle's radius inside the band
    length = 0.22 if g.under_hat else 0.34
    K = 18
    t = np.linspace(0.0, 1.0, K)
    R = (r0 + (0.026 - r0) * _ss(0.0, 0.12, t)) * (1 - 0.3 * _ss(0.35, 1.0, t))
    d0 = _unit(out * 0.55 + DOWN)
    start = tie + out * 0.004
    axis = start + d0[None] * (length * t)[:, None]
    stiff = np.maximum(0.55 - 1.8 * t, 0.03).astype(F)    # it springs out of the band, then hangs
    axis = g.drape.settle(axis[:, None].astype(F), [2], R[:, None] * 0.9, rounds=60, stiff=stiff,
                          lean=(0.0, 0.7, 0.0))[0][:, 0]
    T, N, B = _frames(axis, out)
    n = 6
    a0 = 2 * math.pi * np.arange(n) / n + g.rng.uniform(-0.25, 0.25, n)
    ends = g.rng.uniform(0.8, 1.0, n)
    P = np.zeros((K, n + 1, 3), dtype=F)
    Nl = np.zeros((K, n + 1, 3), dtype=F)
    W = np.zeros((K, n + 1), dtype=F)
    H = np.zeros((K, n + 1), dtype=F)
    for i in range(n):
        s = t * ends[i]
        c, Ni, Bi = (_resample(x, s, axis) for x in (axis, N, B))
        r = np.interp(s, t, R)
        a = a0[i] + 1.6 * s                              # twisting a little round the bundle
        radial = _unit(np.cos(a)[:, None] * Ni + np.sin(a)[:, None] * Bi)
        spread = r * (0.62 - 0.25 * _ss(0.7, 1.0, s))   # coming together toward the tip
        P[:, i] = c + radial * spread[:, None]
        Nl[:, i] = radial
        W[:, i] = r * 0.85
        H[:, i] = r * 0.8
    s = t * 0.6                                          # a core fills the bundle
    P[:, n] = _resample(axis, s, axis)
    Nl[:, n] = _resample(N, s, axis)
    W[:, n] = np.interp(s, t, R) * 0.9
    H[:, n] = np.interp(s, t, R) * 0.9
    locks = Strands(P, W, H, taper=0.7, root=1.0, N=Nl)
    gather = ("cone", tie - out * 0.014, start, 0.022, r0)
    band = tie_band(g, start + d0 * 0.0015, d0, r0)
    return locks, gather, band


def bun(g):
    """The bun's coiled rope and its core (solids for the sculpt)."""
    R = 0.027 if g.under_hat else 0.036
    tie, out = _tie(g, 0.0)
    centre = tie + out * R * 0.6
    u = _unit(np.cross(out, (1.0, 0.0, 0.0)))
    v = np.cross(out, u)
    rope = R * 0.36
    K = 40
    t = np.linspace(0.0, 1.0, K)
    turns = 1.8
    az = 2 * math.pi * turns * t + g.rng.uniform(0, 2 * math.pi)
    polar = math.radians(95) * (1 - t) ** 0.85           # from round the base up over the top to the middle
    rc = R - rope * 0.75
    path = centre + rc * (np.sin(polar)[:, None] * (np.cos(az)[:, None] * u + np.sin(az)[:, None] * v) +
                          np.cos(polar)[:, None] * out)
    T, N, B = _frames(path, out)
    n = 3
    P = np.zeros((K, n, 3), dtype=F)
    Nl = np.zeros((K, n, 3), dtype=F)
    for i in range(n):
        a = 2 * math.pi * i / n + 2 * math.pi * 4.0 * t   # twisted into a rope
        radial = _unit(np.cos(a)[:, None] * N + np.sin(a)[:, None] * B)
        P[:, i] = path + radial * rope * 0.5
        Nl[:, i] = radial
    taper = 1 - 0.55 * _ss(0.75, 1.0, t)
    W = np.repeat((rope * 1.25 * taper)[:, None], n, 1)
    H = np.repeat((rope * 0.95 * taper)[:, None], n, 1)
    locks = Strands(P, W, H, taper=0.85, root=0.6, N=Nl)
    core = [("ellipsoid", centre, (R * 0.78, R * 0.78, R * 0.66), np.stack([u, v, out], 1)),
            ("cone", tie - out * 0.012, centre - out * R * 0.2, R * 0.85, R * 0.7)]
    return locks, core


def tie_band(g, centre, axis, r):
    """The band round a ponytail: a short rounded ring of cloth gripping the bundle (world space)."""
    axis = _unit(np.asarray(axis, dtype=float))
    u = _unit(np.cross(axis, (1.0, 0.0, 0.0)))
    v = np.cross(axis, u)
    head = g.head
    bm = bmesh.new()
    seg, ring = 28, 10
    rows = []
    for j in range(seg):
        a = 2 * math.pi * j / seg
        radial = math.cos(a) * u + math.sin(a) * v
        row = []
        for i in range(ring):
            b = 2 * math.pi * i / ring
            p = centre + radial * (r + 0.0014 + 0.0026 * math.cos(b)) + axis * 0.0034 * math.sin(b)
            row.append(bm.verts.new(head.at(p)))
        rows.append(row)
    col = bm.loops.layers.color.new("Color")
    for j in range(seg):
        for i in range(ring):
            a, b = rows[j], rows[(j + 1) % seg]
            f = bm.faces.new((a[i], b[i], b[(i + 1) % ring], a[(i + 1) % ring]))
            for loop in f.loops:
                loop[col] = (*g.tie_color, 1.0)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return mesh_from(bm, "hair_tie")

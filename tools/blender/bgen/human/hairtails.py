"""Tied hair (head-local units): the ponytail, the bun and the band that ties them.

- A ponytail: the hair combed back is gathered into the tie, springs back out of it in a full bundle, arcs over and
  falls down the back of the head and the nape in a gentle S, resting on the back (drape), and tapers. The
  bundle is eight broad locks twisting round a short core, apart enough that each reads as a clump with a groove
  beside it, and they part a little toward the end, which breaks into their own tips at different lengths
  instead of narrowing to one point.
- A bun: a broad flat rope wound twice round a full core, from the scalp up over the top, sitting on the crown.
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


def _tail_axis(g, start, out, length, K, R):
    """The tail's centreline: it springs back out of the tie, arcs over into a fall down the back of the head and
    neck (resting on whatever it meets) and flicks out a little at its end, a gentle S."""
    back = np.asarray((out[0], out[1], 0.0))
    back = _unit(back) if np.linalg.norm(back) > 0.2 else np.asarray((0.0, -1.0, 0.0))
    s = np.linspace(0.0, length, K)
    x = s / length
    theta = 0.85 * np.exp(-s / 0.04) - 0.38 * np.sin(np.pi * np.minimum(x * 1.3, 1.0)) + 0.22 * x ** 3
    d = np.cos(theta)[:, None] * DOWN + np.sin(theta)[:, None] * back
    P = start + np.concatenate([[np.zeros(3)], np.cumsum((d[:-1] + d[1:]) * 0.5 * (length / (K - 1)), axis=0)])
    seg = np.linalg.norm(np.diff(P, axis=0), axis=1)
    for _ in range(12):                 # kept off the head and the back, smooth, every segment its length
        q = g.drape.collide(P[1:].astype(F), (R[1:] * 0.8).astype(F))[0]
        P[1:] = q
        P[1:-1] = P[1:-1] * 0.5 + (P[:-2] + P[2:]) * 0.25
        for k in range(1, K):
            P[k] = P[k - 1] + _unit(P[k] - P[k - 1]) * seg[k - 1]
    return P.astype(F)


def ponytail(g):
    """The tail's locks, the gather of hair into the tie (a solid for the sculpt) and the band round it. The tail is
    a bundle of broad clumps round a core, full just below the band and ending in the clumps' own tips; the band
    grips it where it leaves the head."""
    tie, out = _tie(g, 0.002)
    hat = bool(g.under_hat)
    r0, top = (0.012, 0.027) if hat else (0.0135, 0.034)     # the bundle inside the band, and at its fullest
    length = 0.22 if hat else 0.32
    K = 22
    t = np.linspace(0.0, 1.0, K)
    s = t * length
    R = (r0 + (top - r0) * _ss(0.0, 0.07, s)) * (1 - 0.42 * _ss(0.35, 1.0, t))
    start = tie + out * 0.003
    axis = _tail_axis(g, start, out, length, K, R)
    T, N, B = _frames(axis, out)
    n = 8
    a0 = 2 * math.pi * np.arange(n) / n + g.rng.uniform(-0.15, 0.15, n)
    ends = 1.0 - 0.24 * g.rng.random(n)
    P = np.zeros((K, n + 1, 3), dtype=F)
    Nl = np.zeros((K, n + 1, 3), dtype=F)
    W = np.zeros((K, n + 1), dtype=F)
    H = np.zeros((K, n + 1), dtype=F)
    for i in range(n):
        u = t * ends[i]
        c, Ni, Bi = (_resample(x, u, axis) for x in (axis, N, B))
        r = np.interp(u, t, R)
        a = a0[i] + 1.1 * u                              # twisting a little round the bundle
        radial = _unit(np.cos(a)[:, None] * Ni + np.sin(a)[:, None] * Bi)
        spread = r * (0.56 + 0.12 * _ss(0.7, 1.0, u))     # parting a little toward the end
        P[:, i] = c + radial * spread[:, None]
        Nl[:, i] = radial
        W[:, i] = r * 0.95
        H[:, i] = r * 0.58
    u = t * 0.62                                         # a core fills the bundle
    P[:, n] = _resample(axis, u, axis)
    Nl[:, n] = _resample(N, u, axis)
    W[:, n] = np.interp(u, t, R) * 1.0
    H[:, n] = np.interp(u, t, R) * 1.0
    locks = Strands(P, W, H, taper=0.55, root=1.0, N=Nl, round=0.5, blend=1.3, blunt=0.25, merge=0.35)
    gather = ("cone", tie - out * 0.014, start, 0.022, r0 * 0.8)
    band = tie_band(g, axis[0] + T[0] * 0.003, T[0], r0 * 0.82 + 0.0005)
    return locks, gather, band


def bun(g):
    """The bun's coiled rope (locks for the sculpt), its core and the pad it sits on (solids). A broad, flat rope
    wound twice round a full core from the scalp up over the top, so the bun reads as one wrapped knot sitting right
    on the back of the crown."""
    R = 0.027 if g.under_hat else 0.034
    tie, out = _tie(g, 0.0)
    centre = tie + out * R * 0.32
    u = _unit(np.cross(out, (1.0, 0.0, 0.0)))
    v = np.cross(out, u)
    K = 56
    t = np.linspace(0.0, 1.0, K)
    turns = 2.1
    az = 2 * math.pi * turns * t + g.rng.uniform(0, 2 * math.pi)
    polar = math.radians(92) * (1 - t) ** 0.8            # from round the base up over the top to the middle
    rope = R * 0.36                                      # the rope's half thickness
    rc = R - rope * 0.6                                  # on the core, a little flatter along ``out``
    radial = np.sin(polar)[:, None] * (np.cos(az)[:, None] * u + np.sin(az)[:, None] * v)
    path = centre + rc * radial + rc * 0.82 * np.cos(polar)[:, None] * out
    Nl = _unit(path - centre)
    P = path[:, None].astype(F)
    width = R * (1.0 - 0.35 * _ss(0.6, 1.0, t))          # wide enough that the turns lie against each other
    locks = Strands(P, width[:, None], np.full((K, 1), rope * 2.0, dtype=F), taper=0.88, root=0.7, N=Nl[:, None],
                    round=0.4, blend=1.6)
    rot = np.stack([u, v, out], 1)
    core = [("ellipsoid", centre, (R * 0.8, R * 0.8, R * 0.66), rot),
            ("ellipsoid", tie, (R * 0.95, R * 0.95, R * 0.4), rot)]
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

"""Strands that lie on the body and hang off it under their own weight: a mummy's loose bandage ends (wraps.py) and
a swamp creature's weed (moss.py).

A strand is traced from its root in small steps: while it touches the body it slides down the surface (gravity laid
into the surface's slope), held a little above it; where the surface turns away under it (the underside of an arm,
the back of the thigh) it leaves it and falls, swinging a little. Each point remembers the skin it last lay on, so a
strand takes the weights of the skin it covers and moves with that limb rather than standing off it rigidly.
"""
import math

import numpy as np
from mathutils import Vector

DOWN = np.asarray((0.0, 0.0, -1.0))
# a flat strand's section (across, up): broad faces, rounded only at the edges so they stay lit like the cloth
SECTION = np.asarray(((1.0, 0.0), (0.92, 0.75), (0.0, 1.0), (-0.92, 0.75), (-1.0, 0.0), (-0.92, -0.75), (0.0, -1.0),
                      (0.92, -0.75)))


def _unit(v):
    n = np.linalg.norm(v)
    return v / n if n > 1e-9 else v


def trace(tree, root, normal, length, n, gap, rng, sway=0.15, cling=-0.35):
    """(points, normals, skin points) of a strand from ``root`` (on the surface, with its ``normal``): ``n`` points
    over ``length``, held ``gap`` above the surface while it lies on it. It clings to surfaces whose normal points up
    more than ``cling`` (its z); off steeper ones it falls, only pushed out of the body where it would pass into it."""
    root, normal = np.asarray(root, dtype=float), _unit(np.asarray(normal, dtype=float))
    step = length / (n - 1)
    phase, side0 = rng.uniform(0, 2 * math.pi), _unit(np.cross(normal, DOWN))
    if np.linalg.norm(side0) < 1e-6:
        side0 = np.asarray((1.0, 0.0, 0.0))
    p = root + normal * gap
    if abs(normal[2]) < 0.95:
        d = _unit(DOWN - normal * min(DOWN.dot(normal), 0.0))
    else:                                           # level: off along the surface; under it: straight down
        d = side0 if normal[2] > 0 else DOWN
    pts, nrms, skin = [p], [normal], [root]
    nn, last = normal, root
    for i in range(1, n):
        q = p + d * step
        loc, nrm, _, _ = tree.find_nearest(Vector(q), gap * 3 + step)
        lying = False
        if loc is not None:
            loc, nrm = np.asarray(loc), np.asarray(nrm)
            off = (q - loc).dot(nrm)
            if off < gap or (off < gap * 3 and nrm[2] > cling):     # inside, or near a surface it can lie on
                q, nn, last, lying = loc + nrm * gap, nrm, loc, True
        wobble = side0 * sway * math.sin(phase + i * 0.9)
        if lying:
            g = DOWN - nn * DOWN.dot(nn)
            if np.linalg.norm(g) > 0.25:
                d = _unit(g + wobble)
            else:                                   # on level ground: keep creeping the way it was going
                along = d - nn * d.dot(nn)
                d = _unit(along if np.linalg.norm(along) > 0.2 else side0 - nn * side0.dot(nn))
        else:
            d = _unit(d * 0.35 + DOWN * 0.65 + wobble * 0.5)
        p = q
        pts.append(p)
        nrms.append(nn)
        skin.append(last)
    return np.asarray(pts), np.asarray(nrms), np.asarray(skin)


def ribbon(pts, nrms, skin, width, thick, rng, taper=0.25, ragged=0.0, section=None):
    """A flat strand along traced points: a thin flat section ``width`` across, rounded at its edges (tapering by
    ``taper`` toward the end, which is torn raggedly by ``ragged`` times the width), its face toward the surface it
    lies on (``section``: its outline, SECTION by default). Returns (verts, tris, across, along, skin point) per vertex,
    as wrapstrip's strips are."""
    section = SECTION if section is None else np.asarray(section, dtype=float)
    n, m = len(pts), len(section)
    tang = np.gradient(pts, axis=0)
    tang /= np.maximum(np.linalg.norm(tang, axis=1, keepdims=True), 1e-9)
    along = np.concatenate([[0.0], np.cumsum(np.linalg.norm(np.diff(pts, axis=0), axis=1))])
    twist = rng.uniform(-0.6, 0.6)
    cut = np.interp(section[:, 0], np.linspace(-1, 1, 4), rng.uniform(-1.0, 1.0, 4)) * ragged * width
    V, A, L, S = [], [], [], []
    for i in range(n):
        t = i / (n - 1)
        up = _unit(nrms[i] - tang[i] * nrms[i].dot(tang[i]))
        side = _unit(np.cross(up, tang[i]))
        a = twist * t * t
        side, up = side * math.cos(a) + up * math.sin(a), up * math.cos(a) - side * math.sin(a)
        hw = 0.5 * width * (1.0 - taper * t)
        for k, (x, y) in enumerate(section):
            q = pts[i] + side * x * hw + up * y * thick * 0.5
            if i == n - 1:
                q = q + tang[i] * cut[k]
            V.append(q)
            A.append(0.5 + 0.5 * x)
            L.append(along[i])
            S.append(skin[i])
    T = []
    for i in range(n - 1):
        for k in range(m):
            a, b = i * m + k, i * m + (k + 1) % m
            T += [(a, b, b + m), (a, b + m, a + m)]
    for r, flip in ((0, True), ((n - 1) * m, False)):          # close both ends
        for k in range(1, m - 1):
            T.append((r, r + k + 1, r + k) if flip else (r, r + k, r + k + 1))
    return np.asarray(V), np.asarray(T, dtype=np.int64), np.asarray(A), np.asarray(L), np.asarray(S)

"""Tufts of fur: clumps scattered over a skin surface (spaced by their width, so they overlap like shingles along
the flow), each rooted in the undercoat, laid along the flow following the surface under it and lifting toward
its tips, and split into three tapered strands that fan a little apart toward their points (the middle one the
longest, lying on top), so the coat reads as shaggy hair rather than scales. Built as one mesh with strand UVs (U along each tuft) and the bone
weights of the skin at its root, so a tuft moves with the skin it grows from.
"""
import math

import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

from .coverings import make_mesh

TC = np.linspace(0.0, 1.0, 6)                     # where a tuft's centre line is sampled (0 root .. 1 tip)
TS = np.asarray((0.0, 0.45, 0.8))                 # ring positions along each strand (the tip closes beyond)
SECTION = ((1.0, 0.0), (0.0, 0.9), (-1.0, 0.0))     # a strand's section: a low ridge, flat underneath
SIDES = len(SECTION)
FLAT = 0.45                                       # thickness / width
STRANDS = 3                                       # strands per tuft


def _sample(V, T, N, L, rng, min_len):
    """Root candidates: area-weighted points (more where tufts are small), with interpolated normal and length."""
    a, b, c = V[T[:, 0]], V[T[:, 1]], V[T[:, 2]]
    area = 0.5 * np.linalg.norm(np.cross(b - a, c - a), axis=1)
    Lt = L[T].mean(1)
    ok = Lt > min_len
    if not ok.any():
        return None
    width = 0.34 * np.maximum(Lt, min_len)
    w = np.where(ok, area / (width * width), 0.0)
    count = int(w.sum() * 12) + 8
    tri = rng.choice(len(T), size=count, p=w / w.sum())
    u, v = rng.random(count), rng.random(count)
    flip = u + v > 1
    u[flip], v[flip] = 1 - u[flip], 1 - v[flip]
    bary = np.stack([1 - u - v, u, v], 1)
    return tri, bary


def _space(P, R, order):
    """Greedy dart throwing: keeps points no closer than the mean of their radii, in ``order``."""
    cell = float(R.max())
    grid = {}
    keep = []
    for i in order:
        key = tuple((P[i] // cell).astype(int))
        ok = True
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                for dz in (-1, 0, 1):
                    for j in grid.get((key[0] + dx, key[1] + dy, key[2] + dz), ()):
                        if np.sum((P[i] - P[j]) ** 2) < (0.5 * (R[i] + R[j])) ** 2:
                            ok = False
                            break
                    if not ok:
                        break
                if not ok:
                    break
            if not ok:
                break
        if ok:
            grid.setdefault(key, []).append(i)
            keep.append(i)
    return np.asarray(keep, dtype=np.int64)


def build(name, V, T, N, W, flow, L, shell, tone, lift, seed=1, min_len=None, slot="char:hair", spacing=0.62):
    """Tufts over the surface (V, T) with normals N, per-vertex weights W, fur direction ``flow``, tuft length L,
    undercoat height ``shell`` and colour ``tone`` (per vertex); ``lift`` raises the tips. Returns an object or None."""
    out = arrays(V, T, N, W, flow, L, shell, tone, lift, seed, min_len, spacing)
    return None if out is None else make_mesh(name, *out[:2], out[2], slot, out[4], out[3])


def arrays(V, T, N, W, flow, L, shell, tone, lift, seed=1, min_len=None, spacing=0.62):
    """The tufts as (verts, triangles, colours, uvs, weights) per vertex, or None (see build)."""
    rng = np.random.default_rng(seed)
    min_len = min_len if min_len is not None else 0.25 * float(np.max(L)) if len(L) else 0.0
    if not len(L) or float(np.max(L)) <= 0:
        return None
    min_len = max(min_len, 1e-4)
    found = _sample(V, T, N, L, rng, min_len)
    if found is None:
        return None
    tri, bary = found
    corners = T[tri]
    P = np.einsum("ij,ijk->ik", bary, V[corners])
    n = np.einsum("ij,ijk->ik", bary, N[corners])
    n /= np.linalg.norm(n, axis=1, keepdims=True)
    length = np.einsum("ij,ij->i", bary, L[corners])
    good = length > min_len * 0.8
    P, n, length, tri, bary, corners = P[good], n[good], length[good], tri[good], bary[good], corners[good]
    width = 0.55 * length
    keep = _space(P, spacing * width, rng.permutation(len(P)))
    P, n, length, width, bary, corners = P[keep], n[keep], length[keep], width[keep], bary[keep], corners[keep]
    m = len(P)
    if not m:
        return None
    f = np.einsum("ij,ijk->ik", bary, flow[corners])
    f -= n * np.einsum("ij,ij->i", f, n)[:, None]
    weak = np.linalg.norm(f, axis=1) < 1e-3
    f[weak] = np.cross(n[weak], (1.0, 0.0, 0.0))
    f /= np.linalg.norm(f, axis=1, keepdims=True)
    sh = np.einsum("ij,ij->i", bary, shell[corners])
    col = np.einsum("ij,ijk->ik", bary, tone[corners])
    length *= rng.uniform(0.82, 1.18, m)
    width *= rng.uniform(0.85, 1.15, m)
    bend = rng.uniform(-0.18, 0.18, m)
    rise = lift * rng.uniform(0.55, 1.45, m)
    shade = rng.uniform(0.88, 1.1, m)
    turn = rng.uniform(-0.25, 0.25, m)
    f = f * np.cos(turn)[:, None] + np.cross(n, f) * np.sin(turn)[:, None]
    tree = BVHTree.FromPolygons([Vector(v) for v in V], [tuple(t) for t in T.tolist()])

    # the clump's centre line, sampled along its length, following the surface (and riding over the undercoat) under it
    C = np.zeros((m, len(TC), 3))
    Nn = np.zeros((m, len(TC), 3))
    for k, t in enumerate(TC):
        rise_k = math.sin(min(t / 0.25, 1.0) * math.pi / 2)
        straight = P + f * (length * t)[:, None] + np.cross(n, f) * (bend * length * t * t)[:, None]
        for i in range(m):
            base, nn, sl = straight[i], n[i], sh[i]
            if t > 0:
                loc, normal, fi, dist = tree.find_nearest(Vector(straight[i]), float(length[i]) * 0.6)
                if loc is not None and normal.dot(Vector(n[i])) > 0.35:
                    base, nn, sl = np.asarray(loc), np.asarray(normal), max(sh[i], float(shell[T[fi]].mean()))
            C[i, k] = base + nn * (sl * 0.7 + (sl * 0.35 + 0.1 * width[i]) * rise_k + rise[i] * length[i] * t * t)
            Nn[i, k] = nn
    tang = np.gradient(C, axis=1)
    tang /= np.maximum(np.linalg.norm(tang, axis=2, keepdims=True), 1e-9)
    side = np.cross(Nn, tang)
    side /= np.maximum(np.linalg.norm(side, axis=2, keepdims=True), 1e-9)
    up = np.cross(tang, side)

    def at(arr, t):                                  # (m, 3) along each centre line at per-clump t (m,)
        x = np.clip(t, 0.0, 1.0) * (len(TC) - 1)
        i0 = np.minimum(np.floor(x).astype(int), len(TC) - 2)
        a = (x - i0)[:, None]
        rows = np.arange(m)
        return arr[rows, i0] * (1 - a) + arr[rows, i0 + 1] * a

    # each clump splits into strands fanning a little apart toward their tips (the middle one longest, on top)
    per_strand = len(TS) * SIDES + 1
    per = STRANDS * per_strand
    verts = np.zeros((m, per, 3))
    colors = np.zeros((m, per, 3))
    uvs = np.zeros((m, per, 2))
    for q in range(STRANDS):
        lane = q - (STRANDS - 1) / 2
        lf = np.ones(m) if lane == 0 else rng.uniform(0.66, 0.9, m)
        fan = lane * rng.uniform(0.16, 0.3, m)
        sw = width * (0.66 if lane == 0 else 0.56) * rng.uniform(0.9, 1.1, m)
        tone_q = col * (shade * rng.uniform(0.9, 1.1, m))[:, None]
        drop = (0.0 if lane == 0 else -0.12) * width
        o = q * per_strand
        for k, t in enumerate(list(TS) + [1.0]):
            tt = t * lf
            c = at(C, tt) + at(side, tt) * (width * lane * 0.1 + width * fan * t)[:, None] + at(up, tt) * (drop * t)[:, None]
            if k == len(TS):
                verts[:, o + per_strand - 1] = c
                colors[:, o + per_strand - 1] = tone_q * 1.08
                uvs[:, o + per_strand - 1] = np.stack([length * tt * 6.0, np.zeros(m)], 1)
                continue
            sd, u = at(side, tt), at(up, tt)
            half = 0.5 * sw * (0.85 + 0.15 * math.sin(min(t / 0.3, 1.0) * math.pi / 2)) * (1 - t ** 1.3) ** 0.8
            for j, (sx, sy) in enumerate(SECTION):
                idx = o + k * SIDES + j
                verts[:, idx] = c + sd * (sx * half)[:, None] + u * (sy * half * FLAT)[:, None]
                colors[:, idx] = tone_q * (0.74 + 0.34 * t ** 0.8)
                uvs[:, idx] = np.stack([length * tt * 6.0, np.full(m, j / SIDES * 0.6)], 1)
    faces = []
    rings = len(TS)
    for q in range(STRANDS):
        o = q * per_strand
        for k in range(rings - 1):
            for j in range(SIDES):
                a, b = o + k * SIDES + j, o + k * SIDES + (j + 1) % SIDES
                faces += [(a, b, b + SIDES), (a, b + SIDES, a + SIDES)]
        for j in range(SIDES):
            faces.append((o + (rings - 1) * SIDES + j, o + (rings - 1) * SIDES + (j + 1) % SIDES, o + per_strand - 1))
    faces = np.asarray(faces, dtype=np.int64)
    F = (faces[None, :, :] + (np.arange(m) * per)[:, None, None]).reshape(-1, 3)
    weights = []
    for i in range(m):
        w = {}
        for c, x in zip(corners[i], bary[i]):
            for bone, y in W[c].items():
                w[bone] = w.get(bone, 0.0) + y * x
        weights += [w] * per
    return verts.reshape(-1, 3), F, colors.reshape(-1, 3), uvs.reshape(-1, 2), weights

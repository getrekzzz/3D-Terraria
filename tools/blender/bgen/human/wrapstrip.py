"""Strips of cloth wound round a body (wraps.py): the surface is measured by casting rays out from an axis (a limb's
bones, the spine, the neck) or from a centre (the skull), and a strip is laid over it along a helix, each turn's
leading edge lying over the turn before so the edges step down toward where the winding began; or along a curve
drawn over the surface (straps across the shoulders, a strip between the legs, bands over the crown).

Each strip comes back as arrays: vertices, triangles and per-vertex (across 0..1 from the raised edge, along in
metres, the surface point under it), for wraps.py to colour and weight.
"""
import math

import numpy as np
from mathutils import Vector

ACROSS = np.asarray((0.0, 0.07, 0.42, 0.8, 1.0))       # profile stations across a wound strip, from its raised edge
LIFT = np.asarray((0.2, 1.0, 1.0, 0.4, 0.0))           # their height (times the cloth's thickness)


class Surface:
    """Radii of a surface round an axis polyline (``pts``) or, with ``centre``, round a point (spherical: t is the
    elevation from ``t0`` to ``t1`` radians), sampled on a (t, angle) grid by ray casts into ``tree`` (or the
    furthest hit among a list of trees: the neck, where the body's collar lies over the head's)."""

    def __init__(self, tree, pts=None, centre=None, t_range=(-0.4, 1.3), samples=72, step=0.008, reach=0.5, up=None):
        self.sphere = centre is not None
        self.phis = np.linspace(0.0, 2 * math.pi, samples + 1)
        if self.sphere:
            self.c = np.asarray(centre, dtype=float)
            self.ts = np.linspace(t_range[0], t_range[1], 64)
            self.length = 1.0
        else:
            P = np.asarray([p[:] for p in pts], dtype=float)
            seg = np.linalg.norm(np.diff(P, axis=0), axis=1)
            acc = np.concatenate([[0.0], np.cumsum(seg)])
            self.length = float(acc[-1])
            n = max(8, int(self.length / step))
            self.ts = np.linspace(0.0, 1.0, n)
            self.axis = np.stack([np.interp(self.ts * self.length, acc, P[:, i]) for i in range(3)], 1)
            tang = np.gradient(self.axis, axis=0)
            tang /= np.linalg.norm(tang, axis=1, keepdims=True)
            ref = np.asarray(up if up is not None else (0.0, 1.0, 0.0), dtype=float)
            nrm = []
            v = ref - tang[0] * ref.dot(tang[0])
            for t in tang:                       # carried along without twisting
                v = v - t * v.dot(t)
                v /= np.linalg.norm(v)
                nrm.append(v)
            self.n = np.asarray(nrm)
            self.b = np.cross(tang, self.n)
        trees = tree if isinstance(tree, (list, tuple)) else [tree]
        R = np.full((len(self.ts), len(self.phis)), np.nan)
        for i in range(len(self.ts)):
            for k, ph in enumerate(self.phis):
                o, d = self._ray(i, ph)
                for tr in trees:
                    hit = tr.ray_cast(Vector(o), Vector(d), reach)
                    if hit[0] is not None and not hit[3] < R[i, k]:
                        R[i, k] = hit[3]
        self.hit = ~np.isnan(R)
        for i in range(len(self.ts)):            # fill misses from neighbours round the ring, then along
            row = R[i]
            if np.isnan(row).all():
                continue
            ok = ~np.isnan(row)
            idx = np.arange(len(row))
            R[i] = np.interp(idx, idx[ok], row[ok], period=len(row))
        for k in range(len(self.phis)):
            col = R[:, k]
            ok = ~np.isnan(col)
            if ok.any():
                R[:, k] = np.interp(np.arange(len(col)), np.flatnonzero(ok), col[ok])
        self.R = np.nan_to_num(R, nan=0.05)

    def missed(self, t, ph):
        """True where the rays nearest (t, ph) found no surface within reach (a strip should leave it out)."""
        i = np.clip(np.rint(np.interp(t, self.ts, np.arange(len(self.ts)))).astype(int), 0, len(self.ts) - 1)
        k = np.rint(np.mod(ph, 2 * math.pi) / (2 * math.pi) * (len(self.phis) - 1)).astype(int)
        return ~self.hit[i, k]

    def _dir(self, i, ph):
        if self.sphere:
            el = self.ts[i]
            return np.asarray((math.cos(el) * math.sin(ph), math.cos(el) * math.cos(ph), math.sin(el)))
        return self.n[i] * math.cos(ph) + self.b[i] * math.sin(ph)

    def _ray(self, i, ph):
        return (self.c if self.sphere else self.axis[i]), self._dir(i, ph)

    def point(self, t, ph, h):
        """Surface points (and the points ``h`` above them) at parameters t (0..1 along the axis, or elevation) and
        angle ph; arrays."""
        t = np.asarray(t, dtype=float)
        ph = np.mod(np.asarray(ph, dtype=float), 2 * math.pi)
        fi = np.clip(np.interp(t, self.ts, np.arange(len(self.ts))), 0, len(self.ts) - 1.0001)
        fk = ph / (2 * math.pi) * (len(self.phis) - 1)
        i0, k0 = np.floor(fi).astype(int), np.floor(fk).astype(int)
        a, b = fi - i0, fk - k0
        k1 = np.minimum(k0 + 1, len(self.phis) - 1)
        R = (self.R[i0, k0] * (1 - a) * (1 - b) + self.R[i0 + 1, k0] * a * (1 - b) + self.R[i0, k1] * (1 - a) * b
             + self.R[i0 + 1, k1] * a * b)
        if self.sphere:
            d = np.stack([np.cos(t) * np.sin(ph), np.cos(t) * np.cos(ph), np.sin(t)], -1)
            base = np.broadcast_to(self.c, d.shape)
        else:
            n = self.n[i0] * (1 - a)[..., None] + self.n[i0 + 1] * a[..., None]
            bb = self.b[i0] * (1 - a)[..., None] + self.b[i0 + 1] * a[..., None]
            d = n * np.cos(ph)[..., None] + bb * np.sin(ph)[..., None]
            d /= np.linalg.norm(d, axis=-1, keepdims=True)
            base = self.axis[i0] * (1 - a)[..., None] + self.axis[i0 + 1] * a[..., None]
        surf = base + d * R[..., None]
        return surf, surf + d * np.asarray(h)[..., None]


def helix(surf, t0, t1, width, pitch, offset, thick, phase=0.0, segs=40, spin=1, skip=None):
    """A strip wound from t0 to t1 round ``surf`` (``width`` and ``pitch``, its advance per turn, in t units; heights
    in metres), turning ``spin`` (+1 or -1) about the axis; its advance wavers a little, so the turns crowd and part.
    ``skip(t, ph)`` -> True leaves stretches out (an eye slit). Returns arrays as _grid does, or None."""
    turns = abs(t1 - t0) / pitch
    n = max(2, int(turns * segs) + 1)
    u = np.linspace(0.0, 1.0, n)
    ph = phase + spin * 2 * math.pi * turns * u
    rate = 1.0 + 0.22 * np.sin(u * turns * 1.7 + phase * 3.1) + 0.1 * np.sin(u * turns * 4.3 + phase)
    acc = np.concatenate([[0.0], np.cumsum(rate[1:] * np.diff(u))])
    tc = t0 + (t1 - t0) * acc / acc[-1]
    sgn = 1.0 if t1 > t0 else -1.0
    T = tc[:, None] + sgn * (ACROSS[None, :] - 0.5) * width       # across 0: the raised edge, toward t0
    PH = np.repeat(ph[:, None], len(ACROSS), 1)
    H = np.broadcast_to(offset + LIFT[None, :] * thick, T.shape)
    surf_pts, pts = surf.point(T, PH, H)
    keep = np.ones(n, dtype=bool) if skip is None else ~skip(tc, ph)
    along = np.concatenate([[0.0], np.cumsum(np.linalg.norm(np.diff(pts[:, 2], axis=0), axis=1))])
    return _grid(pts, surf_pts, keep, along, ACROSS)


def _grid(pts, surf_pts, keep, along, across):
    """(verts, tris, across per vertex, along per vertex, surface point per vertex) for a grid of rows of points;
    rows i and i + 1 are joined where both are kept."""
    n, m = pts.shape[:2]
    idx = np.arange(n * m).reshape(n, m)
    rows = np.flatnonzero(keep[:-1] & keep[1:])
    if not len(rows):
        return None
    a, b = idx[rows][:, :-1], idx[rows][:, 1:]
    c, d = idx[rows + 1][:, 1:], idx[rows + 1][:, :-1]
    tris = np.concatenate([np.stack([a, b, c], -1).reshape(-1, 3), np.stack([a, c, d], -1).reshape(-1, 3)])
    return (pts.reshape(-1, 3), tris, np.tile(np.asarray(across, dtype=float), n), np.repeat(along, m),
            surf_pts.reshape(-1, 3))


def ribbon(tree, curve, width, offset, thick, n=None):
    """A flat strip along a curve over the surface (control points near it): projected onto the surface, raised a
    little in the middle and turned under at both edges. Returns arrays as _grid does."""
    from .features import spline
    length = sum((Vector(b) - Vector(a)).length for a, b in zip(curve, curve[1:]))
    n = n or max(6, int(length / 0.012))
    path = spline([Vector(c) for c in curve], n)
    P, N = [], []
    for q in path:
        loc, nrm, _, _ = tree.find_nearest(q)
        if loc is not None and (not P or (np.asarray(loc) - P[-1]).dot(np.asarray(loc) - P[-1]) > 1e-8):
            P.append(np.asarray(loc))
            N.append(np.asarray(nrm))
    if len(P) < 3:
        return None
    P, N, n = np.asarray(P), np.asarray(N), len(P)
    for _ in range(4):                           # smooth the normals along it
        N[1:-1] = (N[:-2] + 2 * N[1:-1] + N[2:]) / 4
        N /= np.linalg.norm(N, axis=1, keepdims=True)
    tang = np.gradient(P, axis=0)
    tang /= np.linalg.norm(tang, axis=1, keepdims=True)
    side = np.cross(N, tang)
    side /= np.linalg.norm(side, axis=1, keepdims=True)
    across = np.asarray((0.0, 0.08, 0.5, 0.92, 1.0))
    lift = np.asarray((0.2, 1.0, 1.15, 1.0, 0.2))
    surf = P[:, None, :] + side[:, None, :] * ((across - 0.5) * width)[None, :, None]
    pts = surf + N[:, None, :] * (offset + lift * thick)[None, :, None]
    along = np.concatenate([[0.0], np.cumsum(np.linalg.norm(np.diff(P, axis=0), axis=1))])
    return _grid(pts, surf, np.ones(n, dtype=bool), along, across)


def hanging(start, tangent, normal, width, length, thick, seed, n=10):
    """A loose end hanging from ``start``: leaving the surface along ``tangent``, falling under its weight, twisting a
    little and torn off in a ragged edge; two sheets back to back. Returns arrays as _grid does (the surface point of
    every vertex is the root)."""
    rng = np.random.default_rng(seed)
    start, tangent, normal = (np.asarray(v, dtype=float) for v in (start, tangent, normal))
    side = np.cross(normal, tangent)
    side /= np.linalg.norm(side)
    down = np.asarray((0.0, 0.0, -1.0))
    twist = rng.uniform(-0.9, 0.9)
    cols = np.asarray((-0.5, -0.25, 0.0, 0.25, 0.5))
    ragged = rng.uniform(0.0, 1.0, len(cols))
    rows = []
    for i in range(n):
        u = i / (n - 1)
        c = start + tangent * length * 0.22 * math.sin(min(u * 2.0, 1.0) * math.pi / 2) + down * length * u ** 1.15 \
            + normal * length * 0.08 * math.sin(u * math.pi)
        a = twist * u
        sd = side * math.cos(a) + normal * math.sin(a)
        face = np.cross(sd, down if u > 0.1 else tangent)
        face /= max(np.linalg.norm(face), 1e-9)
        front = [c + sd * v * width * (1.0 - 0.12 * u) + down * (length * 0.07 * ragged[k] * (i == n - 1)) for k, v in enumerate(cols)]
        back = [q - face * thick for q in front]
        rows.append(front + back[::-1])
    pts = np.asarray(rows)
    surf = np.broadcast_to(start, pts.shape).copy()
    across = np.concatenate([cols + 0.5, (cols + 0.5)[::-1]])
    return _grid(pts, surf, np.ones(n, dtype=bool), np.linspace(0.0, length, n), across)

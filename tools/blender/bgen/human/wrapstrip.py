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
    furthest hit among a list of trees: the neck, where the body's collar lies over the head's). ``bridge`` passes
    lift the radii over hollows narrower than a few samples (the crease under the lip, beside the nose), as cloth
    pulled over them spans them."""

    def __init__(self, tree, pts=None, centre=None, t_range=(-0.4, 1.3), samples=72, step=0.008, reach=0.5, up=None,
                 bridge=0):
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
        R = np.nan_to_num(R, nan=0.05)
        if bridge:                               # a pit one sample across (a nostril) is spanned outright
            Rp = np.concatenate([R[:, -2:-1], R, R[:, 1:2]], 1)
            Rp = np.concatenate([Rp[:1], Rp, Rp[-1:]], 0)
            ring = np.stack([Rp[1 + a:Rp.shape[0] - 1 + a, 1 + b:Rp.shape[1] - 1 + b]
                             for a in (-1, 0, 1) for b in (-1, 0, 1) if a or b])
            R = np.maximum(R, np.median(ring, axis=0))
        for _ in range(bridge):
            Rp = np.concatenate([R[:, -2:-1], R, R[:, 1:2]], 1)     # the seam at 0 / 2 pi wraps round
            Rp = np.concatenate([Rp[:1], Rp, Rp[-1:]], 0)
            blur = (Rp[:-2, 1:-1] + Rp[2:, 1:-1] + Rp[1:-1, :-2] + Rp[1:-1, 2:] + 2 * Rp[1:-1, 1:-1]) / 6
            R = np.maximum(R, blur)
        self.R = R

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


def helix(surf, t0, t1, width, pitch, offset, thick, phase=0.0, segs=40, spin=1, skip=None, part=None):
    """A strip wound from t0 to t1 round ``surf`` (``width`` and ``pitch``, its advance per turn, in t units; heights
    in metres), turning ``spin`` (+1 or -1) about the axis; its advance wavers a little, so the turns crowd and part.
    ``skip(t, ph)`` -> True leaves stretches out (where the surface is missed); ``part(ph)`` -> (t, half, amount)
    opens a gap between the turns there (an eye slit): the turns either side of t are pushed back off it to t +- half,
    crowding their edges there (by ``amount``, 0..1, so the push fades out round the sides). Returns arrays as _grid
    does, or None."""
    turns = abs(t1 - t0) / pitch
    n = max(2, int(turns * segs) + 1)
    u = np.linspace(0.0, 1.0, n)
    ph = phase + spin * 2 * math.pi * turns * u
    rate = 1.0 + 0.22 * np.sin(u * turns * 1.7 + phase * 3.1) + 0.1 * np.sin(u * turns * 4.3 + phase)
    acc = np.concatenate([[0.0], np.cumsum(rate[1:] * np.diff(u))])
    tc = t0 + (t1 - t0) * acc / acc[-1]
    sgn = 1.0 if t1 > t0 else -1.0
    T = tc[:, None] + sgn * (ACROSS[None, :] - 0.5) * width       # across 0: the raised edge, toward t0
    if part is not None:
        mid, half, amount = part(ph)
        front = ph - (np.mod(ph + math.pi, 2 * math.pi) - math.pi)      # each turn takes one side, where it crosses ph 0
        order = np.argsort(ph)
        above = (np.interp(front, ph[order], tc[order]) >= part(front)[0])[:, None]
        opened = np.where(above, np.maximum(T, (mid + half)[:, None]), np.minimum(T, (mid - half)[:, None]))
        T = T + (opened - T) * amount[:, None]
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


def ribbon(tree, curve, width, offset, thick, n=None, tuck=0.0):
    """A flat strip along a curve over the surface (control points near it): projected onto the surface, raised a
    little in the middle and turned under at both edges; over ``tuck`` metres at each end it sinks to the surface, so
    its ends pass under the turns it crosses. Returns arrays as _grid does."""
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
    along = np.concatenate([[0.0], np.cumsum(np.linalg.norm(np.diff(P, axis=0), axis=1))])
    sink = np.ones(n)
    if tuck > 0:
        e = np.minimum(along, along[-1] - along) / tuck
        sink = np.clip(e, 0.0, 1.0) ** 2 * (3 - 2 * np.clip(e, 0.0, 1.0))
    pts = surf + N[:, None, :] * (sink[:, None] * (offset + lift * thick)[None, :])[..., None]
    return _grid(pts, surf, np.ones(n, dtype=bool), along, across)


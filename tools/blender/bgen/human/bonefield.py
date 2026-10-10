"""Distance-field building blocks for the skeleton (skeletal.py, skulls.py): primitives that know the box their
surface lies in, a union (``Body``) that evaluates each part only near it, so a field of hundreds of small parts stays
fast, and the shapes bones need beyond sdf.py: rounded cylinders, thin triangular plates, flattened tubes and local
frames along a bone. Units are the field's own (metres, or head-local units for the skull).
"""
import math

import numpy as np

from . import sdf
from .sdf import F, vec


def basis(x_dir, y_hint):
    """Local-to-world rotation (columns are the local axes): x along ``x_dir``, y toward ``y_hint``, z = x cross y."""
    x = np.asarray(x_dir, dtype=float)
    x = x / np.linalg.norm(x)
    y = np.asarray(y_hint, dtype=float)
    y = y - x * (y @ x)
    y = y / np.linalg.norm(y)
    return np.stack([x, y, np.cross(x, y)], 1)


class Bone:
    """Points measured along a bone from ``a`` toward ``b``: at(u, y, z) is u metres along it, y toward ``front``
    and z along x cross y (outward on a right-side limb)."""

    def __init__(self, a, b, front=(0.0, 1.0, 0.0)):
        self.a = np.asarray(a, dtype=float)
        self.b = np.asarray(b, dtype=float)
        self.length = float(np.linalg.norm(self.b - self.a))
        self.rot = basis(self.b - self.a, front)

    def at(self, u, y=0.0, z=0.0):
        return self.a + self.rot @ np.asarray((u, y, z), dtype=float)

    def ax(self, i):
        return self.rot[:, i]


class Part:
    """A distance function and the box its surface lies in."""

    def __init__(self, fn, lo, hi):
        self.fn, self.lo, self.hi = fn, np.asarray(lo, dtype=F), np.asarray(hi, dtype=F)


def _box(points, r):
    pts = np.asarray(points, dtype=float).reshape(-1, 3)
    return pts.min(0) - r, pts.max(0) + r


def ball(c, r):
    return Part(lambda p: sdf.sphere(p, c, r), *_box([c], r))


def cone(a, b, ra, rb):
    return Part(lambda p: sdf.round_cone(p, a, b, ra, rb), *_box([a, b], max(ra, rb)))


def capsule(a, b, r):
    return cone(a, b, r, r)


def ell(c, r, rot=None):
    return Part(lambda p: sdf.ellipsoid(p, c, r, rot), *_box([c], max(r)))


def tube(pts, radii):
    pts = [np.asarray(q, dtype=float) for q in pts]
    return Part(lambda p: sdf.tube(p, pts, radii), *_box(pts, max(radii)))


def flat_tube(pts, radii, axis, squash):
    """A tube whose section is stretched 1/``squash`` times along ``axis`` (ribs, the jaw's body): it is a tube in
    space squashed along that axis, so distances along it read short (still safe for the narrow band)."""
    axis = np.asarray(axis, dtype=float) / np.linalg.norm(axis)
    m = np.eye(3) - (1 - squash) * np.outer(axis, axis)
    q = [m @ np.asarray(x, dtype=float) for x in pts]
    mf = vec(m)
    lo, hi = _box(pts, max(radii) / min(squash, 1.0))
    return Part(lambda p: sdf.tube(p @ mf, q, radii), lo, hi)


def rod(c, rot, r, h, rr, ry=None):
    """A cylinder round local x through ``c`` (``rot`` local-to-world): radius r (``ry`` across local y for an oval
    section), half-length h, edges rounded by rr."""
    c, rotf = vec(c), vec(rot)
    sy = r / (ry or r)

    def fn(p):
        q = (p - c) @ rotf
        rho = np.sqrt(q[:, 1] * q[:, 1] * F(sy * sy) + q[:, 2] * q[:, 2])
        dx, dy = rho - F(r - rr), np.abs(q[:, 0]) - F(h - rr)
        out = np.sqrt(np.maximum(dx, 0) ** 2 + np.maximum(dy, 0) ** 2) + np.minimum(np.maximum(dx, dy), 0)
        return out - F(rr)
    ext = max(r, ry or r, h)
    return Part(fn, *_box([c], ext))


def plate(a, b, c, t):
    """A triangle a, b, c thickened to 2t, with rounded edges (after Inigo Quilez's triangle distance)."""
    a, b, c = vec(a), vec(b), vec(c)
    ba, cb, ac = b - a, c - b, a - c
    nor = np.cross(ba, ac)
    cba, ccb, cac = np.cross(ba, nor), np.cross(cb, nor), np.cross(ac, nor)
    nn = F(nor @ nor)

    def seg(e, pe):
        t_ = np.clip(pe @ e / F(e @ e), 0, 1)
        d = e[None, :] * t_[:, None] - pe
        return np.einsum("ij,ij->i", d, d)

    def fn(p):
        pa, pb, pc = p - a, p - b, p - c
        inside = (np.sign(pa @ cba) + np.sign(pb @ ccb) + np.sign(pc @ cac)) >= 2
        edge = np.minimum(np.minimum(seg(ba, pa), seg(cb, pb)), seg(ac, pc))
        face = (pa @ nor) ** 2 / nn
        return np.sqrt(np.where(inside, face, edge)) - F(t)
    return Part(fn, *_box([a, b, c], t))


class Body:
    """A field built up part by part over the points ``p``: each part is only evaluated inside its box (grown by
    the blend and ``margin``), so far from every part the value stays at a large positive constant."""

    def __init__(self, p, margin):
        self.p = p
        self.d = np.full(len(p), F(1.0), dtype=F)
        self.margin = margin

    def _near(self, part, k):
        e = F(self.margin + k)
        p = self.p
        m = (p[:, 0] > part.lo[0] - e) & (p[:, 0] < part.hi[0] + e)
        m &= (p[:, 1] > part.lo[1] - e) & (p[:, 1] < part.hi[1] + e)
        m &= (p[:, 2] > part.lo[2] - e) & (p[:, 2] < part.hi[2] + e)
        return np.flatnonzero(m)

    def add(self, part, k=0.0):
        idx = self._near(part, k)
        if len(idx):
            self.d[idx] = sdf.smin(self.d[idx], part.fn(self.p[idx]), k)
        return self

    def cut(self, part, k=0.0):
        idx = self._near(part, k)
        if len(idx):
            self.d[idx] = sdf.carve(self.d[idx], part.fn(self.p[idx]), k)
        return self

    def apply(self, lo, hi, fn):
        """d = fn(points, d) inside the box [lo, hi] (a shape that is not a plain union or cut)."""
        idx = self._near(Part(None, lo, hi), 0.0)
        if len(idx):
            self.d[idx] = fn(self.p[idx], self.d[idx])
        return self


def arc(points, n):
    """``n`` points evenly spaced along a polyline (its arc length)."""
    pts = np.asarray(points, dtype=float)
    seg = np.linalg.norm(np.diff(pts, axis=0), axis=1)
    s = np.concatenate([[0.0], np.cumsum(seg)])
    t = np.linspace(0, s[-1], n)
    return np.stack([np.interp(t, s, pts[:, i]) for i in range(3)], 1)


def catmull(points, n):
    """A smooth curve through ``points`` (Catmull-Rom), sampled at ``n`` points per span."""
    pts = np.asarray(points, dtype=float)
    ext = np.vstack([2 * pts[0] - pts[1], pts, 2 * pts[-1] - pts[-2]])
    out = []
    for i in range(1, len(ext) - 2):
        p0, p1, p2, p3 = ext[i - 1:i + 3]
        for t in np.linspace(0, 1, n, endpoint=False):
            t2, t3 = t * t, t * t * t
            out.append(0.5 * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (3 * p1 - p0 - 3 * p2 + p3) * t3))
    out.append(pts[-1])
    return np.asarray(out)


def tidy(obj, rounds=10):
    """Mends the nicks surface nets and its reduction leave where a field pinches: where two sheets of surface meet at
    an edge or a vertex, or a face is folded back against its neighbours, the faces round the spot are cut out and the
    hole each sheet is left with is closed again on its own."""
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    for _ in range(rounds):
        bm.normal_update()
        spots = {v for v in bm.verts if not v.is_manifold or v.is_boundary}
        for f in bm.faces:
            nb = [g.normal for e in f.edges for g in e.link_faces if g is not f]
            if nb and f.normal.dot(sum(nb, f.normal * 0)) < 0:
                spots.update(f.verts)
        if not spots:
            break
        bmesh.ops.delete(bm, geom=list({f for v in spots for f in v.link_faces}), context="FACES")
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
        bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=0)
        bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 3])
    bm.to_mesh(obj.data)
    bm.free()
    return obj


def rot_x(angle):
    c, s = math.cos(angle), math.sin(angle)
    return np.asarray(((1, 0, 0), (0, c, -s), (0, s, c)))

"""Plate tubes for the possessed armour (plate.py, platelimbs.py) built as regular grids instead of meshed fields, so
no reduction can smear a thin shell's two close faces together: rings round an axis, each ring's radius either
where a ray out from the axis leaves a distance field or given directly as a profile (a greave's calf swell, a
shin ridge), the inner face a plate's thickness within the outer at every ring, and a rolled lip at each end; an
``arc`` keeps only that many degrees either side of ``front`` (tassets, pauldron lames, knee lames), its ends
rounded off (``ends``: the width left at the very end, as a fraction).
"""
import numpy as np

from . import matter
from .sdf import F


class Lathe:
    """``outer`` (a field) or ``radius(u, e)`` (u: (R, 1) from 0 at ``a`` to 1 at ``b``, e: (1, A, 3) unit directions
    out from the axis; metres) gives the outer face from ``a`` to ``b``; ``bone`` carries it."""

    def __init__(self, name, bone, outer, a, b, t, s, front=(0, 1, 0), arc=None, rings=12, segs=36, radius=None, ends=0.35):
        self.name, self.bone, self.outer, self.t, self.s, self.radius, self.ends = name, bone, outer, t, s, radius, ends
        self.a, self.b = np.asarray(tuple(a), dtype=float), np.asarray(tuple(b), dtype=float)
        self.front, self.arc, self.rings, self.segs = np.asarray(tuple(front), dtype=float), arc, rings, segs

    def frame(self):
        """The axis direction, the front and the side (unit vectors)."""
        d = (self.b - self.a) / np.linalg.norm(self.b - self.a)
        f = self.front - d * (self.front @ d)
        f /= np.linalg.norm(f)
        return d, f, np.cross(d, f)

    def _radii(self, C, e, U):
        if self.radius is not None:
            return np.broadcast_to(self.radius(U, e[None]), U.shape).astype(float)
        lo, hi = np.zeros(U.shape), np.full(U.shape, 0.3 * self.s)
        for _ in range(26):                     # each ring's radius: bisection out from the axis
            mid = (lo + hi) / 2
            inside = (self.outer((C + e[None] * mid[..., None]).reshape(-1, 3).astype(F)) < 0).reshape(mid.shape)
            lo, hi = np.where(inside, mid, lo), np.where(inside, hi, mid)
        return (lo + hi) / 2

    def make(self, key, s, steel):
        import bmesh
        from .garments import mesh_from
        a, b, t = self.a, self.b, self.t
        d, f, side = self.frame()
        closed = self.arc is None
        th = np.linspace(0, 2 * np.pi, self.segs, endpoint=False) if closed else np.radians(np.linspace(-self.arc, self.arc, self.segs // 2 + 1))
        e = np.cos(th)[:, None] * f + np.sin(th)[:, None] * side
        us = np.linspace(0.0, 1.0, self.rings)
        x = np.zeros(len(th)) if closed else th / th[-1]
        w = self.ends + (1 - self.ends) * np.sqrt(np.clip(1 - x ** 4, 0, 1))     # an arc's ends rounded off
        U = 0.5 + (us[:, None] - 0.5) * w[None]
        C = a + (b - a) * U[..., None]
        R = self._radii(C, e, U)
        rho = 0.8 * t
        rows, kind = [], []
        for i in range(len(us)):
            rows.append(C[i] + e * R[i][:, None])
            kind.append(0.45 if i in (0, len(us) - 1) else 0.0)

        def lip(i, sign, phis):
            centre = C[i] + e * (R[i] - t / 2)[:, None]
            for phi in np.radians(phis):
                rows.append(centre + e * rho * np.cos(phi) + d * sign * rho * abs(np.sin(phi)))
                kind.append(1.0)
        lip(len(us) - 1, 1, (0, 60, 120, 180))
        for i in range(len(us) - 1, -1, -1):    # the inner face under every ring (no chord cuts through the outer)
            rows.append(C[i] + e * (R[i] - t)[:, None])
            kind.append(-1.0)
        lip(0, -1, (180, 240, 300))
        bm = bmesh.new()
        grid = [[bm.verts.new(tuple(p)) for p in row] for row in rows]
        n, m = len(grid), len(th)
        for r in range(n):
            g0, g1 = grid[r], grid[(r + 1) % n]
            for k in range(m if closed else m - 1):
                bm.faces.new((g0[k], g0[(k + 1) % m], g1[(k + 1) % m], g1[k]))
        if not closed:                          # the two side edges: flat caps
            for k in (0, m - 1):
                bmesh.ops.triangulate(bm, faces=[bm.faces.new([grid[r][k] for r in range(n)])])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        obj = mesh_from(bm, self.name)
        co = np.concatenate(rows)
        k = np.repeat(np.asarray(kind), m)[:, None]
        streak = matter.noise(co * np.asarray((1.0, 1.0, 0.08)), 70.0, 5)[:, None]
        grime = matter.fbm(co, 8.0, 3, 9)[:, None]
        rgb = steel * (0.92 + 0.12 * streak) * (0.9 + 0.14 * grime) * (1 - 0.32 * np.clip(k, 0, 1))
        matter.paint(obj, np.where(k < 0, steel * 0.5, rgb))
        return obj


def profile(r0, r1, s, ridge=0.0, toward=(0, 1, 0), swell=None, flare=None):
    """A radius profile for Lathe: ``r0`` -> ``r1`` along it (times ``s``), a ridge of ``ridge`` facing ``toward``,
    ``swell`` (height, centre u, width u, toward) a bulge to one side, ``flare`` (height, from u) a lip opening out."""
    tw = np.asarray(tuple(toward), dtype=float)

    def radius(u, e):
        r = r0 + (r1 - r0) * u
        if ridge:
            r = r + ridge * np.clip(e @ tw, 0, 1) ** 10
        if swell is not None:
            h, c, w, way = swell
            r = r + h * np.exp(-((u - c) / w) ** 2) * np.clip(e @ np.asarray(tuple(way), dtype=float), 0, 1) ** 1.4
        if flare is not None:
            r = r + flare[0] * matter.ss(flare[1], 1.0, u)
        return r * s
    return radius

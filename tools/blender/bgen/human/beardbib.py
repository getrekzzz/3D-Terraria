"""The hanging part of a beard as one mass (head-local units). Below the jaw a profile is swept round the head's
axis: from the face's layer at the jaw's edge it falls, jutting a little off the chin, to its tips and comes back up
to the throat, so from the side the beard is solid from the chin and the throat down to its point. It rests on
whatever lies behind it (the neck, a collar, the chest) and draws in as it falls (the style's spread). Its bottom
edge breaks into a few big tapered locks, or gathers into forks, with soft grooves running up between them and finer
ones between the strands.
"""
import math

import numpy as np

from .beard import coverages, jaw_edge, masses

F = np.float32
GAP = 0.0025          # how far it stays off the neck, a collar and the chest
THROAT = 0.016        # how far below the jaw's edge the throat is measured (clear of the chin)
SINK = 0.008          # how far it sinks into the face's layer toward its sides
TIP = 0.006           # the least height its locks round off over (toward its sides, where they get short: no steps)


def _ss(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def _smax(a, b, k):
    h = np.maximum(k - np.abs(a - b), 0.0) / k
    return np.maximum(a, b) + h * h * k * 0.25


def _ramp(x, k):
    """max(x, 0) with its corner rounded over k (and still 0 for x <= 0)."""
    x = np.maximum(x, 0.0)
    return np.where(x < k, x * x / (2 * k), x - k / 2)


class Bib:
    def __init__(self, beard):
        self.b = beard
        st = self.st = beard.st
        self.W, self.L = st.width, st.length
        self.A = np.linspace(-1.75, 1.75, 141)
        self.Z = np.arange(-0.03, jaw_edge(0.0) - 1.25 * st.length - 0.04, -0.0025)
        self.R = self._radii()
        self.zj, q = self._jaw()
        self.Fj = np.hypot(q[:, 0], q[:, 1]) + masses(q, st)                 # the face's layer where it falls from
        self.zn, self.Bn = self._neck()
        self._locks()
        beard.bib = self

    def _jaw(self):
        """Per azimuth, where the hanging hair falls from: the jaw line's widest point (its lowest, where the face
        stands upright down to the jaw's edge), and that point on the skin."""
        A = self.A
        dz = np.linspace(-0.004, 0.012, 17)
        z = jaw_edge(A)[None] + dz[:, None]
        s = self.b.surface(np.tile(A, len(dz)), z.ravel()).reshape(len(dz), len(A), 3)
        r = np.hypot(s[..., 0], s[..., 1])
        first = np.argmax(r >= r.max(0)[None] - 0.0015, axis=0)
        zj = z[first, np.arange(len(A))]
        zj = np.convolve(np.pad(zj, 3, mode="edge"), np.ones(7) / 7, mode="valid")
        return zj, self.b.surface(A, zj)

    def _neck(self):
        """Per azimuth, how far down the throat the beard grows (where it has thinned to a third), and how far out the throat
        (or a collar) is there."""
        A = self.A
        dz = np.arange(0.004, 0.09, 0.002)
        z = jaw_edge(A)[None] - dz[:, None]
        q = self.b.surface(np.tile(A, len(dz)), z.ravel())
        bare = coverages(q, self.st, mouth=False).reshape(len(dz), len(A)) < 0.3
        first = np.where(bare.any(0), np.argmax(bare, axis=0), len(dz) - 1)
        zn = np.convolve(np.pad(z[first, np.arange(len(A))], 3, mode="edge"), np.ones(7) / 7, mode="valid")
        zn = np.minimum(zn, self.zj - 0.006)
        return zn, self.radius(A, zn) + GAP

    # ------------------------------------------------------------------ what lies behind it
    def _radii(self):
        """Table (heights x azimuths) of how far out from the head's axis the skin, the body or the clothes reach."""
        a, z = np.meshgrid(self.A, self.Z)
        a, z = a.ravel(), z.ravel()
        s = self.b.surface(a, z)
        r = np.hypot(s[:, 0], s[:, 1])
        tree = self.b.under
        if tree is not None:
            from mathutils import Vector
            h, c = self.b.head.h, self.b.head.c
            for i in range(len(a)):
                ray = Vector((-math.sin(a[i]), -math.cos(a[i]), 0.0))
                start = c + Vector((math.sin(a[i]) * 0.4, math.cos(a[i]) * 0.4, z[i])) * h
                hit = tree.ray_cast(start, ray, 0.4 * h)[0]
                if hit is not None:
                    q = (hit - c) / h
                    r[i] = max(r[i], math.hypot(q.x, q.y))
        return r.reshape(len(self.Z), len(self.A)).astype(F)

    def radius(self, a, z):
        """The table read back (bilinear)."""
        A, Z = self.A, self.Z
        fa = np.clip((np.asarray(a) - A[0]) / (A[1] - A[0]), 0, len(A) - 1.001)
        fz = np.clip((np.asarray(z) - Z[0]) / (Z[1] - Z[0]), 0, len(Z) - 1.001)
        ia, iz = fa.astype(int), fz.astype(int)
        ta, tz = fa - ia, fz - iz
        R = self.R
        top = R[iz, ia] * (1 - ta) + R[iz, ia + 1] * ta
        bot = R[iz + 1, ia] * (1 - ta) + R[iz + 1, ia + 1] * ta
        return top * (1 - tz) + bot * tz

    # ------------------------------------------------------------------ the locks its bottom edge breaks into
    def _locks(self):
        """The locks the bottom edge breaks into: where they lie across (-1 .. 1), how wide and how long each is; and
        the outline they make smoothed over (the mass behind them)."""
        st, rng = self.st, self.b.rng
        n = max(3, int(round(self.W * 4)) | 1)
        spacing = 1.6 / (n - 1)
        t = np.linspace(-0.8, 0.8, n) + rng.uniform(-0.1, 0.1, n) * spacing
        expo = (2.2 - 1.6 * st.spread) / (1 + st.forks)
        length = np.cos(np.clip(t, -1, 1) * math.pi / 2) ** expo
        if st.forks == 2:           # two long forks, the locks round them shorter
            length = 0.55 + 0.45 * np.exp(-((np.abs(t) - 0.32) / 0.2) ** 2)
        elif st.forks == 1:         # one rounded point, the locks beside it a little shorter
            length = np.cos(np.clip(t, -1, 1) * math.pi / 2) ** 0.8
        if st.forks == 2:
            length = length * np.cos(np.clip(t, -1, 1) * math.pi / 2) ** 0.5
        self.lt = t
        self.ll = length * rng.uniform(0.8, 1.0, n)
        self.ll[np.argmax(self.ll)] = 1.0
        self.lw = spacing * (0.75 + 0.55 * self.ll) * rng.uniform(0.9, 1.1, n)       # (the long ones broad)
        self.et = np.linspace(-1.3, 1.3, 261)
        out = self._outline(self.et)
        g = np.exp(-0.5 * ((self.et - self.et[130]) / (0.6 * spacing)) ** 2)
        self.ev = np.maximum(np.convolve(np.pad(out, 130, mode="edge"), g / g.sum(), mode="valid"), out) * 1.05
        # Grooves: the deep ones between the locks the whole way down, shallower ones parting a lock for a while.
        L = max(self.L, 0.03)
        minor = t + rng.choice((-1, 1), n) * rng.uniform(0.15, 0.3, n) * spacing
        self.gt = np.r_[(t[1:] + t[:-1]) / 2, minor]
        self.gw = np.r_[np.full(n - 1, 0.45), np.full(n, 0.28)] * spacing * rng.uniform(0.85, 1.15, 2 * n - 1)
        self.gd = np.r_[rng.uniform(0.8, 1.0, n - 1), rng.uniform(0.35, 0.6, n)]
        self.g0 = np.r_[np.full(n - 1, -0.03), rng.uniform(-0.01, 0.3, n) * L]
        self.g1 = np.r_[np.full(n - 1, 9.0), rng.uniform(0.5, 1.0, n) * L]
        self.gs = rng.normal(0.0, 0.12 + 1.2 * st.mess, 2 * n - 1) * spacing
        self.gp = rng.uniform(0, 6.3, 2 * n - 1)
        if st.forks == 2:           # parted down the middle into two forks
            self.gt, self.gw = np.r_[self.gt, 0.0], np.r_[self.gw, 0.5 * spacing]
            self.gd, self.g0, self.g1 = np.r_[self.gd, 1.3], np.r_[self.g0, 0.25 * L], np.r_[self.g1, 9.0]
            self.gs, self.gp = np.r_[self.gs, 0.0], np.r_[self.gp, 0.0]

    def _outline(self, t):
        """The locks' outline: each ends in a soft, rounded point."""
        x = (t[:, None] - self.lt[None]) / self.lw[None]
        point = np.maximum(1 - x * x, 0.0) ** 0.5 * self.ll[None]
        out = point[:, 0]
        for j in range(1, point.shape[1]):
            out = _smax(out, point[:, j], 0.08)
        return self.L * out * _ss(1.0, 0.6, np.abs(t))

    def lateral(self, a, drop):
        """Where a point lies across the hanging part: -1 .. 1 between its sides, drawing in as it falls."""
        narrow = 1 - (1 - self.st.spread) * 0.7 * _ss(0.0, max(self.L, 0.03), drop)
        return a / (self.W * narrow), narrow

    def reach(self, t, drop):
        """How far below where it falls from the hanging part reaches at ``t``: its locks' outline and that outline
        smoothed over (the mass behind them); and how near to a lock's crest each point lies at its ``drop`` (1 on a
        crest .. about 0 deep in the groove between two locks)."""
        drop = drop[:, None]
        x = (t[:, None] - self.gt[None] - self.gs[None] * np.sin(drop * 40 + self.gp[None]) * _ss(0.0, 0.05, drop)) / self.gw[None]
        grooves = np.maximum(1 - x * x, 0.0) ** 2 * self.gd[None]
        grooves = grooves * _ss(self.g0[None], self.g0[None] + 0.02, drop) * _ss(self.g1[None], self.g1[None] - 0.02, drop)
        crest = 1 - np.minimum(grooves.sum(1), 1.3)
        return self._outline(t), np.interp(t, self.et, self.ev), crest

    # ------------------------------------------------------------------ the field
    def profile(self, a, z):
        """The hanging part's front and back (radii from the head's axis) at azimuths ``a`` and heights ``z``, with
        each point's drop below where the hair falls from, the reach there and where it lies across (``lateral``)."""
        st = self.st
        zj = np.interp(a, self.A, self.zj)
        drop = zj - z
        t, narrow = self.lateral(a, np.maximum(drop, 0.0))
        reach, env, crest = self.reach(t, drop)
        u = np.maximum(drop, 0.0) / np.maximum(reach, TIP)                   # down its lock
        ue = np.minimum(np.maximum(drop, 0.0) / np.maximum(env, TIP), 1.0)    # down the mass behind the locks
        src = np.clip(a / narrow, self.A[0], self.A[-1])     # where round the jaw the hair here grew: drawing in, the
        Fj = np.interp(src, self.A, self.Fj)                 # mass narrows and rounds off, no flat sides
        Bn = np.interp(src, self.A, self.Bn)
        zn = np.interp(src, self.A, self.zn)        # (and how far down the throat it grew there)
        jut = 0.002 + 0.25 * st.mass
        front = Fj + jut * _ss(0.0, 0.03, drop) - (jut + 0.45 * (Fj - Bn) * st.depth) * _ss(0.4, 1.0, ue)
        # The underside: on the throat down to where the beard stops growing there, then rising to the tips.
        # (Where the beard grows on down the throat the underside lies on it whatever the style's depth, so the hanging
        # part and the throat's layer are one.)
        rest = self.radius(a, np.minimum(z, jaw_edge(a) - THROAT)) + GAP
        start = rest + (Fj - rest) * (1 - st.depth) * _ss(0.03, 0.008, zj - zn)
        s = np.clip((zn - z) / np.maximum(zn - (zj - env), 1e-4), 0.0, 1.0)
        full = _ss(0.08, 0.2, self.L)                # (a short beard full and round underneath, a long one tapering)
        fill = (1 - full) * np.sqrt(np.maximum(1 - s * s, 0.0)) + full * (1 - s ** 1.3)
        wedge = min(1.0, 1.2 * (1 - st.depth))          # (a beard that leaves the throat bare slopes straight down)
        under = front - (front - start) * (fill + ((1 - s) ** 1.2 - fill) * wedge)
        back = front - (front - under) * np.sqrt(np.maximum(1 - np.minimum(u, 1.0) ** 3, 0.0))   # each lock rounded off
        uu = np.minimum(u, 1.0)                      # (and never a thin sheet: round in profile down to its tip)
        back = np.minimum(back, front - (0.004 + 0.02 * self.L) * np.sqrt(np.maximum(1 - uu * uu, 0.0)))
        # Above where it falls from it runs on up under the face's layer, which takes it over, and into the skin.
        q = np.stack([np.sin(a), np.cos(a), np.zeros(len(a))], 1) * self.radius(a, z)[:, None]
        q[:, 2] = z
        face = self.radius(a, z) + (masses(q, st) * self.b.coverage(q) + 0.003) * _ss(-0.02, 0.0, drop) - 0.003
        front = front + (np.minimum(face, Fj + jut * 0.2) - front) * _ss(0.0, -0.006, drop)
        side = np.maximum(np.abs(t) - 0.45, 0.0) / 0.55     # the whole rounded off toward its sides, where it sinks
        keep = np.sqrt(np.maximum(1 - side * side, 0.0))     # into the layer (its locks' tips with it)
        front, back = under + (front - under) * keep - 0.006 * side * side, under + (back - under) * keep
        shift = _ramp(rest - back, 0.006)
        front, back = front + shift, back + shift
        groove = np.minimum(0.0015 + 0.2 * st.mass, 0.35 * (front - back + 0.004))     # (never through it)
        front = front + groove * _ss(-0.02, 0.015, drop) * (crest - 1.0)
        return front, back, drop, reach, t, narrow

    def _raw(self, X):
        r = np.hypot(X[:, 0], X[:, 1])
        a = np.arctan2(X[:, 0], X[:, 1])
        front, back, drop, reach, t, narrow = self.profile(a, X[:, 2])
        d = np.maximum(r - front, back - r)
        d = np.maximum(d, drop - reach)                          # below its tips
        d = np.maximum(d, -drop - 0.02)                          # (up inside the layer)
        d = np.maximum(d, (np.abs(t) - 1.0) * r * self.W * narrow)      # past its sides
        return d + SINK * _ss(0.5, 1.0, np.abs(t))      # (toward its sides it sinks into the layer, locks and all)

    def field(self, X):
        """Distance to the hanging part (a first-order estimate: the raw field over its gradient's length)."""
        X = np.ascontiguousarray(X, dtype=F)
        d = self._raw(X)
        e = F(0.0006)
        g2 = np.zeros(len(X))
        for axis in range(3):
            o = np.zeros(3, dtype=F)
            o[axis] = e
            g2 += ((self._raw(X + o) - self._raw(X - o)) / (2 * e)) ** 2
        return (d / np.maximum(np.sqrt(g2), 1.0)).astype(F)

    def bounds(self):
        """A box round the hanging part, clear of it by more than it blends over (so nothing changes at its walls)."""
        a = np.linspace(-self.W, self.W, 41)
        top = float(self.zj.max() + 0.032)
        lo_z = float(self.zj.min() - self.L * 1.1 - 0.01)
        A, Zs = (v.ravel() for v in np.meshgrid(a, np.linspace(lo_z, top, 40)))
        front, back = self.profile(A, Zs)[:2]
        r, A = np.r_[front, back], np.r_[A, A]
        x, y, pad = r * np.sin(A), r * np.cos(A), 0.012
        return (float(x.min() - pad), float(y.min() - pad), lo_z - pad), (float(x.max() + pad), float(y.max() + pad), top)

    def across(self, X):
        """Where each point lies across the flow of the hanging hair (metres round the face), and its lock's crest."""
        a = np.arctan2(X[:, 0], X[:, 1])
        drop = np.maximum(np.interp(a, self.A, self.zj) - X[:, 2], 0.0)
        t, narrow = self.lateral(a, drop)
        return (t * self.W * 0.065).astype(F), np.clip(self.reach(t, drop)[2], 0.0, 1.0)

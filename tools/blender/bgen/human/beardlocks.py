"""The shape of a beard (head-local units): the mass over the face, combed in clumps down the cheeks and drawing in
toward the chin, with fine grooves between its strands; two rows of moustache locks sweeping out from the philtrum
and down past the corners of the mouth; and, for a beard that hangs, big lobes falling from the chin and jaw, with the
hair from the throat filling in behind them, kept off the neck and in front of the body and the clothes (a long one
lies on the chest), each tapering to a soft point or gathering into a few forks.
"""
import math

import numpy as np
from mathutils import Vector

from . import sdf
from .beard import STYLES, coverages, jaw_edge, masses

F = np.float32
CLUMP = 0.019         # spacing of the clumps the mass over the face is combed into (head-local)
STRAND = 0.006        # and of the fine grooves between its strands


def _ss(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def _unit(v):
    return v / np.maximum(np.linalg.norm(v, axis=-1, keepdims=True), 1e-9)


class Beard:
    def __init__(self, head, style, under=None, seed=1):
        self.head = head
        self.st = STYLES[style]
        shape = head.shape
        self.skin = lambda p: shape.field(p, ears=False)      # the face the beard lies on
        self.full = shape.field                               # with the ears, which nothing may cut into
        self.under = under                    # BVH (world space) of the body and the clothes the beard hangs onto
        self.rng = np.random.default_rng(seed)
        self.phase = self.rng.uniform(0, 6.3, 6)

    # ------------------------------------------------------------------ surfaces
    def normal(self, p, field=None):
        return _unit(sdf.gradient(field or self.skin, np.ascontiguousarray(p, dtype=F), F(0.0006)))

    def onto(self, p, height, steps=3, field=None):
        """Points moved onto the surface ``height`` (per point) above the skin."""
        field = field or self.skin
        p = np.array(p, dtype=F)
        for _ in range(steps):
            g = sdf.gradient(field, p, F(0.0006))
            d = field(p) - height
            p -= g * (d / np.maximum(np.einsum("ij,ij->i", g, g), F(1e-8)))[:, None]
        return p

    def surface(self, a, z):
        """Points on the skin at azimuths ``a`` from the chin (+ toward the character's right) and heights ``z``,
        found along rays out from the head's axis."""
        inner = np.stack([np.zeros(len(a)), np.zeros(len(a)), z], 1).astype(F)
        ray = (np.stack([np.sin(a), np.cos(a), np.zeros(len(a))], 1) * 0.2).astype(F)
        lo, hi = np.zeros(len(a), dtype=F), np.ones(len(a), dtype=F)
        for _ in range(24):
            mid = (lo + hi) * F(0.5)
            inside = self.skin(inner + ray * mid[:, None]) < 0
            lo, hi = np.where(inside, mid, lo), np.where(inside, hi, mid)
        return inner + ray * lo[:, None]

    def across(self, X):
        """Where each point lies across the beard's flow (metres round the face, head-local): constant along every
        line the hair is combed in, down the cheeks and drawing in toward the chin, wavier for a messier beard."""
        a = np.arctan2(X[:, 0], X[:, 1])
        wave = self.st.mess * 0.3 * np.sin(X[:, 2] * 95 + a * 3 + self.phase[5])
        return ((a / (1 + 0.35 * _ss(-0.03, -0.12, X[:, 2])) + wave) * 0.065).astype(F)

    def comb(self, X):
        """How the mass over the face is combed at each point: its coverage, the clumps (1 on a clump's crest .. 0
        between two, of uneven widths, drifting a little) and the grooves between its strands (1 in a groove)."""
        s = self.across(X)
        ph = self.phase
        c = s + CLUMP * (0.22 * np.sin(2 * np.pi * s / (2.7 * CLUMP) + ph[0]) + 0.12 * np.sin(X[:, 2] * 70 + ph[1]))
        clump = 0.5 + 0.5 * np.cos(2 * np.pi * c / CLUMP)
        strand = 0.5 - 0.5 * np.cos(2 * np.pi * (s / STRAND + 0.15 * np.sin(2 * np.pi * s / (3.3 * STRAND) + ph[2])))
        cov = coverages(X, self.st)
        cov = cov + 0.15 * (clump - 0.5) * 4 * cov * (1 - cov)      # the clumps reach a little further out
        return _ss(0.05, 0.95, cov), clump, strand

    def base(self, X):
        """Height of the beard's mass over the skin: in clumps along the flow with grooves between its strands,
        deepest between the clumps; at its edges it thins out and sinks under the skin, breaking up into strands."""
        k, clump, strand = self.comb(X)
        m = masses(X, self.st)
        t = m * (0.78 + 0.44 * clump) * k - 0.0012 * (1 - k)
        t -= (np.minimum(0.0008, m * (0.1 + 0.2 * (1 - clump))) + 0.0028 * k * (1 - k)) * strand
        return t.astype(F)

    # ------------------------------------------------------------------ the moustache
    def moustache(self):
        """Two staggered rows of locks under the nose, sweeping out from the philtrum, arching over the upper lip and
        turning down past the corners of the mouth: polylines, widths, thicknesses (None for none)."""
        st = self.st
        if not st.moustache:
            return None
        roots = []
        for row, (z0, dx) in enumerate(((-0.0513, 0.0), (-0.0553, 0.0023))):
            for sx in (1, -1):
                for i in range(3):
                    x = 0.004 + dx + 0.0072 * i
                    roots.append((sx * x, 0.1, z0 - (0.004 + 0.002 * row) * (x / 0.024) ** 2))
        roots = self.onto(np.asarray(roots, dtype=F), np.zeros(len(roots), dtype=F), steps=5)
        n, K = len(roots), 10
        side = np.where(roots[:, 0] >= 0, 1.0, -1.0)
        lengths = (0.015 + 0.016 * st.droop + 0.005 * (1 - np.abs(roots[:, 0]) / 0.024)) * st.moustache ** 0.5
        lengths = lengths * self.rng.uniform(0.9, 1.05, n)
        P = np.zeros((K, n, 3), dtype=F)
        P[0] = self.onto(roots, np.full(n, 0.0004, dtype=F))
        d = None
        for k in range(1, K):
            prev = P[k - 1]
            corner = _ss(0.014, 0.03, np.abs(prev[:, 0]))
            f = np.stack([side * (1.0 - 0.6 * corner * st.droop), 0.15 * (1 - corner),
                          -(0.3 + 1.6 * st.droop * corner)], 1)
            nrm = self.normal(prev)
            f = _unit(f - nrm * np.einsum("ij,ij->i", f, nrm)[:, None])
            d = f if d is None else _unit(d * 0.5 + f * 0.5)
            arch = 0.0014 + 0.0016 * math.sin(math.pi * min(1.0, k / (K - 1) * 1.15))
            P[k] = self.onto(prev + d * (lengths / (K - 1))[:, None], np.full(n, arch, dtype=F))
        widths = np.full(n, st.lock * 0.62 * st.moustache ** 0.5)
        return P, widths, widths * 0.4, np.zeros(n, dtype=bool)

    # ------------------------------------------------------------------ the hanging part
    def hang(self, a, z0, lengths, height, lean):
        """Columns rooted at azimuths ``a`` and heights ``z0``: down over the face to the jaw's edge, rising out of the
        beard's layer to ``height`` over the skin, then hanging, jutting ``lean`` forward off the chin and kept
        ``height`` off the neck, the body and the clothes. Returns the polylines and each point's drop below the jaw."""
        n = len(a)
        K = 12 + int(self.st.length * 70)
        edge = jaw_edge(a) + 0.007            # where it leaves the face, before the chin turns under
        top = np.maximum(z0 - edge, 0.0)
        s = np.linspace(0.0, 1.0, K)[:, None] * lengths[None]
        P = np.zeros((K, n, 3), dtype=F)
        d = np.tile(np.asarray((0.0, 0.0, -1.0), dtype=F), (n, 1))
        for k in range(K):
            face = s[k] <= top * 1.15
            if face.any():
                z = z0[face] - s[k, face] / 1.15
                rise = _ss(0.35, 1.0, 1 - (z - edge[face]) / np.maximum(top[face], 1e-4))
                q = self.surface(a[face], z)
                lift = self.base(q) * (1 - rise) * 0.5 + height[face] * rise
                P[k, face] = self.onto(q, lift.astype(F))
                if k:
                    d[face] = _unit(P[k, face] - P[k - 1, face])
            if k and (~face).any():
                prev, m = P[k - 1, ~face], ~face
                drop = edge[m] - prev[:, 2]
                ahead = 0.04 + lean[m] * (1 - _ss(0.0, 0.05, drop))
                fall = _unit(np.stack([np.zeros(m.sum()), ahead, -np.ones(m.sum())], 1))
                d[m] = _unit(d[m] * 0.45 + fall * 0.55)
                step = (s[k, m] - s[k - 1, m])[:, None]
                P[k, m] = self.clear((prev + d[m] * step).astype(F), height[m])
        return P, np.maximum(edge[None] - P[..., 2], 0.0)

    def drape(self):
        """The hanging part: big lobes round the chin and jaw, longest at the chin, a front layer and between them a
        shorter one just behind it, narrowing as they fall (and gathering into a few forks), and under them the hair
        from the throat; polylines, widths, thicknesses (None for a beard that doesn't hang)."""
        st = self.st
        if st.length <= 0.0:
            return None
        n = int(round(st.width * 2 * 0.065 / 0.027)) + 1 | 1
        a = np.linspace(-st.width, st.width, n)
        spacing = 2 * st.width * 0.065 / (n - 1)
        a = np.r_[a, (a[1:] + a[:-1]) / 2] if n > 3 else a           # (a small beard has no second layer)
        a = a + self.rng.uniform(-0.15, 0.15, len(a)) * st.width / n
        inner = np.arange(len(a)) >= n
        front = _ss(1.3, 0.35, np.abs(a))
        outline = np.cos(np.clip(a / st.width / 1.25, -1, 1) * math.pi / 2) ** ((2.2 - 1.6 * st.spread) / (1 + st.forks))
        hang = st.length * (0.2 + 0.8 * outline) * self.rng.uniform(0.8, 1.05, len(a)) * np.where(inner, 0.65, 1.0)
        widths = np.clip(spacing * 1.6, 0.022, st.lock * 2) * self.rng.uniform(0.92, 1.08, len(a)) * (0.8 + 0.2 * front)
        thick = st.mass * (0.75 + 0.45 * front) * self.rng.uniform(0.92, 1.08, len(a)) * np.where(inner, 0.9, 1.0)
        height = (thick * np.where(inner, 0.3, 0.55) + 0.001).astype(F)
        z0 = jaw_edge(a) + np.where(inner, 0.01, 0.02 + 0.01 * front)
        P, drop = self.hang(a, z0, hang + (z0 - jaw_edge(a)) * 1.15, height, 0.03 + 0.07 * front)
        if st.forks:
            group = np.minimum(((a / st.width + 1) / 2 * st.forks).astype(int), st.forks - 1)
        else:                       # neighbouring lobes gathered by twos or threes, now and then, into one point
            order = np.argsort(a)
            runs = np.repeat(np.arange(len(a)), self.rng.choice((1, 1, 2, 2, 3), len(a)))[:len(a)]
            group = np.empty(len(a), dtype=int)
            group[order] = runs
        self.shape(P, drop, hang, group, height)
        Q, tq = self.fill(P[:, :n], a[:n], thick[:n]) if st.cheek > 0 else (P[:, :0], P[:, :0, 0])
        thick = np.concatenate([np.broadcast_to(thick, P.shape[:2]), tq], 1)
        return (np.concatenate([P, Q], 1), np.r_[widths, np.full(Q.shape[1], 0.024)], thick,
                np.ones(len(a) + Q.shape[1], dtype=bool))

    def fill(self, P, a, thick):
        """The hair under the jaw, filling the wedge between the throat and the front lobes down to their tips, so
        that from the side the beard is solid from the chin and the throat down to its point: per lobe a column
        whose depth reaches from the line throat-tip to the lobe's back. Polylines and thicknesses per ring."""
        K, n = P.shape[:2]
        i = np.arange(1, n - 1)
        b = a[i] * 0.85
        roots = self.onto(self.surface(b, jaw_edge(b) - 0.014), np.zeros(len(i), dtype=F))
        grows = coverages(roots, self.st) > 0.5                 # (only where it grows on the throat)
        i, roots = i[grows], roots[grows]
        tip = P[int(0.85 * (K - 1)), i]
        f = np.linspace(0.0, 1.0, K)[:, None, None]
        back = roots[None] + (tip - roots)[None] * f + np.asarray((0.0, 0.0, 0.004)) * np.sin(np.pi * f)   # (no sack)
        front = np.stack([np.stack([np.interp(back[:, j, 2], P[::-1, c, 2], P[::-1, c, ax]) for ax in (0, 1)], 1)
                          for j, c in enumerate(i)], 1)            # (the lobe's middle at the same height)
        depth = np.clip(front[..., 1] - back[..., 1] - thick[i][None] * 0.4, 0.0, 0.035)
        Q = back.copy()
        half = depth / np.maximum(front[..., 1] - back[..., 1], 1e-4) * 0.5
        Q[..., :2] = back[..., :2] + (front - back[..., :2]) * half[..., None]
        depth = np.maximum(depth, thick[i][None] * 0.6)
        for k in range(1, K):
            Q[k] = self.clear(Q[k].astype(F), (depth[k] * 0.5 + 0.0005).astype(F))
        return Q.astype(F), depth.astype(F)

    def shape(self, P, drop, hang, group, height):
        """The lobes drawn in as they fall and, into forks, gathered toward their middles by their tips; then swayed a
        little, evened out and kept clear."""
        st = self.st
        t = drop / np.maximum(hang, 1e-4)[None]
        P[..., 0] *= 1 - (1 - st.spread) * 0.85 * _ss(0.0, 1.0, t)
        pull = 0.85 * _ss(0.0, 1.0, t) if st.forks else 0.45 * _ss(0.4, 1.0, t)
        for g in np.unique(group):
            i = group == g
            front = P[:, i, 1].max(1) if st.forks else (P[:, i, 1].mean(1) + P[:, i, 1].max(1)) / 2
            mid = np.stack([P[:, i, 0].mean(1), front], 1)[:, None]
            P[:, i, :2] += (mid - P[:, i, :2]) * pull[:, i, None]
        sway = self.rng.normal(0, 0.003 + 0.015 * st.mess, group.max() + 1)[group]
        P[..., 0] += sway[None] * np.sin(np.minimum(t, 1.0) * math.pi) * _ss(0.0, 0.3, t)
        hung = drop > 0
        for _ in range(3):
            Pm = P.copy()
            Pm[1:-1] = P[1:-1] * 0.5 + (P[:-2] + P[2:]) * 0.25
            P[hung] = Pm[hung]
            for k in range(1, len(P)):
                if hung[k].any():
                    P[k, hung[k]] = self.clear(P[k, hung[k]], height[hung[k]])

    def clear(self, q, height):
        """Hanging hair kept ``height`` (per point) off the head and neck, and in front of the body and its clothes:
        whatever it hangs over (a collar, the chest), it passes in front of."""
        inside = self.full(q) < height
        if inside.any():
            q[inside] = self.onto(q[inside], height[inside], steps=3, field=self.full)
        if self.under is not None:
            h, c = self.head.h, np.asarray(self.head.c)
            back = Vector((0.0, -1.0, 0.0))
            for i in range(len(q)):
                w = c + q[i] * h
                hit = self.under.ray_cast(Vector((w[0], w[1] + 0.5 * h, w[2])), back, h)[0]
                if hit is not None and w[1] < hit.y + (height[i] + 0.003) * h:
                    q[i, 1] = (hit.y + (height[i] + 0.003) * h - c[1]) / h
        return q

    def locks(self):
        """Every part of the beard made of locks: (polylines, widths, thicknesses, hanging) for the moustache and for
        the hanging part; the mass over the face is the layer (``base``)."""
        return [part for part in (self.moustache(), self.drape()) if part is not None]

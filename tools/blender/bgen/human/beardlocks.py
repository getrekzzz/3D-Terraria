"""The shape of a beard over the face (head-local units): the mass over the face, combed in clumps down the cheeks
and drawing in toward the chin, with fine grooves between its strands, thinning out to a ragged edge where it grows out
of the skin; and the moustache rising out of it, sweeping from the philtrum over the upper lip and down past the
corners of the mouth into the beard, parted from the cheeks by a soft groove. The part that hangs below the jaw is
beardbib's.
"""
import numpy as np

from .beard import STYLES, coverages, masses

F = np.float32
CLUMP = 0.024         # spacing of the clumps the mass over the face is combed into (head-local)
STRAND = 0.006        # and of the fine grooves between its strands
MOUTH = (-0.0705, 0.0258, 0.0105)     # z centre, half width and half height of the bare lens round the lips


def _ss(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


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
        self.bib = None                       # the hanging part (beardbib), whose locks the jaw's clumps run into
        self._path = self._whisker()

    # ------------------------------------------------------------------ surfaces
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

    def coverage(self, X):
        """How much beard grows at each point (beard.coverages), up to the lips: bare over a lens round them, which
        narrows to a point at each corner of the mouth so the beard and the moustache close in on it."""
        cov = coverages(X, self.st, mouth=False)
        zc, hw, hh = MOUTH
        lens = (np.abs(X[:, 0]) / hw + ((X[:, 2] - zc) / hh) ** 2 - 1.0) * 0.01
        return cov * (1 - (1 - _ss(0.0004, 0.006, lens)) * _ss(0.04, 0.06, X[:, 1]))

    def comb(self, X, edge=False):
        """How the mass over the face is combed at each point: how far it has grown out of the skin (0 at its edges ..
        1), the clumps (1 on a clump's crest .. 0 between two, of uneven widths, drifting a little) and the grooves
        between its strands (1 in a groove), which fade in and out along the flow; with ``edge``, also the coverage
        its ragged edge follows."""
        s = self.across(X)
        ph = self.phase
        z = X[:, 2]
        c = s + CLUMP * (0.2 * np.sin(2 * np.pi * s / (2.7 * CLUMP) + ph[0]) + 0.15 * np.sin(z * 55 + ph[1]))
        clump = 0.5 + 0.5 * np.cos(2 * np.pi * c / CLUMP)
        strand = 0.5 - 0.5 * np.cos(2 * np.pi * (s / STRAND + 0.15 * np.sin(2 * np.pi * s / (3.3 * STRAND) + ph[2])))
        strand = strand * _ss(-0.3, 0.8, np.sin(z * 150 + s * 260 + ph[3]))
        cov = self.coverage(X)
        ragged = 0.15 * (clump - 0.5) + 0.1 * np.sin(z * 230 + s * 400 + ph[2])     # tufts reach further out
        cov = cov + ragged * 4 * cov * (1 - cov)
        if self.bib is not None:      # toward the jaw the clumps gather into the hanging part's locks
            a = np.arctan2(X[:, 0], X[:, 1])
            above = z - np.interp(a, self.bib.A, self.bib.zj)
            near = _ss(0.035, 0.006, above)
            crest = np.clip(self.bib.reach(a / self.bib.W, -above)[2], 0.0, 1.0)
            clump = clump + (crest - clump) * near
        k = _ss(0.0, 1.0, cov) ** 1.4
        return (k, clump, strand, cov) if edge else (k, clump, strand)

    def base(self, X, slope=False):
        """Height of the beard's mass over the skin: in soft clumps along the flow with shallow grooves between its
        strands; toward its edges it thins out gradually and sinks under the skin, so it grows out of the face. The
        moustache rises out of it over the upper lip, and over the lip's edge. With ``slope``, also how steeply the
        moustache rises (so its distance can be estimated)."""
        k, clump, strand, cov = self.comb(X, edge=True)
        m = masses(X, self.st)
        # It thins out to a fine edge, then dives under the skin (crossing it steeply, so its outline is clean).
        dive = _ss(0.5, 0.1, cov)
        t = m * (0.7 + 0.6 * clump) * k + 0.0004 - 0.0048 * dive
        t -= np.minimum(0.0008, m * (0.1 + 0.15 * (1 - clump))) * strand * k
        h, _, _, _, rim = self.whiskers(X)
        t = t - np.minimum(rim, np.maximum(t - 0.0008, 0.0))   # (a soft groove round the moustache parts it from
        #                                                         the cheeks and the chin, never through the layer)
        hk = 0.0015
        g = np.maximum(hk - np.abs(t - h), 0.0) / hk
        t = np.maximum(t, h) + g * g * hk * 0.25
        if not slope:
            return t.astype(F)
        e = F(0.0004)
        grad = [(self.whiskers(X + o)[0] - self.whiskers(X - o)[0]) / (2 * e)
                for o in (np.asarray((e, 0, 0), dtype=F), np.asarray((0, 0, e), dtype=F))]
        return t.astype(F), np.hypot(*grad).astype(F)

    # ------------------------------------------------------------------ the moustache
    def _whisker(self):
        """The moustache's path over one side of the upper lip (|x|, z): from the philtrum along the lip's edge, round
        past the corner of the mouth and down into the beard; its half widths along it and their running length."""
        st = self.st
        path = np.asarray(((0.0, -0.0535), (0.012, -0.0566), (0.0215, -0.0614), (0.0282, -0.0684),
                           (0.0302, -0.0684 - 0.022 * st.droop)), dtype=F)
        path[1:, 1] += self.rng.uniform(-0.0006, 0.0006, 4)
        half = np.asarray((0.0056, 0.006, 0.0056, 0.0046, 0.0018)) * st.moustache ** 0.5
        arc = np.r_[0.0, np.cumsum(np.linalg.norm(np.diff(path, axis=0), axis=1))]
        return path, half, arc / arc[-1]

    def whiskers(self, X):
        """The moustache's height over the skin at each point (below it off the moustache), how far along it each point
        lies (0 at the philtrum .. 1 at its tip), where across it (-1 .. 1, the lip's side negative), the grooves
        between its strands (1 in one) and how deep the groove round it is. It is fullest toward the lip, where it
        ends in a soft edge over it, and thins out up toward the nose."""
        X = np.atleast_2d(X)
        h, s, off = np.full(len(X), -0.004, dtype=F), np.zeros(len(X), dtype=F), np.zeros(len(X), dtype=F)
        if not self.st.moustache:
            return h, s, off, s, s
        P, H, C = self._path
        p = np.stack([np.abs(X[:, 0]), X[:, 2]], 1)
        e = np.full(len(X), np.inf)
        for i in range(len(P) - 1):
            ab = P[i + 1] - P[i]
            u = np.clip((p - P[i]) @ ab / (ab @ ab), 0.0, 1.0)
            r = p - (P[i] + u[:, None] * ab)
            w = H[i] + (H[i + 1] - H[i]) * u
            ei = np.hypot(r[:, 0], r[:, 1]) / w
            side = np.where(ab[0] * r[:, 1] - ab[1] * r[:, 0] >= 0, 1.0, -1.0)
            take = ei < e
            e = np.where(take, ei, e)
            s = np.where(take, C[i] + (C[i + 1] - C[i]) * u, s)
            off = np.where(take, side * ei, off)
        st = self.st
        o0 = -0.3                                           # (its crest, toward the lip)
        ea = np.where(off >= o0, (off - o0) / (1 - o0), (o0 - off) / (1 + o0))
        bump = (0.5 + 0.5 * np.cos(np.pi * np.minimum(ea, 1.0))) ** 0.7
        along = (0.8 + 0.2 * _ss(0.0, 0.2, s)) * (1.0 - 0.5 * _ss(0.6, 1.0, s))
        h = (0.0035 + 0.003 * st.moustache) * bump * along
        h = h * (0.92 + 0.08 * np.cos(2 * np.pi * (off * 1.3 + s * 0.8) + self.phase[4]))     # a few clumps
        strands = 0.5 - 0.5 * np.cos(2 * np.pi * (off * self._path[1].max() / 0.0022 + s * 2.0
                                                   + 0.25 * np.sin(s * 9.0 + self.phase[3])))
        strands = strands * _ss(0.1, 0.4, bump)
        h = h - 0.0006 * strands
        h = h - 0.004 * _ss(0.8, 1.25, e)                   # off it, under the skin (the face's layer is there)
        rim = 0.0016 * np.exp(-((e - 1.1) / 0.22) ** 2) * np.maximum(off > 0, _ss(0.55, 0.75, s))
        mouth = X[:, 1] > 0.05                               # (only over the mouth, not round the back of the head)
        h, rim = np.where(mouth, h, -0.004), np.where(mouth, rim, 0.0)
        return h.astype(F), s.astype(F), np.clip(off, -1, 1).astype(F), strands.astype(F), rim.astype(F)

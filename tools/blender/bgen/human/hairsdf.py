"""Sculpted hair: the groom's locks (locks.py, hairtails.py) become clumps blended into each other and into a thin layer
over the scalp, meshed from their distance field like the head (fieldmesh). The result is one cohesive surface with soft
grooves between the clumps, the way game hair is sculpted, instead of separate ribbons with gaps. A clump is lens-shaped
across, grows out of the layer under it and narrows to a rounded point (or a blunt end), its tip turning round in
section so it never ends as a flat flake; round the outside of a bend the section swings round, so a bent clump stays
full. Tips blend less, so the ends of long hair stay distinct locks (a part may keep them merging, for an even edge).
Short and tied hair is cut off in a clean line along the hairline. Under a hat the hair is pressed flat inside its
crown and cut round it. Every face takes its UVs from the clump it belongs to (hairpaint.py).

Everything is in head-local units (headshape) until the finished mesh is placed on the head.
"""
import hashlib
from dataclasses import dataclass

import numpy as np

from . import fieldmesh, sdf
from .hair import coverages, ear_clears, hairlines

F = np.float32
CELL = 0.008          # spatial hash cell (head-local) for finding the clumps near a point
SOURCES = ("hairsdf.py", "hair.py", "locks.py", "headshape.py", "earshape.py", "creature.py", "underhat.py", "hats.py",
           "capshapes.py", "headwear.py")      # what the field reads (the hat a head wears is carved out of its hair)


@dataclass
class Strands:
    """Traced locks: rings x locks x 3 centrelines with each lock's width and thickness (per lock, or per ring and
    lock), where they start narrowing to their tips (fraction of their length), how thin they are at the root, how
    round they stay at least (thickness over width), optionally the way each lock's flat side faces (rings x locks
    x 3; by default away from the head), how much more softly than the rest its locks merge (``blend``), how blunt
    their ends are (0 a point .. 1 cut square), how much their tips still merge (``merge``, 1 as much as the rest) and
    whether they are cut off along the hairline (``cut``: the scalp's locks, not a tail hanging from them)."""
    P: np.ndarray
    width: np.ndarray
    thick: np.ndarray
    taper: float = 0.4
    root: float = 0.45
    N: np.ndarray = None
    round: float = 0.0
    blend: float = 1.0
    blunt: float = 0.0
    merge: float = 0.25
    cut: bool = False


def _ss(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def _smin(a, b, k):
    """sdf.smin with a blend width per point."""
    k = k * F(1.25)
    h = np.maximum(k - np.abs(a - b), F(0)) / k
    return np.minimum(a, b) - h * h * h * k / F(6.0)


def _unit(v):
    return v / np.maximum(np.linalg.norm(v, axis=-1, keepdims=True), 1e-9)


def _solid(X, s):
    """A blob added to the hair (a gather behind a tie, the core of a bun): ("cone", a, b, ra, rb) or
    ("ellipsoid", centre, radii, rotation)."""
    if s[0] == "cone":
        return sdf.round_cone(X, *s[1:])
    return sdf.ellipsoid(X, *s[1:])


def _tipped(t, merge):
    """How much blending a clump keeps along it: tips blend only ``merge`` as much, so (by default) they stay apart."""
    return (1.0 - (1.0 - merge) * _ss(0.55, 0.97, t)).astype(F)


class Sculpt:
    def __init__(self, groom, parts, solids=(), blend=0.0026, seam=(0.0008, 0.0008, 0.0045), fill=0.0):
        self.g = groom
        self.fill = fill                # how much fuller than the groom's own layer the hair under the clumps is
        self.k = blend
        self.k_hang = blend * 1.4 if groom.st.fall else blend
        self.seam = seam                # groove depth between clumps over the head, below it, and its reach
        self.reach = max(self.k_hang * 1.25 * max(p.blend for p in parts), seam[2])  # how far off a clump counts
        self.solids = list(solids)
        seg = {k: [] for k in ("a", "b", "Ba", "Bb", "Na", "Nb", "wa", "wb", "ha", "hb", "arc_a", "arc_b", "lock")}
        self.length, self.span, self.kscale = [], [], []
        lock = nseg = 0
        for part in parts:
            P = np.asarray(part.P, dtype=F)
            K, n = P.shape[:2]
            T = _unit(np.gradient(P, axis=0))
            N = groom._normal(P.reshape(-1, 3)).reshape(K, n, 3) if part.N is None else np.asarray(part.N, dtype=F)
            B = _unit(np.cross(T, N))
            N = np.cross(B, T)
            t = (np.arange(K) / (K - 1))[:, None]
            u = np.clip((t - part.taper) / (1 - part.taper), 0.0, 1.0)
            tip = part.blunt + (1 - part.blunt) * (1 - u ** 2.2) ** 0.55     # an ogive: full, then a rounded point
            W = np.broadcast_to(np.asarray(part.width, dtype=F), (K, n)) if np.ndim(part.width) == 2 else \
                np.broadcast_to(np.asarray(part.width, dtype=F)[None, :], (K, n))
            H = np.broadcast_to(np.asarray(part.thick, dtype=F), (K, n)) if np.ndim(part.thick) == 2 else \
                np.broadcast_to(np.asarray(part.thick, dtype=F)[None, :], (K, n))
            w = W * 0.5 * (0.78 + 0.22 * _ss(0.0, 0.15, t)) * tip
            hh = H * 0.5 * (part.root + (1 - part.root) * _ss(0.0, 0.2, t)) * (1 - 0.2 * t) * np.sqrt(tip)
            w = np.maximum(w, 0.0016)
            hh = np.maximum(np.minimum(np.maximum(hh, w * part.round), w), 0.0013)     # round in section toward the tip
            for i in range(n):
                arc = np.concatenate([[0.0], np.cumsum(np.linalg.norm(np.diff(P[:, i], axis=0), axis=1))])
                for key, arr in (("a", P[:-1, i]), ("b", P[1:, i]), ("Ba", B[:-1, i]), ("Bb", B[1:, i]), ("Na", N[:-1, i]),
                                 ("Nb", N[1:, i]), ("wa", w[:-1, i]), ("wb", w[1:, i]), ("ha", hh[:-1, i]), ("hb", hh[1:, i]),
                                 ("arc_a", arc[:-1]), ("arc_b", arc[1:]), ("lock", np.full(K - 1, lock))):
                    seg[key].append(arr)
                self.span.append((nseg, nseg + K - 1))
                self.length.append(arc[-1])
                self.kscale.append((part.blend, part.merge, float(part.cut)))
                lock += 1
                nseg += K - 1
        self.s = {k: np.concatenate(v).astype(np.int64 if k == "lock" else F) for k, v in seg.items()}
        self.n_locks = lock
        self.length = np.asarray(self.length)
        self.kscale = np.asarray(self.kscale, dtype=F)
        self.cut = self.kscale[:, 2] > 0.5
        self.hem = None
        if groom.name == "bob":         # a bob is cut in one even line, through the ends of its locks
            tips = np.concatenate([np.asarray(p.P[-1], dtype=float) for p in parts if p.cut])
            self.hem = (np.arctan2(tips[:, 0], tips[:, 1] + 0.012), tips[:, 2])
        self.shade = groom.rng.uniform(0.92, 1.05, lock)
        self.band = groom.rng.random(lock)
        self._hash()

    # ------------------------------------------------------------------ the clumps' distance field
    def _hash(self):
        """Grid cells -> the segments whose (blend-widened) bounds reach into them."""
        s = self.s
        r = np.maximum(s["wa"], s["wb"]) + self.reach * 1.45 + 0.001      # all the field the clump reaches
        lo = np.minimum(s["a"], s["b"]) - r[:, None]
        hi = np.maximum(s["a"], s["b"]) + r[:, None]
        self.origin = lo.min(0) - CELL
        c0 = np.floor((lo - self.origin) / CELL).astype(np.int64)
        c1 = np.floor((hi - self.origin) / CELL).astype(np.int64)
        self.dims = c1.max(0) + 2
        cells, segs = [], []
        for i in range(len(lo)):
            axes = [np.arange(c0[i, j], c1[i, j] + 1) for j in range(3)]
            g = np.stack(np.meshgrid(*axes, indexing="ij"), -1).reshape(-1, 3)
            cells.append(self._lin(g))
            segs.append(np.full(len(g), i))
        cells, segs = np.concatenate(cells), np.concatenate(segs)
        order = np.argsort(cells, kind="stable")
        self.cells, self.segs = cells[order], segs[order]

    def _lin(self, g):
        return (g[:, 0] * self.dims[1] + g[:, 1]) * self.dims[2] + g[:, 2]

    def _pairs(self, X):
        """Every (point, segment) pair close enough to matter."""
        g = np.floor((X - self.origin) / CELL).astype(np.int64)
        ok = np.all((g >= 0) & (g < self.dims), axis=1)
        lin = np.where(ok, self._lin(np.clip(g, 0, self.dims - 1)), -1)
        start = np.searchsorted(self.cells, lin, "left")
        cnt = np.where(ok, np.searchsorted(self.cells, lin, "right") - start, 0)
        pt = np.repeat(np.arange(len(X)), cnt)
        off = np.arange(len(pt)) - np.repeat(np.cumsum(cnt) - cnt, cnt)
        return pt, self.segs[np.repeat(start, cnt) + off]

    def _seg_dist(self, X, sg):
        """Distance from each point to the lens-shaped, tapering clump around its segment, with the segment parameter
        and the point's offset across the clump (-1..1 within its width)."""
        s = self.s
        a = s["a"][sg]
        ba = s["b"][sg] - a
        v = X - a
        u = np.clip(np.einsum("ij,ij->i", v, ba) / np.maximum(np.einsum("ij,ij->i", ba, ba), 1e-12), 0.0, 1.0)
        r = v - ba * u[:, None]
        Bv = _unit(s["Ba"][sg] + (s["Bb"][sg] - s["Ba"][sg]) * u[:, None])
        Nv = _unit(s["Na"][sg] + (s["Nb"][sg] - s["Na"][sg]) * u[:, None])
        w = s["wa"][sg] + (s["wb"][sg] - s["wa"][sg]) * u
        hh = s["ha"][sg] + (s["hb"][sg] - s["ha"][sg]) * u
        rb = np.einsum("ij,ij->i", r, Bv)
        rn = np.einsum("ij,ij->i", r, Nv)
        rt2 = np.maximum(np.einsum("ij,ij->i", r, r) - rb * rb - rn * rn, 0.0)
        # Past a segment's end (round the outside of a bend, beyond a tip) the section is swung round, so the radius
        # along the lock is the section's own radius in that direction: bends stay full, with no pinch at the joints.
        e = (0.2 * hh) ** 2
        inv = ((rb / w) ** 2 + (rn / hh) ** 2 + e / (w * hh)) / (rb * rb + rn * rn + e)
        k0 = np.sqrt((rb / w) ** 2 + (rn / hh) ** 2 + rt2 * inv)
        k1 = np.sqrt((rb / w ** 2) ** 2 + (rn / hh ** 2) ** 2 + rt2 * inv * inv)
        return (k0 * (k0 - 1) / np.maximum(k1, 1e-12)).astype(F), u, rb / w

    def _along(self, sg, u):
        """How far along its lock (0 root .. 1 tip) a point of a segment lies."""
        s = self.s
        arc = s["arc_a"][sg] + (s["arc_b"][sg] - s["arc_a"][sg]) * u
        return (arc / np.maximum(self.length[s["lock"][sg]], 1e-6)).astype(F)

    def clumps(self, X, chunk=100000):
        """The blended clumps: per point, its distances to the nearby clumps (nearest first) folded together with a
        smooth minimum, so clumps that touch merge (less so near their tips); and the groove down the seams where they
        meet (where the two nearest clumps are about as near), to be cut into the hair once it is all blended."""
        out = np.full(len(X), 1.0, dtype=F)
        cut = np.zeros(len(X), dtype=F)
        reach = F(self.reach)
        for c in range(0, len(X), chunk):
            x = X[c:c + chunk]
            pt, sg = self._pairs(x)
            if not len(pt):
                continue
            d, u, _ = self._seg_dist(x[pt], sg)
            near = np.full(len(x), np.inf, dtype=F)
            np.minimum.at(near, pt, d)
            m = d < near[pt] + reach                           # only clumps within the blend's reach matter
            pt, d, sg, u = pt[m], d[m], sg[m], u[m]
            key = pt * self.n_locks + self.s["lock"][sg]
            order = np.lexsort((d, key))
            key, d, pt, sg, u = key[order], d[order], pt[order], sg[order], u[order]
            first = np.r_[0, np.nonzero(np.diff(key))[0] + 1]          # per (point, clump): its nearest segment
            dl, pl, tl = d[first], pt[first], self._along(sg[first], u[first])
            kl = self.kscale[self.s["lock"][sg[first]]]          # (blend, merge, cut) of each clump
            cl = self.cut[self.s["lock"][sg[first]]]
            if cl.any():                                         # the scalp's locks end along the hairline
                dl[cl] = sdf.smax(dl[cl], self._edge(x)[pl[cl]], 0.005 if self.g.tie is not None else 0.003)
            order = np.lexsort((dl, pl))
            dl, pl, tl, kl = dl[order], pl[order], tl[order], kl[order]
            starts = np.r_[0, np.nonzero(np.diff(pl))[0] + 1]
            rank = np.arange(len(pl)) - np.repeat(starts, np.diff(np.r_[starts, len(pl)]))
            D = np.full((len(x), rank.max() + 1), np.inf, dtype=F)
            Tk = np.ones((len(x), rank.max() + 1), dtype=F)
            Ks = np.ones((len(x), rank.max() + 1), dtype=F)
            D[pl, rank] = dl
            Tk[pl, rank] = _tipped(tl, kl[:, 1])
            Ks[pl, rank] = kl[:, 0]
            # How tipped the clumps here are, weighted by nearness, so the blend changes smoothly across grooves.
            d0 = np.where(np.isfinite(D[:, 0]), D[:, 0], 0.0)
            wt = np.where(np.isfinite(D), np.maximum(1 - (D - d0[:, None]) / reach, 0.0) ** 2, 0.0)
            tk = (wt * Tk).sum(1) / np.maximum(wt.sum(1), 1e-6)
            ks = (wt * Ks).sum(1) / np.maximum(wt.sum(1), 1e-6)
            acc = D[:, 0].copy()
            k = self._blend(x) * tk * ks
            for j in range(1, D.shape[1]):
                col = D[:, j]
                m = np.isfinite(col)
                acc[m] = _smin(acc[m], col[m], k[m])
            out[c:c + chunk] = np.minimum(acc, 1.0)
            if D.shape[1] > 1:          # none between the free tips
                gap = np.where(np.isfinite(D[:, 1]), D[:, 1], 1.0) - d0
                seam = np.maximum(1 - (gap / self.seam[2]) ** 2, 0.0) ** 2
                cut[c:c + chunk] = self._groove(x) * np.clip((tk - 0.25) / 0.75, 0.0, 1.0) * seam
        return out, cut

    def _groove(self, X):
        top, hang, _ = self.seam
        return (top + (hang - top) * _ss(-0.04, -0.12, X[:, 2])).astype(F)

    def _blend(self, X):
        """How softly clumps merge: crisp grooves over the head, a little softer in hair hanging below it."""
        return (self.k + (self.k_hang - self.k) * _ss(-0.04, -0.12, X[:, 2])).astype(F)

    def base(self, X):
        """Height above the skin of the layer of hair under the clumps (fuller on top), sinking under the skin at the
        hairline and round the ears, and only below the brim under a hat."""
        g = self.g
        cov = coverages(X, g.part, g.sideburns, 0.009, ears=not g.st.fall)
        if g.tie is not None:        # hair pulled back thins out over a wider band, to no lip at its edge
            cov = _ss(-0.004, 0.024, X[:, 2] - hairlines(X, g.part, g.sideburns)) * ear_clears(X)
        if g.under_hat:
            top = g.top(X)
            cov = cov * _ss(top + 0.015, top - 0.01, X[:, 2])
        k = _ss(0.05, 0.95, cov)
        # Deep under the skin round the ears, so none shows in the ear canal (long hair lies over them instead); not
        # over the top of the ear, where the hair passing above it would show a trough.
        ear = 0.0 if g.st.fall else (1.0 - ear_clears(X)) * _ss(0.026, 0.01, X[:, 2])
        return ((g.height(X, 0.0, None) + self.fill) * k - 0.0012 * (1 - k) - 0.012 * ear).astype(F), k

    def _edge(self, x):
        """Where the scalp's locks are cut off along the hairline (a field, < 0 where they may grow): hair pulled back
        has its edge round the face made by the even layer under the locks; a cut ends in a clean line a little below
        the hairline, its fringe free to fall across the forehead (unless a hat sits there), messy tips a little
        further; a bob's ends are trimmed to one smooth line."""
        g = self.g
        if self.hem is not None:
            a = np.arctan2(x[:, 0], x[:, 1] + 0.012)[:, None] - self.hem[0][None]
            w = np.exp(-((np.angle(np.exp(1j * a))) / 0.35) ** 2)
            return ((w * self.hem[1][None]).sum(1) / np.maximum(w.sum(1), 1e-9) + 0.004 - x[:, 2]).astype(F)
        hl = hairlines(x, g.part, g.sideburns) - x[:, 2]
        if g.tie is not None:        # (well inside the layer's edge, so the locks melt into it)
            return (hl + 0.01).astype(F)
        reach = 0.006 if g.st.mess > 0.2 else 0.002
        fringe = 0.0 if g.under_hat else 0.008 * _ss(0.02, 0.045, x[:, 1])
        return (hl - reach - fringe).astype(F)

    def field(self, X):
        X = np.ascontiguousarray(X, dtype=F)
        d, cut = self.clumps(X)
        for s in self.solids:
            d = sdf.smin(d, _solid(X, s), 0.004)
        head = np.ones(len(X), dtype=F)
        near = (X[:, 2] > -0.13) & (np.abs(X[:, 0]) < 0.13) & (X[:, 1] > -0.15) & (X[:, 1] < 0.14)
        if near.any():
            x = X[near]
            skull = self.g.field(x)
            base, k = self.base(x)
            d[near] = sdf.smin(d[near], skull - base, self.k)
            if self.g.under_hat:         # pressed flat inside the hat, so none of it comes through its crown,
                uh = self.g.under_hat     # and coming out from under its edge no thicker than the hat is there
                edge = uh.edge_at(x)
                lim = F(0.0085) + F(0.04) * _ss(edge + 0.004, edge - 0.008, x[:, 2])
                lim = np.minimum(lim, uh.rim_at(x) + 0.9 * np.abs(edge + 0.002 - x[:, 2]))
                lim = (lim + _ss(edge - 0.015, edge - 0.035, x[:, 2])).astype(F)    # (not the hair hanging below)
                d[near] = sdf.smax(d[near], skull - lim, 0.002)
            thin = (1 - k) * _ss(0.03, 0.015, skull) * _ss(-0.1, -0.06, x[:, 2])
            cut[near] *= 1 - 0.7 * thin           # shallow where the hair thins out at the hairline
            head[near] = skull if self.g.full is self.g.field else self.g.full(x)
        d = sdf.smax(d + cut, -(head + F(0.0004)), 0.0025)        # nothing inside the head: the hair sits on the scalp
        return self.g.under_hat.carve(X, d) if self.g.under_hat else d     # nor through the hat it is worn under

    # ------------------------------------------------------------------ the mesh
    def build(self, name="hair"):
        g = self.g
        s = self.s
        margin = float(np.maximum(s["wa"], s["wb"]).max()) + self.k_hang * 2 + 0.006     # the widest clump, its blend
        lo = np.minimum(np.minimum(s["a"].min(0), s["b"].min(0)), (-0.105, -0.125, -0.09)) - margin
        hi = np.maximum(np.maximum(s["a"].max(0), s["b"].max(0)), (0.105, 0.115, 0.13)) + margin
        h, target = (0.0012, 32000) if g.st.fall else (0.001, 15000)
        solids = repr([tuple(np.round(np.asarray(v, dtype=float).ravel(), 6).tolist()) if not isinstance(v, str) else v
                       for s in self.solids for v in s])
        locks = hashlib.sha1(b"".join(np.ascontiguousarray(v).tobytes() for v in self.s.values())).hexdigest()
        hat = g.under_hat.key if g.under_hat else None
        key = ("hair", g.head.shape.key(), g.part, g.sideburns, hat, g.st, solids, self.k, self.k_hang, self.seam,
               self.fill, locks, tuple(np.round(self.kscale, 4).ravel().tolist()))
        obj = fieldmesh.build(self.field, (tuple(lo), tuple(hi)), h, target, name, key,
                              SOURCES, "hair", min_piece=600,
                              keep=lambda c: g.full(np.ascontiguousarray(c, dtype=F)) > -0.0002, resample=True)
        from .hairpaint import finish
        finish(self, obj)
        return obj

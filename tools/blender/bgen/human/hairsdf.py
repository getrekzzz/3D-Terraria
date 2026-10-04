"""Sculpted hair: the groom's locks (locks.py) become tapered, flattened clumps blended into each other and into a
thin layer over the scalp, meshed from their distance field like the head (fieldmesh). The result is one cohesive
surface with soft grooves between the clumps, the way game hair is sculpted, instead of separate ribbons with gaps.
Every face takes its UVs from the clump it belongs to (U runs along it), so the hair shader's strands and highlights
follow the flow; the colour darkens toward the roots and into the grooves.

Everything is in head-local units (headshape) until the finished mesh is placed on the head.
"""
import numpy as np

from . import fieldmesh, sdf
from .hair import coverages

F = np.float32
CELL = 0.008          # spatial hash cell (head-local) for finding the clumps near a point


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


class Sculpt:
    def __init__(self, groom, parts, blend=0.002):
        self.g = groom
        self.k = blend
        self.k_hang = blend * 2.5 if groom.st.fall else blend
        st = groom.st
        self.taper = 0.65 if st.fall else 0.35          # where a clump starts to narrow to its tip
        seg = {k: [] for k in ("a", "b", "Ba", "Bb", "Na", "Nb", "wa", "wb", "ha", "hb", "arc_a", "arc_b", "lock")}
        self.length, self.shade, self.band, self.span = [], [], [], []
        lock = nseg = 0
        for P, widths, thick in parts:
            K, n = P.shape[:2]
            T = _unit(np.gradient(P, axis=0))
            N = groom._normal(P.reshape(-1, 3)).reshape(K, n, 3)
            B = _unit(np.cross(T, N))
            N = np.cross(B, T)
            t = np.arange(K) / (K - 1)
            prof = (0.8 + 0.2 * np.minimum(1.0, t / 0.15)) * (1 - _ss(self.taper, 1.0, t)) ** 0.6
            for i in range(n):
                w = np.maximum(widths[i] * 0.5 * prof, 0.0008)
                hh = np.maximum(thick[i] * 0.5 * (0.4 + 0.6 * _ss(0.0, 0.25, t)) * (1 - 0.45 * t), 0.0006)   # thin at the root
                arc = np.concatenate([[0.0], np.cumsum(np.linalg.norm(np.diff(P[:, i], axis=0), axis=1))])
                for key, arr in (("a", P[:-1, i]), ("b", P[1:, i]), ("Ba", B[:-1, i]), ("Bb", B[1:, i]), ("Na", N[:-1, i]),
                                 ("Nb", N[1:, i]), ("wa", w[:-1]), ("wb", w[1:]), ("ha", hh[:-1]), ("hb", hh[1:]),
                                 ("arc_a", arc[:-1]), ("arc_b", arc[1:]), ("lock", np.full(K - 1, lock))):
                    seg[key].append(arr)
                self.span.append((nseg, nseg + K - 1))
                self.length.append(arc[-1])
                lock += 1
                nseg += K - 1
        self.s = {k: np.concatenate(v).astype(np.int64 if k == "lock" else F) for k, v in seg.items()}
        self.n_locks = lock
        self.length = np.asarray(self.length)
        self.shade = groom.rng.uniform(0.9, 1.06, lock)
        self.band = groom.rng.random(lock)
        self._hash()

    # ------------------------------------------------------------------ the clumps' distance field
    def _hash(self):
        """Grid cells -> the segments whose (blend-widened) bounds reach into them."""
        s = self.s
        r = np.maximum(s["wa"], s["wb"]) + self.k_hang * 1.5
        lo = np.minimum(s["a"], s["b"]) - r[:, None]
        hi = np.maximum(s["a"], s["b"]) + r[:, None]
        self.origin = lo.min(0) - CELL
        c0 = np.floor((lo - self.origin) / CELL).astype(np.int64)
        c1 = np.floor((hi - self.origin) / CELL).astype(np.int64)
        self.dims = c1.max(0) + 2
        cells, segs = [], []
        for i in range(len(lo)):
            g = np.stack(np.meshgrid(*[np.arange(c0[i, j], c1[i, j] + 1) for j in range(3)], indexing="ij"), -1).reshape(-1, 3)
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
        """Distance from each point to the flattened, tapering clump around its segment, with the segment parameter
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
        k0 = np.sqrt((rb / w) ** 2 + (rn / hh) ** 2 + rt2 / hh ** 2)
        k1 = np.sqrt((rb / w ** 2) ** 2 + (rn / hh ** 2) ** 2 + rt2 / hh ** 4)
        return (k0 * (k0 - 1) / np.maximum(k1, 1e-12)).astype(F), u, rb / w

    def clumps(self, X, chunk=100000):
        """The blended clumps: per point, its distances to the nearby clumps (nearest first) folded together with a
        smooth minimum, so clumps that touch merge with a soft groove between them."""
        out = np.full(len(X), 1.0, dtype=F)
        reach = F(self.k_hang * 1.25)
        for c in range(0, len(X), chunk):
            x = X[c:c + chunk]
            pt, sg = self._pairs(x)
            if not len(pt):
                continue
            d = self._seg_dist(x[pt], sg)[0]
            near = np.full(len(x), np.inf, dtype=F)
            np.minimum.at(near, pt, d)
            m = d < near[pt] + reach                           # only clumps within the blend's reach matter
            pt, d, lk = pt[m], d[m], self.s["lock"][sg[m]]
            key = pt * self.n_locks + lk
            order = np.argsort(key, kind="stable")
            key, d, pt = key[order], d[order], pt[order]
            first = np.r_[0, np.nonzero(np.diff(key))[0] + 1]
            dl, pl = np.minimum.reduceat(d, first), pt[first]          # per (point, clump): its nearest segment
            order = np.lexsort((dl, pl))
            dl, pl = dl[order], pl[order]
            starts = np.r_[0, np.nonzero(np.diff(pl))[0] + 1]
            rank = np.arange(len(pl)) - np.repeat(starts, np.diff(np.r_[starts, len(pl)]))
            D = np.full((len(x), rank.max() + 1), np.inf, dtype=F)
            D[pl, rank] = dl
            acc = D[:, 0].copy()
            k = self._blend(x)
            for j in range(1, D.shape[1]):
                col = D[:, j]
                m = np.isfinite(col)
                acc[m] = _smin(acc[m], col[m], k[m])
            out[c:c + chunk] = np.minimum(acc, 1.0)
        return out

    def _blend(self, X):
        """How softly clumps merge: crisp grooves over the head, hanging hair melting into a curtain below it."""
        return (self.k + (self.k_hang - self.k) * _ss(-0.04, -0.12, X[:, 2])).astype(F)

    def base(self, X):
        """Height above the skin of the layer of hair under the clumps (fuller on top), sinking under the skin at the
        hairline and round the ears, and only below the brim under a hat."""
        g = self.g
        cov = coverages(X, g.part, g.sideburns, 0.009)
        if g.under_hat:
            cov = cov * _ss(0.06, 0.035, X[:, 2])
        k = _ss(0.05, 0.95, cov)
        low = 0.55 if g.st.fall else 1.0        # long hair: the clumps stand proud of a thinner layer, not a cap
        return (g.height(X, 0.0, None) * low * k - 0.0012 * (1 - k)).astype(F)

    def field(self, X):
        X = np.ascontiguousarray(X, dtype=F)
        d = self.clumps(X)
        head = np.ones(len(X), dtype=F)
        near = (X[:, 2] > -0.13) & (np.abs(X[:, 0]) < 0.13) & (X[:, 1] > -0.15) & (X[:, 1] < 0.14)
        if near.any():
            x = X[near]
            skull = self.g.field(x)
            d[near] = sdf.smin(d[near], skull - self.base(x), self.k)
            head[near] = skull if self.g.full is self.g.field else self.g.full(x)
        return np.maximum(d, -(head + F(0.0004)))       # nothing inside the head: the hair sits on the scalp

    # ------------------------------------------------------------------ the mesh
    def build(self, name="hair"):
        g = self.g
        pts = self.s["a"]
        lo = np.minimum(pts.min(0), (-0.105, -0.125, -0.09)) - 0.02
        hi = np.maximum(np.maximum(pts.max(0), self.s["b"].max(0)), (0.105, 0.115, 0.13)) + 0.02
        h = 0.0013 if g.st.fall else 0.0011
        target = 12000 if g.st.fall else 8000
        key = ("hair", g.head.shape.key(), g.part, g.sideburns, g.under_hat, g.st,
               __import__("hashlib").sha1(b"".join(np.ascontiguousarray(v).tobytes() for v in self.s.values())).hexdigest())
        obj = fieldmesh.build(self.field, (tuple(lo), tuple(hi)), h, target, name, key,
                              ("hairsdf.py", "hair.py", "headshape.py", "earshape.py", "creature.py"), "hair", min_piece=40,
                              keep=lambda c: g.full(np.ascontiguousarray(c, dtype=F)) > -0.0002, resample=True)
        self._finish(obj)
        return obj

    def _finish(self, obj):
        """UVs and colours from each face's clump, then the mesh moved from head-local units onto the head."""
        import bmesh
        me = obj.data
        bm = bmesh.new()
        bm.from_mesh(me)
        bm.faces.ensure_lookup_table()
        corners = np.asarray([[v.co[:] for v in f.verts] for f in bm.faces], dtype=F)       # (faces, 3, 3)
        lock = self._nearest_lock(corners.mean(1))
        uvs, shade = self._corner_uvs(corners, lock)
        uv = bm.loops.layers.uv.new("UVMap")
        col = bm.loops.layers.color.new("Color")
        base = self.g.color
        for fi, f in enumerate(bm.faces):
            for j, loop in enumerate(f.loops):
                loop[uv].uv = uvs[fi, j]
                loop[col] = (*(c * shade[fi, j] for c in base), 1.0)
        head = self.g.head
        for v in bm.verts:
            v.co = head.at(v.co)
        bm.to_mesh(me)
        bm.free()
        for poly in me.polygons:
            poly.use_smooth = True

    def _nearest_lock(self, X):
        """The clump each point belongs to (the nearest one; by segment midpoints where no clump reaches)."""
        pt, sg = self._pairs(X)
        d = self._seg_dist(X[pt], sg)[0]
        lock = np.full(len(X), -1, dtype=np.int64)
        order = np.argsort(d)[::-1]                    # nearest last, so it wins the scatter
        lock[pt[order]] = self.s["lock"][sg[order]]
        lost = np.nonzero(lock < 0)[0]
        mid = (self.s["a"] + self.s["b"]) * 0.5
        for c in range(0, len(lost), 2000):
            i = lost[c:c + 2000]
            far = np.einsum("ijk,ijk->ij", X[i, None] - mid[None], X[i, None] - mid[None])
            lock[i] = self.s["lock"][np.argmin(far, axis=1)]
        return lock

    def _corner_uvs(self, corners, lock):
        F3 = len(corners)
        X = corners.reshape(-1, 3)
        L = np.repeat(lock, 3)
        uvs = np.zeros((F3 * 3, 2), dtype=F)
        shade = np.ones(F3 * 3, dtype=F)
        span = np.asarray(self.span)
        S = int((span[:, 1] - span[:, 0]).max())
        for j in range(S):                             # each corner against every segment of its clump
            sg = np.minimum(span[L, 0] + j, span[L, 1] - 1)
            d, u, across = self._seg_dist(X, sg)
            if j == 0:
                best = d.copy()
                pick_u, pick_a, pick_s = u, across, sg
            else:
                m = d < best
                best[m] = d[m]
                pick_u, pick_a, pick_s = np.where(m, u, pick_u), np.where(m, across, pick_a), np.where(m, sg, pick_s)
        s = self.s
        arc = s["arc_a"][pick_s] + (s["arc_b"][pick_s] - s["arc_a"][pick_s]) * pick_u
        t = arc / np.maximum(self.length[L], 1e-6)
        uvs[:, 0] = arc * 10.0
        uvs[:, 1] = self.band[L] + np.clip(pick_a, -1.5, 1.5) * 0.06
        groove = 1.0 - 0.22 * _ss(0.55, 1.3, np.abs(pick_a))
        shade = self.shade[L] * (0.74 + 0.26 * np.minimum(1.0, t / 0.3)) * groove
        return uvs.reshape(F3, 3, 2), shade.reshape(F3, 3)

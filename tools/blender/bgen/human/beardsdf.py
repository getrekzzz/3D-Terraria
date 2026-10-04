"""Sculpted beards: the mass over the face (beardlocks' layer, combed into clumps with grooves between its strands and
sinking under the skin where the beard thins out) with the beard's locks (the moustache, the hanging lobes) blended into
it and into each other as tapered, flattened clumps, meshed from their distance field (fieldmesh). UVs run along the
hair (U) for the hair shader's strands and highlights; the colour darkens toward the locks' roots and into the
grooves, and where the beard thins out it fades toward the skin, or up the sideburns toward the head hair.
"""
import hashlib

import numpy as np

from . import fieldmesh, sdf
from .beard import coverages

F = np.float32
CELL = 0.008          # spatial hash cell (head-local) for finding the clumps near a point
FACE = ((-0.095, -0.035, -0.16), (0.095, 0.118, 0.0))      # where the layer over the skin can be


def _ss(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def _unit(v):
    return v / np.maximum(np.linalg.norm(v, axis=-1, keepdims=True), 1e-9)


def _smin(a, b, k):
    """sdf.smin with a blend width per point."""
    k = k * F(1.25)
    h = np.maximum(k - np.abs(a - b), F(0)) / k
    return np.minimum(a, b) - h * h * h * k / F(6.0)


class Sculpt:
    def __init__(self, beard, parts, blend=0.0016):
        """``parts``: (polylines rings x locks x 3, widths, thicknesses per lock or per ring and lock, hanging) per
        part of the beard."""
        self.b = beard
        self.k = blend
        segs, lengths, n = [], [], 0
        for part in parts:
            seg, length = self._part(*part, n)
            segs.append(seg)
            lengths.append(length)
            n += len(length)
        self.s = {key: np.concatenate([seg[key] for seg in segs]) for key in segs[0]}
        self.n_locks = n
        self.length = np.concatenate(lengths)
        self.shade = beard.rng.uniform(0.88, 1.06, n)
        self.band = beard.rng.random(n)
        self._hash()

    def _part(self, P, widths, thick, free, first):
        """One part's segments: each lock a leaf, narrow at the root, full, then drawn out to a point."""
        K, n = P.shape[:2]
        T = _unit(np.gradient(P, axis=0))
        N = self.b.normal(P.reshape(-1, 3)).reshape(K, n, 3)
        out = P * np.asarray((1.0, 1.0, 0.0))                  # hanging hair lies flat round the head's axis
        N = np.where(free[None, :, None], _unit(out + np.asarray((0.0, 0.02, 0.0))), N)
        B = _unit(np.cross(T, N))
        N = np.cross(B, T)
        t = np.arange(K) / (K - 1)
        arc = np.concatenate([np.zeros((1, n)), np.cumsum(np.linalg.norm(np.diff(P, axis=0), axis=2), axis=0)])
        tip = 1 - _ss(np.where(free, 0.55, 0.45)[None], 1.0, t[:, None])
        soft = np.where(free, 0.6, 0.8)[None]                  # a hanging lobe ends in a soft point, not a needle
        w = widths[None] * 0.5 * (0.7 + 0.3 * _ss(0.0, 0.25, t[:, None])) * tip ** soft
        hh = np.broadcast_to(thick, (K, n)) * 0.5 * (0.35 + 0.65 * _ss(0.0, 0.3, t[:, None])) * tip ** (soft - 0.15)
        w, hh = np.maximum(w, 0.0009 + 0.0005 * free), np.maximum(hh, 0.0007 + 0.0004 * free)
        cut = lambda v: (v[:-1].reshape(-1, *v.shape[2:]), v[1:].reshape(-1, *v.shape[2:]))
        s = {}
        for key, v in (("", P), ("B", B), ("N", N), ("w", w), ("h", hh), ("arc", arc)):
            s[key + "a"], s[key + "b"] = (x.astype(F) for x in cut(v))
        s["lock"] = (first + np.broadcast_to(np.arange(n)[None], (K - 1, n))).reshape(-1).astype(np.int64)
        return s, arc[-1]

    # ------------------------------------------------------------------ the clumps' distance field
    def _hash(self):
        """Grid cells -> the segments whose (blend-widened) bounds reach into them."""
        s = self.s
        r = np.maximum(s["wa"], s["wb"]) + self.k * 4
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
        g = np.floor((X - self.origin) / CELL).astype(np.int64)
        ok = np.all((g >= 0) & (g < self.dims), axis=1)
        lin = np.where(ok, self._lin(np.clip(g, 0, self.dims - 1)), -1)
        start = np.searchsorted(self.cells, lin, "left")
        cnt = np.where(ok, np.searchsorted(self.cells, lin, "right") - start, 0)
        pt = np.repeat(np.arange(len(X)), cnt)
        off = np.arange(len(pt)) - np.repeat(np.cumsum(cnt) - cnt, cnt)
        return pt, self.segs[np.repeat(start, cnt) + off]

    def _seg_dist(self, X, sg):
        """Distance from each point to the flattened, tapering clump round its segment, with the segment parameter and
        the point's offset across the clump (-1..1 within its width)."""
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

    def _grooves(self, sg, u, across):
        """Fine grooves along each clump, a few across it: the strands it is made of."""
        s = self.s
        w = s["wa"][sg] + (s["wb"][sg] - s["wa"][sg]) * u
        hh = s["ha"][sg] + (s["hb"][sg] - s["ha"][sg]) * u
        lam = np.clip(w * 1.2, 0.004, 0.009)
        ph = self.band[s["lock"][sg]]
        return (np.minimum(0.0011, 0.14 * hh) * (0.5 - 0.5 * np.cos(2 * np.pi * (across * w / lam + ph)))).astype(F)

    def _blend(self, X):
        """Crisp grooves over the face; the hanging hair melts into a fuller mass."""
        z = X[:, 2]
        return (self.k * (1.0 + 1.2 * _ss(-0.09, -0.14, z) + 1.0 * _ss(-0.14, -0.22, z))).astype(F)

    def clumps(self, X, chunk=100000):
        """Per point, its distances to the nearby clumps folded together with a smooth minimum (nearest first)."""
        out = np.full(len(X), 1.0, dtype=F)
        for c in range(0, len(X), chunk):
            x = X[c:c + chunk]
            pt, sg = self._pairs(x)
            if not len(pt):
                continue
            k = self._blend(x)
            d, u, across = self._seg_dist(x[pt], sg)
            d = d + self._grooves(sg, u, across)
            near = np.full(len(x), np.inf, dtype=F)
            np.minimum.at(near, pt, d)
            m = d < near[pt] + k[pt] * 1.25
            pt, d, lk = pt[m], d[m], self.s["lock"][sg[m]]
            key = pt * self.n_locks + lk
            order = np.argsort(key, kind="stable")
            key, d, pt = key[order], d[order], pt[order]
            first = np.r_[0, np.nonzero(np.diff(key))[0] + 1]
            dl, pl = np.minimum.reduceat(d, first), pt[first]
            order = np.lexsort((dl, pl))
            dl, pl = dl[order], pl[order]
            starts = np.r_[0, np.nonzero(np.diff(pl))[0] + 1]
            rank = np.arange(len(pl)) - np.repeat(starts, np.diff(np.r_[starts, len(pl)]))
            D = np.full((len(x), rank.max() + 1), np.inf, dtype=F)
            D[pl, rank] = dl
            acc = D[:, 0].copy()
            for j in range(1, D.shape[1]):
                col = D[:, j]
                m = np.isfinite(col)
                acc[m] = _smin(acc[m], col[m], k[m])
            # Locks gathered into one clump overlap: the blends must not add up into a bulge where they meet.
            out[c:c + chunk] = np.minimum(np.maximum(acc, D[:, 0] - k * F(0.3)), 1.0)
        return out

    def layer(self, X):
        """Distance to the beard's mass over the face (a shell on the skin, not into the ear canals), and to the head,
        for the points near the face (elsewhere 1)."""
        layer, head = np.ones(len(X), dtype=F), np.ones(len(X), dtype=F)
        lo, hi = FACE
        near = np.all((X > np.asarray(lo, dtype=F)) & (X < np.asarray(hi, dtype=F)), axis=1)
        if near.any():
            x = X[near]
            skin = self.b.skin(x)
            layer[near] = np.maximum(skin - self.b.base(x), -(skin + F(0.0015))) / F(1.3)    # (grooves: steeper)
            head[near] = self.b.full(x)
        return layer, head

    def field(self, X):
        X = np.ascontiguousarray(X, dtype=F)
        layer, head = self.layer(X)
        d = sdf.smin(self.clumps(X), layer, self.k)
        return np.maximum(d, -(head + F(0.0004)))       # nothing inside the head: the beard sits on the skin

    # ------------------------------------------------------------------ the mesh
    def build(self, name="beard"):
        b = self.b
        pts = np.concatenate([self.s["a"], self.s["b"]])
        pad = float(np.maximum(self.s["wa"], self.s["ha"]).max()) + self.k * 4 + 0.004
        lo = np.minimum(pts.min(0) - pad, (-0.09, -0.03, -0.15))
        hi = np.maximum(pts.max(0) + pad, (0.09, 0.115, -0.005))
        long = b.st.length > 0.1
        h, target = (0.0011, 12000) if long else (0.0009, 8000)
        locks = hashlib.sha1(b"".join(np.ascontiguousarray(v).tobytes() for v in self.s.values())).hexdigest()
        key = ("beard", b.head.shape.key(), b.st, locks)
        sources = ("beardsdf.py", "beardlocks.py", "beard.py", "headshape.py", "earshape.py")
        return fieldmesh.build(self.field, (tuple(lo), tuple(hi)), h, target, name, key, sources, "beard",
                               min_piece=150, keep=lambda c: b.full(np.ascontiguousarray(c, dtype=F)) > -0.0002,
                               resample=True)

    def finish(self, obj, color, skin, hair=None):
        """UVs and colours from each face's clump, then the mesh moved from head-local units onto the head."""
        import bmesh
        me = obj.data
        bm = bmesh.new()
        bm.from_mesh(me)
        bm.faces.ensure_lookup_table()
        corners = np.asarray([[v.co[:] for v in f.verts] for f in bm.faces], dtype=F)
        X = corners.reshape(-1, 3)
        lock, u, across, arc = self._nearest(X)
        t = arc / np.maximum(self.length[lock], 1e-6)
        uvs = np.stack([arc * 10.0, self.band[lock] + np.clip(across, -1.5, 1.5) * 0.06], 1)
        groove = 1.0 - 0.25 * _ss(0.5, 1.3, np.abs(across))
        shade = self.shade[lock] * (0.7 + 0.3 * np.minimum(1.0, t / 0.3)) * groove * (1.0 + 0.08 * _ss(0.6, 1.0, t))
        mass = self.layer(X)[0] < self.clumps(X) + F(0.0003)        # on the mass over the face, not on a lock
        _, clump, strand = self.b.comb(X[mass])
        uvs[mass] = np.stack([-X[mass, 2] * 10.0, self.b.across(X[mass]) * 8.0], 1)
        shade[mass] = (0.84 + 0.2 * clump) * (1.0 - 0.14 * strand * (0.4 + 0.6 * (1 - clump)))
        col = np.asarray(color, dtype=F)[None] * shade[:, None]
        cov = coverages(X, self.b.st)
        near_skin = mass * (1.0 - _ss(0.05, 0.85, cov))         # thinning out: the skin shows through
        col = col + (np.asarray(skin, dtype=F)[None] * 0.92 - col) * (near_skin * 0.75)[:, None]
        if hair is not None:          # up the sideburns into the head hair's colour
            up = _ss(-0.04, -0.016, X[:, 2]) * _ss(0.9, 1.2, np.arctan2(np.abs(X[:, 0]), X[:, 1]))
            col = col + (np.asarray(hair, dtype=F)[None] * shade[:, None] - col) * up[:, None]
        uv = bm.loops.layers.uv.new("UVMap")
        layer = bm.loops.layers.color.new("Color")
        for fi, f in enumerate(bm.faces):
            for j, loop in enumerate(f.loops):
                i = fi * 3 + j
                loop[uv].uv = uvs[i]
                loop[layer] = (*np.minimum(col[i], 1.0), 1.0)
        head = self.b.head
        for v in bm.verts:
            v.co = head.at(v.co)
        bm.to_mesh(me)
        bm.free()
        for poly in me.polygons:
            poly.use_smooth = True
        return obj

    def _nearest(self, X):
        """Per point: the clump it belongs to, with the segment parameter, the offset across the clump and the length
        along it (points no clump reaches belong to the nearest segment's clump, at its root)."""
        pt, sg = self._pairs(X)
        d, u, across = self._seg_dist(X[pt], sg)
        best = np.full(len(X), -1, dtype=np.int64)
        order = np.argsort(d)[::-1]
        best[pt[order]] = sg[order]
        lost = np.nonzero(best < 0)[0]
        mid = (self.s["a"] + self.s["b"]) * 0.5
        for c in range(0, len(lost), 2000):
            i = lost[c:c + 2000]
            far = np.einsum("ijk,ijk->ij", X[i, None] - mid[None], X[i, None] - mid[None])
            best[i] = np.argmin(far, axis=1)
        d, u, across = self._seg_dist(X, best)
        s = self.s
        arc = s["arca"][best] + (s["arcb"][best] - s["arca"][best]) * u
        return s["lock"][best], u, across, arc

"""Fitting plates to the body (plates.py): the surfaces a character's plates go over (its clothes and the skin left
showing), smooth stand-ins for them (a limb's cone, a shoulder's ball, the torso's radius round the spine as a smooth
function of angle and height), keeping a meshed plate off them and binding it to the rig like the surface under it.
"""
import math

import numpy as np
from mathutils import Vector

from .plates import _bspline


def _blur(R, passes):
    """[1 2 1] passes round the body (wrapping) and up and down it (edges held)."""
    for _ in range(passes):
        R = (np.roll(R, 1, 0) + 2 * R + np.roll(R, -1, 0)) / 4
        R = np.concatenate([R[:, :1] * 0.75 + R[:, 1:2] * 0.25, (R[:, :-2] + 2 * R[:, 1:-1] + R[:, 2:]) / 4,
                            R[:, -1:] * 0.75 + R[:, -2:-1] * 0.25], 1)
    return R


def _envelope(R, passes=16, lift=10, rounds=6):
    """A smooth surface over binned radii: blurred well past the clothes' folds and seams, then lifted by broad
    smooth swells wherever the bins still stand out of it, so a plate laid on it is an even hard shell that clears
    them (the last millimetre or two is left to Fitter.tuck)."""
    S = _blur(R, passes)
    for _ in range(rounds):
        D = np.pad(np.maximum(R - S, 0.0), ((0, 0), (1, 1)), mode="edge")
        D = np.maximum.reduce([np.roll(D, i, 0)[:, 1 + j:D.shape[1] - 1 + j] for i in (-1, 0, 1) for j in (-1, 0, 1)])
        S = S + _blur(D, lift)
    return S


def field_normals(obj, field, lo=0.75, hi=0.93):
    """Vertex normals from the plate's field wherever it agrees with the mesh (its broad faces: a hard shell shades
    as the smooth surface it is, not as the triangles a reduction left), fading to the mesh's own toward its rim and
    seams; taken before the plate is pushed off the clothes (so a push's small dents do not show in the light)."""
    from .sdf import gradient
    me = obj.data
    co = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", co)
    vn = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertex_normals.foreach_get("vector", vn)
    vn = vn.reshape(-1, 3)
    g = gradient(field, co.reshape(-1, 3), 0.0005)
    g /= np.maximum(np.linalg.norm(g, axis=1), 1e-9)[:, None]
    w = np.clip((np.einsum("ij,ij->i", g, vn) - lo) / (hi - lo), 0, 1)[:, None]
    n = g * (w * w * (3 - 2 * w)) + vn * (1 - w * w * (3 - 2 * w))
    return n / np.maximum(np.linalg.norm(n, axis=1), 1e-9)[:, None]


def set_normals(obj, n):
    obj.data.normals_split_custom_set_from_vertices(np.asarray(n, dtype=float).tolist())


def _binned(r, ia, iz, na, nz, q, least=3):
    """The ``q`` percentile of radii ``r`` in each (round, along) bin, empty bins filled from their neighbours round
    the body (wrapping), then along it."""
    R = np.full((na, nz), np.nan)
    for a in range(na):
        for k in range(nz):
            m = (ia == a) & (iz == k)
            if m.sum() >= least:
                R[a, k] = np.percentile(r[m], q)
    for k in range(nz):
        good = np.flatnonzero(~np.isnan(R[:, k]))
        if len(good):
            R[:, k] = np.interp(np.arange(na), np.concatenate([good - na, good, good + na]), np.tile(R[good, k], 3))
    for a in range(na):
        good = np.flatnonzero(~np.isnan(R[a]))
        R[a] = np.interp(np.arange(nz), good, R[a][good])
    return R


def _lookup(RF, fa, fz):
    """Binned radii read back as a cubic B-spline (no creases at the bins' edges) at fractional bin positions
    (wrapping round, held at the ends along)."""
    na, nz = RF.shape
    fz = np.clip(fz, 0, nz - 1.001)
    a0, z0 = np.floor(fa).astype(int), np.floor(fz).astype(int)
    wa, wz = _bspline((fa - a0).astype(np.float32)), _bspline((fz - z0).astype(np.float32))
    rad = np.zeros(len(fa), dtype=np.float32)
    for i in range(4):
        ai = (a0 + i - 1) % na
        for k in range(4):
            rad += wa[i] * wz[k] * RF[ai, np.clip(z0 + k - 1, 0, nz - 1)]
    return rad


class Fitter:
    """The surfaces plates are fitted to (the clothes and the skin left showing): a BVH for keeping plates off them
    and their vertices' skinning weights for binding plates to the rig."""

    def __init__(self, surfaces, clothes=()):
        from .rig import read_weights
        self.surfaces, self.clothes = list(surfaces), list(clothes)
        self.weights = [w for o in self.surfaces for w in read_weights(o)]
        self._index()

    def _index(self):
        from mathutils.bvhtree import BVHTree
        from mathutils.kdtree import KDTree
        cos, polys, base = [], [], 0
        for o in self.surfaces:
            co = np.empty(len(o.data.vertices) * 3, dtype=np.float32)
            o.data.vertices.foreach_get("co", co)
            mw = np.asarray(o.matrix_world, dtype=np.float32)
            cos.append(co.reshape(-1, 3) @ mw[:3, :3].T + mw[:3, 3])
            polys += [tuple(base + i for i in poly.vertices) for poly in o.data.polygons]
            base += len(co) // 3
        self.co = np.concatenate(cos).astype(np.float32)
        verts = self.co.tolist()
        self.bvh = BVHTree.FromPolygons(verts, polys)
        self.kd = KDTree(len(verts))
        for i, v in enumerate(verts):
            self.kd.insert(v, i)
        self.kd.balance()

    def fit_limb(self, a, b, front, span, lo=0.1, hi=0.9, q=92, guess=None):
        """Radii (at ``a`` and at ``b``) of a cone round the segment that just contains the surfaces under a plate
        facing ``front`` within ``span`` radians of it, between ``lo`` and ``hi`` along the segment (ignoring what
        lies far beyond the ``guess`` radii: the other leg, a foot, the torso)."""
        a, b = np.asarray(a, dtype=float), np.asarray(b, dtype=float)
        length = float(np.linalg.norm(b - a))
        axis = (b - a) / length
        f = np.asarray(front, dtype=float)
        f = f - axis * f.dot(axis)
        f /= np.linalg.norm(f)
        rel = self.co - a
        t = rel @ axis / length
        radial = rel - np.outer(t * length, axis)
        rho = np.linalg.norm(radial, axis=1)
        cos = (radial @ f) / np.maximum(rho, 1e-9)
        limit = 0.16 if guess is None else 1.6 * (guess[0] + (guess[1] - guess[0]) * np.clip(t, 0, 1))
        near = (t > lo) & (t < hi) & (rho < limit) & (cos >= math.cos(min(span, math.pi)))
        ts, rs = [], []
        for k in range(6):
            m = near & (t >= lo + (hi - lo) * k / 6) & (t < lo + (hi - lo) * (k + 1) / 6)
            if m.sum() > 8:
                ts.append(float(t[m].mean()))
                rs.append(float(np.percentile(rho[m], q)))
        if len(ts) < 2:
            return None
        k, c = np.polyfit(ts, rs, 1)
        return float(c), float(c + k)

    def fit_wrap(self, a, b, front, lo, hi, guess, q=92, na=16, nz=8):
        """A smooth stand-in for a limb a plate wraps all the way round (a greave over the shin and calf): its
        radius from the segment ``a`` .. ``b`` as a function of the angle round it and the distance along it (the
        clothes' envelope in bins between ``lo`` and ``hi``, as for the torso); returns (field, key, reach)."""
        a, b = np.asarray(a, dtype=float), np.asarray(b, dtype=float)
        length = float(np.linalg.norm(b - a))
        axis = (b - a) / length
        f = np.asarray(front, dtype=float) - axis * np.dot(front, axis)
        f /= np.linalg.norm(f)
        frame = np.stack([f, np.cross(axis, f), axis]).astype(np.float32)
        t0, dt = lo - 0.04, (hi - lo + 0.08) / nz

        def polar(p):
            q3 = (p - a.astype(np.float32)) @ frame.T
            fa = (np.arctan2(q3[:, 1], q3[:, 0]) + np.float32(math.pi)) / np.float32(2 * math.pi) * na - 0.5
            return np.hypot(q3[:, 0], q3[:, 1]), fa, (q3[:, 2] / np.float32(length) - np.float32(t0)) / np.float32(dt) - 0.5
        rho, fa, fz = polar(self.co)
        t = (fz + 0.5) * dt + t0
        limit = 1.6 * (guess[0] + (guess[1] - guess[0]) * np.clip(t, 0, 1))
        m = (fz > -0.5) & (fz < nz - 0.5) & (rho < limit)
        R = _binned(rho[m], np.floor(fa[m] + 0.5).astype(int) % na, np.floor(fz[m] + 0.5).astype(int), na, nz, q, least=2)
        R = np.round(_envelope(R, passes=4, lift=4, rounds=4), 4)
        RF = R.astype(np.float32)

        def field(p):
            rr, pa, pz = polar(p)
            return rr - _lookup(RF, pa, pz)
        import hashlib
        return field, hashlib.sha1(R.tobytes()).hexdigest()[:12], float(R.max())

    def fit_torso(self, fr, q=94):
        """A smooth stand-in for the clothed torso: its radius from a vertical axis through the spine, as a function
        of the angle round it and the height (the clothes' outer envelope in bins, smoothed over their folds: see
        _envelope), and the field of being that far out; returns (field, key)."""
        s, cy = fr.s, fr.j["spine"].y
        z0, dz, na, nz = 0.86 * s, 0.015 * s, 48, 44
        co = self.co
        rel = co[:, :2] - np.asarray((0.0, cy), dtype=np.float32)
        rho = np.linalg.norm(rel, axis=1)
        phi = np.arctan2(rel[:, 0], rel[:, 1])
        body = (np.abs(co[:, 0]) < 0.19 * s) & (co[:, 2] > z0 - dz) & (rho < 0.3 * s)
        for side, sx in (("R", 1), ("L", -1)):        # not the arms
            sh, wr = np.asarray(fr.j[f"shoulder.{side}"]), np.asarray(fr.j[f"wrist.{side}"])
            ab = wr - sh
            t = np.clip((co - sh) @ ab / ab.dot(ab), 0.0, 1.0)
            near = np.linalg.norm(co - (sh + t[:, None] * ab), axis=1) < 0.075 * s
            body &= ~(near & (co[:, 0] * sx > abs(sh[0]) * 0.75) & (t > 0.02))
        ia = np.floor((phi[body] + math.pi) / (2 * math.pi) * na).astype(int) % na
        iz = np.floor((co[body, 2] - z0) / dz).astype(int)
        R = np.round(_envelope(_binned(rho[body], ia, iz, na, nz, q)), 4)
        RF = R.astype(np.float32)
        self.torso_reach = float(R.max())         # (how far the torso reaches from its axis: for meshing bounds)

        def field(p):
            q2 = p[:, :2] - np.asarray((0.0, cy), dtype=np.float32)
            fa = (np.arctan2(q2[:, 0], q2[:, 1]) + np.float32(math.pi)) / np.float32(2 * math.pi) * na - 0.5
            return np.sqrt(np.einsum("ij,ij->i", q2, q2)) - _lookup(RF, fa, (p[:, 2] - np.float32(z0)) / np.float32(dz) - 0.5)
        import hashlib
        return field, hashlib.sha1(R.tobytes()).hexdigest()[:12]

    def fit_ball(self, c, axis, span, q=92, guess=0.1):
        """The radius of a sphere at ``c`` that just contains the surfaces within ``span`` radians of ``axis``."""
        rel = self.co - np.asarray(c, dtype=np.float32)
        r = np.linalg.norm(rel, axis=1)
        ax = np.asarray(axis, dtype=float) / np.linalg.norm(axis)
        m = (r < 1.6 * guess) & ((rel @ ax) / np.maximum(r, 1e-9) > math.cos(span))
        return float(np.percentile(r[m], q)) if m.sum() > 8 else None

    def clearance(self, co):
        """Signed distance of each point from the surfaces (+ outside)."""
        out = np.empty(len(co))
        for i, c in enumerate(co):
            loc, n, _, _ = self.bvh.find_nearest(Vector(c))
            out[i] = 1.0 if loc is None else (Vector(c) - loc).dot(n)
        return out

    def push(self, obj, core, gap, reach=0.035, most=0.03):
        """Moves the plate out (along the core's outward direction) wherever it comes within ``gap`` of the surfaces
        under it: the move is spread over ``reach`` round each such place (the most any neighbour needs, then
        blurred), so the plate swells smoothly over what is under it instead of taking its every bump."""
        from mathutils.kdtree import KDTree
        from .sdf import gradient
        me = obj.data
        co = np.empty(len(me.vertices) * 3, dtype=np.float32)
        me.vertices.foreach_get("co", co)
        co = co.reshape(-1, 3)
        need = np.clip(gap - self.clearance(co), 0.0, most)
        if not need.any():
            return 0.0
        kd = KDTree(len(co))
        for i, c in enumerate(co):
            kd.insert(c, i)
        kd.balance()
        near = [kd.find_range(c, reach) for c in co]
        grown = np.array([max(need[j] for _, j, _ in hood) for hood in near])
        spread = np.empty(len(co))
        for i, hood in enumerate(near):
            w = np.array([1.0 - dist / reach for _, _, dist in hood])
            spread[i] = float(np.dot(w, grown[[j for _, j, _ in hood]]) / w.sum())
        spread = np.maximum(spread, need)
        g = gradient(core, co, 0.001)
        g /= np.maximum(np.linalg.norm(g, axis=1), 1e-9)[:, None]
        co += g * spread[:, None].astype(np.float32)
        me.vertices.foreach_set("co", co.ravel())
        me.update()
        return float(need.max())

    def tuck(self, core, level, outline, bounds, fade=0.008, most=0.012):
        """Presses the clothes in under a plate (to ``level`` off its core) wherever they stand out past it within
        the plate's ``outline`` (fading out over ``fade`` beyond it), so the plate itself can stay an even shell
        instead of being pushed out over their folds. Only clothes facing the way the plate does and lying just
        under it move (not the other leg, the hip beside a cuff or the inside of a sleeve beside the torso)."""
        from .sdf import gradient
        lo, hi = np.asarray(bounds[0]) - fade, np.asarray(bounds[1]) + fade
        moved = False
        for o in self.clothes:
            me = o.data
            co = np.empty(len(me.vertices) * 3, dtype=np.float32)
            me.vertices.foreach_get("co", co)
            vn = np.empty(len(me.vertices) * 3, dtype=np.float32)
            me.vertex_normals.foreach_get("vector", vn)
            mw = np.asarray(o.matrix_world, dtype=float)
            world = co.reshape(-1, 3) @ mw[:3, :3].T + mw[:3, 3]
            idx = np.flatnonzero(np.all((world > lo) & (world < hi), axis=1))
            if not len(idx):
                continue
            p = world[idx].astype(np.float32)
            over = core(p) - np.float32(level)
            w = np.clip(1 - (outline(p) - 0.002) / fade, 0, 1) * np.clip((0.024 - over) / 0.008, 0, 1)
            sel = (over > 0) & (w > 0)
            if not sel.any():
                continue
            g = gradient(core, p[sel], 0.001)
            g /= np.maximum(np.linalg.norm(g, axis=1), 1e-9)[:, None]
            n = (vn.reshape(-1, 3)[idx[sel]] @ mw[:3, :3].T)
            facing = np.clip((np.einsum("ij,ij->i", g, n) / np.maximum(np.linalg.norm(n, axis=1), 1e-9) - 0.2) / 0.3, 0, 1)
            amt = np.minimum(over[sel], most) * (w[sel] * w[sel] * (3 - 2 * w[sel])) * facing
            world[idx[sel]] -= g * amt[:, None]
            local = (world - mw[:3, 3]) @ np.linalg.inv(mw[:3, :3]).T
            me.vertices.foreach_set("co", local.astype(np.float32).ravel())
            me.update()
            moved = True
        if moved:
            self._index()

    def push_round(self, obj, base, axis, gap, most=0.02, bins=48):
        """Moves a ring out from its ``axis`` (through ``base``) wherever it comes within ``gap`` of the surfaces: by
        the same amount all the way up it, and smoothly round it (the most any part at that angle needs, spread over
        a wide arc), so it widens evenly where it meets the shoulders instead of denting."""
        me = obj.data
        co = np.empty(len(me.vertices) * 3, dtype=np.float32)
        me.vertices.foreach_get("co", co)
        co = co.reshape(-1, 3)
        need = np.clip(gap - self.clearance(co), 0.0, most)
        if not need.any():
            return 0.0
        n = np.asarray(axis, dtype=float) / np.linalg.norm(axis)
        q = co - np.asarray(base, dtype=float)
        radial = q - np.outer(q @ n, n)
        e1 = np.cross(n, (0.0, 1.0, 0.0))
        e1 /= np.linalg.norm(e1)
        f = ((np.arctan2(radial @ np.cross(n, e1), radial @ e1) + math.pi) / (2 * math.pi) * bins) % bins
        m = np.zeros(bins)
        np.maximum.at(m, f.astype(int) % bins, need)
        S = _blur(np.maximum.reduce([np.roll(m, i) for i in range(-3, 4)])[:, None].repeat(3, 1), 6)[:, 1]
        for _ in range(3):
            D = np.maximum.reduce([np.roll(np.maximum(m - S, 0.0), i) for i in range(-2, 3)])
            S = S + _blur(D[:, None].repeat(3, 1), 3)[:, 1]
        i0 = np.floor(f - 0.5).astype(int)
        w = (f - 0.5 - i0)
        amount = S[i0 % bins] * (1 - w) + S[(i0 + 1) % bins] * w
        out = radial / np.maximum(np.linalg.norm(radial, axis=1), 1e-9)[:, None]
        me.vertices.foreach_set("co", (co + out * amount[:, None]).astype(np.float32).ravel())
        me.update()
        return float(need.max())

    def bind(self, obj, stiff=0.5, keep=None):
        """Skinning weights: each vertex takes the weights of the surface vertices nearest it, blended by ``stiff``
        toward the plate's mean (a stiffer plate bends less with the body); ``keep`` limits the bones used."""
        from .rig import write_weights
        per = []
        for v in obj.data.vertices:
            acc, total = {}, 0.0
            for _, idx, dist in self.kd.find_n(v.co, 6):
                k = 1.0 / (dist + 0.004)
                total += k
                for b, w in self.weights[idx].items():
                    acc[b] = acc.get(b, 0.0) + w * k
            per.append({b: w / total for b, w in acc.items()})
        mean = {}
        for w in per:
            for b, x in w.items():
                mean[b] = mean.get(b, 0.0) + x / len(per)
        out = []
        for w in per:
            mixed = {b: (1 - stiff) * w.get(b, 0.0) + stiff * mean.get(b, 0.0) for b in sorted(set(w) | set(mean))}
            if keep is not None:
                mixed = {b: x for b, x in mixed.items() if b.split(".")[0] in keep} or {keep[0]: 1.0}
            out.append({b: x for b, x in mixed.items() if x > 0.02})
        write_weights(obj, out)

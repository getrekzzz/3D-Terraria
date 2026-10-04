"""Hard plates fitted over the body (armour, carapace, a suit's panels), modelled as distance fields in world space
(metres, the rest pose) and meshed like the head (fieldmesh, cached):

A plate is a shell between two offsets from a smooth *core* standing in for the body part under it (the torso, a
shoulder, a limb segment, fitted to the clothes it covers: Fitter), cut to an outline drawn in the core's own
surface coordinates (round the torso, along and round a limb) with a rounded rim; panel seams are grooves cut into
its outer face, ridges and raised panels are added to it. After meshing, a plate is pushed out wherever it would
come closer to the clothes under it than its gap, and it takes the clothes' skinning weights (averaged over its
neighbourhood, so it moves as the surface under it does). Glowing accent lines are thin strips swept along curves on
a plate's face.
"""
import math

import bmesh
import numpy as np
from mathutils import Vector

from .sdf import F, round_cone, smax, smin

SOURCES = ("plates.py",)


# ---------------------------------------------------------------------------------------------------- cores
def _bspline(t):
    """The four uniform cubic B-spline weights at fractions ``t``."""
    t2, t3 = t * t, t * t * t
    return ((1 - t) ** 3 / 6, (3 * t3 - 6 * t2 + 4) / 6, (-3 * t3 + 3 * t2 + 3 * t + 1) / 6, t3 / 6)


def limb(a, b, ra, rb):
    """A limb segment's surface: a cone between spheres at its joints."""
    a, b = tuple(a), tuple(b)
    return lambda p: round_cone(p, a, b, ra, rb)


def ball(c, r):
    c = np.asarray(c, dtype=F)
    return lambda p: np.sqrt(np.einsum("ij,ij->i", p - c, p - c)) - F(r)


# ---------------------------------------------------------------------------------------------------- surface coordinates
def around_torso(fr, p, ref=None, back=False):
    """(u, z): arc length round the torso from the front (+ toward the character's right), or from the back, and
    height."""
    ref = ref or 0.13 * fr.s
    cy = fr.j["spine"].y
    phi = np.arctan2(p[:, 0], (p[:, 1] - F(cy)) * F(-1 if back else 1))
    return phi * F(ref), p[:, 2]


def along(p, a, b, front=(0.0, 1.0, 0.0), ref=0.045):
    """(u, v) on a limb from ``a`` to ``b``: arc length round it from its ``front`` side, and distance along it."""
    a, b = np.asarray(a, dtype=float), np.asarray(b, dtype=float)
    axis = (b - a) / np.linalg.norm(b - a)
    f = np.asarray(front, dtype=float)
    f = f - axis * f.dot(axis)
    f /= np.linalg.norm(f)
    side = np.cross(axis, f)
    q = p - a.astype(F)
    v = q @ axis.astype(F)
    phi = np.arctan2(q @ side.astype(F), q @ f.astype(F))
    return phi * F(ref), v


def box(u, v, cu, cv, hu, hv, r=0.0):
    """A rounded rectangle's distance in (u, v)."""
    qx, qy = np.abs(u - F(cu)) - F(hu - r), np.abs(v - F(cv)) - F(hv - r)
    return np.sqrt(np.maximum(qx, 0) ** 2 + np.maximum(qy, 0) ** 2) + np.minimum(np.maximum(qx, qy), 0) - F(r)


def shell(core, inner, thick):
    """The band between ``inner`` and ``inner + thick`` off the core's surface."""
    return smax(core - F(inner + thick), F(inner) - core, 0.0)


def groove(d, core, line, outer, depth=0.0012, k=0.0008):
    """``d`` with a seam cut ``depth`` into its outer face (at ``outer`` off the core) where ``line`` < 0."""
    return smax(d, -smax(line, F(outer - depth) - core, k), k)


def plate(core, inner, thick, outline, rim=0.0025, seams=(), extra=None):
    """A plate's field: the shell ``inner`` .. ``inner + thick`` off ``core``, cut to ``outline`` (< 0 inside) with a
    rim rounded over ``rim``, seams (fields of lines, < 0 on them) grooved into its face; ``extra(p, core, d) -> d``
    adds ridges or panels."""
    def field(p):
        c = core(p)
        d = smax(shell(c, inner, thick), outline(p), rim)
        for line in seams:
            d = groove(d, c, line(p), inner + thick)
        return d if extra is None else extra(p, c, d)
    return field


# ---------------------------------------------------------------------------------------------------- meshing and fitting
def mesh(field, bounds, name, key, sources, h=0.001, target=3000):
    """The plate's mesh (world space), cached like the head's."""
    from . import fieldmesh
    lo, hi = bounds
    bounds = (tuple(round(float(v), 4) for v in lo), tuple(round(float(v), 4) for v in hi))
    return fieldmesh.build(field, bounds, h, target, name, key, SOURCES + tuple(sources), "plates", min_piece=40)


def mirror(obj, name):
    """A copy of ``obj`` mirrored to the character's other side."""
    import bpy
    me = obj.data.copy()
    me.name = name
    co = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    co[:, 0] *= -1
    me.vertices.foreach_set("co", co.ravel())
    me.flip_normals()
    me.update()
    out = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(out)
    return out


class Fitter:
    """The surfaces plates are fitted to (the clothes and the skin left showing): a BVH for keeping plates off them
    and their vertices' skinning weights for binding plates to the rig."""

    def __init__(self, surfaces):
        from mathutils.bvhtree import BVHTree
        from mathutils.kdtree import KDTree
        from .rig import read_weights
        verts, polys, self.weights = [], [], []
        for o in surfaces:
            base = len(verts)
            verts += [o.matrix_world @ v.co for v in o.data.vertices]
            polys += [tuple(base + i for i in poly.vertices) for poly in o.data.polygons]
            self.weights += read_weights(o)
        self.bvh = BVHTree.FromPolygons(verts, polys)
        self.co = np.asarray([v[:] for v in verts], dtype=np.float32)
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
        near = (t > lo) & (t < hi) & (rho < limit) & (cos > math.cos(span))
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

    def fit_torso(self, fr, q=94):
        """A smooth stand-in for the clothed torso: its radius from a vertical axis through the spine, as a function
        of the angle round it and the height (the clothes' outer envelope in bins, blurred), and the field of being
        that far out; returns (field, key)."""
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
        R = np.full((na, nz), np.nan)
        r = rho[body]
        for a in range(na):
            for k in range(nz):
                m = (ia == a) & (iz == k)
                if m.sum() >= 3:
                    R[a, k] = np.percentile(r[m], q)
        for k in range(nz):                      # gaps filled from the neighbours round the body, then up and down
            row = R[:, k]
            if np.isnan(row).all():
                continue
            good = np.flatnonzero(~np.isnan(row))
            R[:, k] = np.interp(np.arange(na), np.concatenate([good - na, good, good + na]), np.tile(row[good], 3))
        for a in range(na):
            col = R[a]
            good = np.flatnonzero(~np.isnan(col))
            R[a] = np.interp(np.arange(nz), good, col[good])
        for _ in range(3):
            R = (np.roll(R, 1, 0) + 2 * R + np.roll(R, -1, 0)) / 4
            R[:, 1:-1] = (R[:, :-2] + 2 * R[:, 1:-1] + R[:, 2:]) / 4
        R = np.round(R, 4)
        RF = R.astype(np.float32)
        self.torso_reach = float(R.max())         # (how far the torso reaches from its axis: for meshing bounds)

        def field(p):
            q2 = p[:, :2] - np.asarray((0.0, cy), dtype=np.float32)
            rr = np.sqrt(np.einsum("ij,ij->i", q2, q2))
            fa = (np.arctan2(q2[:, 0], q2[:, 1]) + np.float32(math.pi)) / np.float32(2 * math.pi) * na - 0.5
            fz = np.clip((p[:, 2] - np.float32(z0)) / np.float32(dz) - 0.5, 0, nz - 1.001)
            a0, z0i = np.floor(fa).astype(int), np.floor(fz).astype(int)
            wa, wz = _bspline((fa - a0).astype(np.float32)), _bspline((fz - z0i).astype(np.float32))
            rad = np.zeros(len(p), dtype=np.float32)          # (a cubic B-spline: no creases at the bins' edges)
            for i in range(4):
                ai = (a0 + i - 1) % na
                for k in range(4):
                    rad += wa[i] * wz[k] * RF[ai, np.clip(z0i + k - 1, 0, nz - 1)]
            return rr - rad
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


# ---------------------------------------------------------------------------------------------------- points on a plate
def onto(core, origin, direction, off, reach=0.3):
    """Where rays from ``origin`` along ``direction`` (arrays) cross ``off`` off the core's surface."""
    o, d = np.asarray(origin, dtype=F), np.asarray(direction, dtype=F)
    d = d / np.linalg.norm(d, axis=1, keepdims=True)
    lo, hi = np.zeros(len(o), dtype=F), np.full(len(o), reach, dtype=F)
    for _ in range(30):
        mid = (lo + hi) * F(0.5)
        inside = core(o + d * mid[:, None]) < F(off)
        lo, hi = np.where(inside, mid, lo), np.where(inside, hi, mid)
    return o + d * lo[:, None]


def torso_points(fr, core, us, zs, off, ref=None, back=False):
    """Points ``off`` off the torso core at arc lengths ``us`` round it (see around_torso) and heights ``zs``."""
    ref = ref or 0.13 * fr.s
    cy = fr.j["spine"].y
    phi = np.asarray(us, dtype=float) / ref
    zs = np.asarray(zs, dtype=float)
    origin = np.stack([0 * phi, 0 * phi + cy, zs], 1)
    return onto(core, origin, np.stack([np.sin(phi), np.cos(phi) * (-1 if back else 1), 0 * phi], 1), off)


def limb_points(core, a, b, us, vs, off, front=(0.0, 1.0, 0.0), ref=0.045):
    """Points ``off`` off a limb core at arc lengths ``us`` round it and distances ``vs`` along it (see along)."""
    a, b = np.asarray(a, dtype=float), np.asarray(b, dtype=float)
    axis = (b - a) / np.linalg.norm(b - a)
    f = np.asarray(front, dtype=float)
    f = f - axis * f.dot(axis)
    f /= np.linalg.norm(f)
    side = np.cross(axis, f)
    phi = np.asarray(us, dtype=float) / ref
    origin = a + np.outer(np.asarray(vs, dtype=float), axis)
    return onto(core, origin, np.outer(np.cos(phi), f) + np.outer(np.sin(phi), side), off)


def strip(bm, core, pts, width, height, sink=0.0):
    """A thin rounded strip (a glowing accent line, a raised seam) along points on a plate's face, lying flat on it
    (``sink`` into it); returns the sweep's (vert, t, a) list."""
    from .features import sweep
    from .sdf import gradient
    pts = np.asarray(pts, dtype=F)
    n = gradient(core, pts, 0.001)
    n /= np.linalg.norm(n, axis=1, keepdims=True)
    path = [Vector(p - q * sink) for p, q in zip(pts, n)]
    return sweep(bm, path, lambda t, a: (math.cos(a) * height, math.sin(a) * width), 8, up=Vector(n[0]))

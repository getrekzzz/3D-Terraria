"""Hair: where it grows (the hairline round the face, ears and nape), the smooth cover long hair lies over the ears
on, eyebrow strands and eyelashes. The hair on the scalp is built from locks (locks.py), beards by beard.py. Every hair surface has UVs running along the strands
(U = along), so the exported tangents follow the hair and the game's hair shader can draw anisotropic highlights.
"""
import math

import bmesh
import numpy as np
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

from .paint import brow_arch, hex3

EAR_ZONE = ((-0.0125, -0.019), (0.0245, 0.042))   # (y, z) centre and radii of the hair-free zone round each ear


HAIRLINE_A = np.asarray((0.0, 0.35, 0.62, 0.9, 1.06, 1.22, 1.42, 1.75, 2.3, math.pi))   # azimuth from the front


def _ss(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def _points(q):
    return np.asarray([[q.x, q.y, q.z]]) if isinstance(q, Vector) else np.atleast_2d(np.asarray(q, dtype=float))


def _pchip(xs, ys, x):
    """A smooth curve through the knots that never overshoots them (monotone cubic), level at both ends."""
    h = np.diff(xs)
    dl = np.diff(ys) / h
    m = np.zeros(len(xs))
    w1, w2 = 2 * h[1:] + h[:-1], h[1:] + 2 * h[:-1]
    same = dl[:-1] * dl[1:] > 0
    m[1:-1] = np.where(same, (w1 + w2) / np.where(same, w1 / np.where(same, dl[:-1], 1) + w2 / np.where(same, dl[1:], 1), 1), 0.0)
    i = np.clip(np.searchsorted(xs, x) - 1, 0, len(xs) - 2)
    t = (x - xs[i]) / h[i]
    return (ys[i] * (2 * t ** 3 - 3 * t ** 2 + 1) + h[i] * m[i] * (t ** 3 - 2 * t ** 2 + t)
            + ys[i + 1] * (3 * t ** 2 - 2 * t ** 3) + h[i] * m[i + 1] * (t ** 3 - t ** 2))


def hairlines(q, part=0.0, sideburns=True):
    """Height (head-local z) of the hairline at the azimuth of each point: a forehead line that recedes a little at
    the temples, sideburns down in front of the ears, the line behind the ears and the nape. ``part`` (-1..1) lowers
    the hairline on one side of the forehead for a fringe swept across from the parting."""
    q = _points(q)
    phi = np.arctan2(q[:, 0], q[:, 1])    # 0 front, +/-pi back; + toward the character's right
    a = np.abs(phi)
    burn = -0.028 if sideburns else -0.006
    zs = np.asarray((0.052, 0.054, 0.045, 0.031, 0.012, burn, burn - 0.004, -0.036, -0.056, -0.076))
    z = _pchip(HAIRLINE_A, zs, a)
    if part:                              # the swept side of the fringe falls lower over the forehead
        side = 0.5 + 0.5 * np.tanh((-phi * (1 if part > 0 else -1) + 0.15) * 5)
        z = z - np.where(a < 1.0, abs(part) * 0.016 * side * np.clip(1 - a, 0, 1) ** 0.7, 0.0)
    return z


def ear_clears(q, soft=0.0):
    """1 away from the ears, 0 on and right round them: hair grows round the ears, not over them (``soft``: thinning
    out over a wider band round them, as hair pulled back does)."""
    q = _points(q)
    (cy, cz), (ry, rz) = EAR_ZONE
    r = np.hypot((q[:, 1] - cy) / ry, (q[:, 2] - cz) / rz)
    return np.where(np.abs(q[:, 0]) < 0.035, 1.0, _ss(0.92, 1.12 + soft, r))


def coverages(q, part=0.0, sideburns=True, soft=0.006, ears=True):
    """How much hair grows at each point (0 bare skin .. 1 full): it thins out over a few millimetres at the
    hairline and (``ears``) stops short of the ears."""
    q = _points(q)
    c = _ss(-soft, soft, q[:, 2] - hairlines(q, part, sideburns))
    return c * ear_clears(q) if ears else c


def hairline(q, part=0.0, sideburns=True) -> float:
    return float(hairlines(q, part, sideburns)[0])


def ear_clear(q) -> float:
    return float(ear_clears(q)[0])


def coverage(q, part=0.0, sideburns=True, soft=0.006) -> float:
    return float(coverages(q, part, sideburns, soft)[0])


def scalp_tint(q, part=0.0, sideburns=True) -> float:
    """How much the skin under the hair takes its colour (paint): only well inside the hair's edge, so none of it
    shows below the hairline as a stain on the forehead or in front of the ears."""
    q = _points(q) - np.asarray((0.0, 0.0, 0.011))
    return float(coverages(q, part, sideburns, 0.005)[0])


_COVERS = {}


def ear_cover(shape, slope=0.6):
    """The surface long hair lies over: the bare skull with each ear covered over by a smooth dome (the hair falls
    over the ear from its rim, sloping back to the skull round it at ``slope``), so the hair hangs past the ears in
    one curtain instead of parting round them. A field like the head's (head-local units)."""
    key = (shape.key(), slope)
    if key not in _COVERS:
        _COVERS[key] = _ear_bumps(shape, slope=slope)
    lo, step, table = _COVERS[key]
    skull = lambda p: shape.field(p, ears=False, face=False)
    if table is None:
        return skull
    n = np.asarray(table.shape) - 1

    def field(p):
        d = skull(p)
        g = (p[:, 1:] - lo) / step
        i = np.clip(np.floor(g).astype(np.int64), 0, n - 1)
        t = np.clip(g - i, 0.0, 1.0)
        b = (table[i[:, 0], i[:, 1]] * (1 - t[:, 0]) * (1 - t[:, 1]) + table[i[:, 0] + 1, i[:, 1]] * t[:, 0] * (1 - t[:, 1])
             + table[i[:, 0], i[:, 1] + 1] * (1 - t[:, 0]) * t[:, 1] + table[i[:, 0] + 1, i[:, 1] + 1] * t[:, 0] * t[:, 1])
        inside = np.all((g >= 0) & (g <= n), axis=1)
        return (d - np.where(inside, b, 0.0) * _ss(0.03, 0.06, np.abs(p[:, 0]))).astype(np.float32)
    return field


def _ear_bumps(shape, step=0.002, slope=0.6, margin=0.002, top=0.015):
    """How far out (along x) the ear stands from the bare skull over a (y, z) grid round it, widened into a dome:
    every point is at least as high as the ear's rim less a fall that curves over gently within ``top`` of it (a
    rounded crown, so neither the ear's shape nor a plateau over it shows) and slopes off at ``slope`` beyond, plus
    ``margin``."""
    if not shape.ears:
        return None, step, None
    lo = np.asarray((-0.078, -0.088))
    ys = lo[0] + step * np.arange(66)
    zs = lo[1] + step * np.arange(74)
    xs = np.arange(0.045, 0.13, 0.001)
    Y, Z, X = np.meshgrid(ys, zs, xs, indexing="ij")
    p = np.stack([X, Y, Z], -1).reshape(-1, 3).astype(np.float32)
    shape_ = (len(ys), len(zs), len(xs))

    def outer(f):
        inside = (f.reshape(shape_) < 0)
        last = np.where(inside.any(2), len(xs) - 1 - np.argmax(inside[:, :, ::-1], axis=2), -1)
        return np.where(last >= 0, xs[np.maximum(last, 0)], np.nan)
    ear = outer(shape.field(p, ears=True, face=False))
    skull = outer(shape.field(p, ears=False, face=False))
    b = np.nan_to_num(np.maximum(ear - np.nan_to_num(skull, nan=0.0), 0.0), nan=0.0)
    b = np.where(b > 0.0015, b, 0.0)
    r = int(min(0.055, float(b.max()) / slope + top / 2 + 0.002) / step)       # as far as the dome reaches
    dome = b.copy()
    pad = np.pad(b, r)
    for dy in range(-r, r + 1):
        for dz in range(-r, r + 1):
            dist = math.hypot(dy, dz) * step
            if dist <= r * step:
                fall = slope * (dist * dist / (2 * top) if dist < top else dist - top / 2)
                dome = np.maximum(dome, pad[r + dy:r + dy + b.shape[0], r + dz:r + dz + b.shape[1]] - fall)
    dome = np.where(dome > 0, dome + margin * _ss(0.0, 0.004, dome), 0.0)
    for _ in range(2):                       # smoothed so the hair over it has no creases
        pad = np.pad(dome, 1, mode="edge")
        dome = sum(pad[1 + a:1 + a + dome.shape[0], 1 + c:1 + c + dome.shape[1]] for a in (-1, 0, 1) for c in (-1, 0, 1)) / 9
    return lo, step, np.maximum(dome, np.where(b > 0, b + 0.0015, 0.0))    # (never down onto the ear's rim)


def mesh_from(bm, name):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    for poly in obj.data.polygons:
        poly.use_smooth = True
    return obj


def brows_and_lashes(head, head_obj, hair_color, lash_color="#1a1412"):
    """Eyebrow strands laid on the skin along the brow arch, and upper eyelashes curling out from the lid margin."""
    tree = BVHTree.FromObject(head_obj, bpy.context.evaluated_depsgraph_get())
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    col = bm.loops.layers.color.new("Color")
    brow = hex3(hair_color)
    lash = hex3(lash_color)
    h = head.h
    import random
    rng = random.Random(7)

    def strand(p, direction, normal, length, width, color):
        side = direction.cross(normal).normalized() * width / 2
        a, b = p - side, p + side
        c, d = p + direction * length + side * 0.3, p + direction * length - side * 0.3
        verts = [bm.verts.new(x) for x in (a, b, c, d)]
        f = bm.faces.new(verts)
        for loop, u in zip(f.loops, ((0, 0), (0, 1), (1, 1), (1, 0))):
            loop[uv].uv = u
            loop[col] = (*color, 1.0)

    fem = head.frame.female        # a woman's brows are finer and more arched, her lashes longer
    for sx in (1, -1):
        e = head.local(head.eyes[sx])
        for _ in range(70):
            x = rng.uniform(-0.016, 0.024)
            arch = brow_arch(x, fem)
            z = arch + rng.gauss(0, 0.0017) * (0.55 if fem else 1.0) * (1.0 - 0.4 * max(0.0, x / 0.024))
            origin = head.at((e.x + sx * x, 0.14, e.z + z))
            hit, normal, _, _ = tree.ray_cast(origin, Vector((0, -1, 0)))
            if hit is None:
                continue
            flow = Vector((sx * (0.9 if x > -0.008 else 0.4), 0, 0.35 if x < 0.01 else -0.15)).normalized()
            flow = (flow - normal * flow.dot(normal)).normalized()
            strand(hit + normal * 0.0004 * h, flow, normal, rng.uniform(0.006, 0.009) * h * (0.8 if fem else 1.0),
                   (0.0007 if fem else 0.0009) * h, brow)
        # Upper lashes along the lid margin, curling up and out, longest toward the outer corner.
        shape = head.shape
        rs = shape.Rs
        for k in range(18):
            t = 0.06 + 0.9 * k / 17
            u = shape.u_in + (shape.u_out - shape.u_in) * t
            v = float(shape.lid_curves(np.asarray([u]))[0][0])
            w = math.sqrt(max(rs * rs - u * u - v * v, 0.0))
            p = head.at((e.x + sx * u, e.y + w - 0.0004, e.z + v))
            d = Vector((sx * u * 10, 1.0, 1.1)).normalized()
            strand(p, d, Vector((0, 1, 0.2)).normalized(), (0.0076 if fem else 0.0052) * h * (0.55 + 0.45 * math.sin(math.pi * min(1.0, t * 1.15))),
                   0.0009 * h, lash)
    obj = mesh_from(bm, "brows")
    return obj

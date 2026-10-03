"""Hair: where it grows (the hairline round the face, ears and nape), beards, eyebrow strands and eyelashes. The hair
on the scalp is built from locks (locks.py). Every hair surface has UVs running along the strands (U = along), so the
exported tangents follow the hair and the game's hair shader can draw anisotropic highlights.
"""
import math

import bmesh
import numpy as np
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

from .head import smooth
from .paint import hex3

EAR_ZONE = ((-0.0125, -0.019), (0.0245, 0.042))   # (y, z) centre and radii of the hair-free zone round each ear


HAIRLINE_A = np.asarray((0.0, 0.35, 0.62, 0.9, 1.06, 1.22, 1.42, 1.75, 2.3, math.pi))   # azimuth from the front


def _ss(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def _points(q):
    return np.asarray([[q.x, q.y, q.z]]) if isinstance(q, Vector) else np.atleast_2d(np.asarray(q, dtype=float))


def hairlines(q, part=0.0, sideburns=True):
    """Height (head-local z) of the hairline at the azimuth of each point: a forehead line that recedes a little at
    the temples, sideburns down in front of the ears, the line behind the ears and the nape. ``part`` (-1..1) lowers
    the hairline on one side of the forehead for a fringe swept across from the parting."""
    q = _points(q)
    phi = np.arctan2(q[:, 0], q[:, 1])    # 0 front, +/-pi back; + toward the character's right
    a = np.abs(phi)
    burn = -0.028 if sideburns else -0.006
    zs = np.asarray((0.052, 0.054, 0.045, 0.031, 0.012, burn, burn - 0.004, -0.036, -0.056, -0.076))
    i = np.clip(np.searchsorted(HAIRLINE_A, a) - 1, 0, len(zs) - 2)
    t = (a - HAIRLINE_A[i]) / (HAIRLINE_A[i + 1] - HAIRLINE_A[i])
    z = zs[i] + (zs[i + 1] - zs[i]) * (t * t * (3 - 2 * t))
    if part:                              # the swept side of the fringe falls lower over the forehead
        side = 0.5 + 0.5 * np.tanh((-phi * (1 if part > 0 else -1) + 0.15) * 5)
        z = z - np.where(a < 1.0, abs(part) * 0.016 * side * np.clip(1 - a, 0, 1) ** 0.7, 0.0)
    return z


def ear_clears(q):
    """1 away from the ears, 0 on and right round them: hair grows round the ears, not over them."""
    q = _points(q)
    (cy, cz), (ry, rz) = EAR_ZONE
    r = np.hypot((q[:, 1] - cy) / ry, (q[:, 2] - cz) / rz)
    return np.where(np.abs(q[:, 0]) < 0.035, 1.0, _ss(0.92, 1.12, r))


def coverages(q, part=0.0, sideburns=True, soft=0.006):
    """How much hair grows at each point (0 bare skin .. 1 full): it thins out over a few millimetres at the
    hairline and stops short of the ears."""
    q = _points(q)
    return _ss(-soft, soft, q[:, 2] - hairlines(q, part, sideburns)) * ear_clears(q)


def hairline(q, part=0.0, sideburns=True) -> float:
    return float(hairlines(q, part, sideburns)[0])


def ear_clear(q) -> float:
    return float(ear_clears(q)[0])


def coverage(q, part=0.0, sideburns=True, soft=0.006) -> float:
    return float(coverages(q, part, sideburns, soft)[0])


def mesh_from(bm, name):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    for poly in obj.data.polygons:
        poly.use_smooth = True
    return obj


def beard_region(q, full=True) -> float:
    """Signed distance-like margin (head-local metres) inside the beard: positive inside, 0 at its edge. The beard
    covers the jaw and chin below a cheek line, a moustache above the upper lip, and leaves the lips bare."""
    x = abs(q.x)
    cheek = (-0.037 - 0.3 * max(0.0, 0.05 - x)) - q.z
    front = q.y + 0.005
    neck = q.z + 0.135
    width = 0.05 - x if not full else 1.0
    lips = (math.hypot(q.x / 0.027, (q.z + 0.07) / 0.0105) - 1.0) * 0.009
    beard = min(cheek, front, neck, lips, width)
    moustache = min(0.03 - x, -0.05 - q.z, q.z + 0.061, q.y - 0.07, lips + 0.002)
    return max(beard, moustache)


def beard(head, head_obj, color, full=True):
    """A beard over the jaw, chin and upper lip, clumped, fuller at the chin and thinning to nothing at its edge."""
    bm = bmesh.new()
    bm.from_mesh(head_obj.data)
    bm.normal_update()
    drop = [f for f in bm.faces if beard_region(head.local(f.calc_center_median()), full) <= 0.0]
    bmesh.ops.delete(bm, geom=drop, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.normal_update()
    uv = bm.loops.layers.uv.new("UVMap")
    col = bm.loops.layers.color.get("Color") or bm.loops.layers.color.new("Color")
    base = hex3(color)
    for v in bm.verts:
        q = head.local(v.co)
        edge = smooth(0.0, 0.007, beard_region(q, full))
        depth = 0.003 + 0.013 * smooth(-0.06, -0.11, q.z) * (1.0 if full else 0.6)
        clump = 0.75 + 0.25 * math.sin(q.x * 300) * math.sin(q.z * 220)
        v.co += (v.normal * depth * clump + Vector((0, 0, -0.006 * smooth(-0.07, -0.12, q.z)))) * head.h * edge
    for f in bm.faces:
        for loop in f.loops:
            q = head.local(loop.vert.co)
            loop[uv].uv = (-q.z * 12, q.x * 8)
            shade = 0.85 + 0.15 * math.sin(q.x * 400 + q.z * 90)
            loop[col] = (base[0] * shade, base[1] * shade, base[2] * shade, 1.0)
    return mesh_from(bm, "beard")


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

    for sx in (1, -1):
        e = head.local(head.eyes[sx])
        for _ in range(70):
            x = rng.uniform(-0.016, 0.024)
            arch = 0.0165 + 0.0035 * math.sin((x + 0.012) / 0.03 * math.pi)
            z = arch + rng.gauss(0, 0.0017) * (1.0 - 0.4 * max(0.0, x / 0.024))
            origin = head.at((e.x + sx * x, 0.14, e.z + z))
            hit, normal, _, _ = tree.ray_cast(origin, Vector((0, -1, 0)))
            if hit is None:
                continue
            flow = Vector((sx * (0.9 if x > -0.008 else 0.4), 0, 0.35 if x < 0.01 else -0.15)).normalized()
            flow = (flow - normal * flow.dot(normal)).normalized()
            strand(hit + normal * 0.0004 * h, flow, normal, rng.uniform(0.006, 0.009) * h, 0.0009 * h, brow)
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
            strand(p, d, Vector((0, 1, 0.2)).normalized(), 0.0062 * h * (0.55 + 0.45 * math.sin(math.pi * min(1.0, t * 1.15))), 0.0009 * h, lash)
    obj = mesh_from(bm, "brows")
    return obj

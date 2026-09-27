"""Hair: a scalp shell (offset from the head, thick on top, feathered at the hairline, broken into clumps and
strands) plus styles built on it — short, messy, long (a hanging mane), ponytail, bun, bald — and beards,
eyebrow strands and eyelashes. Every hair surface has UVs running along the strands (U = along), so the exported
tangents follow the hair and the game's hair shader can draw anisotropic highlights.
"""
import math

import bmesh
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

from .head import smooth
from .paint import hex3


def hairline(q) -> float:
    """Height (head-local z) of the hairline at the azimuth of q: forehead, temples, above the ears, nape."""
    phi = abs(math.atan2(q.x, q.y))       # 0 front, pi back
    pts = [(0.0, 0.052), (0.55, 0.045), (0.95, 0.03), (1.3, 0.012), (1.55, 0.004), (1.85, 0.0), (2.3, -0.045), (math.pi, -0.07)]
    for (a0, z0), (a1, z1) in zip(pts, pts[1:]):
        if phi <= a1:
            t = (phi - a0) / (a1 - a0)
            return z0 + (z1 - z0) * t
    return pts[-1][1]


def _in_ear(q):
    return abs(q.x) > 0.058 and -0.045 < q.z < 0.02 and -0.03 < q.y < 0.025


def _mesh_from(bm, name):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    for poly in obj.data.polygons:
        poly.use_smooth = True
    return obj


def _strand_uv(bm, head, uv):
    """U runs from the crown outward/downward along the hair, V around the head."""
    crown = Vector((0, -0.02, 0.1))
    for f in bm.faces:
        for loop in f.loops:
            q = head.local(loop.vert.co)
            d = q - crown
            loop[uv].uv = (d.length * 12.0, math.atan2(q.x, -q.y) * 1.6)


def scalp_shell(head, head_obj, style, color, under_hat=False):
    """Duplicates the scalp and lifts it into a hair volume with clumps, a parting and a feathered hairline. Under a
    hat only the hair below the hat band remains."""
    thickness = {"short": 0.009, "messy": 0.013, "long": 0.012, "ponytail": 0.007, "bun": 0.007, "buzz": 0.0025}[style]
    bm = bmesh.new()
    bm.from_mesh(head_obj.data)
    bm.normal_update()
    drop = []
    for f in bm.faces:
        q = head.local(f.calc_center_median())
        if q.z < hairline(q) - 0.004 or _in_ear(q) or q.z < -0.13 or (under_hat and q.z > 0.04):
            drop.append(f)
    bmesh.ops.delete(bm, geom=drop, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.normal_update()
    for v in [v for v in bm.verts if v.is_boundary]:
        q = head.local(v.co)
        if not _in_ear(q) and q.z > -0.12:
            v.co = head.at((q.x, q.y, hairline(q) - 0.004))
    bm.normal_update()
    uv = bm.loops.layers.uv.new("UVMap")
    col = bm.loops.layers.color.get("Color") or bm.loops.layers.color.new("Color")
    base = hex3(color)
    for v in bm.verts:
        q = head.local(v.co)
        edge = smooth(0.0, 0.012, q.z - hairline(q))
        phi = math.atan2(q.x, -q.y)
        clump = 0.55 + 0.45 * math.sin(phi * 9 + q.z * 60) * math.sin(phi * 4 - q.z * 25)
        if style == "messy":
            clump += 0.5 * math.sin(phi * 13 + q.z * 90)
        top = 0.6 + 0.4 * smooth(-0.02, 0.09, q.z)
        v.co += v.normal * thickness * head.h * edge * top * (0.75 + 0.35 * clump)
    for f in bm.faces:
        for loop in f.loops:
            q = head.local(loop.vert.co)
            phi = math.atan2(q.x, -q.y)
            streak = 0.82 + 0.18 * math.sin(phi * 70 + q.z * 40)
            root = 0.75 + 0.25 * smooth(-0.02, 0.06, q.z)
            loop[col] = (base[0] * streak * root, base[1] * streak * root, base[2] * streak * root, 1.0)
    _strand_uv(bm, head, uv)
    return _mesh_from(bm, "hair_shell")


def mane(head, color, length):
    """Long hair falling over the back and shoulders: curved sheets of strands from the back of the scalp."""
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    col = bm.loops.layers.color.new("Color")
    base = hex3(color)
    h = head.h
    rows, cols = 10, 26
    grid = []
    for i in range(rows + 1):
        t = i / rows
        row = []
        for k in range(cols + 1):
            a = math.pi * (0.5 + k / cols)                # from beside one ear, around the back, to the other
            r = (0.086 + 0.022 * t) * h
            drop = length * t
            sway = 0.004 * h * math.sin(k * 1.7 + t * 5)
            p = Vector((head.c.x + r * math.sin(a) + sway, head.c.y + r * math.cos(a) - 0.012 * h - 0.03 * t * t * h,
                        head.c.z + 0.035 * h - drop))
            row.append(bm.verts.new(p))
        grid.append(row)
    for i in range(rows):
        for k in range(cols):
            f = bm.faces.new((grid[i][k], grid[i][k + 1], grid[i + 1][k + 1], grid[i + 1][k]))
            for loop, (a, b) in zip(f.loops, ((i, k), (i, k + 1), (i + 1, k + 1), (i + 1, k))):
                loop[uv].uv = (a / rows * 3.0, b / cols * 6.0)
                shade = 0.8 + 0.2 * math.sin(b * 2.3 + a * 0.4)
                loop[col] = (base[0] * shade, base[1] * shade, base[2] * shade, 1.0)
    return _mesh_from(bm, "hair_mane")


def tail(head, color, style):
    """Ponytail (a tapering, gently curving tube) or bun (a flattened sphere) at the back of the head."""
    h = head.h
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    col = bm.loops.layers.color.new("Color")
    base = hex3(color)
    if style == "bun":
        res = bmesh.ops.create_uvsphere(bm, u_segments=18, v_segments=12, radius=0.034 * h)
        bmesh.ops.scale(bm, vec=(1.0, 0.8, 0.85), verts=res["verts"])
        bmesh.ops.translate(bm, vec=head.at((0, -0.1, 0.05)), verts=res["verts"])
    else:
        seg, rings = 10, 9
        prev = None
        for i in range(rings):
            t = i / (rings - 1)
            c = head.at((0, -0.105 - 0.03 * t, 0.03 - 0.16 * t + 0.02 * t * t))
            r = (0.016 - 0.011 * t) * h
            ring = [bm.verts.new(c + Vector((math.cos(a) * r, math.sin(a) * r * 0.8, 0))) for a in (2 * math.pi * k / seg for k in range(seg))]
            if prev:
                for k in range(seg):
                    bm.faces.new((prev[k], prev[(k + 1) % seg], ring[(k + 1) % seg], ring[k]))
            prev = ring
    for f in bm.faces:
        for loop in f.loops:
            q = head.local(loop.vert.co)
            loop[uv].uv = (-q.z * 12, math.atan2(q.x, q.y) * 1.6)
            loop[col] = (*base, 1.0)
    return _mesh_from(bm, "hair_tail")


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
    return _mesh_from(bm, "beard")


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
        # Upper lashes along the lid margin, curling up and out.
        for k in range(22):
            x = -0.0135 + 0.027 * k / 21
            u = max(0.0, 1.0 - (x / 0.0148) ** 2)
            z = 0.0058 * u ** 0.5 + 0.06 * x + 0.0006
            p = head.at((e.x + sx * x, e.y + 0.0118 * 1.02, e.z + z))
            d = Vector((sx * x * 8, 1.0, 1.3)).normalized()
            strand(p, d, Vector((0, 1, 0.2)).normalized(), 0.0085 * h * (0.7 + 0.3 * u), 0.0012 * h, lash)
    obj = _mesh_from(bm, "brows")
    return obj

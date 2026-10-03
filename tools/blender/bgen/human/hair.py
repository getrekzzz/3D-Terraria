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

from .head import LID_UPPER, smooth
from .paint import hex3


def hairline(q, part=0.0, sideburns=True) -> float:
    """Height (head-local z) of the hairline at the azimuth of q: a widow's-peak forehead that recedes a little at the
    temples, sideburns down in front of the ears, up over the ears and down to the nape. ``part`` (-1..1) lowers the
    hairline on one side of the forehead for a fringe swept across from the parting."""
    phi = math.atan2(q.x, q.y)            # 0 front, +/-pi back; + toward the character's right
    a = abs(phi)
    burn = -0.032 if sideburns else 0.0
    pts = [(0.0, 0.05), (0.35, 0.053), (0.62, 0.044), (0.9, 0.03), (1.08, 0.004), (1.22, burn), (1.33, burn),
           (1.42, 0.006), (1.85, 0.004), (2.3, -0.045), (math.pi, -0.072)]
    z = pts[-1][1]
    for (a0, z0), (a1, z1) in zip(pts, pts[1:]):
        if a <= a1:
            t = (a - a0) / (a1 - a0)
            z = z0 + (z1 - z0) * (t * t * (3 - 2 * t))
            break
    if part and a < 1.0:                  # the swept side of the fringe falls lower over the forehead
        side = 0.5 + 0.5 * math.tanh((-phi * (1 if part > 0 else -1) + 0.15) * 5)
        z -= abs(part) * 0.022 * side * (1 - a / 1.0) ** 0.7
    # Clumped, uneven ends instead of a cut line.
    return z - 0.0035 * (0.5 + 0.5 * math.sin(phi * 31 + 0.7)) * (0.5 + 0.5 * math.sin(phi * 13))


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


STYLES = {   # thickness (head-local), top lift (extra volume at the crown and front), fringe sweep, clumping
    "short": (0.008, 0.007, 0.8, 1.0), "messy": (0.011, 0.008, 0.5, 1.8), "long": (0.009, 0.004, 0.55, 0.8),
    "ponytail": (0.006, 0.002, 0.0, 0.6), "bun": (0.006, 0.002, 0.0, 0.6), "buzz": (0.0025, 0.0, 0.0, 0.2),
}


def scalp_shell(head, head_obj, style, color, under_hat=False):
    """Duplicates the scalp and lifts it into a hair volume: fuller on top, a fringe swept from the parting, clumps
    and strand grooves flowing from the crown, and a hairline that thins out in uneven tufts. Under a hat only the
    hair below the hat band remains."""
    thickness, lift, sweep, clumping = STYLES[style]
    tied = style in ("ponytail", "bun")
    part = sweep
    burns = not (head.frame.female or tied)
    bm = bmesh.new()
    bm.from_mesh(head_obj.data)
    bm.normal_update()
    drop = []
    for f in bm.faces:
        q = head.local(f.calc_center_median())
        line = hairline(q, part, burns) + (0.006 if tied else 0.0)
        if q.z < line - 0.004 or _in_ear(q) or q.z < -0.13 or (under_hat and q.z > 0.045):
            drop.append(f)
    bmesh.ops.delete(bm, geom=drop, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.normal_update()
    for v in [v for v in bm.verts if v.is_boundary]:
        q = head.local(v.co)
        if not _in_ear(q) and q.z > -0.12 and not (under_hat and q.z > 0.03):
            v.co = head.at((q.x, q.y, hairline(q, part, burns) + (0.006 if tied else 0.0) - 0.004))
    bm.normal_update()
    uv = bm.loops.layers.uv.new("UVMap")
    col = bm.loops.layers.color.get("Color") or bm.loops.layers.color.new("Color")
    base = hex3(color)
    crown = Vector((0.012 * (1 if part >= 0 else -1), -0.03, 0.1))
    moves = []
    for v in bm.verts:
        q = head.local(v.co)
        line = hairline(q, part, burns)
        edge = smooth(-0.004, 0.02, q.z - line)            # thins out over the last two centimetres
        flow = q - crown                                     # strands run out from the crown
        phi = math.atan2(flow.x, -flow.y)
        clump = math.sin(phi * 9 + q.z * 60) * math.sin(phi * 4 - q.z * 25)
        groove = math.sin(phi * 38 + 2.0 * math.sin(flow.length * 40))   # strand bundles
        if style == "messy":
            clump += 0.6 * math.sin(phi * 13 + q.z * 90) * math.sin(q.x * 70)
        top = 0.65 + 0.35 * smooth(-0.02, 0.08, q.z)
        quiff = lift * smooth(0.02, 0.07, q.z) * smooth(-0.04, 0.05, q.y)     # volume at the top front
        depth = thickness * top * (1.0 + 0.3 * clumping * clump + 0.12 * groove) + quiff
        if under_hat:                                        # pressed flat under the band
            depth *= 0.45
        # Hair lies forward over the forehead and down at the sides, so lift it along the flow as well as outward.
        sweep_dir = Vector((flow.x, flow.y, min(flow.z, 0.0))).normalized() if flow.length > 1e-6 else Vector()
        moves.append((v, (v.normal * depth + sweep_dir * depth * 0.35 * smooth(0.0, 0.05, q.z - line)) * head.h * edge))
    for v, d in moves:
        v.co += d
    for f in bm.faces:
        for loop in f.loops:
            q = head.local(loop.vert.co)
            flow = q - crown
            phi = math.atan2(flow.x, -flow.y)
            streak = 0.8 + 0.2 * math.sin(phi * 70 + q.z * 40) * math.sin(phi * 23)
            root = 0.72 + 0.28 * smooth(-0.02, 0.07, q.z)
            loop[col] = (base[0] * streak * root, base[1] * streak * root, base[2] * streak * root, 1.0)
    _strand_uv(bm, head, uv)
    return _mesh_from(bm, "hair_shell")


def mane(head, color, length):
    """Long hair falling over the back: a thick, clumped curtain that starts on the scalp shell around the back of the
    head, wraps behind the ears and falls past the shoulders, thinning toward the sides and ending in uneven tips."""
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    col = bm.loops.layers.color.new("Color")
    base = hex3(color)
    h = head.h
    rows, cols = 14, 30
    a0, a1 = math.pi * 0.64, math.pi * 1.36       # from behind one ear, around the back, to the other

    def point(i, k, inner):
        t, u = i / rows, k / cols
        a = a0 + (a1 - a0) * u
        edge = math.sin(math.pi * u) ** 0.6             # thinner toward the sides
        tip = 1.0 - 0.1 * (0.5 + 0.5 * math.sin(k * 2.3)) * (0.5 + 0.5 * math.sin(k * 0.9 + 1))   # uneven ends
        clump = 0.0045 * math.sin(k * 1.9 + t * 3) * edge
        r = 0.083 + 0.004 + 0.009 * min(1.0, t * 4) + clump + 0.016 * t * t   # the top edge tucks under the shell
        if inner:
            r -= (0.004 + 0.009 * (1 - t) ** 0.5) * edge
        z = 0.03 - (length / h) * t * (tip if i == rows else 1.0)
        back = -0.012 - 0.022 * t * t                   # the curtain hangs a little behind the neck
        sway = 0.003 * math.sin(k * 1.7 + t * 5)
        return head.at((r * math.sin(a) * (1.0 - 0.12 * t) + sway, r * math.cos(a) + back, z))

    outer = [[bm.verts.new(point(i, k, False)) for k in range(cols + 1)] for i in range(rows + 1)]
    inner = [[bm.verts.new(point(i, k, True)) if 0 < k < cols else outer[i][k] for k in range(cols + 1)] for i in range(rows + 1)]

    def quad(vs, idx):     # a face (quad or triangle) with strand UVs and clumped shading
        f = bm.faces.new(vs)
        for loop, (a, b) in zip(f.loops, idx):
            loop[uv].uv = (a / rows * 3.0, b / cols * 6.0)
            shade = (0.78 + 0.22 * math.sin(b * 2.3 + a * 0.4)) * (0.8 + 0.2 * a / rows)
            loop[col] = (base[0] * shade, base[1] * shade, base[2] * shade, 1.0)

    for i in range(rows):
        for k in range(cols):
            idx = ((i, k), (i, k + 1), (i + 1, k + 1), (i + 1, k))
            quad([outer[a][b] for a, b in idx], idx)
            # The inner layer, facing in; at the side edges it meets the outer layer, so those faces are triangles.
            ridx = idx[::-1]
            seen, vs, vidx = set(), [], []
            for a, b in ridx:
                if inner[a][b] not in seen:
                    seen.add(inner[a][b])
                    vs.append(inner[a][b])
                    vidx.append((a, b))
            if len(vs) >= 3:
                quad(vs, vidx)
    for k in range(1, cols - 1):                       # close the bottom edge between the layers
        idx = ((rows, k + 1), (rows, k))
        quad([outer[rows][k + 1], outer[rows][k], inner[rows][k], inner[rows][k + 1]], (*idx, *idx[::-1]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
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
        seg, rings = 12, 14
        prev = None
        for i in range(rings):
            t = i / (rings - 1)
            # From the tie at the back of the crown, out and down in a gentle curve, full then tapering to a tip.
            c = head.at((0.004 * math.sin(t * 5), -0.094 - 0.05 * t + 0.02 * t * t, 0.045 - 0.2 * t + 0.03 * t * t))
            r = (0.012 + 0.012 * math.sin(min(1.0, t * 2.2) * math.pi * 0.5) * (1 - t) ** 0.8 + 0.002 * (1 - t)) * h
            if i == 1:
                r *= 0.8                                   # pinched by the hair tie
            ring = [bm.verts.new(c + Vector((math.cos(a) * r, math.sin(a) * r * 0.85, 0))) for a in (2 * math.pi * k / seg for k in range(seg))]
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
            z = LID_UPPER * u ** 0.5 + 0.06 * x + 0.0006
            p = head.at((e.x + sx * x, e.y + 0.0118 * 1.02, e.z + z))
            d = Vector((sx * x * 8, 1.0, 1.3)).normalized()
            strand(p, d, Vector((0, 1, 0.2)).normalized(), 0.0085 * h * (0.7 + 0.3 * u), 0.0012 * h, lash)
    obj = _mesh_from(bm, "brows")
    return obj

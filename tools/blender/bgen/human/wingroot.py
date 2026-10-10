"""Where a wing leaves the back: the root found on the body or the garment over the shoulder blade, a vertical slit
let into that garment for it (the cloth darkened into the opening, its edge turned and hemmed, pointed at both ends and
taller than the root so the cloth is seen parted round it), and the root's own section, flattened and broad where it
grows out of a bare back and tapering into the wing's arm (a fleshy stem swelling out of the slit through cloth).
Tails (tails.py) use the same slit in the seat of the trousers, its hem sewn into the garment itself."""
import math

import bmesh
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

from .features import paint, sweep


GARMENTS = ("shirt", "coat", "robe", "pants", "coat_tails", "robe_skirt")     # what a wing or a tail cuts through


def find(fr, body, garments, sx):
    """(point, outward normal, object hit) on the back over the shoulder blade: the outermost of the body and the
    garments a ray from behind meets."""
    import bpy
    s = fr.s
    origin = Vector((sx * 0.075 * s, -0.6 * s, 1.33 * s))
    best = None
    for o in [body] + list(garments):
        if o.type != "MESH" or not len(o.data.polygons):
            continue
        tree = BVHTree.FromObject(o, bpy.context.evaluated_depsgraph_get())
        loc, nrm, _, _ = tree.ray_cast(origin, Vector((0, 1, 0)), 0.6 * s)
        if loc is not None and (best is None or loc.y < best[0].y):
            best = (loc, nrm, o)
    if best is None:
        return Vector((sx * 0.075 * s, -0.12 * s, 1.33 * s)), Vector((0, -1, 0)), None
    return best[0], best[1].normalized(), best[2]


def skin_tone(body):
    """The body's average painted colour (its skin)."""
    me = body.data
    if "Color" not in me.color_attributes or not len(me.loops):
        return (0.6, 0.45, 0.4)
    data = me.color_attributes["Color"].data
    rgb = np.empty(len(data) * 4, dtype=np.float32)
    data.foreach_get("color_srgb", rgb)
    return tuple(float(v) for v in rgb.reshape(-1, 4)[:, :3].mean(0))


def _axes(normal):
    up = Vector((0, 0, 1))
    up = (up - normal * up.dot(normal)).normalized()
    return up, normal.cross(up).normalized()


def slit(obj, point, normal, s, half_w, half_h, pointed=False):
    """Darkens the garment ``obj`` into a vertical slit round ``point`` (half-width and half-height ``half_w``,
    ``half_h``; ``pointed``: a lens coming to a point at top and bottom) and returns the hem along its edge as a sweep
    in a new bmesh (or None if ``obj`` is bare skin)."""
    if obj is None or obj.name.split(".")[0] not in GARMENTS:
        return None
    up, side = _axes(normal)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    layer = bm.loops.layers.color.get("Color")
    if layer is None:
        bm.free()
        return None
    base = None
    for v in bm.verts:
        d = v.co - point
        if abs(d.dot(normal)) > 0.03 * s:          # (the front of the garment, the far side of the arm)
            continue
        x, z = d.dot(side) / half_w, d.dot(up) / half_h
        if pointed:              # dark right out to the hem: the opening is seen above and below what comes through
            e = max(abs(x) / max(1 - z * z, 1e-3), abs(z))
            k = 1.0 - max(0.0, min(1.0, (e - 0.72) / 0.2))
        else:
            e = math.hypot(x, z)
            k = 1.0 - max(0.0, min(1.0, (e - 0.55) / 0.3))    # dark only well inside, where the root hides it
        if e > 1.25:
            continue
        for loop in v.link_loops:
            c = loop[layer]
            if base is None:
                base = (c[0], c[1], c[2])
            loop[layer] = (c[0] * (1 - 0.88 * k), c[1] * (1 - 0.88 * k), c[2] * (1 - 0.85 * k), c[3])
    bm.to_mesh(obj.data)
    bm.free()
    if base is None:
        return None
    # The turned edge: a flat band round the slit, lying on the cloth (pointed at top and bottom like a seam).
    import bpy
    tree = BVHTree.FromObject(obj, bpy.context.evaluated_depsgraph_get())
    ring = []
    n = 28
    for k in range(n + 1):
        a = 2 * math.pi * k / n
        x, z = math.sin(a) * half_w, math.cos(a) * half_h
        x *= abs(math.sin(a)) if pointed else 1 - 0.35 * abs(math.cos(a)) ** 6        # pinched toward the ends
        q = point + side * x + up * z + normal * 0.02 * s
        hit, nrm, _, _ = tree.ray_cast(q, -normal, 0.06 * s)
        ring.append((hit if hit is not None else q - normal * 0.02 * s) + normal * 0.0016 * s)
    hem = bmesh.new()
    hem.loops.layers.color.new("Color")
    verts = sweep(hem, ring, lambda t, a: (math.cos(a) * 0.0024 * s, math.sin(a) * 0.0042 * s), 6, up=normal,
                  caps=(False, False))
    tone = tuple(c * 0.96 for c in base)                     # the cloth's own colour, turned
    paint(hem, verts, lambda t, a: tone)
    return hem


def crossing(obj, verts, s, pad=0.003):
    """Where a swept part (``verts`` as sweep() returns them) passes out through the garment ``obj``: on each line
    along the part, the point where it leaves the cloth, pushed ``pad`` out from the part. None when it does not
    cross the garment cleanly."""
    import bpy
    tree = BVHTree.FromObject(obj, bpy.context.evaluated_depsgraph_get())
    lines = {}
    for v, t, a in verts:
        lines.setdefault(round(a, 6), []).append((t, v.co.copy()))
    ring = []
    for a in sorted(lines):
        prev = None
        for t, co in sorted(lines[a], key=lambda x: x[0]):
            loc, nrm, _, _ = tree.find_nearest(co, 0.08 * s)
            if loc is None:
                prev = None
                continue
            sd = (co - loc).dot(nrm)
            if prev is not None and prev[0] < 0 <= sd:
                ring.append(prev[1].lerp(co, -prev[0] / (sd - prev[0])))
                break
            prev = (sd, co)
    if len(ring) < 6:
        return None
    c = sum(ring, Vector()) / len(ring)
    return [p + (p - c).normalized() * pad * s for p in ring]


def lens(obj, ring, normal, s, stretch=1.45):
    """A pointed vertical slit() round the opening ``ring`` (crossing()), ``stretch`` times as tall as it, so the cloth
    is seen parted above and below what comes through it; the hem fits round it at the sides."""
    if obj is None or obj.name.split(".")[0] not in GARMENTS:
        return None
    up, side = _axes(normal)
    c = sum(ring, Vector()) / len(ring)
    half_h = stretch * max(abs((p - c).dot(up)) for p in ring)
    half_w = max(abs((p - c).dot(side)) / (1 - ((p - c).dot(up) / half_h) ** 2) for p in ring) + 0.001 * s
    return slit(obj, c, normal, s, half_w, half_h, pointed=True)


def slit_ring(obj, ring, normal, s):
    """Like slit(), for an opening of any outline ``ring`` (crossing()): darkens the garment just inside it, where
    the part covers it, and returns the hem lying on the cloth along it (or None if ``obj`` is bare skin)."""
    if obj is None or obj.name.split(".")[0] not in GARMENTS:
        return None
    import bpy
    up, side = _axes(normal)
    c = sum(ring, Vector()) / len(ring)
    polar = sorted((math.atan2((p - c).dot(up), (p - c).dot(side)), (p - c).length) for p in ring)

    def edge(ang):
        best = min(polar, key=lambda q: abs((q[0] - ang + math.pi) % (2 * math.pi) - math.pi))
        return best[1]
    reach = max(r for _, r in polar)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    layer = bm.loops.layers.color.get("Color")
    if layer is None:
        bm.free()
        return None
    base = None
    for v in bm.verts:
        d = v.co - c
        if abs(d.dot(normal)) > 0.03 * s or d.length > 1.3 * reach:
            continue
        e = d.length / max(edge(math.atan2(d.dot(up), d.dot(side))), 1e-6)
        k = 1.0 - max(0.0, min(1.0, (e - 0.4) / 0.25))
        for loop in v.link_loops:
            col = loop[layer]
            if base is None:
                base = (col[0], col[1], col[2])
            loop[layer] = (col[0] * (1 - 0.88 * k), col[1] * (1 - 0.88 * k), col[2] * (1 - 0.85 * k), col[3])
    bm.to_mesh(obj.data)
    bm.free()
    if base is None:
        return None
    tree = BVHTree.FromObject(obj, bpy.context.evaluated_depsgraph_get())
    lying = []
    for p in ring + ring[:1]:
        loc, nrm, _, _ = tree.find_nearest(p, 0.05 * s)
        lying.append(p if loc is None else loc + nrm * 0.0016 * s)
    hem = bmesh.new()
    hem.loops.layers.color.new("Color")
    verts = sweep(hem, lying, lambda t, a: (math.cos(a) * 0.0024 * s, math.sin(a) * 0.0042 * s), 6, up=normal,
                  caps=(False, False))
    tone = tuple(x * 0.96 for x in base)
    paint(hem, verts, lambda t, a: tone)
    return hem


def section(s, r_arm, u_skin, u_joint, flat=0.85, broad=True):
    """The root's ring offsets: up to ``u_skin`` (where it leaves the back) it is broad and flattened vertically, and
    it narrows to the arm's radius ``r_arm(u)`` by ``u_joint``; ``a`` = 0 points up. Not ``broad`` (coming out
    through cloth) it is a fleshy stem, a little taller than wide and swelling just where it leaves the slit (lens()),
    so the opening stays small."""
    def fn(u, a):
        r = r_arm(u)
        if u < u_joint:
            k = 1.0 - max(0.0, (u - u_skin) / (u_joint - u_skin)) if u > u_skin else 1.0
            k = k * k * (3 - 2 * k)
            if not broad:
                swell = 1 + 0.16 * math.exp(-((u - u_skin) / (0.35 * (u_joint - u_skin))) ** 2)
                return math.cos(a) * r * (1 + 0.38 * k) * swell, math.sin(a) * r * swell
            r_up = r * (1 + 1.25 * k)
            r_side = r * (1 + 0.15 * k) * (1 - flat * 0.25 * k)
            return math.cos(a) * r_up, math.sin(a) * r_side
        return math.cos(a) * r, math.sin(a) * r
    return fn


def sew(obj, hem):
    """Adds a slit's hem (a bmesh from slit()) to the garment ``obj`` itself, each vertex weighted like the cloth
    nearest it, so it moves with the garment and needs no object of its own."""
    from mathutils.kdtree import KDTree
    from .rig import read_weights
    me = obj.data
    index = {g.name: g.index for g in obj.vertex_groups}
    weights = read_weights(obj)
    kd = KDTree(len(me.vertices))
    for i, v in enumerate(me.vertices):
        kd.insert(v.co, i)
    kd.balance()
    bm = bmesh.new()
    bm.from_mesh(me)
    layer, deform = bm.loops.layers.color.get("Color"), bm.verts.layers.deform.verify()
    src = hem.loops.layers.color.get("Color")
    made = {}
    for v in hem.verts:
        made[v] = nv = bm.verts.new(v.co)
        for name, w in weights[kd.find(v.co)[1]].items():
            nv[deform][index[name]] = w
    for f in hem.faces:
        nf = bm.faces.new([made[lp.vert] for lp in f.loops])
        nf.smooth = True
        for lp, nl in zip(f.loops, nf.loops):
            nl[layer] = lp[src]
    bm.to_mesh(me)
    bm.free()
    hem.free()

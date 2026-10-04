"""Square patches sewn over the scarecrow's clothes (scarecrow.py; ``patches``: a list of colours, default tan,
blue, green, red and tan again): each a small grid of cloth laid onto the garment by rays a little off its surface,
squarish but crooked, its corners and edges frayed a little, plaid or plain, with a running stitch of coarse thread
round it. Every patch takes the weights of the body under it, so it moves with the cloth.
"""
import math

import bmesh
import numpy as np
from mathutils import Vector

from . import matter

COLORS = ("#cdb47a", "#3e5878", "#66763a", "#a4462e", "#c8b07a")
# (garment, joint, offset from it / s, the side the cloth faces there, size / s, turn in degrees, plaid)
PLACES = (("shirt", "chest", (0.07, 0.0, 0.035), (0, 1, 0), 0.075, 8, True),
          ("shirt", "elbow.L", (0.0, 0.0, 0.0), (-0.45, -1, 0.1), 0.06, -12, False),
          ("shirt", "spine", (0.02, 0.0, 0.11), (0, -1, 0), 0.09, 5, False),
          ("pants", "knee.L", (0.0, 0.0, 0.05), (0, 1, 0), 0.08, -7, False),
          ("pants", "hip.R", (-0.03, 0.0, -0.075), (0, -1, 0), 0.085, 6, True))


def _tree(obj):
    from mathutils.bvhtree import BVHTree
    return BVHTree.FromPolygons([v.co.copy() for v in obj.data.vertices], [tuple(p.vertices) for p in obj.data.polygons])


def _patch(bm, col, tree, loc, n, size, turn, color, plaid, thread, rng, s):
    """One patch on the cloth round ``loc`` (facing ``n``), ``size`` across; its vertex colours go in ``col``."""
    up = Vector((0, 0, 1)) if abs(n.z) < 0.9 else Vector((0, 1, 0))
    t1 = (up - n * up.dot(n)).normalized()
    t2 = n.cross(t1)
    a = math.radians(turn)
    t1, t2 = t1 * math.cos(a) + t2 * math.sin(a), t2 * math.cos(a) - t1 * math.sin(a)

    def lay(x, y, lift):
        p = loc + t2 * x + t1 * y
        hit, hn, _, _ = tree.ray_cast(p + n * 0.04 * s, -n, 0.1 * s)
        if hit is None:                     # past the cloth's edge as seen along n: the nearest point of it
            hit, hn, _, _ = tree.find_nearest(p)
        return hit + hn * lift
    G = 10
    c = matter.hexrgb(color)
    top, bottom = [], []
    for i in range(G + 1):
        for j in range(G + 1):
            x, y = (i / G - 0.5) * size, (j / G - 0.5) * size
            if i in (0, G) or j in (0, G):             # crooked, frayed edges
                x, y = x + rng.normal(0, 0.0012) * s, y + rng.normal(0, 0.0012) * s
            top.append(bm.verts.new(lay(x, y, 0.0026 * s)))
            bottom.append(bm.verts.new(lay(x, y, 0.0008 * s)))
            u, v = i / G, j / G
            if plaid:
                k = 1 - 0.18 * (math.cos(u * 2 * math.pi * 3) > 0.55) - 0.18 * (math.cos(v * 2 * math.pi * 3) > 0.55)
            else:
                k = 0.9 + 0.12 * rng.random()
            edge = 0.86 if i in (0, G) or j in (0, G) else 1.0
            col[top[-1]] = col[bottom[-1]] = tuple(c * k * edge)
    idx = lambda i, j: i * (G + 1) + j
    faces = []
    for i in range(G):
        for j in range(G):
            q = (idx(i, j), idx(i + 1, j), idx(i + 1, j + 1), idx(i, j + 1))
            faces.append(bm.faces.new([top[k] for k in q]))
            faces.append(bm.faces.new([bottom[k] for k in reversed(q)]))
    rim = [idx(i, 0) for i in range(G)] + [idx(G, j) for j in range(G)] + [idx(i, G) for i in range(G, 0, -1)] + [idx(0, j) for j in range(G, 0, -1)]
    for k in range(len(rim)):
        a0, a1 = rim[k], rim[(k + 1) % len(rim)]
        faces.append(bm.faces.new((bottom[a0], bottom[a1], top[a1], top[a0])))
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    # a running stitch round it, a little in from the edge
    inset, dash, step = size * 0.38, 0.008 * s, 0.014 * s
    edges = [((-1, -1), (1, -1)), ((1, -1), (1, 1)), ((1, 1), (-1, 1)), ((-1, 1), (-1, -1))]
    for (x0, y0), (x1, y1) in edges:
        count = int(2 * inset / step)
        for m in range(count):
            u0 = (m + 0.5) / count - dash / (4 * inset)
            u1 = u0 + dash / (2 * inset)
            pts = [lay((x0 + (x1 - x0) * u) * inset, (y0 + (y1 - y0) * u) * inset, 0.0036 * s) for u in (u0, (u0 + u1) / 2, u1)]
            n0 = len(bm.verts)
            matter.tube(bm, pts, 0.0013 * s, 4)
            bm.verts.ensure_lookup_table()
            for v in bm.verts[n0:]:
                col[v] = thread


def sew(fr, cloth, colors, lookup):
    """The patches on whichever of their garments are worn, as one object."""
    from .garments import mesh_from
    s, j = fr.s, fr.j
    colors = list(colors) if isinstance(colors, (list, tuple)) and colors else list(COLORS)
    rng = np.random.default_rng(matter.seed_of("patches"))
    bm = bmesh.new()
    layer = bm.loops.layers.color.new("Color")
    col, trees = {}, {}
    for k, (name, joint, off, facing, size, turn, plaid) in enumerate(PLACES):
        if name not in cloth:
            continue
        tree = trees.setdefault(name, _tree(cloth[name]))
        n0 = Vector(facing).normalized()
        aim = j[joint] + Vector(off) * s
        loc, n, _, _ = tree.ray_cast(aim + n0 * 0.4 * s, -n0, 0.8 * s)
        if loc is None:
            continue
        color = colors[k % len(colors)]
        thread = (0.86, 0.8, 0.64) if sum(matter.hexrgb(color)) < 1.2 else (0.2, 0.14, 0.09)
        _patch(bm, col, tree, loc, n, size * s, turn, color, plaid, thread, rng, s)
    if not bm.verts:
        bm.free()
        return []
    for f in bm.faces:
        for loop in f.loops:
            loop[layer] = tuple(col.get(loop.vert, (0.5, 0.5, 0.5))) + (1.0,)
    obj = mesh_from(bm, "patches")
    matter.copy_weights(obj, lookup)
    matter.slot(obj, "char:cloth")
    return [obj]

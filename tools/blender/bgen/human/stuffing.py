"""What makes the scarecrow (scarecrow.py) read as stuffed rather than a man in a sack mask: its clothes pushed out
in soft lumps where the straw packs them (one field for every garment, so the shirt stays over the trousers, fading
out toward the hems, cuffs and collar so the bands there still meet them), each sleeve cinched by a twine tie above
its cuff and ballooning over it, and hands of straw in place of the gloves:
a bundle spreading from inside each cuff over the palm and splitting into five tufts along the fingers, each stalk
carried by the hand and finger bones it lies along. Seeded, so one name always gives one mesh.
"""
import bmesh
import numpy as np
from mathutils import Vector

from . import matter


def _ties(fr):
    """Where each sleeve is tied: (centre, axis along the arm) per side."""
    out = {}
    for side in ("R", "L"):
        sh, wr = fr.j[f"shoulder.{side}"], fr.j[f"wrist.{side}"]
        out[side] = (sh + (wr - sh) * 0.9, (wr - sh).normalized())
    return out


def lumps(fr, garments):
    """Pushes the garments (objects) out in lumps, in place, the shirt's sleeves pinched in at their ties. Every
    garment fades out near every garment's open edges, so where the shirt's hem lies over the trousers neither
    moves and the trousers cannot come through it."""
    from mathutils.kdtree import KDTree
    s = fr.s
    rims = []
    for obj in garments:
        faces_per_edge = {}
        for poly in obj.data.polygons:
            for k in poly.edge_keys:
                faces_per_edge[k] = faces_per_edge.get(k, 0) + 1
        co = matter.coords(obj)
        rims += [co[v] for v in sorted({v for k, c in faces_per_edge.items() if c == 1 for v in k})]
    tree = KDTree(max(len(rims), 1))
    for i, c in enumerate(rims):
        tree.insert(c, i)
    tree.balance()
    for obj in garments:
        me = obj.data
        co, n = matter.coords(obj), matter.normals(obj)
        fade = matter.ss(0.012 * s, 0.07 * s, np.asarray([tree.find(c)[2] for c in co])) if rims else np.ones(len(co))
        q = co / s
        lump = np.clip((matter.fbm(q, 4.6, 3, 91) - 0.34) / 0.5, 0, 1) ** 1.3
        sag = matter.noise(q * np.asarray((1.0, 1.0, 0.35)), 14.0, 93)      # (long soft creases where it hangs slack)
        d = (0.004 + 0.03 * lump - 0.005 * sag) * s * fade
        if obj.name.split(".")[0] == "shirt":
            for c, a in _ties(fr).values():
                rel = co - np.asarray(tuple(c))
                h = rel @ np.asarray(tuple(a))
                rad = np.linalg.norm(rel - h[:, None] * np.asarray(tuple(a)), axis=1)
                near = rad < 0.075 * s
                d = d + near * s * (0.012 * np.exp(-((h + 0.04 * s) / (0.035 * s)) ** 2)        # (ballooning above)
                                    - 0.016 * np.exp(-(h / (0.009 * s)) ** 2))                # (cinched under the tie)
        me.vertices.foreach_set("co", (co + n * d[:, None]).astype(np.float32).ravel())
        me.update()


def straw_hands(fr, parts, color):
    """Takes the hands (and their nails) out of ``parts`` and returns one object of straw hands in their place."""
    import bpy
    from .garments import mesh_from
    from .hands import hand_axes
    from .rig import write_weights
    from .straw import _shade, strand
    for o in parts["hands"] + parts["nails"]:
        bpy.data.objects.remove(o, do_unlink=True)
    parts["hands"], parts["nails"] = [], []
    j, s = fr.j, fr.s
    body = parts["body"]                    # (and the stump of the arm the hand was on, out of the cuff)
    co = matter.coords(body)
    drop = np.zeros(len(co), dtype=bool)
    for side in ("R", "L"):
        wr, along = np.asarray(tuple(j[f"wrist.{side}"])), np.asarray(tuple(hand_axes(fr, side)[0]))
        rel = co - wr
        drop |= (rel @ along > -0.03 * s) & (np.linalg.norm(rel, axis=1) < 0.09 * s)
    if drop.any():
        bm = bmesh.new()
        bm.from_mesh(body.data)
        bm.verts.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[bm.verts[i] for i in np.flatnonzero(drop)], context="VERTS")
        bm.to_mesh(body.data)
        bm.free()
    rng = np.random.default_rng(matter.seed_of("straw hands"))
    base = matter.hexrgb(color)
    bm = bmesh.new()
    layer = bm.loops.layers.color.new("Color")
    weights = []

    def add(start, d, length, chain, sag=0.06):
        v0 = len(bm.verts)
        strand(bm, layer, start, d, length, rng.uniform(0.0019, 0.0026) * s, _shade(base, rng), rng, sag)
        bm.verts.ensure_lookup_table()
        for i in range(v0, len(bm.verts)):
            weights.append(_along(bm.verts[i].co, chain))

    for side in ("R", "L"):
        along, thumb, dorsal = hand_axes(fr, side)
        wr = j[f"wrist.{side}"]
        palm = [(wr - along * 0.05 * s, f"hand.{side}"), (wr + along * 0.2 * s, f"hand.{side}")]
        for _ in range(40):                 # the palm: from deep in the cuff (filling it) out to the knuckles
            a, r = rng.uniform(0, 2 * np.pi), np.sqrt(rng.uniform(0, 1)) * 0.026
            start = wr - along * 0.05 * s + (thumb * np.cos(a) + dorsal * np.sin(a)) * r * s
            end = wr + along * rng.uniform(0.07, 0.095) * s + (thumb * rng.uniform(-0.036, 0.036) + dorsal * rng.uniform(-0.012, 0.01)) * s
            add(start, end - start, (end - start).length * rng.uniform(0.95, 1.1), palm, sag=0.04)
        for f in ("thumb", "index", "middle", "ring", "pinky"):
            pts = [j[f"{f}{i}.{side}"] for i in (1, 2, 3)] + [j[f"{f}_tip.{side}"]]
            chain = [(wr, f"hand.{side}")] + [(pts[i], f"{f}.0{i + 1}.{side}") for i in range(3)] + [(pts[3], f"{f}.03.{side}")]
            for _ in range(4 if f == "thumb" else 5):
                jit = Vector(tuple(rng.normal(0, 1, 3))) * 0.004 * s
                start = pts[0] - (pts[1] - pts[0]).normalized() * 0.025 * s + jit
                d = (pts[3] - start).normalized() + Vector(tuple(rng.normal(0, 0.08, 3)))
                add(start, d, (pts[3] - start).length * rng.uniform(0.92, 1.18), chain)
    obj = mesh_from(bm, "straw_hands")
    write_weights(obj, weights)
    matter.slot(obj, "char:cloth")
    return obj


def _along(co, chain):
    """Weights for a point along a chain of (joint, bone carrying the segment from it): the bone of the segment it
    lies along, blending into the next over the joints."""
    best, w = None, {}
    for i in range(len(chain) - 1):
        a, b = chain[i][0], chain[i + 1][0]
        ab = b - a
        L2 = max(ab.length_squared, 1e-9)
        t = max(0.0, min(1.0, (co - a).dot(ab) / L2))
        dist = (co - (a + ab * t)).length
        if best is None or dist < best:
            best = dist
            nxt = chain[i + 1][1] if i + 2 < len(chain) else chain[i][1]
            blend = max(0.0, (t - 0.8) / 0.2) * 0.5
            w = {chain[i][1]: 1 - blend}
            if blend > 0 and nxt != chain[i][1]:
                w[nxt] = blend
    return w


def cuff_ties(fr, shirt, color):
    """Twine tied round each sleeve in its pinch, knotted on the outside, two short ends hanging."""
    from .garments import mesh_from
    from .rig import write_weights
    from .rope import end, knot, twisted
    from .straw import _rim, _ring
    s = fr.s
    r = 0.0055 * s
    bm = bmesh.new()
    layer = bm.loops.layers.color.new("Color")
    groups = []
    for k, (side, (c, a)) in enumerate(_ties(fr).items()):
        v0 = len(bm.verts)
        R = _rim(shirt, c, a, 0.004 * s) + 0.3 * r
        e1, e2 = _ring(a)
        sx = 1 if side == "R" else -1
        out = Vector((sx, 0, 0))
        out = (out - a * out.dot(a)).normalized()
        ang = [2 * np.pi * i / 40 for i in range(41)]
        twisted(bm, layer, [c + (e1 * np.cos(t) + e2 * np.sin(t)) * R for t in ang], r, color)
        knot(bm, layer, c + out * R, a.cross(out).normalized(), out, r, color)
        for i, w in enumerate((-1, 1)):
            p0 = c + out * (R + 1.2 * r) + a.cross(out).normalized() * w * 0.006 * s
            end(bm, layer, [p0 + (a * 0.6 + out * 0.4) * 0.012 * s * t + Vector((0, 0, -0.004 * s * t)) for t in range(4)],
                r * 0.85, color, 61 + 2 * k + i)
        groups.append((v0, len(bm.verts), f"forearm.{side}"))
    obj = mesh_from(bm, "cuff_ties")
    weights = [None] * len(obj.data.vertices)
    for v0, v1, bone in groups:
        for i in range(v0, v1):
            weights[i] = {bone: 1.0}
    write_weights(obj, weights)
    matter.slot(obj, "char:cloth")
    return obj

"""A scarecrow (``sack``: the colour of the burlap sack it has for a head). The sack takes the place of the head and
eyes: stuffed round and lumpy, tucked up under a wide hat, gathered into folds and tied round the neck with a rope,
its ragged skirt below the tie lying over the collar. Two slanted holes are cut in it, black inside with an ember
glowing deep in each (``eyes``), and its mouth is a crooked line stitched shut with coarse thread. Gloves are the
skin (``skin``, ``nails``) and there are no brows (``brows: none``).

The rest of it: straw.py (straw sticking out of the cuffs, the trouser hems, the collar and a few holes, ``straw``),
patches.py (square patches sewn over the clothes, ``patches``) and rope.py (a rope for a belt and for the tie,
``rope``). Everything here is in head-local units (head.at) and seeded, so one name always gives one mesh.
"""
import bpy
import numpy as np

from . import fieldmesh, matter
from .sdf import F, ellipsoid, round_cone, smin

SOURCES = ("scarecrow.py",)
TIE_Z, TIE_R, HEM = -0.138, 0.052, -0.2          # the tie's height and radius, the skirt's hem (head-local)
EYE = ((0.066, 0.02), (0.014, 0.006), (0.041, -0.026))      # one eye hole's corners (x, z): outer, inner, bottom


def _eye(q, sx):
    """Distance in the face's plane (x, z) to one slanted, ragged three-cornered eye hole (negative inside)."""
    x, z = q[:, 0] * F(sx), q[:, 2]
    d = None
    for i in range(3):
        (ax, az), (bx, bz) = EYE[i], EYE[(i + 1) % 3]
        L = np.hypot(bx - ax, bz - az)
        e = (x - F(ax)) * F((bz - az) / L) - (z - F(az)) * F((bx - ax) / L)
        d = e if d is None else np.maximum(d, e)
    return d + F(0.0018) * (matter.noise(q, 260.0, 11) - F(0.5))


def _mouth_z(x):
    return -0.066 + 0.022 * (x / 0.05) ** 2 + 0.004 * np.sin(x * 70.0)      # a crooked grin


def _fold(q):
    th = np.arctan2(q[:, 0], q[:, 1] + F(0.016))
    return F(1) - np.abs(np.sin(th * F(6) + F(2.2) * matter.noise(q, 22.0, 4))), th


def sack_field(hat=None):
    """The stuffed sack in head-local units; with ``hat`` (hats.Hat) kept inside the hat's crown above its band."""
    def field(q):
        x, y, z = q[:, 0], q[:, 1], q[:, 2]
        bag = ellipsoid(q, (0, 0.004, -0.014), (0.088, 0.103, 0.116)) + F(0.005) * (matter.fbm(q, 14.0, 2, 3) - F(0.5))
        d = smin(bag, round_cone(q, (0, -0.012, -0.1), (0, -0.016, TIE_Z), 0.066, TIE_R - 0.004), 0.03)
        fold, th = _fold(q)
        d = d + F(0.008) * fold * matter.ss(-0.05, TIE_Z + 0.004, z).astype(F)       # folds running into the tie
        # the skirt below the tie: flaring out fast over the collar, then lying on the shoulders
        u = np.clip((F(TIE_Z) - z) / F(TIE_Z - HEM), 0, 1)
        rho = np.hypot(x, y + F(0.018)) - F(0.006) * fold
        k = F(1) / np.sqrt(F(1) + (F(0.148 / (TIE_Z - HEM)) * (F(1) - u)) ** 2)
        skirt = (rho - (F(TIE_R - 0.006) + F(0.074) * (F(1) - (F(1) - u) ** 2))) * k
        ring = np.stack([np.cos(th), np.sin(th), np.zeros_like(th)], 1)
        hem = F(HEM) + F(0.012) * (matter.noise(ring, 2.2, 7) - F(0.5))
        skirt = np.maximum(np.abs(skirt + F(0.0024)) - F(0.0024), np.maximum(hem - z, z - F(TIE_Z + 0.006)))
        front = F(0.03) - y
        for sx in (1, -1):                           # eye holes: pockets 2.2 cm deep under the surface
            d = np.maximum(d, -np.maximum(np.maximum(_eye(q, sx), -d - F(0.022)), front))
        groove = np.maximum(np.abs(z - _mouth_z(x).astype(F)) - F(0.0024), np.abs(x) - F(0.05))
        d = np.maximum(d, -np.maximum(np.maximum(groove, -d - F(0.0045)), front))
        d = np.minimum(d, skirt)
        if hat is not None:                          # tucked up inside the hat's crown
            t = matter.ss(-0.03, -0.004, hat.local(q)[:, 2]).astype(F)
            d = d * (F(1) - t) + np.maximum(d, hat.inner(q) + F(0.003)) * t
        return d
    return field


def _hat(head, p):
    if p.get("hat") != "wide":
        return None
    from . import hats
    return hats.Hat(p.get("hatShape") or "brim", head.shape.fem, 0.004 if p.get("hairStyle") in ("bald", "buzz") else 0.012)


def sack(head, p, under):
    """The sack's mesh on the head, its skirt draped over ``under`` (the neck and the clothes), painted burlap."""
    from .shellmesh import place
    hat = _hat(head, p)
    field = sack_field(hat)
    key = ("sack", None if hat is None else (hat.shape, hat.clear), head.shape.fem)
    obj = fieldmesh.build(field, ((-0.15, -0.15, -0.22), (0.15, 0.16, 0.14)), 0.0015, 11000, "sack", key, SOURCES, "matter")
    q = place(obj, head).astype(float)
    _drape(obj, head, under)
    fold, _ = _fold(q.astype(F))
    base = matter.hexrgb(p.get("sack", "#a8875a"))
    mottle = (0.86 + 0.24 * matter.fbm(q, 30.0, 3, 21)) * (0.92 + 0.16 * matter.noise(q, 420.0, 27))       # coarse grain
    stain = 1 - 0.22 * matter.ss(0.55, 0.75, matter.fbm(q, 9.0, 2, 23))
    weave = 0.95 + 0.1 * matter.noise(q * np.asarray((1.0, 1.0, 9.0)), 160.0, 25)       # coarse threads
    crease = 1 - 0.3 * fold * matter.ss(-0.07, TIE_Z, q[:, 2]) - 0.2 * matter.ss(-0.15, -0.19, q[:, 2])
    d = field(q.astype(F))
    hole = np.zeros(len(q))
    for sx in (1, -1):
        hole = np.maximum(hole, matter.ss(0.002, -0.004, _eye(q.astype(F), sx)) * (q[:, 1] > 0.03))
    groove = (np.abs(q[:, 2] - _mouth_z(q[:, 0])) < 0.004) & (np.abs(q[:, 0]) < 0.05) & (q[:, 1] > 0.03)
    shade = (mottle * stain * weave * crease)[:, None] * (1 - 0.9 * hole[:, None]) * np.where(groove, 0.45, 1.0)[:, None]
    matter.paint(obj, base * shade + 0 * d[:, None])
    matter.slot(obj, "char:cloth")
    return obj, field


def _drape(obj, head, under, gap=0.0055):
    """The skirt below the tie laid over whatever it would sink into (the neck, the collar, the shoulders)."""
    from mathutils.bvhtree import BVHTree
    verts, polys = [], []
    for o in under:
        base = len(verts)
        verts += [v.co.copy() for v in o.data.vertices]
        polys += [tuple(base + i for i in poly.vertices) for poly in o.data.polygons]
    tree = BVHTree.FromPolygons(verts, polys)
    g = gap * head.h
    for v in obj.data.vertices:
        if head.local(v.co).z > TIE_Z - 0.008:
            continue
        loc, n, _, dist = tree.find_nearest(v.co, 0.05 * head.h)
        if loc is not None and ((v.co - loc).dot(n) < 0 or dist < g):
            v.co = loc + n * g
    obj.data.update()


def stitches(head, field, color):
    """The mouth stitched shut: a thread along the groove and coarse stitches across it, on the sack's surface."""
    import bmesh
    from .garments import colorize, mesh_from

    def on(points, lift=0.0016):
        pts = np.asarray(points, dtype=float)
        hit = matter.onto(field, pts + np.asarray((0, 0.12, 0)), np.tile((0, -1.0, 0), (len(pts), 1)), 0.2)
        return [head.at(tuple(p + np.asarray((0, lift, 0)))) for p in hit]
    bm = bmesh.new()
    xs = np.linspace(-0.052, 0.052, 15)
    matter.tube(bm, on([(x, 0, _mouth_z(x)) for x in xs], 0.0005), 0.0018 * head.h, 5)
    for x in np.linspace(-0.042, 0.042, 7):
        tilt = 0.004 * np.sin(x * 300)
        zc = _mouth_z(x)
        matter.tube(bm, on([(x - tilt, 0, zc + 0.012), (x, 0, zc), (x + tilt, 0, zc - 0.012)]), 0.0021 * head.h, 5)
    colorize(bm, color)
    obj = mesh_from(bm, "sack_stitches")
    matter.slot(obj, "char:cloth")
    return obj


def embers(head, field, color):
    """An ember at the back of each eye hole."""
    import bmesh
    from .garments import mesh_from
    bm = bmesh.new()
    for sx in (1, -1):
        cx, cz = sum(c[0] for c in EYE) / 3 * sx, sum(c[1] for c in EYE) / 3
        hit = matter.onto(field, np.asarray([(cx, 0.15, cz)]), np.asarray([(0, -1.0, 0)]), 0.2)[0]
        res = bmesh.ops.create_icosphere(bm, subdivisions=2, radius=1.0)
        bmesh.ops.scale(bm, vec=(0.009 * head.h, 0.005 * head.h, 0.007 * head.h), verts=res["verts"])
        bmesh.ops.translate(bm, vec=head.at((cx, float(hit[1]) - 0.001, cz - 0.002)), verts=res["verts"])
    obj = mesh_from(bm, "eye_glow")
    matter.paint(obj, np.tile(matter.hexrgb(color), (len(obj.data.vertices), 1)))
    matter.slot(obj, "char:glow")
    return obj


def dress(fr, head, parts, p, worn, lookup):
    """Hook at the end of human.dress: the sack replaces the head and its eyes, straw, patches and a rope belt join
    the clothes (the leather belt goes). Returns the new list of worn objects."""
    from . import patches, rope, straw
    from .rig import head_weights
    tree, body_w = lookup
    cloth = {o.name.split(".")[0]: o for o in worn if o.type == "MESH"}
    under = [parts["body"]] + [o for o in worn if o.type == "MESH" and not o.name.startswith(("hat", "belt", "buckle"))]
    for o in [parts["head"]] + list(parts["eyes"]):
        bpy.data.objects.remove(o, do_unlink=True)
    obj, field = sack(head, p, under)
    parts["head"], parts["eyes"] = obj, []
    rope_color = p.get("rope", "#9a8458")
    extra = [stitches(head, field, "#2a1c12"), embers(head, field, p.get("eyes", "#ff8a2a"))]
    extra += rope.tie(head, field, rope_color, TIE_Z)
    for o in [obj] + extra:
        head_weights(o, head, tree, body_w, torso_only=True)
    belt = [o for o in worn if o.name.split(".")[0] in ("belt", "belt_tongue", "buckle")]
    for o in belt:
        bpy.data.objects.remove(o, do_unlink=True)
    worn = [o for o in worn if all(o is not b for b in belt)]
    if belt:
        extra += rope.belt(fr, p, [cloth[k] for k in ("shirt", "pants", "coat") if k in cloth], rope_color)
    extra += patches.sew(fr, cloth, p.get("patches"), lookup)
    extra += straw.stuff(fr, head, obj, cloth, p.get("straw", "#d8b45a"), lookup)
    return worn + extra

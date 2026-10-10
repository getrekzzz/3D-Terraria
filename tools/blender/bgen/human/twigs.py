"""The splinterling's twigs (woodfolk.py): twig fingers (a knotted palm and four crooked, knuckled twigs tapering to
points), branches forking out of the crown and one shoulder with a few autumn leaves and a broken stub on the other
shoulder, pale splinters sticking out of the bark, and the embers deep in the eye hollows."""
import math

import bmesh
import numpy as np
from mathutils import Vector

from . import matter
from .features import blade, spline
from .matter import F
from .sdf import ellipsoid, round_cone, smin, sphere

SOURCES = ("twigs.py", "woodfolk.py")
LEAVES = ((0.62, 0.42, 0.12), (0.78, 0.36, 0.1), (0.46, 0.5, 0.16), (0.7, 0.56, 0.18))


def hand_field(fr, side):
    from .hands import hand_axes
    j, s = fr.j, fr.s
    V = lambda v: np.asarray(tuple(v), dtype=F)
    along, thumb, dorsal = (np.asarray(tuple(v)) for v in hand_axes(fr, side))
    rot = np.stack([along, thumb, dorsal], 1)
    wr = V(j[f"wrist.{side}"])

    def field(p):
        d = ellipsoid(p, wr + V(along * 0.042 * s), (0.05 * s, 0.034 * s, 0.02 * s), rot)
        d = smin(d, round_cone(p, wr - V(along * 0.02 * s), wr + V(along * 0.02 * s), 0.022 * s, 0.024 * s), 0.01 * s)
        for f, r in (("thumb", 0.0095), ("index", 0.0088), ("middle", 0.009), ("ring", 0.0084)):
            pts = [V(j[f"{f}{i}.{side}"]) for i in (1, 2, 3)] + [V(j[f"{f}_tip.{side}"])]
            pts = [pts[0]] + [pts[0] + (x - pts[0]) * F(1.3) for x in pts[1:]]     # long twigs
            tip = pts[2] + (pts[3] - pts[2]) * 2.1          # with long, pointed ends
            pts = [pts[0] - V(along * 0.02 * s)] + pts[1:3] + [tip]       # rooted well inside the palm
            radii = (r * 1.3, r * 1.05, r * 0.82, 0.0018)
            for i in range(3):
                d = smin(d, round_cone(p, pts[i], pts[i + 1], radii[i] * s, radii[i + 1] * s), (0.008 if i == 0 else 0.004) * s)
            for i in (1, 2):
                d = smin(d, sphere(p, pts[i], radii[i] * 1.18 * s), 0.003 * s)
        return d + F(0.0005) * (matter.noise(p, 300.0, 7 if side == "R" else 8) - F(0.5))

    lo = np.asarray(tuple(j[f"wrist.{side}"])) - 0.2 * s
    return field, (tuple(lo), tuple(lo + 0.4 * s))


def hands(fr, arm, lookup, base):
    from .rig import chain_weights, hand_bones
    out = []
    for side in ("R", "L"):
        field, bounds = hand_field(fr, side)
        obj = matter.mesh(field, bounds, 0.0009 * fr.s, 3600, f"twig_hand_{side}", ("twigs", matter.frame_key(fr)), SOURCES)
        chain_weights(obj, arm, hand_bones(side), 0.006 * fr.s, *lookup, blend_bone=f"hand.{side}")
        co = matter.coords(obj)
        stripe = matter.noise(co * np.asarray((1, 1, 0.2)), 500.0, 3)[:, None]
        matter.paint(obj, base * (0.8 + 0.3 * stripe))
        matter.slot(obj, "char:leather")
        out.append(obj)
    return out


def _branch(bm, layer, pts, r0, r1, base, rng, leaves, leaf_size, twig=True):
    """A crooked branch through ``pts`` tapering from ``r0`` to ``r1``, a twig forking off it, leaves near the end."""
    path = spline(pts, 18)
    verts = matter.tube(bm, path, lambda t: r0 + (r1 - r0) * t ** 0.8, 7)
    tone = {}
    for v, t, a in verts:
        k = (0.78 + 0.2 * math.sin(a * 3 + t * 9)) * (1.05 - 0.2 * t)
        tone[v] = tuple(min(1.0, c * k) for c in base)
    for v, c in tone.items():
        for loop in v.link_loops:
            loop[layer] = (*c, 1.0)
    tips = [(path[-1], path[-1] - path[-2])]
    if twig:
        k = int(len(path) * 0.55)
        d = (path[k + 1] - path[k]).normalized()
        side = d.orthogonal().normalized()
        side = (side * math.cos(rng.uniform(0, 6.28)) + d.cross(side) * math.sin(rng.uniform(0, 6.28))).normalized()
        q = [path[k], path[k] + (d * 0.6 + side * 0.8).normalized() * r0 * 4, path[k] + (d * 0.9 + side * 0.9).normalized() * r0 * 7.5]
        tips.append((q[-1], q[-1] - q[-2]))
        sub = matter.tube(bm, spline(q, 8), lambda t: r0 * 0.5 * (1 - t) + r1 * 0.6 * t, 6)
        for v, t, a in sub:
            for loop in v.link_loops:
                loop[layer] = (*(c * 0.82 for c in base), 1.0)
    for tip, d in tips[:leaves]:
        for i in range(4):
            col = LEAVES[int(rng.integers(len(LEAVES)))]
            dd = (d.normalized() + Vector(tuple(rng.normal(0, 0.7, 3)))).normalized()
            n = dd.orthogonal().normalized()

            def color(u, v, under, col=col):
                k = (0.82 if under else 1.0) * (0.85 + 0.25 * u) * (0.8 if abs(v) < 0.12 else 1.0)
                return tuple(min(1.0, c * k) for c in col)
            blade(bm, layer, tip, dd, n, leaf_size * rng.uniform(0.8, 1.15), leaf_size * 0.36, color, fold=0.3, curl=-0.22,
                  thick=leaf_size * 0.03)


def _spike(bm, layer, base, d, length, width, color):
    """A splinter: a thin four-sided shard."""
    d = d.normalized()
    t = d.orthogonal().normalized()
    b = d.cross(t)
    ring = [bm.verts.new(base + (t * math.cos(a) + b * math.sin(a) * 0.45) * width) for a in (0.0, 1.57, 3.14, 4.71)]
    tip = bm.verts.new(base + d * length)
    root = bm.verts.new(base - d * width * 2)
    faces = [bm.faces.new((ring[k], ring[(k + 1) % 4], tip)) for k in range(4)]
    faces += [bm.faces.new((ring[(k + 1) % 4], ring[k], root)) for k in range(4)]
    for f in faces:
        for loop in f.loops:
            k = 1.0 if loop.vert is not tip else 1.15
            loop[layer] = (*(min(1.0, c * k) for c in color), 1.0)


def _rigid_at(obj, lookup, point):
    """Every vertex takes the weights of the body vertex nearest ``point`` (a piece carried rigidly from its root)."""
    from .rig import write_weights
    tree, body_w = lookup
    w = body_w[tree.find(point)[1]]
    write_weights(obj, [dict(w) for _ in obj.data.vertices])


def growth(fr, head, wood, lookup, base, p):
    """Branches, leaves, the shoulder stub and the splinters: [objects]."""
    from .garments import mesh_from
    from .rig import rigid
    j, s, h = fr.j, fr.s, head.h
    rng = np.random.default_rng(23)
    out = []
    bark = tuple(base * 0.9)
    # crown: three branches out of the top of the head
    bm = bmesh.new()
    layer = bm.loops.layers.color.new("Color")
    for pts, r0, r1, n in (([(0.018, -0.02, 0.09), (0.04, -0.03, 0.16), (0.08, -0.05, 0.23), (0.1, -0.04, 0.3)], 0.022, 0.005, 2),
                           ([(-0.03, 0.0, 0.09), (-0.065, 0.006, 0.16), (-0.12, -0.01, 0.2), (-0.16, -0.03, 0.22)], 0.019, 0.0045, 2),
                           ([(0.0, 0.035, 0.1), (0.014, 0.06, 0.15), (0.006, 0.08, 0.19)], 0.011, 0.003, 1)):
        _branch(bm, layer, [head.at(x) for x in pts], r0 * h, r1 * h, bark, rng, n, 0.056 * h, twig=n > 1)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    crown = mesh_from(bm, "branches")
    rigid(crown, "head")
    out.append(crown)
    # a branch out of the left shoulder, a broken stub on the right
    for side, sx, grow in (("L", -1, True), ("R", 1, False)):
        root = j[f"shoulder.{side}"] + Vector((-sx * 0.035 * s, -0.025 * s, 0.035 * s))
        bm = bmesh.new()
        layer = bm.loops.layers.color.new("Color")
        if grow:
            pts = [root - Vector((0, 0, 0.02 * s)), root + Vector((sx * 0.03, -0.02, 0.07)) * s,
                   root + Vector((sx * 0.08, -0.05, 0.13)) * s, root + Vector((sx * 0.1, -0.05, 0.19)) * s]
            _branch(bm, layer, pts, 0.02 * s, 0.0035 * s, bark, rng, 2, 0.06 * s)
        else:
            pts = [root - Vector((0, 0, 0.02 * s)), root + Vector((sx * 0.02, -0.01, 0.045)) * s, root + Vector((sx * 0.03, -0.012, 0.07)) * s]
            _branch(bm, layer, pts, 0.017 * s, 0.011 * s, bark, rng, 0, 0.05 * s, twig=False)
            for k in range(5):            # the broken end splintered
                a = 2 * math.pi * k / 5
                d = Vector((math.cos(a) * 0.3, math.sin(a) * 0.3, 1.0))
                _spike(bm, layer, pts[-1] + Vector((math.cos(a), math.sin(a), 0)) * 0.007 * s, d, 0.02 * s * (0.6 + 0.4 * (k % 2)),
                       0.0035 * s, (0.78, 0.64, 0.44))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        obj = mesh_from(bm, f"shoulder_branch_{side}")
        _rigid_at(obj, lookup, root)
        out.append(obj)
    # splinters sticking out of the bark
    spots = [("chest", (0.06, -0.09, 1.36), (0.4, -1, 0.5)), ("chest", (-0.08, -0.08, 1.3), (-0.6, -1, 0.3)),
             ("spine", (0.02, -0.075, 1.12), (0.2, -1, -0.2)), ("arm", "forearm.R", (0.6, -0.4, 0.3)),
             ("arm", "upper_arm.L", (-0.5, -0.5, 0.4)), ("arm", "thigh.R", (0.6, 0.3, 0.2)), ("arm", "shin.L", (-0.6, -0.4, 0.0))]
    for kind, where, direction in spots:
        d = Vector(direction).normalized()
        if kind == "arm":
            a, b = (j[k] for k in {"forearm.R": ("elbow.R", "wrist.R"), "upper_arm.L": ("shoulder.L", "elbow.L"),
                                   "thigh.R": ("hip.R", "knee.R"), "shin.L": ("knee.L", "ankle.L")}[where])
            centre = a.lerp(b, 0.45)
        else:
            centre = Vector(where) * s
            centre = Vector((0, centre.y * 0.2, centre.z))
            d = (Vector(where) * s - centre).normalized()
        hit = Vector(tuple(matter.onto(wood.shape, np.asarray(tuple(centre + d * 0.3 * s)), np.asarray(tuple(-d)), 0.3 * s)[0]))
        bm = bmesh.new()
        layer = bm.loops.layers.color.new("Color")
        tilt = (d + Vector((0, 0, 0.6))).normalized()
        _spike(bm, layer, hit - d * 0.003 * s, tilt, 0.05 * s * rng.uniform(0.7, 1.2), 0.0045 * s, (0.8, 0.66, 0.46))
        _spike(bm, layer, hit - d * 0.003 * s + tilt.orthogonal() * 0.006 * s, (tilt + d).normalized(), 0.03 * s, 0.0035 * s, (0.74, 0.6, 0.42))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        obj = mesh_from(bm, "splinter")
        _rigid_at(obj, lookup, hit)
        out.append(obj)
    return out


def embers(head, color):
    """A faint glow far inside each eye hollow."""
    from .garments import mesh_from
    from .rig import rigid
    bm = bmesh.new()
    for sx in (1, -1):
        res = bmesh.ops.create_icosphere(bm, subdivisions=2, radius=0.0062 * head.h)
        bmesh.ops.translate(bm, vec=head.at((sx * 0.035, 0.054, 0.004)), verts=res["verts"])
    obj = mesh_from(bm, "eye_embers")
    matter.paint(obj, np.tile(matter.hexrgb(color), (len(obj.data.vertices), 1)))
    matter.slot(obj, "char:glow")
    rigid(obj, "head")
    return obj


def build(fr, head, arm, wood, lookup, base, p):
    out = hands(fr, arm, lookup, base)
    for obj in growth(fr, head, wood, lookup, base, p):
        matter.slot(obj, "char:leather")
        out.append(obj)
    out.append(embers(head, p.get("eyes", "#ff8a2a")))
    return out

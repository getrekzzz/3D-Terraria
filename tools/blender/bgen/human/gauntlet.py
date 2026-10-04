"""The rest of the possessed armour (plate.py): gauntlets (a flared cuff, a plate over the back of the hand round
a solid palm, and every finger segment its own small plate on its own bone, knuckles ridged), rivets set along the
plates' edges, the eyes burning in the helm's slit, and the dim red glow that fills the suit where the plates part.
"""
import bmesh
import numpy as np
from mathutils import Vector

from . import matter
from .matter import F
from .rig import rigid
from .sdf import ellipsoid, round_cone, smin

SOURCES = ("gauntlet.py", "plate.py")


def _hand_pieces(fr, side):
    """(name, bone, field, lo, hi, target) for one gauntlet."""
    from .hands import hand_axes
    j, s = fr.j, fr.s
    along, thumb, dorsal = (np.asarray(tuple(v)) for v in hand_axes(fr, side))
    wr = np.asarray(tuple(j[f"wrist.{side}"]))
    el = np.asarray(tuple(j[f"elbow.{side}"]))
    fw = (wr - el) / np.linalg.norm(wr - el)
    rot = np.stack([along, thumb, dorsal], 1)
    out = []
    a, b = wr - fw * 0.035 * s, wr + fw * 0.03 * s

    def cuff(p, a=a, b=b):
        o = round_cone(p, a, b, 0.046 * s, 0.056 * s)
        return np.maximum(np.maximum(o, -(o + F(0.005 * s))), np.maximum((p - a) @ -fw.astype(F), (p - b) @ fw.astype(F)))
    out.append((f"gauntlet_cuff_{side}", f"hand.{side}", cuff, a - 0.08 * s, b + 0.08 * s, 1200))
    c = wr + along * 0.05 * s

    def back(p, c=c):
        palm = ellipsoid(p, c - dorsal * 0.004 * s, (0.05 * s, 0.047 * s, 0.022 * s), rot)
        plate = ellipsoid(p, c + dorsal * 0.008 * s, (0.054 * s, 0.05 * s, 0.022 * s), rot)
        return smin(palm, plate, 0.006 * s)
    out.append((f"gauntlet_hand_{side}", f"hand.{side}", back, c - 0.09 * s, c + 0.09 * s, 1400))
    for f in ("thumb", "index", "middle", "ring", "pinky"):
        pts = [np.asarray(tuple(j[f"{f}{i}.{side}"])) for i in (1, 2, 3)] + [np.asarray(tuple(j[f"{f}_tip.{side}"]))]
        if f == "thumb":
            pts[0] = pts[0] + (pts[1] - pts[0]) * 0.4
        r = (0.0125 if f == "thumb" else 0.0105 if f != "pinky" else 0.0092) * s
        for i in range(3):
            p0, p1 = pts[i], pts[i + 1]
            L = np.linalg.norm(p1 - p0)
            dd = (p1 - p0) / L

            def seg(p, p0=p0, p1=p1, dd=dd, r=r, i=i):
                o = round_cone(p, p0 + dd * 0.002 * s, p1 - dd * (0.001 * s if i < 2 else -0.002 * s), r, r * (0.9 if i < 2 else 0.7))
                ridge = ellipsoid(p, p0 + dd * 0.004 * s + np.asarray(tuple(dorsal)) * r * 0.55, (r * 0.8, r * 0.8, r * 0.5))
                return smin(o, ridge, 0.002 * s)
            out.append((f"{f}{i}_{side}", f"{f}.0{i + 1}.{side}", seg, np.minimum(p0, p1) - 0.03 * s, np.maximum(p0, p1) + 0.03 * s, 220))
    return out


def build(fr, steel, key):
    out = []
    s = fr.s
    for side in ("R", "L"):
        for name, bone, field, lo, hi, target in _hand_pieces(fr, side):
            small = target < 500
            obj = matter.mesh(field, (lo, hi), (0.0009 if small else 0.0014) * s, target, name, key, SOURCES)
            rigid(obj, bone)
            co = matter.coords(obj)
            streak = matter.noise(co * np.asarray((1.0, 1.0, 0.1)), 70.0, 5)[:, None]
            matter.paint(obj, steel * (0.88 + 0.12 * streak) * (0.82 if small else 1.0))
            matter.slot(obj, "char:metal")
            out.append(obj)
    return out


def _bone_of(obj):
    return obj.vertex_groups[0].name if obj.vertex_groups else "chest"


def rivets(fr, head, plates, color):
    """Small domed rivets along the plates' edges: rays toward each plate's bone from outside, a rivet where each
    first meets steel, carried by that plate's bone."""
    from mathutils.bvhtree import BVHTree
    j, s = fr.j, fr.s
    verts, polys, owner = [], [], []
    for o in plates:
        base = len(verts)
        verts += [v.co.copy() for v in o.data.vertices]
        polys += [tuple(base + i for i in poly.vertices) for poly in o.data.polygons]
        owner += [_bone_of(o)] * len(o.data.polygons)
    tree = BVHTree.FromPolygons(verts, polys)
    rays = []

    def ring(c, axis, angles, reach=0.4 * s):
        axis = Vector(axis).normalized()
        ref = Vector((0, 1, 0)) if abs(axis.y) < 0.9 else Vector((0, 0, 1))
        ref = (ref - axis * ref.dot(axis)).normalized()
        side = axis.cross(ref)
        for a in angles:
            d = ref * np.cos(np.radians(a)) + side * np.sin(np.radians(a))
            rays.append((Vector(c) + d * reach, -d))

    ring((0, -0.01 * s, 1.1 * s), (0, 0, 1), (-150, -110, -70, -35, 35, 70, 110, 150, 180))
    for k in range(3):
        ring((0, -0.012 * s, (1.065 - 0.062 * k) * s), (0, 0, 1), (-120, -60, 0, 60, 120, 180))
    ring((0, -0.024 * s, 1.43 * s), (0, 0, 1), (-60, 0, 60))
    for side, sx in (("R", 1), ("L", -1)):
        sh, el, wr = j[f"shoulder.{side}"], j[f"elbow.{side}"], j[f"wrist.{side}"]
        hip, kn, an = j[f"hip.{side}"], j[f"knee.{side}"], j[f"ankle.{side}"]
        ring(sh.lerp(el, 0.42), el - sh, (sx * 60, sx * 100, sx * 140), 0.2 * s)
        ring(el.lerp(wr, 0.8), wr - el, (sx * 60, sx * 110), 0.2 * s)
        ring(hip.lerp(kn, 0.2), kn - hip, (sx * 35, sx * 75, sx * 115), 0.25 * s)
        ring(kn.lerp(an, 0.2), an - kn, (sx * 40, sx * 90, sx * 140), 0.25 * s)
        ring(sh + Vector((sx * 0.012 * s, 0, 0)), Vector((0, 1, 0)), (sx * 55, sx * 90, sx * 125), 0.25 * s)
    bm = bmesh.new()
    groups = []
    r = 0.0075 * s
    for origin, d in rays:
        loc, n, idx, _ = tree.ray_cast(origin, d, 0.5 * s)
        if loc is None:
            continue
        res = bmesh.ops.create_icosphere(bm, subdivisions=1, radius=r)
        t = n.orthogonal().normalized()
        for v in res["verts"]:
            off = v.co.copy()
            h = off.dot(n)
            v.co = loc + n * (h * 0.6 - r * 0.15) + (off - n * h)
        groups.append((len(res["verts"]), owner[idx]))
    from .garments import mesh_from
    obj = mesh_from(bm, "rivets")
    from .rig import write_weights
    w = [{bone: 1.0} for count, bone in groups for _ in range(count)]
    write_weights(obj, w)
    matter.paint(obj, np.tile(matter.hexrgb(color), (len(obj.data.vertices), 1)))
    matter.slot(obj, "char:metal")
    return [obj]


def eyes(head, color):
    """Two burning eyes behind the helm's slit."""
    from .garments import mesh_from
    bm = bmesh.new()
    for sx in (1, -1):
        res = bmesh.ops.create_icosphere(bm, subdivisions=2, radius=1.0)
        bmesh.ops.scale(bm, vec=(0.014 * head.h, 0.008 * head.h, 0.007 * head.h), verts=res["verts"])
        bmesh.ops.translate(bm, vec=head.at((sx * 0.032, 0.09, -0.003)), verts=res["verts"])
    obj = mesh_from(bm, "eye_glow")
    matter.paint(obj, np.tile(matter.hexrgb(color), (len(obj.data.vertices), 1)))
    matter.slot(obj, "char:glow")
    rigid(obj, "head")
    return obj


def core(fr, arm, color):
    """The dim glow inside the suit: a slim body well within the plates, seen only where they part."""
    from . import frame as frame_mod
    from . import rig
    from .blobs import Blob
    from .body import build_body
    slim = frame_mod.Frame({"height": fr.height, "body": "female" if fr.female else "male", "build": 0.0, "weight": 0.0})
    blob = Blob("core_blob", 0.012 * fr.s)
    build_body(slim, blob)
    body = blob.to_mesh("core", 4000, smooth_iterations=1)
    fingers = [b.name for b in arm.data.bones if b.name.split(".")[0] in frame_mod.FINGERS]
    rig.heat_weights(body, arm, drop=fingers + ["root"])
    rig.clean_limb_weights(body, arm, fr)
    bm = bmesh.new()                       # no hands: the gauntlets are solid, and shrunk fingers would turn inside out
    bm.from_mesh(body.data)
    hands = [i for i, w in enumerate(rig.read_weights(body)) if w and max(w, key=w.get).startswith("hand.")]
    bm.verts.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.verts[i] for i in hands], context="VERTS")
    bm.to_mesh(body.data)
    bm.free()
    co, n = matter.coords(body), matter.normals(body)
    body.data.vertices.foreach_set("co", (co - n * 0.02 * fr.s).astype(np.float32).ravel())
    body.data.update()
    matter.paint(body, np.tile(matter.hexrgb(color), (len(co), 1)))
    matter.slot(body, "char:glow")
    return body

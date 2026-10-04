"""Creature hands and feet. ``handSize`` scales the hands (bones and all) about the wrists; ``hands: claws`` puts a
hooked claw on every fingertip in place of the nail; ``feet`` (besides talons) replaces the body's shod foot:

- paws: a wolf's padded foot, four thick toes splayed in an arc ending in dark hooked claws;
- ape: a yeti's broad, long, flat foot with five stubby toes, the big toe set apart, and small dark nails.

Feet are built like the talons (a fine blob, painted ``faceSkin`` or ``skin``; the claws or nails are coloured
``nails``) and skinned the same way (talons.skin).
"""
import math

import bmesh
import bpy
from mathutils import Vector

from .blobs import Blob
from .hands import RADII, hand_axes

KINDS = ("paws", "ape")


def reshape(fr, p):
    """Scales the hands' joints about the wrists by ``handSize`` (hands.py scales their flesh with it)."""
    k = float(p.get("handSize", 1.0))
    fr.hand_scale = k
    if k == 1.0:
        return
    for side in ("R", "L"):
        w = fr.j[f"wrist.{side}"]
        for name in list(fr.j):
            if name.endswith(f".{side}") and name.split(".")[0] not in ("wrist", "elbow", "shoulder", "clavicle") and \
                    any(name.startswith(f) for f in ("thumb", "index", "middle", "ring", "pinky", "hand_end")):
                fr.j[name] = w + (fr.j[name] - w) * k


def _hook(bm, pts, radii, flat=1.0, segs=7):
    """A closed horn through ``pts`` (radius per point, the last 0), flattened sideways by ``flat``."""
    rings = []
    for i, (c, r) in enumerate(zip(pts, radii)):
        d = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        side = d.cross(Vector((0, 0, 1)))
        side = side.normalized() if side.length > 1e-6 else d.orthogonal().normalized()
        up = side.cross(d).normalized()
        if r <= 0:
            rings.append([bm.verts.new(c)])
        else:
            rings.append([bm.verts.new(c + (side * math.cos(a) * flat + up * math.sin(a)) * r)
                          for a in (2 * math.pi * k / segs for k in range(segs))])
    for a, b in zip(rings, rings[1:]):
        for k in range(segs):
            if len(b) == 1:
                bm.faces.new((a[k], a[(k + 1) % segs], b[0]))
            else:
                bm.faces.new((a[k], a[(k + 1) % segs], b[(k + 1) % segs], b[k]))
    bm.faces.new(rings[0][::-1])


def _object(bm, name):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    for f in bm.faces:
        f.smooth = True
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def hand_claws(fr, side):
    """Hooked claws on the fingertips, curving down past them toward the palm (in place of the nails)."""
    j = fr.j
    k = fr.s * (0.9 if fr.female else 1.0) * getattr(fr, "hand_scale", 1.0)
    _, _, dorsal = hand_axes(fr, side)
    bm = bmesh.new()
    for f, radii in RADII.items():
        tip, base = j[f"{f}_tip.{side}"], j[f"{f}3.{side}"]
        d = (tip - base).normalized()
        up = (dorsal - d * dorsal.dot(d)).normalized()
        if f == "thumb":
            up = (up * 0.6 + d.cross(up) * (0.8 if side == "R" else -0.8)).normalized()
        r = radii[2] * k
        root = tip - d * r * 1.4 + up * r * 0.55
        pts = [root, root + d * r * 1.4 + up * r * 0.25, root + d * r * 2.8 - up * r * 0.2, root + d * r * 3.8 - up * r * 1.1,
               root + d * r * 4.3 - up * r * 2.2]
        _hook(bm, pts, [r * 0.78, r * 0.7, r * 0.52, r * 0.28, 0.0], flat=0.72)
    return _object(bm, f"nails_{side}")


def build_feet(fr, side, kind):
    """(foot mesh, claw or nail mesh) for one side, unpainted."""
    j, s = fr.j, fr.s
    sx = 1 if side == "R" else -1
    an, heel, ball, toe = (j[f"{n}.{side}"] for n in ("ankle", "heel", "ball", "toe"))
    ape = kind == "ape"
    g = 1.22 if ape else 1.12                     # the foot's size against a shoe
    fwd = Vector((toe.x - heel.x, toe.y - heel.y, 0)).normalized()
    out = Vector((fwd.y, -fwd.x, 0)) * sx          # toward the outside of the foot
    h0 = Vector((heel.x, heel.y, 0.0)) - fwd * 0.01 * s
    length = (toe - heel).length * g
    B = Blob(f"foot_{side}", 0.0028 * s)
    B.capsule(an + Vector((0, 0, 0.07 * s)), an, 0.034 * s * (1.1 if ape else 1.0))
    B.ball(h0 + fwd * 0.03 * s + Vector((0, 0, 0.034 * s)), 0.036 * s * g)                       # heel
    mid = h0 + fwd * length * 0.45 + Vector((0, 0, 0.032 * s))
    from .body import limb_rotation
    B.ellipsoid(mid, (length * 0.5, 0.047 * s * g * (1.15 if ape else 1.0), 0.034 * s), limb_rotation(fwd))   # sole and instep
    B.ellipsoid(Vector((an.x, an.y, 0)) + fwd * 0.04 * s + Vector((0, 0, 0.055 * s)), (0.05 * s, 0.04 * s, 0.035 * s),
                limb_rotation(fwd + Vector((0, 0, -0.6))))
    toes = []
    if ape:      # big toe on the inside, set apart; the rest in a falling row
        spec = [(-0.042, 0.0, 0.019, 0.034), (-0.012, -0.004, 0.0145, 0.027), (0.01, -0.012, 0.0135, 0.025),
                (0.03, -0.022, 0.0125, 0.022), (0.047, -0.034, 0.0115, 0.019)]
    else:        # four toes splayed in an arc, the middle two furthest forward
        spec = [(-0.032, -0.012, 0.0175, 0.03), (-0.011, 0.0, 0.0185, 0.032), (0.011, 0.0, 0.0185, 0.032),
                (0.032, -0.012, 0.0175, 0.03)]
    front = h0 + fwd * length * (0.86 if ape else 0.8)
    for x, y, r, tl in spec:
        c = front + out * x * s * g + fwd * y * s * g
        r, tl = r * s * g, tl * s * g
        d = (fwd + out * x * (1.6 if not ape else 0.6)).normalized()
        a, b = c - d * tl * 0.2 + Vector((0, 0, r * 1.05)), c + d * tl * 0.75 + Vector((0, 0, r * 0.9))
        B.capsule(a, b, r)
        B.ball(b + d * r * 0.1 + Vector((0, 0, -r * 0.15)), r * 0.95)
        toes.append((b, d, r))
    B.ellipsoid(Vector((h0.x, h0.y, -0.03 * s)) + fwd * length * 0.5, (0.12 * s, length * 0.8, 0.032 * s), negative=True)
    foot = B.to_mesh(f"foot_{side}", 3200, smooth_iterations=1)
    bm = bmesh.new()
    for b, d, r in toes:
        root = b + d * r * 0.35 + Vector((0, 0, r * 0.45))
        if ape:      # a small flat nail on top of each toe
            _hook(bm, [root - d * r * 0.3, root + d * r * 0.4, root + d * r * 0.85 - Vector((0, 0, r * 0.2)),
                       root + d * r * 1.05 - Vector((0, 0, r * 0.45))], [r * 0.5, r * 0.55, r * 0.38, 0.0], flat=1.6)
        else:        # a hooked claw reaching down to the ground
            _hook(bm, [root, root + d * r * 0.9 + Vector((0, 0, r * 0.05)), root + d * r * 1.7 - Vector((0, 0, r * 0.45)),
                       root + d * r * 2.2 - Vector((0, 0, r * 1.15))], [r * 0.48, r * 0.42, r * 0.26, 0.0], flat=0.8)
    return foot, _object(bm, f"claws_{side}")

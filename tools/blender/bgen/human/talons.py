"""Bird feet (a harpy's, ``feet: talons``): a scaly shank out of the ankle onto a padded sole, three jointed toes spread
forward and one turned back, each ending in a hooked claw. Built per side at a fine blob resolution like the hands; the
body leaves out its own shoe-like foot for them.
"""
import math

import bmesh
import bpy
from mathutils import Vector

from .blobs import Blob

# (angle from straight ahead, toward the outside; segment lengths; root radius) per toe, all scaled with the body.
TOES = ((0.0, (0.058, 0.044, 0.034), 0.0148), (0.45, (0.048, 0.037, 0.03), 0.0135),
        (-0.45, (0.046, 0.036, 0.029), 0.0135), (math.pi, (0.039, 0.028), 0.013))


def _toes(frame, side):
    """Each toe's joints (root to tip) and radii."""
    j, s = frame.j, frame.s
    sx = 1 if side == "R" else -1
    an = j[f"ankle.{side}"]
    hub = Vector((an.x + sx * 0.004 * s, an.y + 0.028 * s, 0.03 * s))
    out = []
    for ang, segs, r in TOES:
        d = Vector((sx * math.sin(ang), math.cos(ang), 0.0))
        p = hub + d * 0.014 * s
        pts, radii = [p], [r * s]
        for k, length in enumerate(segs):
            p = p + d * length * s
            p.z = max(r * s * (0.85 - 0.12 * k), 0.008 * s)
            pts.append(p.copy())
            radii.append(r * s * (0.82 - 0.14 * k))
        out.append((d, pts, radii))
    return hub, out


def build_talons(frame, side, color, claw_color):
    """(foot mesh, claw mesh) for one side, painted."""
    from .paint import flat, hex3, paint
    j, s = frame.j, frame.s
    an = j[f"ankle.{side}"]
    hub, toes = _toes(frame, side)
    B = Blob(f"talon_{side}", 0.0028 * s)
    B.capsule(an + Vector((0, 0, 0.05 * s)), an, 0.027 * s)                          # the leg's end, up inside the trousers
    B.capsule(an + Vector((0, -0.002 * s, 0.0)), hub, 0.022 * s)                      # shank
    B.ellipsoid(hub + Vector((0, 0.004 * s, -0.004 * s)), (0.033 * s, 0.035 * s, 0.021 * s))  # the sole's pad
    for d, pts, radii in toes:
        for a, b, ra, rb in zip(pts, pts[1:], radii, radii[1:]):
            B.capsule(a, b - d * rb * 0.3, (ra + rb) * 0.5)
            B.ball(a + Vector((0, 0, -ra * 0.25)), ra * 1.1)                           # knuckle pad
        B.ball(pts[-1], radii[-1] * 0.95)
    foot = B.to_mesh(f"talon_{side}", 2600, smooth_iterations=1)
    c = hex3(color)
    paint(foot, lambda co: tuple(x * (0.9 + 0.1 * math.cos(co.z / s * 2 * math.pi / 0.011) if co.z > 0.035 * s else 0.96)
                                 for x in c))                                        # scutes ringing the shank
    bm = bmesh.new()
    for d, pts, radii in toes:
        tip, r = pts[-1], radii[-1]
        rings = []
        for k, (t, rr) in enumerate(((0.0, 0.95), (0.45, 0.72), (0.8, 0.4), (1.0, 0.0))):
            c = tip + d * (r * 0.4 + t * 2.4 * r) + Vector((0, 0, r * (0.25 + 0.6 * t - 1.7 * t * t)))
            side_v = d.cross(Vector((0, 0, 1))).normalized()
            up = side_v.cross(d).normalized()
            if rr <= 0:
                rings.append([bm.verts.new(c)])
            else:
                rings.append([bm.verts.new(c + (side_v * math.cos(a) + up * math.sin(a)) * r * rr)
                              for a in (2 * math.pi * i / 8 for i in range(8))])
        for a, b in zip(rings, rings[1:]):
            for i in range(8):
                if len(b) == 1:
                    bm.faces.new((a[i], a[(i + 1) % 8], b[0]))
                else:
                    bm.faces.new((a[i], a[(i + 1) % 8], b[(i + 1) % 8], b[i]))
        bm.faces.new(rings[0][::-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    for f in bm.faces:
        f.smooth = True
    me = bpy.data.meshes.new(f"claws_{side}")
    bm.to_mesh(me)
    bm.free()
    claws = bpy.data.objects.new(f"claws_{side}", me)
    bpy.context.scene.collection.objects.link(claws)
    flat(claws, claw_color)
    return foot, claws


def skin(objs, arm, frame, side, body_tree, body_w):
    """The foot bone's, blending into the body's own weights up the shank and over the ankle."""
    from .head import smooth
    from .rig import write_weights
    an, s = frame.j[f"ankle.{side}"], frame.s
    for obj in objs:
        out = []
        for v in obj.data.vertices:
            k = smooth(-0.01 * s, 0.03 * s, v.co.z - an.z)
            w = {f"foot.{side}": 1.0 - k}
            if k > 0:
                for b, x in body_w[body_tree.find(v.co)[1]].items():
                    w[b] = w.get(b, 0.0) + x * k
            out.append({b: x for b, x in w.items() if x > 0.01})
        write_weights(obj, out)

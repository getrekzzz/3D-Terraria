"""The walking dead in bone: skull (sockets, nasal cavity, teeth, jaw), vertebrae, ribcage and sternum, collarbones,
shoulder blades, pelvis, long bones with knobbed joints, hand and foot bones. Each piece is its own small blob bound
to the rig bone that carries it, so skeletons share the human rig and its animations.
"""
import math

from mathutils import Vector

from .blobs import Blob
from .body import limb_rotation
from .rig import chain_weights, rigid


def build_skeleton(frame, head, arm):
    """Returns the bone meshes (already weighted); material slot char:bone, glowing eyes char:glow."""
    j, s = frame.j, frame.s
    parts = []

    def piece(name, bone, res, fill, tris=1800):
        b = Blob(name, res * s)
        fill(b)
        obj = b.to_mesh(name, tris, smooth_iterations=1)
        if isinstance(bone, list):
            chain_weights(obj, arm, bone, 0.01)
        else:
            rigid(obj, bone)
        parts.append(obj)

    def skull(b):
        h = head.h
        b.ellipsoid(head.at((0, -0.012, 0.022)), (0.068 * h, 0.088 * h, 0.078 * h))
        b.ellipsoid(head.at((0, 0.048, -0.022)), (0.047 * h, 0.042 * h, 0.036 * h))                      # face
        b.ellipsoid(head.at((0, 0.066, -0.05)), (0.034 * h, 0.024 * h, 0.018 * h))                        # maxilla
        b.capsule(head.at((-0.042, 0.075, 0.014)), head.at((0.042, 0.075, 0.014)), 0.009 * h)             # brow ridge
        for sx in (1, -1):
            b.capsule(head.at((sx * 0.048, 0.058, -0.016)), head.at((sx * 0.068, -0.004, -0.024)), 0.0085 * h)  # cheek arch
            b.ball(head.at((sx * 0.031, 0.086, -0.002)), 0.022 * h, negative=True)                          # eye socket
        b.ellipsoid(head.at((0, 0.096, -0.034)), (0.01 * h, 0.022 * h, 0.015 * h), negative=True)           # nasal cavity
        for k in range(10):
            a = (k - 4.5) / 4.5 * 0.95
            b.ball(head.at((math.sin(a) * 0.027, 0.086 * math.cos(a * 0.8) - 0.003, -0.066)), 0.0046 * h)  # upper teeth

    def jaw(b):
        h = head.h
        for sx in (1, -1):
            b.capsule(head.at((sx * 0.055, -0.004, -0.03)), head.at((sx * 0.052, 0.0, -0.07)), 0.007 * h)
            b.capsule(head.at((sx * 0.052, 0.0, -0.07)), head.at((sx * 0.02, 0.07, -0.092)), 0.0075 * h)
        b.ball(head.at((0, 0.075, -0.094)), 0.011 * h)
        for k in range(10):
            a = (k - 4.5) / 4.5 * 0.9
            b.ball(head.at((math.sin(a) * 0.024, 0.078 * math.cos(a * 0.8) - 0.004, -0.073)), 0.0042 * h)

    piece("skull", "head", 0.0022, skull, 3000)
    piece("jaw", "head", 0.002, jaw, 900)

    def spine(b):
        path = [j["pelvis"], j["spine"], j["chest"], j["neck"], j["head"]]
        pts = [path[i].lerp(path[i + 1], t / 4) for i in range(len(path) - 1) for t in range(4)] + [path[-1]]
        for p in pts:
            b.ellipsoid(p - Vector((0, 0.025 * s, 0)), (0.017 * s, 0.014 * s, 0.009 * s))
            b.capsule(p - Vector((0, 0.03 * s, 0)), p - Vector((0, 0.05 * s, 0.01 * s)), 0.0045 * s)

    piece("spine", ["hips", "spine", "chest", "neck"], 0.0025, spine, 2400)

    def ribs(b):
        c = j["chest"]
        for k in range(10):
            z = (1.42 - 0.026 * k) * s
            rx = (0.1 + 0.03 * math.sin(min(k, 7) / 7 * math.pi * 0.7)) * s
            ry = (0.085 + 0.012 * k / 9) * s
            pts = []
            for i in range(9):
                a = -0.1 + i / 8 * (math.pi * (0.92 if k < 7 else 0.6))
                for sx in (1,):
                    pts.append(Vector((math.sin(a) * rx, -math.cos(a) * ry + c.y - 0.01 * s, z - 0.02 * s * math.sin(a))))
            for sx in (1, -1):
                mirrored = [Vector((p.x * sx, p.y, p.z)) for p in pts]
                for a0, a1 in zip(mirrored, mirrored[1:]):
                    b.capsule(a0, a1, 0.0055 * s)
        b.capsule(Vector((0, c.y + 0.08 * s, 1.43 * s)), Vector((0, c.y + 0.085 * s, 1.22 * s)), 0.011 * s)   # sternum

    piece("ribcage", "chest", 0.0026, ribs, 4000)

    def pelvis(b):
        p = j["pelvis"]
        for sx in (1, -1):
            b.ellipsoid(p + Vector((sx * 0.085, -0.01, 0.02)) * s, (0.055 * s, 0.018 * s, 0.05 * s), limb_rotation(Vector((sx * 0.3, 0.5, 1))))
            b.ball(j[f"hip.{'R' if sx > 0 else 'L'}"], 0.024 * s)
            b.capsule(p + Vector((sx * 0.06, 0.03, -0.05)) * s, p + Vector((sx * 0.015, 0.06, -0.075)) * s, 0.01 * s)
        b.ellipsoid(p + Vector((0, -0.05, -0.01)) * s, (0.03 * s, 0.015 * s, 0.05 * s))

    piece("pelvis", "hips", 0.0028, pelvis, 1600)

    def long_bone(a, b_, r):
        def fill(b):
            b.capsule(a, b_, r * s)
            b.ball(a + (b_ - a) * 0.04, r * 1.75 * s)
            b.ball(b_ - (b_ - a) * 0.04, r * 1.6 * s)
        return fill

    for side, sx in (("R", 1), ("L", -1)):
        piece(f"clavicle_{side}", f"clavicle.{side}", 0.0025, long_bone(j[f"clavicle.{side}"], j[f"shoulder.{side}"], 0.006), 500)
        piece(f"humerus_{side}", f"upper_arm.{side}", 0.003, long_bone(j[f"shoulder.{side}"], j[f"elbow.{side}"], 0.009), 900)
        piece(f"forearm_{side}", f"forearm.{side}", 0.0028, long_bone(j[f"elbow.{side}"], j[f"wrist.{side}"], 0.007), 900)
        piece(f"femur_{side}", f"thigh.{side}", 0.0032, long_bone(j[f"hip.{side}"], j[f"knee.{side}"], 0.012), 1000)
        piece(f"shin_{side}", f"shin.{side}", 0.0032, long_bone(j[f"knee.{side}"], j[f"ankle.{side}"], 0.011), 1000)

        def scapula(b, side=side, sx=sx):
            b.ellipsoid(j["chest"] + Vector((sx * 0.08, -0.1, 0.1)) * s, (0.05 * s, 0.01 * s, 0.065 * s), limb_rotation(Vector((sx * 0.2, 0, 1))))

        piece(f"scapula_{side}", "chest", 0.003, scapula, 500)

        def hand(b, side=side):
            for f in ("thumb", "index", "middle", "ring", "pinky"):
                pts = [j[f"wrist.{side}"]] + [j[f"{f}{n}.{side}"] for n in (1, 2, 3)] + [j[f"{f}_tip.{side}"]]
                for a, b_ in zip(pts, pts[1:]):
                    b.capsule(a + (b_ - a) * 0.08, b_ - (b_ - a) * 0.08, 0.0042 * s)
                    b.ball(a + (b_ - a) * 0.08, 0.0055 * s)

        from .rig import hand_bones
        piece(f"hand_{side}", hand_bones(side), 0.0016, hand, 2400)

        def foot(b, side=side, sx=sx):
            an, heel, ball, toe = j[f"ankle.{side}"], j[f"heel.{side}"], j[f"ball.{side}"], j[f"toe.{side}"]
            b.ellipsoid(an.lerp(heel, 0.5), (0.03 * s, 0.022 * s, 0.022 * s), limb_rotation(heel - an))
            for k in range(5):
                off = Vector(((k - 2) * 0.012 * sx * s, 0, 0))
                b.capsule(an + off * 0.5 + Vector((0, 0.02 * s, -0.02 * s)), ball + off, 0.004 * s)
                b.capsule(ball + off, toe + off * 1.1, 0.0035 * s)

        piece(f"foot_{side}", [f"foot.{side}", f"toe.{side}"], 0.0022, foot, 1200)
    return parts

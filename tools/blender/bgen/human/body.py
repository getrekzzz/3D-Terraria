"""The body below the head: torso, arms (to the wrist) and legs with feet, modelled as blended muscle masses.

Surface sizes are for an average 1.78 m man and scale with the frame; ``build`` (muscle) and ``weight`` (fat)
reshape the masses; women get narrower shoulders, a wider pelvis, fuller hips and a chest.
"""
from mathutils import Matrix, Quaternion, Vector

from .blobs import Blob


def limb_rotation(direction: Vector) -> Quaternion:
    """Rotation mapping +X to the limb axis and +Y to the body's forward direction (kept perpendicular)."""
    d = Vector(direction).normalized()
    f = Vector((0, 1, 0))
    f = (f - d * f.dot(d))
    if f.length < 1e-4:
        f = Vector((1, 0, 0))
    f.normalize()
    z = d.cross(f)
    return Matrix((d, f, z)).transposed().to_quaternion()


def along(a: Vector, b: Vector, t: float) -> Vector:
    return a.lerp(b, t)


def build_body(frame, blob: Blob):
    j, s = frame.j, frame.s
    fem = frame.female
    m = 0.82 + 0.36 * frame.build   # muscle scale
    f = 0.84 + 0.4 * frame.weight   # fat scale
    old = 0.93 if frame.old else 1.0
    B = blob

    def e(c, r, rot=None, neg=False, stiffness=2.0):
        B.ellipsoid(Vector(c), tuple(x * s for x in r), rot, negative=neg, stiffness=stiffness)

    def at(p):
        return Vector(p) * s

    # ---- torso
    e(at((0, -0.02, 1.355)), ((0.138 if fem else 0.158) * f ** 0.3, 0.095, 0.118))        # upper thorax
    e(at((0, -0.006, 1.225)), ((0.12 if fem else 0.128) * f ** 0.5, 0.096 * f ** 0.6, 0.1))  # lower ribs
    e(at((0, 0.012, 1.1)), (0.112 * f ** 0.9, 0.088 * f, 0.105))                        # abdomen
    if frame.weight > 0.5:
        e(at((0, 0.05, 1.07)), (0.1 * f, 0.07 * (frame.weight - 0.3), 0.085))              # belly
    e(at((0, -0.012, 0.978)), ((0.158 if fem else 0.136) * f ** 0.7, 0.092 * f ** 0.6, 0.09))  # pelvis
    for sx in (-1, 1):
        e(at((sx * (0.068 if fem else 0.062), -0.06, 0.925)), ((0.09 if fem else 0.076) * f ** 0.6, (0.082 if fem else 0.07), 0.088))  # glutes
        e(at((sx * (0.105 if fem else 0.096), 0.0, 1.035)), ((0.045 if fem else 0.038) * f, 0.068 * f ** 0.8, 0.07))  # flanks
        e(at((sx * 0.08, -0.055, 1.28)), (0.11 * m, 0.04, 0.045 * m), limb_rotation(Vector((sx * 0.15, 0, -1))))  # lats
        e(at((sx * 0.066, -0.03, 1.438)), (0.1, 0.05 * m, 0.042 * m), limb_rotation(Vector((sx, 0, -0.45))))  # trapezius
        if fem:
            # Breasts: soft, slightly outward-facing masses that settle low and blend into the chest (not two spheres).
            e(at((sx * 0.07, 0.052, 1.268)), (0.062 * f ** 0.5, 0.05 * f ** 0.5, 0.054 * f ** 0.4), limb_rotation(Vector((sx * 0.35, 1, -0.25))), stiffness=1.4)
        else:
            e(at((sx * 0.066, 0.05, 1.34)), (0.08 * m, 0.027 * m, 0.056 * m), limb_rotation(Vector((sx, 0.12, -0.3))))  # pectorals
        B.capsule(j[f"clavicle.{'R' if sx > 0 else 'L'}"] + at((0, 0.006, 0.01)), j[f"shoulder.{'R' if sx > 0 else 'L'}"] + at((0, 0.012, 0.018)), 0.014 * s)  # collarbone
    B.capsule(j["neck"] + at((0, 0.004, -0.03)), j["head"] + at((0, 0.004, 0.01)), (0.054 if fem else 0.062) * s * m ** 0.4)

    # ---- arms
    for side, sx in (("R", 1), ("L", -1)):
        sh, el, wr = j[f"shoulder.{side}"], j[f"elbow.{side}"], j[f"wrist.{side}"]
        up = (el - sh).normalized()
        fw = (wr - el).normalized()
        rot_up, rot_fw = limb_rotation(up), limb_rotation(fw)
        arm = 0.86 if fem else 1.0
        e(sh + up * 0.03 * s + Vector((0, 0, -0.004 * s)), (0.058 * m * arm, 0.056 * m * arm, 0.06 * m * arm), rot_up)  # deltoid
        B.capsule(sh + up * 0.03 * s, el, 0.04 * s * arm * f ** 0.4)                   # upper arm
        e(along(sh, el, 0.5) + Vector((0, 0.012 * s, 0)), (0.075, 0.033 * m * arm, 0.028 * m * arm), rot_up)  # biceps
        e(along(sh, el, 0.4) + Vector((0, -0.013 * s, 0)), (0.085, 0.031 * m * arm, 0.03 * m * arm), rot_up)  # triceps
        B.ball(el, 0.033 * s * arm)                                                        # elbow
        mid = along(el, wr, 0.42)
        B.capsule(el, mid, 0.039 * s * arm * m ** 0.5)
        B.capsule(mid, wr - fw * 0.012 * s, 0.025 * s * arm)
        e(along(el, wr, 0.22) + Vector((sx * 0.008 * s, 0.01 * s, 0)), (0.07, 0.03 * m * arm, 0.026 * m * arm), rot_fw)  # brachioradialis

    # ---- legs
    for side, sx in (("R", 1), ("L", -1)):
        hip, kn, an = j[f"hip.{side}"], j[f"knee.{side}"], j[f"ankle.{side}"]
        th = (kn - hip).normalized()
        sh = (an - kn).normalized()
        rot_th, rot_sh = limb_rotation(th), limb_rotation(sh)
        leg = 1.06 if fem else 1.0
        mid = along(hip, kn, 0.45)
        B.capsule(hip + Vector((sx * 0.01 * s, 0, 0)), mid, 0.078 * s * f ** 0.6 * leg)
        B.capsule(mid, kn, 0.056 * s * f ** 0.4)
        e(along(hip, kn, 0.45) + Vector((sx * 0.004 * s, 0.024 * s, 0)), (0.15, 0.058 * m, 0.052 * m), rot_th)   # quadriceps
        e(along(hip, kn, 0.48) + Vector((0, -0.028 * s, 0)), (0.15, 0.05 * m, 0.05 * m), rot_th)               # hamstrings
        e(along(hip, kn, 0.3) + Vector((-sx * 0.035 * s, 0, 0)), (0.13, 0.045 * f, 0.042 * f * leg), rot_th)     # adductors
        B.ball(kn + Vector((0, 0.004 * s, 0)), 0.05 * s)
        B.ball(kn + Vector((0, 0.04 * s, 0.006 * s)), 0.022 * s)                                              # kneecap
        lmid = along(kn, an, 0.5)
        B.capsule(kn, lmid, 0.046 * s)
        B.capsule(lmid, an, 0.031 * s)
        e(along(kn, an, 0.3) + Vector((0, -0.028 * s, 0)), (0.12, 0.048 * m * old, 0.05 * m * old), rot_sh)   # calf
        B.ball(an + Vector((0, 0, 0.004 * s)), 0.029 * s)
        # Foot: one smooth shoe-like mass (heel, instep and a rounded toe box, no separate toes) with a flat sole;
        # people are always shod, and boots are shells of this surface.
        heel, ball, toe = j[f"heel.{side}"], j[f"ball.{side}"], j[f"toe.{side}"]
        B.ball(heel + Vector((0, 0.01 * s, 0.026 * s)), 0.034 * s)
        fd = (toe - heel)
        e(along(heel, toe, 0.45) + Vector((0, 0, 0.026 * s)), (fd.length * 0.56 / s, 0.041, 0.03), limb_rotation(fd))   # instep
        e(along(ball, toe, 0.25) + Vector((0, 0, 0.018 * s)), (0.046, 0.044, 0.02), limb_rotation(toe - ball))             # toe box
        e(along(heel, an, 0.5) + Vector((0, 0.03 * s, 0.01 * s)), (0.05, 0.034, 0.04), limb_rotation(ball - an))           # top of the foot
        e(Vector((an.x, (heel.y + toe.y) / 2, -0.03 * s)), (0.07, 0.2, 0.034), neg=True)                                   # sole

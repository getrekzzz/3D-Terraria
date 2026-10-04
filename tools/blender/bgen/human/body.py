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


def build_body(frame, blob: Blob, feet=True):
    j, s = frame.j, frame.s
    fem = frame.female
    m = 0.82 + 0.36 * frame.build   # muscle scale
    f = 0.84 + 0.4 * frame.weight   # fat scale
    # Mass beyond an average body (0 there, 1 at build 1.0 / weight 1.0): broad shoulders, a deep chest and thick
    # limbs with muscle; a round belly, love handles, a full chest and heavy limbs with fat.
    bulk = max(0.0, frame.build - 0.5) * 2.0
    fat = max(0.0, frame.weight - 0.45) / 0.55
    old = 0.93 if frame.old else 1.0
    # A man's waist runs nearly straight from his ribs to his hips; a lean one (weight under average) also has flat
    # pectorals and buttocks: thinning him must not pinch his waist or leave rounded masses on his chest.
    lean = 0.0 if fem else max(0.0, 0.45 - frame.weight) / 0.45
    B = blob

    def e(c, r, rot=None, neg=False, stiffness=2.0):
        B.ellipsoid(Vector(c), tuple(x * s for x in r), rot, negative=neg, stiffness=stiffness)

    def at(p):
        return Vector(p) * s

    # ---- torso
    e(at((0, -0.02, 1.355)), ((0.138 if fem else 0.158) * f ** 0.3 * (1 + 0.1 * bulk + 0.05 * fat),
                              0.095 * (1 + 0.14 * bulk + 0.1 * fat), 0.118))                                  # upper thorax
    e(at((0, -0.006, 1.225)), ((0.118 if fem else 0.128) * f ** 0.5 * (1 + 0.06 * bulk + 0.12 * fat),
                               0.096 * f ** 0.6 * (1 + 0.1 * bulk + 0.12 * fat), 0.1))                         # lower ribs
    e(at((0, 0.012, 1.1)), ((0.106 * f ** 0.9 if fem else 0.12 * f ** 0.6) * (1 + 0.22 * fat), 0.088 * f * (1 + 0.08 * fat), 0.105))  # abdomen
    if fat > 0:
        # Belly: a round mass carried in front, low, sagging a little over the belt.
        g, fw = min(1.0, fat * 3.0), fat ** 0.7
        e(at((0, 0.035 + 0.06 * fw, 1.08 - 0.02 * fat)), ((0.1 + 0.05 * fat) * g, (0.055 + 0.09 * fw) * g, (0.09 + 0.035 * fat) * g))
    e(at((0, -0.012, 0.978)), ((0.158 if fem else 0.136) * f ** 0.7 * (1 + 0.1 * fat - 0.08 * lean), 0.092 * f ** 0.6 * (1 + 0.1 * fat), 0.09))  # pelvis
    for sx in (-1, 1):
        side = "R" if sx > 0 else "L"
        e(at((sx * (0.068 if fem else 0.062 - 0.012 * lean), -0.06, 0.925)), ((0.09 if fem else 0.076) * f ** 0.6 * (1 + 0.12 * fat),
                                                               (0.082 if fem else 0.07 * (1 - 0.15 * lean)) * (1 + 0.15 * fat), 0.088))  # glutes
        # Flanks, thickening into love handles (on women lower, where the waist turns into the hips).
        e(at((sx * ((0.1 if fem else 0.096) + 0.012 * fat), 0.0, 1.02 if fem else 1.05)),
          ((0.04 if fem else 0.038) * f * (1 + 0.5 * fat), 0.068 * f ** 0.8 * (1 + 0.25 * fat), 0.07))
        e(at((sx * (0.08 + 0.012 * bulk), -0.055, 1.28)), (0.11 * m, 0.04 * (1 + 0.3 * bulk), 0.045 * m * (1 + 0.35 * bulk)),
          limb_rotation(Vector((sx * 0.15, 0, -1))))                                                            # lats
        tr = (0.094, 0.046, 0.039) if fem else (0.1, 0.05, 0.042)
        e(at((sx * 0.066, -0.03, 1.438)), (tr[0] * (1 + 0.08 * bulk), tr[1] * m * (1 + 0.2 * bulk), tr[2] * m * (1 + 0.35 * bulk + 0.15 * fat)),
          limb_rotation(Vector((sx, 0, -0.58 if fem else -0.45))))                                              # trapezius
        if fem:
            # Breasts: full, round masses set on the ribs, settling low and turned slightly outward.
            e(at((sx * 0.07, 0.058, 1.262)), (0.064 * f ** 0.5, 0.058 * f ** 0.6, 0.058 * f ** 0.4),
              limb_rotation(Vector((sx * 0.3, 1, -0.3))), stiffness=1.6)
        else:
            pk = 1 - 0.15 * lean            # (broad and thin: a small element would leave a mound)
            e(at((sx * 0.066, 0.05 - 0.02 * lean, 1.34 + 0.01 * lean)),
              (0.08 * m * pk * (1 + 0.1 * bulk), 0.027 * m * (1 + 0.3 * bulk - 0.5 * lean) + 0.022 * fat, 0.056 * m * pk * (1 + 0.1 * fat)),
              limb_rotation(Vector((sx, 0.12, -0.3))))                                                          # pectorals
        # Collarbone, from the top of the sternum (which a stoop doesn't carry forward with the shoulders) out to the
        # shoulder, lying back over the rounded shoulder instead of standing out in front of it.
        B.capsule(j[f"clavicle.{side}"] + at((0, 0.006, 0.01)) - Vector((0, frame.stoop, 0)),
                  j[f"shoulder.{side}"] + at((0, 0.012, 0.018)) - Vector((0, 0.5 * frame.stoop, 0)), 0.014 * s)
    if fem:
        # The bust's common mass over the cleavage and under it, so cloth spans the breasts and falls from them.
        e(at((0, 0.054, 1.258)), (0.072 * f ** 0.5, 0.05 * f ** 0.6, 0.05 * f ** 0.4), stiffness=1.6)
        e(at((0, 0.04, 1.205)), (0.09 * f ** 0.5, 0.05 * f ** 0.6, 0.05), stiffness=1.6)
    # A slim core for the neck: the head's sculpted neck covers it and runs down into the chest.
    B.capsule(j["neck"] + at((0, 0.004, -0.03)), j["head"] + at((0, 0.004, 0.01)),
              (0.032 if fem else 0.037) * s * m ** 0.4 * (1 + 0.15 * fat + 0.08 * bulk))

    # ---- arms
    for side, sx in (("R", 1), ("L", -1)):
        sh, el, wr = j[f"shoulder.{side}"], j[f"elbow.{side}"], j[f"wrist.{side}"]
        up = (el - sh).normalized()
        fw = (wr - el).normalized()
        rot_up, rot_fw = limb_rotation(up), limb_rotation(fw)
        arm = 0.82 if fem else 1.0
        dl = m * arm * (1 + 0.14 * bulk + 0.08 * fat)
        e(sh + up * 0.03 * s + Vector((sx * (0.012 * bulk - (0.007 if fem else 0.0)) * s, 0, -0.004 * s)),
          (0.058 * dl, 0.056 * dl, 0.06 * dl), rot_up)                                                             # deltoid
        B.capsule(sh + up * 0.03 * s, el, 0.04 * s * arm * f ** 0.4 * (1 + 0.12 * bulk + 0.3 * fat))           # upper arm
        e(along(sh, el, 0.5) + Vector((0, 0.012 * s, 0)), (0.075, 0.033 * m * arm * (1 + 0.3 * bulk), 0.028 * m * arm * (1 + 0.3 * bulk)), rot_up)  # biceps
        e(along(sh, el, 0.4) + Vector((0, -0.013 * s, 0)), (0.085, 0.031 * m * arm * (1 + 0.3 * bulk + 0.15 * fat),
                                                            0.03 * m * arm * (1 + 0.3 * bulk + 0.15 * fat)), rot_up)  # triceps
        B.ball(el, 0.033 * s * arm * (1 + 0.1 * bulk + 0.12 * fat))                                              # elbow
        mid = along(el, wr, 0.42)
        B.capsule(el, mid, 0.039 * s * arm * m ** 0.5 * (1 + 0.14 * bulk + 0.15 * fat))
        B.capsule(mid, wr - fw * 0.012 * s, 0.025 * s * arm)
        e(along(el, wr, 0.22) + Vector((sx * 0.008 * s, 0.01 * s, 0)), (0.07, 0.03 * m * arm * (1 + 0.3 * bulk + 0.1 * fat),
                                                                      0.026 * m * arm * (1 + 0.3 * bulk + 0.1 * fat)), rot_fw)  # brachioradialis

    # ---- legs
    for side, sx in (("R", 1), ("L", -1)):
        hip, kn, an = j[f"hip.{side}"], j[f"knee.{side}"], j[f"ankle.{side}"]
        th = (kn - hip).normalized()
        sh = (an - kn).normalized()
        rot_th, rot_sh = limb_rotation(th), limb_rotation(sh)
        leg = 1.06 if fem else 1.0
        mid = along(hip, kn, 0.45)
        B.capsule(hip + Vector((sx * 0.01 * s, 0, 0)), mid, 0.078 * s * f ** 0.6 * leg * (1 + 0.06 * bulk + 0.2 * fat))
        B.capsule(mid, kn, 0.056 * s * f ** 0.4 * (1 + 0.08 * bulk + 0.14 * fat))
        q = m * (1 + 0.22 * bulk)
        e(along(hip, kn, 0.45) + Vector((sx * 0.004 * s, 0.024 * s, 0)), (0.15, 0.058 * q, 0.052 * q), rot_th)   # quadriceps
        e(along(hip, kn, 0.48) + Vector((0, -0.028 * s, 0)), (0.15, 0.05 * q, 0.05 * q), rot_th)               # hamstrings
        e(along(hip, kn, 0.3) + Vector((-sx * 0.035 * s, 0, 0)), (0.13, 0.045 * f * (1 + 0.2 * fat), 0.042 * f * leg * (1 + 0.2 * fat)), rot_th)  # adductors
        B.ball(kn + Vector((0, 0.004 * s, 0)), 0.05 * s)
        B.ball(kn + Vector((0, 0.04 * s, 0.006 * s)), 0.022 * s)                                              # kneecap
        lmid = along(kn, an, 0.5)
        B.capsule(kn, lmid, 0.046 * s * (1 + 0.06 * bulk + 0.08 * fat))
        B.capsule(lmid, an, 0.031 * s)
        c = m * old * (1 + 0.2 * bulk + 0.12 * fat)
        e(along(kn, an, 0.3) + Vector((0, -0.028 * s, 0)), (0.12, 0.048 * c, 0.05 * c), rot_sh)                # calf
        B.ball(an + Vector((0, 0, 0.004 * s)), 0.029 * s)
        if not feet:
            continue
        # Foot: one smooth shoe-like mass (heel, instep and a rounded toe box, no separate toes) with a flat sole;
        # people are always shod, and boots are shells of this surface.
        heel, ball, toe = j[f"heel.{side}"], j[f"ball.{side}"], j[f"toe.{side}"]
        B.ball(heel + Vector((0, 0.01 * s, 0.026 * s)), 0.034 * s)
        fd = (toe - heel)
        e(along(heel, toe, 0.45) + Vector((0, 0, 0.026 * s)), (fd.length * 0.56 / s, 0.041, 0.03), limb_rotation(fd))   # instep
        e(along(ball, toe, 0.25) + Vector((0, 0, 0.018 * s)), (0.046, 0.044, 0.02), limb_rotation(toe - ball))             # toe box
        e(along(heel, an, 0.5) + Vector((0, 0.03 * s, 0.01 * s)), (0.05, 0.034, 0.04), limb_rotation(ball - an))           # top of the foot
        e(Vector((an.x, (heel.y + toe.y) / 2, -0.03 * s)), (0.07, 0.2, 0.034), neg=True)                                   # sole

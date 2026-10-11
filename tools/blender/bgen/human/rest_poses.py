"""Whole-body rest poses for generated humans, looping locomotion states the game plays for townsfolk at rest: sit,
sleep, bound and lie. Each breathes slowly and loops seamlessly; the body is lowered from standing (a hips offset)
onto what it rests on, so the game sets the model's origin on the floor, the chair or the bed:

sit    on a chair: thighs level, knees bent square with the feet flat, the hip joints at seat height (a knee's height
       above the floor), hands resting on the thighs.
sleep  on the back: the body turned level about the hips, which sit just above the surface (a bed's mattress), legs
       out straight, the right hand on the belly and the head propped a little as if on a pillow.
bound  kneeling upright with the wrists crossed behind the back and the head hung, giving a short struggle now and then.
lie    on the right side, curled up (knocked out, or asleep on the ground like the Angler), hands before the face.
"""
import math

from .actions import arm, hump
from .motion import BACK, FWD, SIDE_AXIS, UP, deg, legs, wave

SEAT = 0.505          # sit: hip joints above the floor at scale 1 (thighs level over shins standing upright; times the
                      # frame's ``legs`` for short-legged bodies)
SUPINE = 0.11         # sleep: the hips' joint above the surface lying on the back
SIDE = 0.16           # lie: the hips' joint above the ground lying on the side
KNEE = 0.055          # bound: the knee joint above the ground when kneeling


def breathing(pose, t, depth=1.0, cycles=1):
    """A slow breath (``cycles`` per loop) in the chest and shoulders."""
    b = wave(t, cycles)
    pose["chest"] = pose.get("chest", []) + [("swing", BACK, deg(1.4 * depth * b))]
    for side in ("R", "L"):
        pose[f"clavicle.{side}"] = pose.get(f"clavicle.{side}", []) + [("swing", UP, deg(1.2 * depth * b))]
    return pose


def sit(m, t):
    s, hip = m.s, m.frame.j["hip.R"].z
    legs_s = s * getattr(m.frame, "legs", 1.0)      # (short-legged folk sit lower)
    pose = {"hips": {"rot": [], "loc": (0, 0, -(hip - SEAT * legs_s))},
            "spine": [("swing", BACK, deg(2))], "head": [("swing", FWD, deg(3 + 1.5 * wave(t, 1, 0.3)))]}
    for side in ("R", "L"):
        legs(pose, side, 90, 90)
        arm(pose, side, 34, 52, 8)            # the forearms along the thighs, hands resting near the knees
    return breathing(pose, t, cycles=2)


def sleep(m, t):
    s, pelvis = m.s, m.frame.j["pelvis"].z
    pose = {"hips": {"rot": [("axis", SIDE_AXIS, deg(90))], "loc": (0, 0, -(pelvis - SUPINE * s))},   # face up, head back
            "neck": [("swing", FWD, deg(8))], "head": [("swing", FWD, deg(10)), ("axis", UP, deg(6))]}
    for side, sx in (("R", 1), ("L", -1)):
        legs(pose, side, 2, 6, -28)           # legs out, the feet relaxed
        pose[f"thigh.{side}"] += [("axis", UP, deg(-10 * sx))]     # turned out a little
    arm(pose, "R", 10, 72, 75)                # the forearm across the belly
    arm(pose, "L", 6, 14, -6)                 # the other arm by the side
    return breathing(pose, t, depth=1.6)


def bound(m, t):
    s, hip = m.s, m.frame.j["hip.R"].z
    thigh = 12                                # sitting back a little toward the heels
    tug = hump(t, 0.4, 0.55)                  # a struggle against the ropes
    jerk = tug * wave(t, 8)
    legs_s = s * getattr(m.frame, "legs", 1.0)
    pose = {"hips": {"rot": [], "loc": (0, -0.02 * s, -(hip - (KNEE + 0.43 * math.cos(deg(thigh))) * legs_s))},
            "spine": [("swing", FWD, deg(8 + 4 * tug)), ("axis", UP, deg(5 * jerk))],
            "chest": [("axis", UP, deg(7 * jerk))],
            "neck": [("swing", FWD, deg(16 - 10 * tug))], "head": [("swing", FWD, deg(18 - 12 * tug))]}
    for side, sx in (("R", 1), ("L", -1)):
        legs(pose, side, thigh, thigh + 90, -150)   # shins flat on the ground behind, the feet laid along them
        # Upper arms back, forearms turned in behind the back so the wrists cross over the small of it.
        pose[f"upper_arm.{side}"] = [("axis", SIDE_AXIS, deg(-30 - 6 * tug)), ("axis", UP, deg(4 * sx * jerk))]
        pose[f"forearm.{side}"] = [("swing", (-sx, -0.35, 0.3), deg(84 + 4 * tug))]
    return breathing(pose, t, depth=1.2 + tug)


def lie(m, t):
    s, pelvis = m.s, m.frame.j["pelvis"].z
    pose = {"hips": {"rot": [("axis", FWD, deg(90))], "loc": (0, 0, -(pelvis - SIDE * s))},   # on the right side
            # Curled forward, the chest bent up off the ground over the broader shoulder, the head drooping to it.
            "spine": [("swing", FWD, deg(16)), ("axis", FWD, deg(-5))], "chest": [("swing", FWD, deg(8)), ("axis", FWD, deg(-5))],
            "neck": [("swing", FWD, deg(10))], "head": [("swing", FWD, deg(8)), ("axis", FWD, deg(10))]}
    legs(pose, "R", 56, 84, -20)              # knees drawn up, the upper leg a little further
    legs(pose, "L", 68, 92, -20)
    arm(pose, "R", 70, 96, 18)                # both hands curled up before the face
    arm(pose, "L", 62, 104, 26)
    return breathing(pose, t, depth=1.2)


def clips(m):
    """name -> (length in seconds, pose function) for Motion.build; whole-body loops on the acting stance."""
    return {
        "sit": (4.0, lambda t: sit(m, t)), "sleep": (4.0, lambda t: sleep(m, t)),
        "bound": (3.0, lambda t: bound(m, t)), "lie": (4.0, lambda t: lie(m, t)),
    }


"""Upper-body item actions for generated humans. The game layers these over locomotion (filtered to the spine, head
and arms) and time-scales each to the item's use time, so they only need the right shape and rhythm:

swing   Terraria's overhead arc: wind up behind the head, a fast arc over the top, follow through low, recover.
mine    a tool chop from over the shoulder with a little bounce on impact.
thrust  pull back to the hip, lunge the arm straight out, retract (spears, shortswords).
shoot   both hands on the weapon at shoulder height, a recoil kick and settle (guns, launchers).
bow     the bow arm held out, the other hand drawing the string back to the cheek and releasing.
cast    a gathering pull then a palm-forward push (staffs, wands, tomes).
drink   the hand brought to the mouth, head tipped back.
use     the item lifted out in front (placing, summoning, other uses).
hold    a carried light held up in front (loops; the game plays it on the right arm only).

The right arm's main angle is its pitch in the side plane: 0 hanging down, 90 straight out in front, 180 overhead,
over 180 reaching behind the head. Held items stick out of the fist at right angles to the forearm (a sword carried
point-forward, a torch upright); the wrist angle turns them in line with the arm (about -100 with the elbow's soft bend), so blades, shafts and barrels
extend the arm at the moment of a strike, a thrust or a shot.
"""
from .motion import BACK, FWD, SIDE_AXIS, UP, deg, tl, wave


def arm(pose, side, pitch, elbow, inward=0.0, wrist=0.0):
    """An arm by its pitch (see above), elbow bend, sweep across the body and wrist cock, all in degrees."""
    sx = 1 if side == "R" else -1
    pose[f"upper_arm.{side}"] = [("axis", SIDE_AXIS, deg(pitch)), ("axis", UP, deg(inward * sx))]
    pose[f"forearm.{side}"] = [("swing", FWD, deg(elbow))]
    if wrist:
        pose[f"hand.{side}"] = [("local", (1, 0, 0), deg(wrist))]   # flexes toward the thumb side (items turn in line at -90)
    return pose


def torso(pose, twist=0.0, lean=0.0, look=True):
    """Turns the torso (+ brings the right shoulder forward) and leans it (+ forward); the head keeps facing ahead."""
    pose["spine"] = [("axis", UP, deg(twist * 0.4)), ("swing", FWD if lean >= 0 else BACK, deg(abs(lean) * 0.5))]
    pose["chest"] = [("axis", UP, deg(twist * 0.6)), ("swing", FWD if lean >= 0 else BACK, deg(abs(lean) * 0.5))]
    if look:
        pose["head"] = [("axis", UP, deg(-twist * 0.75)), ("swing", BACK if lean >= 0 else FWD, deg(abs(lean) * 0.6))]
    return pose


def swing(t):
    pitch = tl(t, [(0, 178), (0.22, 208, "out"), (0.46, 72, "in"), (0.62, 26, "out"), (1, 34, "io")])
    elbow = tl(t, [(0, 45), (0.22, 72, "out"), (0.46, 4, "in"), (0.62, 14), (1, 22)])
    inward = tl(t, [(0, 0), (0.22, -4), (0.46, 10, "in"), (0.62, 22, "out"), (1, 10)])
    wrist = tl(t, [(0, -30), (0.22, -10), (0.46, -75, "in"), (0.62, -80), (1, -45)])   # the blade extends the arm at the strike
    twist = tl(t, [(0, -14), (0.22, -26, "out"), (0.46, 20, "in"), (0.62, 28, "out"), (1, 6)])
    lean = tl(t, [(0, -3), (0.22, -7), (0.46, 12, "in"), (0.62, 15), (1, 3)])
    pose = torso({}, twist, lean)
    arm(pose, "R", pitch, elbow, inward, wrist)
    counter = tl(t, [(0, 30), (0.22, 48), (0.46, -18, "in"), (0.62, -24), (1, 8)])
    arm(pose, "L", counter, 30 + 0.3 * max(0.0, counter), 14)
    return pose


def mine(t):
    pitch = tl(t, [(0, 110), (0.26, 168, "out"), (0.5, 58, "in"), (0.58, 70, "out"), (1, 100, "io")])
    elbow = tl(t, [(0, 55), (0.26, 98, "out"), (0.5, 10, "in"), (0.58, 18), (1, 48)])
    twist = tl(t, [(0, -8), (0.26, -18), (0.5, 14, "in"), (0.58, 12), (1, -2)])
    lean = tl(t, [(0, 0), (0.26, -5), (0.5, 15, "in"), (0.58, 13), (1, 3)])
    pose = torso({}, twist, lean)
    arm(pose, "R", pitch, elbow, 6, tl(t, [(0, -20), (0.26, -5), (0.5, -80, "in"), (0.58, -75), (1, -35)]))
    arm(pose, "L", 38, 88, 26)            # the free hand braced at the chest
    return pose


def thrust(t):
    pitch = tl(t, [(0, 45), (0.3, 18, "out"), (0.5, 88, "in"), (0.66, 86), (1, 52, "io")])
    elbow = tl(t, [(0, 60), (0.3, 100, "out"), (0.5, 0, "in"), (0.66, 3), (1, 50)])
    twist = tl(t, [(0, 0), (0.3, -22, "out"), (0.5, 24, "in"), (0.66, 22), (1, 2)])
    lean = tl(t, [(0, 0), (0.3, -4), (0.5, 12, "in"), (0.66, 10), (1, 1)])
    pose = torso({}, twist, lean)
    pose["clavicle.R"] = [("swing", FWD, deg(tl(t, [(0, 0), (0.3, -6), (0.5, 12, "in"), (1, 0)])))]
    arm(pose, "R", pitch, elbow, 8, -104)         # the shaft in line with the arm, like a lance
    arm(pose, "L", tl(t, [(0, 20), (0.3, 36), (0.5, -12, "in"), (1, 14)]), 34, 10)
    return pose


def shoot(t):
    kick = tl(t, [(0, 0), (0.08, 1, "out"), (0.5, 0, "io"), (1, 0)])
    pose = torso({}, 10, -3 * kick)
    arm(pose, "R", 88 + 14 * kick, 6 + 10 * kick, -6, -102 + 18 * kick)   # the barrel in line with the arm, kicking up
    arm(pose, "L", 80 + 10 * kick, 42, 38)       # the support hand under the barrel
    pose["neck"] = [("axis", UP, deg(-4))]
    return pose


def bow(t):
    draw = tl(t, [(0, 0.1), (0.5, 1, "out"), (0.6, 1, "lin"), (0.66, -0.1, "in"), (1, 0.1, "io")])
    pose = torso({}, 30, 0)
    arm(pose, "R", 90, 2, -26)                       # the bow arm, square to the target
    arm(pose, "L", 84 - 4 * draw, 30 + 115 * draw, 44 - 22 * draw)   # the string hand, drawn back to the cheek
    pose["clavicle.L"] = [("swing", BACK, deg(10 * draw))]
    return pose


def cast(t):
    push = tl(t, [(0, 0.2), (0.22, 0, "out"), (0.44, 1.1, "in"), (0.56, 1, "out"), (1, 0.6, "io")])
    pose = torso({}, 12 * push - 6, 6 * push - 2)
    arm(pose, "R", 40 + 54 * push, 70 - 64 * push, 4, -30 - 66 * push)   # the staff tips toward the target
    arm(pose, "L", 30 + 30 * push, 60 - 20 * push, 16)
    return pose


def drink(t):
    lift = tl(t, [(0, 0), (0.3, 1, "out"), (0.78, 1, "lin"), (1, 0, "io")])
    tip = tl(t, [(0, 0), (0.34, 0), (0.46, 1, "io"), (0.74, 1, "lin"), (0.9, 0, "io")])
    pose = torso({}, 6 * lift, 0, look=False)
    pose["neck"] = [("swing", BACK, deg(10 * tip))]
    pose["head"] = [("swing", BACK, deg(20 * tip))]
    arm(pose, "R", 20 + 38 * lift + 8 * tip, 20 + 118 * lift, 30 * lift)
    arm(pose, "L", 14, 20, 0)
    return pose


def use(t):
    lift = tl(t, [(0, 0.1), (0.35, 1, "back"), (0.7, 1, "lin"), (1, 0.3, "io")])
    pose = torso({}, 8 * lift, 3 * lift)
    arm(pose, "R", 20 + 62 * lift, 30 - 14 * lift, 6, -40 * lift)
    arm(pose, "L", 18, 24, 0)
    return pose


def hold(t):
    pose = {}
    arm(pose, "R", 52 + 2 * wave(t, 2), 70 + 2 * wave(t, 2, 0.2), 12)
    return pose


def clips(m):
    """name -> (length in seconds, pose function); the game rescales every action to its item's use time."""
    return {
        "swing": (0.5, swing), "mine": (0.46, mine), "thrust": (0.4, thrust), "shoot": (0.5, shoot),
        "bow": (0.7, bow), "cast": (0.5, cast), "drink": (0.8, drink), "use": (0.4, use), "hold": (2.0, hold),
    }

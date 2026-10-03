"""Upper-body item actions for generated humans. The game layers these over locomotion (filtered to the spine, head
and arms) and time-scales each to the item's use time, so they only need the right shape and rhythm:

swing   Terraria's overhead arc: the blade raised behind the head, a fast arc over the top, follow through low across
        the body, recover.
mine    a tool chop from over the shoulder with a little bounce on impact.
thrust  an underhand lunge from the hip: the arm drives forward and down while the shaft stays level (spears,
        shortswords).
shoot   the gun arm straight out at the target, the free hand cupping the gun hand, a recoil kick and settle.
bow     the bow arm held out, the other hand drawing the string back to the cheek and releasing.
cast    a gathering pull then a push that tips the staff toward the target (staffs, wands, tomes).
drink   the hand brought to the mouth, head tipped back.
use     the item lifted out in front (placing, summoning, other uses).
hold    a carried light held upright out beside the shoulder (loops; the game plays it on the right arm only).

The right arm's main angle is its pitch in the side plane: 0 hanging down, 90 straight out in front, 180 overhead,
over 180 reaching behind the head. The relaxed stance under every action adds about 16 to the item angle below. The right hand is a fist around the handle (motion.GRIP); held items leave it past
the thumb, at right angles to the forearm, so in the side plane an item points at pitch + elbow + wrist + 90 (minus
the game's grip tilt for guns, see TILT). Wrists stay within a natural range (about -70..+25 degrees): blades come in
line with a strike through the arm and body, not by folding the wrist flat.
"""
from .motion import BACK, FWD, SIDE_AXIS, UP, deg, tl, wave

# Degrees the game tilts an item in the fist toward the fingers (along the forearm) for each action, so a pistol's
# barrel runs along the forearm like a real grip; keep in step with PlayerAvatar.HeldTilt.
TILT = {"shoot": 70}


def arm(pose, side, pitch, elbow, inward=0.0, wrist=0.0, roll=0.0):
    """An arm by its pitch (see above), elbow bend, sweep across the body, wrist cock and inward roll of the upper arm
    (turning the elbow out so the forearm folds across the body), all in degrees."""
    sx = 1 if side == "R" else -1
    pose[f"upper_arm.{side}"] = [("axis", SIDE_AXIS, deg(pitch)), ("axis", UP, deg(inward * sx))]
    if roll:
        pose[f"upper_arm.{side}"].append(("roll", -deg(roll) * sx))
    pose[f"forearm.{side}"] = [("swing", FWD, deg(elbow))]
    if wrist:
        pose[f"hand.{side}"] = [("local", (1, 0, 0), deg(wrist))]   # + toward the thumb side, - toward the little finger
    return pose


def torso(pose, twist=0.0, lean=0.0, look=True):
    """Turns the torso (+ brings the right shoulder forward) and leans it (+ forward); the head keeps facing ahead."""
    pose["spine"] = [("axis", UP, deg(twist * 0.4)), ("swing", FWD if lean >= 0 else BACK, deg(abs(lean) * 0.5))]
    pose["chest"] = [("axis", UP, deg(twist * 0.6)), ("swing", FWD if lean >= 0 else BACK, deg(abs(lean) * 0.5))]
    if look:
        pose["head"] = [("axis", UP, deg(-twist * 0.75)), ("swing", BACK if lean >= 0 else FWD, deg(abs(lean) * 0.6))]
    return pose


def swing(t):
    # Item angle (pitch + elbow + wrist + 90): ~205 raised behind the head, ~245 at the top of the wind-up, through 90
    # (level, pointing ahead) just after the strike key, ~55 at the end of the follow-through low across the body.
    pitch = tl(t, [(0, 140), (0.22, 168, "out"), (0.46, 66, "in"), (0.62, 14, "out"), (1, 40, "io")])
    elbow = tl(t, [(0, 25), (0.22, 32, "out"), (0.46, 6, "in"), (0.62, 12), (1, 24)])
    inward = tl(t, [(0, -6), (0.22, -10), (0.46, 12, "in"), (0.62, 30, "out"), (1, 12)])
    wrist = tl(t, [(0, -50), (0.22, -45), (0.46, -40, "in"), (0.62, -50), (1, -30)])
    twist = tl(t, [(0, -16), (0.22, -28, "out"), (0.46, 22, "in"), (0.62, 32, "out"), (1, 6)])
    lean = tl(t, [(0, -3), (0.22, -8), (0.46, 12, "in"), (0.62, 17), (1, 3)])
    pose = torso({}, twist, lean)
    arm(pose, "R", pitch, elbow, inward, wrist)
    counter = tl(t, [(0, 30), (0.22, 48), (0.46, -18, "in"), (0.62, -24), (1, 8)])
    arm(pose, "L", counter, 30 + 0.3 * max(0.0, counter), 14)
    return pose


def mine(t):
    # A shorter chop: the head of the tool from up behind the shoulder (~225) down to just past level (~70) on impact.
    pitch = tl(t, [(0, 112), (0.26, 160, "out"), (0.5, 32, "in"), (0.58, 42, "out"), (1, 100, "io")])
    elbow = tl(t, [(0, 50), (0.26, 70, "out"), (0.5, 6, "in"), (0.58, 12), (1, 44)])
    twist = tl(t, [(0, -8), (0.26, -20), (0.5, 16, "in"), (0.58, 13), (1, -2)])
    lean = tl(t, [(0, 0), (0.26, -6), (0.5, 16, "in"), (0.58, 14), (1, 3)])
    pose = torso({}, twist, lean)
    arm(pose, "R", pitch, elbow, 6, tl(t, [(0, -40), (0.26, -45), (0.5, -55, "in"), (0.58, -52), (1, -40)]))
    arm(pose, "L", 40, 92, 22, roll=40)       # the free hand braced at the chest
    return pose


def thrust(t):
    # The shaft stays near level (item angle ~90) while the arm drives from the hip forward and down to full reach.
    pitch = tl(t, [(0, 30), (0.3, 8, "out"), (0.5, 52, "in"), (0.66, 50), (1, 32, "io")])
    elbow = tl(t, [(0, 50), (0.3, 72, "out"), (0.5, 0, "in"), (0.66, 3), (1, 44)])
    wrist = tl(t, [(0, -55), (0.3, -60, "out"), (0.5, -70, "in"), (0.66, -70), (1, -58)])
    twist = tl(t, [(0, 0), (0.3, -24, "out"), (0.5, 26, "in"), (0.66, 24), (1, 2)])
    lean = tl(t, [(0, 0), (0.3, -4), (0.5, 14, "in"), (0.66, 12), (1, 1)])
    pose = torso({}, twist, lean)
    pose["clavicle.R"] = [("swing", FWD, deg(tl(t, [(0, 0), (0.3, -6), (0.5, 14, "in"), (1, 0)])))]
    arm(pose, "R", pitch, elbow, 6, wrist)
    arm(pose, "L", tl(t, [(0, 20), (0.3, 36), (0.5, -12, "in"), (1, 14)]), 34, 10)
    return pose


def shoot(t):
    # The gun is tilted along the forearm in the fist (TILT): barrel angle = pitch + elbow + wrist + 90 - 70.
    kick = tl(t, [(0, 0), (0.08, 1, "out"), (0.45, 0, "io"), (1, 0)])
    pose = torso({}, 14, -3 * kick)
    arm(pose, "R", 86 + 7 * kick, 2 + 6 * kick, -4, -26 + 9 * kick)        # straight at the target, muzzle climbing
    arm(pose, "L", 74 + 6 * kick, 20, 40, 0, roll=30)                       # the free hand cupping the gun hand
    pose["neck"] = [("axis", UP, deg(-5))]
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
    arm(pose, "R", 40 + 54 * push, 60 - 54 * push, 4, -10 - 40 * push)   # the staff tips from upright toward the target
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
    arm(pose, "R", 20 + 58 * lift, 30 - 10 * lift, 6, -30 * lift)        # held out in front, the item tipped up and forward
    arm(pose, "L", 18, 24, 0)
    return pose


def hold(t):
    # Forearm level (pitch + elbow ~ 80) so the light stands nearly upright, out beside the shoulder where it lights
    # the way without hiding the face.
    pose = {}
    arm(pose, "R", 34 + 2 * wave(t, 2), 46 + 2 * wave(t, 2, 0.2), -24, 4)
    return pose


def clips(m):
    """name -> (length in seconds, pose function); the game rescales every action to its item's use time."""
    return {
        "swing": (0.5, swing), "mine": (0.46, mine), "thrust": (0.4, thrust), "shoot": (0.5, shoot),
        "bow": (0.7, bow), "cast": (0.5, cast), "drink": (0.8, drink), "use": (0.4, use), "hold": (2.0, hold),
    }

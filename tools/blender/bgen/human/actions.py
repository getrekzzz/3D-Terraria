"""Upper-body item actions for generated humans. The game layers these over locomotion (filtered to the spine, head
and arms) and time-scales each to the item's use time, so they only need the right shape and rhythm:

swing   Terraria's overhead arc: from a guard, a settle, the blade wound up behind the right shoulder, a whip over the
        top, follow through low across the body to the left, settle and recover.
mine    a tool chop from over the shoulder that stops dead on impact, kicks back and lifts again (repeats back to back).
thrust  an underhand lunge from the hip: drawn back and held, then a drive that overshoots full reach while the shaft
        stays level (spears, shortswords).

Swing and mine strike between about 0.36 and 0.56 of the clip: the melee system sweeps its hits from the player's right
to their left in that window (MeleeSystem.SweepProgress) and the game draws the blade's trail there, so keep the strike
there when retiming them. Keys for joints further down the arm trail those above (torso, shoulder, arm, elbow, wrist),
so the blade drags and then snaps through.
shoot   the gun arm straight out at the target, the free hand cupping the gun hand, a recoil kick and settle.
bow     the bow arm held out, the other hand drawing the string back to the cheek and releasing.
cast    a gathering pull then a push that tips the staff toward the target (staffs, wands, tomes).
drink   the hand brought to the mouth, head tipped back.
use     the item lifted out in front (placing, summoning, other uses).
hold    a carried light held upright out beside the shoulder (loops; the game plays it on the right arm only).

The right arm's main angle is its pitch in the side plane: 0 hanging down, 90 straight out in front, 180 overhead,
over 180 reaching behind the head. The relaxed stance under every action adds about 16 to the item angle below. The
right hand is a fist around the handle (motion.GRIP); held items leave it past the thumb, at right angles to the
forearm, so in the side plane an item points at pitch + elbow + wrist + 90 (minus the game's grip tilt for guns, see
TILT). Wrists stay within a natural range (about -70..+25 degrees): blades come in line with a strike through the arm
and body, not by folding the wrist flat. Sweeping an arm across the body (inward) only turns it about the vertical, so
an arm raised overhead lifts out to the side instead (out) to keep the blade over its own shoulder.
"""
from .motion import BACK, FWD, SIDE_AXIS, UP, deg, tl, wave

# Degrees the game tilts an item in the fist toward the fingers (along the forearm) for each action, so a pistol's
# barrel runs along the forearm like a real grip; keep in step with PlayerAvatar.HeldTilt.
TILT = {"shoot": 70}


def arm(pose, side, pitch, elbow, inward=0.0, wrist=0.0, roll=0.0, out=0.0):
    """An arm by its pitch (see above), elbow bend, sweep across the body, wrist cock, inward roll of the upper arm
    (turning the elbow out so the forearm folds across the body) and lift out to the side, all in degrees."""
    sx = 1 if side == "R" else -1
    pose[f"upper_arm.{side}"] = [("axis", SIDE_AXIS, deg(pitch)), ("axis", FWD, deg(-out * sx)),
                                 ("axis", UP, deg(inward * sx))]
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


def shoulder(pose, side, raise_, forward):
    """Shrugs the shoulder up (+) and drives it forward (+) or draws it back (-), in degrees."""
    pose[f"clavicle.{side}"] = [("swing", UP, deg(raise_)), ("swing", FWD if forward >= 0 else BACK, deg(abs(forward)))]
    return pose


def swing(t):
    # From a guard (the blade forward and up, item angle ~140) the body settles, then winds up: the torso turns the
    # right shoulder back first and the arm follows it up and out until the blade lies behind the head (~275). The
    # torso starts to unwind while the blade is still dragging back (~290), then the arm whips over and the wrist snaps
    # through last, so the blade is fastest just above level in front (~115); it follows through low across the body
    # (~75), overshoots, settles and eases back to the guard. Each joint's keys trail the one above it.
    pitch = tl(t, [(0, 32), (0.08, 26), (0.26, 150, "io"), (0.32, 146), (0.46, 54, "in"), (0.6, 10, "out"),
                   (0.7, 16), (1, 32)])
    out = tl(t, [(0, 0), (0.08, 0), (0.26, 30, "io"), (0.32, 28), (0.46, 6, "in"), (0.6, 0, "out"), (1, 0)])
    elbow = tl(t, [(0, 40), (0.08, 48), (0.26, 80, "io"), (0.33, 88), (0.47, 6, "in"), (0.61, 16, "out"),
                   (0.72, 14), (1, 40)])
    wrist = tl(t, [(0, -40), (0.1, -44), (0.28, -20, "io"), (0.35, -8), (0.48, -46, "in"), (0.63, -58, "out"),
                   (0.76, -52), (1, -40)])
    inward = tl(t, [(0, 8), (0.08, 8), (0.26, 0, "io"), (0.33, 0), (0.47, 10, "in"), (0.61, 44, "out"),
                    (0.72, 38), (1, 8)])
    roll = tl(t, [(0, 0), (0.33, 0), (0.47, 4, "in"), (0.61, 14, "out"), (1, 0)])
    twist = tl(t, [(0, 0), (0.06, 3), (0.22, -30, "io"), (0.3, -22), (0.42, 24, "in"), (0.56, 34, "out"),
                   (0.68, 28), (1, 0)])
    lean = tl(t, [(0, 0), (0.08, 2), (0.24, -8, "io"), (0.32, -6), (0.44, 12, "in"), (0.58, 16, "out"), (0.7, 13),
                  (1, 0)])
    pose = torso({}, twist, lean)
    shoulder(pose, "R", tl(t, [(0, 0), (0.24, 10, "out"), (0.36, 4), (0.46, -2, "in"), (1, 0)]),
             tl(t, [(0, 0), (0.24, -6, "out"), (0.46, 10, "in"), (0.6, 12), (1, 0)]))
    arm(pose, "R", pitch, elbow, inward, wrist, roll, out)
    counter = tl(t, [(0, 18), (0.08, 14), (0.24, 52, "out"), (0.32, 46), (0.45, -16, "in"), (0.6, -22, "out"),
                     (1, 18)])
    arm(pose, "L", counter, 30 + 0.3 * max(0.0, counter), 14)
    return pose


def mine(t):
    # A chop that repeats back to back: from behind the shoulder (item angle ~220) the tool lifts further back (~290)
    # while the torso coils, the shoulder drives it down hardest at the end so its head lands just past level (~85),
    # where it stops dead, kicks back up off the block, settles and lifts again.
    pitch = tl(t, [(0, 104), (0.22, 158, "out"), (0.28, 154), (0.46, 30, "in"), (0.5, 40, "out"), (0.56, 33),
                   (0.64, 36), (1, 104, "io")])
    elbow = tl(t, [(0, 48), (0.22, 72, "out"), (0.3, 76), (0.47, 6, "in"), (0.51, 14, "out"), (0.57, 10),
                   (0.66, 16), (1, 48, "io")])
    wrist = tl(t, [(0, -40), (0.24, -46, "out"), (0.31, -36), (0.47, -56, "in"), (0.52, -48, "out"), (0.58, -54),
                   (0.7, -50), (1, -40, "io")])
    twist = tl(t, [(0, -8), (0.2, -22, "out"), (0.27, -18), (0.43, 16, "in"), (0.5, 12, "out"), (0.6, 14),
                   (1, -8, "io")])
    lean = tl(t, [(0, 0), (0.22, -6, "out"), (0.28, -5), (0.45, 17, "in"), (0.5, 13, "out"), (0.58, 15),
                  (1, 0, "io")])
    pose = torso({}, twist, lean)
    shoulder(pose, "R", tl(t, [(0, 4), (0.22, 12, "out"), (0.46, -3, "in"), (0.52, 0), (1, 4, "io")]),
             tl(t, [(0, 0), (0.22, -6, "out"), (0.46, 12, "in"), (0.52, 8), (1, 0, "io")]))
    arm(pose, "R", pitch, elbow, 6, wrist)
    brace = tl(t, [(0, 0), (0.46, 0), (0.5, 1, "out"), (0.7, 0, "io"), (1, 0)])
    arm(pose, "L", 40 - 6 * brace, 92 + 6 * brace, 22, roll=40)       # the free hand braced at the chest
    return pose


def thrust(t):
    # The shaft stays level (item angle ~95; ~110 at the guard) and moves along its own line: the arm draws back past
    # the hip with the torso coiled and holds for a beat, then everything drives forward at once and the arm overshoots
    # full reach before settling back to the guard.
    pitch = tl(t, [(0, 30), (0.24, -18, "io"), (0.32, -20), (0.48, 60, "back"), (0.66, 56), (1, 30, "io")])
    elbow = tl(t, [(0, 40), (0.24, 78, "io"), (0.33, 82), (0.49, 0, "back"), (0.66, 4), (1, 40, "io")])
    wrist = tl(t, [(0, -66), (0.26, -70, "io"), (0.34, -70), (0.5, -70, "back"), (0.68, -68), (1, -66, "io")])
    twist = tl(t, [(0, 0), (0.22, -26, "io"), (0.31, -24), (0.46, 28, "back"), (0.66, 22), (1, 0, "io")])
    lean = tl(t, [(0, 0), (0.24, -5, "io"), (0.32, -4), (0.47, 15, "back"), (0.66, 12), (1, 0, "io")])
    pose = torso({}, twist, lean)
    shoulder(pose, "R", 0, tl(t, [(0, 0), (0.24, -10, "io"), (0.32, -10), (0.48, 16, "back"), (0.66, 12), (1, 0)]))
    arm(pose, "R", pitch, elbow, 6, wrist)
    arm(pose, "L", tl(t, [(0, 20), (0.24, 38, "io"), (0.32, 36), (0.47, -14, "in"), (1, 14)]), 34, 10)
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

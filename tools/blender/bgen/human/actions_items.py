"""Ranged, consumable and tether actions for generated humans (the melee and gesture clips, and the helpers these use,
are in human/actions.py, which explains the arm angles). Ranged items fire as their use starts, as in Terraria, so
each clip lets go of its shot in the first moments:

gun      both hands on the weapon, held out along the aim (the gun lies along the forearm in the fist, like a real
         pistol grip), a recoil kick over the first eighth that settles (pistols, rifles and launchers alike).
bow      drawn to the cheek at the start, the string snaps at the release (the arrow leaves at once, as in Terraria),
         the hand settles, nocks and draws again through the re-draw window and holds drawn (auto-fire chains).
staff    the arm out at the target with the staff tilted 30 degrees up, gem forward, lifting a little as the bolt leaves.
tome     the book held open before the chest, pushed toward the target as the spell leaves, the free palm with it.
throw    overhand: the arm cocked behind the head, whipped over and forward to let go at the release, followed through
         low and brought back up (boomerangs, knives, bombs).
hold_up  the item raised high overhead and held there (summons, crystals, mirrors, crates).
drink    the hand brought to the mouth, head tipped back.
eat      the food brought to the mouth for two bites, the head dipping into each.
yoyo     the right arm out in front working the string with wrist flicks (loops; right arm only, like hold).
"""
import math

from .actions import arm, hump, keyed, torso
from .motion import BACK, FWD, UP, deg, tl, wave


def gun(t):
    # The gun lies along the forearm in the fist (actions.TILT, the style's hand frame): its barrel points at
    # pitch + elbow + wrist + ITEM - TILT, level with the arm straight out and the wrist easy.
    kick = tl(t, [(0, 0), (0.04, 1, "out"), (0.12, 0.25, "io"), (0.3, 0, "io"), (1, 0)])
    pose = torso({}, 14, -3 * kick)
    arm(pose, "R", 86 + 7 * kick, 2 + 6 * kick, -4, -26 + 9 * kick)       # straight at the target, the muzzle climbing
    arm(pose, "L", 74 + 6 * kick, 20, 40, 0, roll=30)                       # the free hand cupping the gun hand
    pose["neck"] = [("axis", UP, deg(-5))]
    return pose


def bow(t, w, m):
    r, a, b = w["release"], w["redrawStart"], w["redrawEnd"]
    snap = min(r + 0.06, (r + a) / 2)
    # 1 = drawn to the cheek; past 0 the hand follows the string forward. Drawn until the release, snaps, settles,
    # takes the next arrow to the string at the re-draw start and is drawn again by its end.
    draw = tl(t, [(0, 1), (r, 1, "lin"), (snap, -0.12, "out"), ((snap + a) / 2, 0.12, "io"), (a, 0.0, "io"), (b, 1, "io"), (1, 1, "lin")])
    kick = tl(t, [(0, 0), (r, 0, "lin"), (snap, 1, "out"), (a, 0, "io")])
    pose = torso({}, 30, 0)
    arm(pose, "R", 90 + 5 * kick, 2 + 6 * kick, -26)        # the bow arm, square to the target, jolted by the shot
    arm(pose, "L", 84 - 4 * draw, 30 + 115 * draw, 44 - 22 * draw)   # the string hand, drawn back to the cheek
    pose["clavicle.L"] = [("swing", BACK, deg(10 * max(0.0, draw)))]
    loose = tl(t, [(0, 0), (r, 0, "lin"), (snap, 1, "out"), (a, 0, "io")])
    return {**pose, **m.fingers("L", -0.25 * loose, -0.3 * loose)}   # the fingers spring open as the string goes


def staff(t):
    lift = tl(t, [(0, 0), (0.1, 1, "out"), (0.32, 0.5, "io"), (1, 0, "io")])
    pose = torso({}, 14, 2 + 3 * lift)
    arm(pose, "R", 80 + 8 * lift, 12 + 4 * lift, -4, -62 + 4 * lift)   # out at the target, the staff tilted 30 up
    arm(pose, "L", 24, 34, 10)
    return pose


def tome(t, m):
    push = tl(t, [(0, 0), (0.1, 1, "out"), (0.3, 0.75, "io"), (1, 0, "io")])
    pose = torso({}, 10 + 6 * push, 3 + 5 * push)
    pose["head"] += [("swing", FWD, deg(6))]                           # reading over the book
    arm(pose, "R", 62 + 18 * push, 64 - 34 * push, 26, -36)          # the book held open, pushed out with the spell
    arm(pose, "L", 56 + 26 * push, 70 - 44 * push, 38 - 8 * push, -20 * push)   # the free palm pushing beside it
    return {**pose, **m.fingers("L", 0.1 - 0.5 * push, 0.2 - 0.3 * push)}   # the palm opens toward the target


def throw(t, w, m):
    r = w["release"]
    through, low = min(r + 0.14, (r + 1) / 2), min(r + 0.34, (r + 2) / 3)
    keys = [(0, "lin"), (r, "in"), (through, "out"), (low, "io"), (1, "io")]
    #                       cocked  release  through  low  cocked
    pitch = keyed(t, keys, (152, 118, 52, 36, 152))
    elbow = keyed(t, keys, (80, 30, 8, 20, 80))
    wrist = keyed(t, keys, (-110, -100, -96, -70, -110))   # the item up and back behind the head, flung over
    twist = keyed(t, keys, (-28, 6, 26, 22, -28))
    lean = keyed(t, keys, (-8, 4, 14, 10, -8))
    pose = torso({}, twist, lean)
    arm(pose, "R", pitch, elbow, 4, wrist)
    arm(pose, "L", keyed(t, keys, (70, 40, -6, -2, 70)), 24, 18)   # aimed, then pulled down
    opened = tl(t, [(0, 0), (r * 0.8, 0, "lin"), (min(r + 0.06, through), 1, "out"), (low, 1, "lin"), (1, 0, "io")])
    return {**pose, **m.fingers("R", -0.45 * opened, -0.3 * opened)}   # the fingers open as it leaves the hand


def hold_up(t):
    up = tl(t, [(0, 0), (0.25, 1, "out"), (0.8, 1, "lin"), (1, 0.2, "io")])
    pose = torso({}, 4 * up, -6 * up, look=False)
    pose["neck"] = [("swing", BACK, deg(8 * up))]
    pose["head"] = [("swing", BACK, deg(18 * up))]                     # looking up at it
    arm(pose, "R", 18 + 146 * up, 24 - 14 * up, -10 * up, -88 * up)   # high overhead, the item upright in the fist
    arm(pose, "L", 14 + 10 * up, 24, -6 * up)
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


def eat(t):
    lift = tl(t, [(0, 0), (0.2, 1, "out"), (0.82, 1, "lin"), (1, 0, "io")])
    bite = hump(t, 0.24, 0.44) + hump(t, 0.5, 0.7)                    # two bites
    pose = torso({}, 6 * lift, 4 * lift + 3 * bite, look=False)
    pose["neck"] = [("swing", FWD, deg(4 * bite))]
    pose["head"] = [("swing", FWD, deg(4 * lift + 10 * bite))]         # dipping into each bite
    arm(pose, "R", 22 + 34 * lift + 6 * bite, 24 + 106 * lift + 8 * bite, 32 * lift, -30 * lift)
    arm(pose, "L", 14 + 10 * lift, 24 + 20 * lift, 10 * lift)
    return pose


def yoyo(t):
    flick = wave(t, 2)                                                 # two flicks per loop
    snap = max(0.0, math.sin(4 * math.pi * t)) ** 3
    pose = {}
    arm(pose, "R", 62 + 6 * flick, 48 - 10 * flick, 4, -30 + 34 * flick + 10 * snap)   # the wrist working the string
    return pose


def clips(m, w):
    """name -> (length in seconds, pose function), merged into human/actions.clips; ``w`` is its timing."""
    return {
        "gun": (0.5, gun), "bow": (w["bow"]["length"], lambda t: bow(t, w["bow"], m)), "staff": (0.5, staff),
        "tome": (0.5, lambda t: tome(t, m)), "throw": (w["throw"]["length"], lambda t: throw(t, w["throw"], m)),
        "hold_up": (0.8, hold_up), "drink": (0.8, drink), "eat": (0.9, eat), "yoyo": (0.8, yoyo),
    }

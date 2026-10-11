"""Upper-body item actions for generated humans. The game layers these over locomotion (filtered to the spine, head
and arms) and time-scales each to the item's use animation, so they only need the right shape and rhythm. Every item
use style (data/config/use_styles.json "styles") names the clip it plays. The clips that gameplay times take their
length and windows from that file's "clips", the file Core reads for the strike window
(src/Strata.Core/Presentation/UseStyleConfig.cs), so what the body shows and what the game does line up. This module
holds the melee, tool and gesture clips; human/actions_items.py the ranged, consumable and tether clips:

swing   Terraria's overhead arc: from a raised guard (the blade upright before the right shoulder) the torso coils, then
        unwinds as the arm cuts down in front through the strike window and follows through low across the body to
        the left, and the blade is brought back up to the guard (a chained swing carries on from where the last one
        ended). The blade's angle is the style's bladeCurve, the curve the game sweeps the hitbox and trail by, so what
        is seen is what hits; the joints share it out, each trailing the one above (torso, shoulder, arm, elbow, wrist).
mine    a tool chop from behind the shoulder: the tool lifts back while the torso coils, the shoulder drives it down to
        land just past level at the strike end, where it stops dead, kicks back up off the block and lifts again.
rapier  a quick jab (shortswords): a small pull back, the arm straight out at the target by the strike end with the
        blade level, held, back to the guard.
spear   an underhand lunge from the hip: drawn back through the wind-up, everything drives forward at once to full
        reach by the strike end (the shaft level all the way), held, then back to the guard.
whip    the arm wound back over the shoulder through the wind-up, lashed forward, the wrist cracking at the strike end.
place   a short reach forward and down to the block, a tap, and back.
use     the item lifted out in front (items without a use style).
wave    a greeting: the hand raised beside the head, waving.
hold    a carried light held upright out beside the shoulder (loops; the game plays it on the right arm only).
flail   the right arm swinging a flail round over the head (loops; right arm only, like hold).

The right arm's main angle is its pitch in the side plane: 0 hanging down, 90 straight out in front, 180 overhead,
over 180 reaching behind the head. Keep the upper arm short of straight up (about 170 at most) at a clip's start and
end and wherever the game fades it: the game blends an action in and out by the shortest turn from the rest pose, so
an arm turned past straight up would swing the other way round for a frame; fold the elbow to reach behind the head
instead (Motion.stance gives actions a living person's arms on every body for the same reason).

The right hand is a fist around the handle (motion.GRIP); held items leave it past the thumb, at right angles to the
forearm, so in the side plane an item points at pitch + elbow + wrist + 90, plus about 16 from the relaxed stance
under every action (ITEM), minus the tilt a style's hand frame gives it in the fist (guns lie along the forearm like
a real pistol grip, see TILT). Wrists stay within a natural range (about -70..+25 degrees): blades come in line with a
strike through the arm and body, not by folding the wrist flat. Sweeping an arm across the body (inward) only turns
it about the vertical, so an arm raised overhead lifts out to the side instead (out) to keep the blade over its own
shoulder.
"""
import json
import math
import pathlib
import re

from .motion import BACK, FWD, SIDE_AXIS, UP, deg, tl, wave

USE_STYLES = pathlib.Path(__file__).resolve().parents[4] / "data" / "config" / "use_styles.json"
_COMMENTS = re.compile(r'"(?:\\.|[^"\\])*"|//[^\n]*|/\*.*?\*/', re.S)
_TRAILING = re.compile(r'"(?:\\.|[^"\\])*"|,(?=\s*[}\]])')

# Clip -> the timing keys it is authored to (besides "length").
TIMED = {
    "swing": ("strikeStart", "strikeEnd"), "mine": ("strikeStart", "strikeEnd"), "rapier": ("strikeStart", "strikeEnd"),
    "spear": ("strikeStart", "strikeEnd"), "whip": ("strikeStart", "strikeEnd"), "throw": ("release",),
    "bow": ("release", "redrawStart", "redrawEnd"),
}

# Degrees a clip's use style tilts the item in the fist toward the fingers (its hand frame in use_styles.json turns
# 90 - TILT about the hand's X instead of 90), so a pistol's barrel runs along the forearm; tools/blender/preview.py
# --hold=<clip> tilts its stand-in rod the same way.
TILT = {"gun": 70}

# What the relaxed stance under every action adds to the item's angle in the side plane (see above).
ITEM = 106


def load_jsonc(path):
    """Reads a data file: JSON with // and /* */ comments and trailing commas (strings are left alone)."""
    keep = lambda m: m.group(0) if m.group(0).startswith('"') else ""
    return json.loads(_TRAILING.sub(keep, _COMMENTS.sub(keep, path.read_text(encoding="utf-8"))))


def timing():
    """use_styles.json: clip name -> its "clips" entry (length and windows), plus "bladeCurve": the swing's curve."""
    data = load_jsonc(USE_STYLES)
    clips = data["clips"]
    for name, keys in TIMED.items():
        missing = [k for k in ("length",) + keys if k not in clips.get(name, {})]
        if missing:
            raise SystemExit(f"ERROR: {USE_STYLES}: clip '{name}' needs {', '.join(missing)}")
    curve = data.get("styles", {}).get("swing", {}).get("bladeCurve")
    if not curve or len(curve) < 2:
        raise SystemExit(f"ERROR: {USE_STYLES}: styles.swing needs a bladeCurve")
    return {**clips, "bladeCurve": [(float(k[0]), float(k[1]), k[2]) for k in curve]}


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


def keyed(t, keys, values):
    """One channel over shared key times: keys = [(time, ease), ...], values = one value per key."""
    return tl(t, [(k, v, e) for (k, e), v in zip(keys, values)])


def hump(t, start, end):
    """0 outside [start, end], rising smoothly to 1 halfway through."""
    if not start < t < end:
        return 0.0
    return math.sin(math.pi * (t - start) / (end - start)) ** 2


def retimed(t, ours, authored):
    """Maps a time on this clip's timeline (keys ``ours``) onto the timeline a pose was authored on (``authored``),
    piecewise linearly, so an authored motion lands on the windows use_styles.json gives the clip."""
    for (a0, a1), (b0, b1) in zip(zip(ours, ours[1:]), zip(authored, authored[1:])):
        if t <= a1:
            return b0 + (b1 - b0) * (t - a0) / max(a1 - a0, 1e-9)
    return authored[-1]


def swing(t, w, blade):
    a, b = w["strikeStart"], w["strikeEnd"]
    f = b + 0.3 * (1 - b)                   # the end of the follow-through; the rest brings the blade back up
    r = f + 0.55 * (1 - f)                  # the elbow folds early on the way back, so the blade rises over it
    # Each joint trails the one above it: the torso unwinds first, the shoulder drives, the elbow opens, the wrist
    # snaps through last (its ulnar cock grows through the cut), so the blade drags and then whips over.
    lean = tl(t, [(0, -4), (a, -7, "out"), (a + 0.35 * (b - a), 6, "io"), (b, 14, "io"), (f, 15, "out"), (1, -4, "io")])
    twist = tl(t, [(0, -18), (a, -26, "out"), (a + 0.3 * (b - a), -6, "io"), (b - 0.1 * (b - a), 26, "io"),
                   (f, 32, "out"), (r, 12, "io"), (1, -18, "io")])
    elbow = tl(t, [(0, 50), (a, 58, "out"), (a + 0.4 * (b - a), 40, "io"), (a + 0.75 * (b - a), 12, "io"), (b, 6, "out"),
                   (f, 12, "io"), (r, 52, "io"), (1, 50, "io")])
    wrist = tl(t, [(0, -32), (a, -28, "out"), (a + 0.45 * (b - a), -26, "io"), (a + 0.8 * (b - a), -48, "in"),
                   (b, -60, "out"), (f, -62, "io"), (r, -44, "io"), (1, -32, "io")])
    inward = tl(t, [(0, 4), (a, 0, "out"), (a + 0.5 * (b - a), 6, "io"), (b, 26, "io"), (f, 40, "out"), (r, 18, "io"),
                    (1, 4, "io")])
    roll = tl(t, [(0, 0), (a + 0.5 * (b - a), 0), (b, 8, "io"), (f, 14, "out"), (1, 0, "io")])
    # The arm takes what the elbow and wrist leave of the blade's angle (the gameplay curve).
    pitch = tl(t, blade) - elbow - wrist - ITEM
    out = 14 * max(0.0, min(1.0, (pitch - 60) / 60))      # a raised arm lifts out, keeping the blade over its shoulder
    pose = torso({}, twist, lean)
    shoulder(pose, "R", tl(t, [(0, 8), (a, 12, "out"), (a + 0.5 * (b - a), 3, "io"), (b, -2, "io"), (f, -2), (1, 8, "io")]),
             tl(t, [(0, -4), (a, -8, "out"), (a + 0.5 * (b - a), 8, "io"), (b, 12, "io"), (f, 12), (1, -4, "io")]))
    arm(pose, "R", pitch, elbow, inward, wrist, roll, out)
    counter = tl(t, [(0, 30), (a, 44, "out"), (a + 0.5 * (b - a), 4, "io"), (b, -18, "io"), (f, -22, "out"), (1, 30, "io")])
    arm(pose, "L", counter, 30 + 0.3 * max(0.0, counter), 14)
    return pose


def _chop(t):
    """A chop authored on its own timeline (the impact at 0.46): from behind the shoulder (item angle ~220) the tool
    lifts further back (~290) while the torso coils, the shoulder drives it down hardest at the end so its head lands
    just past level (~85), where it stops dead, kicks back up off the block, settles and lifts again."""
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


def mine(t, w):
    a, b = w["strikeStart"], w["strikeEnd"]
    # The lift back peaks at the wind-up's end and the tool lands at the strike end; the bounce and the lift back up
    # share out the rest.
    after = (1 - b) / 0.54
    return _chop(retimed(t, [0, a * 0.7, a, b, b + 0.04 * after, b + 0.1 * after, b + 0.18 * after, 1],
                         [0, 0.22, 0.28, 0.46, 0.5, 0.56, 0.64, 1]))


def rapier(t, w):
    a, b = w["strikeStart"], w["strikeEnd"]
    held = min(b + 0.2, (b + 1) / 2)        # held out at full length, then drawn back to the guard
    keys = [(0, "lin"), (a, "out"), (b, "in"), (held, "lin"), (1, "io")]
    #                       guard  pulled  jabbed  held  guard
    pitch = keyed(t, keys, (30, 22, 60, 58, 30))      # the blade level all the way (item angle ~110 at the guard, ~96 out)
    elbow = keyed(t, keys, (40, 54, 0, 3, 40))
    wrist = keyed(t, keys, (-66, -68, -70, -68, -66))
    twist = keyed(t, keys, (6, 0, 24, 22, 6))
    lean = keyed(t, keys, (2, 0, 10, 9, 2))
    pose = torso({}, twist, lean)
    shoulder(pose, "R", 0, keyed(t, keys, (2, -4, 14, 13, 2)))
    arm(pose, "R", pitch, elbow, 6, wrist)
    arm(pose, "L", keyed(t, keys, (14, 22, -14, -12, 14)), 36, 10)
    return pose


def _lunge(t):
    """A lunge authored on its own timeline (full reach at 0.48): the shaft stays level (item angle ~95; ~110 at the
    guard) and moves along its own line: the arm draws back past the hip with the torso coiled and holds for a beat,
    then everything drives forward at once and the arm overshoots full reach before settling back to the guard."""
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


def spear(t, w):
    a, b = w["strikeStart"], w["strikeEnd"]
    held = min(b + 0.3, (b + 1) / 2)
    return _lunge(retimed(t, [0, a, b, held, 1], [0, 0.32, 0.48, 0.66, 1]))


def whip(t, w):
    a, b = w["strikeStart"], w["strikeEnd"]
    keys = [(0, "lin"), (a, "out"), (b, "in"), (min(b + 0.18, (b + 1) / 2), "out"), (1, "io")]
    #                       ready  wound   crack  through  ready
    pitch = keyed(t, keys, (62, 150, 96, 48, 62))
    elbow = keyed(t, keys, (36, 86, 4, 18, 36))
    inward = keyed(t, keys, (6, -10, 6, 20, 6))
    wrist = keyed(t, keys, (-60, -105, -96, -70, -60))  # the handle cocked up behind the shoulder, snapped forward at the crack
    twist = keyed(t, keys, (4, -24, 20, 26, 4))
    lean = keyed(t, keys, (2, -6, 12, 14, 2))
    pose = torso({}, twist, lean)
    arm(pose, "R", pitch, elbow, inward, wrist)
    arm(pose, "L", keyed(t, keys, (20, 46, -10, -16, 20)), 34, 12)
    return pose


def place(t):
    tap = tl(t, [(0, 0), (0.45, 1, "out"), (0.6, 0.85, "io"), (1, 0, "io")])
    pose = torso({}, 8 * tap, 6 * tap)
    arm(pose, "R", 30 + 46 * tap, 40 - 30 * tap, 8, -20 - 30 * tap)   # reaching to the block ahead and down
    arm(pose, "L", 16, 24, 0)
    return pose


def use(t):
    lift = tl(t, [(0, 0.1), (0.35, 1, "back"), (0.7, 1, "lin"), (1, 0.3, "io")])
    pose = torso({}, 8 * lift, 3 * lift)
    arm(pose, "R", 20 + 58 * lift, 30 - 10 * lift, 6, -30 * lift)        # held out in front, the item tipped up and forward
    arm(pose, "L", 18, 24, 0)
    return pose


def wave_hand(t):
    up = tl(t, [(0, 0), (0.2, 1, "out"), (0.8, 1, "lin"), (1, 0, "io")])
    waving = up * wave(t, 3)
    pose = torso({}, 6 * up, -2 * up)
    pose["head"] += [("axis", FWD, deg(-5 * up))]                      # a friendly tilt of the head
    arm(pose, "R", 20 + 100 * up, 20 + 35 * up + 12 * waving, -34 * up + 14 * waving)   # the forearm upright by the head
    arm(pose, "L", 12, 18, 0)
    return pose


def hold(t):
    # Forearm level (pitch + elbow ~ 80) so the light stands nearly upright, out beside the shoulder where it lights
    # the way without hiding the face.
    pose = {}
    arm(pose, "R", 34 + 2 * wave(t, 2), 46 + 2 * wave(t, 2, 0.2), -24, 4)
    return pose


def flail(t):
    # One turn per loop: the hand circles over the head with the forearm leading, the wrist rolling the chain round.
    c, s = math.cos(2 * math.pi * t), math.sin(2 * math.pi * t)
    pose = {}
    arm(pose, "R", 146 + 12 * s, 36 + 12 * c, 18 * c - 4, -50 + 18 * s)
    pose["clavicle.R"] = [("swing", UP, deg(6 + 3 * s))]
    return pose


def clips(m):
    """name -> (length in seconds, pose function); the game rescales every action to its item's use animation."""
    from . import actions_items
    w = timing()
    timed = lambda name, fn: (w[name]["length"], lambda t: fn(t, w[name]))
    return {
        "swing": (w["swing"]["length"], lambda t: swing(t, w["swing"], w["bladeCurve"])), "mine": timed("mine", mine),
        "rapier": timed("rapier", rapier), "spear": timed("spear", spear), "whip": timed("whip", whip),
        "place": (0.25, place), "use": (0.4, use), "wave": (1.0, wave_hand), "hold": (2.0, hold), "flail": (0.6, flail),
        **actions_items.clips(m, w),
    }

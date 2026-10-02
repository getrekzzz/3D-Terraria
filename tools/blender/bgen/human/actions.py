"""Upper-body item actions for generated humans. The game layers these over locomotion (filtered to the spine, head
and arms) and time-scales each to the item's use animation, so they only need the right shape and rhythm. Every item
use style (data/config/use_styles.json "styles") names the clip it plays. The clips that gameplay times take their
length and windows from that file's "clips", the file Core reads for the strike window
(src/Strata.Core/Presentation/UseStyleConfig.cs), so what the body shows and what the game does line up. This module
holds the melee, tool and gesture clips; human/actions_items.py the ranged, consumable and tether clips:

swing   Terraria's overhead arc: the blade raised behind the head at the start and end (a chained swing carries on
        from where the last one ended), a short wind-up, the strike over the top and down in front through the strike
        window, a follow-through low, then the blade brought back up. The arm's pitch is the style's bladeCurve, the
        curve the game sweeps the blade by.
mine    a tool chop from over the shoulder that bites early (impact at the strike end), a bounce, then the pull back up.
rapier  a quick jab (shortswords): a small pull back, the arm straight out at the target by the strike end, held, back.
spear   pull back to the hip through the wind-up, lunge the arm straight out by the strike end, hold, retract.
whip    the arm wound back over the shoulder through the wind-up, lashed forward, the wrist cracking at the strike end.
place   a short reach forward and down to the block, a tap, and back.
use     the item lifted out in front (items without a use style).
wave    a greeting: the hand raised beside the head, waving.
hold    a carried light held up in front (loops; the game plays it on the right arm only).
flail   the right arm swinging a flail round over the head (loops; right arm only, like hold).

The right arm's main angle is its pitch in the side plane: 0 hanging down, 90 straight out in front, 180 overhead,
over 180 reaching behind the head. Keep the upper arm short of straight up (about 170 at most) at a clip's start and
end and wherever the game fades it: the game blends an action in and out by the shortest turn from the rest pose, so
an arm turned past straight up would swing the other way round for a frame; fold the elbow to reach behind the head
instead (Motion.stance gives actions a living person's arms on every body for the same reason).

Held items stick out of the fist at right angles to the forearm (a sword carried point-forward, a torch upright); the
wrist angle turns them in line with the arm (about -100 with the elbow's soft bend), so blades, shafts and barrels
extend the arm at the moment of a strike, a thrust or a shot.
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


def keyed(t, keys, values):
    """One channel over shared key times: keys = [(time, ease), ...], values = one value per key."""
    return tl(t, [(k, v, e) for (k, e), v in zip(keys, values)])


def hump(t, start, end):
    """0 outside [start, end], rising smoothly to 1 halfway through."""
    if not start < t < end:
        return 0.0
    return math.sin(math.pi * (t - start) / (end - start)) ** 2


def swing(t, w, blade):
    a, b = w["strikeStart"], w["strikeEnd"]
    f = b + 0.3 * (1 - b)                   # the end of the follow-through; the rest brings the blade back up
    keys = [(0, "lin"), (a, "out"), (b, "io"), (f, "out"), (1, "io")]
    #                       raised  wind-up  struck  follow  raised
    pitch = tl(t, blade)                    # the gameplay blade curve: raised behind, over the top, down in front
    elbow = keyed(t, keys, (58, 72, 6, 14, 58)) + 40 * hump(t, f, 1)     # the elbow folds to lift the blade back
    inward = keyed(t, keys, (0, -4, 12, 22, 0))
    wrist = keyed(t, keys, (-95, -90, -78, -80, -95))   # the blade in line with the arm: raised up-back, extending the strike
    twist = keyed(t, keys, (-20, -26, 22, 28, -20))
    lean = keyed(t, keys, (-5, -7, 13, 15, -5))
    pose = torso({}, twist, lean)
    arm(pose, "R", pitch, elbow, inward, wrist)
    counter = keyed(t, keys, (42, 48, -18, -24, 42))
    arm(pose, "L", counter, 30 + 0.3 * max(0.0, counter), 14)
    return pose


def mine(t, w):
    a, b = w["strikeStart"], w["strikeEnd"]
    keys = [(0, "lin"), (a, "out"), (b, "in"), (min(b + 0.08, (b + 1) / 2), "out"), (1, "io")]
    #                       raised  wind-up  impact  bounce  raised
    pitch = keyed(t, keys, (160, 170, 50, 66, 160))
    elbow = keyed(t, keys, (70, 80, 10, 18, 70))
    twist = keyed(t, keys, (-15, -18, 14, 12, -15))
    lean = keyed(t, keys, (-4, -5, 15, 13, -4))
    wrist = keyed(t, keys, (-90, -85, -95, -88, -90))   # the handle over the shoulder, then in line at the bite
    pose = torso({}, twist, lean)
    arm(pose, "R", pitch, elbow, 6, wrist)
    arm(pose, "L", 38, 88, 26)            # the free hand braced at the chest
    return pose


def rapier(t, w):
    a, b = w["strikeStart"], w["strikeEnd"]
    held = min(b + 0.2, (b + 1) / 2)        # held out at full length, then drawn back to the guard
    keys = [(0, "lin"), (a, "out"), (b, "in"), (held, "lin"), (1, "io")]
    #                       guard  pulled  jabbed  held  guard
    pitch = keyed(t, keys, (72, 64, 90, 89, 72))
    elbow = keyed(t, keys, (58, 74, 2, 4, 58))
    twist = keyed(t, keys, (8, 2, 22, 20, 8))
    lean = keyed(t, keys, (2, 0, 9, 8, 2))
    pose = torso({}, twist, lean)
    pose["clavicle.R"] = [("swing", FWD, deg(keyed(t, keys, (2, 0, 12, 11, 2))))]
    arm(pose, "R", pitch, elbow, 6, -100)          # the blade in line with the forearm, point at the target
    arm(pose, "L", keyed(t, keys, (14, 18, -14, -12, 14)), 36, 10)
    return pose


def spear(t, w):
    a, b = w["strikeStart"], w["strikeEnd"]
    keys = [(0, "lin"), (a, "out"), (b, "in"), (min(b + 0.3, (b + 1) / 2), "lin"), (1, "io")]
    #                       ready  pulled  lunged  held  ready
    pitch = keyed(t, keys, (45, 18, 88, 86, 45))
    elbow = keyed(t, keys, (60, 100, 0, 3, 60))
    twist = keyed(t, keys, (0, -22, 24, 22, 0))
    lean = keyed(t, keys, (0, -4, 12, 10, 0))
    pose = torso({}, twist, lean)
    pose["clavicle.R"] = [("swing", FWD, deg(keyed(t, keys, (0, -6, 12, 11, 0))))]
    arm(pose, "R", pitch, elbow, 8, -104)         # the shaft in line with the arm, like a lance
    arm(pose, "L", keyed(t, keys, (20, 36, -12, -10, 20)), 34, 10)
    return pose


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
    arm(pose, "R", 20 + 62 * lift, 30 - 14 * lift, 6, -40 * lift)
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
    pose = {}
    arm(pose, "R", 52 + 2 * wave(t, 2), 70 + 2 * wave(t, 2, 0.2), 12)
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

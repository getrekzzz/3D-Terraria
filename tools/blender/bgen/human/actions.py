"""Upper-body item actions for generated humans. The game layers these over locomotion (filtered to the spine, head
and arms) and time-scales each to the item's use animation, so they only need the right shape and rhythm. The clips
that gameplay times (swing, mine, thrust, bow) take their length and windows from data/config/use_styles.json, the
file Core reads for the strike window (src/Strata.Core/Presentation/UseStyleConfig.cs), so what the body shows and
what the game does line up:

swing   Terraria's overhead arc: raised behind the head at the start and end (autoswing chains seamlessly), a short
        wind-up, the strike over the top and down in front through the strike window (eased like the gameplay sweep),
        a follow-through low, then the blade brought back up.
mine    a tool chop from over the shoulder that bites early (impact at the strike end), a bounce, then the pull back up.
thrust  pull back to the hip through the wind-up, lunge the arm straight out by the strike end, hold, retract.
shoot   both hands on the weapon at shoulder height, a recoil kick and settle (guns, launchers).
bow     drawn to the cheek at the start, the string snaps at the release (the arrow leaves at once, as in Terraria),
        the hand settles, nocks and draws again through the re-draw window and holds drawn (auto-fire chains).
cast    a gathering pull then a palm-forward push (staffs, wands, tomes).
drink   the hand brought to the mouth, head tipped back.
use     the item lifted out in front (placing, summoning, other uses).
hold    a carried light held up in front (loops; the game plays it on the right arm only).

The right arm's main angle is its pitch in the side plane: 0 hanging down, 90 straight out in front, 180 overhead,
over 180 reaching behind the head. Held items stick out of the fist at right angles to the forearm (a sword carried
point-forward, a torch upright); the wrist angle turns them in line with the arm (about -100 with the elbow's soft bend), so blades, shafts and barrels
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


def load_jsonc(path):
    """Reads a data file: JSON with // and /* */ comments and trailing commas (strings are left alone)."""
    keep = lambda m: m.group(0) if m.group(0).startswith('"') else ""
    return json.loads(_TRAILING.sub(keep, _COMMENTS.sub(keep, path.read_text(encoding="utf-8"))))


def timing():
    """Clip name -> its entry in use_styles.json "clips" (length, strikeStart/strikeEnd, release/redrawStart/redrawEnd)."""
    clips = load_jsonc(USE_STYLES)["clips"]
    need = {"swing": ("strikeStart", "strikeEnd"), "mine": ("strikeStart", "strikeEnd"), "thrust": ("strikeStart", "strikeEnd"),
            "bow": ("release", "redrawStart", "redrawEnd")}
    for name, keys in need.items():
        missing = [k for k in ("length",) + keys if k not in clips.get(name, {})]
        if missing:
            raise SystemExit(f"ERROR: {USE_STYLES}: clip '{name}' needs {', '.join(missing)}")
    return clips


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


def swing(t, w):
    a, b = w["strikeStart"], w["strikeEnd"]
    f = b + 0.3 * (1 - b)                   # the end of the follow-through; the rest brings the blade back up
    keys = [(0, "lin"), (a, "out"), (b, "io"), (f, "out"), (1, "io")]
    #                       raised  wind-up  struck  follow  raised
    pitch = keyed(t, keys, (195, 205, 45, 26, 195))
    elbow = keyed(t, keys, (20, 30, 6, 14, 20)) + 85 * hump(t, f, 1)     # the elbow folds to lift the blade back
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


def thrust(t, w):
    a, b = w["strikeStart"], w["strikeEnd"]
    keys = [(0, "lin"), (a, "out"), (b, "in"), (min(b + 0.1, (b + 1) / 2), "lin"), (1, "io")]
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


def shoot(t):
    kick = tl(t, [(0, 0), (0.08, 1, "out"), (0.5, 0, "io"), (1, 0)])
    pose = torso({}, 10, -3 * kick)
    arm(pose, "R", 88 + 14 * kick, 6 + 10 * kick, -6, -102 + 18 * kick)   # the barrel in line with the arm, kicking up
    arm(pose, "L", 80 + 10 * kick, 42, 38)       # the support hand under the barrel
    pose["neck"] = [("axis", UP, deg(-4))]
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
    """name -> (length in seconds, pose function); the game rescales every action to its item's use animation."""
    w = timing()
    timed = lambda name, fn: (w[name]["length"], lambda t: fn(t, w[name]))
    return {
        "swing": timed("swing", swing), "mine": timed("mine", mine), "thrust": timed("thrust", thrust), "shoot": (0.5, shoot),
        "bow": (w["bow"]["length"], lambda t: bow(t, w["bow"], m)), "cast": (0.5, cast), "drink": (0.8, drink),
        "use": (0.4, use), "hold": (2.0, hold),
    }

"""Actions for generated humans, sampled every frame from pose functions (exact curves, authored easing, seamless
loops).

Whole-body clips: idle, walk, run, sprint, jump, fall, land, swim, climb, glide, hurt, death. Upper-body actions that
the game layers over them (swing, mine, thrust, shoot, bow, cast, drink, use, hold) live in human/actions.py. Undead
folk (zombies, mummies) shamble with their arms held out; skeletons keep a hunch but walk normally.

Every pose starts from a relaxed stance (arms lowered from the A-pose rest, elbows soft, fingers curled; the right
hand closed around a handle at GRIP for held items) and adds the clip's rotations. Poses are dicts of bone -> rotation ops (see
rig.Poser), or {"rot": ops, "loc": offset} for bones that also move.
"""
import math

import bpy

from .hands import hand_axes
from .rig import Poser, keyframe_action

DOWN, UP, FWD, BACK = (0, 0, -1), (0, 0, 1), (0, 1, 0), (0, -1, 0)
SIDE_AXIS = (1, 0, 0)          # rotating about +X by +a turns a limb from pointing down to pointing forward
SIDES = (("R", 1), ("L", -1))
FPS = 30
TAU = 2 * math.pi
THIGH, SHIN = 0.43, 0.427      # leg segment lengths at scale 1 (human/frame.py)
# Where a held item's handle runs through the right fist, in units of the hand bone's length from its head: toward the
# palm (-X) and along the hand (+Y); the handle itself runs along the bone's Z (out past the thumb). The game attaches
# items here (game/Player/PlayerAvatar.cs, HeldGrip) and tools/blender/preview.py --hold=1 does the same.
GRIP = (-0.33, 0.89, 0.0)


def deg(a):
    return math.radians(a)


def out(sx):
    return (sx, 0, 0)


def wave(t, cycles=1.0, phase=0.0):
    return math.sin(TAU * (t * cycles + phase))


def bump(x, centre, width):
    """Smooth periodic bump on the unit cycle."""
    d = min(abs(x - centre) % 1.0, 1 - abs(x - centre) % 1.0)
    return math.exp(-(d / width) ** 2)


def ease(kind, u):
    if kind == "lin":
        return u
    if kind == "in":               # accelerate (a strike)
        return u * u * u
    if kind == "out":              # decelerate (into a hold)
        return 1 - (1 - u) ** 3
    if kind == "back":             # overshoot, then settle
        c = 2.2
        return 1 + (c + 1) * (u - 1) ** 3 + c * (u - 1) ** 2
    return u * u * (3 - 2 * u)     # "io"


def tl(t, keys):
    """A value on a timeline: keys = [(time, value[, ease into this key]), ...] sorted by time."""
    if t <= keys[0][0]:
        return keys[0][1]
    for a, b in zip(keys, keys[1:]):
        if t <= b[0]:
            u = (t - a[0]) / max(b[0] - a[0], 1e-9)
            return a[1] + (b[1] - a[1]) * ease(b[2] if len(b) > 2 else "io", u)
    return keys[-1][1]


def merge(*poses):
    result = {}
    for pose in poses:
        for bone, spec in pose.items():
            cur = result.setdefault(bone, {"rot": [], "loc": None})
            if isinstance(spec, dict):
                cur["rot"] += spec.get("rot", [])
                if "loc" in spec:
                    cur["loc"] = spec["loc"]
            else:
                cur["rot"] += spec
    return {b: ({"rot": s["rot"], "loc": s["loc"]} if s["loc"] is not None else s["rot"]) for b, s in result.items()}


def legs(pose, side, thigh, knee, foot=0.0):
    """Leg angles in degrees: thigh forward, knee bend, and the foot kept level plus ``foot`` (toes up)."""
    pose[f"thigh.{side}"] = [("axis", SIDE_AXIS, deg(thigh))]
    pose[f"shin.{side}"] = [("axis", SIDE_AXIS, deg(-knee))]
    pose[f"foot.{side}"] = [("axis", SIDE_AXIS, deg(knee - thigh + foot))]


def drop(s, thigh, knee):
    """How far the hips sink when a planted leg bends (so the feet stay on the ground)."""
    return s * (THIGH + SHIN - THIGH * math.cos(deg(thigh)) - SHIN * math.cos(deg(knee - thigh)))


class Motion:
    def __init__(self, arm, frame, p):
        self.arm = arm
        self.frame = frame
        self.s = frame.s
        self.poser = Poser(arm)
        self.undead = bool(p.get("undead"))
        self.shamble = self.undead and p.get("anatomy") != "skeleton"
        self.hunch = 1.0 if self.undead else 0.0
        self.spread = float(p.get("armSpread", 0.0))     # degrees the arms hang further out (bulky creatures)
        self.palm = {side: tuple(-hand_axes(frame, side)[2]) for side, _ in SIDES}
        self.across = {side: tuple(-hand_axes(frame, side)[1]) for side, _ in SIDES}   # from the thumb toward the pinky
        self.bones = [b.name for b in arm.data.bones]

    def fingers(self, side, curl, thumb=0.5):
        pose = {}
        palm = self.palm[side]
        for f, spread in (("index", 1.0), ("middle", 1.0), ("ring", 1.05), ("pinky", 1.1)):
            for k, amount in ((1, 55), (2, 72), (3, 42)):
                pose[f"{f}.0{k}.{side}"] = [("swing", palm, deg(amount * curl * spread))]
        for k, amount in ((1, 10), (2, 22), (3, 28)):
            pose[f"thumb.0{k}.{side}"] = [("swing", palm, deg(amount * thumb))]
        return pose

    def grip(self, side):
        """A closed fist around a handle (GRIP, in hand-bone units) that runs across the palm: the fingers wrap it and
        the thumb closes over the index and middle fingers, so held items sit in the hand rather than on it."""
        pose = self.fingers(side, 1.12, 0.0)
        palm, across = self.palm[side], self.across[side]
        pose[f"thumb.01.{side}"] = [("swing", palm, deg(26)), ("swing", across, deg(14))]
        pose[f"thumb.02.{side}"] = [("swing", palm, deg(30)), ("swing", across, deg(20))]
        pose[f"thumb.03.{side}"] = [("swing", palm, deg(26))]
        return pose

    def stance(self):
        pose = {"spine": [("swing", FWD, deg(4 * self.hunch))], "neck": [("swing", FWD, deg(10 * self.hunch))]}
        for side, sx in SIDES:
            pose[f"upper_arm.{side}"] = [("swing", DOWN, deg(33 - self.spread)), ("swing", FWD, deg(4 + 25 * self.hunch))]
            pose[f"forearm.{side}"] = [("swing", FWD, deg(12 + 30 * self.hunch))]
            if self.shamble:     # arms held out in front, wrists limp
                pose[f"upper_arm.{side}"] += [("axis", SIDE_AXIS, deg(52)), ("axis", UP, deg(-6 * sx))]
                pose[f"forearm.{side}"] = [("swing", FWD, deg(14))]
                pose[f"hand.{side}"] = [("axis", SIDE_AXIS, deg(-28))]
            if side == "R" and not self.shamble:
                pose = merge(pose, self.grip(side))
            else:
                pose = merge(pose, self.fingers(side, 0.3 if self.shamble else 0.28, 0.35))
        return pose

    # ------------------------------------------------------------------ clips
    def build(self):
        bpy.context.scene.render.fps = FPS
        base = self.stance()
        walk = self.shuffle if self.shamble else self.gait
        clips = {
            "idle": (4.0, self.idle),
            "walk": (1.4 if self.shamble else 1.0, lambda t: walk(t, 1.0)),
            "run": (1.0 if self.shamble else 0.64, lambda t: walk(t, 1.9)),
            "sprint": (0.5, lambda t: walk(t, 2.5)),
            "jump": (0.5, self.jump),
            "fall": (0.9, self.fall),
            "land": (0.4, self.land),
            "swim": (1.3, self.swim),
            "climb": (0.9, self.climb),
            "glide": (1.6, self.glide),
            "hurt": (0.4, self.hurt),
            "death": (1.3, self.death),
        }
        from . import actions     # (imports this module's helpers)
        clips.update(actions.clips(self))
        for name, (length, fn) in clips.items():
            self.clip(name, length, fn, base)

    def clip(self, name, length, fn, base):
        """Samples ``fn(t)`` (t in 0..1) at every frame; every bone is keyed so layered clips never fall back to rest."""
        n = max(2, round(length * FPS))
        rest = {b: [] for b in self.bones}
        keys = {i / n: merge(rest, base, fn(i / n)) for i in range(n + 1)}
        keyframe_action(self.arm, self.poser, name, n / FPS, keys, fps=FPS)

    def idle(self, t):
        s = self.s
        breath = wave(t, 2)                     # two slow breaths
        sway = wave(t, 1)                       # weight shifts from foot to foot
        glance = tl(t, [(0, 0), (0.3, 0), (0.4, 12), (0.54, 12, "lin"), (0.64, 0), (0.8, -6), (0.9, -6, "lin"), (1.0, 0)])
        roll = deg(1.5 if not self.undead else 3.0) * sway
        pose = {
            "hips": {"rot": [("axis", FWD, roll)], "loc": (0.004 * s * sway, 0, -0.003 * s * (1 + wave(t, 2, 0.25)))},
            "thigh.R": [("axis", FWD, -roll)], "thigh.L": [("axis", FWD, -roll)],
            "spine": [("axis", FWD, -roll * 0.7), ("swing", BACK, deg(0.8 * breath))],
            "chest": [("swing", BACK, deg(1.6 * breath))],
            "clavicle.R": [("swing", UP, deg(1.4 * breath))], "clavicle.L": [("swing", UP, deg(1.4 * breath))],
            "neck": [("axis", UP, deg(glance * 0.35))],
            "head": [("axis", UP, deg(glance * 0.65)), ("swing", FWD, deg(1.5 * breath - 1))],
            "upper_arm.R": [("swing", FWD, deg(1.2 * breath))], "upper_arm.L": [("swing", FWD, deg(1.2 * breath))],
        }
        if self.undead:                         # head lolling, arms drifting
            pose["head"] += [("axis", FWD, deg(7) * wave(t, 1, 0.2)), ("swing", FWD, deg(5))]
            pose["upper_arm.R"] += [("axis", SIDE_AXIS, deg(4) * wave(t, 2, 0.1))]
            pose["upper_arm.L"] += [("axis", SIDE_AXIS, deg(4) * wave(t, 2, 0.4))]
        return pose

    def gait(self, t, pace):
        """Walk (pace 1), run (~1.9) and sprint (2.5): the right foot strikes at t = 0, the left at t = 0.5."""
        s = self.s
        c, sn = math.cos(TAU * t), math.sin(TAU * t)
        fast = max(0.0, pace - 1)
        lean = 4 + 8 * fast
        bob = -0.016 * s * pace * math.cos(2 * TAU * t) - 0.012 * s * fast
        pose = {
            "hips": {"rot": [("axis", UP, deg(6 + 2 * fast) * c), ("axis", FWD, deg(3) * sn)], "loc": (0.01 * s * sn, 0, bob)},
            "spine": [("swing", FWD, deg(lean)), ("axis", UP, deg(-3 - fast) * c)],
            "chest": [("axis", UP, deg(-5 - 3 * fast) * c), ("swing", BACK, deg(1.5) * math.cos(2 * TAU * t))],
            "neck": [("swing", BACK, deg(lean * 0.3))],
            "head": [("swing", BACK, deg(lean * 0.4)), ("axis", UP, deg(3 + fast) * c)],
        }
        for side, sx in SIDES:
            phase = t if side == "R" else (t + 0.5) % 1.0
            swing = math.cos(TAU * phase)                     # +1 leg forward (strike), -1 leg back (push off)
            thigh = (24 + 10 * fast) * swing + 6 * fast + 10 * fast * bump(phase, 0.8, 0.12)
            knee = 5 + (46 + 44 * fast) * bump(phase, 0.72, 0.16) + (10 + 8 * fast) * bump(phase, 0.1, 0.07)
            foot = 12 * bump(phase, 0.0, 0.08) - (16 + 10 * fast) * bump(phase, 0.5, 0.07) + 8 * bump(phase, 0.78, 0.1)
            legs(pose, side, thigh, knee, foot)
            pose[f"toe.{side}"] = [("axis", SIDE_AXIS, deg(-26 * bump(phase, 0.48, 0.06)))]  # toes bend at push-off
            arm = -swing                                      # arms counter the legs
            pose[f"upper_arm.{side}"] = [("axis", SIDE_AXIS, deg((18 + 16 * fast) * arm + 4 * fast)), ("swing", out(sx), deg(3 * pace))]
            pose[f"forearm.{side}"] = [("swing", FWD, deg(6 + 12 * max(0.0, arm) + 52 * min(1.0, fast) + 10 * fast * max(0.0, arm)))]
        return pose

    def shuffle(self, t, pace):
        """The undead shamble: stiff lurching steps, the left foot dragging, the body rolling, the head lolling."""
        s = self.s
        c, sn = math.cos(TAU * t), math.sin(TAU * t)
        pose = {
            "hips": {"rot": [("axis", UP, deg(8) * c), ("axis", FWD, deg(6) * sn)],
                     "loc": (0.022 * s * sn, 0, -0.022 * s * (1 + math.cos(2 * TAU * t)) * 0.5 - 0.01 * s)},
            "spine": [("swing", FWD, deg(9 + 3 * pace)), ("axis", FWD, deg(-4) * sn)],
            "chest": [("axis", UP, deg(-4) * c)],
            "head": [("axis", FWD, deg(10) * wave(t, 1, 0.15)), ("swing", FWD, deg(6 + 4 * wave(t, 2, 0.1)))],
        }
        for side, sx in SIDES:
            phase = t if side == "R" else (t + 0.5) % 1.0
            swing = math.cos(TAU * phase)
            drag = side == "L"
            thigh = (18 if drag else 24) * pace ** 0.5 * swing
            knee = 4 + (10 if drag else 30) * bump(phase, 0.72, 0.18)
            legs(pose, side, thigh, knee, -12 * bump(phase, 0.6, 0.12) if drag else 6 * bump(phase, 0.0, 0.1))
            pose[f"upper_arm.{side}"] = [("axis", SIDE_AXIS, deg(5) * wave(t, 1, 0.3 if drag else 0.8)), ("axis", UP, deg(-3 * sx) * c)]
            pose[f"forearm.{side}"] = [("swing", FWD, deg(6 + 6 * wave(t, 1, 0.5 if drag else 0.0)))]
        return pose

    def jump(self, t):
        """Push off with the arms thrown up, then tuck the knees."""
        push = tl(t, [(0, 1), (0.28, 0, "out")])
        tuck = tl(t, [(0, 0), (0.3, 0.35, "io"), (1, 1, "out")])
        arms = tl(t, [(0, 0.3), (0.22, 1, "out"), (1, 0.75, "io")])
        pose = {
            "spine": [("swing", FWD, deg(3 + 7 * tuck))],
            "head": [("swing", BACK, deg(5 * arms))],
            "clavicle.R": [("swing", UP, deg(6 * arms))], "clavicle.L": [("swing", UP, deg(6 * arms))],
        }
        legs(pose, "R", -6 * push + 50 * tuck, 6 + 80 * tuck, -30 * push - 20 * tuck)
        legs(pose, "L", -10 * push + 26 * tuck, 6 + 56 * tuck, -34 * push - 16 * tuck)
        for side, sx in SIDES:
            pose[f"upper_arm.{side}"] = [("axis", SIDE_AXIS, deg(62 * arms)), ("swing", out(sx), deg(16 * arms))]
            pose[f"forearm.{side}"] = [("swing", FWD, deg(18 + 22 * arms))]
        return pose

    def fall(self, t):
        """Arms up and flailing, legs paddling."""
        pose = {"spine": [("swing", BACK, deg(4))], "head": [("swing", FWD, deg(10))],
                "chest": [("axis", UP, deg(4) * wave(t))]}
        for side, sx in SIDES:
            ph = 0.0 if side == "R" else 0.5
            pose[f"upper_arm.{side}"] = [("swing", out(sx), deg(78 + 16 * wave(t, 1, ph))), ("axis", SIDE_AXIS, deg(28 + 14 * wave(t, 1, ph + 0.25)))]
            pose[f"forearm.{side}"] = [("swing", FWD, deg(38 + 22 * wave(t, 1, ph + 0.4)))]
            pose[f"hand.{side}"] = [("axis", SIDE_AXIS, deg(12) * wave(t, 2, ph))]
            legs(pose, side, 16 + 12 * wave(t, 1, ph + 0.1), 30 + 16 * wave(t, 1, ph + 0.35), -25)
        return pose

    def land(self, t):
        """Knees absorb the landing, then straighten."""
        dip = tl(t, [(0, 0.35), (0.22, 1, "out"), (1, 0, "io")])
        th, kn = 34 * dip, 64 * dip
        pose = {
            "hips": {"rot": [], "loc": (0, -0.02 * self.s * dip, -drop(self.s, th, kn))},
            "spine": [("swing", FWD, deg(14 * dip))], "head": [("swing", BACK, deg(8 * dip))],
        }
        for side, sx in SIDES:
            legs(pose, side, th, kn)
            pose[f"upper_arm.{side}"] = [("axis", SIDE_AXIS, deg(22 * dip)), ("swing", out(sx), deg(14 * dip))]
            pose[f"forearm.{side}"] = [("swing", FWD, deg(18 * dip))]
        return pose

    def swim(self, t):
        """Treading water: a breaststroke sweep of the arms and a scissor kick."""
        pull = wave(t)
        pose = {"spine": [("swing", FWD, deg(12))], "head": [("swing", BACK, deg(12))],
                "hips": {"rot": [], "loc": (0, 0, 0.012 * self.s * wave(t, 1, 0.3))}}
        for side, sx in SIDES:
            pose[f"upper_arm.{side}"] = [("axis", SIDE_AXIS, deg(62 + 12 * pull)), ("axis", UP, deg(-sx * (8 + 34 * (0.5 + 0.5 * wave(t, 1, 0.25)))))]
            pose[f"forearm.{side}"] = [("swing", FWD, deg(35 + 35 * (0.5 - 0.5 * pull)))]
            pose[f"hand.{side}"] = [("axis", FWD, deg(sx * 25))]
            ph = 0.0 if side == "R" else 0.5
            legs(pose, side, 14 + 16 * wave(t, 1, ph), 28 + 24 * wave(t, 1, ph + 0.25), -30)
        return pose

    def climb(self, t):
        """Hand over hand up a rope, knees lifting in turn."""
        pose = {"spine": [("swing", FWD, deg(6))], "head": [("swing", BACK, deg(14))]}
        for side, sx in SIDES:
            ph = 0.0 if side == "R" else 0.5
            reach = 0.5 + 0.5 * wave(t, 1, ph)
            pose[f"upper_arm.{side}"] = [("axis", SIDE_AXIS, deg(105 + 55 * reach)), ("axis", UP, deg(sx * 10))]
            pose[f"forearm.{side}"] = [("swing", FWD, deg(70 - 45 * reach))]
            lift = 0.5 + 0.5 * wave(t, 1, ph + 0.5)
            legs(pose, side, 12 + 36 * lift, 20 + 50 * lift, -15)
        return pose

    def glide(self, t):
        """Arms spread, legs trailing, a slow rise and fall."""
        rock = wave(t)
        pose = {"spine": [("swing", FWD, deg(16))], "head": [("swing", BACK, deg(20))],
                "hips": {"rot": [("axis", FWD, deg(3) * rock)], "loc": (0, 0, 0)}}
        for side, sx in SIDES:
            pose[f"upper_arm.{side}"] = [("swing", out(sx), deg(72 + 5 * rock * sx)), ("axis", SIDE_AXIS, deg(-12))]
            pose[f"forearm.{side}"] = [("swing", FWD, deg(10))]
            legs(pose, side, -10 + 4 * wave(t, 1, 0.5 if side == "L" else 0), 18 + 6 * wave(t, 1, 0.25), -35)
        return pose

    def hurt(self, t):
        """A flinch: head and chest snap back, arms come up, then recover."""
        k = tl(t, [(0, 0), (0.2, 1, "out"), (1, 0, "io")])
        pose = {"spine": [("swing", BACK, deg(10 * k))], "chest": [("swing", BACK, deg(8 * k)), ("axis", UP, deg(6 * k))],
                "neck": [("swing", BACK, deg(6 * k))], "head": [("swing", BACK, deg(14 * k)), ("axis", FWD, deg(8 * k))]}
        for side, sx in SIDES:
            pose[f"upper_arm.{side}"] = [("axis", SIDE_AXIS, deg(30 * k)), ("swing", out(sx), deg(14 * k))]
            pose[f"forearm.{side}"] = [("swing", FWD, deg(30 * k))]
        return pose

    def death(self, t):
        """Stagger, knees buckle, topple onto the back with a small bounce, arms falling open."""
        s = self.s
        buckle = tl(t, [(0, 0), (0.28, 1, "io"), (0.6, 0.4), (1, 0.15)])
        fall = tl(t, [(0.18, 0), (0.66, 1, "in"), (0.76, 0.93, "out"), (0.88, 1, "io")])
        pose = {
            "root": {"rot": [("axis", SIDE_AXIS, deg(86 * fall))], "loc": (0, 0, 0.11 * s * fall)},   # onto the back
            "spine": [("swing", FWD, deg(16 * buckle * (1 - fall)))], "chest": [("axis", UP, deg(10 * buckle))],
            "head": [("swing", BACK, deg(18 * tl(t, [(0, 0), (0.2, 1), (0.6, 0.2), (0.8, 1)])))],
        }
        for side, sx in SIDES:
            legs(pose, side, 30 * buckle, 58 * buckle, -15 * fall)
            pose[f"upper_arm.{side}"] = [("swing", out(sx), deg(20 + 45 * fall)), ("axis", SIDE_AXIS, deg(35 * buckle * (1 - fall)))]
            pose[f"forearm.{side}"] = [("swing", FWD, deg(25 + 20 * buckle))]
        return pose


def animate(arm, frame, p):
    Motion(arm, frame, p).build()

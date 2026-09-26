"""Actions for generated humans: idle (breathing, weight shift), walk and run cycles with pelvis bob, sway and
counter-rotation, jump, fall, a two-handed weapon swing, use, hurt and death. Clip names match the old humanoid
rig so the game's animation choice is unchanged.

Every pose starts from a relaxed stance (arms lowered from the A-pose rest, elbows soft, fingers curled — the right
hand in a loose grip for held items) and adds the action's rotations.
"""
import math

from .hands import hand_axes
from .rig import Poser, keyframe_action

DOWN, UP, FWD, BACK = (0, 0, -1), (0, 0, 1), (0, 1, 0), (0, -1, 0)
SIDES = (("R", 1), ("L", -1))


def deg(a):
    return math.radians(a)


def out(sx):
    return (sx, 0, 0)


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


class Motion:
    def __init__(self, arm, frame, p):
        self.arm = arm
        self.frame = frame
        self.poser = Poser(arm)
        self.hunch = 1.0 if p.get("undead") else 0.0
        self.palm = {side: tuple(-hand_axes(frame, side)[2]) for side, _ in SIDES}

    def fingers(self, side, curl, thumb=0.5):
        pose = {}
        palm = self.palm[side]
        for f, spread in (("index", 1.0), ("middle", 1.0), ("ring", 1.05), ("pinky", 1.1)):
            for k, amount in ((1, 55), (2, 72), (3, 42)):
                pose[f"{f}.0{k}.{side}"] = [("swing", palm, deg(amount * curl * spread))]
        for k, amount in ((1, 10), (2, 22), (3, 28)):
            pose[f"thumb.0{k}.{side}"] = [("swing", palm, deg(amount * thumb))]
        return pose

    def stance(self, grip=0.62):
        pose = {"spine": [("swing", FWD, deg(4 * self.hunch))], "neck": [("swing", FWD, deg(10 * self.hunch))]}
        for side, sx in SIDES:
            pose[f"upper_arm.{side}"] = [("swing", DOWN, deg(33)), ("swing", FWD, deg(4 + 25 * self.hunch))]
            pose[f"forearm.{side}"] = [("swing", FWD, deg(12 + 30 * self.hunch))]
            pose = merge(pose, self.fingers(side, grip if side == "R" else 0.28, 0.7 if side == "R" else 0.35))
        return pose

    # ------------------------------------------------------------------ clips
    def build(self):
        base = self.stance()
        self.clip("idle", 3.0, {t: merge(base, self.idle(t)) for t in (0.0, 0.25, 0.5, 0.75, 1.0)})
        self.clip("walk", 0.95, {t: merge(base, self.gait(t, 1.0)) for t in (i / 8 for i in range(9))})
        self.clip("run", 0.62, {t: merge(base, self.gait(t, 1.9)) for t in (i / 8 for i in range(9))})
        self.clip("jump", 0.3, {t: merge(base, self.jump()) for t in (0.0, 1.0)})
        self.clip("fall", 0.4, {t: merge(base, self.fall(t)) for t in (0.0, 0.5, 1.0)})
        self.clip("swing", 0.42, {t: merge(base, pose) for t, pose in self.swing().items()})
        self.clip("use", 0.3, {t: merge(base, pose) for t, pose in self.use().items()})
        self.clip("hurt", 0.25, {t: merge(base, pose) for t, pose in self.hurt().items()})
        self.clip("death", 0.9, {t: merge(base, pose) for t, pose in self.death().items()})

    def clip(self, name, length, keys):
        keyframe_action(self.arm, self.poser, name, length, keys)

    def idle(self, t):
        breath = math.sin(t * 2 * math.pi)
        shift = math.sin(t * 2 * math.pi) * 0.006 * self.frame.s
        return {
            "hips": {"rot": [("axis", FWD, deg(1.2) * math.sin(t * 2 * math.pi))], "loc": (shift, 0, 0)},
            "chest": [("swing", BACK, deg(1.5 * breath))],
            "clavicle.R": [("swing", UP, deg(1.2 * breath))],
            "clavicle.L": [("swing", UP, deg(1.2 * breath))],
            "head": [("axis", UP, deg(4) * math.sin(t * 2 * math.pi + 1.0)), ("swing", FWD, deg(2 * breath))],
            "upper_arm.R": [("swing", FWD, deg(1.5 * breath))],
            "upper_arm.L": [("swing", FWD, deg(-1.5 * breath))],
        }

    def gait(self, t, pace):
        """Walk (pace 1) and run (pace ~1.9): right foot strikes at t = 0, left at t = 0.5."""
        c = math.cos(2 * math.pi * t)
        s = self.frame.s
        lean = 4 + 9 * (pace - 1)
        pose = {
            "hips": {"rot": [("axis", UP, deg(6 * pace) * c), ("axis", FWD, deg(3) * math.sin(2 * math.pi * t))],
                     "loc": (0.012 * s * math.sin(2 * math.pi * t), 0, -0.018 * s * pace * math.cos(4 * math.pi * t) - 0.01 * s * (pace - 1))},
            "spine": [("swing", FWD, deg(lean)), ("axis", UP, deg(-3 * pace) * c)],
            "chest": [("axis", UP, deg(-5 * pace) * c)],
            "head": [("swing", BACK, deg(lean * 0.6)), ("axis", UP, deg(3 * pace) * c)],
        }
        for side, sx in SIDES:
            phase = t if side == "R" else (t + 0.5) % 1.0
            swing = math.cos(2 * math.pi * phase)             # +1 leg forward (strike), -1 leg back (push off)
            knee = 6 + (48 + 42 * (pace - 1)) * bump(phase, 0.72, 0.17) + 12 * bump(phase, 0.1, 0.08)
            foot = 10 * bump(phase, 0.0, 0.1) - 18 * bump(phase, 0.52, 0.08) + 6 * bump(phase, 0.75, 0.1)
            pose[f"thigh.{side}"] = [("swing", FWD, deg(26 * pace * swing + 6 * (pace - 1)))]
            pose[f"shin.{side}"] = [("swing", BACK, deg(knee))]
            pose[f"foot.{side}"] = [("swing", UP, deg(foot))]
            arm = -swing                                          # arms counter the legs
            pose[f"upper_arm.{side}"] = [("swing", FWD, deg(20 * pace * arm)), ("swing", out(sx), deg(3 * pace))]
            pose[f"forearm.{side}"] = [("swing", FWD, deg(8 + 14 * pace * max(0.0, arm) + 45 * (pace - 1)))]
        return pose

    def jump(self):
        return {
            "spine": [("swing", FWD, deg(8))],
            "thigh.R": [("swing", FWD, deg(40))], "shin.R": [("swing", BACK, deg(70))],
            "thigh.L": [("swing", FWD, deg(10))], "shin.L": [("swing", BACK, deg(35))],
            "upper_arm.R": [("swing", FWD, deg(35)), ("swing", (1, 0, 0), deg(20))],
            "upper_arm.L": [("swing", FWD, deg(35)), ("swing", (-1, 0, 0), deg(20))],
            "forearm.R": [("swing", FWD, deg(30))], "forearm.L": [("swing", FWD, deg(30))],
        }

    def fall(self, t):
        w = math.sin(t * 2 * math.pi) * 5
        return {
            "spine": [("swing", BACK, deg(4))],
            "upper_arm.R": [("swing", UP, deg(60 + w)), ("swing", FWD, deg(15))],
            "upper_arm.L": [("swing", UP, deg(60 - w)), ("swing", FWD, deg(15))],
            "forearm.R": [("swing", UP, deg(20))], "forearm.L": [("swing", UP, deg(20))],
            "thigh.R": [("swing", FWD, deg(18 + w))], "shin.R": [("swing", BACK, deg(30))],
            "thigh.L": [("swing", FWD, deg(4 - w))], "shin.L": [("swing", BACK, deg(18))],
            "head": [("swing", FWD, deg(12))],
        }

    def swing(self):
        wind = {"chest": [("axis", UP, deg(28))], "spine": [("axis", UP, deg(10)), ("swing", BACK, deg(6))],
                "upper_arm.R": [("swing", UP, deg(150)), ("swing", BACK, deg(10))], "forearm.R": [("swing", FWD, deg(40))],
                "upper_arm.L": [("swing", FWD, deg(30)), ("swing", (-1, 0, 0), deg(15))], "forearm.L": [("swing", FWD, deg(35))]}
        hit = {"chest": [("axis", UP, deg(-22))], "spine": [("axis", UP, deg(-8)), ("swing", FWD, deg(12))],
               "upper_arm.R": [("swing", FWD, deg(75)), ("swing", (-1, 0, 0), deg(18))], "forearm.R": [("swing", FWD, deg(8))],
               "upper_arm.L": [("swing", BACK, deg(20)), ("swing", (-1, 0, 0), deg(20))], "forearm.L": [("swing", FWD, deg(40))],
               "thigh.R": [("swing", FWD, deg(12))], "shin.L": [("swing", BACK, deg(12))]}
        follow = {"chest": [("axis", UP, deg(-10))], "spine": [("swing", FWD, deg(5))],
                  "upper_arm.R": [("swing", FWD, deg(35))], "forearm.R": [("swing", FWD, deg(15))]}
        return {0.0: wind, 0.45: hit, 1.0: follow}

    def use(self):
        reach = {"upper_arm.R": [("swing", FWD, deg(75))], "forearm.R": [("swing", FWD, deg(8))], "chest": [("axis", UP, deg(-8))]}
        return {0.0: {"upper_arm.R": [("swing", FWD, deg(15))]}, 0.4: reach, 1.0: {"upper_arm.R": [("swing", FWD, deg(60))], "forearm.R": [("swing", FWD, deg(10))]}}

    def hurt(self):
        flinch = {"spine": [("swing", BACK, deg(10))], "chest": [("swing", BACK, deg(8))], "head": [("swing", BACK, deg(14))],
                  "upper_arm.R": [("swing", FWD, deg(25)), ("swing", (1, 0, 0), deg(10))],
                  "upper_arm.L": [("swing", FWD, deg(25)), ("swing", (-1, 0, 0), deg(10))]}
        return {0.0: {}, 0.4: flinch, 1.0: {}}

    def death(self):
        s = self.frame.s
        slump = {"root": {"rot": [("axis", (1, 0, 0), deg(-60))], "loc": (0, 0, 0)},
                 "spine": [("swing", FWD, deg(10))], "shin.R": [("swing", BACK, deg(50))], "shin.L": [("swing", BACK, deg(40))],
                 "thigh.R": [("swing", FWD, deg(30))], "thigh.L": [("swing", FWD, deg(20))]}
        down = {"root": {"rot": [("axis", (1, 0, 0), deg(-88))], "loc": (0, 0, 0.12 * s)},
                "upper_arm.R": [("swing", (1, 0, 0), deg(40))], "upper_arm.L": [("swing", (-1, 0, 0), deg(40))],
                "head": [("swing", (1, 0, 0), deg(25))], "thigh.R": [("swing", (1, 0, 0), deg(8))]}
        return {0.0: {"root": {"rot": [], "loc": (0, 0, 0)}}, 0.6: slump, 1.0: down}


def bump(x, centre, width):
    """Smooth periodic bump on the unit cycle."""
    d = min(abs(x - centre), 1 - abs(x - centre))
    return math.exp(-(d / width) ** 2)


def animate(arm, frame, p):
    Motion(arm, frame, p).build()

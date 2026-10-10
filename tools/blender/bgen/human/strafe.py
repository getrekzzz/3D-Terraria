"""Strafe clips for generated humans: strafe_left and strafe_right, run cycles whose feet travel STRAFE_ANGLE degrees
to the left or right of where the hips face.

The player's body faces the crosshair while A/D move it sideways. The game turns the legs (the whole model) partway
toward the movement and the aim twists the torso back to the crosshair; these clips cover the rest of the angle. Each
thigh turns out about the vertical through its hip (the knees and feet point along the travel), the legs open a
little so the feet pass clear of each other, and the hips' sway, roll and the forward lean follow the travel.
Turning the legs STRAFE_ANGLE toward the movement and playing a strafe clip sends the feet exactly sideways. The
stride matches run (the same ground per cycle), so the playback rate follows ground speed the same way.
"""
import math

from .motion import FWD, SIDES, UP, deg

STRAFE_ANGLE = 45.0            # degrees between the hips' facing and the feet's travel
SPREAD = 3.0                   # degrees each leg opens outward so the feet pass clear
PACE = 1.9                     # the run's pace (motion.Motion.gait)


def turned(ops, c, s):
    """The ops with their axis and swing directions turned about the vertical (cos c, sin s)."""
    out = []
    for op in ops:
        if op[0] in ("axis", "swing"):
            x, y, z = op[1]
            out.append((op[0], (x * c - y * s, x * s + y * c, z), op[2]))
        else:
            out.append(op)
    return out


def strafe(walk, side):
    """A pose function: ``walk`` (Motion.gait or the undead shuffle) at run pace with the stride turned to ``side``."""
    yaw = deg(-STRAFE_ANGLE if side == "right" else STRAFE_ANGLE)    # + turns the forward toward the character's left
    c, s = math.cos(yaw), math.sin(yaw)

    def pose(t):
        p = walk(t, PACE)
        for leg, sx in SIDES:
            # Flex in the leg's own plane, open it outward, then turn the whole leg about the vertical through the hip.
            p[f"thigh.{leg}"] = p[f"thigh.{leg}"] + [("axis", FWD, deg(-SPREAD * sx)), ("axis", UP, yaw)]
        hips = p["hips"]
        x, y, z = hips["loc"]
        p["hips"] = {"rot": turned(hips["rot"], c, s), "loc": (x * c - y * s, x * s + y * c, z)}
        for bone in ("spine", "neck", "head"):     # lean into the travel; the head keeps level along it
            if bone in p:
                p[bone] = turned(p[bone], c, s)
        return p

    return pose


def clips(walk, run_length):
    """name -> (length in seconds, pose function) for Motion.build: ``walk`` is the set's gait, ``run_length`` its run."""
    return {f"strafe_{side}": (run_length, strafe(walk, side)) for side in ("left", "right")}

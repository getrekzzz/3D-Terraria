"""Hardmode creatures of the radiant land: the glimmer sprite (a winged mote of light) and the prism steed (a horned
horse with a prismatic mane). Rigid parts on small rigs with looping actions, like bgen.fauna. Front faces Blender +Y;
the origin is the bottom of the creature's hitbox."""
import math

from mathutils import Matrix, Vector

from .creatures import _action
from .mesh import MeshBuilder

SPRITE_BONES = {
    "body": ((0, -0.05, 0.3), (0, 0.05, 0.3), None),
    "wing.R": ((0.03, -0.03, 0.37), (0.3, -0.06, 0.46), "body"),
    "wing.L": ((-0.03, -0.03, 0.37), (-0.3, -0.06, 0.46), "body"),
}


def _sprite_actions(arm):
    beat = {0.0: {"wing.R": (0, 0.7, 0), "wing.L": (0, -0.7, 0), "body": (0.1, 0, 0)},
            0.5: {"wing.R": (0, -0.5, 0), "wing.L": (0, 0.5, 0), "body": (-0.05, 0, 0)},
            1.0: {"wing.R": (0, 0.7, 0), "wing.L": (0, -0.7, 0), "body": (0.1, 0, 0)}}
    _action(arm, "fly", 0.1, beat)


def sprite(mb: MeshBuilder, p, rng):
    """A tiny glowing figure with trailing legs, a wisp of hair and two pairs of luminous wings; motes trail behind."""
    glow, hair, wing = p.get("glow", "#fff0ff"), p.get("hair", "#ffb0e8"), p.get("wing", "#6a88c8")
    mb.bone = "body"
    mb.sphere((0, 0.02, 0.43), 0.052, "prop:plain:glow", glow, subdivisions=2)
    for k in range(5):  # hair flowing back
        a = (k - 2) * 0.5
        mb.sphere((math.sin(a) * 0.035, -0.02 - k * 0.012, 0.46 - abs(a) * 0.02), 0.03 - k * 0.003, "prop:plain:glow", hair, subdivisions=1)
    mb.sphere((0, 0.0, 0.33), 0.045, "prop:plain:glow", glow, subdivisions=2, scale=(0.8, 0.7, 1.5))
    for sx in (1, -1):
        mb.tube([Vector((sx * 0.03, 0.0, 0.37)), Vector((sx * 0.07, 0.03, 0.31)), Vector((sx * 0.06, 0.06, 0.27))], [0.012, 0.01, 0.007],
                "prop:plain:glow", glow, segments=5)
        mb.tube([Vector((sx * 0.018, -0.005, 0.27)), Vector((sx * 0.022, -0.04, 0.2)), Vector((sx * 0.012, -0.09, 0.13))], [0.016, 0.011, 0.004],
                "prop:plain:glow", glow, segments=5)
    for k in range(5):  # motes trailing below and behind
        mb.sphere((math.sin(k * 1.7) * 0.03, -0.08 - k * 0.05, 0.2 - k * 0.035), 0.016 - k * 0.0025, "prop:plain:glow", hair, subdivisions=1)
    for side, sx in (("R", 1), ("L", -1)):
        mb.bone = f"wing.{side}"
        upper = [(sx * 0.02, 0.02), (sx * 0.08, 0.14), (sx * 0.18, 0.24), (sx * 0.28, 0.25), (sx * 0.31, 0.16), (sx * 0.24, 0.05), (sx * 0.1, 0.0)]
        lower = [(sx * 0.02, -0.01), (sx * 0.12, -0.03), (sx * 0.2, -0.1), (sx * 0.19, -0.18), (sx * 0.12, -0.19), (sx * 0.05, -0.1)]
        for shape in (upper, lower):
            mb.extrude_polygon(shape, 0.004, "prop:gel:glow", wing,
                               matrix=Matrix.Translation((0, -0.02, 0.37)) @ Matrix.Rotation(-math.pi / 2 + 0.25, 4, "X"))
    mb.bone = None
    return {"rig": SPRITE_BONES, "actions": _sprite_actions}


STEED_BONES = {
    "body": ((0, -0.35, 1.15), (0, 0.35, 1.15), None),
    "neck": ((0, 0.6, 1.3), (0, 0.9, 1.78), "body"),
    "tail": ((0, -0.7, 1.28), (0, -1.0, 0.8), "body"),
    "leg.FR": ((0.17, 0.45, 1.0), (0.17, 0.45, 0.0), "body"),
    "leg.FL": ((-0.17, 0.45, 1.0), (-0.17, 0.45, 0.0), "body"),
    "leg.BR": ((0.17, -0.48, 1.0), (0.17, -0.48, 0.0), "body"),
    "leg.BL": ((-0.17, -0.48, 1.0), (-0.17, -0.48, 0.0), "body"),
}


def _steed_actions(arm):
    def pose(front, back, neck, body):
        return {"leg.FR": (front, 0, 0), "leg.FL": (front * 0.6, 0, 0), "leg.BR": (back, 0, 0), "leg.BL": (back * 0.6, 0, 0),
                "neck": (neck, 0, 0), "body": (body, 0, 0), "tail": (-0.3, 0, 0)}

    _action(arm, "walk", 0.55, {0.0: pose(0.55, -0.45, 0.12, 0.05), 0.5: pose(-0.45, 0.55, -0.1, -0.05), 1.0: pose(0.55, -0.45, 0.12, 0.05)})
    _action(arm, "idle", 3.0, {0.0: {"neck": (0.0, 0, 0), "tail": (0, 0, 0.25)}, 0.5: {"neck": (0.12, 0, 0), "tail": (0, 0, -0.25)},
                               1.0: {"neck": (0.0, 0, 0), "tail": (0, 0, 0.25)}})


def steed(mb: MeshBuilder, p, rng):
    """A horned horse: barrel body, arched neck, long head with a muzzle, eyes and ears, a spiralled crystal horn, a
    mane and tail in prismatic locks, and legs with hooves."""
    coat, hoof = p.get("coat", "#f2f0f8"), p.get("hoof", "#5a5060")
    mane = p.get("mane", ["#ffb0e0", "#b0d0ff", "#b8ffe0", "#fff0b0", "#e0b8ff"])
    horn = p.get("horn", "#f0e0ff")
    mb.bone = "body"
    mb.sphere((0, 0.0, 1.15), 0.3, "prop:plain", coat, subdivisions=3, scale=(1.0, 2.4, 1.13), shade=0.3)
    mb.sphere((0, 0.5, 1.2), 0.28, "prop:plain", coat, subdivisions=2, scale=(1.0, 1.0, 1.1))    # chest
    mb.sphere((0, -0.52, 1.18), 0.3, "prop:plain", coat, subdivisions=2, scale=(1.0, 1.0, 1.0))  # hindquarters
    mb.bone = "neck"
    mb.tube([Vector((0, 0.45, 1.22)), Vector((0, 0.7, 1.55)), Vector((0, 0.86, 1.8))], [0.24, 0.18, 0.13], "prop:plain", coat, segments=10)
    mb.sphere((0, 1.03, 1.71), 0.135, "prop:plain", coat, subdivisions=2, scale=(0.95, 2.3, 1.1), rotation=Matrix.Rotation(-0.6, 4, "X"))
    mb.sphere((0, 1.22, 1.56), 0.1, "prop:plain", coat, subdivisions=2, scale=(1.0, 1.1, 0.95))   # muzzle
    for sx in (1, -1):
        mb.sphere((sx * 0.045, 1.3, 1.55), 0.022, "prop:plain", "#3a2a38", subdivisions=1)              # nostril
        mb.sphere((sx * 0.1, 0.96, 1.8), 0.032, "prop:plain", "#141018", subdivisions=2)               # eye
        mb.cylinder((sx * 0.07, 0.86, 1.88), (sx * 0.09, 0.8, 2.02), 0.035, 0.0, "prop:plain", coat, segments=6)  # ear
    base, tip = Vector((0, 0.98, 1.86)), Vector((0, 1.16, 2.22))
    mb.cylinder(tuple(base), tuple(tip), 0.04, 0.0, "prop:crystal:glow", horn, segments=8)
    axis = (tip - base).normalized()
    side = axis.cross(Vector((1, 0, 0))).normalized()
    up = axis.cross(side)
    spiral = [base + axis * (t * (tip - base).length) + (side * math.cos(t * 18) + up * math.sin(t * 18)) * 0.04 * (1 - t)
              for t in (i / 24 for i in range(25))]
    mb.tube(spiral, [0.008] * 24 + [0.001], "prop:crystal:glow", horn, segments=4)
    for k in range(9):  # mane locks along the crest, falling to the right
        t = k / 8
        at = Vector((0, 0.55, 1.4)).lerp(Vector((0, 0.9, 1.9)), t)
        col = mane[k % len(mane)]
        mb.tube([at, at + Vector((0.08, -0.06, -0.05)), at + Vector((0.14, -0.1, -0.25 - 0.1 * (1 - t)))], [0.05, 0.04, 0.01],
                "prop:cloth", col, segments=5)
    mb.bone = "tail"
    for k in range(5):
        a = (k - 2) * 0.12
        mb.tube([Vector((a * 0.2, -0.72, 1.28)), Vector((a * 0.5, -0.95, 1.1)), Vector((a, -1.05, 0.75)), Vector((a * 1.2, -1.0, 0.45))],
                [0.05, 0.06, 0.045, 0.01], "prop:cloth", mane[k % len(mane)], segments=5)
    for bone, x, y in (("leg.FR", 0.17, 0.45), ("leg.FL", -0.17, 0.45), ("leg.BR", 0.17, -0.48), ("leg.BL", -0.17, -0.48)):
        mb.bone = bone
        back = y < 0
        knee = Vector((x, y + (-0.06 if back else 0.02), 0.55))
        mb.tube([Vector((x, y, 1.05)), Vector((x, y - (0.05 if back else 0.0), 0.8)), knee], [0.13 if back else 0.1, 0.09, 0.06],
                "prop:plain", coat, segments=8)
        mb.tube([knee, Vector((x, y, 0.3)), Vector((x, y + 0.02, 0.13))], [0.055, 0.045, 0.05], "prop:plain", coat, segments=8)
        mb.cylinder((x, y + 0.02, 0.0), (x, y + 0.02, 0.13), 0.068, 0.056, "prop:stone", hoof, segments=10)
        mb.tube([Vector((x, y + 0.02, 0.2)), Vector((x, y + 0.0, 0.1))], [0.07, 0.075], "prop:cloth", mane[0], segments=6)  # feathering
    mb.bone = None
    return {"rig": STEED_BONES, "actions": _steed_actions}


GENERATORS = {"sprite": sprite, "steed": steed}

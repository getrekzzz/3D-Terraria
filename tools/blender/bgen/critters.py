"""Critters: small harmless animals (bunnies, squirrels, birds, butterflies, fireflies, worms, frogs, ducks) that a Bug
Net catches. Same conventions as fauna.py: front faces Blender +Y, origin at the bottom of the hitbox. The "critter"
item generator builds the same mesh unrigged for the caught item's model and icon."""
import math

from mathutils import Matrix, Vector

from .creatures import _action
from .mesh import MeshBuilder

FLAT = Matrix.Rotation(-math.pi / 2, 4, "X")


def _hop_actions(arm):
    _action(arm, "walk", 0.45, {0.0: {"body": (0.25, 0, 0)}, 0.5: {"body": (-0.2, 0, 0)}, 1.0: {"body": (0.25, 0, 0)}})
    _action(arm, "idle", 1.8, {0.0: {"ear.R": (0, 0, 0), "ear.L": (0, 0, 0)}, 0.5: {"ear.R": (0.3, 0, 0), "ear.L": (-0.1, 0, 0)},
                               1.0: {"ear.R": (0, 0, 0), "ear.L": (0, 0, 0)}})


BUNNY_BONES = {
    "body": ((0, -0.15, 0.2), (0, 0.15, 0.2), None),
    "ear.R": ((0.05, 0.14, 0.38), (0.07, 0.12, 0.62), "body"),
    "ear.L": ((-0.05, 0.14, 0.38), (-0.07, 0.12, 0.62), "body"),
}


def bunny(mb: MeshBuilder, p, rng):
    fur, belly = p.get("fur", "#d8d0c4"), p.get("belly", "#f4efe8")
    mb.bone = "body"
    mb.sphere((0, -0.02, 0.2), 0.17, "prop:cloth", fur, subdivisions=2, scale=(0.9, 1.25, 0.95))
    mb.sphere((0, 0.02, 0.15), 0.12, "prop:cloth", belly, subdivisions=1, scale=(0.8, 1.0, 0.8))
    mb.sphere((0, 0.18, 0.32), 0.1, "prop:cloth", fur, subdivisions=2)
    mb.sphere((0, -0.22, 0.24), 0.06, "prop:cloth", belly, subdivisions=1)
    for sx in (-1, 1):
        mb.sphere((sx * 0.05, 0.25, 0.35), 0.02, "prop:plain", "#101010", subdivisions=1)
        mb.sphere((sx * 0.08, 0.1, 0.05), 0.05, "prop:cloth", fur, subdivisions=1, scale=(0.8, 1.6, 0.6))
    mb.sphere((0, 0.28, 0.31), 0.015, "prop:flesh", "#e08a9a", subdivisions=1)
    for side, sx in (("R", 1), ("L", -1)):
        mb.bone = f"ear.{side}"
        mb.sphere((sx * 0.06, 0.13, 0.5), 0.035, "prop:cloth", fur, subdivisions=1, scale=(0.9, 0.5, 3.2))
    mb.bone = None
    return {"rig": BUNNY_BONES, "actions": _hop_actions}


SQUIRREL_BONES = {
    "body": ((0, -0.15, 0.2), (0, 0.15, 0.2), None),
    "tail": ((0, -0.18, 0.2), (0, -0.3, 0.55), "body"),
}


def _squirrel_actions(arm):
    _action(arm, "walk", 0.35, {0.0: {"body": (0.2, 0, 0), "tail": (0.3, 0, 0)}, 0.5: {"body": (-0.15, 0, 0), "tail": (-0.2, 0, 0)},
                                1.0: {"body": (0.2, 0, 0), "tail": (0.3, 0, 0)}})
    _action(arm, "idle", 1.4, {0.0: {"tail": (0, 0, 0.2)}, 0.5: {"tail": (0, 0, -0.2)}, 1.0: {"tail": (0, 0, 0.2)}})


def squirrel(mb: MeshBuilder, p, rng):
    fur = p.get("fur", "#a0602e")
    mb.bone = "body"
    mb.sphere((0, 0, 0.2), 0.14, "prop:cloth", fur, subdivisions=2, scale=(0.85, 1.3, 1.0))
    mb.sphere((0, 0.17, 0.3), 0.085, "prop:cloth", fur, subdivisions=2)
    mb.sphere((0, 0.05, 0.16), 0.09, "prop:cloth", "#e8d0a8", subdivisions=1)
    for sx in (-1, 1):
        mb.sphere((sx * 0.045, 0.23, 0.33), 0.018, "prop:plain", "#101010", subdivisions=1)
        mb.cylinder((sx * 0.04, 0.16, 0.37), (sx * 0.05, 0.15, 0.43), 0.025, 0.0, "prop:cloth", fur, segments=4)
    mb.bone = "tail"
    mb.tube([Vector((0, -0.15, 0.2)), Vector((0, -0.3, 0.32)), Vector((0, -0.28, 0.5)), Vector((0, -0.2, 0.58))],
            [0.05, 0.09, 0.09, 0.05], "prop:cloth", fur, segments=6)
    mb.bone = None
    return {"rig": SQUIRREL_BONES, "actions": _squirrel_actions}


BIRD_BONES = {
    "body": ((0, -0.1, 0.25), (0, 0.1, 0.25), None),
    "wing.R": ((0.06, 0, 0.3), (0.3, 0, 0.3), "body"),
    "wing.L": ((-0.06, 0, 0.3), (-0.3, 0, 0.3), "body"),
}


def _bird_actions(arm):
    _action(arm, "fly", 0.25, {0.0: {"wing.R": (0, 0.9, 0), "wing.L": (0, -0.9, 0)}, 0.5: {"wing.R": (0, -0.7, 0), "wing.L": (0, 0.7, 0)},
                               1.0: {"wing.R": (0, 0.9, 0), "wing.L": (0, -0.9, 0)}})
    _action(arm, "walk", 0.4, {0.0: {"body": (0.15, 0, 0)}, 0.5: {"body": (-0.1, 0, 0)}, 1.0: {"body": (0.15, 0, 0)}})
    _action(arm, "idle", 1.5, {0.0: {"body": (0, 0, 0)}, 0.5: {"body": (0.2, 0, 0)}, 1.0: {"body": (0, 0, 0)}})


def songbird(mb: MeshBuilder, p, rng):
    """Small perching bird (Bird, Blue Jay, Cardinal): round body, short beak, stubby wings."""
    body, breast = p.get("body", "#8a6a4a"), p.get("breast", "#d8b890")
    mb.bone = "body"
    mb.sphere((0, 0, 0.25), 0.12, "prop:cloth", body, subdivisions=2, scale=(0.9, 1.3, 0.95))
    mb.sphere((0, 0.05, 0.21), 0.08, "prop:cloth", breast, subdivisions=1)
    mb.sphere((0, 0.14, 0.34), 0.075, "prop:cloth", p.get("head", body), subdivisions=2)
    mb.cylinder((0, 0.2, 0.34), (0, 0.27, 0.33), 0.025, 0.0, "prop:plain", "#e0a030", segments=5)
    for sx in (-1, 1):
        mb.sphere((sx * 0.045, 0.19, 0.36), 0.013, "prop:plain", "#101010", subdivisions=1)
        mb.cylinder((sx * 0.03, 0.0, 0.14), (sx * 0.03, 0.02, 0.0), 0.008, 0.008, "prop:plain", "#c08040", segments=3)
    mb.extrude_polygon([(-0.04, -0.1), (0.04, -0.1), (0.07, -0.24), (-0.07, -0.24)], 0.012, "prop:cloth", body,
                       matrix=Matrix.Translation((0, 0, 0.28)) @ FLAT)
    for side, sx in (("R", 1), ("L", -1)):
        mb.bone = f"wing.{side}"
        mb.extrude_polygon([(sx * 0.06, 0.07), (sx * 0.24, 0.02), (sx * 0.26, -0.06), (sx * 0.06, -0.08)], 0.012, "prop:cloth",
                           p.get("wing", body), matrix=Matrix.Translation((0, 0, 0.3)) @ FLAT)
    mb.bone = None
    return {"rig": BIRD_BONES, "actions": _bird_actions}


FLUTTER_BONES = {
    "body": ((0, -0.1, 0.4), (0, 0.1, 0.4), None),
    "wing.R": ((0.02, 0, 0.42), (0.3, 0, 0.42), "body"),
    "wing.L": ((-0.02, 0, 0.42), (-0.3, 0, 0.42), "body"),
}


def _flutter_actions(arm):
    _action(arm, "fly", 0.3, {0.0: {"wing.R": (0, 1.0, 0), "wing.L": (0, -1.0, 0)}, 0.5: {"wing.R": (0, -0.5, 0), "wing.L": (0, 0.5, 0)},
                              1.0: {"wing.R": (0, 1.0, 0), "wing.L": (0, -1.0, 0)}})


def butterfly(mb: MeshBuilder, p, rng):
    wing, spots = p.get("wing", "#f0a020"), p.get("spots", "#201a10")
    mb.bone = "body"
    mb.cylinder((0, -0.12, 0.4), (0, 0.1, 0.41), 0.02, 0.015, "prop:cloth", "#201a10", segments=5)
    mb.sphere((0, 0.12, 0.41), 0.025, "prop:cloth", "#201a10", subdivisions=1)
    for sx in (-1, 1):
        mb.cylinder((sx * 0.01, 0.13, 0.42), (sx * 0.05, 0.22, 0.47), 0.004, 0.004, "prop:plain", "#201a10", segments=3)
    for side, sx in (("R", 1), ("L", -1)):
        mb.bone = f"wing.{side}"
        mat = Matrix.Translation((0, 0, 0.42)) @ FLAT
        mb.extrude_polygon([(sx * 0.02, 0.02), (sx * 0.2, 0.16), (sx * 0.3, 0.1), (sx * 0.24, -0.02), (sx * 0.02, -0.01)], 0.004,
                           "prop:plain", wing, matrix=mat)
        mb.extrude_polygon([(sx * 0.02, -0.01), (sx * 0.2, -0.04), (sx * 0.2, -0.16), (sx * 0.08, -0.14)], 0.004, "prop:plain", wing, matrix=mat)
        mb.sphere((sx * 0.2, 0.08, 0.425), 0.03, "prop:cloth", spots, subdivisions=1, scale=(1, 1, 0.2))
    mb.bone = None
    return {"rig": FLUTTER_BONES, "actions": _flutter_actions}


def firefly(mb: MeshBuilder, p, rng):
    glow = p.get("glow", "#d8ff60")
    mb.bone = "body"
    mb.sphere((0, 0.05, 0.4), 0.04, "prop:cloth", "#302418", subdivisions=1, scale=(0.9, 1.2, 0.9))
    mb.sphere((0, -0.06, 0.39), 0.055, "prop:plain:glow", glow, subdivisions=2, scale=(0.9, 1.4, 0.9))
    mb.sphere((0, 0.11, 0.41), 0.025, "prop:cloth", "#c04020", subdivisions=1)
    for side, sx in (("R", 1), ("L", -1)):
        mb.bone = f"wing.{side}"
        mb.extrude_polygon([(sx * 0.02, 0.06), (sx * 0.14, 0.02), (sx * 0.14, -0.04), (sx * 0.02, -0.02)], 0.004, "prop:gel", "#e0f0ff",
                           matrix=Matrix.Translation((0, 0, 0.44)) @ FLAT)
    mb.bone = None
    return {"rig": FLUTTER_BONES, "actions": _flutter_actions}


WORM_BONES = {"body": ((0, -0.15, 0.05), (0, 0.15, 0.05), None)}


def _worm_actions(arm):
    _action(arm, "walk", 0.6, {0.0: {"body": (0, 0, 0.25)}, 0.5: {"body": (0, 0, -0.25)}, 1.0: {"body": (0, 0, 0.25)}})
    _action(arm, "idle", 1.2, {0.0: {"body": (0, 0, 0.1)}, 0.5: {"body": (0, 0, -0.1)}, 1.0: {"body": (0, 0, 0.1)}})


def worm(mb: MeshBuilder, p, rng):
    skin = p.get("skin", "#d88a8a")
    mb.bone = "body"
    pts = [Vector((math.sin(k * 0.9) * 0.04, -0.2 + k * 0.08, 0.045)) for k in range(6)]
    mb.tube(pts, [0.03, 0.04, 0.045, 0.045, 0.04, 0.035], "prop:flesh", skin, segments=6)
    mb.sphere((pts[2].x, pts[2].y, 0.05), 0.05, "prop:flesh", p.get("band", "#c06a6a"), subdivisions=1, scale=(1, 0.6, 1))
    mb.bone = None
    return {"rig": WORM_BONES, "actions": _worm_actions}


FROG_BONES = {
    "body": ((0, -0.1, 0.15), (0, 0.1, 0.15), None),
    "leg.R": ((0.1, -0.08, 0.1), (0.2, -0.2, 0.02), "body"),
    "leg.L": ((-0.1, -0.08, 0.1), (-0.2, -0.2, 0.02), "body"),
}


def _frog_actions(arm):
    _action(arm, "walk", 0.5, {0.0: {"body": (0.3, 0, 0), "leg.R": (-0.6, 0, 0), "leg.L": (-0.6, 0, 0)},
                               0.5: {"body": (-0.1, 0, 0), "leg.R": (0.3, 0, 0), "leg.L": (0.3, 0, 0)},
                               1.0: {"body": (0.3, 0, 0), "leg.R": (-0.6, 0, 0), "leg.L": (-0.6, 0, 0)}})
    _action(arm, "idle", 2.0, {0.0: {"body": (0, 0, 0)}, 0.5: {"body": (0.05, 0, 0)}, 1.0: {"body": (0, 0, 0)}})


def frog(mb: MeshBuilder, p, rng):
    skin, belly = p.get("skin", "#4a9a3a"), p.get("belly", "#c8e090")
    mb.bone = "body"
    mb.sphere((0, 0, 0.13), 0.14, "prop:gel", skin, subdivisions=2, scale=(1.1, 1.2, 0.7))
    mb.sphere((0, 0.04, 0.08), 0.1, "prop:gel", belly, subdivisions=1, scale=(1.0, 1.1, 0.5))
    for sx in (-1, 1):
        mb.sphere((sx * 0.07, 0.1, 0.23), 0.04, "prop:gel", skin, subdivisions=1)
        mb.sphere((sx * 0.07, 0.13, 0.24), 0.022, "prop:plain", "#101010", subdivisions=1)
        mb.tube([Vector((sx * 0.08, 0.1, 0.08)), Vector((sx * 0.12, 0.18, 0.0))], [0.025, 0.02], "prop:gel", skin, segments=4)
    for side, sx in (("R", 1), ("L", -1)):
        mb.bone = f"leg.{side}"
        mb.tube([Vector((sx * 0.1, -0.06, 0.1)), Vector((sx * 0.2, -0.12, 0.12)), Vector((sx * 0.18, -0.2, 0.01))],
                [0.04, 0.03, 0.02], "prop:gel", skin, segments=5)
    mb.bone = None
    return {"rig": FROG_BONES, "actions": _frog_actions}


DUCK_BONES = {"body": ((0, -0.15, 0.25), (0, 0.15, 0.25), None)}


def _duck_actions(arm):
    _action(arm, "walk", 0.5, {0.0: {"body": (0, 0, 0.12)}, 0.5: {"body": (0, 0, -0.12)}, 1.0: {"body": (0, 0, 0.12)}})
    _action(arm, "idle", 2.0, {0.0: {"body": (0, 0, 0)}, 0.5: {"body": (0.1, 0, 0)}, 1.0: {"body": (0, 0, 0)}})


def duck(mb: MeshBuilder, p, rng):
    body, head = p.get("body", "#8a7a6a"), p.get("head", "#2a6a3a")
    mb.bone = "body"
    mb.sphere((0, -0.02, 0.22), 0.16, "prop:cloth", body, subdivisions=2, scale=(0.85, 1.35, 0.8))
    mb.cylinder((0, 0.14, 0.26), (0, 0.18, 0.4), 0.05, 0.045, "prop:cloth", head, segments=6)
    mb.sphere((0, 0.19, 0.44), 0.075, "prop:cloth", head, subdivisions=2)
    mb.sphere((0, 0.28, 0.42), 0.04, "prop:plain", "#e0a030", subdivisions=1, scale=(0.9, 1.5, 0.5))
    mb.sphere((0, 0.15, 0.33), 0.052, "prop:cloth", "#f0f0e8", subdivisions=1, scale=(1, 0.4, 0.3))
    for sx in (-1, 1):
        mb.sphere((sx * 0.045, 0.23, 0.46), 0.013, "prop:plain", "#101010", subdivisions=1)
        mb.cylinder((sx * 0.05, 0.0, 0.1), (sx * 0.05, 0.02, 0.0), 0.012, 0.012, "prop:plain", "#e08a30", segments=3)
        mb.sphere((sx * 0.12, -0.04, 0.26), 0.07, "prop:cloth", p.get("wing", "#6a5a4a"), subdivisions=1, scale=(0.4, 1.4, 0.7))
    mb.bone = None
    return {"rig": DUCK_BONES, "actions": _duck_actions}


GENERATORS = {"bunny": bunny, "squirrel": squirrel, "songbird": songbird, "butterfly": butterfly, "firefly": firefly, "worm": worm,
              "frog": frog, "duck": duck}


def critter_item(mb: MeshBuilder, p, rng):
    """A caught critter as a held item: the creature's mesh without its rig."""
    params = {k: v for k, v in p.items() if k != "creature"}
    GENERATORS[p["creature"]](mb, params, rng)
    mb.bone = None


ITEM_GENERATORS = {"critter": critter_item}

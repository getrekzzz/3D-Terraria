"""Wild creatures: birds, crabs, jellyfish, fish, flying insects and snatcher heads. Rigid parts on small rigs with
looping actions ("fly", "walk", "idle"). Front faces Blender +Y; the origin is the bottom of the creature's hitbox,
so bodies are raised by half their height."""
import math

from mathutils import Matrix, Vector

from .creatures import _action
from .mesh import MeshBuilder

BIRD_BONES = {
    "body": ((0, -0.2, 0.5), (0, 0.2, 0.5), None),
    "wing.R": ((0.1, 0, 0.58), (0.7, 0, 0.6), "body"),
    "wing.L": ((-0.1, 0, 0.58), (-0.7, 0, 0.6), "body"),
}


def _bird_actions(arm):
    flap = {0.0: {"wing.R": (0, 0.8, 0), "wing.L": (0, -0.8, 0)}, 0.5: {"wing.R": (0, -0.6, 0), "wing.L": (0, 0.6, 0)},
            1.0: {"wing.R": (0, 0.8, 0), "wing.L": (0, -0.8, 0)}}
    _action(arm, "fly", 0.55, flap)
    _action(arm, "idle", 2.0, {0.0: {"wing.R": (0, 0.2, 0), "wing.L": (0, -0.2, 0)}, 0.5: {"wing.R": (0, 0.1, 0), "wing.L": (0, -0.1, 0)},
                                1.0: {"wing.R": (0, 0.2, 0), "wing.L": (0, -0.2, 0)}})


def bird(mb: MeshBuilder, p, rng):
    """Vulture-like: dark body, bald pink head on a bare neck, broad fingered wings."""
    feathers, head = p.get("feathers", "#3a3028"), p.get("head", "#d09080")
    mb.bone = "body"
    mb.sphere((0, 0, 0.5), 0.22, "prop:cloth", feathers, subdivisions=2, scale=(0.9, 1.5, 0.85))
    mb.cylinder((0, 0.25, 0.55), (0, 0.4, 0.68), 0.05, 0.04, "prop:flesh", head, segments=6)
    mb.sphere((0, 0.44, 0.7), 0.08, "prop:flesh", head, subdivisions=1)
    mb.cylinder((0, 0.5, 0.69), (0, 0.6, 0.64), 0.03, 0.0, "prop:plain", "#e0c060", segments=5)
    mb.extrude_polygon([(-0.12, -0.3), (0.12, -0.3), (0.2, -0.55), (-0.2, -0.55)], 0.02, "prop:cloth", feathers,
                       matrix=Matrix.Translation((0, 0, 0.5)) @ Matrix.Rotation(-math.pi / 2, 4, "X"))
    for side, sx in (("R", 1), ("L", -1)):
        mb.bone = f"wing.{side}"
        pts = [(sx * 0.1, 0.12), (sx * 0.5, 0.14), (sx * 0.8, 0.02), (sx * 0.78, -0.08), (sx * 0.62, -0.02),
               (sx * 0.6, -0.14), (sx * 0.4, -0.08), (sx * 0.3, -0.18), (sx * 0.1, -0.1)]
        mb.extrude_polygon(pts, 0.015, "prop:cloth", p.get("wing", "#2a221c"), matrix=Matrix.Translation((0, 0, 0.58)) @ Matrix.Rotation(-math.pi / 2, 4, "X"))
    mb.bone = None
    return {"rig": BIRD_BONES, "actions": _bird_actions}


CRAB_BONES = {
    "body": ((0, -0.1, 0.25), (0, 0.1, 0.25), None),
    "claw.R": ((0.25, 0.15, 0.3), (0.38, 0.35, 0.3), "body"),
    "claw.L": ((-0.25, 0.15, 0.3), (-0.38, 0.35, 0.3), "body"),
}


def _crab_actions(arm):
    _action(arm, "walk", 0.5, {0.0: {"body": (0, 0.12, 0), "claw.R": (0.3, 0, 0), "claw.L": (-0.2, 0, 0)},
                               0.5: {"body": (0, -0.12, 0), "claw.R": (-0.2, 0, 0), "claw.L": (0.3, 0, 0)},
                               1.0: {"body": (0, 0.12, 0), "claw.R": (0.3, 0, 0), "claw.L": (-0.2, 0, 0)}})
    _action(arm, "idle", 1.6, {0.0: {"claw.R": (0.1, 0, 0), "claw.L": (0.1, 0, 0)}, 0.5: {"claw.R": (0.4, 0, 0), "claw.L": (0.0, 0, 0)},
                               1.0: {"claw.R": (0.1, 0, 0), "claw.L": (0.1, 0, 0)}})


def crab(mb: MeshBuilder, p, rng):
    shell = p.get("shell", "#c0482a")
    mb.bone = "body"
    mb.sphere((0, 0, 0.25), 0.28, "prop:clay", shell, subdivisions=2, scale=(1.3, 1.0, 0.55))
    for sx in (-1, 1):
        mb.cylinder((sx * 0.06, 0.18, 0.32), (sx * 0.08, 0.22, 0.45), 0.018, None, "prop:clay", shell, segments=4)
        mb.sphere((sx * 0.08, 0.22, 0.47), 0.035, "prop:plain", "#101010", subdivisions=1)
        for k in range(3):
            y = -0.12 + k * 0.1
            knee = Vector((sx * 0.42, y, 0.28))
            mb.tube([Vector((sx * 0.25, y, 0.22)), knee, Vector((sx * 0.5, y + 0.02, 0.0))], [0.03, 0.025, 0.012], "prop:clay", shell, segments=4)
    for side, sx in (("R", 1), ("L", -1)):
        mb.bone = f"claw.{side}"
        mb.tube([Vector((sx * 0.25, 0.15, 0.3)), Vector((sx * 0.35, 0.3, 0.32))], [0.04, 0.035], "prop:clay", shell, segments=5)
        mb.sphere((sx * 0.38, 0.4, 0.32), 0.09, "prop:clay", shell, subdivisions=1, scale=(0.8, 1.3, 0.7))
        mb.cylinder((sx * 0.34, 0.45, 0.32), (sx * 0.32, 0.56, 0.32), 0.025, 0.0, "prop:clay", shell, segments=4)
    mb.bone = None
    return {"rig": CRAB_BONES, "actions": _crab_actions}


JELLY_BONES = {"bell": ((0, 0, 0.55), (0, 0, 0.9), None)}
for _k in range(6):
    _a = _k * math.pi / 3
    JELLY_BONES[f"t{_k}"] = ((math.cos(_a) * 0.2, math.sin(_a) * 0.2, 0.5), (math.cos(_a) * 0.25, math.sin(_a) * 0.25, 0.1), "bell")


def _jelly_actions(arm):
    open_ = {f"t{k}": (0.35 * math.sin(k * math.pi / 3), -0.35 * math.cos(k * math.pi / 3), 0) for k in range(6)}
    closed = {f"t{k}": (-0.15 * math.sin(k * math.pi / 3), 0.15 * math.cos(k * math.pi / 3), 0) for k in range(6)}
    _action(arm, "fly", 0.9, {0.0: closed, 0.3: open_, 1.0: closed})
    _action(arm, "idle", 2.4, {0.0: closed, 0.5: open_, 1.0: closed})


def jelly(mb: MeshBuilder, p, rng):
    """Translucent glowing bell with trailing tentacles."""
    body = p.get("color", "#6ab0ff")
    mb.bone = "bell"
    mb.lathe([(0.0, 0.45), (0.3, 0.46), (0.33, 0.6), (0.25, 0.78), (0.12, 0.86), (0.0, 0.88)], "prop:gel", body, segments=16)
    mb.sphere((0, 0, 0.62), 0.12, "prop:plain:glow", p.get("core", "#c0e8ff"), subdivisions=1)
    for k in range(6):
        a = k * math.pi / 3
        mb.bone = f"t{k}"
        pts = [Vector((math.cos(a) * 0.22, math.sin(a) * 0.22, 0.48)), Vector((math.cos(a) * 0.26, math.sin(a) * 0.26, 0.3)),
               Vector((math.cos(a) * 0.22 + 0.04, math.sin(a) * 0.22, 0.1))]
        mb.tube(pts, [0.03, 0.02, 0.006], "prop:gel", body, segments=4)
    mb.bone = None
    return {"rig": JELLY_BONES, "actions": _jelly_actions}


FISH_BONES = {
    "body": ((0, -0.1, 0.25), (0, 0.25, 0.25), None),
    "tail": ((0, -0.1, 0.25), (0, -0.4, 0.25), "body"),
}


def _fish_actions(arm):
    _action(arm, "fly", 0.35, {0.0: {"tail": (0, 0, 0.5), "body": (0, 0, -0.1)}, 0.5: {"tail": (0, 0, -0.5), "body": (0, 0, 0.1)},
                               1.0: {"tail": (0, 0, 0.5), "body": (0, 0, -0.1)}})
    _action(arm, "idle", 1.2, {0.0: {"tail": (0, 0, 0.2)}, 0.5: {"tail": (0, 0, -0.2)}, 1.0: {"tail": (0, 0, 0.2)}})


def fish(mb: MeshBuilder, p, rng):
    """Toothy river fish: deep body, underbite, fins; the tail swings."""
    scales, belly = p.get("scales", "#6a7a5a"), p.get("belly", "#c05040")
    mb.bone = "body"
    mb.sphere((0, 0.05, 0.25), 0.2, "prop:gel", scales, subdivisions=2, scale=(0.55, 1.4, 0.95))
    mb.sphere((0, 0.1, 0.17), 0.14, "prop:flesh", belly, subdivisions=1, scale=(0.5, 1.2, 0.6))
    for sx in (-1, 1):
        mb.sphere((sx * 0.08, 0.26, 0.3), 0.035, "prop:plain", "#f0f000", subdivisions=1)
    for k in range(5):
        x = -0.06 + k * 0.03
        mb.cylinder((x, 0.3, 0.16), (x, 0.31, 0.22), 0.01, 0.0, "prop:plain", "#f4f0e8", segments=3)
    # Dorsal fin in the body's vertical plane (polygon x -> body Y, z -> height).
    mb.extrude_polygon([(0.16, 0.0), (-0.14, 0.0), (0.0, 0.16)], 0.01, "prop:gel", scales,
                       matrix=Matrix.Translation((0, 0, 0.4)) @ Matrix.Rotation(math.pi / 2, 4, "Z"))
    mb.bone = "tail"
    mb.extrude_polygon([(0.0, 0.0), (-0.24, 0.18), (-0.24, -0.18)], 0.012, "prop:gel", scales,
                       matrix=Matrix.Translation((0, -0.2, 0.25)) @ Matrix.Rotation(math.pi / 2, 4, "Z"))
    mb.bone = None
    return {"rig": FISH_BONES, "actions": _fish_actions}


INSECT_BONES = {
    "body": ((0, -0.2, 0.45), (0, 0.2, 0.45), None),
    "wing.R": ((0.05, 0.05, 0.58), (0.4, 0.0, 0.62), "body"),
    "wing.L": ((-0.05, 0.05, 0.58), (-0.4, 0.0, 0.62), "body"),
}


def _insect_actions(arm):
    buzz = {0.0: {"wing.R": (0, 0.6, 0), "wing.L": (0, -0.6, 0)}, 0.5: {"wing.R": (0, -0.4, 0), "wing.L": (0, 0.4, 0)},
            1.0: {"wing.R": (0, 0.6, 0), "wing.L": (0, -0.6, 0)}}
    _action(arm, "fly", 0.12, buzz)


def insect(mb: MeshBuilder, p, rng):
    """Hornet: striped abdomen with a stinger, big eyes, translucent wings."""
    yellow, black = p.get("color", "#e0b020"), p.get("stripes", "#201a10")
    mb.bone = "body"
    mb.sphere((0, 0.18, 0.5), 0.1, "prop:cloth", black, subdivisions=1)
    for sx in (-1, 1):
        mb.sphere((sx * 0.06, 0.24, 0.52), 0.05, "prop:plain:glow", p.get("eyes", "#a0ff60"), subdivisions=1)
    mb.sphere((0, 0.02, 0.48), 0.11, "prop:cloth", yellow, subdivisions=1)
    for k in range(3):
        mb.sphere((0, -0.14 - k * 0.09, 0.42 - k * 0.04), 0.13 - k * 0.02, "prop:cloth", yellow if k % 2 == 0 else black, subdivisions=1)
    mb.cylinder((0, -0.38, 0.33), (0, -0.48, 0.25), 0.03, 0.0, "prop:plain", "#101010", segments=4)
    for k in range(3):
        for sx in (-1, 1):
            y = 0.08 - k * 0.07
            mb.tube([Vector((sx * 0.06, y, 0.42)), Vector((sx * 0.16, y, 0.36)), Vector((sx * 0.18, y + 0.02, 0.22))], [0.015, 0.012, 0.006], "prop:cloth", black, segments=3)
    for side, sx in (("R", 1), ("L", -1)):
        mb.bone = f"wing.{side}"
        mb.extrude_polygon([(sx * 0.05, 0.05), (sx * 0.42, 0.08), (sx * 0.45, -0.05), (sx * 0.1, -0.06)], 0.006, "prop:gel", "#e0f0ff",
                           matrix=Matrix.Translation((0, 0, 0.6)) @ Matrix.Rotation(-math.pi / 2, 4, "X"))
    mb.bone = None
    return {"rig": INSECT_BONES, "actions": _insect_actions}


SNATCHER_BONES = {
    "head": ((0, -0.2, 0.4), (0, 0.3, 0.4), None),
    "jaw": ((0, -0.15, 0.36), (0, 0.3, 0.3), "head"),
}


def _snatcher_actions(arm):
    _action(arm, "idle", 1.4, {0.0: {"jaw": (0.1, 0, 0)}, 0.5: {"jaw": (0.3, 0, 0)}, 1.0: {"jaw": (0.1, 0, 0)}})
    _action(arm, "fly", 0.4, {0.0: {"jaw": (0.05, 0, 0)}, 0.4: {"jaw": (0.8, 0, 0)}, 1.0: {"jaw": (0.05, 0, 0)}})


def snatcher(mb: MeshBuilder, p, rng):
    """Carnivorous plant head: leafy upper lip, hinged jaw, rows of teeth (the stalk is drawn by the game)."""
    leaf, inner = p.get("leaf", "#4a8a2a"), p.get("inner", "#c03050")
    mb.bone = "head"
    mb.sphere((0, 0.05, 0.46), 0.26, "prop:leaves", leaf, subdivisions=2, scale=(1.0, 1.25, 0.55))
    mb.sphere((0, 0.08, 0.4), 0.2, "prop:flesh", inner, subdivisions=1, scale=(0.9, 1.1, 0.35))
    for k in range(7):
        x = -0.15 + k * 0.05
        mb.cylinder((x, 0.28 - abs(x) * 0.6, 0.38), (x, 0.28 - abs(x) * 0.6, 0.31), 0.018, 0.0, "prop:plain", "#f4f0e0", segments=3)
    for k in range(5):
        a = k * 2 * math.pi / 5
        # Petals fanned around the back of the head (rotating about the facing axis).
        mb.extrude_polygon([(0, 0), (0.14, 0.05), (0.26, 0), (0.14, -0.05)], 0.01, "prop:leaves", leaf,
                           matrix=Matrix.Translation((0, -0.2, 0.42)) @ Matrix.Rotation(a, 4, "Y"))
    mb.bone = "jaw"
    mb.sphere((0, 0.05, 0.33), 0.22, "prop:leaves", leaf, subdivisions=1, scale=(0.95, 1.2, 0.4))
    for k in range(6):
        x = -0.13 + k * 0.052
        mb.cylinder((x, 0.24 - abs(x) * 0.6, 0.36), (x, 0.24 - abs(x) * 0.6, 0.42), 0.016, 0.0, "prop:plain", "#f4f0e0", segments=3)
    mb.bone = None
    return {"rig": SNATCHER_BONES, "actions": _snatcher_actions}


GENERATORS = {"bird": bird, "crab": crab, "jelly": jelly, "fish": fish, "insect": insect, "snatcher": snatcher}

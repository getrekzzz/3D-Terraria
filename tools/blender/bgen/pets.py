"""P3-18 pets and light pets: the summoning items (Fairy Bell, Magic Lantern, Wisp in a Bottle) and two small pet
creatures (Baby Penguin, Baby Truffle). Low-poly, existing material layers only. Items stand on Z with their origin at
the bottom; creatures face Blender +Y with the origin at the bottom of the hitbox (as in critters.py)."""
import math

from . import generator
from .creatures import _action
from .mesh import MeshBuilder


@generator
def pet_bell(mb: MeshBuilder, p, rng):
    """Fairy Bell: a small golden bell on a loop, with a pink glow inside the mouth."""
    gold, glow = p.get("metal", "#e8c040"), p.get("glow", "#ff9ae8")
    mb.lathe([(0.11, 0.0), (0.105, 0.02), (0.08, 0.06), (0.06, 0.12), (0.045, 0.17), (0.0, 0.19)], "prop:gold", gold, segments=12)
    mb.sphere((0, 0, 0.02), 0.03, "prop:plain:glow", glow, subdivisions=1)
    mb.cylinder((0, 0, 0.18), (0, 0, 0.24), 0.012, 0.012, "prop:gold", gold, segments=6)
    mb.sphere((0, 0, 0.25), 0.025, "prop:gold", gold, subdivisions=1, scale=(1, 0.4, 1))


@generator
def pet_lantern(mb: MeshBuilder, p, rng):
    """Magic Lantern: a dark iron cage around a warm glowing core, with a carrying ring."""
    iron, glow = p.get("metal", "#3a3a44"), p.get("glow", "#ffd070")
    mb.box((-0.08, -0.08, 0.0), (0.08, 0.08, 0.025), "prop:dark_iron", iron)
    mb.box((-0.065, -0.065, 0.2), (0.065, 0.065, 0.225), "prop:dark_iron", iron)
    for sx in (-1, 1):
        for sy in (-1, 1):
            mb.cylinder((sx * 0.065, sy * 0.065, 0.02), (sx * 0.055, sy * 0.055, 0.205), 0.009, 0.009, "prop:dark_iron", iron, segments=4)
    mb.sphere((0, 0, 0.11), 0.055, "prop:plain:glow", glow, subdivisions=1, scale=(1, 1, 1.3))
    mb.cylinder((0, 0, 0.225), (0, 0, 0.26), 0.035, 0.012, "prop:dark_iron", iron, segments=6)
    ring = [(0, math.cos(a) * 0.035, 0.29 + math.sin(a) * 0.035) for a in (k * math.tau / 8 for k in range(9))]
    mb.tube(ring, [0.007] * len(ring), "prop:dark_iron", iron, segments=4)


@generator
def pet_bottle(mb: MeshBuilder, p, rng):
    """Wisp in a Bottle: a clear bottle with a cork and a glowing wisp inside."""
    glow = p.get("glow", "#8affe8")
    mb.sphere((0, 0, 0.09), 0.05, "prop:plain:glow", glow, subdivisions=1, scale=(1, 1, 1.2))
    mb.lathe([(0.0, 0.0), (0.075, 0.005), (0.085, 0.06), (0.08, 0.13), (0.035, 0.175), (0.03, 0.22), (0.0, 0.22)],
             "prop:gel:clear", (0.8, 0.95, 1.0, 0.3), segments=12)
    mb.cylinder((0, 0, 0.21), (0, 0, 0.25), 0.032, 0.036, "prop:planks", "#a07850", segments=8)


PENGUIN_BONES = {
    "body": ((0, 0, 0.0), (0, 0, 0.3), None),
    "flipper.R": ((0.1, 0, 0.22), (0.15, 0, 0.08), "body"),
    "flipper.L": ((-0.1, 0, 0.22), (-0.15, 0, 0.08), "body"),
}


def _penguin_actions(arm):
    _action(arm, "walk", 0.4, {0.0: {"body": (0, 0.18, 0), "flipper.R": (0, 0.3, 0), "flipper.L": (0, -0.3, 0)},
                               0.5: {"body": (0, -0.18, 0), "flipper.R": (0, -0.1, 0), "flipper.L": (0, 0.1, 0)},
                               1.0: {"body": (0, 0.18, 0), "flipper.R": (0, 0.3, 0), "flipper.L": (0, -0.3, 0)}})
    _action(arm, "idle", 1.6, {0.0: {"flipper.R": (0, 0, 0), "flipper.L": (0, 0, 0)}, 0.5: {"flipper.R": (0, 0.25, 0), "flipper.L": (0, -0.25, 0)},
                               1.0: {"flipper.R": (0, 0, 0), "flipper.L": (0, 0, 0)}})


@generator
def baby_penguin(mb: MeshBuilder, p, rng):
    """A round baby penguin: dark back, white belly, orange beak and feet."""
    back, belly = p.get("back", "#2a2e3a"), p.get("belly", "#f0f2f4")
    mb.bone = "body"
    mb.sphere((0, 0, 0.17), 0.14, "prop:cloth", back, subdivisions=2, scale=(1, 0.95, 1.15))
    mb.sphere((0, 0.05, 0.15), 0.11, "prop:cloth", belly, subdivisions=1, scale=(0.9, 0.8, 1.1))
    mb.sphere((0, 0.02, 0.33), 0.09, "prop:cloth", back, subdivisions=2)
    for sx in (-1, 1):
        mb.sphere((sx * 0.035, 0.085, 0.35), 0.022, "prop:plain", "#ffffff", subdivisions=1)
        mb.sphere((sx * 0.035, 0.1, 0.355), 0.011, "prop:plain", "#101010", subdivisions=1)
        mb.sphere((sx * 0.05, 0.06, 0.01), 0.03, "prop:flesh", "#f0a030", subdivisions=1, scale=(1, 1.6, 0.4))
    mb.cylinder((0, 0.09, 0.315), (0, 0.15, 0.31), 0.022, 0.0, "prop:flesh", "#f0a030", segments=6)
    for side, sx in (("R", 1), ("L", -1)):
        mb.bone = f"flipper.{side}"
        mb.sphere((sx * 0.13, 0, 0.15), 0.035, "prop:cloth", back, subdivisions=1, scale=(0.4, 1, 2.2))
    mb.bone = None
    return {"rig": PENGUIN_BONES, "actions": _penguin_actions}


TRUFFLE_BONES = {
    "body": ((0, 0, 0.0), (0, 0, 0.2), None),
    "cap": ((0, 0, 0.2), (0, 0, 0.4), "body"),
}


def _truffle_actions(arm):
    _action(arm, "walk", 0.45, {0.0: {"body": (0.2, 0, 0), "cap": (-0.15, 0, 0)}, 0.5: {"body": (-0.15, 0, 0), "cap": (0.15, 0, 0)},
                                1.0: {"body": (0.2, 0, 0), "cap": (-0.15, 0, 0)}})
    _action(arm, "idle", 2.0, {0.0: {"cap": (0, 0, 0)}, 0.5: {"cap": (0, 0.12, 0)}, 1.0: {"cap": (0, 0, 0)}})


@generator
def baby_truffle(mb: MeshBuilder, p, rng):
    """A baby Truffle: a pale stubby stalk with a glowing blue cap and two dark eyes."""
    stalk, cap = p.get("stalk", "#e8e0d0"), p.get("cap", "#5a9aff")
    mb.bone = "body"
    mb.sphere((0, 0, 0.12), 0.1, "prop:flesh", stalk, subdivisions=2, scale=(1, 0.9, 1.2))
    for sx in (-1, 1):
        mb.sphere((sx * 0.035, 0.085, 0.15), 0.016, "prop:plain", "#101018", subdivisions=1)
        mb.sphere((sx * 0.05, 0.02, 0.015), 0.03, "prop:flesh", stalk, subdivisions=1, scale=(1, 1.3, 0.5))
    mb.bone = "cap"
    mb.sphere((0, 0, 0.25), 0.15, "prop:plain:glow", cap, subdivisions=2, scale=(1, 1, 0.55))
    for k in range(4):
        a = k * math.tau / 4 + 0.4
        mb.sphere((math.cos(a) * 0.09, math.sin(a) * 0.09, 0.3), 0.022, "prop:plain", "#d8ecff", subdivisions=1, scale=(1, 1, 0.5))
    mb.bone = None
    return {"rig": TRUFFLE_BONES, "actions": _truffle_actions}

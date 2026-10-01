"""Wiring devices (X-15): timers, teleporter pads, statues, logic gates and lamps; the coloured wrenches and the
Actuator. Objects follow furniture.py (origin at the footprint's floor centre, front faces Blender +Y, cells of 0.5 m);
items follow items.py (held along +Z)."""
import math

from mathutils import Matrix, Vector

from . import generator


@generator
def wiring_timer(mb, p, rng):
    """A small stone box with a clock face; the hand's angle tells the period apart."""
    mb.box((-0.2, -0.2, 0), (0.2, 0.2, 0.36), "prop:stone", "#8a8a86")
    mb.cylinder((0, 0.2, 0.18), (0, 0.22, 0.18), 0.13, 0.13, "prop:plain", "#f0ead8", segments=16)
    a = float(p.get("hand", 0.0)) * math.pi * 2
    tip = Vector((math.sin(a) * 0.1, 0.235, 0.18 + math.cos(a) * 0.1))
    mb.tube([Vector((0, 0.235, 0.18)), tip], [0.01, 0.008], "prop:plain", "#d02a20", segments=4)
    mb.sphere((0, 0.235, 0.18), 0.018, "prop:iron", "#a0a4ac", subdivisions=1)


@generator
def teleporter(mb, p, rng):
    """A low metal pad with a glowing ring inlay."""
    mb.box((-0.75, -0.75, 0), (0.75, 0.75, 0.38), "prop:dark_iron", "#4a4e58")
    mb.box((-0.7, -0.7, 0.38), (0.7, 0.7, 0.46), "prop:iron", "#9aa0a8")
    ring = [Vector((math.cos(t) * 0.5, math.sin(t) * 0.5, 0.47)) for t in (i * 2 * math.pi / 32 for i in range(33))]
    mb.tube(ring, [0.035] * len(ring), "prop:crystal:glow", p.get("glow", "#5ad8ff"), segments=5)
    mb.cylinder((0, 0, 0.46), (0, 0, 0.49), 0.18, 0.18, "prop:crystal:glow", p.get("glow", "#5ad8ff"), segments=16)


@generator
def statue(mb, p, rng):
    """A stone statue: a plinth and a rough carving of what it releases."""
    stone, tint = "prop:stone", p.get("color", "#9a9a96")
    mb.box((-0.45, -0.45, 0), (0.45, 0.45, 0.25), stone, "#7a7a76")
    shape = p.get("shape", "slime")
    if shape == "slime":
        mb.sphere((0, 0, 0.62), 0.38, stone, tint, subdivisions=2, scale=(1, 1, 0.8))
    elif shape == "bunny":
        mb.sphere((0, 0, 0.55), 0.26, stone, tint, subdivisions=2, scale=(0.9, 1.2, 1))
        mb.sphere((0, 0.18, 0.85), 0.16, stone, tint, subdivisions=2)
        for sx in (-1, 1):
            mb.sphere((sx * 0.07, 0.16, 1.12), 0.05, stone, tint, subdivisions=1, scale=(1, 1, 3))
    elif shape == "bat":
        mb.sphere((0, 0, 0.9), 0.15, stone, tint, subdivisions=2)
        for sx in (-1, 1):
            mb.extrude_polygon([(0, 0), (sx * 0.42, 0.18), (sx * 0.36, -0.1), (sx * 0.2, 0.02)], 0.04, stone, tint,
                               matrix=Matrix.Translation((0, 0.02, 0.9)) @ Matrix.Rotation(math.pi / 2, 4, "X"))
        mb.cylinder((0, 0, 0.25), (0, 0, 0.75), 0.05, 0.04, stone, "#7a7a76", segments=8)
    elif shape == "crab":
        mb.sphere((0, 0, 0.5), 0.3, stone, tint, subdivisions=2, scale=(1.3, 1, 0.6))
        for sx in (-1, 1):
            mb.sphere((sx * 0.4, 0.15, 0.6), 0.11, stone, tint, subdivisions=1, scale=(1, 1.4, 1))
    elif shape == "bird":
        mb.sphere((0, 0, 0.6), 0.22, stone, tint, subdivisions=2, scale=(0.8, 1.2, 0.9))
        mb.sphere((0, 0.2, 0.82), 0.12, stone, tint, subdivisions=2)
        mb.cylinder((0, 0.3, 0.82), (0, 0.42, 0.8), 0.04, 0.0, stone, "#b0a080", segments=6)
    elif shape == "bomb":
        mb.sphere((0, 0, 0.6), 0.32, stone, tint, subdivisions=2)
        mb.cylinder((0, 0, 0.9), (0, 0, 1.02), 0.08, 0.08, stone, tint, segments=8)
        mb.tube([Vector((0, 0, 1.02)), Vector((0.05, 0, 1.12)), Vector((0.12, 0.02, 1.16))], [0.02, 0.018, 0.015], stone, "#7a7a76", segments=4)
    else:  # humanoid: zombie, skeleton
        mb.cylinder((0, 0, 0.25), (0, 0, 1.0), 0.18, 0.15, stone, tint, segments=10)
        mb.sphere((0, 0, 1.18), 0.16, stone, tint, subdivisions=2)
        for sx in (-1, 1):
            mb.cylinder((sx * 0.2, 0, 0.95), (sx * 0.24, 0.3, 0.9 if shape == "zombie" else 0.6), 0.05, 0.045, stone, tint, segments=6)


@generator
def logic_gate(mb, p, rng):
    """A metal block with a coloured face plate; its symbol colour names the gate."""
    mb.box((-0.24, -0.24, 0), (0.24, 0.24, 0.48), "prop:dark_iron", "#50545e")
    mb.box((-0.17, 0.24, 0.1), (0.17, 0.255, 0.38), "prop:crystal:glow", p.get("color", "#d02a20"))


@generator
def logic_lamp(mb, p, rng):
    """A frosted lamp cube in an iron frame (lit when its gate input is on)."""
    mb.box((-0.24, -0.24, 0), (0.24, 0.24, 0.05), "prop:iron", "#9aa0a8")
    mb.box((-0.24, -0.24, 0.43), (0.24, 0.24, 0.48), "prop:iron", "#9aa0a8")
    mb.box((-0.2, -0.2, 0.05), (0.2, 0.2, 0.43), "prop:gel", p.get("color", "#e8e0a0"))


@generator(view="item")
def wrench(mb, p, rng):
    """A wrench with a coloured handle (Blue, Green, Yellow Wrench)."""
    mb.box((-0.02, -0.012, -0.12), (0.02, 0.012, 0.22), "prop:plain", p.get("color", "#2a6ad0"))
    mb.box((-0.06, -0.015, 0.22), (0.06, 0.015, 0.28), "prop:iron", "#a0a4ac")
    for sx in (-1, 1):
        mb.box((sx * 0.06 - 0.018, -0.015, 0.28), (sx * 0.06 + 0.018, 0.015, 0.34), "prop:iron", "#a0a4ac")


@generator
def actuator(mb, p, rng):
    """A small brass actuator box with a red contact."""
    mb.box((-0.08, -0.08, 0), (0.08, 0.08, 0.1), "prop:copper", "#c89a4a")
    mb.cylinder((0, 0, 0.1), (0, 0, 0.13), 0.035, 0.035, "prop:plain", "#d02a20", segments=8)

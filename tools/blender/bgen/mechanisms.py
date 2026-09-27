"""Mechanisms and builder's gear: levers, switches, pressure plates, dart traps, the minecart, the Red
Wrench, Wire Cutter, a coil of wire, the Bug Net and a Firefly in a Bottle. Objects follow furniture.py (origin at the
footprint's floor centre, front faces Blender +Y, cells of 0.5 m); items follow items.py (held along +Z)."""
import math

from mathutils import Vector

from .mesh import MeshBuilder

RED = "#d02a20"


def lever(mb: MeshBuilder, p, rng):
    mb.box((-0.1, -0.06, 0), (0.1, 0.06, 0.05), "prop:stone", "#8a8a8a")
    mb.cylinder((0, 0, 0.04), (0.08, 0, 0.3), 0.014, 0.012, "prop:iron", "#9aa0a8", segments=6)
    mb.sphere((0.085, 0, 0.31), 0.035, "prop:plain", RED, subdivisions=1)


def switch(mb: MeshBuilder, p, rng):
    mb.box((-0.08, -0.03, 0.12), (0.08, 0.03, 0.34), "prop:stone", "#9a9a9a")
    mb.box((-0.035, 0.03, 0.2), (0.035, 0.07, 0.27), "prop:plain", RED)


def pressure_plate(mb: MeshBuilder, p, rng):
    mb.box((-0.2, -0.2, 0), (0.2, 0.2, 0.025), "prop:stone", "#6a6a6a")
    mb.box((-0.16, -0.16, 0.025), (0.16, 0.16, 0.05), "prop:plain", RED)


def dart_trap(mb: MeshBuilder, p, rng):
    mb.box((-0.25, -0.25, 0), (0.25, 0.25, 0.5), "prop:stone", "#7a7a72")
    mb.box((-0.18, 0.25, 0.08), (0.18, 0.26, 0.42), "prop:stone", "#5e5e58")
    mb.cylinder((0, 0.24, 0.25), (0, 0.28, 0.25), 0.05, 0.05, "prop:plain", "#101010", segments=10)


def bottle_light(mb: MeshBuilder, p, rng):
    mb.lathe([(0.0, 0.0), (0.07, 0.0), (0.08, 0.1), (0.05, 0.16), (0.03, 0.2), (0.0, 0.2)], "prop:gel", "#d8f0ff", segments=12)
    mb.cylinder((0, 0, 0.2), (0, 0, 0.24), 0.034, 0.034, "prop:planks", "#8a6a4a", segments=8)
    for k in range(3):
        a = k * 2.1
        mb.sphere((math.cos(a) * 0.03, math.sin(a) * 0.03, 0.06 + k * 0.03), 0.02, "prop:plain:glow", p.get("glow", "#d8ff60"), subdivisions=1)


def minecart(mb: MeshBuilder, p, rng):
    """An open iron tub on four wheels (front faces +Y)."""
    iron, trim = "#6a6e76", "#3a3c40"
    mb.box((-0.17, -0.24, 0.1), (0.17, 0.24, 0.13), "prop:iron", iron)
    for sx in (-1, 1):
        mb.box((sx * 0.17 - 0.015, -0.24, 0.1), (sx * 0.17 + 0.015, 0.24, 0.32), "prop:iron", iron)
    for sy in (-1, 1):
        mb.box((-0.17, sy * 0.24 - 0.015, 0.1), (0.17, sy * 0.24 + 0.015, 0.32), "prop:iron", iron)
    rim = [Vector((x, y, 0.33)) for x, y in ((-0.18, -0.25), (0.18, -0.25), (0.18, 0.25), (-0.18, 0.25), (-0.18, -0.25))]
    mb.tube(rim, [0.015] * len(rim), "prop:iron", trim, segments=4)
    for sx in (-1, 1):
        for sy in (-1, 1):
            mb.cylinder((sx * 0.13, sy * 0.15, 0.06), (sx * 0.19, sy * 0.15, 0.06), 0.06, 0.06, "prop:iron", trim, segments=10)


def red_wrench(mb: MeshBuilder, p, rng):
    mb.box((-0.02, -0.012, -0.12), (0.02, 0.012, 0.22), "prop:plain", RED)
    mb.box((-0.06, -0.015, 0.22), (0.06, 0.015, 0.28), "prop:iron", "#a0a4ac")
    for sx in (-1, 1):
        mb.box((sx * 0.06 - 0.018, -0.015, 0.28), (sx * 0.06 + 0.018, 0.015, 0.34), "prop:iron", "#a0a4ac")


def wire_cutter(mb: MeshBuilder, p, rng):
    for sx in (-1, 1):
        mb.tube([Vector((sx * 0.05, 0, -0.12)), Vector((sx * 0.03, 0, 0.1)), Vector((0, 0, 0.16))], [0.018, 0.015, 0.012], "prop:plain", "#e0c020", segments=5)
        mb.tube([Vector((0, 0, 0.16)), Vector((sx * 0.02, 0, 0.25)), Vector((0, 0, 0.3))], [0.014, 0.012, 0.006], "prop:iron", "#a0a4ac", segments=4)


def wire_coil(mb: MeshBuilder, p, rng):
    pts = [Vector((math.cos(a) * 0.12, math.sin(a) * 0.12, 0.02 + a * 0.012)) for a in (i * 0.35 for i in range(60))]
    mb.tube(pts, [0.012] * len(pts), "prop:plain", RED, segments=4)


def bug_net(mb: MeshBuilder, p, rng):
    mb.cylinder((0, 0, -0.3), (0, 0, 0.55), 0.014, 0.012, "prop:planks", "#8a6a4a", segments=6)
    ring = [Vector((math.cos(a) * 0.14, math.sin(a) * 0.02, 0.69 + math.sin(a) * 0.14)) for a in (i * 2 * math.pi / 20 for i in range(21))]
    mb.tube(ring, [0.008] * len(ring), "prop:iron", "#a0a4ac", segments=4)
    mb.sphere((0, -0.08, 0.66), 0.12, "prop:gel", "#f0f0f0", subdivisions=2, scale=(1.1, 0.8, 1.1))


GENERATORS = {"lever": lever, "switch": switch, "pressure_plate": pressure_plate, "dart_trap": dart_trap,
              "bottle_light": bottle_light, "minecart": minecart, "red_wrench": red_wrench, "wire_cutter": wire_cutter, "wire_coil": wire_coil,
              "bug_net": bug_net}

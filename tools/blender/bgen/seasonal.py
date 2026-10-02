"""Seasonal events (X-06): Halloween and Christmas vanity hats, pumpkins and Jack 'O Lanterns, a Christmas tree,
festive lights and candy. Objects follow furniture.py (origin at the footprint's floor centre, front faces Blender +Y,
cells of 0.5 m); items follow items.py."""
import math

from mathutils import Vector

from . import generator

ORANGE = "#e8801a"


@generator
def seasonal_hat(mb, p, rng):
    """Vanity headwear: a witch's hat, a Santa hat, reindeer antlers or a carved pumpkin mask."""
    style = p.get("style", "witch")
    if style == "witch":
        mb.cylinder((0, 0, 0.0), (0, 0, 0.02), 0.2, 0.2, "prop:cloth", "#3a2a4a", segments=20)
        mb.tube([Vector((0, 0, 0.02)), Vector((0, 0.01, 0.18)), Vector((0, -0.04, 0.3)), Vector((0, -0.1, 0.34))],
                [0.1, 0.06, 0.025, 0.006], "prop:cloth", "#3a2a4a", segments=12)
        mb.cylinder((0, 0, 0.02), (0, 0, 0.05), 0.1, 0.092, "prop:cloth", "#8a3ad0", segments=16)
    elif style == "santa":
        mb.cylinder((0, 0, 0.0), (0, 0, 0.05), 0.13, 0.13, "prop:cloth", "#f4f4f4", segments=18)
        mb.tube([Vector((0, 0, 0.05)), Vector((0, 0.02, 0.18)), Vector((0, 0.1, 0.24)), Vector((0, 0.16, 0.16))],
                [0.12, 0.08, 0.04, 0.015], "prop:cloth", "#c81a1a", segments=12)
        mb.sphere((0, 0.17, 0.14), 0.035, "prop:cloth", "#f4f4f4", subdivisions=2)
    elif style == "antlers":
        mb.tube([Vector((-0.12, 0, 0.0)), Vector((0, 0, 0.06)), Vector((0.12, 0, 0.0))], [0.012] * 3, "prop:cloth", "#6a3a1a", segments=6)
        for sx in (-1, 1):
            mb.tube([Vector((sx * 0.08, 0, 0.04)), Vector((sx * 0.12, 0, 0.16)), Vector((sx * 0.1, 0.02, 0.26))], [0.018, 0.014, 0.008], "prop:bark", "#8a6a3a", segments=6)
            mb.tube([Vector((sx * 0.115, 0, 0.13)), Vector((sx * 0.18, 0.02, 0.2))], [0.012, 0.006], "prop:bark", "#8a6a3a", segments=5)
    else:  # pumpkin mask
        _pumpkin(mb, 0.15, carved=True)


def _pumpkin(mb, r, carved=False, glow=False):
    for k in range(8):
        a = k * math.pi / 4
        mb.sphere((math.cos(a) * r * 0.35, math.sin(a) * r * 0.35, r * 0.85), r * 0.62, "prop:gel", ORANGE, subdivisions=2, scale=(1, 1, 0.85))
    mb.cylinder((0, 0, r * 1.5), (0.01, 0, r * 1.85), r * 0.1, r * 0.07, "prop:bark", "#4a6a2a", segments=6)
    if carved:
        face = "#ffd040" if glow else "#2a1a0a"
        layer = "prop:crystal:glow" if glow else "prop:plain"
        for sx in (-1, 1):
            mb.extrude_polygon([(sx * r * 0.18, r * 1.0), (sx * r * 0.42, r * 1.0), (sx * r * 0.3, r * 1.25)], 0.01, layer, face,
                               matrix=_front(r))
        mb.extrude_polygon([(-r * 0.4, r * 0.6), (r * 0.4, r * 0.6), (r * 0.2, r * 0.45), (-r * 0.2, r * 0.45)], 0.01, layer, face, matrix=_front(r))


def _front(r):
    from mathutils import Matrix
    return Matrix.Translation((0, r * 0.96, 0)) @ Matrix.Rotation(math.pi / 2, 4, "X")


@generator
def pumpkin(mb, p, rng):
    """A pumpkin; carved and lit inside for a Jack 'O Lantern."""
    _pumpkin(mb, p.get("radius", 0.2), carved=p.get("carved", False), glow=p.get("carved", False))


@generator
def christmas_tree(mb, p, rng):
    """A decorated fir: tiers of needles, baubles, a star on top."""
    mb.cylinder((0, 0, 0), (0, 0, 0.3), 0.07, 0.06, "prop:bark", "#5a3a1a", segments=8)
    for k, (z, r) in enumerate([(0.3, 0.48), (0.8, 0.4), (1.3, 0.3), (1.75, 0.2)]):
        mb.cylinder((0, 0, z), (0, 0, z + 0.65), r, 0.02, "prop:leaves", "#2a6a3a", segments=12)
    colors = ["#e83a3a", "#3a8ae8", "#e8c03a", "#c83ae8"]
    for k in range(18):
        a = k * 2.4
        z = 0.4 + (k % 9) * 0.17
        r = 0.46 - z * 0.2
        mb.sphere((math.cos(a) * r, math.sin(a) * r, z), 0.035, "prop:crystal:glow", colors[k % 4], subdivisions=1)
    mb.sphere((0, 0, 2.42), 0.07, "prop:crystal:glow", "#ffe060", subdivisions=1, scale=(1, 0.4, 1))


@generator
def festive_lights(mb, p, rng):
    """A sagging string of coloured bulbs along a wall."""
    pts = [Vector((-0.72 + i * 0.12, 0.0, 0.38 - 0.08 * math.sin(i / 12 * math.pi))) for i in range(13)]
    mb.tube(pts, [0.008] * len(pts), "prop:plain", "#2a3a2a", segments=4)
    colors = ["#ff3a3a", "#3aff6a", "#3a8aff", "#ffd83a", "#ff6aff"]
    for i, q in enumerate(pts[1:-1:2]):
        mb.sphere((q.x, q.y + 0.01, q.z - 0.04), 0.03, "prop:crystal:glow", colors[i % len(colors)], subdivisions=1, scale=(1, 1, 1.4))


@generator
def candy(mb, p, rng):
    """Candy corn (a tricolour wedge) or a striped candy cane."""
    if p.get("style") == "cane":
        pts = [Vector((0, 0, z)) for z in (0.0, 0.1, 0.2)] + [Vector((math.sin(a) * 0.05 - 0.05 + 0.05, 0, 0.2 + math.cos(a) * 0.05)) for a in (0.6, 1.4, 2.2, 3.0)]
        mb.tube(pts, [0.018] * len(pts), "prop:plain", "#f4f4f4", segments=8)
        for z in (0.02, 0.08, 0.14):
            mb.cylinder((0, 0, z), (0, 0, z + 0.025), 0.019, 0.019, "prop:plain", "#d81a1a", segments=8)
    else:
        for z0, z1, r0, r1, c in [(0, 0.05, 0.06, 0.045, "#f0c030"), (0.05, 0.1, 0.045, 0.028, ORANGE), (0.1, 0.14, 0.028, 0.0, "#f4f4f4")]:
            mb.cylinder((0, 0, z0), (0, 0, z1), r0, r1, "prop:gel", c, segments=10)

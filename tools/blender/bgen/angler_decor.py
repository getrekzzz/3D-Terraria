"""The Angler's fishing decor (P5-23): lifebuoy, ship's wheel, wall anchor, treasure map, fish plaques (wall
hangings, back on the footprint's -Y face), a pile of seashells and a seaweed planter (floor).

Footprints come from tile-object data (``p['size']`` = [width, height, depth] in metres; origin at the centre of the
bottom face, front facing +Y, Z up).
lifebuoy(p): ring red/white bands.   ships_wheel(p): wood  hub/spoke tint.   wall_anchor(p): metal tint.
treasure_map(p): parchment, ink.     fish_plaque(p): fish body tint, belly, board tint.
seashells(p): shell tints.           seaweed_planter(p): pot tint, weed tint.
"""
import math

from mathutils import Matrix

from . import generator
from .mesh import MeshBuilder


def _ring(mb, cx, cz, y, radius, thick, material, colors, segments=16):
    """A torus-like ring in the XZ plane made of short cylinders, alternating ``colors``."""
    for i in range(segments):
        a0, a1 = 2 * math.pi * i / segments, 2 * math.pi * (i + 1) / segments
        p0 = (cx + radius * math.cos(a0), y, cz + radius * math.sin(a0))
        p1 = (cx + radius * math.cos(a1), y, cz + radius * math.sin(a1))
        mb.cylinder(p0, p1, thick, None, material, colors[(i * len(colors)) // segments], segments=8)


def _back(p):
    w, h, d = p["size"]
    return w, h, -d / 2 + 0.02


@generator
def lifebuoy(mb: MeshBuilder, p, rng):
    """A red and white life ring with a rope loop."""
    w, h, y = _back(p)
    r = min(w, h) * 0.36
    _ring(mb, 0, h / 2, y + 0.08, r, r * 0.28, "prop:plain", [p.get("red", "#d03028"), p.get("white", "#f0ece4")] * 2)
    _ring(mb, 0, h / 2, y + 0.04, r * 1.32, 0.012, "prop:cloth", [p.get("rope", "#c8a870")], segments=20)


@generator
def ships_wheel(mb: MeshBuilder, p, rng):
    """A wooden helm: rim, hub and eight spokes with handles."""
    w, h, y = _back(p)
    wood = p.get("wood", "#8a5a30")
    r = min(w, h) * 0.32
    cz = h / 2
    _ring(mb, 0, cz, y + 0.06, r, 0.035, "prop:planks", [wood], segments=20)
    mb.cylinder((0, y, cz), (0, y + 0.14, cz), r * 0.18, None, "prop:planks", p.get("hub", "#5a3a1a"), segments=12)
    for k in range(8):
        a = math.pi * k / 4
        c, s = math.cos(a), math.sin(a)
        mb.cylinder((0, y + 0.06, cz), (c * r * 1.3, y + 0.06, cz + s * r * 1.3), 0.02, 0.016, "prop:planks", wood, segments=6)
        mb.sphere((c * r * 1.32, y + 0.06, cz + s * r * 1.32), 0.035, "prop:planks", wood, subdivisions=1)


@generator
def wall_anchor(mb: MeshBuilder, p, rng):
    """An iron anchor: shank, ring, stock and curved arms with flukes."""
    w, h, y = _back(p)
    metal, tint = "prop:iron", p.get("tint", "#4a5058")
    y += 0.06
    top, bottom = h * 0.9, h * 0.15
    mb.cylinder((0, y, bottom), (0, y, top), 0.04, None, metal, tint, segments=8)
    _ring(mb, 0, top + 0.08, y, 0.07, 0.018, metal, [tint], segments=10)
    mb.cylinder((-w * 0.3, y, top - 0.12), (w * 0.3, y, top - 0.12), 0.03, None, metal, tint, segments=8)
    pts = [(math.sin(a) * w * 0.38, bottom + (1 - math.cos(a)) * h * 0.28) for a in [i * math.pi / 8 for i in range(-4, 5)]]
    mb.tube([(x, y, z) for x, z in pts], [0.035] * len(pts), metal, tint, segments=8)
    for sx in (-1, 1):
        x, z = pts[0 if sx < 0 else -1]
        mb.extrude_polygon([(x, z), (x + sx * 0.1, z - 0.02), (x - sx * 0.02, z - 0.14)], 0.04, metal, tint, matrix=Matrix.Translation((0, y, 0)))


@generator
def treasure_map(mb: MeshBuilder, p, rng):
    """A curling parchment with a dotted trail and a red X."""
    w, h, y = _back(p)
    mb.box((-w * 0.42, y, h * 0.12), (w * 0.42, y + 0.02, h * 0.88), "prop:cloth", p.get("parchment", "#e0c890"))
    for sx in (-1, 1):
        mb.cylinder((sx * w * 0.43, y + 0.03, h * 0.1), (sx * w * 0.43, y + 0.03, h * 0.9), 0.035, None, "prop:cloth", p.get("parchment", "#d0b878"))
    ink = p.get("ink", "#5a3a20")
    for i in range(7):
        t = i / 6
        mb.box((-w * 0.3 + t * w * 0.45 - 0.015, y + 0.02, h * (0.3 + 0.35 * math.sin(t * 3)) - 0.015),
               (-w * 0.3 + t * w * 0.45 + 0.015, y + 0.03, h * (0.3 + 0.35 * math.sin(t * 3)) + 0.015), "prop:plain", ink)
    for a in (0.785, -0.785):
        c, s = math.cos(a) * 0.08, math.sin(a) * 0.08
        mb.cylinder((w * 0.22 - c, y + 0.03, h * 0.62 - s), (w * 0.22 + c, y + 0.03, h * 0.62 + s), 0.015, None, "prop:plain", "#c02020", segments=6)


@generator
def fish_plaque(mb: MeshBuilder, p, rng):
    """A mounted fish on a varnished board."""
    w, h, y = _back(p)
    mb.bevel_box((-w * 0.45, y, h * 0.15), (w * 0.45, y + 0.04, h * 0.85), "prop:planks", p.get("board", "#6a4020"), radius=0.02)
    body, belly = p.get("body", "#5a7a4a"), p.get("belly", "#d8d0a0")
    mb.sphere((0, y + 0.1, h / 2), 1.0, "prop:plain", body, subdivisions=2, scale=(w * 0.3, 0.06, h * 0.16))
    mb.sphere((0, y + 0.11, h * 0.46), 1.0, "prop:plain", belly, subdivisions=2, scale=(w * 0.24, 0.05, h * 0.08))
    mb.extrude_polygon([(w * 0.28, h / 2), (w * 0.42, h * 0.66), (w * 0.42, h * 0.34)], 0.03, "prop:plain", body, matrix=Matrix.Translation((0, y + 0.1, 0)))
    mb.sphere((-w * 0.2, y + 0.16, h * 0.54), 0.025, "prop:plain", "#101010", subdivisions=1)


@generator
def seashells(mb: MeshBuilder, p, rng):
    """A heap of sand-coloured shells: cones and fans."""
    w, h, d = p["size"]
    tints = p.get("tints", ["#f0d8c0", "#e8b8a0", "#f8f0e0", "#d8a878"])
    for i in range(9):
        x, y = (rng.random() - 0.5) * w * 0.7, (rng.random() - 0.5) * d * 0.7
        tint = tints[i % len(tints)]
        if i % 2:
            mb.cylinder((x, y, 0), (x + 0.05, y, 0.12), 0.06, 0.0, "prop:plain", tint, segments=8)
        else:
            mb.sphere((x, y, 0.03), 1.0, "prop:plain", tint, subdivisions=1, scale=(0.08, 0.07, 0.04))


@generator
def seaweed_planter(mb: MeshBuilder, p, rng):
    """A clay pot with swaying kelp fronds."""
    w, h, d = p["size"]
    pot, weed = p.get("pot", "#a05a30"), p.get("weed", "#3a7a3a")
    mb.lathe([(0.0, 0.0), (w * 0.3, 0.0), (w * 0.38, h * 0.3), (w * 0.34, h * 0.32), (0.0, h * 0.3)], "prop:plain", pot)
    for k in range(6):
        a = 2 * math.pi * k / 6
        base = (math.cos(a) * w * 0.12, math.sin(a) * d * 0.12, h * 0.28)
        pts = [(base[0] + math.sin(t * 2 + k) * 0.06, base[1], base[2] + t * h * 0.65) for t in (0, 0.33, 0.66, 1.0)]
        mb.tube(pts, [0.03, 0.025, 0.02, 0.008], "prop:jungle_leaves", weed, segments=5)

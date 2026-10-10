"""Wall banners, statues, shelf books and spike strips (Dungeon decor, reusable for any hall or shrine).

Footprints come from tile-object data (``p['size']`` = [width, height, depth] in metres; origin at the centre of the
bottom face, front facing Blender +Y, Z up). Wall hangings put their back on the footprint's -Y face, which the game
turns towards the wall they hang from.

banner(p):  cloth   panel colour            trim    hem/edge band colour      emblem   emblem colour
            shape   'skull' | 'cross' | 'flame' | 'star' | 'shield' | 'diamond' (the emblem's outline)
            hem     'point' (default) | 'swallowtail' | 'flat'    rod  rod tint    cloth_layer  prop layer (cloth)
statue(p):  pose    'skeleton' | 'armor' | 'angel'    stone  prop layer (stone)    tint  stone tint
            base    pedestal tint (default: tint darkened)
book(p):    covers  list of cover tints; variant N of the object takes covers[N % len]   pages  page tint
spike_strip(p): metal  prop layer (iron)    tint  spike tint    plate  plate tint    count  spikes (5)
bone_welder(p): bone  bone tint    slab  work-slab tint    iron  vise tint (a bone-framed anvil with a skull vise)
Helpers: emblem_outline(shape, r) -> [(x, z)] polygon, hanging_cloth(mb, ...) a sagging panel with a hem.
"""
import math

from mathutils import Matrix

from . import generator
from .mesh import MeshBuilder

_EMBLEMS = {
    "skull": [(-0.8, 0.3), (-0.9, 0.8), (-0.5, 1.1), (0.5, 1.1), (0.9, 0.8), (0.8, 0.3), (0.45, 0.1), (0.4, -0.3), (0.15, -0.15),
              (0.0, -0.4), (-0.15, -0.15), (-0.4, -0.3), (-0.45, 0.1)],
    "cross": [(-0.25, -1.0), (0.25, -1.0), (0.25, 0.3), (0.8, 0.3), (0.8, 0.75), (0.25, 0.75), (0.25, 1.1), (-0.25, 1.1),
              (-0.25, 0.75), (-0.8, 0.75), (-0.8, 0.3), (-0.25, 0.3)],
    "flame": [(0.0, -0.9), (0.7, -0.5), (0.8, 0.1), (0.45, 0.5), (0.5, 1.0), (0.15, 0.6), (0.0, 1.2), (-0.2, 0.55), (-0.55, 0.85),
              (-0.45, 0.3), (-0.8, 0.0), (-0.65, -0.5)],
    "shield": [(-0.8, 1.0), (0.8, 1.0), (0.8, 0.0), (0.5, -0.6), (0.0, -1.0), (-0.5, -0.6), (-0.8, 0.0)],
    "diamond": [(0.0, -1.0), (0.7, 0.0), (0.0, 1.0), (-0.7, 0.0)],
}


def emblem_outline(shape, r):
    """Outline of a named emblem scaled to radius ``r`` as (x, z) points; 'star' is a five-pointed star."""
    if shape == "star":
        return [((r if k % 2 == 0 else r * 0.42) * math.sin(k * math.pi / 5), (r if k % 2 == 0 else r * 0.42) * math.cos(k * math.pi / 5))
                for k in range(10)]
    return [(x * r, z * r) for x, z in _EMBLEMS[shape]]


def hanging_cloth(mb: MeshBuilder, material, color, x0, x1, top, bottom, y, hem="point", thickness=0.025, sag=0.12):
    """A cloth panel hanging from ``top`` to ``bottom`` between x0..x1 at depth ``y``, its lower edge cut by ``hem``."""
    w = x1 - x0
    if hem == "swallowtail":
        edge = [(x1, bottom), (x0 + w * 0.5, bottom + sag * 2.2), (x0, bottom)]
    elif hem == "flat":
        edge = [(x1, bottom + sag * 0.4), (x0, bottom + sag * 0.4)]
    else:
        edge = [(x1, bottom + sag), (x0 + w * 0.5, bottom), (x0, bottom + sag)]
    poly = [(x0, top), (x1, top)] + edge
    mb.extrude_polygon(poly, thickness, material, color, matrix=Matrix.Translation((0, y, 0)))


@generator
def banner(mb: MeshBuilder, p, rng):
    """A wall banner: iron rod with finials, a cloth panel with edge bands and a sagging hem, and a painted emblem."""
    w, h, d = p["size"]
    cloth = f"prop:{p.get('cloth_layer', 'cloth')}"
    back = -d / 2 + 0.04
    rod_z = h - 0.08
    mb.cylinder((-w * 0.48, back + 0.03, rod_z), (w * 0.48, back + 0.03, rod_z), 0.025, None, "prop:dark_iron", p.get("rod", "#c0b090"), segments=6)
    for s in (-1, 1):
        mb.sphere((s * w * 0.48, back + 0.03, rod_z), 0.045, "prop:dark_iron", p.get("rod", "#c0b090"), subdivisions=1)
    x0, x1, top, bottom = -w * 0.38, w * 0.38, rod_z, h * 0.06
    hem = p.get("hem", "point")
    hanging_cloth(mb, cloth, p.get("cloth", "#3a4aa0"), x0, x1, top, bottom, back + 0.05, hem)
    trim = p.get("trim", "#d8c070")
    for x in (x0, x1 - 0.05):  # edge bands down both sides
        mb.box((x, back + 0.03, bottom + 0.15), (x + 0.05, back + 0.075, top - 0.02), cloth, trim)
    mb.box((x0, back + 0.03, top - 0.14), (x1, back + 0.075, top - 0.08), cloth, trim)
    outline = emblem_outline(p.get("shape", "skull"), w * 0.2)
    mb.extrude_polygon(outline, 0.02, cloth, p.get("emblem", "#f0ead8"), matrix=Matrix.Translation((0, back + 0.075, h * 0.58)))


def _skeleton(mb, stone, tint, top):
    mb.sphere((0, 0, top - 0.17), 0.15, stone, tint, subdivisions=2, scale=(0.9, 1.0, 1.05))  # skull
    mb.box((-0.07, 0.04, top - 0.33), (0.07, 0.14, top - 0.27), stone, tint)  # jaw
    mb.cylinder((0, -0.02, top * 0.42), (0, -0.02, top - 0.32), 0.035, None, stone, tint, segments=6)  # spine
    for k in range(4):  # ribs
        z = top - 0.42 - k * 0.09
        mb.cylinder((0, 0, z), (0, 0, z + 0.04), 0.17 - k * 0.015, 0.17 - k * 0.015, stone, tint, segments=8, smooth=False, caps=False)
    mb.cylinder((0, 0, top * 0.4), (0, 0, top * 0.46), 0.14, 0.11, stone, tint, segments=8)  # pelvis
    for s in (-1, 1):
        mb.tube([(s * 0.22, 0, top - 0.38), (s * 0.27, 0.04, top * 0.62), (s * 0.24, 0.1, top * 0.48)], [0.035, 0.03, 0.03], stone, tint, segments=5)
        mb.tube([(s * 0.08, 0, top * 0.4), (s * 0.1, 0.03, top * 0.2), (s * 0.1, 0, 0.0)], [0.04, 0.035, 0.035], stone, tint, segments=5)


def _armor(mb, stone, tint, top):
    mb.cylinder((0, 0, top - 0.32), (0, 0, top), 0.15, 0.13, stone, tint, segments=10)  # helm
    mb.box((-0.11, 0.12, top - 0.2), (0.11, 0.15, top - 0.17), stone, "#505050")  # visor slit (darker)
    mb.cylinder((0, 0, top), (0, 0, top + 0.06), 0.03, 0.0, stone, tint, segments=6)
    mb.lathe([(0.17, top * 0.42), (0.24, top * 0.55), (0.25, top - 0.42), (0.13, top - 0.32)], stone, tint, segments=10)  # cuirass
    for s in (-1, 1):
        mb.sphere((s * 0.27, 0, top - 0.42), 0.09, stone, tint, subdivisions=1, scale=(1.1, 1.0, 0.8))  # pauldron
        mb.cylinder((s * 0.29, 0, top - 0.45), (s * 0.3, 0.05, top * 0.5), 0.055, 0.05, stone, tint, segments=6)
        mb.cylinder((s * 0.1, 0, top * 0.42), (s * 0.11, 0, 0.0), 0.075, 0.07, stone, tint, segments=6)  # greave
    mb.extrude_polygon(emblem_outline("shield", 0.2), 0.05, stone, tint, matrix=Matrix.Translation((-0.3, 0.12, top * 0.5)))


def _angel(mb, stone, tint, top):
    mb.cylinder((0, 0, 0.0), (0, 0, top * 0.68), 0.27, 0.13, stone, tint, segments=10)  # robe
    mb.cylinder((0, 0, top * 0.68), (0, 0, top - 0.25), 0.14, 0.1, stone, tint, segments=10)  # chest
    mb.sphere((0, 0.01, top - 0.14), 0.12, stone, tint, subdivisions=2)  # head
    halo = [(0.12 * math.cos(a), 0.12 * math.sin(a), top + 0.02) for a in (2 * math.pi * k / 10 for k in range(11))]
    mb.tube(halo, [0.018] * len(halo), stone, tint, segments=4)
    wing = [(0.0, 0.0), (0.2, 0.25), (0.42, 0.55), (0.5, 0.85), (0.38, 0.7), (0.3, 0.45), (0.2, 0.2)]
    for s in (-1, 1):
        pts = [(s * (0.1 + x * 1.0), z * (top * 0.6)) for x, z in wing]
        if s < 0:
            pts.reverse()
        mb.extrude_polygon(pts, 0.05, stone, tint, matrix=Matrix.Translation((0, -0.12, top * 0.35)))
        mb.tube([(s * 0.13, 0.02, top - 0.3), (s * 0.1, 0.14, top * 0.6), (0.0, 0.16, top * 0.62)], [0.04, 0.035, 0.03], stone, tint, segments=5)


@generator
def statue(mb: MeshBuilder, p, rng):
    """A stone figure on a stepped pedestal; ``pose`` picks skeleton, armor or angel."""
    from .dungeon_furniture import plinth
    w, h, d = p["size"]
    stone, tint = f"prop:{p.get('stone', 'stone')}", p.get("tint", "#b8b4ac")
    base = 0.2
    plinth(mb, {"trim": stone, "trimTint": p.get("base", "#8a8680")}, w * 0.8, d * 0.8, base)
    figure = {"skeleton": _skeleton, "armor": _armor, "angel": _angel}[p.get("pose", "skeleton")]
    top = h - base - 0.08
    vm = len(mb.bm.verts)
    figure(mb, stone, tint, top)
    for v in list(mb.bm.verts)[vm:]:  # figures are built from z = 0: stand them on the pedestal
        v.co.z += base


@generator
def book(mb: MeshBuilder, p, rng):
    """A closed tome lying on its side: cover boards with a rounded spine around a block of pages."""
    covers = p.get("covers", ["#7a2a2a", "#2a4a8a", "#2a6a3a", "#6a4a2a"])
    variant = int(mb.name.rsplit("_v", 1)[1]) if "_v" in mb.name else 0  # build_all names object meshes <id>_v<variant>
    cover = covers[variant % len(covers)]
    yaw = Matrix.Rotation((rng.random() - 0.5) * 0.8, 4, "Z")
    w, l, t = 0.2, 0.28, 0.07
    vm = len(mb.bm.verts)
    mb.box((-w / 2, -l / 2, 0), (w / 2, l / 2, t * 0.18), "prop:leather", cover)
    mb.box((-w / 2, -l / 2, t * 0.82), (w / 2, l / 2, t), "prop:leather", cover)
    mb.box((-w / 2 + 0.01, -l / 2 + 0.01, t * 0.18), (w / 2 - 0.005, l / 2 - 0.01, t * 0.82), "prop:plain", p.get("pages", "#e8dcc0"))
    mb.cylinder((-w / 2, -l / 2, t / 2), (-w / 2, l / 2, t / 2), t / 2, None, "prop:leather", cover, segments=6)
    for k in range(2):  # gilt bands on the spine
        y = -l * 0.25 + k * l * 0.5
        mb.cylinder((-w / 2, y - 0.01, t / 2), (-w / 2, y + 0.01, t / 2), t / 2 + 0.004, None, "prop:leather", p.get("bands", "#c8a040"), segments=6)
    for v in list(mb.bm.verts)[vm:]:  # a little askew on its shelf
        v.co = yaw @ v.co


@generator
def spike_strip(mb: MeshBuilder, p, rng):
    """An iron plate filling the cell's floor with ``count`` (5: corners and centre) sharp cones."""
    w, h, d = p["size"]
    metal, tint = f"prop:{p.get('metal', 'iron')}", p.get("tint", "#9aa0a8")
    mb.box((-w * 0.48, -d * 0.48, 0), (w * 0.48, d * 0.48, 0.04), metal, p.get("plate", "#6a6e76"))
    spots = [(0, 0), (-1, -1), (1, -1), (-1, 1), (1, 1), (0, -1), (0, 1), (-1, 0), (1, 0)][:p.get("count", 5)]
    for sx, sy in spots:
        x, y = sx * w * 0.27, sy * d * 0.27
        tall = h * (0.7 if (sx, sy) == (0, 0) else 0.55)
        mb.cylinder((x, y, 0.04), (x, y, tall), w * 0.1, 0.0, metal, tint, segments=6, smooth=False)


@generator
def bone_welder(mb: MeshBuilder, p, rng):
    """A bone-framed anvil: crossed femur legs under a stone work slab, a skull clamped in an iron vise on top."""
    w, h, d = p["size"]
    bone, slab, iron = p.get("bone", "#e0d8c0"), p.get("slab", "#6a6660"), p.get("iron", "#5a5e66")
    top = h * 0.5
    for sx in (-1, 1):  # two crossed pairs of femurs, knobbed at both ends
        for sy in (-1, 1):
            a, b = (sx * w * 0.38, sy * d * 0.3, 0.0), (-sx * w * 0.12, sy * d * 0.22, top - 0.06)
            mb.cylinder(a, b, 0.045, 0.04, "prop:plain", bone, segments=6)
            for end in (a, b):
                mb.sphere(end, 0.07, "prop:plain", bone, subdivisions=1, scale=(1.0, 1.0, 0.8))
    mb.box((-w * 0.45, -d * 0.4, top - 0.06), (w * 0.45, d * 0.4, top + 0.1), "prop:stone", slab)  # work slab
    for k in range(5):  # rib-bone edging along the front of the slab
        x = -w * 0.36 + k * w * 0.18
        mb.cylinder((x, -d * 0.41, top - 0.04), (x, -d * 0.41, top + 0.08), 0.03, None, "prop:plain", bone, segments=5)
    base = top + 0.1
    mb.box((-w * 0.18, -d * 0.12, base), (-w * 0.12, d * 0.12, base + 0.34), "prop:iron", iron)  # vise jaws
    mb.box((w * 0.12, -d * 0.12, base), (w * 0.18, d * 0.12, base + 0.34), "prop:iron", iron)
    mb.cylinder((-w * 0.3, 0, base + 0.22), (w * 0.3, 0, base + 0.22), 0.025, None, "prop:iron", iron, segments=6)  # screw
    mb.sphere((0, 0, base + 0.2), 0.17, "prop:plain", bone, subdivisions=2, scale=(0.85, 1.0, 1.0))  # the skull
    mb.box((-0.08, -0.12, base + 0.02), (0.08, 0.02, base + 0.09), "prop:plain", bone)  # jaw
    for sx in (-1, 1):
        mb.sphere((sx * 0.06, -0.14, base + 0.22), 0.04, "prop:plain", "#201810", subdivisions=1)  # eye sockets

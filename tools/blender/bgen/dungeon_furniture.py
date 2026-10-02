"""Palette-driven furniture family: door, chair, table, bookcase, candelabra, chandelier, floor lamp and vase.

Built for the Dungeon's blue, green and pink brick sets, but nothing here is tied to brick: every generator reads its
materials from a *palette*, so any furniture family (wood, metal, glass, flesh…) can reuse them by passing other
layers and tints. Footprints come from tile-object data (``p['size']`` = [width, height, depth] in metres; origin at
the centre of the bottom face, front facing Blender +Y, Z up). At most three material slots per model.

Params read by every generator here:
  palette    a name from PALETTES ("blue" | "green" | "pink") or a dict with the keys below (default "blue")
  body       prop texture layer of the main mass (stone_brick, planks, …)       bodyTint   its vertex tint
  trim       layer of mouldings, rims and feet (usually the body layer)         trimTint   its tint
  metal      layer of hinges, chains, candle cups and rings                      metalTint  its tint
  wax        tint of candle wax ("prop:plain")                                   books      list of spine tints (bookcase)
Any key given directly in params overrides the palette's value, e.g. {"palette": "green", "metal": "gold"}.

Reusable helpers (import them from another generator module):
  resolve_palette(p) -> dict          the merged palette for a params dict
  candle_cup(mb, pal, at, r)          a metal cup with a wax candle; returns the flame base (x, y, z)
  plinth(mb, pal, w, d, h)            a stepped base block for case furniture and lamps
Flames are drawn live by the game from the object's "flame" data (its "at" and "also" points); keep those in sync
with the candle tops the generators return (flame_points(p) lists them for the candelabra and chandelier).
"""
import math

from mathutils import Matrix

from . import generator
from .mesh import MeshBuilder

PALETTES = {
    # Terraria's dungeon brick colours: slate blue, moss green and dusty rose, with darker iron to match.
    "blue": {"body": "stone_brick", "bodyTint": "#8a9ce0", "trim": "stone_brick", "trimTint": "#6576b8",
             "metal": "dark_iron", "metalTint": "#a8b4e0", "wax": "#e8e4f2",
             "books": ["#4a5aa0", "#8a3030", "#3a6a4a", "#a08040", "#5a3a70"]},
    "green": {"body": "stone_brick", "bodyTint": "#8ac096", "trim": "stone_brick", "trimTint": "#5f9070",
              "metal": "dark_iron", "metalTint": "#b0d0b4", "wax": "#ecf0e2",
              "books": ["#3a6a4a", "#8a3030", "#4a5aa0", "#a08040", "#6a4a30"]},
    "pink": {"body": "stone_brick", "bodyTint": "#dc9cc4", "trim": "stone_brick", "trimTint": "#b07098",
             "metal": "dark_iron", "metalTint": "#e0b4d0", "wax": "#f4e8ee",
             "books": ["#8a3a6a", "#4a5aa0", "#3a6a4a", "#a08040", "#5a3a70"]},
}
_KEYS = ("body", "bodyTint", "trim", "trimTint", "metal", "metalTint", "wax", "books")


def resolve_palette(p) -> dict:
    """The palette for a params dict: the named (or inline) palette, then any key of _KEYS given in params on top.
    Layers come back as material slots ("prop:<layer>")."""
    base = p.get("palette", "blue")
    pal = dict(PALETTES[base] if isinstance(base, str) else base)
    pal.update({k: p[k] for k in _KEYS if k in p})
    for k in ("body", "trim", "metal"):
        pal[k] = pal[k] if pal[k].startswith("prop:") else f"prop:{pal[k]}"
    return pal


def plinth(mb: MeshBuilder, pal, w, d, h):
    """A two-step base (trim colour) under case furniture, lamps and statues."""
    mb.box((-w / 2, -d / 2, 0), (w / 2, d / 2, h * 0.6), pal["trim"], pal["trimTint"])
    mb.box((-w / 2 + 0.03, -d / 2 + 0.03, h * 0.6), (w / 2 - 0.03, d / 2 - 0.03, h), pal["trim"], pal["trimTint"])


def candle_cup(mb: MeshBuilder, pal, at, r=0.05, wax_h=0.14):
    """A metal drip cup with a wax candle standing at ``at`` (the cup's bottom); returns the flame base."""
    x, y, z = at
    mb.cylinder((x, y, z), (x, y, z + 0.035), r * 0.6, r * 1.3, pal["metal"], pal["metalTint"], segments=8)
    mb.cylinder((x, y, z + 0.035), (x, y, z + 0.035 + wax_h), r * 0.55, None, "prop:plain", pal["wax"], segments=6)
    return (x, y, z + 0.035 + wax_h + 0.01)


def _candelabra_cups(w, h):
    return [(-w * 0.34, 0.0, h * 0.62), (0.0, 0.0, h * 0.74), (w * 0.34, 0.0, h * 0.62)]


def _chandelier_cups(w, h):
    r = w * 0.36
    return [(r * math.cos(a), r * math.sin(a), h * 0.34) for a in (math.pi / 2, math.pi * 7 / 6, math.pi * 11 / 6)]


def flame_points(kind, size):
    """Flame bases in game model space (x, y up, z = -Blender y) for 'candelabra' or 'chandelier' of a footprint."""
    w, h, _ = size
    cups = _candelabra_cups(w, h) if kind == "candelabra" else _chandelier_cups(w, h)
    return [(round(x, 3), round(z + 0.035 + 0.14 + 0.01, 3), round(-y, 3)) for x, y, z in cups]


@generator
def dungeon_door(mb: MeshBuilder, p, rng):
    """A slab door in the body colour: framed leaf with raised panels, iron straps and a ring pull."""
    pal = resolve_palette(p)
    w, h, d = p["size"]
    lw, lh, t = w * 0.92, h * 0.97, min(d * 0.3, 0.12)
    mb.box((-lw / 2, -t / 2, 0.01), (lw / 2, t / 2, lh), pal["body"], pal["bodyTint"])
    for z0, z1 in ((0.12, lh * 0.45), (lh * 0.52, lh - 0.1)):  # raised panels on both faces
        for s in (-1, 1):
            mb.box((-lw * 0.36, s * t / 2 - 0.015, z0), (lw * 0.36, s * t / 2 + 0.015, z1), pal["trim"], pal["trimTint"])
    for z in (lh * 0.2, lh * 0.8):  # straps across the leaf
        mb.box((-lw / 2, -t / 2 - 0.03, z), (lw / 2 - 0.04, t / 2 + 0.03, z + 0.06), pal["metal"], pal["metalTint"])
    for s in (-1, 1):
        mb.cylinder((lw * 0.34, s * (t / 2 + 0.03), lh * 0.5 + 0.06), (lw * 0.34, s * (t / 2 + 0.03), lh * 0.5 - 0.06), 0.045,
                    None, pal["metal"], pal["metalTint"], segments=8)


@generator
def dungeon_chair(mb: MeshBuilder, p, rng):
    """A heavy block chair: solid seat and legs, tall back with a pointed crest."""
    pal = resolve_palette(p)
    w, h, d = p["size"]
    seat, sw, sd = h * 0.36, w * 0.34, d * 0.34
    mb.box((-sw, -sd, seat - 0.06), (sw, sd, seat + 0.04), pal["body"], pal["bodyTint"])
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * (sw - 0.05), sy * (sd - 0.05)
            mb.box((x - 0.05, y - 0.05, 0), (x + 0.05, y + 0.05, seat - 0.06), pal["trim"], pal["trimTint"])
    back_top = h * 0.88
    mb.box((-sw, -sd, seat + 0.04), (sw, -sd + 0.09, back_top), pal["body"], pal["bodyTint"])
    mb.box((-sw * 0.6, -sd - 0.01, seat + 0.18), (sw * 0.6, -sd + 0.1, back_top - 0.12), pal["trim"], pal["trimTint"])
    crest = [(-sw, 0.0), (sw, 0.0), (sw * 0.55, h * 0.06), (0.0, h * 0.11), (-sw * 0.55, h * 0.06)]
    mb.extrude_polygon(crest, 0.09, pal["trim"], pal["trimTint"], matrix=Matrix.Translation((0, -sd + 0.045, back_top)))


@generator
def dungeon_table(mb: MeshBuilder, p, rng):
    """A thick slab table on two carved pedestal ends joined by a stretcher."""
    pal = resolve_palette(p)
    w, h, d = p["size"]
    top = h * 0.8
    mb.box((-w / 2 + 0.02, -d / 2 + 0.02, top), (w / 2 - 0.02, d / 2 - 0.02, top + 0.1), pal["body"], pal["bodyTint"])
    mb.box((-w / 2 + 0.06, -d / 2 + 0.06, top - 0.06), (w / 2 - 0.06, d / 2 - 0.06, top), pal["trim"], pal["trimTint"])
    for sx in (-1, 1):
        x = sx * (w / 2 - 0.28)
        mb.box((x - 0.16, -d * 0.36, 0), (x + 0.16, d * 0.36, 0.1), pal["trim"], pal["trimTint"])  # foot
        mb.box((x - 0.09, -d * 0.24, 0.1), (x + 0.09, d * 0.24, top - 0.06), pal["body"], pal["bodyTint"])  # pedestal
        mb.box((x - 0.14, -d * 0.3, top * 0.45), (x + 0.14, d * 0.3, top * 0.55), pal["trim"], pal["trimTint"])  # collar
    mb.box((-w / 2 + 0.3, -0.05, top * 0.22), (w / 2 - 0.3, 0.05, top * 0.3), pal["body"], pal["bodyTint"])


@generator
def bookcase(mb: MeshBuilder, p, rng):
    """An open case with ``shelves`` (default 3) rows of book spines in the palette's book tints; a few lean over."""
    pal = resolve_palette(p)
    w, h, d = p["size"]
    shelves, wall = p.get("shelves", 3), 0.07
    plinth(mb, pal, w, d * 0.9, 0.12)
    mb.box((-w / 2, -d * 0.45, 0.12), (-w / 2 + wall, d * 0.4, h), pal["body"], pal["bodyTint"])
    mb.box((w / 2 - wall, -d * 0.45, 0.12), (w / 2, d * 0.4, h), pal["body"], pal["bodyTint"])
    mb.box((-w / 2, -d * 0.45, 0.12), (w / 2, -d * 0.38, h), pal["body"], pal["bodyTint"])  # back
    mb.box((-w / 2 - 0.03, -d * 0.47, h - 0.08), (w / 2 + 0.03, d * 0.42, h), pal["trim"], pal["trimTint"])  # cornice
    inner = w - 2 * wall
    gap = (h - 0.2 - 0.08) / shelves
    tints = pal["books"]
    for s in range(shelves):
        z = 0.12 + s * gap
        mb.box((-w / 2 + wall, -d * 0.38, z), (w / 2 - wall, d * 0.38, z + 0.05), pal["trim"], pal["trimTint"])
        x = -inner / 2 + 0.02
        while x < inner / 2 - 0.08:
            bw = 0.05 + rng.random() * 0.05
            bh = gap * (0.55 + rng.random() * 0.3)
            if x + bw > inner / 2 - 0.02:
                break
            tint = tints[rng.randrange(len(tints))]
            lean = rng.random() < 0.12 and x + bw + 0.12 < inner / 2
            m = Matrix.Translation((x, 0, z + 0.05)) @ Matrix.Rotation(-0.35, 4, "Y") if lean else Matrix.Translation((x, 0, z + 0.05))
            mb.box((0, -d * 0.3, 0), (bw, d * 0.3, bh), "prop:leather", tint, matrix=m)
            x += bw + (0.1 if lean else 0.008)


@generator
def candelabra(mb: MeshBuilder, p, rng):
    """A three-branched candlestick on a stepped foot: centre candle raised, two on curved arms."""
    pal = resolve_palette(p)
    w, h, d = p["size"]
    mb.cylinder((0, 0, 0), (0, 0, 0.06), w * 0.2, w * 0.17, pal["trim"], pal["trimTint"], segments=8, smooth=False)
    mb.cylinder((0, 0, 0.06), (0, 0, h * 0.74), 0.03, 0.025, pal["metal"], pal["metalTint"], segments=6)
    mb.sphere((0, 0, h * 0.3), 0.055, pal["metal"], pal["metalTint"], subdivisions=1)
    cups = _candelabra_cups(w, h)
    for x, y, z in (cups[0], cups[2]):
        mb.tube([(0, 0, h * 0.42), (x * 0.6, 0, h * 0.44), (x, 0, h * 0.5), (x, 0, z)], [0.02, 0.02, 0.02, 0.02],
                pal["metal"], pal["metalTint"], segments=5)
    for c in cups:
        candle_cup(mb, pal, c, r=0.045)


@generator
def chandelier(mb: MeshBuilder, p, rng):
    """A hanging iron ring on three chains from a ceiling rose, with a drop finial and three candles on the ring."""
    pal = resolve_palette(p)
    w, h, d = p["size"]
    ring_r, ring_z = w * 0.36, h * 0.34
    mb.cylinder((0, 0, h - 0.06), (0, 0, h), w * 0.12, w * 0.14, pal["trim"], pal["trimTint"], segments=8, smooth=False)  # rose
    # Ring: a 12-sided tube of the metal, closed by overlapping its ends.
    pts = [(ring_r * math.cos(a), ring_r * math.sin(a), ring_z) for a in (2 * math.pi * k / 12 for k in range(13))]
    mb.tube(pts, [0.03] * len(pts), pal["metal"], pal["metalTint"], segments=5)
    mb.cylinder((0, 0, ring_z - 0.02), (0, 0, h - 0.06), 0.025, None, pal["metal"], pal["metalTint"], segments=5)  # stem
    mb.sphere((0, 0, ring_z - 0.06), 0.07, pal["metal"], pal["metalTint"], subdivisions=1, scale=(1, 1, 1.3))  # finial
    mb.cylinder((0, 0, ring_z - 0.13), (0, 0, ring_z - 0.24), 0.04, 0.0, pal["metal"], pal["metalTint"], segments=6)
    for k, (x, y, z) in enumerate(_chandelier_cups(w, h)):
        # Chain of four links from the rose to the ring at each candle.
        top = (x * 0.15, y * 0.15, h - 0.06)
        for i in range(4):
            t0, t1 = i / 4, (i + 0.85) / 4
            a = tuple(top[j] + ((x, y, z)[j] - top[j]) * t0 for j in range(3))
            b = tuple(top[j] + ((x, y, z)[j] - top[j]) * t1 for j in range(3))
            mb.cylinder(a, b, 0.012, None, pal["metal"], pal["metalTint"], segments=4, caps=False, smooth=False)
        candle_cup(mb, pal, (x, y, z), r=0.05)
        mb.cylinder((x * 0.75, y * 0.75, z), (x * 0.25, y * 0.25, z), 0.018, None, pal["metal"], pal["metalTint"], segments=4)  # spoke


@generator
def floor_lamp(mb: MeshBuilder, p, rng):
    """A tall lamp: stepped foot, ringed post and a lantern cage whose wick holds the flame (at 0.78 h + 0.09 m)."""
    pal = resolve_palette(p)
    w, h, d = p["size"]
    plinth(mb, pal, w * 0.6, d * 0.6, 0.1)
    mb.cylinder((0, 0, 0.1), (0, 0, h * 0.78), 0.045, 0.035, pal["body"], pal["bodyTint"], segments=8)
    for z in (h * 0.25, h * 0.55):
        mb.cylinder((0, 0, z), (0, 0, z + 0.05), 0.07, None, pal["trim"], pal["trimTint"], segments=8, smooth=False)
    top = h * 0.78
    mb.cylinder((0, 0, top), (0, 0, top + 0.04), w * 0.24, w * 0.24, pal["metal"], pal["metalTint"], segments=8, smooth=False)
    for k in range(4):
        a = math.pi / 4 + k * math.pi / 2
        x, y = math.cos(a) * w * 0.2, math.sin(a) * w * 0.2
        mb.box((x - 0.015, y - 0.015, top + 0.04), (x + 0.015, y + 0.015, h * 0.97), pal["metal"], pal["metalTint"])
    mb.cylinder((0, 0, h * 0.97), (0, 0, h), w * 0.26, w * 0.08, pal["metal"], pal["metalTint"], segments=8, smooth=False)
    mb.cylinder((0, 0, top + 0.04), (0, 0, top + 0.08), 0.04, None, "prop:plain", pal["wax"], segments=6)


@generator
def vase(mb: MeshBuilder, p, rng):
    """A turned urn in the body colour with a trim band and lip; ``shape`` 'urn' (default) or 'tall'."""
    pal = resolve_palette(p)
    w, h, d = p["size"]
    r = min(w, d) * 0.42
    tall = p.get("shape") == "tall"
    belly = 0.36 if tall else 0.42
    profile = [(r * 0.55, 0.0), (r * 0.7, h * 0.04), (r * 0.9, h * 0.2), (r, h * belly), (r * 0.85, h * 0.62),
               (r * 0.45, h * 0.78), (r * 0.42, h * 0.88), (r * 0.62, h * 0.97), (r * 0.5, h), (0.0, h * 0.97)]
    mb.lathe(profile, pal["body"], pal["bodyTint"], segments=12)
    mb.cylinder((0, 0, h * (belly - 0.03)), (0, 0, h * (belly + 0.03)), r * 1.02, r * 1.02, pal["trim"], pal["trimTint"],
                segments=12, smooth=False)
    mb.cylinder((0, 0, 0), (0, 0, h * 0.04), r * 0.66, r * 0.7, pal["trim"], pal["trimTint"], segments=12, smooth=False)

"""Furniture, crafting stations, containers and lights. Footprint sizes come from tile-object data (cells of 0.5 m);
``p['size']`` is the footprint in metres [x, y(up), z] after conversion by build_all. Front faces Blender +Y.

Bevel pass: every wooden or metal box here is built with ``MeshBuilder.bevel_box`` so no edge is a razor-sharp
unlit line. Two radii keep the look consistent across the family (and with any other furniture module):
  SOFT  (0.018 m, 2 segments)  large faces a hand rests on: table and bench tops, seats, chest bodies, bed frames
  EDGE  (0.010 m, 1 segment)   legs, rails, slats, bands, planks — a chamfer that catches the light
``p['bevel']`` scales both (0 = the old hard boxes) for a variant that wants crisper or softer edges."""
import math

from mathutils import Matrix, Vector

from .mesh import MeshBuilder

SOFT, SOFT_SEGMENTS = 0.018, 2
EDGE = 0.010


def bevels(p):
    """(soft, edge) radii for a params dict: SOFT and EDGE scaled by ``p['bevel']`` (default 1)."""
    k = p.get("bevel", 1.0)
    return SOFT * k, EDGE * k


def _legs(mb, w, d, h, t, material, inset=0.06, color="#ffffff", bevel=EDGE):
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * (w / 2 - inset - t / 2), sy * (d / 2 - inset - t / 2)
            mb.bevel_box((x - t / 2, y - t / 2, 0), (x + t / 2, y + t / 2, h), material, color, radius=bevel)


def table(mb: MeshBuilder, p, rng):
    w, h, d = p["size"]
    wood = f"prop:{p.get('wood', 'planks')}"
    soft, edge = bevels(p)
    top = h * 0.82
    mb.bevel_box((-w / 2 + 0.02, -d / 2 + 0.02, top), (w / 2 - 0.02, d / 2 - 0.02, top + 0.08), wood, radius=soft, segments=SOFT_SEGMENTS)
    _legs(mb, w, d, top, 0.09, wood, bevel=edge)
    mb.bevel_box((-w / 2 + 0.1, -0.03, top * 0.3), (w / 2 - 0.1, 0.03, top * 0.36), wood, radius=edge)


def workbench(mb: MeshBuilder, p, rng):
    w, h, d = p["size"]
    soft, edge = bevels(p)
    table(mb, {"size": (w, h * 0.95, d), "bevel": p.get("bevel", 1.0)}, rng)
    top = h * 0.95 * 0.82 + 0.08
    mb.bevel_box((w / 2 - 0.35, -d / 2 + 0.05, top), (w / 2 - 0.1, -d / 2 + 0.25, top + 0.18), "prop:iron", "#9aa0a8", radius=edge)
    mb.cylinder((w / 2 - 0.22, -d / 2 + 0.25, top + 0.09), (w / 2 - 0.22, -d / 2 + 0.45, top + 0.09), 0.02, None, "prop:iron", segments=6)
    mb.bevel_box((-w / 2 + 0.15, -0.1, top), (-w / 2 + 0.55, 0.15, top + 0.05), "prop:dark_planks", radius=edge)  # a plank offcut
    mb.cylinder((-0.2, 0.15, top + 0.02), (0.15, 0.2, top + 0.02), 0.02, None, "prop:iron", "#707070", segments=6)  # saw back


def furnace(mb: MeshBuilder, p, rng):
    """Stone kiln with a glowing mouth. Forges recolour the stone, fire and trim and add metal bands (fractions of
    the height in ``bands``)."""
    w, h, d = p["size"]
    stone, tint = f"prop:{p.get('stone', 'stone_brick')}", p.get("stoneTint", "#d0c8c0")
    trim, trim_tint = f"prop:{p.get('trim', 'dark_iron')}", p.get("trimTint", "#ffffff")
    profile = [(w * 0.48, 0.0), (w * 0.46, h * 0.45), (w * 0.3, h * 0.8), (w * 0.14, h * 0.85)]
    mb.lathe(profile, stone, tint, segments=8, smooth=False)
    mb.cylinder((0, 0, h * 0.8), (0, 0, h), w * 0.12, w * 0.1, stone, p.get("chimneyTint", "#b0a8a0"), segments=8, smooth=False)
    mb.box((-w * 0.2, d * 0.28, 0.08), (w * 0.2, d * 0.46, h * 0.32), "prop:plain:glow", p.get("glow", "#ff8a2a"))
    mb.bevel_box((-w * 0.26, d * 0.38, h * 0.32), (w * 0.26, d * 0.48, h * 0.38), trim, trim_tint, radius=EDGE)
    for f in p.get("bands", []):
        z = h * f
        (r0, z0), (r1, z1) = next((a, b) for a, b in zip(profile, profile[1:]) if a[1] <= z <= b[1])
        r = r0 + (r1 - r0) * (z - z0) / (z1 - z0) + 0.015
        mb.cylinder((0, 0, z - 0.03), (0, 0, z + 0.03), r, r, trim, trim_tint, segments=16)


def anvil(mb: MeshBuilder, p, rng):
    w, h, d = p["size"]
    metal = f"prop:{p.get('metal', 'iron')}"
    mb.cylinder((0, 0, 0), (0, 0, h * 0.45), d * 0.3, d * 0.26, "prop:bark", segments=10)  # stump
    profile = [(-w * 0.28, h * 0.45), (w * 0.28, h * 0.45), (w * 0.2, h * 0.6), (w * 0.22, h * 0.72), (w * 0.34, h * 0.8),
               (w * 0.46, h * 0.86), (w * 0.3, h * 0.92), (-w * 0.3, h * 0.92), (-w * 0.36, h * 0.8), (-w * 0.2, h * 0.72), (-w * 0.2, h * 0.6)]
    mb.extrude_polygon(profile, d * 0.36, metal, p.get("tint", "#ffffff"), matrix=Matrix.Rotation(math.pi / 2, 4, "Z"))
    if "glow" in p:  # hardmode anvils: a vein of their ore glowing along the waist
        mb.box((-d * 0.19, -w * 0.21, h * 0.62), (d * 0.19, w * 0.21, h * 0.68), "prop:plain:glow", p["glow"])


def sawmill(mb: MeshBuilder, p, rng):
    w, h, d = p["size"]
    edge = bevels(p)[1]
    table(mb, {"size": (w, h * 0.55, d), "wood": "planks", "bevel": p.get("bevel", 1.0)}, rng)
    top = h * 0.55 * 0.82 + 0.08
    mb.cylinder((0.1, -0.04, top + 0.35), (0.1, 0.04, top + 0.35), 0.38, None, "prop:iron", "#c0c4cc", segments=20)
    mb.bevel_box((-0.05, -0.12, top), (0.25, 0.12, top + 0.2), "prop:dark_iron", radius=edge)
    for k in range(3):
        y = -d / 2 + 0.15 + k * 0.12
        mb.cylinder((-w / 2 + 0.1, y, top + 0.08), (-w / 2 + 0.8, y, top + 0.08), 0.06, None, "prop:bark", segments=8)


def loom(mb: MeshBuilder, p, rng):
    w, h, d = p["size"]
    wood = "prop:planks"
    edge = bevels(p)[1]
    for sx in (-1, 1):
        mb.bevel_box((sx * w * 0.42 - 0.05, -d * 0.35, 0), (sx * w * 0.42 + 0.05, -d * 0.25, h * 0.9), wood, radius=edge)
        mb.bevel_box((sx * w * 0.42 - 0.05, d * 0.25, 0), (sx * w * 0.42 + 0.05, d * 0.35, h * 0.55), wood, radius=edge)
        mb.bevel_box((sx * w * 0.42 - 0.05, -d * 0.35, 0.1), (sx * w * 0.42 + 0.05, d * 0.35, 0.18), wood, radius=edge)
    mb.cylinder((-w * 0.42, -d * 0.3, h * 0.88), (w * 0.42, -d * 0.3, h * 0.88), 0.05, None, wood, segments=8)
    mb.cylinder((-w * 0.42, d * 0.3, h * 0.5), (w * 0.42, d * 0.3, h * 0.5), 0.07, None, "prop:red_cloth", segments=10)
    mb.box((-w * 0.36, -d * 0.3, h * 0.5), (w * 0.36, d * 0.3, h * 0.52), "prop:red_cloth", matrix=Matrix.Rotation(-0.55, 4, "X") @ Matrix.Translation((0, -0.2, 0.25)))


def bottle(mb: MeshBuilder, p, rng):
    s = p["size"][1]
    mb.lathe([(0.0, 0.0), (s * 0.3, 0.0), (s * 0.34, s * 0.3), (s * 0.12, s * 0.62), (s * 0.1, s * 0.8), (s * 0.13, s * 0.84), (0.0, s * 0.84)],
             "prop:gel", p.get("tint", "#d8f0ff"), segments=12)
    mb.cylinder((0, 0, s * 0.8), (0, 0, s * 0.92), s * 0.08, None, "prop:bark", segments=8)


def chair(mb: MeshBuilder, p, rng):
    w, h, d = p["size"]
    wood = "prop:planks"
    soft, edge = bevels(p)
    seat = h * 0.36
    mb.bevel_box((-w * 0.32, -d * 0.32, seat), (w * 0.32, d * 0.32, seat + 0.06), wood, radius=soft, segments=SOFT_SEGMENTS)
    for sx in (-1, 1):
        for sy in (-1, 1):
            top = h * 0.95 if sy < 0 else seat
            x, y = sx * w * 0.27, sy * d * 0.27
            mb.bevel_box((x - 0.04, y - 0.04, 0), (x + 0.04, y + 0.04, top), wood, radius=edge)
    for k in range(3):
        z = seat + 0.15 + k * (h * 0.95 - seat - 0.2) / 2
        mb.bevel_box((-w * 0.27, -d * 0.3, z), (w * 0.27, -d * 0.24, z + 0.07), wood, radius=edge)


def door(mb: MeshBuilder, p, rng):
    w, h, d = p["size"]
    wood = "prop:planks"
    edge = bevels(p)[1]
    leaf_w, leaf_h, t = w * 0.9, h * 0.97, 0.08
    for k in range(4):  # planks with chamfered edges, so the joints between them read as grooves
        x0 = -leaf_w / 2 + k * leaf_w / 4
        mb.bevel_box((x0 + 0.005, -t / 2, 0.02), (x0 + leaf_w / 4 - 0.005, t / 2, leaf_h), wood, radius=edge)
    for z in (0.3, leaf_h - 0.35):
        mb.bevel_box((-leaf_w / 2, -t / 2 - 0.02, z), (leaf_w / 2, t / 2 + 0.02, z + 0.1), "prop:dark_planks", radius=edge)
    mb.bevel_box((-leaf_w / 2 + 0.05, -t / 2 - 0.03, leaf_h * 0.4), (leaf_w / 2 - 0.05, t / 2 + 0.03, leaf_h * 0.44), "prop:dark_iron", radius=edge * 0.6)
    mb.sphere((leaf_w / 2 - 0.12, t / 2 + 0.05, leaf_h * 0.5), 0.04, "prop:iron", subdivisions=1)
    mb.sphere((leaf_w / 2 - 0.12, -t / 2 - 0.05, leaf_h * 0.5), 0.04, "prop:iron", subdivisions=1)


def chest(mb: MeshBuilder, p, rng):
    # Optional tints (woodTint, bandTint, latchTint) colour the shared layers for locked and biome chests.
    w, h, d = p["size"]
    wood, band = f"prop:{p.get('wood', 'planks')}", f"prop:{p.get('band', 'dark_iron')}"
    wt, bt, lt = p.get("woodTint", "#ffffff"), p.get("bandTint", "#ffffff"), p.get("latchTint", "#ffffff")
    soft, edge = bevels(p)
    body_h = h * 0.58
    mb.bevel_box((-w * 0.46, -d * 0.4, 0), (w * 0.46, d * 0.4, body_h), wood, wt, radius=soft, segments=SOFT_SEGMENTS)
    lid = [(-d * 0.4, 0), (d * 0.4, 0), (d * 0.38, h * 0.18), (d * 0.25, h * 0.3), (0, h * 0.36), (-d * 0.25, h * 0.3), (-d * 0.38, h * 0.18)]
    # The lid is its own node hinged at the back top edge of the body (the game swings it open).
    mb.part("lid", (0, -d * 0.4, body_h)).extrude_polygon(lid, w * 0.92, wood, wt, matrix=Matrix.Translation((0, 0, body_h)) @ Matrix.Rotation(math.pi / 2, 4, "Z"), bevel=edge)
    for x in (-w * 0.3, w * 0.3):
        mb.bevel_box((x - 0.04, -d * 0.42, 0), (x + 0.04, d * 0.42, body_h + 0.02), band, bt, radius=edge)
    mb.bevel_box((-0.07, d * 0.4, body_h - 0.14), (0.07, d * 0.44, body_h + 0.04), p.get("latch", "prop:gold"), lt, radius=edge)


def torch(mb: MeshBuilder, p, rng):
    # Stick, cloth wrap and a charred head with a glowing ember core; the flame itself is drawn live by the game
    # (shaders/flame.gdshader, placed by the object's "flame" data).
    mb.cylinder((0, 0, 0), (0, 0, 0.32), 0.025, 0.03, "prop:bark", segments=6)
    mb.cylinder((0, 0, 0.3), (0, 0, 0.36), 0.04, 0.04, "prop:cloth", "#5a4030", segments=6)
    mb.sphere((0, 0, 0.365), 0.038, "prop:plain", "#2a1c14", subdivisions=1, scale=(1, 1, 0.7))
    mb.sphere((0, 0, 0.372), 0.024, "prop:plain:glow", "#ff6a20", subdivisions=1, scale=(1, 1, 0.8))


def candle(mb: MeshBuilder, p, rng):
    # Holder, wax and a blackened wick; the flame is drawn live by the game.
    mb.cylinder((0, 0, 0), (0, 0, 0.02), 0.09, 0.1, "prop:iron:metal", "#e8804a", segments=10)
    mb.cylinder((0, 0, 0.02), (0, 0, 0.2), 0.035, None, "prop:plain", "#f0ead8", segments=8)
    mb.cylinder((0, 0, 0.2), (0, 0, 0.222), 0.004, 0.003, "prop:plain", "#1a1410", segments=4)


def bed(mb: MeshBuilder, p, rng):
    w, h, d = p["size"]
    wood = "prop:planks"
    soft, edge = bevels(p)
    mb.bevel_box((-w * 0.46, -d * 0.48, 0.15), (w * 0.46, d * 0.48, 0.32), wood, radius=soft, segments=SOFT_SEGMENTS)
    _legs(mb, w * 0.92, d * 0.96, 0.18, 0.1, wood, inset=0.0, bevel=edge)
    mb.bevel_box((-w * 0.46, -d * 0.48, 0.15), (w * 0.46, -d * 0.43, h * 0.9), wood, radius=edge)  # headboard at the back
    mb.bevel_box((-w * 0.46, d * 0.43, 0.15), (w * 0.46, d * 0.48, h * 0.55), wood, radius=edge)
    # Mattress and blanket: well rounded, as stuffed cloth is.
    mb.bevel_box((-w * 0.42, -d * 0.42, 0.32), (w * 0.42, d * 0.42, 0.45), "prop:cloth", radius=0.05, segments=2)
    mb.bevel_box((-w * 0.43, -d * 0.1, 0.33), (w * 0.43, d * 0.43, 0.48), "prop:red_cloth", radius=0.04, segments=2)
    mb.sphere((0, -d * 0.3, 0.5), 0.2, "prop:cloth", subdivisions=1, scale=(1.6, 0.9, 0.45))


def rail(mb: MeshBuilder, p, rng):
    for y in (-0.16, 0.0, 0.16):
        mb.box((-0.22, y - 0.04, 0), (0.22, y + 0.04, 0.05), "prop:dark_planks")
    for x in (-0.13, 0.13):
        mb.box((x - 0.02, -0.25, 0.05), (x + 0.02, 0.25, 0.1), "prop:iron", "#a0a4ac")


GENERATORS = {
    "table": table, "workbench": workbench, "furnace": furnace, "anvil": anvil, "sawmill": sawmill, "loom": loom,
    "bottle": bottle, "chair": chair, "door": door, "chest": chest, "torch": torch, "candle": candle, "bed": bed,
    "rail": rail,
}

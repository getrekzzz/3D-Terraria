"""Furniture, crafting stations, containers and lights. Footprint sizes come from tile-object data (cells of 0.5 m);
``p['size']`` is the footprint in metres [x, y(up), z] after conversion by build_all. Front faces Blender +Y."""
import math

from mathutils import Matrix, Vector

from .mesh import MeshBuilder


def _legs(mb, w, d, h, t, material, inset=0.06, color="#ffffff"):
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * (w / 2 - inset - t / 2), sy * (d / 2 - inset - t / 2)
            mb.box((x - t / 2, y - t / 2, 0), (x + t / 2, y + t / 2, h), material, color)


def table(mb: MeshBuilder, p, rng):
    w, h, d = p["size"]
    wood = f"prop:{p.get('wood', 'planks')}"
    top = h * 0.82
    mb.box((-w / 2 + 0.02, -d / 2 + 0.02, top), (w / 2 - 0.02, d / 2 - 0.02, top + 0.08), wood)
    _legs(mb, w, d, top, 0.09, wood)
    mb.box((-w / 2 + 0.1, -0.03, top * 0.3), (w / 2 - 0.1, 0.03, top * 0.36), wood)


def workbench(mb: MeshBuilder, p, rng):
    w, h, d = p["size"]
    table(mb, {"size": (w, h * 0.95, d)}, rng)
    top = h * 0.95 * 0.82 + 0.08
    mb.box((w / 2 - 0.35, -d / 2 + 0.05, top), (w / 2 - 0.1, -d / 2 + 0.25, top + 0.18), "prop:iron", "#9aa0a8")
    mb.cylinder((w / 2 - 0.22, -d / 2 + 0.25, top + 0.09), (w / 2 - 0.22, -d / 2 + 0.45, top + 0.09), 0.02, None, "prop:iron", segments=6)
    mb.box((-w / 2 + 0.15, -0.1, top), (-w / 2 + 0.55, 0.15, top + 0.05), "prop:dark_planks")  # a plank offcut
    mb.cylinder((-0.2, 0.15, top + 0.02), (0.15, 0.2, top + 0.02), 0.02, None, "prop:iron", "#707070", segments=6)  # saw back


def furnace(mb: MeshBuilder, p, rng):
    w, h, d = p["size"]
    mb.lathe([(w * 0.48, 0.0), (w * 0.46, h * 0.45), (w * 0.3, h * 0.8), (w * 0.14, h * 0.85)], "prop:stone_brick", "#d0c8c0", segments=8, smooth=False)
    mb.cylinder((0, 0, h * 0.8), (0, 0, h), w * 0.12, w * 0.1, "prop:stone_brick", "#b0a8a0", segments=8, smooth=False)
    mb.box((-w * 0.2, d * 0.28, 0.08), (w * 0.2, d * 0.46, h * 0.32), "prop:plain:glow", "#ff8a2a")
    mb.box((-w * 0.26, d * 0.38, h * 0.32), (w * 0.26, d * 0.48, h * 0.38), "prop:dark_iron")


def anvil(mb: MeshBuilder, p, rng):
    w, h, d = p["size"]
    metal = f"prop:{p.get('metal', 'iron')}"
    mb.cylinder((0, 0, 0), (0, 0, h * 0.45), d * 0.3, d * 0.26, "prop:bark", segments=10)  # stump
    profile = [(-w * 0.28, h * 0.45), (w * 0.28, h * 0.45), (w * 0.2, h * 0.6), (w * 0.22, h * 0.72), (w * 0.34, h * 0.8),
               (w * 0.46, h * 0.86), (w * 0.3, h * 0.92), (-w * 0.3, h * 0.92), (-w * 0.36, h * 0.8), (-w * 0.2, h * 0.72), (-w * 0.2, h * 0.6)]
    mb.extrude_polygon(profile, d * 0.36, metal, p.get("tint", "#ffffff"), matrix=Matrix.Rotation(math.pi / 2, 4, "Z"))


def sawmill(mb: MeshBuilder, p, rng):
    w, h, d = p["size"]
    table(mb, {"size": (w, h * 0.55, d), "wood": "planks"}, rng)
    top = h * 0.55 * 0.82 + 0.08
    mb.cylinder((0.1, -0.04, top + 0.35), (0.1, 0.04, top + 0.35), 0.38, None, "prop:iron", "#c0c4cc", segments=20)
    mb.box((-0.05, -0.12, top), (0.25, 0.12, top + 0.2), "prop:dark_iron")
    for k in range(3):
        y = -d / 2 + 0.15 + k * 0.12
        mb.cylinder((-w / 2 + 0.1, y, top + 0.08), (-w / 2 + 0.8, y, top + 0.08), 0.06, None, "prop:bark", segments=8)


def loom(mb: MeshBuilder, p, rng):
    w, h, d = p["size"]
    wood = "prop:planks"
    for sx in (-1, 1):
        mb.box((sx * w * 0.42 - 0.05, -d * 0.35, 0), (sx * w * 0.42 + 0.05, -d * 0.25, h * 0.9), wood)
        mb.box((sx * w * 0.42 - 0.05, d * 0.25, 0), (sx * w * 0.42 + 0.05, d * 0.35, h * 0.55), wood)
        mb.box((sx * w * 0.42 - 0.05, -d * 0.35, 0.1), (sx * w * 0.42 + 0.05, d * 0.35, 0.18), wood)
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
    seat = h * 0.36
    mb.box((-w * 0.32, -d * 0.32, seat), (w * 0.32, d * 0.32, seat + 0.06), wood)
    for sx in (-1, 1):
        for sy in (-1, 1):
            top = h * 0.95 if sy < 0 else seat
            x, y = sx * w * 0.27, sy * d * 0.27
            mb.box((x - 0.04, y - 0.04, 0), (x + 0.04, y + 0.04, top), wood)
    for k in range(3):
        z = seat + 0.15 + k * (h * 0.95 - seat - 0.2) / 2
        mb.box((-w * 0.27, -d * 0.3, z), (w * 0.27, -d * 0.24, z + 0.07), wood)


def door(mb: MeshBuilder, p, rng):
    w, h, d = p["size"]
    wood = "prop:planks"
    leaf_w, leaf_h, t = w * 0.9, h * 0.97, 0.08
    for k in range(4):
        x0 = -leaf_w / 2 + k * leaf_w / 4
        mb.box((x0 + 0.005, -t / 2, 0.02), (x0 + leaf_w / 4 - 0.005, t / 2, leaf_h), wood)
    for z in (0.3, leaf_h - 0.35):
        mb.box((-leaf_w / 2, -t / 2 - 0.02, z), (leaf_w / 2, t / 2 + 0.02, z + 0.1), "prop:dark_planks")
    mb.box((-leaf_w / 2 + 0.05, -t / 2 - 0.03, leaf_h * 0.4), (leaf_w / 2 - 0.05, t / 2 + 0.03, leaf_h * 0.44), "prop:dark_iron")
    mb.sphere((leaf_w / 2 - 0.12, t / 2 + 0.05, leaf_h * 0.5), 0.04, "prop:iron", subdivisions=1)
    mb.sphere((leaf_w / 2 - 0.12, -t / 2 - 0.05, leaf_h * 0.5), 0.04, "prop:iron", subdivisions=1)


def chest(mb: MeshBuilder, p, rng):
    w, h, d = p["size"]
    wood, band = f"prop:{p.get('wood', 'planks')}", f"prop:{p.get('band', 'dark_iron')}"
    body_h = h * 0.58
    mb.box((-w * 0.46, -d * 0.4, 0), (w * 0.46, d * 0.4, body_h), wood)
    lid = [(-d * 0.4, 0), (d * 0.4, 0), (d * 0.38, h * 0.18), (d * 0.25, h * 0.3), (0, h * 0.36), (-d * 0.25, h * 0.3), (-d * 0.38, h * 0.18)]
    mb.extrude_polygon(lid, w * 0.92, wood, matrix=Matrix.Translation((0, 0, body_h)) @ Matrix.Rotation(math.pi / 2, 4, "Z"))
    for x in (-w * 0.3, w * 0.3):
        mb.box((x - 0.04, -d * 0.42, 0), (x + 0.04, d * 0.42, body_h + 0.02), band)
    mb.box((-0.07, d * 0.4, body_h - 0.14), (0.07, d * 0.44, body_h + 0.04), p.get("latch", "prop:gold"))


def torch(mb: MeshBuilder, p, rng):
    mb.cylinder((0, 0, 0), (0, 0, 0.32), 0.025, 0.03, "prop:bark", segments=6)
    mb.cylinder((0, 0, 0.3), (0, 0, 0.36), 0.04, 0.04, "prop:cloth", "#5a4030", segments=6)
    mb.sphere((0, 0, 0.42), 0.055, "prop:plain:glow", "#ffb040", subdivisions=1, scale=(1, 1, 1.6))
    mb.sphere((0, 0, 0.4), 0.035, "prop:plain:glow", "#fff0b0", subdivisions=1, scale=(1, 1, 1.4))


def candle(mb: MeshBuilder, p, rng):
    mb.cylinder((0, 0, 0), (0, 0, 0.02), 0.09, 0.1, "prop:copper", segments=10)
    mb.cylinder((0, 0, 0.02), (0, 0, 0.2), 0.035, None, "prop:plain", "#f0ead8", segments=8)
    mb.sphere((0, 0, 0.25), 0.03, "prop:plain:glow", "#ffc050", subdivisions=1, scale=(1, 1, 1.7))


def bed(mb: MeshBuilder, p, rng):
    w, h, d = p["size"]
    wood = "prop:planks"
    mb.box((-w * 0.46, -d * 0.48, 0.15), (w * 0.46, d * 0.48, 0.32), wood)
    _legs(mb, w * 0.92, d * 0.96, 0.18, 0.1, wood, inset=0.0)
    mb.box((-w * 0.46, -d * 0.48, 0.15), (w * 0.46, -d * 0.43, h * 0.9), wood)  # headboard at the back
    mb.box((-w * 0.46, d * 0.43, 0.15), (w * 0.46, d * 0.48, h * 0.55), wood)
    mb.box((-w * 0.42, -d * 0.42, 0.32), (w * 0.42, d * 0.42, 0.45), "prop:cloth")
    mb.box((-w * 0.43, -d * 0.1, 0.33), (w * 0.43, d * 0.43, 0.48), "prop:red_cloth")
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

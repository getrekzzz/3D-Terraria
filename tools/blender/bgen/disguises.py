"""Disguised creatures (Terraria's Mimic): the chest it poses as and the toothy chest it really is.

Both stand on their feet origin, front facing Blender +Y, inside the Mimic's 1 m hitbox. ``mimic_chest`` is built by the
very generator that builds the placeable Wooden Chest (furniture.chest at its 1 m footprint), so a sleeping Mimic is
indistinguishable from a real chest; ``mimic`` is the same chest awake, its lid thrown back over rows of teeth and a
lolling tongue. Params (both): wood, band, latch — prop layers as for furniture.chest; mimic only: gape (lid angle, rad).
"""
import math

from mathutils import Matrix, Vector

from . import generator
from .furniture import EDGE, chest
from .mesh import MeshBuilder

SIZE = (1.0, 1.0, 1.0)


@generator
def mimic_chest(mb: MeshBuilder, p, rng):
    """The disguise: a Wooden Chest, exactly."""
    chest(mb, {**p, "size": SIZE}, rng)
    return None


@generator
def mimic(mb: MeshBuilder, p, rng):
    """The Mimic awake: chest body, a lid hinged open at the back, fangs along both rims, a tongue and glinting eyes."""
    w, h, d = SIZE
    wood, band = f"prop:{p.get('wood', 'planks')}", f"prop:{p.get('band', 'dark_iron')}"
    latch = p.get("latch", "prop:gold")
    body_h = h * 0.58
    # Body: a hollow-looking box (dark maw inset in its top) with its iron bands.
    mb.bevel_box((-w * 0.46, -d * 0.4, 0), (w * 0.46, d * 0.4, body_h), wood, radius=0.018, segments=2)
    mb.box((-w * 0.4, -d * 0.34, body_h - 0.02), (w * 0.4, d * 0.34, body_h + 0.005), "prop:flesh", "#3a0a10")
    for x in (-w * 0.3, w * 0.3):
        mb.bevel_box((x - 0.04, -d * 0.42, 0), (x + 0.04, d * 0.42, body_h + 0.02), band, radius=EDGE)
    # The lid: the furniture lid profile, hinged at the back edge and thrown open by `gape`.
    gape = p.get("gape", 0.85)
    hinge = Matrix.Translation((0, -d * 0.4, body_h)) @ Matrix.Rotation(gape, 4, "X") @ Matrix.Translation((0, d * 0.4, 0))
    lid = [(-d * 0.4, 0), (d * 0.4, 0), (d * 0.38, h * 0.18), (d * 0.25, h * 0.3), (0, h * 0.36), (-d * 0.25, h * 0.3), (-d * 0.38, h * 0.18)]
    mb.extrude_polygon(lid, w * 0.92, wood, matrix=hinge @ Matrix.Rotation(math.pi / 2, 4, "Z"), bevel=EDGE)
    mb.box((-w * 0.4, -d * 0.34, -0.005), (w * 0.4, d * 0.34, 0.02), "prop:flesh", "#4a1018", matrix=hinge)  # inside of the lid
    mb.bevel_box((-0.07, d * 0.4, -0.12), (0.07, d * 0.44, 0.04), latch, radius=EDGE, matrix=hinge)
    # Fangs: down from the lid's front rim, up from the body's, and along the sides; uneven, like a predator's.
    tooth = "#f2ead6"
    for k in range(7):
        x = (k - 3) * w * 0.12
        size = 0.06 + rng.random() * 0.035
        mb.cylinder((x, d * 0.36, body_h), (x, d * 0.37, body_h + size * 1.6), size * 0.45, 0.0, "prop:plain", tooth, segments=5, smooth=False)
        top = hinge @ Vector((x, d * 0.36, 0.0))
        tip = hinge @ Vector((x, d * 0.37, -size * 1.8))
        mb.cylinder(top, tip, size * 0.45, 0.0, "prop:plain", tooth, segments=5, smooth=False)
    for sx in (-1, 1):
        for k in range(3):
            y = (k - 1) * d * 0.22
            mb.cylinder((sx * w * 0.42, y, body_h), (sx * w * 0.42, y, body_h + 0.07), 0.025, 0.0, "prop:plain", tooth, segments=4, smooth=False)
    # Tongue lolling over the front rim, and two eyes glinting in the dark under the lid.
    mb.tube([(0, -d * 0.05, body_h - 0.02), (0, d * 0.3, body_h + 0.03), (0, d * 0.5, body_h - 0.08), (0, d * 0.55, body_h - 0.22)],
            [0.1, 0.09, 0.07, 0.03], "prop:flesh", "#c83a5a", segments=6)
    for sx in (-1, 1):
        mb.sphere(hinge @ Vector((sx * w * 0.2, d * 0.1, -0.06)), 0.05, "prop:plain:glow", "#ffd040", subdivisions=1, scale=(1, 0.6, 1))
    return None

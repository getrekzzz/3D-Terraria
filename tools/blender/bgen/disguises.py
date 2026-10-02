"""Disguised creatures (Terraria's Mimic): the chest it poses as.

``mimic_chest`` stands on its feet origin, front facing Blender +Y, inside the Mimic's 1 m hitbox, and is built by the
very generator that builds the placeable Wooden Chest (furniture.chest at its 1 m footprint), so a sleeping Mimic is
indistinguishable from a real chest. Params: wood, band, latch — prop layers as for furniture.chest. The Mimic awake is
the rigged ``mimic`` creature (fauna2: lid bone, fangs, tongue, stubby legs).
"""
from . import generator
from .furniture import chest
from .mesh import MeshBuilder

SIZE = (1.0, 1.0, 1.0)


@generator
def mimic_chest(mb: MeshBuilder, p, rng):
    """The disguise: a Wooden Chest, exactly."""
    chest(mb, {**p, "size": SIZE}, rng)
    return None

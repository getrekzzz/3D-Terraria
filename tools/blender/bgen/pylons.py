"""Biome pylons (P5-34): a carved stone plinth with gold trim, and above it a slowly floating faceted crystal in the
biome's colour. Objects get p['size'] = [width, height, depth] in metres (origin at the centre of the bottom face).
pylon(p): crystal (hex), stone (hex), trim (hex).
"""
from . import generator
from .mesh import MeshBuilder


@generator
def pylon(mb: MeshBuilder, p, rng):
    """A two-tier plinth with a crystal hovering over a glowing ring."""
    w, h, d = p.get("size", [1.0, 2.0, 1.0])
    crystal, stone, trim = p.get("crystal", "#5ad84a"), p.get("stone", "#8a8a96"), p.get("trim", "#e8c040")
    hw, hd = w * 0.42, d * 0.42
    mb.box((-hw, -hd, 0.0), (hw, hd, 0.18), "prop:stone", stone, bevel=0.03)
    mb.box((-hw * 0.72, -hd * 0.72, 0.18), (hw * 0.72, hd * 0.72, 0.62), "prop:stone", stone, bevel=0.025)
    mb.box((-hw * 0.8, -hd * 0.8, 0.6), (hw * 0.8, hd * 0.8, 0.7), "prop:iron:metal", trim, bevel=0.015)
    for sx in (-1, 1):
        for sz in (-1, 1):
            mb.cylinder((sx * hw * 0.62, sz * hd * 0.62, 0.7), (sx * hw * 0.5, sz * hd * 0.5, 0.95), 0.03, 0.0, "prop:iron:metal", trim, segments=5)
    mb.cylinder((0, 0, 0.7), (0, 0, 0.74), hw * 0.5, None, "prop:plain:glow", crystal, segments=16)
    top = min(h - 0.1, 1.85)
    mid = (0.95 + top) * 0.5
    r = min(hw, hd) * 0.42
    mb.cylinder((0, 0, 0.95), (0, 0, mid), 0.0, r, "prop:crystal:glow", crystal, segments=6, smooth=False)
    mb.cylinder((0, 0, mid), (0, 0, top), r, 0.0, "prop:crystal:glow", crystal, segments=6, smooth=False)

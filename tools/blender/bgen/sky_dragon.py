"""The Wyvern: a long white sky dragon built as worm pieces (head, legs, body, tail), each modelled around its centre
along +Y, the direction of travel (the game strings the pieces behind the head and turns each toward the one in front).

Params (every part):
  part     "head" | "legs" | "body" | "tail"
  radius   body radius in metres (default 0.55; the head is about twice as long as a body piece)
  length   piece length in metres (default 1.05, a little over the segment spacing so pieces overlap)
  scales   main hide tint        belly   underside plates        mane   cloud-white tufts along the back
  horn     horn and claw tint    eyes    glowing eye colour
About 400–700 triangles per piece, so a sixteen-piece Wyvern stays under 10k.
"""
import math

from mathutils import Vector

from . import generator
from .mesh import MeshBuilder


def _palette(p):
    return (p.get("scales", "#eef2fa"), p.get("belly", "#a8bce0"), p.get("mane", "#ffffff"),
            p.get("horn", "#d8cca8"), p.get("eyes", "#5ad0ff"))


def _trunk(mb, r, length, scales, belly, mane, rng, taper=1.0):
    """A body piece: a slightly tapering scaled tube, a ridged belly plate and cloud tufts and wisps along the back."""
    mb.cylinder((0, -length * 0.55, 0), (0, length * 0.55, 0), r * taper, r, "prop:plain", scales, segments=12)
    mb.sphere((0, 0, -r * 0.62), r * 0.62, "prop:stone", belly, subdivisions=1, scale=(1.1, 1.5 * length / (r * 2), 0.5))
    for k in range(3):
        y = (k - 1) * length * 0.32
        mb.sphere((0, y, r * 0.88), r * (0.3 + rng.random() * 0.06), "prop:cloth", mane, subdivisions=1,
                  scale=(0.55, 1.3, 0.9), jitter=0.12, rng=rng)
    for sx in (-1, 1):  # wisps trailing back from the flanks, like Terraria's cloud fins
        base = Vector((sx * r * 0.8, length * 0.1, r * 0.35))
        mb.tube([base, base + Vector((sx * r * 0.45, -length * 0.35, r * 0.2)), base + Vector((sx * r * 0.6, -length * 0.75, r * 0.15))],
                [r * 0.18, r * 0.1, 0.0], "prop:cloth", mane, segments=5)


def _leg(mb, sx, r, scales, horn):
    """A short foreleg reaching down and forward, three hooked claws at the foot."""
    hip = Vector((sx * r * 0.75, 0.05, -r * 0.35))
    knee = hip + Vector((sx * r * 0.45, -r * 0.25, -r * 0.65))
    foot = knee + Vector((sx * r * 0.05, r * 0.45, -r * 0.45))
    mb.tube([hip, knee, foot], [r * 0.3, r * 0.2, r * 0.15], "prop:plain", scales, segments=7)
    for k in (-1, 0, 1):
        tip = foot + Vector((k * r * 0.12, r * 0.32, -r * 0.08))
        mb.cylinder(foot + Vector((k * r * 0.08, r * 0.05, 0)), tip, r * 0.06, 0.0, "prop:plain", horn, segments=4, smooth=False)


def _head(mb, r, length, scales, belly, mane, horn, eyes, rng):
    """Long-snouted dragon head: skull, snout with nostrils, a hanging jaw full of teeth, swept horns and a cloud mane."""
    mb.sphere((0, 0, 0.05 * r), r * 1.0, "prop:plain", scales, subdivisions=2, scale=(0.95, 1.25, 0.85))
    mb.cylinder((0, length * 0.35, 0.05 * r), (0, length * 1.25, -0.05 * r), r * 0.62, r * 0.34, "prop:plain", scales, segments=12)
    mb.sphere((0, length * 1.25, -0.05 * r), r * 0.36, "prop:plain", scales, subdivisions=1, scale=(1.0, 0.8, 0.8))
    for sx in (-1, 1):
        mb.sphere((sx * r * 0.16, length * 1.42, r * 0.08), r * 0.07, "prop:plain", "#2a3040", subdivisions=1)  # nostrils
    # Lower jaw, hinged under the skull and hanging a little open.
    mb.tube([(0, 0, -r * 0.45), (0, length * 0.7, -r * 0.62), (0, length * 1.2, -r * 0.55)], [r * 0.5, r * 0.36, r * 0.2],
            "prop:stone", belly, segments=8)
    for k in range(5):
        y = length * (0.5 + k * 0.16)
        for sx in (-1, 1):
            x = sx * r * (0.38 - k * 0.04)
            mb.cylinder((x, y, -r * 0.3), (x, y + 0.02, -r * 0.5), r * 0.05, 0.0, "prop:plain", "#f4efe2", segments=4, smooth=False)
            mb.cylinder((x * 0.95, y + 0.05, -r * 0.5), (x * 0.95, y + 0.06, -r * 0.33), r * 0.04, 0.0, "prop:plain", "#f4efe2", segments=4, smooth=False)
    for sx in (-1, 1):
        mb.sphere((sx * r * 0.55, length * 0.42, r * 0.38), r * 0.16, "prop:plain:glow", eyes, subdivisions=1, scale=(0.7, 1.2, 0.6))
        mb.cylinder((sx * r * 0.42, length * 0.32, r * 0.55), (sx * r * 0.7, length * 0.5, r * 0.5), r * 0.1, r * 0.04,
                    "prop:plain", scales, segments=5)  # brow ridge
        root = Vector((sx * r * 0.42, -length * 0.05, r * 0.7))
        mb.tube([root, root + Vector((sx * r * 0.25, -length * 0.45, r * 0.35)), root + Vector((sx * r * 0.35, -length * 0.95, r * 0.45))],
                [r * 0.16, r * 0.1, 0.0], "prop:plain", horn, segments=6)
        mb.tube([Vector((sx * r * 0.35, length * 1.3, -r * 0.1)), Vector((sx * r * 0.9, length * 1.1, -r * 0.35)),
                 Vector((sx * r * 1.3, length * 0.7, -r * 0.6))], [r * 0.05, r * 0.035, 0.0], "prop:cloth", mane, segments=4)  # whiskers
    for k in range(4):  # the mane: cloud puffs from the crown down the neck
        mb.sphere((0, -length * (0.1 + k * 0.22), r * (0.85 - k * 0.08)), r * (0.38 - k * 0.04), "prop:cloth", mane, subdivisions=1,
                  scale=(0.75, 1.2, 0.9), jitter=0.12, rng=rng)


@generator
def sky_dragon(mb: MeshBuilder, p, rng):
    """One piece of the Wyvern (see the module notes for params)."""
    part, r, length = p.get("part", "body"), p.get("radius", 0.55), p.get("length", 1.05)
    scales, belly, mane, horn, eyes = _palette(p)
    if part == "head":
        _head(mb, r, length, scales, belly, mane, horn, eyes, rng)
    elif part == "tail":
        mb.cylinder((0, -length * 0.7, 0), (0, length * 0.55, 0), r * 0.18, r * 0.8, "prop:plain", scales, segments=10)
        mb.sphere((0, 0, -r * 0.4), r * 0.42, "prop:stone", belly, subdivisions=1, scale=(1.0, 2.0, 0.5))
        for k in range(5):  # a fan of cloud plumes at the tip
            a = (k - 2) * 0.35
            tip = Vector((math.sin(a) * r * 1.0, -length * 1.6, math.cos(a) * r * 0.55))
            mb.tube([Vector((0, -length * 0.55, 0)), Vector((tip.x * 0.5, -length * 1.1, tip.z * 0.6)), tip],
                    [r * 0.2, r * 0.16, 0.0], "prop:cloth", mane, segments=5)
    else:
        _trunk(mb, r, length, scales, belly, mane, rng)
        if part == "legs":
            for sx in (-1, 1):
                _leg(mb, sx, r, scales, horn)
    return None

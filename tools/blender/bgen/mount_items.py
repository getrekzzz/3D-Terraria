"""Mount summoning items (X-14): the Slimy Saddle, Fuzzy Carrot, Honeyed Goggles and Cosmic Car Key. Items follow
items.py (held along +Z, about 0.3–0.4 m)."""
import math

from mathutils import Vector

from . import generator


@generator
def slimy_saddle(mb, p, rng):
    """A leather saddle dripping blue gel: seat, pommel, flaps and stirrups."""
    leather, gel = "prop:plain", "#4aa0ff"
    mb.sphere((0, 0, 0.12), 0.16, leather, "#b07440", subdivisions=2, scale=(1.0, 1.3, 0.35))
    mb.cylinder((0, 0.17, 0.12), (0, 0.19, 0.22), 0.04, 0.03, leather, "#8a5a30", segments=8)
    for sx in (-1, 1):
        mb.box((sx * 0.15 - 0.012, -0.08, -0.06), (sx * 0.15 + 0.012, 0.08, 0.1), leather, "#9a6436")
        mb.cylinder((sx * 0.17, 0, -0.06), (sx * 0.17, 0, -0.12), 0.012, 0.012, "prop:iron", "#a0a4ac", segments=6)
        mb.cylinder((sx * 0.17 - 0.03, 0, -0.14), (sx * 0.17 + 0.03, 0, -0.14), 0.02, 0.02, "prop:iron", "#a0a4ac", segments=8)
    for i in range(5):
        x, y = rng.uniform(-0.12, 0.12), rng.uniform(-0.15, 0.15)
        mb.sphere((x, y, 0.06), rng.uniform(0.025, 0.045), "prop:gel", gel, subdivisions=1, scale=(1, 1, 1.6))


@generator
def fuzzy_carrot(mb, p, rng):
    """A carrot fuzzy with fine hairs, its leafy green top."""
    profile = [(0.0, -0.18), (0.012, -0.16), (0.03, -0.08), (0.045, 0.02), (0.05, 0.1), (0.04, 0.13), (0.0, 0.135)]
    mb.lathe([(r, z) for r, z in profile], "prop:plain", "#e8781e", segments=14)
    for i in range(30):
        z = rng.uniform(-0.15, 0.11)
        a = rng.uniform(0, math.pi * 2)
        r = 0.05 * (z + 0.18) / 0.3
        base = Vector((math.cos(a) * r, math.sin(a) * r, z))
        mb.tube([base, base * 1.0 + Vector((math.cos(a), math.sin(a), 0.4)) * 0.018], [0.003, 0.001], "prop:plain", "#f4c090", segments=3)
    for i in range(5):
        a = i * math.pi * 2 / 5 + rng.uniform(-0.2, 0.2)
        tip = Vector((math.cos(a) * 0.05, math.sin(a) * 0.05, 0.26 + rng.uniform(-0.02, 0.03)))
        mb.tube([Vector((0, 0, 0.13)), (Vector((0, 0, 0.13)) + tip) * 0.5 + Vector((0, 0, 0.03)), tip], [0.012, 0.009, 0.003], "prop:leaves", "#4a9a2a", segments=5)


@generator
def honeyed_goggles(mb, p, rng):
    """Aviator goggles with amber lenses and honey dripping off the strap."""
    for sx in (-1, 1):
        mb.cylinder((sx * 0.06, 0, 0.0), (sx * 0.06, 0.03, 0.0), 0.05, 0.05, "prop:gold", "#c09040", segments=16)
        mb.cylinder((sx * 0.06, 0.028, 0.0), (sx * 0.06, 0.034, 0.0), 0.042, 0.042, "prop:crystal", "#f0a820", segments=16)
    mb.box((-0.015, 0.0, -0.01), (0.015, 0.025, 0.01), "prop:gold", "#c09040")
    strap = [Vector((math.sin(t) * 0.13, -math.cos(t) * 0.1 + 0.0, 0)) for t in (math.pi * (0.5 + i / 16) for i in range(17))]
    mb.tube(strap, [0.012] * len(strap), "prop:leather", "#8a5a30", segments=6)
    for i in range(4):
        x = rng.uniform(-0.1, 0.1)
        mb.sphere((x, rng.uniform(-0.08, 0.0), -0.02 - rng.uniform(0, 0.03)), rng.uniform(0.012, 0.02), "prop:gel", "#f0b030",
                  subdivisions=1, scale=(1, 1, 1.8))


@generator
def cosmic_car_key(mb, p, rng):
    """A key with a glowing alien fob: a teardrop of green crystal in a silver frame, a toothed blade."""
    mb.sphere((0, 0, 0.12), 0.07, "prop:plain", "#d8dce4", subdivisions=2, scale=(1, 0.35, 1.25))
    mb.sphere((0, 0, 0.12), 0.05, "prop:plain:glow", "#60f0a0", subdivisions=2, scale=(1, 0.75, 1.25))
    mb.cylinder((0, 0, 0.21), (0, 0, 0.24), 0.02, 0.02, "prop:iron", "#c8ccd4", segments=10)
    mb.box((-0.012, -0.006, -0.16), (0.012, 0.006, 0.04), "prop:plain", "#e0e4ec")
    for i in range(4):
        z = -0.14 + i * 0.04
        mb.box((0.012, -0.006, z), (0.03, 0.006, z + 0.02), "prop:plain", "#e0e4ec")

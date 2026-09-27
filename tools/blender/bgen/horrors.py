"""Evil-biome and hive models: the Brain of Cthulhu (creature, +Y forward, centred), and the Shadow Orb / Crimson
Heart, Demon / Crimson Altar and Queen Bee larva (tile objects: ``size`` in metres, origin at the floor centre)."""
import math

from mathutils import Vector

from .mesh import MeshBuilder


def brain(mb: MeshBuilder, p, rng):
    """A lumpy pink brain with folded lobes, a dangling spinal cord and (exposed) a single staring eye."""
    r = p.get("radius", 1.2)
    color, fold = p.get("color", "#d88a9a"), p.get("fold", "#a85a6a")
    for sx in (-1, 1):  # two hemispheres
        mb.sphere((sx * r * 0.28, 0, r * 0.05), r * 0.72, "prop:flesh", color, subdivisions=3, scale=(0.8, 1.1, 0.85), jitter=0.06, rng=rng)
    for k in range(14):  # gyri: short worm-like folds over the surface
        a, b = rng.random() * math.tau, rng.random() * math.pi * 0.8 + 0.1
        n = Vector((math.cos(a) * math.sin(b), math.sin(a) * math.sin(b) * 1.2, math.cos(b) * 0.9))
        c = n * r * 0.78
        t = n.cross(Vector((0, 0, 1))).normalized() if abs(n.z) < 0.95 else Vector((1, 0, 0))
        mb.tube([c - t * r * 0.25, c + n * 0.03, c + t * r * 0.25], [r * 0.07, r * 0.09, r * 0.07], "prop:flesh", fold, segments=5)
    spine = [Vector((0, -r * 0.2, -r * 0.6)), Vector((0.1, -r * 0.3, -r * 1.2)), Vector((-0.1, -r * 0.1, -r * 1.8)), Vector((0.05, 0, -r * 2.3))]
    mb.tube(spine, [r * 0.18, r * 0.14, r * 0.1, r * 0.03], "prop:flesh", fold, segments=7)
    if p.get("exposed"):
        mb.sphere((0, r * 0.75, 0), r * 0.38, "prop:plain", "#f4efe8", subdivisions=2)
        mb.sphere((0, r * 1.05, 0), r * 0.18, "prop:plain:glow", "#e03030", subdivisions=2, scale=(1, 0.45, 1))
        mb.sphere((0, r * 1.12, 0), r * 0.08, "prop:plain", "#050505", subdivisions=1, scale=(1, 0.4, 1))
    return None


def evil_orb(mb: MeshBuilder, p, rng):
    """Shadow Orb (a dark glowing sphere wrapped in thorny shell) or Crimson Heart (a veined, beating heart)."""
    w, h, _ = p["size"]
    r = min(w, h) * 0.42
    c = (0, 0, h * 0.5)
    if p.get("kind") == "heart":
        mb.sphere(c, r, "prop:flesh", "#b82a3a", subdivisions=3, scale=(1, 0.85, 1.1), jitter=0.05, rng=rng)
        mb.sphere(c, r * 0.55, "prop:plain:glow", "#ff5a5a", subdivisions=2)
        for k in range(8):
            a = k * math.tau / 8 + rng.random() * 0.3
            d = Vector((math.cos(a), math.sin(a), rng.uniform(-0.5, 0.5))).normalized()
            base = Vector(c) + d * r * 0.9
            mb.tube([base, base + d * r * 0.5 + Vector((0, 0, -r * 0.3)), base + d * r * 0.8 + Vector((0, 0, -r * 0.9))], [r * 0.12, r * 0.08, r * 0.03], "prop:flesh", "#8a1a2a", segments=5)
        return
    mb.sphere(c, r * 0.8, "prop:plain:glow", "#9a4aff", subdivisions=3)
    for k in range(10):
        a = k * math.tau / 10
        d = Vector((math.cos(a), math.sin(a), rng.uniform(-0.8, 0.8))).normalized()
        base = Vector(c) + d * r * 0.7
        mb.tube([base - d * r * 0.2, base + d * r * 0.25, base + d * r * 0.5 + Vector((0, 0, r * 0.2))], [r * 0.16, r * 0.1, r * 0.01], "prop:stone", "#3a2a4a", segments=5)


def altar(mb: MeshBuilder, p, rng):
    """A rough stone altar: a slab on legs, horned corners and a glowing bowl."""
    w, h, d = p["size"]
    stone, glow = p.get("stone", "#4a3a5a"), p.get("glow", "#b060ff")
    mb.box((-w * 0.45, -d * 0.4, 0), (w * 0.45, d * 0.4, h * 0.25), "prop:stone", stone)
    for sx in (-1, 1):
        mb.box((sx * w * 0.3 - w * 0.08, -d * 0.3, h * 0.25), (sx * w * 0.3 + w * 0.08, d * 0.3, h * 0.55), "prop:stone", stone)
    mb.box((-w * 0.5, -d * 0.45, h * 0.55), (w * 0.5, d * 0.45, h * 0.7), "prop:stone", stone)
    for sx in (-1, 1):
        for sy in (-1, 1):
            base = Vector((sx * w * 0.44, sy * d * 0.38, h * 0.7))
            mb.tube([base, base + Vector((sx * 0.05, 0, h * 0.2)), base + Vector((-sx * 0.05, 0, h * 0.35))], [0.06, 0.04, 0.005], "prop:plain", p.get("horn", "#d8c8a8"), segments=5)
    mb.cylinder((0, 0, h * 0.7), (0, 0, h * 0.82), w * 0.16, w * 0.2, "prop:stone", stone, segments=10)
    mb.sphere((0, 0, h * 0.8), w * 0.12, "prop:plain:glow", glow, subdivisions=2, scale=(1, 1, 0.4))


def larva(mb: MeshBuilder, p, rng):
    """The Queen Bee's larva: a pale, segmented grub in a waxy cell."""
    w, h, _ = p["size"]
    r = min(w, h) * 0.4
    mb.cylinder((0, 0, 0), (0, 0, h * 0.3), r * 1.1, r * 1.2, "prop:gel", "#e8b030", segments=6, smooth=False)
    for k in range(4):
        mb.sphere((0, (k - 1.5) * r * 0.35, h * 0.35 + math.sin(k) * 0.02), r * (0.45 - abs(k - 1.5) * 0.06), "prop:flesh", "#f4ead0", subdivisions=2)
    mb.sphere((0, r * 0.75, h * 0.38), r * 0.25, "prop:flesh", "#e8d8b0", subdivisions=1)


GENERATORS = {"brain": brain, "evil_orb": evil_orb, "altar": altar, "larva": larva}

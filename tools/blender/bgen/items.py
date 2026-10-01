"""Hand-held items and item-only models (icons, drops, held visuals). Origin at the grip, business end along +Z.
Colour parameters: ``metal`` (tint of the metal layer), ``wood``, ``gem``, ``color``."""
import math

from mathutils import Matrix, Vector

from .mesh import MeshBuilder

HANDLE = "prop:dark_planks"


def _metal(p):
    return f"prop:{p.get('metalLayer', 'iron')}", p.get("metal", "#c0c4cc")


def sword(mb: MeshBuilder, p, rng):
    layer, tint = _metal(p)
    length, width = p.get("length", 0.9), p.get("width", 0.09)
    mb.cylinder((0, 0, -0.18), (0, 0, 0.02), 0.022, None, HANDLE, segments=8)
    mb.sphere((0, 0, -0.2), 0.035, layer, tint, subdivisions=1)
    mb.box((-width * 1.4, -0.025, 0.02), (width * 1.4, 0.025, 0.07), layer, p.get("guard", tint))
    blade = [(-width / 2, 0.07), (width / 2, 0.07), (width / 2, length * 0.85), (0, length), (-width / 2, length * 0.85)]
    glow = ":glow" if p.get("glow") else ""
    mb.extrude_polygon(blade, 0.018, layer + glow, tint)
    mb.extrude_polygon([(-0.008, 0.08), (0.008, 0.08), (0.008, length * 0.8), (-0.008, length * 0.8)], 0.024, layer, "#e8ecf0")


def shortsword(mb: MeshBuilder, p, rng):
    sword(mb, {**p, "length": p.get("length", 0.55), "width": p.get("width", 0.07)}, rng)


def _haft(mb, length, wood="prop:planks", tint="#ffffff"):
    mb.cylinder((0, 0, -0.1), (0, 0, length), 0.022, 0.02, wood, tint, segments=7)


def pickaxe(mb: MeshBuilder, p, rng):
    layer, tint = _metal(p)
    _haft(mb, 0.75)
    head = [Vector((-0.34, 0, 0.58)), Vector((-0.18, 0, 0.72)), Vector((0, 0, 0.76)), Vector((0.18, 0, 0.72)), Vector((0.34, 0, 0.58))]
    mb.tube(head, [0.012, 0.035, 0.045, 0.035, 0.012], layer, tint, segments=6)


def axe(mb: MeshBuilder, p, rng):
    layer, tint = _metal(p)
    _haft(mb, 0.7)
    blade = [(0.0, 0.52), (0.1, 0.5), (0.22, 0.44), (0.24, 0.62), (0.22, 0.8), (0.1, 0.74), (0.0, 0.72)]
    mb.extrude_polygon(blade, 0.03, layer, tint)
    mb.box((-0.06, -0.035, 0.56), (0.02, 0.035, 0.7), layer, tint)


def hammer(mb: MeshBuilder, p, rng):
    layer, tint = _metal(p)
    wood = p.get("headLayer")
    _haft(mb, 0.65)
    mb.box((-0.16, -0.07, 0.6), (0.16, 0.07, 0.76), f"prop:{wood}" if wood else layer, tint if not wood else "#ffffff")


def bow(mb: MeshBuilder, p, rng):
    layer = f"prop:{p.get('limbLayer', 'planks')}"
    tint = p.get("metal", "#ffffff") if p.get("limbLayer") in ("iron", "gold", "copper") else "#ffffff"
    pts = [Vector((0.02 * math.sin(t * math.pi) - 0.0, 0, (t - 0.5) * 1.1)) + Vector((-0.18 * math.sin(t * math.pi), 0, 0)) for t in (i / 8 for i in range(9))]
    mb.tube(pts, [0.012, 0.018, 0.022, 0.026, 0.03, 0.026, 0.022, 0.018, 0.012], layer, tint, segments=6)
    mb.cylinder(pts[0], pts[-1], 0.004, None, "prop:cloth", "#f0f0e0", segments=4)
    mb.cylinder((-0.17, 0, -0.08), (-0.17, 0, 0.08), 0.03, None, "prop:red_cloth", segments=8)
    # The hand holds the riser; the arrow nocks at the string centre (the origin) and flies toward the riser (-X).
    mb.socket("grip", (-0.17, 0, 0))
    mb.socket("nock", (0, 0, 0), (-1, 0, 0), (0, 0, 1))


def wand(mb: MeshBuilder, p, rng):
    mb.cylinder((0, 0, -0.1), (0, 0, 0.42), 0.016, 0.012, "prop:bark", segments=6)
    mb.sphere((0, 0, 0.46), 0.04, "prop:plain:glow", p.get("gem", "#ff8a2a"), subdivisions=1)
    mb.socket("tip", (0, 0, 0.46))


def staff(mb: MeshBuilder, p, rng):
    mb.cylinder((0, 0, -0.2), (0, 0, 1.1), 0.024, 0.02, "prop:planks", segments=7)
    for k in range(3):
        a = k * 2 * math.pi / 3
        mb.tube([(0, 0, 1.05), (math.cos(a) * 0.06, math.sin(a) * 0.06, 1.15), (math.cos(a) * 0.03, math.sin(a) * 0.03, 1.25)],
                [0.012, 0.01, 0.004], "prop:gold", segments=4)
    mb.sphere((0, 0, 1.18), 0.055, "prop:crystal:glow", p.get("gem", "#b06aff"), subdivisions=1, scale=(1, 1, 1.4))
    mb.socket("tip", (0, 0, 1.18))


def arrow(mb: MeshBuilder, p, rng):
    mb.cylinder((0, 0, -0.35), (0, 0, 0.3), 0.008, None, "prop:planks", segments=5)
    mb.cylinder((0, 0, 0.3), (0, 0, 0.38), 0.02, 0.0, p.get("tipLayer", "prop:stone"), p.get("tip", "#b0b0b0"), segments=4, smooth=False)
    for k in range(3):
        a = k * 2 * math.pi / 3
        m = Matrix.Rotation(a, 4, "Z")
        mb.extrude_polygon([(0.0, -0.34), (0.035, -0.3), (0.035, -0.22), (0.0, -0.18)], 0.004, "prop:cloth", p.get("fletching", "#e8e0d0"), matrix=m)


def essence(mb: MeshBuilder, p, rng):
    """A floating mote of essence: a glowing core in a pale shell with wisps drifting around it."""
    color = p["color"]
    mb.sphere((0, 0, 0.12), 0.05, "prop:plain:glow", p.get("core", "#ffffff"), subdivisions=2)
    mb.sphere((0, 0, 0.12), 0.085, "prop:gel:glow", color, subdivisions=2, jitter=0.08, rng=rng)
    for k in range(4):
        a = k * math.pi / 2 + 0.4
        pts = [Vector((math.cos(a + t) * (0.09 + t * 0.05), math.sin(a + t) * (0.09 + t * 0.05), 0.12 + t * 0.1 - 0.05)) for t in (0.0, 0.4, 0.8, 1.2)]
        mb.tube(pts, [0.014, 0.01, 0.006, 0.001], "prop:plain:glow", color, segments=4)


def bar(mb: MeshBuilder, p, rng):
    layer, tint = _metal(p)
    for i, off in enumerate([(0, 0, 0), (0.05, 0.02, 0.075)][: p.get("count", 1)]):
        profile = [(-0.14, 0.0), (0.14, 0.0), (0.11, 0.07), (-0.11, 0.07)]
        mb.extrude_polygon(profile, 0.1, layer, tint, matrix=Matrix.Translation(off))


def gem(mb: MeshBuilder, p, rng):
    color = p["color"]
    mb.cylinder((0, 0, 0.0), (0, 0, 0.1), 0.0, 0.09, "prop:crystal", color, segments=6, smooth=False)
    mb.cylinder((0, 0, 0.1), (0, 0, 0.15), 0.09, 0.05, "prop:crystal", color, segments=6, smooth=False)


def blob(mb: MeshBuilder, p, rng):
    mb.sphere((0, 0, 0.08), 0.12, f"prop:{p.get('layer', 'gel')}", p["color"], subdivisions=2, scale=(1, 1, 0.7), jitter=0.05, rng=rng)
    mb.sphere((0.04, 0.05, 0.13), 0.025, "prop:plain", "#ffffff", subdivisions=1)


def lens(mb: MeshBuilder, p, rng):
    mb.lathe([(0.0, 0.0), (0.1, 0.01), (0.12, 0.025), (0.1, 0.04), (0.0, 0.05)], "prop:gel", p.get("color", "#f0f0f0"), segments=16)
    mb.lathe([(0.0, 0.045), (0.05, 0.05), (0.0, 0.056)], "prop:plain", "#4060a0", segments=12)


def spool(mb: MeshBuilder, p, rng):
    mb.cylinder((0, 0, 0), (0, 0, 0.18), 0.07, None, "prop:cloth", p.get("color", "#f0f0f0"), segments=12)
    for z in (0.0, 0.18):
        mb.cylinder((0, 0, z - 0.01), (0, 0, z + 0.01), 0.09, None, "prop:planks", segments=12)


def potion(mb: MeshBuilder, p, rng):
    """Round flask: the liquid is the body (so it reads at icon size), glass shoulders and neck, cork."""
    s = 0.3
    liquid = "prop:plain:glow" if p.get("glow") else "prop:gel"
    mb.lathe([(0.0, 0.0), (s * 0.3, 0.0), (s * 0.4, s * 0.16), (s * 0.4, s * 0.34), (s * 0.3, s * 0.46), (0.0, s * 0.5)], liquid, p["color"], segments=14)
    mb.lathe([(s * 0.3, s * 0.46), (s * 0.14, s * 0.6), (s * 0.11, s * 0.8), (0.0, s * 0.8)], "prop:gel", "#e8f4ff", segments=14)
    mb.cylinder((0, 0, s * 0.78), (0, 0, s * 0.92), s * 0.09, None, "prop:bark", segments=8)


def coin(mb: MeshBuilder, p, rng):
    layer, tint = _metal(p)
    for i in range(p.get("stack", 3)):
        off = Vector(((rng.random() - 0.5) * 0.02, (rng.random() - 0.5) * 0.02, i * 0.018))
        mb.cylinder(off, off + Vector((0, 0, 0.016)), 0.07, None, layer, tint, segments=16)


def star(mb: MeshBuilder, p, rng):
    pts = []
    for k in range(10):
        a = math.pi / 2 + k * math.pi / 5
        r = 0.15 if k % 2 == 0 else 0.065
        pts.append((math.cos(a) * r, math.sin(a) * r + 0.15))
    mb.extrude_polygon(pts, 0.05, "prop:plain:glow", p["color"])


def heart(mb: MeshBuilder, p, rng):
    mat = "prop:crystal:glow"
    mb.sphere((-0.055, 0, 0.16), 0.07, mat, p["color"], subdivisions=1)
    mb.sphere((0.055, 0, 0.16), 0.07, mat, p["color"], subdivisions=1)
    mb.cylinder((0, 0, 0.15), (0, 0, 0.0), 0.11, 0.0, mat, p["color"], segments=6, smooth=False)


def seed(mb: MeshBuilder, p, rng):
    mb.sphere((0, 0, 0.05), 0.05, "prop:planks", "#b08050", subdivisions=2, scale=(1, 1, 1.2))
    mb.lathe([(0.0, 0.08), (0.055, 0.085), (0.05, 0.11), (0.0, 0.12)], "prop:bark", segments=10)
    mb.cylinder((0, 0, 0.12), (0.01, 0, 0.15), 0.006, None, "prop:bark", segments=4)


def chunk(mb: MeshBuilder, p, rng):
    mb.sphere((0, 0, 0.08), 0.1, f"prop:{p.get('layer', 'stone')}", p.get("color", "#ffffff"), subdivisions=1, jitter=0.25, rng=rng)


def helmet(mb: MeshBuilder, p, rng):
    layer, tint = _metal(p)
    mb.lathe([(0.13, 0.0), (0.14, 0.08), (0.12, 0.16), (0.07, 0.22), (0.0, 0.24)], layer, tint, segments=14)
    mb.box((-0.02, 0.1, 0.0), (0.02, 0.15, 0.12), layer, tint)


def chest_armor(mb: MeshBuilder, p, rng):
    layer, tint = _metal(p)
    mb.lathe([(0.12, 0.0), (0.15, 0.1), (0.17, 0.25), (0.12, 0.33), (0.05, 0.35)], layer, tint, segments=12)
    for sx in (-1, 1):
        mb.sphere((sx * 0.17, 0, 0.3), 0.07, layer, tint, subdivisions=1, scale=(1, 1, 0.6))


def greaves(mb: MeshBuilder, p, rng):
    layer, tint = _metal(p)
    for sx in (-1, 1):
        mb.cylinder((sx * 0.06, 0, 0.0), (sx * 0.06, 0, 0.3), 0.045, 0.055, layer, tint, segments=8)
        mb.box((sx * 0.06 - 0.05, -0.02, 0.0), (sx * 0.06 + 0.05, 0.08, 0.04), layer, tint)
    mb.box((-0.12, -0.05, 0.28), (0.12, 0.05, 0.34), layer, tint)


def boots(mb: MeshBuilder, p, rng):
    for sx in (-1, 1):
        mb.cylinder((sx * 0.07, 0, 0.03), (sx * 0.07, 0, 0.2), 0.045, 0.05, "prop:red_cloth", p.get("color", "#ffffff"), segments=8)
        mb.box((sx * 0.07 - 0.045, -0.04, 0.0), (sx * 0.07 + 0.045, 0.1, 0.05), "prop:dark_planks")
        mb.sphere((sx * 0.07 + sx * 0.05, 0, 0.17), 0.03, "prop:plain", "#f0f0f0", subdivisions=1, scale=(0.4, 1.4, 1))


def horseshoe(mb: MeshBuilder, p, rng):
    pts = [Vector((math.cos(a) * 0.1, 0, math.sin(a) * 0.1 + 0.1)) for a in (math.radians(d) for d in range(-50, 231, 35))]
    mb.tube(pts, [0.02] * len(pts), "prop:iron", "#c0c4cc", segments=6)


def ring(mb: MeshBuilder, p, rng):
    pts = [Vector((math.cos(a) * 0.07, 0, math.sin(a) * 0.07 + 0.08)) for a in (i * 2 * math.pi / 16 for i in range(17))]
    mb.tube(pts, [0.014] * len(pts), "prop:gold", "#ffffff", segments=6)
    mb.sphere((0, 0, 0.16), 0.025, "prop:crystal:glow", p.get("gem", "#ff4060"), subdivisions=1)


def bottled(mb: MeshBuilder, p, rng):
    potion(mb, {"color": p.get("color", "#e0f0ff")}, rng)
    mb.sphere((0, 0, 0.1), 0.06, "prop:plain", "#ffffff", subdivisions=2, jitter=0.15, rng=rng)


def bucket(mb: MeshBuilder, p, rng):
    """Tapered iron pail with a rolled rim and wire handle; filled buckets show the liquid's surface."""
    layer, tint = "prop:iron", "#b8bcc4"
    mb.lathe([(0.0, 0.0), (0.1, 0.0), (0.13, 0.22), (0.12, 0.22), (0.092, 0.018), (0.0, 0.018)], layer, tint, segments=18)
    rim = [Vector((math.cos(a) * 0.126, math.sin(a) * 0.126, 0.222)) for a in (i * 2 * math.pi / 20 for i in range(21))]
    mb.tube(rim, [0.009] * len(rim), layer, tint, segments=5)
    handle = [Vector((math.cos(a) * 0.128, 0, 0.2 + math.sin(a) * 0.15)) for a in (math.radians(d) for d in range(0, 181, 15))]
    mb.tube(handle, [0.006] * len(handle), layer, "#9098a0", segments=5)
    if "liquid" in p:
        glow = p.get("glow", False)
        mb.cylinder((0, 0, 0.02), (0, 0, 0.18), 0.094, 0.117, "prop:plain:glow" if glow else "prop:gel", p["liquid"], segments=18)


def feather(mb: MeshBuilder, p, rng):
    """Quill with a curved vane of barbs."""
    color = p.get("color", "#4a3a30")
    mb.cylinder((0, 0, 0), (0.02, 0, 0.34), 0.008, 0.004, "prop:plain", "#e8e0d0", segments=4)
    pts = [(0.0, 0.06), (0.05, 0.1), (0.07, 0.2), (0.06, 0.3), (0.02, 0.36), (-0.03, 0.3), (-0.05, 0.18), (-0.03, 0.08)]
    mb.extrude_polygon(pts, 0.006, "prop:cloth", color)


def spike(mb: MeshBuilder, p, rng):
    """Curved stinger / tooth: a tapered horn."""
    color = p.get("color", "#2a2418")
    length = p.get("length", 0.3)
    pts = [Vector((0, 0, 0)), Vector((0.02, 0, length * 0.5)), Vector((0.07, 0, length))]
    mb.tube(pts, [p.get("width", 0.045), p.get("width", 0.045) * 0.6, 0.002], p.get("layer", "prop:plain"), color, segments=6)


def bone(mb: MeshBuilder, p, rng):
    color = p.get("color", "#e8e0c8")
    mb.cylinder((0, 0, 0.05), (0, 0, 0.3), 0.025, None, "prop:plain", color, segments=6)
    for z in (0.04, 0.31):
        for sx in (-1, 1):
            mb.sphere((sx * 0.03, 0, z), 0.035, "prop:plain", color, subdivisions=1)


def tome(mb: MeshBuilder, p, rng):
    """A thick spellbook standing on its spine edge, with a clasp and an emblem on the cover."""
    cover, pages = p.get("cover", "#3a2a4a"), p.get("pages", "#e8dcc0")
    mb.box((-0.12, -0.05, 0.0), (0.12, 0.05, 0.3), "prop:cloth", cover)
    mb.box((-0.11, -0.04, 0.01), (0.115, 0.04, 0.29), "prop:plain", pages)
    mb.box((-0.125, -0.055, 0.0), (-0.1, 0.055, 0.3), "prop:cloth", cover)  # spine
    mb.box((0.11, -0.015, 0.12), (0.13, 0.015, 0.18), "prop:gold", "#ffffff")  # clasp
    mb.sphere((0.0, 0.056, 0.15), 0.045, "prop:plain", p.get("emblem", "#e8e0c8"), subdivisions=1, scale=(1, 0.4, 1.1))
    for sx in (-1, 1):
        mb.sphere((sx * 0.016, 0.07, 0.16), 0.01, "prop:plain:glow", p.get("glow", "#ff5030"), subdivisions=1)


def doll(mb: MeshBuilder, p, rng):
    """A small stitched doll (the wayfinder's effigy)."""
    cloth, skin = p.get("shirt", "#5a7a3a"), p.get("skin", "#e0b088")
    mb.box((-0.06, -0.035, 0.08), (0.06, 0.035, 0.2), "prop:cloth", cloth)
    for sx in (-1, 1):
        mb.box((sx * 0.02 - 0.022, -0.03, 0.0), (sx * 0.02 + 0.022, 0.03, 0.08), "prop:cloth", p.get("pants", "#4a3a2a"))
        mb.box((sx * 0.085 - 0.02, -0.025, 0.1), (sx * 0.085 + 0.02, 0.025, 0.19), "prop:cloth", cloth)
    mb.sphere((0, 0, 0.25), 0.055, "prop:cloth", skin, subdivisions=2)
    mb.box((-0.055, -0.05, 0.27), (0.055, 0.04, 0.31), "prop:cloth", p.get("hair", "#6a4020"))
    for sx in (-1, 1):
        mb.sphere((sx * 0.022, 0.05, 0.255), 0.011, "prop:plain", "#101010", subdivisions=1)
    mb.cylinder((-0.07, 0.036, 0.16), (0.07, 0.036, 0.12), 0.004, None, "prop:plain", "#d0c0a0", segments=4)  # a stitch


def sigil(mb: MeshBuilder, p, rng):
    """A shield-shaped emblem with a gem."""
    pts = [(-0.11, 0.2), (0.11, 0.2), (0.12, 0.08), (0.0, -0.04), (-0.12, 0.08)]
    mb.extrude_polygon(pts, 0.025, "prop:gold", "#ffffff", matrix=Matrix.Translation((0, 0.0125, 0)) @ Matrix.Rotation(math.pi / 2, 4, "X"))
    mb.sphere((0, 0.03, 0.11), 0.04, "prop:crystal:glow", p.get("gem", "#ff4040"), subdivisions=1, scale=(1, 0.5, 1))


GENERATORS = {
    "doll": doll, "sigil": sigil,
    "tome": tome,
    "sword": sword, "shortsword": shortsword, "pickaxe": pickaxe, "axe": axe, "hammer": hammer, "bow": bow,
    "wand": wand, "staff": staff, "arrow": arrow, "bar": bar, "gem": gem, "blob": blob, "lens": lens, "spool": spool,
    "potion": potion, "coin": coin, "star": star, "heart": heart, "seed": seed, "chunk": chunk, "helmet": helmet,
    "chest_armor": chest_armor, "greaves": greaves, "boots": boots, "horseshoe": horseshoe, "ring": ring,
    "bottled": bottled, "bucket": bucket, "feather": feather, "spike": spike, "bone": bone, "essence": essence,
}

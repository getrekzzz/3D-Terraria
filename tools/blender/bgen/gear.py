"""More hand-held items: grappling hooks, wings, shields, balloons, guns and bullets, explosives, fishing rods, mirrors,
crates, fish, keys and glowing orbs. Same conventions as items.py: origin at the grip, business end along +Z; colours
through ``color``/``metal``/``wood`` parameters."""
import math

from mathutils import Matrix, Vector

from .guns import gun  # noqa: F401  (registered below)
from .mesh import MeshBuilder


def _metal(p, default="#c0c4cc"):
    return f"prop:{p.get('metalLayer', 'iron')}", p.get("metal", default)


def hook(mb: MeshBuilder, p, rng):
    """A three-pronged grappling claw with a coil of chain below it."""
    layer, tint = _metal(p, "#b8bcc4")
    claw = p.get("claw", tint)
    mb.cylinder((0, 0, 0.14), (0, 0, 0.3), 0.018, 0.012, layer, claw, segments=6)
    for k in range(3):
        a = k * 2 * math.pi / 3
        d = Vector((math.cos(a), math.sin(a), 0))
        pts = [Vector((0, 0, 0.3)), d * 0.05 + Vector((0, 0, 0.33)), d * 0.09 + Vector((0, 0, 0.29)), d * 0.08 + Vector((0, 0, 0.24))]
        mb.tube(pts, [0.014, 0.012, 0.01, 0.004], layer, claw, segments=5)
    if p.get("gem"):
        mb.sphere((0, 0, 0.3), 0.03, "prop:crystal:glow", p["gem"], subdivisions=1)
    coil = [Vector((math.cos(a) * 0.07, math.sin(a) * 0.07, 0.02 + a * 0.012)) for a in (i * 0.35 for i in range(28))]
    mb.tube(coil, [0.008] * len(coil), layer, p.get("chain", tint), segments=4)


def wings(mb: MeshBuilder, p, rng):
    """A pair of wings: feathered (layered vanes), membrane (bat), leafy or glowing energy blades."""
    style, color = p.get("style", "feather"), p.get("color", "#f4f0e8")
    glow = ":glow" if p.get("glow") else ""
    layer = "prop:plain" + glow if style in ("energy", "insect") else ("prop:cloth" if style != "leaf" else "prop:plain")
    for sx in (-1, 1):
        base = Matrix.Translation((sx * 0.03, 0, 0.12)) @ Matrix.Rotation(-sx * 0.3, 4, "Y")  # swept up and out
        if style == "membrane":
            pts = [(0, 0), (0.28, 0.1), (0.36, 0.3), (0.26, 0.22), (0.2, 0.3), (0.12, 0.2), (0.06, 0.26), (0.0, 0.12)]
        elif style == "insect":
            pts = [(0, 0), (0.18, 0.05), (0.32, 0.22), (0.24, 0.34), (0.1, 0.26), (0.0, 0.12)]
        else:
            pts = [(0, 0), (0.12, -0.02), (0.3, 0.06), (0.38, 0.22), (0.3, 0.26), (0.22, 0.3), (0.1, 0.26), (0.0, 0.14)]
        mirror = Matrix.Scale(-1, 4, (1, 0, 0)) if sx < 0 else Matrix.Identity(4)
        mb.extrude_polygon(pts, 0.012, layer, color, matrix=base @ mirror @ Matrix.Rotation(math.pi / 2, 4, "X"))
        if style == "feather":
            for i in range(4):
                t = 0.3 + i * 0.17
                tip = Vector((sx * (0.06 + 0.3 * t), 0.004, 0.12 + 0.2 * t))
                mb.tube([tip - Vector((0, 0, 0.1)), tip + Vector((sx * 0.05, 0, 0.02))], [0.012, 0.002], "prop:cloth", p.get("color2", color), segments=4)
    mb.box((-0.04, -0.02, 0.08), (0.04, 0.02, 0.16), "prop:leather" if style != "energy" else "prop:plain" + glow, p.get("strap", "#5a3a24"))


def shield(mb: MeshBuilder, p, rng):
    """A heater shield with a raised boss or an eye emblem."""
    layer, tint = _metal(p, p.get("color", "#8a5a3a"))
    pts = [(-0.14, 0.32), (0.14, 0.32), (0.15, 0.14), (0.0, -0.04), (-0.15, 0.14)]
    mb.extrude_polygon(pts, 0.03, layer, tint, matrix=Matrix.Translation((0, 0.015, 0)) @ Matrix.Rotation(math.pi / 2, 4, "X"))
    mb.sphere((0, 0.035, 0.16), 0.05, "prop:plain" + (":glow" if p.get("glow") else ""), p.get("emblem", "#e8e0d0"), subdivisions=2, scale=(1, 0.45, 1))
    if p.get("pupil"):
        mb.sphere((0, 0.055, 0.16), 0.022, "prop:plain", p["pupil"], subdivisions=1, scale=(1, 0.4, 1))


def balloon(mb: MeshBuilder, p, rng):
    """A balloon on a string."""
    mb.cylinder((0, 0, 0), (0, 0, 0.18), 0.003, None, "prop:plain", "#e8e8e8", segments=3)
    mb.sphere((0, 0, 0.28), 0.1, "prop:gel", p.get("color", "#e83040"), subdivisions=2, scale=(1, 1, 1.15))


def bullet(mb: MeshBuilder, p, rng):
    """A handful of cartridges."""
    color = p.get("color", "#c89a4a")
    for i, (x, y) in enumerate(((0, 0), (0.05, 0.02), (-0.04, 0.03))):
        mb.cylinder((x, y, 0.0), (x, y, 0.08), 0.014, None, "prop:gold", "#d8b060", segments=6)
        mb.cylinder((x, y, 0.08), (x, y, 0.12), 0.014, 0.004, "prop:plain" + (":glow" if p.get("glow") else ""), color, segments=6)


def bomb(mb: MeshBuilder, p, rng):
    """Explosives: a round bomb with a fuse, bundled dynamite or a grenade."""
    style = p.get("style", "bomb")
    if style == "dynamite":
        for x in (-0.03, 0.0, 0.03):
            mb.cylinder((x, 0, 0.0), (x, 0, 0.22), 0.024, None, "prop:red_cloth", "#c02a20", segments=8)
        mb.box((-0.06, -0.026, 0.08), (0.06, 0.026, 0.11), "prop:leather", "#4a3a2a")
        fuse = [Vector((0, 0, 0.22)), Vector((0.02, 0, 0.28)), Vector((0.05, 0, 0.3))]
    elif style == "grenade":
        mb.sphere((0, 0, 0.09), 0.08, "prop:iron", "#5a6a4a", subdivisions=2, scale=(1, 1, 1.2))
        mb.box((-0.02, -0.02, 0.17), (0.02, 0.02, 0.21), "prop:iron", "#8a8a8a")
        fuse = [Vector((0.02, 0, 0.2)), Vector((0.07, 0, 0.16))]
    else:
        mb.sphere((0, 0, 0.1), 0.1, "prop:iron", p.get("color", "#303038"), subdivisions=2)
        mb.cylinder((0, 0, 0.18), (0, 0, 0.21), 0.025, None, "prop:iron", "#6a6a70", segments=6)
        fuse = [Vector((0, 0, 0.21)), Vector((0.02, 0, 0.26)), Vector((0.05, 0, 0.28))]
    mb.tube(fuse, [0.006] * len(fuse), "prop:plain", "#d8c89a", segments=4)
    mb.sphere(tuple(fuse[-1]), 0.012, "prop:plain:glow", "#ffb040", subdivisions=1)


def rod(mb: MeshBuilder, p, rng):
    """A fishing rod: tapered pole, reel, line and a bobber."""
    color = p.get("color", "#6a4428")
    mb.cylinder((0, 0, -0.1), (0, 0, 0.9), 0.015, 0.005, "prop:planks" if not p.get("metal") else "prop:iron", p.get("metal", color), segments=6)
    mb.cylinder((-0.03, 0, 0.05), (0.03, 0, 0.05), 0.03, None, "prop:iron", "#9098a0", segments=10)
    line = [Vector((0, 0, 0.9)), Vector((0.04, 0, 0.7)), Vector((0.06, 0, 0.5))]
    mb.tube(line, [0.0015] * 3, "prop:plain", "#e8e8e8", segments=3)
    mb.sphere((0.06, 0, 0.48), 0.018, "prop:plain", p.get("bobber", "#e83030"), subdivisions=1)
    mb.socket("tip", (0, 0, 0.9))


def mirror(mb: MeshBuilder, p, rng):
    """A hand mirror: framed glass disc on a handle."""
    layer, tint = _metal(p, "#d8b860")
    mb.cylinder((0, 0, -0.02), (0, 0, 0.12), 0.015, None, layer, tint, segments=6)
    ring = [Vector((math.cos(a) * 0.08, 0, 0.2 + math.sin(a) * 0.08)) for a in (i * 2 * math.pi / 20 for i in range(21))]
    mb.tube(ring, [0.012] * len(ring), layer, tint, segments=5)
    mb.cylinder((0, -0.004, 0.2), (0, 0.004, 0.2), 0.072, None, "prop:crystal:glow", p.get("glass", "#a8d8ff"), segments=16)


def orb(mb: MeshBuilder, p, rng):
    """A glowing orb on a small stand (crystal balls, boss summons, power cells)."""
    if p.get("stand", True):
        mb.cylinder((0, 0, 0), (0, 0, 0.04), 0.07, 0.05, "prop:gold" if not p.get("standColor") else "prop:iron", p.get("standColor", "#ffffff"), segments=10)
    mb.sphere((0, 0, 0.12), p.get("size", 0.08), "prop:crystal:glow", p.get("color", "#8ad0ff"), subdivisions=2)


def crate(mb: MeshBuilder, p, rng):
    """A small bound crate (fishing crates)."""
    mb.box((-0.12, -0.12, 0.0), (0.12, 0.12, 0.22), "prop:planks", p.get("color", "#ffffff"))
    for z in (0.02, 0.2):
        mb.box((-0.125, -0.125, z - 0.015), (0.125, 0.125, z + 0.015), "prop:" + p.get("band", "iron"), "#ffffff")


def fish(mb: MeshBuilder, p, rng):
    """A fish: tapered body, tail fin and an eye."""
    color = p.get("color", "#8aa0b8")
    mb.sphere((0, 0, 0.12), 0.05, "prop:gel", color, subdivisions=2, scale=(0.5, 1, 2.4))
    tail = [(0, 0), (0.06, -0.06), (0.06, 0.06)]
    mb.extrude_polygon(tail, 0.01, "prop:gel", p.get("fin", color), matrix=Matrix.Translation((0, 0, 0.0)) @ Matrix.Rotation(-math.pi / 2, 4, "Y"))
    mb.sphere((0.02, 0.02, 0.2), 0.008, "prop:plain", "#101010", subdivisions=1)


def key(mb: MeshBuilder, p, rng):
    """An ornate key."""
    layer, tint = _metal(p, "#d8b860")
    ring = [Vector((math.cos(a) * 0.05, 0, 0.26 + math.sin(a) * 0.05)) for a in (i * 2 * math.pi / 14 for i in range(15))]
    mb.tube(ring, [0.012] * len(ring), layer, tint, segments=5)
    mb.cylinder((0, 0, 0.0), (0, 0, 0.21), 0.013, None, layer, tint, segments=6)
    mb.box((0.0, -0.01, 0.02), (0.05, 0.01, 0.05), layer, tint)
    mb.box((0.0, -0.01, 0.07), (0.035, 0.01, 0.09), layer, tint)


def whip(mb: MeshBuilder, p, rng):
    """A coiled whip with a handle; ``pose: s_curve`` (its icon) lays the lash out as an S-curve instead."""
    mb.cylinder((0, 0, -0.1), (0, 0, 0.08), 0.02, None, "prop:dark_planks", p.get("handle", "#5a3a24"), segments=6)
    if p.get("pose") == "s_curve":
        lash = [Vector((math.sin(t * 2 * math.pi) * 0.14 * t, 0, 0.08 + t * 0.9)) for t in (i / 30 for i in range(31))]
        mb.tube(lash, [0.016 - i * 0.00042 for i in range(31)], "prop:leather", p.get("color", "#6a4a2a"), segments=5)
        mb.sphere(tuple(lash[-1]), 0.012, "prop:leather", p.get("color", "#6a4a2a"), subdivisions=1)
    else:
        coil = [Vector((math.cos(a) * (0.1 - a * 0.004), math.sin(a) * (0.1 - a * 0.004), 0.1 + a * 0.01)) for a in (i * 0.4 for i in range(40))]
        mb.tube(coil, [0.01 - i * 0.0002 for i in range(40)], "prop:leather", p.get("color", "#6a4a2a"), segments=4)
    mb.socket("handle_end", (0, 0, 0.08))


def boomerang(mb: MeshBuilder, p, rng):
    """A curved throwing stick (a flat chevron), optionally glowing."""
    color = p.get("color", "#a8703a")
    layer = "prop:plain:glow" if p.get("glow") else ("prop:planks" if p.get("wood", True) else "prop:iron")
    pts = [(-0.2, 0.0), (0.0, 0.2), (0.2, 0.0), (0.15, -0.02), (0.0, 0.13), (-0.15, -0.02)]
    mb.extrude_polygon(pts, 0.025, layer, color, matrix=Matrix.Translation((0, 0.012, 0.05)) @ Matrix.Rotation(math.pi / 2, 4, "X"))


def yoyo(mb: MeshBuilder, p, rng):
    """Two discs on an axle with a coil of string; ``head_only`` (the thrown projectile) centres the discs on the
    origin without the string."""
    color = p.get("color", "#b0703a")
    z = 0.0 if p.get("head_only") else 0.12
    layer = "prop:plain:glow" if p.get("glow") else "prop:plain"
    for y in (-0.03, 0.03):
        mb.cylinder((0, y, z), (0, y + (0.02 if y > 0 else -0.02), z), 0.09, 0.08, layer, color, segments=14)
    mb.cylinder((0, -0.03, z), (0, 0.03, z), 0.02, None, "prop:plain", p.get("axle", "#e8e8e8"), segments=8)
    mb.socket("string", (0, 0, z))
    if p.get("head_only"):
        return
    string = [Vector((0, 0, 0.12)), Vector((0.02, 0, 0.0)), Vector((0.0, 0, -0.1))]
    mb.tube(string, [0.003] * 3, "prop:plain", "#f0f0f0", segments=3)


def flail(mb: MeshBuilder, p, rng):
    """A handle, a short chain and a spiked (or glowing) ball."""
    layer, tint = _metal(p, "#9098a0")
    centre = Vector((0, 0, 0)) if p.get("head_only") else Vector((0.02, 0, 0.36))
    if p.get("head_only"):  # the thrown ball: centred on the origin, its chain attaches at the bottom
        mb.socket("chain", (0, 0, -0.08), (0, 0, -1))
    else:
        mb.cylinder((0, 0, -0.1), (0, 0, 0.08), 0.02, None, "prop:dark_planks", "#5a3a24", segments=6)
        chain = [Vector((0, 0, 0.08)), Vector((0.03, 0, 0.18)), Vector((0.02, 0, 0.28))]
        mb.tube(chain, [0.008] * 3, "prop:iron", "#8a8e96", segments=4)
        mb.socket("chain", (0, 0, 0.08))
    ball = "prop:plain:glow" if p.get("glow") else layer
    mb.sphere(tuple(centre), 0.08, ball, p.get("ball", tint), subdivisions=2)
    for k in range(6):
        d = [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)][k]
        base = centre + Vector(d) * 0.07
        mb.cylinder(tuple(base), tuple(base + Vector(d) * 0.05), 0.02, 0.002, layer, tint, segments=5)


def spear(mb: MeshBuilder, p, rng):
    """A long shaft with a leaf-shaped head (tridents get three prongs)."""
    layer, tint = _metal(p, "#b8bcc4")
    length = p.get("length", 1.1)
    mb.cylinder((0, 0, -0.15), (0, 0, length), 0.018, 0.016, "prop:planks" if not p.get("darkShaft") else "prop:dark_planks", p.get("shaft", "#ffffff"), segments=7)
    if p.get("prongs"):
        for x in (-0.05, 0.0, 0.05):
            mb.cylinder((x, 0, length - 0.02), (x, 0, length + (0.2 if x == 0 else 0.16)), 0.01, 0.002, layer, tint, segments=5)
        mb.box((-0.06, -0.01, length - 0.04), (0.06, 0.01, length), layer, tint)
    else:
        mb.sphere((0, 0, length + 0.1), 0.04, layer, tint, subdivisions=2, scale=(1, 0.3, 3))
    if p.get("glow"):
        mb.sphere((0, 0, length), 0.025, "prop:plain:glow", p["glow"], subdivisions=1)


def shuriken(mb: MeshBuilder, p, rng):
    """A four-pointed throwing star."""
    pts = []
    for k in range(8):
        a = k * math.pi / 4
        r = 0.1 if k % 2 == 0 else 0.03
        pts.append((math.cos(a) * r, math.sin(a) * r))
    mb.extrude_polygon(pts, 0.01, "prop:iron", p.get("color", "#b8bcc4"), matrix=Matrix.Translation((0, 0.005, 0.1)) @ Matrix.Rotation(math.pi / 2, 4, "X"))


def knife(mb: MeshBuilder, p, rng):
    """A small throwing knife."""
    mb.cylinder((0, 0, -0.06), (0, 0, 0.04), 0.012, None, "prop:leather", "#4a3020", segments=6)
    mb.extrude_polygon([(-0.018, 0.04), (0.018, 0.04), (0.0, 0.2)], 0.006, "prop:iron", p.get("color", "#c8ccd4"),
                       matrix=Matrix.Translation((0, 0.003, 0)) @ Matrix.Rotation(math.pi / 2, 4, "X"))


def drill(mb: MeshBuilder, p, rng):
    """A power drill: motor housing with a handle, a fluted metal bit pointing along +Z."""
    layer, tint = _metal(p)
    mb.box((-0.06, -0.08, -0.12), (0.06, 0.06, 0.2), "prop:iron", "#4a4e56")
    mb.cylinder((0, -0.02, -0.1), (0, -0.14, -0.2), 0.03, None, "prop:dark_planks", "#3a2a1a", segments=6)
    mb.cylinder((0, 0, 0.2), (0, 0, 0.3), 0.07, 0.06, layer, tint, segments=10)
    mb.cylinder((0, 0, 0.3), (0, 0, 0.62), 0.06, 0.0, layer, tint, segments=8, smooth=False)
    spiral = [Vector((math.cos(a) * 0.05 * (1 - a / 12), math.sin(a) * 0.05 * (1 - a / 12), 0.3 + a * 0.026)) for a in (i * 0.5 for i in range(24))]
    mb.tube(spiral, [0.012] * len(spiral), layer, tint, segments=4)


def chainsaw(mb: MeshBuilder, p, rng):
    """A chainsaw: engine block, handle and a long toothed bar along +Z."""
    layer, tint = _metal(p)
    mb.box((-0.07, -0.08, -0.15), (0.07, 0.08, 0.15), "prop:iron", "#4a4e56")
    mb.box((-0.06, 0.08, -0.12), (0.06, 0.1, 0.12), layer, tint)
    mb.box((-0.015, -0.05, 0.15), (0.015, 0.05, 0.7), layer, tint)
    for k in range(10):
        z = 0.18 + k * 0.05
        for y in (-0.055, 0.055):
            mb.box((-0.01, y - 0.01, z), (0.01, y + 0.01, z + 0.02), "prop:iron", "#c8ccd4")


GENERATORS = {
    "drill": drill, "chainsaw": chainsaw,
    "boomerang": boomerang, "yoyo": yoyo, "flail": flail, "spear": spear, "shuriken": shuriken, "knife": knife,
    "hook": hook, "wings": wings, "shield": shield, "balloon": balloon, "gun": gun, "bullet": bullet, "bomb": bomb,
    "rod": rod, "mirror": mirror, "orb": orb, "crate": crate, "fish": fish, "key": key, "whip": whip,
}

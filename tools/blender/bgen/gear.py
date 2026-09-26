"""More hand-held items: grappling hooks, wings, shields, balloons, guns and bullets, explosives, fishing rods, mirrors,
crates, fish, keys and glowing orbs. Same conventions as items.py: origin at the grip, business end along +Z; colours
through ``color``/``metal``/``wood`` parameters."""
import math

from mathutils import Matrix, Vector

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


def gun(mb: MeshBuilder, p, rng):
    """Firearms: pistol, musket/rifle, shotgun, minigun (rotating barrels) or launcher (a wide tube)."""
    style = p.get("style", "pistol")
    layer, tint = _metal(p, "#6a6e78")
    wood = p.get("wood", "#6a4428")
    length = {"pistol": 0.3, "musket": 0.9, "rifle": 0.85, "shotgun": 0.7, "minigun": 0.75, "launcher": 0.8}.get(style, 0.5)
    mb.box((-0.025, -0.06, -0.08), (0.025, 0.02, 0.04), "prop:dark_planks", wood)  # grip
    if style in ("musket", "rifle", "shotgun"):
        mb.box((-0.03, -0.05, -0.28), (0.03, 0.04, 0.05), "prop:planks", wood)  # stock
    if style == "minigun":
        for k in range(6):
            a = k * math.pi / 3
            mb.cylinder((math.cos(a) * 0.025, 0.04 + math.sin(a) * 0.025, 0.0), (math.cos(a) * 0.025, 0.04 + math.sin(a) * 0.025, length), 0.01, None, layer, tint, segments=5)
        mb.cylinder((0, 0.04, -0.05), (0, 0.04, 0.12), 0.05, None, layer, tint, segments=10)
    elif style == "launcher":
        mb.cylinder((0, 0.05, -0.2), (0, 0.05, length), 0.06, 0.065, layer, tint, segments=12)
    else:
        mb.box((-0.03, 0.0, 0.0), (0.03, 0.07, 0.12), layer, tint)  # receiver
        mb.cylinder((0, 0.045, 0.1), (0, 0.045, length), 0.018 if style != "shotgun" else 0.024, None, layer, tint, segments=8)
    if p.get("accent"):
        mb.box((-0.035, 0.07, 0.02), (0.035, 0.085, length * 0.6), "prop:plain:glow" if p.get("glow") else "prop:plain", p["accent"])


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
    """A coiled whip with a handle."""
    mb.cylinder((0, 0, -0.1), (0, 0, 0.08), 0.02, None, "prop:dark_planks", p.get("handle", "#5a3a24"), segments=6)
    coil = [Vector((math.cos(a) * (0.1 - a * 0.004), math.sin(a) * (0.1 - a * 0.004), 0.1 + a * 0.01)) for a in (i * 0.4 for i in range(40))]
    mb.tube(coil, [0.01 - i * 0.0002 for i in range(40)], "prop:leather", p.get("color", "#6a4a2a"), segments=4)


GENERATORS = {
    "hook": hook, "wings": wings, "shield": shield, "balloon": balloon, "gun": gun, "bullet": bullet, "bomb": bomb,
    "rod": rod, "mirror": mirror, "orb": orb, "crate": crate, "fish": fish, "key": key, "whip": whip,
}

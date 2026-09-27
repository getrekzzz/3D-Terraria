"""Event creatures: the moons' haunted trees and Pumpking, the Frost Legion's snowmen, Santa-NK1, the Present Mimic, the
Flying Dutchman, the Martians' saucer, walker and tesla turret, and Betsy. Creatures: +Y forward, origin at the bottom of
the hitbox, sized in metres."""
import math

from mathutils import Matrix, Vector

from .mesh import MeshBuilder


def haunted_tree(mb: MeshBuilder, p, rng):
    """A walking tree with a screaming face (Mourning Wood: a gnarled oak; Everscream: a decorated pine). ~5 m tall."""
    bark, glow, style = p.get("bark", "#5a3a22"), p.get("glow", "#ff8a2a"), p.get("style", "oak")
    for sx in (-1, 1):  # root legs
        mb.tube([Vector((sx * 0.35, 0, 1.2)), Vector((sx * 0.8, 0.2, 0.5)), Vector((sx * 1.0, 0.4, 0.0))], [0.32, 0.24, 0.12], "prop:bark", bark, segments=7)
    mb.cylinder((0, 0, 0.9), (0, 0, 4.2), 0.75, 0.5, "prop:bark", bark, segments=10)
    for sx in (-1, 1):  # glowing eye holes and a howling mouth
        mb.sphere((sx * 0.28, 0.62, 3.2), 0.16, "prop:plain:glow", glow, subdivisions=1, scale=(1, 0.4, 0.8))
    mb.sphere((0, 0.65, 2.5), 0.22, "prop:plain:glow", glow, subdivisions=1, scale=(1.2, 0.4, 1.6))
    if style == "pine":
        green = p.get("leaves", "#2a6a3a")
        for k in range(4):
            z = 3.4 + k * 0.55
            mb.cylinder((0, 0, z), (0, 0, z + 0.9), 1.6 - k * 0.35, 0.1, "prop:leaves", green, segments=9, smooth=False)
        for k in range(10):  # ornaments
            a, z = rng.random() * math.tau, 3.5 + rng.random() * 1.6
            r = 1.45 - (z - 3.4) * 0.55
            mb.sphere((math.cos(a) * r, math.sin(a) * r, z), 0.12, "prop:plain:glow", ("#e84a4a", "#ffd040", "#4a8aff")[k % 3], subdivisions=1)
        mb.sphere((0, 0, 5.8), 0.25, "prop:plain:glow", "#ffe060", subdivisions=1)
    else:
        for k in range(5):  # clawing branches
            a = k * math.tau / 5 + 0.3
            d = Vector((math.cos(a), math.sin(a), 0))
            base = Vector((0, 0, 3.8 + (k % 2) * 0.3))
            mb.tube([base, base + d * 0.9 + Vector((0, 0, 0.6)), base + d * 1.6 + Vector((0, 0, 0.5)), base + d * 1.9 + Vector((0, 0, 1.0))],
                    [0.2, 0.14, 0.08, 0.02], "prop:bark", bark, segments=5)
    return None


def pumpkin_lord(mb: MeshBuilder, p, rng):
    """Pumpking: a grinning jack-o'-lantern head in a tattered cloak, two floating hands with scythes (hitbox 2.5 m)."""
    orange, glow = p.get("color", "#e8742a"), p.get("glow", "#ffd040")
    c = Vector((0, 0, 1.6))
    for k in range(8):  # ribbed pumpkin
        a = k * math.tau / 8
        mb.sphere(tuple(c + Vector((math.cos(a) * 0.35, math.sin(a) * 0.35, 0))), 0.75, "prop:flesh", orange, subdivisions=2, scale=(0.8, 0.8, 0.95))
    for sx in (-1, 1):
        mb.sphere((sx * 0.4, 0.95, 1.85), 0.2, "prop:plain:glow", glow, subdivisions=1, scale=(1, 0.3, 0.8))
    for k in range(5):  # jagged grin
        x = (k - 2) * 0.22
        mb.box((x - 0.08, 0.92, 1.2 - abs(k - 2) * 0.05), (x + 0.08, 1.02, 1.38), "prop:plain:glow", glow)
    mb.cylinder((0, 0, 2.55), (0.1, 0, 2.95), 0.12, 0.06, "prop:bark", "#3a5a1a", segments=6)
    mb.cylinder((0, -0.2, 0.2), (0, -0.1, 1.2), 0.9, 0.5, "prop:cloth", "#1a1a2a", segments=10)  # cloak
    for sx in (-1, 1):
        hand = Vector((sx * 1.7, 0.6, 1.2))
        mb.sphere(tuple(hand), 0.28, "prop:flesh", "#3a5a1a", subdivisions=1)
        mb.cylinder(tuple(hand + Vector((0, 0, -0.8))), tuple(hand + Vector((0, 0, 1.4))), 0.05, 0.05, "prop:planks", "#3a2a1a", segments=6)
        top = hand + Vector((0, 0, 1.4))
        mb.tube([top, top + Vector((sx * 0.5, 0.2, 0.1)), top + Vector((sx * 0.9, 0.5, -0.3))], [0.1, 0.07, 0.01], "prop:plain:glow", "#ff6a2a", segments=4)
    return None


def snowman(mb: MeshBuilder, p, rng):
    """A Frost Legion snowman (style: stabby with a knife, gangsta with a gun and hat, balla with a snowball). ~1.5 m."""
    snow, style = p.get("snow", "#f4f8ff"), p.get("style", "stabby")
    for z, r in ((0.35, 0.4), (0.9, 0.3), (1.32, 0.22)):
        mb.sphere((0, 0, z), r, "prop:gel", snow, subdivisions=2)
    for sx in (-1, 1):
        mb.sphere((sx * 0.08, 0.2, 1.38), 0.03, "prop:plain", "#101010", subdivisions=1)
    mb.cylinder((0, 0.2, 1.3), (0, 0.42, 1.3), 0.04, 0.0, "prop:plain", "#ff8a2a", segments=6)
    for sx in (-1, 1):
        mb.tube([Vector((sx * 0.28, 0, 0.95)), Vector((sx * 0.55, 0.1, 1.05)), Vector((sx * 0.7, 0.25, 1.0))], [0.03, 0.02, 0.01], "prop:bark", "#5a3a22", segments=4)
    if style == "gangsta":
        mb.cylinder((0, 0, 1.5), (0, 0, 1.52), 0.3, 0.3, "prop:cloth", "#1a1a1a", segments=10)
        mb.cylinder((0, 0, 1.52), (0, 0, 1.72), 0.18, 0.17, "prop:cloth", "#1a1a1a", segments=10)
        mb.box((0.55, 0.2, 0.95), (0.62, 0.7, 1.05), "prop:iron", "#3a3a3a")
    elif style == "balla":
        mb.sphere((0.72, 0.3, 1.05), 0.12, "prop:gel", snow, subdivisions=1)
        mb.cylinder((0, 0, 1.5), (0, 0, 1.62), 0.2, 0.18, "prop:cloth", "#c83a3a", segments=10)
    else:
        mb.box((0.66, 0.25, 0.98), (0.7, 0.55, 1.02), "prop:iron", "#c8ccd4")
        mb.cylinder((0, 0, 1.48), (0, 0, 1.56), 0.22, 0.2, "prop:cloth", "#3a3a8a", segments=10)
    return None


def tank(mb: MeshBuilder, p, rng):
    """Santa-NK1: a treaded war machine with a gun turret and a Santa hat (hitbox 3 m)."""
    red, metal = p.get("color", "#c82a2a"), p.get("metal", "#5a5e66")
    for sx in (-1, 1):
        mb.box((sx * 1.4 - 0.3, -1.2, 0), (sx * 1.4 + 0.3, 1.2, 0.7), "prop:iron", "#2a2a2a")  # treads
    mb.box((-1.15, -1.0, 0.4), (1.15, 1.0, 1.5), "prop:iron", red)
    mb.cylinder((0, 0, 1.5), (0, 0, 2.2), 0.85, 0.7, "prop:iron", metal, segments=10)
    for sx in (-1, 1):
        mb.cylinder((sx * 0.3, 0.5, 1.85), (sx * 0.3, 2.2, 1.85), 0.1, 0.08, "prop:iron", "#2a2a2a", segments=8)
    mb.sphere((0, 0.72, 2.0), 0.18, "prop:plain:glow", "#ff3a3a", subdivisions=1, scale=(1.5, 0.4, 0.6))
    mb.cylinder((0, 0, 2.2), (0.3, -0.2, 3.1), 0.6, 0.08, "prop:cloth", red, segments=10)
    mb.sphere((0.3, -0.2, 3.12), 0.14, "prop:cloth", "#ffffff", subdivisions=1)
    mb.cylinder((0, 0, 2.18), (0, 0, 2.32), 0.66, 0.66, "prop:cloth", "#ffffff", segments=12)
    return None


def present(mb: MeshBuilder, p, rng):
    """The Present Mimic: a wrapped gift box with a ribbon, a toothy lid and stubby legs (hitbox 1 m)."""
    wrap, ribbon = p.get("color", "#3a8a3a"), p.get("ribbon", "#e84a4a")
    mb.box((-0.45, -0.45, 0.15), (0.45, 0.45, 0.85), "prop:cloth", wrap)
    mb.box((-0.5, -0.5, 0.85), (0.5, 0.5, 1.0), "prop:cloth", wrap)
    mb.box((-0.07, -0.51, 0.15), (0.07, 0.51, 1.01), "prop:cloth", ribbon)
    mb.box((-0.51, -0.07, 0.15), (0.51, 0.07, 1.01), "prop:cloth", ribbon)
    for sx in (-1, 1):
        mb.sphere((sx * 0.18, 0, 1.08), 0.14, "prop:cloth", ribbon, subdivisions=1, scale=(1.4, 0.6, 0.8))
    for k in range(6):
        x = (k - 2.5) * 0.14
        mb.cylinder((x, 0.46, 0.85), (x, 0.5, 0.72), 0.04, 0.0, "prop:plain", "#f0ece0", segments=4, smooth=False)
    for sx in (-1, 1):
        for sy in (-1, 1):
            mb.cylinder((sx * 0.3, sy * 0.3, 0.15), (sx * 0.34, sy * 0.34, 0.0), 0.07, 0.05, "prop:flesh", "#8a5a3a", segments=6)
    return None


def ghost_ship(mb: MeshBuilder, p, rng):
    """The Flying Dutchman: a rotting galleon with tattered sails and rows of cannons (about 8 m long). It sails broadside
    on: the hull runs along X and its cannons point along +Y, at whatever it faces."""
    wood, sail = p.get("wood", "#4a3a2a"), p.get("sail", "#c8c0a0")
    mb.box((-3.6, -0.9, 0.0), (2.8, 0.9, 0.7), "prop:planks", "#3a2a1a")  # keel
    mb.box((-4.0, -1.3, 0.6), (3.2, 1.3, 1.7), "prop:planks", wood)
    mb.cylinder((3.2, 0, 0.9), (4.6, 0, 1.6), 1.0, 0.2, "prop:planks", wood, segments=8)  # bow
    mb.box((-4.6, -1.3, 1.2), (-3.2, 1.3, 3.0), "prop:planks", wood)  # stern castle
    for x, h in ((1.6, 6.5), (-1.2, 7.2)):
        mb.cylinder((x, 0, 1.7), (x, 0, h), 0.12, 0.09, "prop:planks", "#3a2a1a", segments=6)
        for k in range(2):
            z = 3.0 + k * 1.8
            mb.box((x - 0.05, -1.5 + rng.random() * 0.2, z), (x + 0.05, 1.5, z + 1.4), "prop:cloth", sail)
    for sy in (-1, 1):
        for k in range(4):
            x = -2.6 + k * 1.5
            mb.cylinder((x, sy * 1.2, 1.2), (x, sy * 1.7, 1.2), 0.14, 0.12, "prop:iron", "#2a2a2a", segments=8)
    mb.sphere((-4.0, 0, 3.2), 0.25, "prop:plain:glow", "#9aff9a", subdivisions=1)  # a ghostly lantern
    return None


def saucer(mb: MeshBuilder, p, rng):
    """The Martian Saucer: a wide disc with a glass dome, a ring of lights and a deathray emitter below (6 m across)."""
    metal, glow = p.get("metal", "#8a9aa8"), p.get("glow", "#4aff6a")
    mb.lathe([(0.0, 0.2), (2.0, 0.4), (3.0, 0.75), (2.6, 1.0), (1.2, 1.2), (0.0, 1.25)], "prop:iron", metal, segments=24)
    mb.sphere((0, 0, 1.25), 0.9, "prop:crystal", "#9ad8ff", subdivisions=2, scale=(1, 1, 0.7))
    mb.sphere((0, 0, 1.2), 0.35, "prop:flesh", "#6ac06a", subdivisions=1)  # the pilot
    for k in range(12):
        a = k * math.tau / 12
        mb.sphere((math.cos(a) * 2.7, math.sin(a) * 2.7, 0.78), 0.12, "prop:plain:glow", glow if k % 2 else "#ffe060", subdivisions=1)
    mb.cylinder((0, 0, 0.25), (0, 0, -0.2), 0.45, 0.2, "prop:iron", "#4a4e56", segments=10)
    mb.sphere((0, 0, -0.25), 0.2, "prop:plain:glow", glow, subdivisions=1)
    return None


def tesla_turret(mb: MeshBuilder, p, rng):
    """A Martian tesla turret: a tripod base with a coiled mast and a crackling orb (~1.2 m)."""
    metal, glow = p.get("metal", "#6a7a8a"), p.get("glow", "#8ae0ff")
    for k in range(3):
        a = k * math.tau / 3
        mb.tube([Vector((0, 0, 0.35)), Vector((math.cos(a) * 0.35, math.sin(a) * 0.35, 0.1)), Vector((math.cos(a) * 0.4, math.sin(a) * 0.4, 0.0))], [0.05, 0.04, 0.04],
                "prop:iron", metal, segments=5)
    mb.cylinder((0, 0, 0.3), (0, 0, 0.95), 0.1, 0.08, "prop:iron", metal, segments=8)
    for k in range(5):
        mb.cylinder((0, 0, 0.4 + k * 0.11), (0, 0, 0.44 + k * 0.11), 0.16, 0.16, "prop:iron", "#c8a040", segments=10)
    mb.sphere((0, 0, 1.05), 0.16, "prop:plain:glow", glow, subdivisions=2)
    return None


def mech_walker(mb: MeshBuilder, p, rng):
    """A Martian Walker: an armoured pod with a cockpit dome on two long backward-jointed legs (~2.5 m)."""
    metal, glow = p.get("metal", "#7a8a98"), p.get("glow", "#4aff6a")
    for sx in (-1, 1):
        hip, knee, foot = Vector((sx * 0.5, 0, 1.6)), Vector((sx * 0.7, -0.5, 0.9)), Vector((sx * 0.7, 0.2, 0.0))
        mb.tube([hip, knee], [0.14, 0.12], "prop:iron", metal, segments=6)
        mb.tube([knee, foot], [0.12, 0.1], "prop:iron", metal, segments=6)
        mb.box((sx * 0.7 - 0.2, 0.0, 0.0), (sx * 0.7 + 0.2, 0.5, 0.1), "prop:iron", "#4a4e56")
    mb.sphere((0, 0, 1.9), 0.75, "prop:iron", metal, subdivisions=2, scale=(1.1, 1.0, 0.7))
    mb.sphere((0, 0.35, 2.2), 0.35, "prop:crystal", "#9ad8ff", subdivisions=2)
    for sx in (-1, 1):
        mb.cylinder((sx * 0.7, 0.4, 1.8), (sx * 0.7, 1.1, 1.8), 0.07, 0.06, "prop:iron", "#2a2a2a", segments=6)
        mb.sphere((sx * 0.7, 1.12, 1.8), 0.06, "prop:plain:glow", glow, subdivisions=1)
    return None


def dragon(mb: MeshBuilder, p, rng):
    """Betsy: a great wyvern with a long neck, horned head, broad membrane wings and a spiked tail (hitbox ~4 m)."""
    scales, belly, wing = p.get("scales", "#8a2a2a"), p.get("belly", "#e8a060"), p.get("wing", "#6a1a2a")
    mb.sphere((0, 0, 1.3), 1.0, "prop:flesh", scales, subdivisions=2, scale=(0.9, 1.4, 0.8))
    mb.sphere((0, 0.2, 1.05), 0.7, "prop:flesh", belly, subdivisions=2, scale=(0.8, 1.3, 0.6))
    neck = [Vector((0, 1.2, 1.5)), Vector((0, 2.0, 2.2)), Vector((0, 2.6, 2.4))]
    mb.tube(neck, [0.45, 0.35, 0.3], "prop:flesh", scales, segments=8)
    mb.sphere((0, 3.0, 2.45), 0.45, "prop:flesh", scales, subdivisions=2, scale=(0.8, 1.4, 0.7))
    for sx in (-1, 1):
        mb.sphere((sx * 0.22, 3.3, 2.6), 0.08, "prop:plain:glow", "#ffd040", subdivisions=1)
        mb.tube([Vector((sx * 0.2, 2.8, 2.7)), Vector((sx * 0.35, 2.4, 3.1)), Vector((sx * 0.3, 2.0, 3.3))], [0.08, 0.05, 0.01], "prop:plain", "#e8d8b0", segments=4)
        root = Vector((sx * 0.7, 0.2, 1.8))
        tip = Vector((sx * 4.2, -0.6, 2.6))
        mb.tube([root, (root + tip) * 0.5 + Vector((0, 0, 0.8)), tip], [0.14, 0.1, 0.03], "prop:flesh", scales, segments=5)
        mb.extrude_polygon([(sx * 0.7, 0.2), (sx * 2.4, 0.4), (sx * 4.2, -0.6), (sx * 3.0, -1.6), (sx * 1.6, -1.4), (sx * 0.7, -0.8)], 0.04, "prop:gel", wing,
                           matrix=Matrix.Translation((0, 0, 2.0)) @ Matrix.Rotation(-math.pi / 2, 4, "X"))
        for sy in (0.6, -0.6):  # legs
            mb.tube([Vector((sx * 0.5, sy, 0.9)), Vector((sx * 0.6, sy + 0.1, 0.4)), Vector((sx * 0.55, sy + 0.25, 0.0))], [0.18, 0.12, 0.08], "prop:flesh", scales, segments=6)
    tail = [Vector((0, -1.2, 1.2)), Vector((0, -2.2, 1.0)), Vector((0, -3.2, 1.2)), Vector((0, -4.0, 1.5))]
    mb.tube(tail, [0.4, 0.28, 0.16, 0.04], "prop:flesh", scales, segments=7)
    return None


GENERATORS = {"haunted_tree": haunted_tree, "pumpkin_lord": pumpkin_lord, "snowman": snowman, "tank": tank, "present": present, "ghost_ship": ghost_ship,
              "saucer": saucer, "tesla_turret": tesla_turret, "mech_walker": mech_walker, "dragon": dragon}

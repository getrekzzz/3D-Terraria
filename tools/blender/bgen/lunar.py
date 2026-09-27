"""Endgame models: the Celestial Pillars, the Moon Lord (body, head and hands), the True Eye of Cthulhu, the
Mysterious Tablet (creatures: +Y forward, origin at the bottom of the hitbox) and the Ancient Manipulator (tile
object: ``size`` in metres, origin at the floor centre)."""
import math

from mathutils import Vector

from .mesh import MeshBuilder

SKIN, DARK, GLOW = "#9ab4a8", "#3a4a46", "#4affd8"


def celestial_pillar(mb: MeshBuilder, p, rng):
    """A towering monolith on a stepped pedestal with a glowing emblem, ribbed flanks and a crown of its element.
    Sized for a 5 m x 10 m hitbox."""
    color, glow, accent = p.get("color", "#c86a2a"), p.get("glow", "#ffb040"), p.get("accent", "#6a3010")
    style = p.get("style", "solar")
    mb.cylinder((0, 0, 0), (0, 0, 0.5), 2.4, 2.2, "prop:stone", accent, segments=8, smooth=False)
    mb.cylinder((0, 0, 0.5), (0, 0, 1.0), 2.0, 1.8, "prop:stone", color, segments=8, smooth=False)
    mb.cylinder((0, 0, 1.0), (0, 0, 8.6), 1.6, 1.15, "prop:stone", color, segments=8, smooth=False)
    for k in range(8):  # ribs up the flanks
        a = (k + 0.5) * math.tau / 8
        c, s = math.cos(a), math.sin(a)
        mb.tube([Vector((c * 1.62, s * 1.62, 1.1)), Vector((c * 1.45, s * 1.45, 5.0)), Vector((c * 1.2, s * 1.2, 8.5))], [0.16, 0.13, 0.1], "prop:stone", accent, segments=5)
    for y in (1, -1):  # the emblem, front and back
        mb.sphere((0, y * 1.35, 5.2), 0.75, "prop:plain:glow", glow, subdivisions=2, scale=(1, 0.35, 1.25))
        mb.cylinder((0, y * 1.2, 5.2), (0, y * 1.55, 5.2), 1.05, 1.05, "prop:stone", accent, segments=12)
        mb.sphere((0, y * 1.62, 5.2), 0.5, "prop:plain:glow", glow, subdivisions=2, scale=(1, 0.3, 1.2))
    for k in range(4):  # glowing seams
        a = k * math.tau / 4 + math.tau / 16
        mb.box((math.cos(a) * 1.45 - 0.06, math.sin(a) * 1.45 - 0.06, 2.0), (math.cos(a) * 1.45 + 0.06, math.sin(a) * 1.45 + 0.06, 7.8), "prop:plain:glow", glow)
    top = Vector((0, 0, 8.6))
    if style == "solar":  # a flaring sunburst of spikes
        for k in range(10):
            a = k * math.tau / 10
            d = Vector((math.cos(a), math.sin(a), 0))
            mb.cylinder(tuple(top + d * 0.8), tuple(top + d * 2.0 + Vector((0, 0, 1.2 + 0.4 * (k % 2)))), 0.28, 0.0, "prop:stone", color, segments=5, smooth=False)
        mb.sphere(tuple(top + Vector((0, 0, 0.6))), 0.9, "prop:plain:glow", glow, subdivisions=2)
    elif style == "vortex":  # stacked swirling rings
        for k in range(3):
            z, r = 9.0 + k * 0.55, 1.5 - k * 0.35
            pts = [Vector((math.cos(t * math.tau / 10 + k) * r, math.sin(t * math.tau / 10 + k) * r, z + 0.15 * math.sin(t))) for t in range(11)]
            mb.tube(pts, [0.14] * len(pts), "prop:plain:glow", glow, segments=6)
        mb.cylinder(tuple(top), tuple(top + Vector((0, 0, 2.2))), 0.5, 0.05, "prop:stone", color, segments=8)
    elif style == "nebula":  # a floating brain-like orb held by curling horns
        for sx in (-1, 1):
            mb.tube([top + Vector((sx * 0.8, 0, 0)), top + Vector((sx * 1.6, 0, 1.1)), top + Vector((sx * 1.0, 0, 2.2))], [0.3, 0.22, 0.05], "prop:stone", color, segments=6)
        mb.sphere(tuple(top + Vector((0, 0, 1.6))), 1.0, "prop:plain:glow", glow, subdivisions=2, jitter=0.08, rng=rng)
    else:  # stardust: a four-pointed star over a blue spire
        mb.cylinder(tuple(top), tuple(top + Vector((0, 0, 1.4))), 0.8, 0.3, "prop:stone", color, segments=8)
        c = top + Vector((0, 0, 2.4))
        for d in (Vector((1, 0, 0)), Vector((-1, 0, 0)), Vector((0, 0, 1)), Vector((0, 0, -1))):
            mb.cylinder(tuple(c), tuple(c + d * 1.4), 0.35, 0.0, "prop:plain:glow", glow, segments=4, smooth=False)
        mb.sphere(tuple(c), 0.45, "prop:plain:glow", "#ffffff", subdivisions=2)
    for k in range(6):  # shards drifting around it
        a = k * math.tau / 6 + rng.random() * 0.4
        c = Vector((math.cos(a) * 2.6, math.sin(a) * 2.6, 2.0 + k * 1.1))
        mb.cylinder(tuple(c - Vector((0, 0, 0.35))), tuple(c + Vector((0, 0, 0.35))), 0.18, 0.0, "prop:plain:glow", glow, segments=4, smooth=False)
    return None


def moon_lord(mb: MeshBuilder, p, rng):
    """The Moon Lord's body (hitbox 4 m x 5 m, centre 2.5 m up): a gaunt ribbed torso around an exposed glowing heart,
    arms reaching out to where his hands float (8 m to each side, 2 m forward), a neck rising to his head and long legs
    trailing below."""
    skin, dark, glow = p.get("skin", SKIN), p.get("dark", DARK), p.get("glow", GLOW)
    mb.sphere((0, 0, 3.2), 1.9, "prop:flesh", skin, subdivisions=3, scale=(1.05, 0.7, 1.15), jitter=0.02, rng=rng)
    mb.sphere((0, 0, 1.2), 1.3, "prop:flesh", skin, subdivisions=2, scale=(1.0, 0.7, 0.9))
    mb.sphere((0, 1.05, 2.9), 0.75, "prop:plain:glow", glow, subdivisions=2)  # the heart, bared in the chest
    for k in range(5):  # ribs caging it
        z = 2.0 + k * 0.45
        for sx in (-1, 1):
            mb.tube([Vector((sx * 0.25, 1.25, z)), Vector((sx * 1.2, 1.1, z + 0.1)), Vector((sx * 1.85, 0.2, z - 0.1))], [0.1, 0.13, 0.09], "prop:plain", "#d8dcd0", segments=5)
    for sx in (-1, 1):
        shoulder = Vector((sx * 2.0, 0.0, 4.4))
        mb.sphere(tuple(shoulder), 0.75, "prop:flesh", skin, subdivisions=2)
        elbow, wrist = Vector((sx * 5.0, 0.6, 5.6)), Vector((sx * 7.1, 1.7, 3.9))
        mb.tube([shoulder, (shoulder + elbow) * 0.5 + Vector((0, 0, 0.4)), elbow], [0.55, 0.42, 0.4], "prop:flesh", skin, segments=8)
        mb.sphere(tuple(elbow), 0.45, "prop:flesh", dark, subdivisions=2)
        mb.tube([elbow, (elbow + wrist) * 0.5 + Vector((0, 0, 0.3)), wrist], [0.4, 0.34, 0.3], "prop:flesh", skin, segments=8)
        hip, knee, foot = Vector((sx * 0.8, 0.0, 0.4)), Vector((sx * 1.3, 0.6, -3.0)), Vector((sx * 1.2, 0.1, -6.5))
        mb.tube([hip, (hip + knee) * 0.5, knee], [0.6, 0.45, 0.38], "prop:flesh", skin, segments=8)
        mb.tube([knee, (knee + foot) * 0.5 + Vector((0, -0.3, 0)), foot], [0.38, 0.3, 0.12], "prop:flesh", skin, segments=8)
    mb.tube([Vector((0, 0, 4.6)), Vector((0, 0.2, 5.8)), Vector((0, 0.3, 7.2))], [0.6, 0.5, 0.55], "prop:flesh", skin, segments=8)
    for k in range(4):  # dark veins down the belly
        x = (k - 1.5) * 0.35
        mb.tube([Vector((x, 0.9, 1.9)), Vector((x * 1.2, 0.95, 1.0)), Vector((x, 0.7, 0.3))], [0.07, 0.06, 0.03], "prop:flesh", dark, segments=4)
    return None


def moon_lord_head(mb: MeshBuilder, p, rng):
    """The Moon Lord's head (hitbox 4 m x 5 m): a tall ridged cranium, one great staring eye and a beard of tentacles."""
    skin, dark, glow = p.get("skin", SKIN), p.get("dark", DARK), p.get("glow", GLOW)
    mb.sphere((0, 0, 2.9), 1.7, "prop:flesh", skin, subdivisions=3, scale=(0.95, 0.9, 1.35), jitter=0.02, rng=rng)
    for k in range(5):  # ridges over the crown
        a = (k - 2) * 0.35
        mb.tube([Vector((math.sin(a) * 0.9, -0.6, 4.6)), Vector((math.sin(a) * 1.4, -1.3, 3.8)), Vector((math.sin(a) * 1.2, -1.6, 2.6))], [0.2, 0.16, 0.06], "prop:flesh", dark, segments=5)
    mb.sphere((0, 1.05, 3.1), 0.95, "prop:plain", "#e8ece0", subdivisions=2, scale=(1, 0.6, 1))
    mb.sphere((0, 1.55, 3.1), 0.5, "prop:plain:glow", glow, subdivisions=2, scale=(1, 0.4, 1))
    mb.sphere((0, 1.72, 3.1), 0.22, "prop:plain", "#06201a", subdivisions=1, scale=(1, 0.4, 1))
    for sx in (-1, 1):  # heavy brow lids
        mb.tube([Vector((sx * 1.0, 1.0, 3.6)), Vector((sx * 0.4, 1.35, 4.05)), Vector((0, 1.3, 4.1))], [0.2, 0.22, 0.18], "prop:flesh", skin, segments=5)
    for k in range(7):  # tentacles hanging from the jaw
        x = (k - 3) * 0.32
        base = Vector((x, 0.7 - abs(x) * 0.2, 1.5))
        curl = 0.4 * (1 if k % 2 else -1)
        mb.tube([base, base + Vector((curl * 0.3, 0.35, -1.0)), base + Vector((curl, 0.2, -2.1)), base + Vector((curl * 0.6, 0.55, -2.9))],
                [0.2, 0.15, 0.1, 0.03], "prop:flesh", skin if k % 3 else dark, segments=6)
    return None


def moon_lord_hand(mb: MeshBuilder, p, rng):
    """One of the Moon Lord's hands (hitbox 3 m x 4 m): a bony palm facing +Y with an eye in its centre and long
    clawed fingers curling forward."""
    skin, dark, glow = p.get("skin", SKIN), p.get("dark", DARK), p.get("glow", GLOW)
    mb.sphere((0, 0, 1.8), 1.1, "prop:flesh", skin, subdivisions=3, scale=(1.0, 0.45, 1.1))
    mb.sphere((0, 0.42, 1.8), 0.55, "prop:plain", "#e8ece0", subdivisions=2, scale=(1, 0.5, 1))
    mb.sphere((0, 0.66, 1.8), 0.3, "prop:plain:glow", glow, subdivisions=2, scale=(1, 0.4, 1))
    mb.sphere((0, 0.76, 1.8), 0.13, "prop:plain", "#06201a", subdivisions=1, scale=(1, 0.4, 1))
    for k in range(4):
        x = (k - 1.5) * 0.5
        root = Vector((x, 0.05, 2.7))
        pts = [root, root + Vector((x * 0.15, 0.2, 0.7)), root + Vector((x * 0.2, 0.55, 1.25)), root + Vector((x * 0.2, 0.95, 1.45))]
        mb.tube(pts, [0.2, 0.17, 0.13, 0.05], "prop:flesh", skin, segments=6)
        for q in pts[1:3]:
            mb.sphere(tuple(q), 0.19, "prop:flesh", dark, subdivisions=1)
    mb.tube([Vector((0, 0.05, 1.0)), Vector((0, 0.35, 0.4)), Vector((0, 0.75, 0.2))], [0.24, 0.18, 0.05], "prop:flesh", skin, segments=6)  # a thumb-like claw below
    mb.tube([Vector((0, -0.3, 1.2)), Vector((0, -0.9, 0.9))], [0.35, 0.3], "prop:flesh", skin, segments=8)  # wrist stub towards the arm
    return None


def true_eye(mb: MeshBuilder, p, rng):
    """A True Eye of Cthulhu: a pale eyeball with a glowing green iris, trailing tentacles (hitbox 1.5 m)."""
    glow = p.get("glow", GLOW)
    c = Vector((0, 0, 0.75))
    mb.sphere(tuple(c), 0.7, "prop:plain", "#e8ece0", subdivisions=3)
    mb.sphere(tuple(c + Vector((0, 0.55, 0))), 0.34, "prop:plain:glow", glow, subdivisions=2, scale=(1, 0.4, 1))
    mb.sphere(tuple(c + Vector((0, 0.66, 0))), 0.14, "prop:plain", "#06201a", subdivisions=1, scale=(1, 0.4, 1))
    for k in range(5):
        a = k * math.tau / 5
        base = c + Vector((math.cos(a) * 0.35, -0.55, math.sin(a) * 0.35))
        mb.tube([base, base + Vector((math.cos(a) * 0.3, -0.6, math.sin(a) * 0.3)), base + Vector((math.cos(a) * 0.1, -1.2, math.sin(a) * 0.1 - 0.2))],
                [0.12, 0.08, 0.02], "prop:flesh", p.get("skin", SKIN), segments=5)
    return None


def tablet(mb: MeshBuilder, p, rng):
    """The Mysterious Tablet: a carved stone slab on a plinth, its runes glowing blue (hitbox 1 m x 1.8 m)."""
    stone, glow = p.get("stone", "#5a5a6a"), p.get("glow", "#6ab0ff")
    mb.box((-0.6, -0.35, 0), (0.6, 0.35, 0.25), "prop:stone", "#3a3a48")
    mb.box((-0.45, -0.14, 0.25), (0.45, 0.14, 1.75), "prop:stone", stone)
    mb.cylinder((0, -0.14, 1.75), (0, 0.14, 1.75), 0.45, 0.45, "prop:stone", stone, segments=12)
    for k in range(5):
        z = 0.5 + k * 0.24
        w = 0.12 + (k * 37 % 5) * 0.04
        for y in (0.15, -0.15):
            mb.box((-w, y - 0.01, z), (w, y + 0.01, z + 0.08), "prop:plain:glow", glow)
    mb.sphere((0, 0.15, 1.8), 0.14, "prop:plain:glow", glow, subdivisions=2, scale=(1, 0.3, 1))


def manipulator(mb: MeshBuilder, p, rng):
    """The Ancient Manipulator: a stone altar holding a floating celestial orb inside a ring set with the four
    fragments' crystals."""
    w, h, d = p["size"]
    stone, glow = p.get("stone", "#4a4a5a"), p.get("glow", GLOW)
    mb.box((-w * 0.45, -d * 0.4, 0), (w * 0.45, d * 0.4, h * 0.15), "prop:stone", "#3a3a48")
    mb.cylinder((0, 0, h * 0.15), (0, 0, h * 0.35), w * 0.28, w * 0.2, "prop:stone", stone, segments=8, smooth=False)
    c = Vector((0, 0, h * 0.68))
    ring = [c + Vector((math.cos(t * math.tau / 16) * w * 0.36, 0, math.sin(t * math.tau / 16) * h * 0.28)) for t in range(17)]
    mb.tube(ring, [0.04] * len(ring), "prop:stone", "#8a8aa0", segments=5)
    mb.sphere(tuple(c), min(w, h) * 0.14, "prop:plain:glow", glow, subdivisions=2)
    for k, col in enumerate(("#ffb040", "#3affc0", "#ff6ae0", "#6ab0ff")):
        a = k * math.tau / 4 + math.tau / 8
        q = c + Vector((math.cos(a) * w * 0.36, 0, math.sin(a) * h * 0.28))
        mb.cylinder(tuple(q - Vector((0, 0, 0.06))), tuple(q + Vector((0, 0, 0.08))), 0.05, 0.0, "prop:plain:glow", col, segments=4, smooth=False)
    mb.cylinder((0, 0, h * 0.35), tuple(c - Vector((0, 0, h * 0.14))), 0.03, 0.03, "prop:stone", "#8a8aa0", segments=4)


GENERATORS = {"celestial_pillar": celestial_pillar, "moon_lord": moon_lord, "moon_lord_head": moon_lord_head, "moon_lord_hand": moon_lord_hand,
              "true_eye": true_eye, "tablet": tablet, "manipulator": manipulator}

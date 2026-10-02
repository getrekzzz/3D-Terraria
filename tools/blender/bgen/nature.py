"""Trees, giant fungi, cacti and small flora. Every generator: fn(mb: MeshBuilder, p: dict, rng) -> None."""
import math

from mathutils import Matrix, Vector

from .mesh import MeshBuilder, hex_color


def _lean(rng, amount):
    return Vector(((rng.random() - 0.5) * amount, (rng.random() - 0.5) * amount, 0))


def broadleaf(mb: MeshBuilder, p, rng):
    """Oak-like tree: flared trunk, a few limbs, and a crown of leaf-card clumps around their ends."""
    h, r = p["height"], p["trunk"]
    bark = f"prop:{p['bark']}"
    tint, leaf_tint = p.get("barkTint", "#ffffff"), p.get("leafTint", "#ffffff")
    top = Vector((0, 0, h * 0.62)) + _lean(rng, h * 0.06)
    pts = [Vector((0, 0, -0.3)).lerp(top, t) + _lean(rng, 0.12) * math.sin(t * math.pi) for t in (0, 0.2, 0.4, 0.6, 0.8, 1.0)]
    mb.tube(pts, [r * 1.25, r, r * 0.9, r * 0.8, r * 0.7, r * 0.55], bark, tint, segments=9)
    for k in range(4):  # root flares
        a = k * math.pi / 2 + rng.random() * 0.6
        d = Vector((math.cos(a), math.sin(a), 0))
        mb.tube([Vector((0, 0, 0.5)), d * r * 1.3 + Vector((0, 0, 0.05)), d * r * 2.0 + Vector((0, 0, -0.2))],
                [r * 0.45, r * 0.35, r * 0.1], bark, tint, segments=6)
    blobs = [(top + Vector((0, 0, p["canopy"] * 0.45)), p["canopy"] * 1.1)]
    for k in range(p.get("limbs", 4)):
        a = k * 2 * math.pi / p.get("limbs", 4) + rng.random()
        start = Vector((0, 0, 0)).lerp(top, 0.5 + rng.random() * 0.3)
        reach = p["canopy"] * (0.95 + rng.random() * 0.4)
        end = start + Vector((math.cos(a) * reach, math.sin(a) * reach, reach * (0.35 + rng.random() * 0.3)))
        mid = start.lerp(end, 0.5) + Vector((0, 0, 0.3))
        mb.tube([start, mid, end], [r * 0.4, r * 0.28, r * 0.12], bark, tint, segments=6)
        blobs.append((end + Vector((0, 0, 0.2)), p["canopy"] * (0.65 + rng.random() * 0.25)))
    card = f"card:{p.get('sprig', 'oak_sprig')}"
    for center, radius in blobs:
        _twigs(mb, center, radius, bark, tint, rng)
        mb.leaf_cards(center, radius, card, leaf_tint, rng, count=_cards(radius, 0.8), scale=(1, 1, 0.8), size=0.55)
        for _ in range(3):
            off = Vector(((rng.random() - 0.5), (rng.random() - 0.5), (rng.random() - 0.3))) * radius * 1.2
            mb.leaf_cards(center + off, radius * 0.5, card, leaf_tint, rng, count=_cards(radius * 0.5, 1.0), size=0.8)


def _cards(radius, flat):
    """Cards for a crown of this radius (and vertical squash): enough to close the silhouette, a few gaps inside."""
    return int(16 + 15 * radius * radius * (0.4 + 0.6 * flat))


def _twigs(mb: MeshBuilder, center, radius, bark, tint, rng, count=4):
    """Thin branches reaching into a crown, glimpsed through the gaps between leaf cards."""
    for _ in range(count):
        d = Vector((rng.random() - 0.5, rng.random() - 0.5, rng.random() * 0.6)).normalized()
        mb.tube([center - d * radius * 0.3, center + d * radius * 0.35, center + d * radius * 0.8], [0.06, 0.035, 0.012],
                bark, tint, segments=4)


def conifer(mb: MeshBuilder, p, rng):
    """Pine: straight trunk; whorls of drooping branches carrying needle cards, shorter toward the top."""
    h, r = p["height"], p["trunk"]
    bark, tint = f"prop:{p['bark']}", p.get("barkTint", "#ffffff")
    card = f"card:{p.get('sprig', 'pine_sprig')}"
    rgba = hex_color(p.get("leafTint", "#ffffff"))
    mb.cylinder((0, 0, -0.3), (0, 0, h * 0.97), r, r * 0.2, bark, tint, segments=8)
    tiers = p.get("tiers", 6) * 2
    base, width = h * 0.2, p["canopy"]
    for i in range(tiers):
        t = i / tiers
        z = base + (h * 0.95 - base) * t
        reach = width * (1.0 - t * 0.85) * (0.85 + rng.random() * 0.3)
        branches = 6 + int(rng.random() * 3)
        spin = rng.random() * math.tau
        for k in range(branches):
            a = spin + k * math.tau / branches + (rng.random() - 0.5) * 0.4
            d = Vector((math.cos(a), math.sin(a), 0))
            droop = 0.25 + 0.2 * rng.random()
            start = Vector((0, 0, z))
            tip = start + d * reach + Vector((0, 0, -reach * droop + 0.15))
            mb.tube([start, start.lerp(tip, 0.5) + Vector((0, 0, 0.12)), tip], [0.05 + 0.02 * (1 - t), 0.03, 0.01], bark, tint, segments=4)
            side = d.cross(Vector((0, 0, 1)))
            cone = (0, 0, z - width * 0.4)

            def normal_of(q, cone=Vector(cone)):
                return (q - cone).normalized()

            for j in range(3):
                f = 0.12 + j * 0.3 + rng.random() * 0.08
                at = start.lerp(tip, f) + Vector((0, 0, 0.1 * math.sin(f * math.pi)))
                length = reach * (0.62 - 0.12 * j)
                along = (tip - start).normalized()
                for roll in (-0.45, 0.45):
                    k_occ = 0.62 + 0.38 * f
                    color = (rgba[0] * k_occ, rgba[1] * k_occ, rgba[2] * k_occ, rgba[3])
                    tilt = side * math.cos(roll + (rng.random() - 0.5) * 0.4) + Vector((0, 0, math.sin(roll)))
                    mb.card(at - along * length * 0.15, along, tilt, length, length * 0.9, card, color, normal_of, flip=rng.random() < 0.5)
    tip_rgba = (rgba[0], rgba[1], rgba[2], rgba[3])
    for k in range(3):  # leader at the very top
        a = k * math.tau / 3
        side = Vector((math.cos(a), math.sin(a), 0))
        mb.card(Vector((0, 0, h * 0.9)), Vector((0, 0, 1)), side, h * 0.12, h * 0.08, card, tip_rgba,
                lambda q: Vector((q.x, q.y, 0.6)).normalized())


def palm(mb: MeshBuilder, p, rng):
    """Palm: curved ringed trunk, drooping fronds and a coconut cluster."""
    h, r = p["height"], p["trunk"]
    bark, fronds = f"prop:{p['bark']}", f"prop:{p['leaves']}"
    bend = p.get("bend", 1.4) * (0.7 + rng.random() * 0.6)
    a = rng.random() * 2 * math.pi
    d = Vector((math.cos(a), math.sin(a), 0))
    pts = [d * bend * t * t + Vector((0, 0, h * t - 0.3)) for t in (i / 9 for i in range(10))]
    mb.tube(pts, [r * (1.1 - 0.4 * i / 9) for i in range(10)], bark, p.get("barkTint", "#ffffff"), segments=8)
    crown = pts[-1]
    for k in range(p.get("fronds", 8)):
        b = k * 2 * math.pi / p.get("fronds", 8) + rng.random() * 0.3
        fd = Vector((math.cos(b), math.sin(b), 0))
        length = p["canopy"] * (0.85 + rng.random() * 0.3)
        arc = [crown + fd * length * t + Vector((0, 0, 0.8 * math.sin(t * math.pi * 0.8) - 1.2 * t * t)) for t in (0, 0.25, 0.5, 0.75, 1.0)]
        mb.tube(arc, [0.18, 0.3, 0.26, 0.16, 0.02], fronds, p.get("leafTint", "#ffffff"), segments=3)
    for k in range(3):
        c = crown + Vector((math.cos(k * 2.1), math.sin(k * 2.1), -0.35)) * 0.25
        mb.sphere(c, 0.16, "prop:palm_bark", "#6a4a2a", subdivisions=1)


def jungle(mb: MeshBuilder, p, rng):
    """Mahoe: very tall trunk with buttress roots and a broad, flat, layered canopy."""
    h, r = p["height"], p["trunk"]
    bark, leaves = f"prop:{p['bark']}", f"prop:{p['leaves']}"
    pts = [Vector((0, 0, -0.3)) + Vector((0, 0, h * t)) + _lean(rng, 0.3) * math.sin(t * math.pi) for t in (0, 0.25, 0.5, 0.75, 0.92)]
    mb.tube(pts, [r * 1.2, r, r * 0.85, r * 0.7, r * 0.55], bark, p.get("barkTint", "#ffffff"), segments=10)
    for k in range(5):
        a = k * 2 * math.pi / 5 + rng.random() * 0.4
        d = Vector((math.cos(a), math.sin(a), 0))
        mb.tube([Vector((0, 0, 2.2)), d * r * 1.4 + Vector((0, 0, 0.8)), d * r * 2.6 + Vector((0, 0, -0.2))],
                [r * 0.3, r * 0.22, r * 0.08], bark, p.get("barkTint", "#ffffff"), segments=5)
    card = f"card:{p.get('sprig', 'jungle_sprig')}"
    for layer in range(3):
        z = h * (0.8 + layer * 0.08)
        radius = p["canopy"] * (1.0 - layer * 0.25)
        for k in range(4 - layer):
            a = rng.random() * math.tau
            off = Vector((math.cos(a), math.sin(a), 0)) * radius * (0.35 + 0.55 * rng.random()) * (1 if k else 0.3)
            center = Vector((0, 0, z)) + off
            _twigs(mb, center, radius * 0.75, bark, p.get("barkTint", "#ffffff"), rng, count=3)
            mb.leaf_cards(center, radius * 0.75, card, p.get("leafTint", "#ffffff"), rng, count=_cards(radius * 0.75, 0.38),
                          scale=(1, 1, 0.38), size=0.45, droop=0.3)
    for k in range(3):  # hanging vines
        a = rng.random() * 2 * math.pi
        top = Vector((math.cos(a) * p["canopy"] * 0.6, math.sin(a) * p["canopy"] * 0.6, h * 0.78))
        mb.tube([top, top + Vector((0.1, 0, -1.5)), top + Vector((0, 0.1, -3.0))], [0.05, 0.04, 0.02], leaves, "#9ad07a", segments=4)


def twisted(mb: MeshBuilder, p, rng):
    """Gloomwood: corkscrew trunk and spiky dark foliage on crooked branches."""
    h, r = p["height"], p["trunk"]
    bark = f"prop:{p['bark']}"
    pts = [Vector((math.sin(t * 7) * 0.35 * t, math.cos(t * 7) * 0.35 * t, h * 0.7 * t - 0.3)) for t in (i / 10 for i in range(11))]
    mb.tube(pts, [r * (1.2 - 0.7 * i / 10) for i in range(11)], bark, p.get("barkTint", "#ffffff"), segments=7)
    crown = pts[-1]
    card = f"card:{p.get('sprig', 'gloom_sprig')}"
    for k in range(7):
        off = Vector(((rng.random() - 0.5) * 2.4, (rng.random() - 0.5) * 2.4, rng.random() * 1.8))
        radius = p["canopy"] * (0.35 + rng.random() * 0.25)
        mb.tube([crown, crown + off * 0.6 + Vector((0, 0, 0.3)), crown + off], [0.12, 0.07, 0.03], bark, p.get("barkTint", "#ffffff"), segments=5)
        mb.leaf_cards(crown + off, radius, card, p.get("leafTint", "#ffffff"), rng, count=_cards(radius, 1.0) + 6, size=0.6, droop=0.2)


def cactus(mb: MeshBuilder, p, rng):
    h, r = p["height"] * (0.8 + rng.random() * 0.3), p["radius"]
    skin = "prop:cactus_skin"
    mb.lathe([(r * 0.9, -0.1), (r, 0.2), (r, h - r * 0.8), (r * 0.7, h - r * 0.2), (0.0, h)], skin, segments=10)
    for k in range(rng.randint(1, 3)):
        a = rng.random() * 2 * math.pi
        d = Vector((math.cos(a), math.sin(a), 0))
        z = h * (0.3 + rng.random() * 0.35)
        up = h * (0.2 + rng.random() * 0.25)
        start = Vector((0, 0, z))
        mb.tube([start, start + d * r * 1.8, start + d * r * 2.0 + Vector((0, 0, r)), start + d * r * 2.0 + Vector((0, 0, up))],
                [r * 0.6, r * 0.55, r * 0.55, r * 0.35], skin, segments=8)


def giant_mushroom(mb: MeshBuilder, p, rng):
    h, cap_r = p["height"], p["cap"]
    mb.lathe([(0.55, -0.1), (0.4, 0.3), (0.3, h * 0.5), (0.35, h - 0.6), (0.0, h - 0.5)], "prop:plain", "#d8e4f0", segments=10)
    mb.lathe([(cap_r, h - 0.6), (cap_r * 0.95, h - 0.2), (cap_r * 0.7, h + 0.5), (cap_r * 0.3, h + 0.9), (0.0, h + 1.0)],
             f"prop:{p['capLayer']}", "#ffffff", segments=16)
    mb.lathe([(0.3, h - 0.62), (cap_r * 0.97, h - 0.62), (0.0, h - 0.58)], "prop:plain:glow", p.get("glow", "#5aa0ff"), segments=16)


def grass_tuft(mb: MeshBuilder, p, rng):
    for _ in range(rng.randint(6, 10)):
        a = rng.random() * 2 * math.pi
        base = Vector((math.cos(a), math.sin(a), 0)) * rng.random() * 0.12
        tip = base + Vector((math.cos(a) * 0.1, math.sin(a) * 0.1, p.get("height", 0.35) * (0.6 + rng.random() * 0.6)))
        mb.tube([base, base.lerp(tip, 0.5) + Vector((0, 0, 0.02)), tip], [0.025, 0.018, 0.001], "prop:fronds", p.get("tint", "#ffffff"), segments=3)


def flower(mb: MeshBuilder, p, rng):
    colors = p["colors"]
    color = colors[rng.randrange(len(colors))]
    h = 0.3 + rng.random() * 0.12
    mb.tube([(0, 0, 0), (0.02, 0, h * 0.5), (0, 0.02, h)], [0.012, 0.01, 0.008], "prop:fronds", "#ffffff", segments=4)
    for k in range(5):
        a = k * 2 * math.pi / 5
        c = Vector((math.cos(a) * 0.045, math.sin(a) * 0.045, h))
        mb.sphere(c, 0.04, "prop:plain", color, subdivisions=1, scale=(1, 1, 0.35))
    mb.sphere((0, 0.02, h + 0.01), 0.025, "prop:plain", "#f0c020", subdivisions=1)
    mb.tube([(0, 0, 0.08), (0.06, 0.02, 0.14)], [0.012, 0.002], "prop:fronds", "#ffffff", segments=3)


def bush(mb: MeshBuilder, p, rng):
    card = f"card:{p.get('sprig', 'oak_sprig')}"
    for _ in range(rng.randint(3, 5)):
        c = Vector(((rng.random() - 0.5) * 0.5, (rng.random() - 0.5) * 0.5, 0.25 + rng.random() * 0.3))
        mb.leaf_cards(c, 0.32 + rng.random() * 0.15, card, p.get("tint", "#ffffff"), rng, count=14, size=1.1, core=0.2)


def fern(mb: MeshBuilder, p, rng):
    for k in range(rng.randint(6, 9)):
        a = k * 2 * math.pi / 8 + rng.random() * 0.4
        d = Vector((math.cos(a), math.sin(a), 0))
        length = 0.55 + rng.random() * 0.25
        arc = [d * length * t + Vector((0, 0, 0.6 * math.sin(t * math.pi * 0.7))) for t in (0, 0.35, 0.7, 1.0)]
        mb.tube(arc, [0.02, 0.09, 0.07, 0.01], f"prop:{p['leaves']}", p.get("tint", "#ffffff"), segments=3)


def small_mushroom(mb: MeshBuilder, p, rng):
    h = 0.18 + rng.random() * 0.06
    mb.lathe([(0.03, 0.0), (0.025, h), (0.0, h + 0.01)], "prop:plain", "#e8e0d0", segments=8)
    glow = ":glow" if p.get("glow") else ""
    mb.lathe([(0.1, h - 0.01), (0.09, h + 0.04), (0.05, h + 0.08), (0.0, h + 0.09)], f"prop:{p['capLayer']}{glow}", p.get("tint", "#ffffff"), segments=10)
    for k in range(p.get("spots", 0)):  # pale flecks on a toadstool cap
        a = k * 2.4
        mb.sphere((math.cos(a) * 0.055, math.sin(a) * 0.055, h + 0.06 - (k % 2) * 0.02), 0.014, "prop:plain", "#f4efe0", subdivisions=1, scale=(1, 1, 0.4))


def spike(mb: MeshBuilder, p, rng):
    """Stalagmite (hanging=False) or stalactite (hanging=True) with growth rings."""
    h = p["height"] * (0.7 + rng.random() * 0.3)
    r = p["radius"] * (0.8 + rng.random() * 0.4)
    profile = [(r, 0.0), (r * 0.8, h * 0.3), (r * 0.55, h * 0.55), (r * 0.25, h * 0.85), (0.0, h)]
    if p.get("hanging"):
        top = p["height"]
        profile = [(r, top), (r * 0.8, top - h * 0.3), (r * 0.55, top - h * 0.55), (r * 0.25, top - h * 0.85), (0.0, top - h)]
        profile = list(reversed(profile))
    mb.lathe(profile, f"prop:{p.get('layer', 'stone')}", p.get("tint", "#ffffff"), segments=7, smooth=True)


def rubble(mb: MeshBuilder, p, rng):
    for _ in range(rng.randint(3, 6)):
        c = Vector(((rng.random() - 0.5) * 0.7, (rng.random() - 0.5) * 0.7, 0.04))
        mb.sphere(c, 0.07 + rng.random() * 0.1, f"prop:{p.get('layer', 'stone')}", p.get("tint", "#ffffff"), subdivisions=1,
                  scale=(1, 1, 0.6), jitter=0.25, rng=rng)


def pot(mb: MeshBuilder, p, rng):
    w = 0.38 + rng.random() * 0.08
    h = 0.75 + rng.random() * 0.15
    mb.lathe([(w * 0.55, 0.0), (w * 0.9, h * 0.2), (w, h * 0.45), (w * 0.8, h * 0.75), (w * 0.45, h * 0.88),
              (w * 0.5, h), (w * 0.42, h), (w * 0.35, h * 0.9)], "prop:clay", p.get("tint", "#ffffff"), segments=14)
    band = p["bands"][rng.randrange(len(p["bands"]))]
    mb.lathe([(w * 1.01, h * 0.42), (w * 1.01, h * 0.5)], "prop:plain", band, segments=14, smooth=True)


def crystal_node(mb: MeshBuilder, p, rng):
    for _ in range(4):
        c = Vector(((rng.random() - 0.5) * 0.8, (rng.random() - 0.5) * 0.8, 0.05))
        mb.sphere(c, 0.2 + rng.random() * 0.1, "prop:stone", "#ffffff", subdivisions=1, scale=(1, 1, 0.6), jitter=0.2, rng=rng)
    heart = f"prop:crystal:glow"
    mb.sphere((-0.12, 0, 0.62), 0.2, heart, p["color"], subdivisions=1)
    mb.sphere((0.12, 0, 0.62), 0.2, heart, p["color"], subdivisions=1)
    mb.cylinder((0, 0, 0.58), (0, 0, 0.18), 0.3, 0.0, heart, p["color"], segments=6, smooth=False)


def shrine(mb: MeshBuilder, p, rng):
    mb.lathe([(0.75, 0.0), (0.7, 0.25), (0.55, 0.35), (0.55, 0.55), (0.0, 0.6)], "prop:stone_brick", "#c8c8d0", segments=8, smooth=False)
    for _ in range(6):
        a = rng.random() * 2 * math.pi
        mb.sphere((math.cos(a) * 0.9, math.sin(a) * 0.9, 0.05), 0.15 + rng.random() * 0.1, "prop:stone", "#ffffff",
                  subdivisions=1, jitter=0.3, rng=rng)
    tilt = Matrix.Rotation(0.12, 4, "X")
    mb.box((-0.06, -0.015, 0.45), (0.06, 0.015, 1.5), "prop:iron:glow", "#6a8cff", matrix=tilt)
    mb.box((-0.25, -0.05, 1.45), (0.25, 0.05, 1.53), "prop:gold", "#ffffff", matrix=tilt)
    mb.box((-0.03, -0.03, 1.53), (0.03, 0.03, 1.8), "prop:dark_planks", "#ffffff", matrix=tilt)


GENERATORS = {
    "broadleaf": broadleaf, "conifer": conifer, "palm": palm, "jungle": jungle, "twisted": twisted,
    "cactus": cactus, "giant_mushroom": giant_mushroom, "grass_tuft": grass_tuft, "flower": flower,
    "bush": bush, "fern": fern, "small_mushroom": small_mushroom, "spike": spike, "rubble": rubble,
    "pot": pot, "crystal_node": crystal_node, "shrine": shrine,
}

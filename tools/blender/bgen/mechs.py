"""The mechanical terrors: the Twin Lenses (armoured eyes that open in their second phase into a laser barrel and a
fanged furnace), the Iron Serpent (head, armoured segment with a laser port, tail, probe drone) and Warden Prime (a
steel skull and its four tools: saw, vice, cannon, laser). Sizes are final; front faces Blender +Y; bosses are drawn at
their centre; worm parts are modelled along +Y (the direction of travel)."""
import math

from mathutils import Matrix, Vector

from .bosses import skull
from .mesh import MeshBuilder

STEEL, DARK, BRASS = "#b4b8c2", "#50545e", "#c8a050"


def _ring(mb, radius, y, width, thickness, layer, color, segments=24):
    """A flat band around the Y axis (armour seams, lens sockets)."""
    profile = [(radius, y - width / 2), (radius + thickness, y - width / 2), (radius + thickness, y + width / 2), (radius, y + width / 2)]
    mb.lathe(profile, layer, color, segments=segments, smooth=False, matrix=Matrix.Rotation(-math.pi / 2, 4, "X"))


def _rivets(mb, radius, y, count, color=DARK):
    for k in range(count):
        a = k * math.tau / count
        mb.sphere((math.cos(a) * radius, y, math.sin(a) * radius), 0.035, "prop:iron", color, subdivisions=1)


def _cables(mb, r, rng, color):
    """Armoured hoses trailing behind an eye, each banded like a spine."""
    for k in range(6):
        a = k * math.tau / 6 + rng.random() * 0.4
        base = Vector((math.cos(a) * r * 0.45, -r * 0.8, math.sin(a) * r * 0.45))
        mid = base + Vector((math.cos(a) * 0.35, -0.9, math.sin(a) * 0.35 - 0.15))
        tip = mid + Vector((math.cos(a) * 0.2, -0.8, math.sin(a) * 0.2 - 0.3))
        mb.tube([base, mid, tip], [0.13, 0.1, 0.05], "prop:dark_iron", color, segments=8)
        for t in (0.3, 0.6, 0.85):
            p = base.lerp(mid, t * 2) if t < 0.5 else mid.lerp(tip, (t - 0.5) * 2)
            mb.sphere(tuple(p), 0.12 - t * 0.05, "prop:iron", STEEL, subdivisions=1, scale=(1, 0.5, 1))


def mech_eye(mb: MeshBuilder, p, rng):
    """An armoured eye: steel shell in banded plates, a glowing iris in a steel socket; open, the front plates part
    around a laser barrel (ray) or a fanged furnace mouth (flare)."""
    r, iris = p.get("radius", 1.3), p.get("iris", "#ff3030")
    variant, opened = p.get("variant", "ray"), p.get("open", False)
    mb.sphere((0, 0, 0), r, "prop:iron", p.get("shell", STEEL), subdivisions=3)
    for y, w in ((-r * 0.45, 0.12), (r * 0.05, 0.1), (r * 0.5, 0.1)):
        rr = math.sqrt(max(r * r - y * y, 0.01)) - 0.02
        _ring(mb, rr, y, w, 0.06, "prop:dark_iron", DARK)
        _rivets(mb, rr + 0.05, y, 12)
    mb.sphere((0, -r * 0.95, 0), r * 0.35, "prop:plain:glow", p.get("thrust", "#ff9040"), subdivisions=2, scale=(1, 0.35, 1))
    _cables(mb, r, rng, DARK)
    front = r * 0.78
    if not opened:
        _ring(mb, r * 0.52, front, 0.2, 0.12, "prop:iron", STEEL, segments=28)
        mb.sphere((0, front + 0.04, 0), r * 0.5, "prop:plain:glow", iris, subdivisions=2, scale=(1, 0.3, 1))
        mb.sphere((0, front + 0.14, 0), r * 0.2, "prop:plain", "#060606", subdivisions=2, scale=(1, 0.3, 1))
        return
    # Phase two: the front splits into four petals folded back, exposing the weapon.
    for k in range(4):
        a = k * math.pi / 2 + math.pi / 4
        m = Matrix.Translation((math.cos(a) * r * 0.7, front * 0.9, math.sin(a) * r * 0.7)) @ Matrix.Rotation(a, 4, "Y")
        mb.box((-0.35, -0.08, -0.5), (0.35, 0.08, 0.5), "prop:iron", STEEL, matrix=m @ Matrix.Rotation(0.9, 4, "X"))
    mb.sphere((0, front - 0.1, 0), r * 0.5, "prop:plain", "#101014", subdivisions=2, scale=(1, 0.4, 1))
    if variant == "ray":
        mb.cylinder((0, front - 0.2, 0), (0, front + 0.9, 0), 0.32, 0.26, "prop:dark_iron", DARK, segments=16)
        for k in range(5):  # cooling fins
            y = front + 0.05 + k * 0.16
            mb.cylinder((0, y, 0), (0, y + 0.05, 0), 0.42, 0.42, "prop:iron", STEEL, segments=16)
        mb.cylinder((0, front + 0.9, 0), (0, front + 1.0, 0), 0.2, 0.2, "prop:plain:glow", iris, segments=16)
    else:
        mb.sphere((0, front - 0.05, 0), r * 0.38, "prop:plain:glow", iris, subdivisions=2, scale=(1, 0.5, 1))
        for ring, count, length in ((r * 0.6, 14, 0.35), (r * 0.42, 10, 0.25)):
            for k in range(count):
                a = k * math.tau / count
                base = Vector((math.cos(a) * ring, front + 0.1, math.sin(a) * ring))
                tip = base - Vector((math.cos(a), 0, math.sin(a))) * length + Vector((0, length * 0.5, 0))
                mb.cylinder(tuple(base), tuple(tip), length * 0.25, 0.0, "prop:iron", "#e0e4ea", segments=5, smooth=False)


def serpent(mb: MeshBuilder, p, rng):
    """One part of the Iron Serpent, along +Y: head (drill jaws, red slit eyes), armoured segment (plated ring with a
    glowing laser port below), tail (tapering fins) or probe (a small hovering drone)."""
    part, r, length = p.get("part", "segment"), p.get("radius", 0.9), p.get("length", 1.3)
    glow = p.get("glow", "#ff2a20")
    if part == "probe":
        mb.sphere((0, 0, 0), 0.32, "prop:iron", STEEL, subdivisions=2)
        _ring(mb, 0.31, 0.0, 0.08, 0.05, "prop:dark_iron", DARK)
        mb.sphere((0, 0.26, 0), 0.12, "prop:plain:glow", glow, subdivisions=2, scale=(1, 0.5, 1))
        for k in range(3):
            a = k * math.tau / 3
            m = Matrix.Translation((math.cos(a) * 0.3, -0.1, math.sin(a) * 0.3)) @ Matrix.Rotation(-a, 4, "Y")
            mb.box((-0.03, -0.18, -0.12), (0.03, 0.05, 0.12), "prop:dark_iron", DARK, matrix=m)
        return
    if part == "tail":
        mb.cylinder((0, -length * 0.6, 0), (0, length * 0.5, 0), r * 0.25, r * 0.85, "prop:dark_iron", DARK, segments=12)
        for k in range(4):
            a = k * math.pi / 2
            base = Vector((math.cos(a) * r * 0.5, -length * 0.1, math.sin(a) * r * 0.5))
            mb.extrude_polygon([(0, 0), (0.1, -0.7), (0.04, -0.75), (-0.05, 0.1)], 0.05, "prop:iron", STEEL,
                               matrix=Matrix.Translation(base) @ Matrix.Rotation(a, 4, "Y") @ Matrix.Rotation(math.pi / 2, 4, "X"))
        return
    # Core and armour plates (an octagonal shell in two rows), a laser port underneath.
    mb.cylinder((0, -length * 0.5, 0), (0, length * 0.5, 0), r * 0.7, r * 0.7, "prop:dark_iron", DARK, segments=16)
    for y in (-length * 0.32, length * 0.12):
        mb.cylinder((0, y, 0), (0, y + length * 0.34, 0), r, r * 0.94, "prop:iron", p.get("plate", STEEL), segments=8, smooth=False)
        _rivets(mb, r * 0.97, y + length * 0.17, 8)
    mb.cylinder((0, 0, -r * 0.85), (0, 0, -r * 1.12), 0.16, 0.12, "prop:dark_iron", DARK, segments=10)
    mb.sphere((0, 0, -r * 1.12), 0.1, "prop:plain:glow", glow, subdivisions=1)
    for k in range(2):  # dorsal spikes
        a = math.pi / 2 + (k - 0.5) * 0.8
        base = Vector((math.cos(a) * r * 0.95, 0, math.sin(a) * r * 0.95))
        mb.cylinder(tuple(base), tuple(base * 1.45 + Vector((0, -0.2, 0))), 0.1, 0.0, "prop:iron", STEEL, segments=4, smooth=False)
    if part == "head":
        mb.cylinder((0, length * 0.5, 0), (0, length * 1.0, 0), r * 0.95, r * 0.5, "prop:iron", STEEL, segments=8, smooth=False)
        for sx in (-1, 1):
            base = Vector((sx * r * 0.45, length * 0.95, 0))
            mb.tube([base, base + Vector((sx * 0.35, 0.5, 0.05)), base + Vector((-sx * 0.1, 0.95, 0.0))], [0.16, 0.11, 0.02],
                    "prop:dark_iron", "#d0d4dc", segments=6)
            mb.box((sx * r * 0.5 - 0.18, length * 0.78, r * 0.35), (sx * r * 0.5 + 0.18, length * 0.84, r * 0.45), "prop:plain:glow", glow)


def prime_skull(mb: MeshBuilder, p, rng):
    """Warden Prime's head: the crypt skull recast in steel, bolted plates across the dome, burning red sockets."""
    r = p.get("radius", 1.5)
    skull(mb, {"radius": r, "bone": p.get("steel", STEEL), "eyes": p.get("eyes", "#ff2020"), "layer": "iron", "cracks": 0}, rng)
    for k in range(-2, 3):  # a crest of plates over the dome
        y = k * r * 0.22
        mb.box((-r * 0.12, y - r * 0.1, r * 0.9), (r * 0.12, y + r * 0.1, r * 1.1), "prop:dark_iron", DARK)
    for sx in (-1, 1):
        mb.cylinder((sx * r * 0.85, 0, r * 0.1), (sx * r * 1.1, 0, r * 0.1), r * 0.16, r * 0.12, "prop:dark_iron", DARK, segments=10)
        mb.sphere((sx * r * 1.12, 0, r * 0.1), r * 0.09, "prop:plain:glow", p.get("eyes", "#ff2020"), subdivisions=1)


def prime_arm(mb: MeshBuilder, p, rng):
    """One of Prime's four tools, centred at the origin with its working end towards +Y and a forearm stub rising
    behind it: saw (spinning toothed disc), vice (open clamp jaws), cannon (wide barrel) or laser (slim emitter)."""
    tool, glow = p.get("tool", "saw"), p.get("glow", "#ff3020")
    mb.cylinder((0, -0.3, 0.0), (0, -0.55, 1.2), 0.2, 0.16, "prop:dark_iron", DARK, segments=10)
    mb.sphere((0, -0.3, 0.0), 0.26, "prop:iron", STEEL, subdivisions=2)
    if tool == "saw":
        mb.cylinder((-0.06, 0.35, 0), (0.06, 0.35, 0), 0.75, 0.75, "prop:iron", "#d8dce4", segments=32, smooth=False)
        for k in range(24):
            a = k * math.tau / 24
            base = Vector((0, 0.35 + math.cos(a) * 0.74, math.sin(a) * 0.74))
            tip = Vector((0, 0.35 + math.cos(a + 0.12) * 0.9, math.sin(a + 0.12) * 0.9))
            mb.cylinder(tuple(base), tuple(tip), 0.05, 0.0, "prop:iron", "#e8ecf2", segments=3, smooth=False)
        mb.cylinder((-0.12, 0.35, 0), (0.12, 0.35, 0), 0.18, 0.18, "prop:dark_iron", DARK, segments=12)
        mb.box((-0.08, -0.3, -0.1), (0.08, 0.35, 0.1), "prop:dark_iron", DARK)
    elif tool == "vice":
        for sz in (1, -1):
            mb.box((-0.18, -0.1, sz * 0.12), (0.18, 0.75, sz * 0.32), "prop:iron", STEEL)
            mb.box((-0.2, 0.6, sz * 0.05), (0.2, 0.8, sz * 0.32), "prop:dark_iron", DARK)
            for k in range(4):  # grip ridges
                mb.box((-0.18, 0.62 + k * 0.045, sz * 0.05), (0.18, 0.64 + k * 0.045, sz * 0.1), "prop:iron", "#e0e4ea")
        mb.cylinder((-0.25, 0.0, 0), (0.25, 0.0, 0), 0.12, 0.12, "prop:dark_iron", DARK, segments=10)
    elif tool == "cannon":
        mb.cylinder((0, -0.2, 0), (0, 1.1, 0), 0.34, 0.3, "prop:dark_iron", DARK, segments=16)
        for y in (0.2, 0.7, 1.05):
            mb.cylinder((0, y, 0), (0, y + 0.1, 0), 0.4, 0.4, "prop:iron", BRASS, segments=16)
        mb.cylinder((0, 1.08, 0), (0, 1.16, 0), 0.22, 0.22, "prop:plain:glow", "#ff9030", segments=16)
    else:
        mb.cylinder((0, -0.2, 0), (0, 1.0, 0), 0.16, 0.12, "prop:iron", STEEL, segments=12)
        for k in range(5):  # coils
            y = 0.05 + k * 0.17
            mb.cylinder((0, y, 0), (0, y + 0.07, 0), 0.24, 0.24, "prop:iron:metal", "#e08c5a", segments=14)
        mb.sphere((0, 1.02, 0), 0.14, "prop:plain:glow", glow, subdivisions=2)


def clockwork(mb: MeshBuilder, p, rng):
    """A summoning trinket in brass and steel: a geared eye, a coiled segmented worm or a small steel skull."""
    kind, glow = p.get("kind", "eye"), p.get("glow", "#ff3030")
    if kind == "eye":
        mb.sphere((0, 0, 0.14), 0.12, "prop:iron", STEEL, subdivisions=2)
        mb.sphere((0, 0.1, 0.14), 0.055, "prop:plain:glow", glow, subdivisions=1, scale=(1, 0.4, 1))
        for k in range(12):  # a gear ring around it
            a = k * math.tau / 12
            mb.box((-0.02, -0.02, -0.02), (0.02, 0.02, 0.02), "prop:gold", BRASS,
                   matrix=Matrix.Translation((math.cos(a) * 0.17, 0, 0.14 + math.sin(a) * 0.17)))
        mb.cylinder((0, -0.02, 0.14), (0, 0.02, 0.14), 0.165, 0.165, "prop:gold", BRASS, segments=24)
    elif kind == "worm":
        pts = [Vector((math.cos(t) * 0.1 * (1 - t / 12), math.sin(t) * 0.1 * (1 - t / 12), 0.05 + t * 0.012)) for t in (i * 0.5 for i in range(20))]
        for k, pt in enumerate(pts):
            mb.sphere(tuple(pt), 0.035 - k * 0.001, "prop:iron" if k % 2 else "prop:gold", STEEL if k % 2 else BRASS, subdivisions=1)
        mb.sphere(tuple(pts[0] + Vector((0, 0, 0.01))), 0.012, "prop:plain:glow", glow, subdivisions=1)
    else:
        skull(mb, {"radius": 0.11, "centerZ": 0.13, "bone": STEEL, "eyes": glow, "layer": "iron", "cracks": 0}, rng)


GENERATORS = {"mech_eye": mech_eye, "serpent": serpent, "prime_skull": prime_skull, "prime_arm": prime_arm, "clockwork": clockwork}

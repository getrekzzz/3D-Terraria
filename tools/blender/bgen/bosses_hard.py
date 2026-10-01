"""Hardmode bosses in the reference game's likeness: the Twins (living eyes half-armoured in bolted steel, trailing
armoured hoses, that open into a laser cannon or a fanged flamethrower maw), the Destroyer (a plated steel worm with
glowing red lights, a jawed head, and its probes), Skeletron Prime (a steel skull with a jaw, glowing sockets and
four worked tools on hydraulic arms) and Plantera (a spotted pink bulb with jagged jaws that splits into a fanged maw
with writhing tentacles). Centred models; worm parts along +Y; front faces Blender +Y.
"""
import math

from mathutils import Matrix, Vector

from .bosses_pre import _eyeball
from .bosses_pre2 import clips, skeletron_skull, wave
from .detail import fang, fibonacci, mix, noise3, organic, spikes, teeth_ring
from .mesh import MeshBuilder

STEEL, STEEL_DARK, GUNMETAL, BRASS = "#b8bec8", "#5a606a", "#3a3e46", "#c8a050"
ROT_X = Matrix.Rotation(-math.pi / 2, 4, "X")        # lathes about Z -> about +Y


def _metal(q, n, base=STEEL, dark=STEEL_DARK):
    return mix(dark, base, 0.55 + 0.35 * max(0.0, n.z) + 0.1 * noise3(q, 5))


def _band(mb, r, y, width, depth, color=STEEL_DARK, layer="prop:dark_iron", segments=32):
    """A raised seam band around the Y axis at height y on a sphere of radius r."""
    rr = math.sqrt(max(r * r - y * y, 1e-4))
    mb.lathe([(rr - 0.01, y - width / 2), (rr + depth, y - width / 2), (rr + depth, y + width / 2), (rr - 0.01, y + width / 2)],
             layer, color, segments=segments, smooth=False, matrix=ROT_X)
    for k in range(12):
        a = 2 * math.pi * k / 12
        mb.sphere((math.cos(a) * (rr + depth), y, math.sin(a) * (rr + depth)), width * 0.28, "prop:iron", STEEL, subdivisions=1)


def _hoses(mb, r, rng, count=6):
    """Armoured hoses trailing from the back: tubes wrapped in steel rings."""
    for k in range(count):
        a = 2 * math.pi * k / count + rng.uniform(-0.15, 0.15)
        pts = []
        for i in range(6):
            t = i / 5
            pts.append(Vector((math.cos(a + t * 1.5) * r * 0.35 * (1 - 0.5 * t), -r * 0.8 - t * r * 1.8, math.sin(a + t * 1.5) * r * 0.3 * (1 - 0.5 * t) - t * t * r * 0.3)))
        mb.tube(pts, [r * 0.14 * (1 - 0.6 * i / 5) for i in range(6)], "prop:dark_iron", GUNMETAL, segments=8)
        for i in range(1, 6):
            mb.cylinder(pts[i] - (pts[i] - pts[i - 1]).normalized() * 0.03, pts[i] + (pts[i] - pts[i - 1]).normalized() * 0.03,
                        r * 0.16 * (1 - 0.55 * i / 5), None, "prop:iron", STEEL, segments=8)


# ---------------------------------------------------------------------------------------------------- the Twins
def twin_eye(mb: MeshBuilder, p, rng):
    r, iris = p.get("radius", 1.3), p.get("iris", "#ff2a2a")
    opened, variant = p.get("open", False), p.get("variant", "ray")
    _eyeball(mb, r * 0.985, iris, rng, vein_trees=8, sclera="#ece6e0", tris=3500, vein_depth=2, hole=0.62 if opened else 0.0,
             cornea=not opened)
    # Steel plating over the back half: two shells with seams, bolted rings and a thruster.
    for y0, y1 in ((-r * 1.02, -r * 0.35), (-r * 0.3, r * 0.2)):
        prof = [(math.sqrt(max(r * r - y * y, 0)) * 1.04, y) for y in (y0 + (y1 - y0) * i / 8 for i in range(9))]
        mb.lathe(prof, "prop:iron", STEEL, segments=32, matrix=ROT_X)
        _band(mb, r * 1.04, y1, 0.08 * r, 0.05 * r)
    mb.sphere((0, -r * 1.02, 0), r * 0.32, "prop:plain:glow", p.get("thrust", "#ff8a30"), subdivisions=2, scale=(1, 0.35, 1))
    _hoses(mb, r, rng)
    if not opened:
        return
    front = Vector((0, r * 0.7, 0))
    for k in range(6):  # torn armour petals folded back around the opening
        a = 2 * math.pi * k / 6
        radial = Vector((math.cos(a), 0, math.sin(a)))
        base = front + radial * r * 0.62
        mb.tube([base, base + radial * r * 0.25 + Vector((0, -r * 0.15, 0))], [r * 0.12, r * 0.04], "prop:iron", STEEL, segments=4)
    mb.lathe([(r * 0.6, 0), (r * 0.5, -r * 0.2), (r * 0.25, -r * 0.4), (0, -r * 0.45)], "prop:flesh", "#401010", segments=24, matrix=Matrix.Translation(front) @ ROT_X)
    if variant == "ray":  # a laser cannon pushing out of the eye
        mb.cylinder(front - Vector((0, r * 0.3, 0)), front + Vector((0, r * 0.75, 0)), r * 0.26, r * 0.2, "prop:dark_iron", GUNMETAL, segments=20)
        for k in range(5):
            y = front.y + r * (0.02 + k * 0.13)
            mb.cylinder((0, y, 0), (0, y + r * 0.05, 0), r * 0.34, None, "prop:iron", STEEL, segments=20)
        mb.cylinder(front + Vector((0, r * 0.72, 0)), front + Vector((0, r * 0.8, 0)), r * 0.15, None, "prop:plain:glow", iris, segments=20)
    else:  # a fanged furnace mouth
        mb.sphere(front - Vector((0, r * 0.2, 0)), r * 0.35, "prop:plain:glow", iris, subdivisions=2, scale=(1, 0.5, 1))
        teeth_ring(mb, front + Vector((0, r * 0.05, 0)), r * 0.55, (0, 1, 0), 14, r * 0.3, r * 0.06, inward=0.75, rng=rng, material="prop:iron", color="#e4e8ee")
        teeth_ring(mb, front - Vector((0, r * 0.12, 0)), r * 0.38, (0, 1, 0), 10, r * 0.2, r * 0.045, inward=0.8, rng=rng, material="prop:iron", color="#e4e8ee")


# ---------------------------------------------------------------------------------------------------- the Destroyer
def destroyer_part(mb: MeshBuilder, p, rng):
    """A plated segment (octagonal armour with red lights and a laser port below), the jawed head or the finned tail;
    or a probe drone."""
    part, r, length = p.get("part", "segment"), p.get("radius", 0.9), p.get("length", 1.3)
    glow = p.get("glow", "#ff2a20")
    if part == "probe":
        organic(mb, lambda b: (b.ball((0, 0, 0), 0.3), b.ellipsoid((0, -0.1, 0), (0.36, 0.2, 0.36))), "prop:iron", tris=700, resolution=0.03, paint=_metal)
        _band(mb, 0.36, -0.08, 0.06, 0.03)
        mb.sphere((0, 0.28, 0), 0.1, "prop:plain:glow", glow, subdivisions=2, scale=(1, 0.5, 1))
        for k in range(4):
            a = 2 * math.pi * k / 4 + math.pi / 4
            mb.cylinder((math.cos(a) * 0.3, -0.05, math.sin(a) * 0.3), (math.cos(a) * 0.5, -0.22, math.sin(a) * 0.5), 0.04, 0.02, "prop:dark_iron", GUNMETAL, segments=5)
        return
    if part == "tail":
        mb.lathe([(0.0, -length * 0.9), (r * 0.3, -length * 0.6), (r * 0.7, -length * 0.1), (r * 0.85, length * 0.5)], "prop:iron", STEEL, segments=8, smooth=False, matrix=ROT_X)
        for k in range(4):
            a = k * math.pi / 2 + math.pi / 4
            radial = Vector((math.cos(a), 0, math.sin(a)))
            mb.tube([radial * r * 0.6 + Vector((0, length * 0.1, 0)), radial * r * 1.1 + Vector((0, -length * 0.6, 0))], [r * 0.1, r * 0.02], "prop:dark_iron", GUNMETAL, segments=4)
        return
    mb.cylinder((0, -length * 0.52, 0), (0, length * 0.52, 0), r * 0.72, None, "prop:dark_iron", GUNMETAL, segments=16)
    for y0 in (-length * 0.46, length * 0.02):  # two rows of chamfered octagonal plates
        mb.lathe([(r * 0.9, y0), (r * 1.0, y0 + length * 0.06), (r * 1.0, y0 + length * 0.36), (r * 0.9, y0 + length * 0.44)], "prop:iron", STEEL, segments=8, smooth=False, matrix=ROT_X)
        for k in range(8):
            a = 2 * math.pi * (k + 0.5) / 8
            mb.sphere((math.cos(a) * r * 0.99, y0 + length * 0.22, math.sin(a) * r * 0.99), r * 0.05, "prop:iron", STEEL_DARK, subdivisions=1)
    for sx in (-1, 1):  # red running lights along the flanks
        mb.box((sx * r * 1.0 - 0.02, -length * 0.08, -r * 0.1), (sx * r * 1.0 + 0.05, length * 0.08, r * 0.1), "prop:plain:glow", glow)
    mb.cylinder((0, 0, -r * 0.8), (0, 0, -r * 1.15), r * 0.18, r * 0.14, "prop:dark_iron", GUNMETAL, segments=10)
    mb.sphere((0, 0, -r * 1.15), r * 0.11, "prop:plain:glow", glow, subdivisions=2)
    if part == "head":
        mb.lathe([(r * 0.95, length * 0.5), (r * 0.9, length * 0.8), (r * 0.6, length * 1.1), (r * 0.3, length * 1.2)], "prop:iron", STEEL, segments=8, smooth=False, matrix=ROT_X)
        for sx in (-1, 1):
            base = Vector((sx * r * 0.55, length * 0.95, -r * 0.2))
            pts = [base, base + Vector((sx * r * 0.45, length * 0.4, 0)), base + Vector((sx * r * 0.15, length * 0.8, 0)), base + Vector((-sx * r * 0.2, length * 0.95, 0))]
            mb.tube(pts, [r * 0.2, r * 0.16, r * 0.1, r * 0.02], "prop:dark_iron", "#d0d4dc", segments=6)
            mb.box((sx * r * 0.5 - r * 0.2, length * 0.85, r * 0.3), (sx * r * 0.5 + r * 0.2, length * 0.92, r * 0.45), "prop:plain:glow", glow)


# ---------------------------------------------------------------------------------------------------- Skeletron Prime
def prime_head(mb: MeshBuilder, p, rng):
    """Skeletron's skull recast in steel: bolted crest plates, riveted cheeks, burning red sockets and a steel jaw."""
    r = p.get("radius", 1.2)
    skeletron_skull(mb, {"radius": r, "layer": "iron", "eyes": p.get("eyes", "#ff2020"), "cracks": 0}, rng)
    for k in range(-2, 3):  # a crest of plates over the dome
        y = k * r * 0.2
        mb.box((-r * 0.1, y - r * 0.08, r * 0.95), (r * 0.1, y + r * 0.08, r * 1.12), "prop:dark_iron", GUNMETAL)
    for sx in (-1, 1):  # bolt clusters and exhausts at the temples
        for k in range(3):
            mb.sphere((sx * r * 0.82, r * (0.1 * k - 0.1), r * 0.25), r * 0.05, "prop:iron", STEEL, subdivisions=1)
        mb.cylinder((sx * r * 0.8, -r * 0.2, r * 0.5), (sx * r * 1.0, -r * 0.35, r * 0.8), r * 0.1, r * 0.08, "prop:dark_iron", GUNMETAL, segments=8)


def prime_tool(mb: MeshBuilder, p, rng):
    """One of Prime's tools, centred, working end toward +Y with a forearm socket behind (the arm joins there)."""
    tool, glow = p.get("tool", "saw"), p.get("glow", "#ff3020")
    mb.sphere((0, -0.35, 0), 0.26, "prop:iron", STEEL, subdivisions=2)
    mb.cylinder((0, -0.35, 0), (0, 0.05, 0), 0.2, 0.18, "prop:dark_iron", GUNMETAL, segments=12)
    if tool == "saw":
        mb.lathe([(0.0, -0.05), (0.72, -0.03), (0.78, 0.0), (0.72, 0.03), (0.0, 0.05)], "prop:iron", "#dfe3ea", segments=40, smooth=False,
                 matrix=Matrix.Translation((0, 0.4, 0)) @ Matrix.Rotation(math.pi / 2, 4, "Y"))
        for k in range(28):
            a = 2 * math.pi * k / 28
            base = Vector((0, 0.4 + math.cos(a) * 0.76, math.sin(a) * 0.76))
            fang(mb, base, Vector((0, 0.4 + math.cos(a + 0.15) * 0.92, math.sin(a + 0.15) * 0.92)), 0.05, "prop:iron", "#f0f2f6", segments=3)
        mb.cylinder((-0.14, 0.4, 0), (0.14, 0.4, 0), 0.2, None, "prop:dark_iron", GUNMETAL, segments=14)
        mb.box((-0.08, -0.05, -0.1), (0.08, 0.4, 0.1), "prop:dark_iron", GUNMETAL)
    elif tool == "vice":
        for sz in (1, -1):
            mb.box((-0.2, -0.05, sz * 0.12), (0.2, 0.8, sz * 0.34), "prop:iron", STEEL)
            mb.box((-0.22, 0.6, sz * 0.04), (0.22, 0.86, sz * 0.34), "prop:dark_iron", GUNMETAL)
            for k in range(5):
                mb.box((-0.2, 0.62 + k * 0.045, sz * 0.04), (0.2, 0.64 + k * 0.045, sz * 0.09), "prop:iron", "#e6eaf0")
        mb.cylinder((-0.28, 0.05, 0), (0.28, 0.05, 0), 0.13, None, "prop:dark_iron", GUNMETAL, segments=12)
        mb.cylinder((0, 0.05, -0.45), (0, 0.05, 0.45), 0.05, None, "prop:iron", BRASS, segments=8)   # the screw
    elif tool == "cannon":
        mb.cylinder((0, -0.1, 0), (0, 1.1, 0), 0.36, 0.3, "prop:dark_iron", GUNMETAL, segments=20)
        for y in (0.15, 0.6, 1.02):
            mb.cylinder((0, y, 0), (0, y + 0.1, 0), 0.42, None, "prop:iron", BRASS, segments=20)
        mb.cylinder((0, 1.08, 0), (0, 1.14, 0), 0.24, None, "prop:plain:glow", "#ff9030", segments=20)
        for sx in (-1, 1):
            mb.sphere((sx * 0.38, 0.3, 0), 0.08, "prop:iron", STEEL, subdivisions=1)
    else:  # laser
        mb.cylinder((0, -0.1, 0), (0, 1.0, 0), 0.17, 0.12, "prop:iron", STEEL, segments=14)
        for k in range(6):
            y = 0.05 + k * 0.15
            mb.cylinder((0, y, 0), (0, y + 0.06, 0), 0.26, None, "prop:iron:metal", "#e08c5a", segments=16)
        mb.sphere((0, 1.02, 0), 0.15, "prop:plain:glow", glow, subdivisions=2)


def prime_strut(mb: MeshBuilder, p, rng):
    """A hydraulic arm piece one unit long along +Y (the game stretches it): a cylinder with a sliding piston rod,
    joint balls at both ends and a couple of cables."""
    r = p.get("radius", 0.15)
    mb.sphere((0, 0, 0), r * 1.3, "prop:iron", STEEL, subdivisions=2)
    mb.cylinder((0, 0.05, 0), (0, 0.6, 0), r, r, "prop:dark_iron", GUNMETAL, segments=12)
    mb.cylinder((0, 0.58, 0), (0, 0.62, 0), r * 1.15, None, "prop:iron", BRASS, segments=12)
    mb.cylinder((0, 0.6, 0), (0, 0.95, 0), r * 0.55, None, "prop:iron", "#e0e4ea", segments=10)
    mb.sphere((0, 1.0, 0), r * 1.1, "prop:iron", STEEL, subdivisions=2)
    for sx in (-1, 1):
        mb.tube([Vector((sx * r * 1.1, 0.1, 0)), Vector((sx * r * 1.5, 0.5, 0.05)), Vector((sx * r * 1.1, 0.9, 0))], [r * 0.18, r * 0.18, r * 0.18], "prop:dark_iron", "#2a2c30", segments=5)


# ---------------------------------------------------------------------------------------------------- Plantera
PETAL, PETAL_DARK, SPOT, JAW_GREEN = "#e0608c", "#8a2448", "#c8e050", "#5a8a2a"
PLANT_BONES = {"bulb": ((0, -1.2, 0), (0, 0.2, 0), None), "jaw.U": ((0, 0.5, 0.3), (0, 1.8, 0.6), "bulb"), "jaw.D": ((0, 0.5, -0.3), (0, 1.8, -0.6), "bulb")}


def plantera(mb: MeshBuilder, p, rng):
    """The bulb: a spotted pink flower head, petals folded around it, jagged green jaws at the front that snap. Open:
    the bulb peels back into a huge fanged maw ringed by tentacles."""
    opened = p.get("open", False)
    mb.bone = "bulb"

    def spots(q, n):
        s = noise3(q, 3.5, 2.0)
        return mix(mix(PETAL_DARK, PETAL, 0.6 + 0.3 * max(0.0, n.z)), SPOT, 1.0 if s > 0.45 else 0.0)

    def bulb(b):
        b.ellipsoid((0, -0.2, 0), (1.3, 1.5, 1.25))
        if opened:
            b.ball((0, 1.3, 0), 1.05, negative=True)
    organic(mb, bulb, "prop:flesh", tris=4000, resolution=0.06, paint=spots, sculpt=lambda q, n: n * 0.05 * noise3(q, 2.5))
    for k in range(7):  # veined petals folded back over the bulb
        a = 2 * math.pi * k / 7
        radial = Vector((math.cos(a), 0, math.sin(a)))
        base = radial * 1.0 + Vector((0, 0.6 if opened else 0.3, 0))
        tip = radial * (1.9 if opened else 1.45) + Vector((0, -0.7, 0))
        side = radial.cross(Vector((0, 1, 0))).normalized() * 0.45
        mb.tube([base, base.lerp(tip, 0.5) + radial * 0.25, tip], [0.28, 0.22, 0.03], "prop:flesh", PETAL, segments=6)
        mb.tube([base + side * 0.2, tip - side * 0.1], [0.05, 0.01], "prop:flesh", PETAL_DARK, segments=4)
    if opened:
        mouth = Vector((0, 0.8, 0))
        mb.lathe([(0.95, 0.3), (0.8, 0.0), (0.5, -0.4), (0.0, -0.5)], "prop:flesh", "#3a0a1a", segments=28, matrix=Matrix.Translation(mouth) @ ROT_X)
        teeth_ring(mb, mouth + Vector((0, 0.3, 0)), 0.9, (0, 1, 0), 18, 0.5, 0.09, inward=0.7, rng=rng)
        teeth_ring(mb, mouth, 0.65, (0, 1, 0), 12, 0.35, 0.07, inward=0.8, rng=rng)
        for k in range(8):  # tentacles lashing out around the maw
            a = 2 * math.pi * k / 8 + 0.2
            radial = Vector((math.cos(a), 0, math.sin(a)))
            pts = [radial * 0.9 + Vector((0, 0.9, 0))]
            for i in range(4):
                pts.append(pts[-1] + radial * 0.35 + Vector((0, 0.4, 0)) + Vector((rng.uniform(-0.2, 0.2), 0, rng.uniform(-0.2, 0.2))))
            mb.tube(pts, [0.16, 0.13, 0.1, 0.07, 0.02], "prop:flesh", JAW_GREEN, segments=6)
        mb.bone = None
        return clips((PLANT_BONES, {"idle": (1.0, _plant_idle), "fly": (1.0, _plant_idle), "attack": (0.5, _plant_idle)}))
    for side, sz in (("U", 1), ("D", -1)):  # the jaws
        mb.bone = f"jaw.{side}"
        base = Vector((0, 0.9, sz * 0.25))

        def jaw(b, base=base, sz=sz):
            b.ellipsoid(base + Vector((0, 0.35, sz * 0.2)), (0.6, 0.5, 0.18))
        organic(mb, jaw, "prop:flesh", tris=700, resolution=0.05, paint=lambda q, n: mix(JAW_GREEN, "#b0d060", 0.3 + 0.3 * n.z))
        for k in range(7):
            x = (k - 3) * 0.14
            q = base + Vector((x, 0.55 - abs(x) * 0.5, 0))
            fang(mb, q, q + Vector((0, 0.05, -sz * 0.22)), 0.045, "prop:plain", "#f4ecd8")
    mb.bone = None
    return clips((PLANT_BONES, {"idle": (1.2, _plant_bite(0.25)), "fly": (0.8, _plant_bite(0.35)), "attack": (0.4, _plant_bite(0.55))}))


def _plant_idle(t):
    return {"bulb": [((1, 0, 0), 0.04 * wave(t))]}


def _plant_bite(open_):
    return lambda t: {"jaw.U": [((1, 0, 0), -open_ * (0.5 + 0.5 * wave(t)))], "jaw.D": [((1, 0, 0), open_ * (0.5 + 0.5 * wave(t)))],
                      "bulb": [((1, 0, 0), 0.03 * wave(t))]}


GENERATORS = {"twin_eye": twin_eye, "destroyer_part": destroyer_part, "prime_head": prime_head, "prime_tool": prime_tool,
              "prime_strut": prime_strut, "plantera_bulb": plantera}

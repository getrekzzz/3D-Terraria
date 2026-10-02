"""Event bosses in the reference game's likeness (P4-36).

Betsy: the Old One's Army's wyvern queen: a scaled red-orange body, long neck, horned head with a toothed jaw and an
ember-lit throat, a spined tail, and great membrane wings on finger bones that beat ('fly').
Mothron: the eclipse's giant moth: a fuzzy body, burning compound eyes, feathered antennae, and broad dusty wings
patterned with bands and eye spots that flap.
Martian Saucer: a gleaming lathe-turned disc with panel seams, a glass dome with its alien pilot, a ring of running
lights that turns, a glowing under-ring, a deathray lens and four turrets (also built alone, 'event_saucer_turret').
Front faces Blender +Y; Betsy and Mothron have their origin under the body, the saucer at its centre.
"""
import math

from mathutils import Matrix, Vector

from . import generator
from .bosses_pre2 import clips, wave
from .detail import fang, mix, noise3, organic, teeth_row
from .mesh import MeshBuilder

UP, FWD, SIDE = Vector((0, 0, 1)), Vector((0, 1, 0)), Vector((1, 0, 0))


def fan_wing(mb, root, sx, theta0, theta1, radius, material, paint, nu=14, nv=7, dihedral=0.12, camber=0.08, thickness=0.01):
    """A two-sided wing sheet fanned out from ``root``: angle θ runs from ``theta0`` (leading edge, 0 = straight out
    to the side, positive sweeps back) to ``theta1``; ``radius(θ)`` is the outline. ``paint(u, v, p)`` colours it
    with u = 0..1 across the fan, v = 0..1 from root to edge. Returns the grid (rows of points) for adding veins."""
    root = Vector(root)
    grid = []
    for i in range(nu + 1):
        u = i / nu
        th = theta0 + (theta1 - theta0) * u
        d = Vector((sx * math.cos(th), -math.sin(th), 0))
        r_edge = radius(th)
        row = []
        for j in range(nv + 1):
            v = j / nv
            r = r_edge * (0.04 + 0.96 * v)
            p = root + d * r + UP * (r * math.tan(dihedral) + camber * r_edge * math.sin(math.pi * v) * 0.5)
            row.append((p, u, v))
        grid.append(row)
    for side in (1, -1):
        off = UP * thickness * 0.5 * side
        verts = [[mb.bm.verts.new(p + off) for p, _, _ in row] for row in grid]
        faces = []
        for i in range(nu):
            for j in range(nv):
                quad = (verts[i][j], verts[i + 1][j], verts[i + 1][j + 1], verts[i][j + 1])
                if (side > 0) == (sx > 0):
                    quad = quad[::-1]
                faces.append(mb.bm.faces.new(quad))
        flat = [v for row in verts for v in row]
        mb._finish_part(flat, material, "#ffffff", True)
        mb.bm.normal_update()
        cols = {verts[i][j]: paint(u, v, p) for i, row in enumerate(grid) for j, (p, u, v) in enumerate(row)}
        for f in faces:
            for loop in f.loops:
                loop[mb.color_layer] = cols[loop.vert]
    return [[p for p, _, _ in row] for row in grid]


# ---------------------------------------------------------------------------------------------------- Betsy
SCALE, SCALE_DARK, BELLY, MEMBRANE, HORN, EMBER = "#c0401c", "#4a1008", "#e8a050", "#a83418", "#3a2a20", "#ff8a1a"
BETSY_BONES = {"body": ((0, -1.0, 1.6), (0, 0.8, 1.7), None), "head": ((0, 1.2, 2.0), (0, 3.6, 2.4), "body"),
               "jaw": ((0, 2.85, 2.25), (0, 3.6, 2.05), "head"), "tail": ((0, -1.0, 1.6), (0, -4.0, 1.2), "body"),
               "wing.R": ((0.5, 0.55, 2.05), (4.2, 0.0, 2.4), "body"), "wing.L": ((-0.5, 0.55, 2.05), (-4.2, 0.0, 2.4), "body")}


def _scales(q, n):
    belly = max(0.0, -n.z) * max(0.0, 1 - abs(n.x) * 1.3)
    tile = 0.5 + 0.5 * math.sin(q.x * 24 + math.sin(q.y * 21) * 2) * math.sin(q.y * 24)
    c = mix(mix(SCALE_DARK, SCALE, 0.55 + 0.3 * tile + 0.15 * noise3(q, 3)), BELLY, belly ** 0.7)
    return mix(c, SCALE_DARK, max(0.0, n.z - 0.6) * 1.2)                    # dark ridge down the back


@generator("event_betsy")
def betsy(mb: MeshBuilder, p, rng):
    """About 8.4 m across the wings and 7.6 m nose to tail tip, the belly 1 m off the ground."""
    mb.bone = "body"

    def body(b):
        b.ellipsoid((0, -0.1, 1.65), (0.75, 1.35, 0.7))
        b.ellipsoid((0, 0.6, 1.85), (0.62, 0.7, 0.6))                    # chest
        for sx in (-1, 1):
            b.capsule((sx * 0.45, -0.7, 1.45), (sx * 0.65, -0.4, 0.75), 0.3)   # thighs
            b.capsule((sx * 0.65, -0.4, 0.75), (sx * 0.6, -0.85, 0.25), 0.16)  # shins
            b.ellipsoid((sx * 0.6, -0.7, 0.12), (0.18, 0.32, 0.1))
            b.ball((sx * 0.5, 0.55, 2.1), 0.3)                            # wing shoulders
    organic(mb, body, "prop:chitin", tris=2600, resolution=0.05, paint=_scales, sculpt=lambda q, n: n * 0.015 * noise3(q, 20))
    for sx in (-1, 1):  # hind claws
        for k in range(3):
            base = Vector((sx * (0.6 + (k - 1) * 0.1), -0.45, 0.1))
            fang(mb, base, base + Vector(((k - 1) * 0.06, 0.2, -0.1)), 0.04, "prop:bone", "#e8dcc0", segments=4)
    mb.bone = "head"

    def neck_head(b):
        b.capsule((0, 0.9, 1.95), (0, 1.8, 2.3), 0.4)
        b.capsule((0, 1.8, 2.3), (0, 2.6, 2.5), 0.3)
        b.ellipsoid((0, 2.9, 2.5), (0.36, 0.45, 0.3))                    # skull
        b.capsule((0, 3.0, 2.48), (0, 3.6, 2.38), 0.17)                  # snout
        for sx in (-1, 1):
            b.ellipsoid((sx * 0.28, 2.7, 2.55), (0.16, 0.25, 0.14))       # cheek frills
    organic(mb, neck_head, "prop:chitin", tris=1800, resolution=0.035, paint=_scales)
    for sx in (-1, 1):
        mb.sphere((sx * 0.2, 3.1, 2.62), 0.07, "prop:plain:glow", "#ffd040", subdivisions=2, scale=(0.7, 1.2, 0.6))   # eyes
        mb.sphere((sx * 0.07, 3.6, 2.46), 0.03, "prop:plain", "#100808", subdivisions=1)                         # nostrils
        horn = [Vector((sx * 0.22, 2.75, 2.7)), Vector((sx * 0.35, 2.4, 2.95)), Vector((sx * 0.4, 1.95, 3.0)), Vector((sx * 0.38, 1.6, 2.85))]
        mb.tube(horn, [0.11, 0.08, 0.05, 0.005], "prop:bone", HORN, segments=7)
        mb.tube([Vector((sx * 0.3, 2.6, 2.55)), Vector((sx * 0.5, 2.3, 2.6)), Vector((sx * 0.55, 2.05, 2.5))], [0.06, 0.04, 0.005], "prop:bone", HORN, segments=5)
    teeth_row(mb, (-0.13, 3.05, 2.32), (-0.08, 3.55, 2.3), 6, 0.08, (0, 0, -1), 0.02, rng)
    teeth_row(mb, (0.13, 3.05, 2.32), (0.08, 3.55, 2.3), 6, 0.08, (0, 0, -1), 0.02, rng)
    for k in range(5):  # embers glowing through the throat
        q = Vector((0, 1.0 + k * 0.4, 1.78 + k * 0.12))
        mb.sphere(q, 0.2 - k * 0.02, "prop:plain:glow", mix(EMBER, "#ff3000", k * 0.15), subdivisions=1, scale=(0.9, 1.3, 0.5))
    for k in range(9):  # spines down the neck
        q = Vector((0, 0.9 + k * 0.22, 2.32 + k * 0.03))
        mb.cylinder(q, q + Vector((0, -0.15, 0.2 - k * 0.01)), 0.06, 0.0, "prop:bone", HORN, segments=5)
    mb.bone = "jaw"
    organic(mb, lambda b: b.capsule((0, 2.85, 2.25), (0, 3.5, 2.2), 0.13), "prop:chitin", tris=400, resolution=0.03, paint=_scales)
    mb.sphere((0, 3.15, 2.3), 0.1, "prop:plain:glow", EMBER, subdivisions=1, scale=(1, 2.5, 0.4))          # glowing mouth
    mb.bone = "tail"
    pts = [Vector((0, -1.2, 1.6)), Vector((0, -2.0, 1.5)), Vector((0.1, -2.8, 1.35)), Vector((0.05, -3.5, 1.25)), Vector((-0.1, -4.0, 1.2))]
    organic(mb, lambda b: [b.capsule(pts[i], pts[i + 1], 0.5 - i * 0.11) for i in range(4)], "prop:chitin", tris=900, resolution=0.045, paint=_scales)
    for k in range(10):  # tail spines and a bony spade at the tip
        t = k / 10
        q = pts[0].lerp(pts[-1], t) + UP * (0.5 - t * 0.38)
        mb.cylinder(q, q + Vector((0, -0.2, 0.28 - t * 0.15)), 0.08 - t * 0.04, 0.0, "prop:bone", HORN, segments=5)
    mb.extrude_polygon([(0, 0.15), (0.3, 0), (0, -0.45), (-0.3, 0)], 0.05, "prop:bone", HORN,
                       matrix=Matrix.Translation(pts[-1]) @ Matrix.Rotation(math.pi / 2, 4, "X"))
    for s, sx in (("R", 1), ("L", -1)):
        mb.bone = f"wing.{s}"
        _betsy_wing(mb, sx)
    mb.bone = None

    def fly(t):
        f = wave(t)
        return {"wing.R": [(FWD, -0.65 * f)], "wing.L": [(FWD, 0.65 * f)], "body": [(SIDE, -0.04 * f)],
                "head": [(SIDE, 0.06 * f)], "tail": [(UP, 0.12 * wave(t, 1, 0.3)), (SIDE, 0.06 * f)]}

    def attack(t):
        pose = fly(t * 2 % 1)
        roar = math.sin(math.pi * t)
        pose["head"] = [(SIDE, 0.25 * roar)]
        pose["jaw"] = [(SIDE, -0.5 * roar)]
        return pose

    return clips((BETSY_BONES, {"fly": (1.1, fly), "idle": (1.6, fly), "attack": (1.1, attack)}))


def _betsy_wing(mb, sx):
    """The arm (humerus, forearm), four long finger bones and the membrane between them, its trailing edge scalloped."""
    s = Vector((sx * 0.55, 0.55, 2.1))
    e = Vector((sx * 1.3, 0.8, 2.85))
    w = Vector((sx * 2.0, 0.45, 3.3))
    mb.tube([s, e, w], [0.2, 0.15, 0.12], "prop:chitin", SCALE, segments=8)
    mb.cylinder(w, w + Vector((sx * 0.15, 0.3, 0.1)), 0.08, 0.0, "prop:bone", HORN, segments=5)       # thumb claw
    fingers = [-0.45, 0.2, 0.8, 1.35]
    lengths = [2.3, 2.55, 2.4, 2.1]

    def outline(th):
        for k in range(len(fingers) - 1):
            a, b = fingers[k], fingers[k + 1]
            if a <= th <= b:
                u = (th - a) / (b - a)
                return (lengths[k] * (1 - u) + lengths[k + 1] * u) * (1 - 0.28 * math.sin(math.pi * u))
        return lengths[-1]

    def paint(u, v, p):
        vein = 0.5 + 0.5 * math.cos((u * (len(fingers) - 1)) * 2 * math.pi)
        return mix(mix(MEMBRANE, "#601408", 0.3 + 0.4 * v), SCALE_DARK, vein ** 8 * 0.6)

    fan_wing(mb, w, sx, fingers[0], fingers[-1], outline, "prop:leather", paint, nu=24, nv=6, dihedral=-0.3, camber=0.12, thickness=0.03)
    # Inner membrane from the last finger back to the flank.
    fan_wing(mb, w, sx, fingers[-1], 2.5, lambda th: (2.1 + 0.15 * (th - 1.35) / 1.15) * (1 - 0.2 * math.sin(math.pi * (th - 1.35) / 1.15)),
             "prop:leather", lambda u, v, p: mix(MEMBRANE, "#601408", 0.3 + 0.4 * v), nu=10, nv=6, dihedral=-0.5, camber=0.1, thickness=0.03)
    for th, ln in zip(fingers, lengths):
        d = Vector((sx * math.cos(th), -math.sin(th), 0))
        tip = w + d * ln + UP * (math.tan(-0.3) * ln)
        mb.tube([w, w.lerp(tip, 0.5) + UP * 0.08, tip], [0.07, 0.05, 0.015], "prop:chitin", SCALE_DARK, segments=6)


# ---------------------------------------------------------------------------------------------------- Mothron
FUZZ, FUZZ_LIGHT, WING_BASE, WING_DARK, EYE = "#6a4a2a", "#c8a070", "#9a7048", "#3a2614", "#ff7a1a"
MOTH_BONES = {"body": ((0, -0.3, 0.4), (0, 0.25, 0.4), None), "wing.R": ((0.06, 0.05, 0.47), (0.5, -0.05, 0.5), "body"),
              "wing.L": ((-0.06, 0.05, 0.47), (-0.5, -0.05, 0.5), "body")}


def _moth_paint(base, dark, fore):
    def paint(u, v, p):
        band = 0.5 + 0.5 * math.sin(v * 9 + u * 2 + 2 * noise3(p, 9))
        c = mix(base, dark, 0.25 + 0.35 * band ** 3)
        c = mix(c, dark, max(0.0, v - 0.88) * 6)                          # dark margin
        if fore:
            spot = math.hypot((u - 0.55) * 1.3, v - 0.62)
            c = mix(c, "#1a0e06", max(0.0, 1 - spot / 0.16))               # eye spot
            c = mix(c, "#e8b060", max(0.0, 1 - abs(spot - 0.17) / 0.04) * 0.9)
            c = mix(c, "#f0d8a8", max(0.0, 1 - math.hypot(u - 0.2, v - 0.95) / 0.12) * 0.7)   # pale tip
        return c
    return paint


@generator("event_mothron")
def mothron(mb: MeshBuilder, p, rng):
    """About 1.1 m across the wings at scale 1 (the game draws it 3.2x), body 0.4 m off the origin, facing +Y."""
    base, dark = p.get("color", WING_BASE), p.get("stripes", WING_DARK)
    eye = p.get("eyes", EYE)
    mb.bone = "body"

    def body(b):
        b.ellipsoid((0, 0.08, 0.42), (0.085, 0.11, 0.085))               # thorax
        b.ellipsoid((0, -0.17, 0.39), (0.07, 0.17, 0.065), stiffness=2.5)  # abdomen
        b.ball((0, 0.21, 0.43), 0.06)                                    # head
    organic(mb, body, "prop:fur", tris=3200, resolution=0.007,
            paint=lambda q, n: mix(FUZZ, FUZZ_LIGHT, 0.3 + 0.3 * noise3(q, 60) + (0.25 if math.sin(q.y * 70) > 0.6 and q.y < -0.05 else 0)),
            sculpt=lambda q, n: n * 0.012 * abs(noise3(q, 140)) ** 0.5)
    for sx in (-1, 1):
        c = Vector((sx * 0.045, 0.245, 0.45))
        mb.sphere(c, 0.03, "prop:plain:glow", eye, subdivisions=2, jitter=0.04, rng=rng)                # compound eyes
        a = Vector((sx * 0.02, 0.26, 0.48))
        spine = [a, a + Vector((sx * 0.05, 0.08, 0.06)), a + Vector((sx * 0.13, 0.12, 0.09)), a + Vector((sx * 0.2, 0.12, 0.08))]
        mb.tube(spine, [0.006, 0.005, 0.004, 0.002], "prop:fur", FUZZ, segments=4)
        for k in range(9):  # feathered antennae
            t = (k + 1) / 10
            q = spine[int(t * 2.99)].lerp(spine[int(t * 2.99) + 1], (t * 2.99) % 1)
            for side in (1, -1):
                mb.cylinder(q, q + Vector((-sx * 0.012 * side, 0.025, 0.012 * side)), 0.004, 0.0, "prop:fur", FUZZ_LIGHT, segments=3, caps=False)
        for k, y in enumerate((0.13, 0.07, 0.01)):  # six legs
            hip = Vector((sx * 0.05, y, 0.37))
            knee = hip + Vector((sx * 0.07, 0.03 - 0.02 * k, -0.04))
            foot = knee + Vector((sx * 0.03, 0.02 - 0.05 * k, -0.08))
            mb.tube([hip, knee, foot], [0.008, 0.006, 0.003], "prop:fur", FUZZ, segments=4)
    mb.sphere((0, 0.22, 0.395), 0.015, "prop:plain", "#2a1a0a", subdivisions=1)              # proboscis
    for s, sx in (("R", 1), ("L", -1)):
        mb.bone = f"wing.{s}"
        root = Vector((sx * 0.06, 0.07, 0.47))
        fan_wing(mb, root, sx, -0.75, 0.55, lambda th: 0.5 * (1 - 0.18 * math.sin((th + 0.75) / 1.3 * math.pi)) * (0.85 + 0.15 * math.cos(th)),
                 "prop:fur", _moth_paint(base, dark, True), nu=18, nv=10, dihedral=0.05, camber=0.02, thickness=0.004)
        fan_wing(mb, root + Vector((0, -0.07, -0.01)), sx, 0.45, 1.3, lambda th: 0.36 * (1 - 0.25 * math.sin((th - 0.45) / 0.85 * math.pi)),
                 "prop:fur", _moth_paint(mix(base, "#c08a50", 0.3), dark, False), nu=12, nv=8, dihedral=0.0, camber=0.02, thickness=0.004)
    mb.bone = None

    def fly(t):
        f = wave(t)
        return {"wing.R": [(FWD, -0.75 * f)], "wing.L": [(FWD, 0.75 * f)], "body": [(SIDE, 0.06 * wave(t, 1, 0.25))]}

    return clips((MOTH_BONES, {"fly": (0.4, fly), "idle": (0.6, fly), "attack": (0.3, fly)}))


# ---------------------------------------------------------------------------------------------------- Martian Saucer
HULL, HULL_DARK, RAY, LIGHTS = "#b8c2cc", "#4a525c", "#50ffa0", ("#40ff80", "#40d8ff")
SAUCER_BONES = {"hull": ((0, -1.0, 0), (0, 1.0, 0), None), "ring": ((0, 0, -0.2), (0, 0, 0.6), "hull")}


def _repaint(mb, verts, fn):
    mb.bm.normal_update()
    for f in {f for v in verts for f in v.link_faces}:
        for loop in f.loops:
            loop[mb.color_layer] = fn(loop.vert.co.copy(), loop.vert.normal.copy())


def _seams(q, n):
    r = math.hypot(q.x, q.y)
    a = math.atan2(q.y, q.x)
    seam = max(abs(math.sin(a * 8)) < 0.04, abs(r - 2.05) < 0.04, abs(r - 1.45) < 0.03)
    c = mix(HULL, "#e8eef4", max(0.0, n.z) * 0.3 + 0.1 * noise3(q, 3))
    return mix(c, HULL_DARK, 0.8 if seam else 0.0)


def turret(mb, base, out, scale=1.0):
    """A ball turret hanging under the hull: armoured dome, twin barrels toward ``out``, a glowing sight."""
    b, o = Vector(base), Vector(out).normalized()
    s = scale
    mb.cylinder(b, b - UP * 0.12 * s, 0.32 * s, 0.3 * s, "prop:dark_iron", HULL_DARK, segments=14)
    mb.sphere(b - UP * 0.15 * s, 0.28 * s, "prop:iron:metal", HULL, subdivisions=2, scale=(1, 1, 0.8))
    aim = (o - UP * 0.35).normalized()
    side = o.cross(UP).normalized()
    for k in (-1, 1):
        p0 = b - UP * 0.18 * s + side * 0.08 * s * k
        mb.cylinder(p0, p0 + aim * 0.55 * s, 0.045 * s, 0.04 * s, "prop:iron:metal", HULL_DARK, segments=8)
        mb.sphere(p0 + aim * 0.56 * s, 0.035 * s, "prop:plain:glow", RAY, subdivisions=1)
    mb.sphere(b - UP * 0.15 * s + aim * 0.26 * s, 0.06 * s, "prop:plain:glow", "#ff4040", subdivisions=1, scale=(1, 1, 0.6))


def _alien(mb, base):
    """The pilot: a slender grey-green alien in a violet suit, huge black eyes, at its glowing console."""
    b = Vector(base)
    mb.cylinder(b, b + UP * 0.35, 0.28, 0.22, "prop:cloth", "#5a3a8a", segments=12)
    for sx in (-1, 1):
        mb.sphere(b + Vector((sx * 0.22, 0, 0.32)), 0.09, "prop:cloth", "#5a3a8a", subdivisions=1)
        mb.tube([b + Vector((sx * 0.24, 0.02, 0.3)), b + Vector((sx * 0.3, 0.2, 0.15)), b + Vector((sx * 0.18, 0.4, 0.12))], [0.05, 0.04, 0.03],
                "prop:plain", "#8ab08a", segments=5)
    mb.cylinder(b + UP * 0.33, b + UP * 0.45, 0.06, 0.05, "prop:plain", "#8ab08a", segments=8)
    h = b + UP * 0.66
    organic(mb, lambda bl: (bl.ellipsoid(h, (0.2, 0.2, 0.25)), bl.ellipsoid(h + Vector((0, 0.08, -0.14)), (0.11, 0.12, 0.1))), "prop:plain",
            tris=500, resolution=0.02, paint=lambda q, n: mix("#9ac09a", "#5a7a5a", max(0.0, -n.z) * 0.6))
    for sx in (-1, 1):
        mb.sphere(h + Vector((sx * 0.09, 0.16, -0.02)), 0.07, "prop:plain", "#08080c", subdivisions=2, scale=(0.8, 0.5, 1.3),
                  rotation=Matrix.Rotation(-0.4 * sx, 4, "Y"))
    mb.box(b + Vector((-0.4, 0.45, 0.05)), b + Vector((0.4, 0.6, 0.18)), "prop:dark_iron", HULL_DARK)
    for k in range(5):
        mb.sphere(b + Vector(((k - 2) * 0.14, 0.53, 0.2)), 0.03, "prop:plain:glow", LIGHTS[k % 2] if k != 2 else "#ff5040", subdivisions=1)


@generator("event_martian_saucer")
def martian_saucer(mb: MeshBuilder, p, rng):
    """6 m across, from 0.45 m below its centre to the dome top 1.9 m above."""
    mb.bone = "hull"
    prof = [(0.0, -0.36), (0.9, -0.42), (2.2, -0.26), (2.85, -0.07), (3.0, 0.03), (2.9, 0.14), (2.2, 0.36), (1.4, 0.54), (1.2, 0.6), (0.0, 0.6)]
    _repaint(mb, mb.lathe(prof, "prop:iron:metal", HULL, segments=64), _seams)
    mb.lathe([(1.25, 0.55), (1.3, 0.62), (1.2, 0.68), (1.1, 0.68)], "prop:dark_iron", HULL_DARK, segments=48)  # dome collar
    for k in range(8):  # vents and fins round the upper hull
        a = k * math.pi / 4 + math.pi / 8
        d = Vector((math.cos(a), math.sin(a), 0))
        mb.box((-0.12, -0.25, 0), (0.12, 0.25, 0.08), "prop:dark_iron", HULL_DARK,
               matrix=Matrix.Translation(d * 1.8 + UP * 0.43) @ Matrix.Rotation(a + math.pi / 2, 4, "Z") @ Matrix.Rotation(0.3, 4, "Y"))
    mb.tube([Vector((math.cos(a) * 1.6, math.sin(a) * 1.6, -0.33)) for a in [i * 2 * math.pi / 40 for i in range(41)]], [0.06] * 41,
            "prop:plain:glow", RAY, segments=6)                           # the glowing under-ring
    mb.lathe([(0.55, -0.37), (0.45, -0.5), (0.25, -0.56), (0.0, -0.58)], "prop:crystal:glow", RAY, segments=24)   # deathray lens
    mb.lathe([(0.7, -0.35), (0.62, -0.42), (0.56, -0.4)], "prop:dark_iron", HULL_DARK, segments=24)
    for k in range(4):
        a = k * math.pi / 2 + math.pi / 4
        d = Vector((math.cos(a), math.sin(a), 0))
        turret(mb, d * 2.1 + UP * -0.25, d)
    _alien(mb, Vector((0, -0.15, 0.6)))
    dome = [(math.cos(a) * 1.12, 0.62 + math.sin(a) * 1.25) for a in [i * math.pi / 2 / 10 for i in range(11)]]
    mb.lathe(dome, "prop:gel:clear", (0.7, 0.95, 0.9, 0.3), segments=40)
    mb.bone = "ring"
    mb.lathe([(2.96, -0.03), (3.04, 0.0), (3.04, 0.1), (2.96, 0.13)], "prop:dark_iron", HULL_DARK, segments=64)
    for k in range(24):
        a = k * 2 * math.pi / 24
        mb.sphere((math.cos(a) * 3.04, math.sin(a) * 3.04, 0.05), 0.07, "prop:plain:glow", LIGHTS[k % 2], subdivisions=1)
    mb.bone = None
    step = 2 * math.pi / 12                                               # the lights repeat every two of 24

    def fly(t):
        return {"ring": [(UP, step * t)], "hull": [(FWD, 0.03 * wave(t)), (SIDE, 0.02 * wave(t, 1, 0.25))]}

    def attack(t):
        return {"ring": [(UP, step * 3 * t)], "hull": [(SIDE, -0.12 + 0.02 * wave(t, 3))]}

    return clips((SAUCER_BONES, {"fly": (1.0, fly), "idle": (1.4, fly), "attack": (0.8, attack)}))


@generator("event_saucer_turret")
def saucer_turret(mb: MeshBuilder, p, rng):
    """One saucer turret alone (a future part), mounted at the origin, barrels toward +Y."""
    turret(mb, (0, 0, 0), (0, 1, 0), p.get("scale", 1.0))
    return None

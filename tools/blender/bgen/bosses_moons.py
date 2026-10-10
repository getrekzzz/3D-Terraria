"""Moon-event tree bosses in the reference game's likeness (P4-35).

Mourning Wood: a gnarled, leafless haunted oak with deep bark furrows, a carved face burning orange from inside, fire
weeping from its eyes, and a skirt of root legs it stalks on. Everscream: an evil pine of drooping snow-dusted tiers
trimmed with baubles, lights and a garland, red glaring eyes and a fanged maw, walking on the same kind of roots.
Both are rigged (trunk, crown, six root legs) with idle / walk / attack clips. Origin at the feet, facing +Y.
"""
import math

from mathutils import Matrix, Vector

from . import generator
from .bosses_pre2 import clips, wave
from .detail import fang, mix, noise3, organic
from .mesh import MeshBuilder

UP, FWD, SIDE = Vector((0, 0, 1)), Vector((0, 1, 0)), Vector((1, 0, 0))
ROOTS = 6


# ---------------------------------------------------------------------------------------------------- shared rig
def _root_dir(i):
    a = math.pi / 2 + (i + 0.5) * 2 * math.pi / ROOTS
    return Vector((math.cos(a), math.sin(a), 0))


def _tree_bones(base_z, top_z, crown_z, reach):
    bones = {"trunk": ((0, 0, base_z * 0.5), (0, 0, top_z), None), "crown": ((0, 0, top_z), (0, 0, crown_z), "trunk")}
    for i in range(ROOTS):
        d = _root_dir(i)
        bones[f"root.{i}"] = (tuple(d * 0.6 + Vector((0, 0, base_z))), tuple(d * reach), "trunk")
    return bones


def _root_legs(mb, base_z, reach, thick, rng, bark="prop:bark", color="#4a3220", paint=None):
    """Six thick, knuckled roots splaying from the trunk foot to the ground, each on its own bone, with rootlets."""
    for i in range(ROOTS):
        mb.bone = f"root.{i}"
        d = _root_dir(i)
        side = d.cross(UP)
        wob = rng.uniform(-0.15, 0.15)
        pts = [d * 0.45 + UP * (base_z + 0.25), d * reach * 0.45 + UP * (base_z * 0.95) + side * wob,
               d * reach * 0.72 + UP * (base_z * 0.62) - side * wob, d * reach * 0.9 + UP * (base_z * 0.22), d * reach * 1.02 + UP * 0.05,
               d * reach * 1.12 + UP * 0.02]
        radii = [thick, thick * 0.86, thick * 0.66, thick * 0.46, thick * 0.26, thick * 0.06]
        verts = mb.tube(pts, radii, bark, color, segments=10)
        if paint:
            _repaint(mb, verts, paint)
        for k in range(3):  # rootlets clawing the ground
            a = (k - 1) * 0.6
            toe = pts[4]
            dir2 = (d * math.cos(a) + side * math.sin(a)).normalized()
            mb.tube([toe, toe + dir2 * thick * 1.4 + UP * 0.05, toe + dir2 * thick * 2.4 - UP * 0.02], [thick * 0.16, thick * 0.1, 0.01],
                    bark, color, segments=5)
        for k in range(2):  # knots
            q = pts[1 + k * 2]
            mb.sphere(q + UP * radii[1 + k * 2] * 0.6, radii[1 + k * 2] * 0.45, bark, mix(color, "#000000", 0.2), subdivisions=1,
                      scale=(1, 1, 0.6))
    mb.bone = None


def _repaint(mb, verts, fn):
    mb.bm.normal_update()
    for f in {f for v in verts for f in v.link_faces}:
        for loop in f.loops:
            loop[mb.color_layer] = fn(loop.vert.co.copy(), loop.vert.normal.copy())


def _tree_clips(bones, lift=0.35, stride=0.3):
    def idle(t):
        return {"trunk": [(FWD, 0.025 * wave(t)), (SIDE, 0.015 * wave(t, 1, 0.25))], "crown": [(FWD, 0.05 * wave(t, 1, 0.15)), (SIDE, 0.03 * wave(t, 2))]}

    def walk(t):
        pose = {"trunk": [(FWD, 0.05 * wave(t)), (SIDE, -0.04 + 0.02 * wave(t, 2))], "crown": [(FWD, -0.06 * wave(t, 1, 0.2)), (SIDE, 0.04 * wave(t, 2, 0.3))]}
        for i in range(ROOTS):
            d = _root_dir(i)
            phase = 0.0 if i % 2 == 0 else 0.5                # two alternating tripods
            up_ = max(0.0, wave(t, 1, phase))
            tangent = UP.cross(d)                            # lifting the foot outward-up
            pose[f"root.{i}"] = [(tuple(tangent), -lift * up_), (tuple(SIDE), -stride * wave(t, 1, phase + 0.25))]
        return pose

    def attack(t):
        rear = math.sin(math.pi * min(1.0, t / 0.4)) if t < 0.4 else 0.0
        lunge = 0.0 if t < 0.3 else math.sin(math.pi * min(1.0, (t - 0.3) / 0.6))
        pose = {"trunk": [(SIDE, 0.16 * rear - 0.22 * lunge)], "crown": [(SIDE, 0.12 * rear - 0.2 * lunge), (FWD, 0.08 * wave(t, 4) * lunge)]}
        for i in range(ROOTS):
            pose[f"root.{i}"] = [(tuple(UP.cross(_root_dir(i))), -0.15 * rear)]
        return pose

    return clips((bones, {"idle": (2.4, idle), "walk": (1.4, walk), "attack": (1.0, attack)}))


# ---------------------------------------------------------------------------------------------------- Mourning Wood
BARK, BARK_DARK, BARK_LIGHT, EMBER, CHAR = "#8a5a2e", "#3a2414", "#b08a62", "#ff8a2a", "#1a0a04"


def _furrows(q):
    a = math.atan2(q.y, q.x)
    return math.sin(a * 13 + 2.5 * noise3(q, 1.3) + q.z * 0.5) * 0.5 + 0.5


def _branch(mb, start, direction, length, radius, depth, rng, color):
    """A crooked branch that forks ``depth`` times, kinking at every joint, thinning to twigs."""
    pts, radii = [Vector(start)], [radius]
    d = Vector(direction).normalized()
    steps = 4
    for k in range(steps):
        d = (d + Vector((rng.uniform(-0.45, 0.45), rng.uniform(-0.45, 0.45), rng.uniform(-0.2, 0.35)))).normalized()
        pts.append(pts[-1] + d * length / steps)
        radii.append(radius * (1 - 0.7 * (k + 1) / steps))
    mb.tube(pts, radii, "prop:bark", color, segments=7 if radius > 0.12 else 5)
    if depth > 0:
        for k in range(2 if depth > 1 else 3):
            at = rng.randint(2, steps)
            nd = (d + Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-0.2, 0.8)))).normalized()
            _branch(mb, pts[at], nd, length * rng.uniform(0.45, 0.65), radii[at] * 0.85, depth - 1, rng, color)


@generator("moon_mourning_wood")
def mourning_wood(mb: MeshBuilder, p, rng):
    """About 5.6 m tall, roots 4.5 m across."""
    glow = p.get("glow", EMBER)
    bark = p.get("bark", BARK)
    base_z, top_z = 1.0, 4.3
    eyes = [Vector((sx * 0.4, 0.78, 3.0)) for sx in (-1, 1)]
    mouth = Vector((0, 0.86, 2.15))
    mb.bone = "trunk"

    def trunk(b):
        b.capsule((0, 0, base_z - 0.2), (0, 0, 2.1), 1.0)
        b.capsule((0, 0, 2.1), (0.12, -0.05, 3.7), 0.82)
        b.capsule((0.12, -0.05, 3.7), (0, -0.1, top_z + 0.3), 0.55)
        b.capsule((0.2, 0, 3.8), (1.25, 0.1, 4.6), 0.3)                  # the great boughs
        b.capsule((-0.2, 0, 3.6), (-1.35, -0.15, 4.3), 0.33)
        b.ellipsoid((0, 0.55, 3.25), (0.75, 0.35, 0.3))                   # the brow ridge
        for i in range(ROOTS):                                           # root flare
            d = _root_dir(i)
            b.capsule(UP * (base_z + 0.9), d * 0.95 + UP * (base_z + 0.05), 0.42)
        for e in eyes:                                                   # eye sockets and the gaping mouth
            b.ball(e + Vector((0, 0.22, 0)), 0.34, negative=True)
        b.ellipsoid(mouth + Vector((0, 0.3, 0)), (0.62, 0.45, 0.38), negative=True)
        b.ball((0.45, 0.6, 1.5), 0.18, negative=True)                     # a knot hole

    def sculpt(q, n):
        f = _furrows(q)
        return -n * (0.07 * f ** 3 + 0.03 * abs(noise3(q, 6.0)))

    def paint(q, n):
        f = _furrows(q)
        c = mix(mix(BARK_LIGHT, bark, 0.5 + 0.3 * noise3(q, 2)), BARK_DARK, f ** 2 * 0.9)
        near = min([(q - e).length for e in eyes] + [(q - mouth).length * 0.8])
        c = mix(c, CHAR, max(0.0, 1 - (near - 0.25) / 0.35))              # scorched around the burning holes
        return mix(c, "#a04010", max(0.0, 1 - (near - 0.12) / 0.15) * 0.8)

    organic(mb, trunk, "prop:bark", tris=5200, resolution=0.045, paint=paint, sculpt=sculpt, smooth=1)
    # The fire inside: glowing pools set back in each hole, a jagged splintered lip on the mouth.
    for e in eyes:
        mb.sphere(e - Vector((0, 0.08, 0)), 0.25, "prop:plain:glow", glow, subdivisions=2, scale=(1.1, 0.5, 0.8))
        mb.sphere(e - Vector((0, 0.05, 0.02)), 0.09, "prop:plain:glow", "#fff0b0", subdivisions=1, scale=(1, 0.5, 0.8))
    mb.sphere(mouth - Vector((0, 0.06, 0)), 0.46, "prop:plain:glow", mix(glow, "#ff3000", 0.4), subdivisions=2, scale=(1.2, 0.45, 0.7))
    for k in range(7):
        x = (k - 3) * 0.13
        for sz in (1, -1):
            base = mouth + Vector((x, 0.42 - abs(x) * 0.35, sz * 0.27))
            fang(mb, base, base + Vector((rng.uniform(-0.04, 0.04), 0.04, -sz * rng.uniform(0.1, 0.2))), 0.05, "prop:bark", BARK_DARK, segments=4)
    for e in eyes:  # fire weeping down the face
        x = e.x * 1.05
        mb.tube([e + Vector((0, 0.08, -0.22)), Vector((x * 1.05, 0.86, e.z - 0.5)), Vector((x * 1.15, 0.84, e.z - 0.95)), Vector((x * 1.3, 0.8, e.z - 1.25))],
                [0.06, 0.05, 0.035, 0.0], "prop:plain:glow", glow, segments=5)
    mb.bone = "crown"
    for start, d, ln, r in (((1.15, 0.1, 4.55), (0.5, 0.1, 1.0), 1.15, 0.24), ((-1.25, -0.15, 4.25), (-0.55, -0.1, 1.0), 1.2, 0.26),
                            ((0, -0.1, top_z + 0.6), (0.1, -0.2, 1.0), 1.1, 0.36), ((0.1, 0.0, top_z + 0.3), (0.2, 0.9, 0.8), 0.85, 0.2),
                            ((-0.1, -0.2, top_z + 0.2), (-0.3, -0.9, 0.7), 0.9, 0.22)):
        _branch(mb, start, d, ln, r, 2, rng, mix(bark, BARK_DARK, 0.25))
    _root_legs(mb, base_z, 1.85, 0.4, rng, color=mix(bark, BARK_DARK, 0.2),
               paint=lambda q, n: mix(mix(BARK_LIGHT, bark, 0.6), BARK_DARK, _furrows(q) ** 2 * 0.8))
    return _tree_clips(_tree_bones(base_z, top_z, 5.6, 1.85))


# ---------------------------------------------------------------------------------------------------- Everscream
NEEDLE, NEEDLE_DARK, SNOW, EYE_RED = "#2a6a3a", "#0e2a16", "#e8f0f8", "#ff2a1a"
TIERS = [(1.75, 1.7), (2.5, 1.45), (3.22, 1.18), (3.9, 0.92), (4.5, 0.66), (5.05, 0.42)]  # (z, radius)


def _tier_r(z):
    """The foliage radius at height z (for placing things on its skin)."""
    best = 0.0
    for tz, r in TIERS:
        if z <= tz + 0.1:
            best = max(best, r * (1 - max(0.0, tz - z) / 0.9) * 1.04)
    return best


@generator("moon_everscream")
def everscream(mb: MeshBuilder, p, rng):
    """About 6.1 m tall to the star, 3.6 m across the lowest boughs."""
    glow = p.get("glow", EYE_RED)
    needle = p.get("needles", NEEDLE)
    base_z, top_z = 0.9, 3.1
    face_z = 2.3
    eyes = [Vector((sx * 0.42, 1.25, face_z + 0.42)) for sx in (-1, 1)]
    maw = Vector((0, 1.32, face_z - 0.3))
    mb.bone = "trunk"

    def trunk(b):
        b.capsule((0, 0, base_z - 0.2), (0, 0, 2.2), 0.55)
        for i in range(ROOTS):
            d = _root_dir(i)
            b.capsule(UP * (base_z + 0.6), d * 0.6 + UP * (base_z + 0.05), 0.3)
    organic(mb, trunk, "prop:pine_bark", tris=1200, resolution=0.05, paint=lambda q, n: mix("#5a4030", "#22160e", _furrows(q) ** 2),
            sculpt=lambda q, n: -n * 0.04 * _furrows(q) ** 3)

    def needles_sculpt(q, n):
        spiky = max(0.0, noise3(q, 11.0)) ** 1.5 + 0.6 * max(0.0, noise3(q, 23.0, 3.0)) ** 2
        return n * 0.14 * spiky - Vector((0, 0, 0.12 * spiky * max(0.0, -n.z)))   # needle tufts that droop

    def needles_paint(q, n):
        c = mix(NEEDLE_DARK, needle, 0.45 + 0.35 * max(0.0, noise3(q, 11.0)) + 0.2 * max(0.0, n.z))
        c = mix(c, "#000000", 0.4 * max(0.0, -n.z))                             # shade under each tier
        near = min((q - e).length for e in eyes + [maw])
        c = mix(c, "#0a0404", max(0.0, 1 - (near - 0.25) / 0.3))
        return mix(c, SNOW, 1.0 if n.z > 0.72 and noise3(q, 4.0) > -0.2 else 0.0)

    def boughs(b, lo, hi):
        for z, r in TIERS[lo:hi]:  # each tier a cone: a wide drooping skirt narrowing upward
            b.ellipsoid((0, 0, z - 0.32), (r, r, 0.16))
            b.ellipsoid((0, 0, z - 0.08), (r * 0.74, r * 0.74, 0.2))
            b.ellipsoid((0, 0, z + 0.2), (r * 0.48, r * 0.48, 0.24))
        if hi == len(TIERS):
            b.capsule((0, 0, TIERS[-1][0]), (0, 0, 5.75), 0.12)
        if lo == 0:
            b.ellipsoid((0, 0.7, face_z + 0.05), (0.9, 0.65, 0.8))       # the face bulging out of the boughs
            for e in eyes:
                b.ball(e + Vector((0, 0.18, 0)), 0.3, negative=True)
            b.ellipsoid(maw + Vector((0, 0.3, 0)), (0.62, 0.45, 0.38), negative=True)
    organic(mb, lambda b: boughs(b, 0, 3), "prop:jungle_leaves", tris=3400, resolution=0.05, paint=needles_paint, sculpt=needles_sculpt, smooth=1)
    for e in eyes:  # glaring eyes, angled into a scowl, and the maw's dark throat lined with fangs
        mb.sphere(e - Vector((0, 0.1, 0)), 0.2, "prop:plain:glow", glow, subdivisions=2, scale=(1.2, 0.5, 0.7),
                  rotation=Matrix.Rotation(0.35 * (1 if e.x > 0 else -1), 4, "Y"))
        mb.sphere(e - Vector((0, 0.02, 0)), 0.07, "prop:plain:glow", "#ffe0a0", subdivisions=1, scale=(1, 0.5, 1))
    mb.sphere(maw - Vector((0, 0.1, 0)), 0.5, "prop:flesh", "#2a0606", subdivisions=2, scale=(1.15, 0.5, 0.75))
    mb.sphere(maw - Vector((0, 0.18, 0.06)), 0.25, "prop:plain:glow", "#c01808", subdivisions=1, scale=(1.2, 0.4, 0.5))
    for k in range(8):
        x = (k - 3.5) * 0.13
        for sz in (1, -1):
            base = maw + Vector((x, 0.35 - abs(x) * 0.4, sz * 0.32))
            fang(mb, base, base + Vector((0, 0.03, -sz * rng.uniform(0.14, 0.24))), 0.045, "prop:bone", "#e8dcc0", segments=4)
    mb.bone = "crown"
    organic(mb, lambda b: boughs(b, 3, len(TIERS)), "prop:jungle_leaves", tris=2200, resolution=0.045, paint=needles_paint, sculpt=needles_sculpt, smooth=1)
    _ornaments(mb, rng)
    mb.bone = "trunk"
    _root_legs(mb, base_z, 1.65, 0.3, rng, bark="prop:pine_bark", color="#4a3424")
    return _tree_clips(_tree_bones(base_z, top_z, 6.0, 1.65), lift=0.3, stride=0.25)


def _ornaments(mb, rng):
    """Baubles hung from the tier edges, a spiral garland of fairy lights and a glowing star on top."""
    colors = ("#d02020", "#e0b030", "#2050d0", "#c0c8d0", "#a020a0")
    for ti, (z, r) in enumerate(TIERS[:-1]):
        n = max(3, int(r * 5))
        for k in range(n):
            a = 2 * math.pi * (k + 0.5 * (ti % 2)) / n + rng.uniform(-0.15, 0.15)
            if abs(math.sin(a) - 1) < 0.25 and ti < 2:
                continue                                                  # keep the face clear
            q = Vector((math.cos(a) * r * 0.98, math.sin(a) * r * 0.98, z - 0.42))
            mb.cylinder(q + UP * 0.12, q + UP * 0.02, 0.02, 0.01, "prop:gold", "#d8b040", segments=5)
            mb.sphere(q - UP * 0.08, 0.13, "prop:iron:metal", rng.choice(colors), subdivisions=2)
    pts, z = [], 1.6
    for k in range(64):  # the garland winding up
        t = k / 63
        z = 1.5 + t * 3.4
        a = t * 2 * math.pi * 3.2
        r = _tier_r(z) * 0.98 + 0.06
        pts.append(Vector((math.cos(a) * r, math.sin(a) * r, z)))
    for k in range(0, len(pts) - 1, 2):
        mb.cylinder(pts[k], pts[k + 1], 0.025, 0.025, "prop:gold", "#c8a030", segments=4, caps=False)
        if k % 4 == 0:
            mb.sphere(pts[k], 0.05, "prop:plain:glow", rng.choice(("#ffe080", "#ff5050", "#60ff80", "#60a0ff")), subdivisions=1)
    top = Vector((0, 0, 5.8))
    star = [(math.cos(math.pi / 2 + k * math.pi / 5) * (0.42 if k % 2 == 0 else 0.18),
             math.sin(math.pi / 2 + k * math.pi / 5) * (0.42 if k % 2 == 0 else 0.18)) for k in range(10)]
    mb.extrude_polygon(star, 0.12, "prop:gold:glow", "#ffd860", matrix=Matrix.Translation(top + UP * 0.25))

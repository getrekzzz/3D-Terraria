"""The temple and jungle-heart bosses rebuilt in the reference game's likeness, as separate pieces the game moves on
their own: Golem's carved Lihzahrd body (bevelled stone blocks with glyph relief cut into them, a glowing sun disc on
the chest, stubby legs; no head or fists), its head (glowing eye slots, a crest of sun rays; ``golem_head_free`` adds
the ember glow of the rocket under the neck once it flies loose) and a stone fist with knuckles and glyph bands; and
Plantera's thorny vine-claw hook and her snapping tentacle bud. Golem's body stands on z = 0; every other piece is
centred at its origin. Front faces Blender +Y.
"""
import math

import bmesh
import bpy
from mathutils import Vector

from .bosses_pre2 import clips, wave
from .detail import fang, mix, noise3, organic, teeth_row
from .mesh import MeshBuilder

UP, FWD, SIDE = (0, 0, 1), (0, 1, 0), (1, 0, 0)

# ---------------------------------------------------------------------------------------------------- Golem stone
STONE, STONE_DARK, GLYPH, SUN, EMBER = "#b8743a", "#6a3e1c", "#3e220e", "#ffb830", "#ff7a1a"


def _glyph(q):
    """0–1 how deep the carving is at a point: brick courses, a meander band and diamond glyphs (Lihzahrd relief)."""
    course = max(0.0, 1.0 - abs(((q.z * 4.0) % 1.0) - 0.5) / 0.035)                                   # horizontal joints
    stagger = 0.5 if int(math.floor(q.z * 4.0)) % 2 else 0.0
    joint = max(0.0, 1.0 - abs((((q.x + q.y) * 2.2 + stagger) % 1.0) - 0.5) / 0.03) if ((q.z * 4.0) % 1.0) > 0.08 else 0.0
    zig = abs(((q.x * 3 + q.y * 3 + abs(((q.z * 6) % 1.0) - 0.5) * 2) % 1.0) - 0.5)
    band = max(0.0, 1.0 - zig / 0.06) if 0.95 < q.z < 1.15 else 0.0                                     # meander band
    diamond = abs(((q.x * 5) % 1.0) - 0.5) + abs(((q.z * 5) % 1.0) - 0.5)
    glyphs = max(0.0, 1.0 - abs(diamond - 0.3) / 0.05) if 1.45 < q.z < 1.95 else 0.0                    # glyph rows
    return min(1.0, max(course, joint, band, glyphs))


def _stone_paint(q, n):
    g = _glyph(q)
    weather = 0.6 + 0.25 * noise3(q, 3) + 0.15 * max(0.0, n.z)
    return mix(mix(STONE_DARK, STONE, weather), GLYPH, 0.85 * g)


def _grid_box(n):
    """A closed unit cube (−0.5…0.5) whose faces are grids of n[axis] cells along each axis, vertices shared at edges."""
    bm = bmesh.new()
    verts = {}

    def vert(c):
        key = tuple(round(x, 5) for x in c)
        if key not in verts:
            verts[key] = bm.verts.new(c)
        return verts[key]

    for axis in range(3):
        u, v = (axis + 1) % 3, (axis + 2) % 3
        for side in (-0.5, 0.5):
            for i in range(n[u]):
                for j in range(n[v]):
                    quad = []
                    for di, dj in ((0, 0), (1, 0), (1, 1), (0, 1)):
                        c = [0.0, 0.0, 0.0]
                        c[axis], c[u], c[v] = side, (i + di) / n[u] - 0.5, (j + dj) / n[v] - 0.5
                        quad.append(vert(c))
                    bm.faces.new(quad if side > 0 else quad[::-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return bm


def _stone(mb, lo, hi, radius=0.07, relief=0.025, spacing=0.06, paint=_stone_paint, glyphs=True):
    """A carved block: a subdivided box rounded to a bevel of ``radius`` and its surface cut ``relief`` deep along the
    glyph pattern (displaced along the normal), weathered and painted per vertex."""
    lo, hi = Vector(lo), Vector(hi)
    size = hi - lo
    bm = _grid_box([max(1, math.ceil(size[k] / spacing)) for k in range(3)])
    centre = (lo + hi) / 2
    inner_lo, inner_hi = lo + Vector((radius,) * 3), hi - Vector((radius,) * 3)
    for v in bm.verts:
        p = Vector((v.co.x * size.x, v.co.y * size.y, v.co.z * size.z)) + centre
        core = Vector((min(max(p.x, inner_lo.x), inner_hi.x), min(max(p.y, inner_lo.y), inner_hi.y), min(max(p.z, inner_lo.z), inner_hi.z)))
        d = p - core
        v.co = core + d.normalized() * radius if d.length > 1e-6 else p
    bm.normal_update()
    for v in bm.verts:
        cut = _glyph(v.co) if glyphs else 0.0
        v.co += v.normal * (-relief * cut + 0.008 * noise3(v.co, 9))
    mesh = bpy.data.meshes.new("stone_block")
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new("stone_block", mesh)
    bpy.context.scene.collection.objects.link(obj)
    return mb.merge(obj, "prop:stone", STONE, paint=paint, smooth=False)


def _sun_disc(mb, centre, r, normal_y=1.0):
    """The glowing sun on Golem's chest: a stone ring, a glowing core and twelve stone rays."""
    c = Vector(centre)
    d = Vector((0, normal_y, 0))
    mb.cylinder(c - d * 0.04, c + d * 0.06, r, None, "prop:stone", STONE_DARK, segments=28)
    mb.cylinder(c + d * 0.02, c + d * 0.1, r * 0.7, None, "prop:plain:glow", SUN, segments=28)
    mb.sphere(c + d * 0.08, r * 0.45, "prop:plain:glow", "#fff0a0", subdivisions=2, scale=(1, 0.25, 1))
    for k in range(12):
        a = 2 * math.pi * k / 12
        base = c + Vector((math.cos(a) * r * 1.05, 0.02, math.sin(a) * r * 1.05))
        mb.cylinder(base, base + Vector((math.cos(a) * r * 0.55, 0, math.sin(a) * r * 0.55)), r * 0.16, r * 0.04, "prop:stone", STONE,
                    segments=4, smooth=False)


GOLEM_BONES = {"root": ((0, 0, 0), (0, 0, 0.6), None),
               "torso": ((0, 0, 0.75), (0, 0, 1.9), "root"),
               "leg.R": ((0.45, 0, 0.75), (0.45, 0, 0.3), "root"), "leg.L": ((-0.45, 0, 0.75), (-0.45, 0, 0.3), "root")}


def golem_body(mb: MeshBuilder, p, rng):
    """About 2.2 m wide and 1.95 m tall at scale 1: a carved torso block with a sun disc, shoulder blocks where the
    fists' chains hang, a waist course and two stubby legs on wide feet. The head and fists are separate models."""
    mb.bone = "torso"
    _stone(mb, (-0.88, -0.6, 0.72), (0.88, 0.6, 1.92), radius=0.09, spacing=0.09)          # torso
    _stone(mb, (-0.95, -0.66, 0.66), (0.95, 0.66, 0.84), radius=0.04, spacing=0.1, glyphs=False)   # waist course
    _stone(mb, (-0.62, -0.5, 1.88), (0.62, 0.5, 2.0), radius=0.03, spacing=0.12, glyphs=False)     # neck plinth
    _sun_disc(mb, (0, 0.6, 1.36), 0.3)
    for sx in (-1, 1):
        _stone(mb, (sx * 0.86 - 0.22, -0.38, 1.42), (sx * 0.86 + 0.22, 0.38, 1.88), radius=0.05, spacing=0.1)   # shoulders
        mb.cylinder((sx * 1.1, 0, 1.62), (sx * 1.18, 0, 1.62), 0.12, None, "prop:dark_iron", "#4a4a50", segments=10)   # chain ring
        for k in range(3):  # glyph studs down the flanks
            mb.box((sx * 0.88 - 0.02, -0.12, 1.0 + k * 0.25), (sx * 0.88 + 0.03, 0.12, 1.12 + k * 0.25), "prop:stone", GLYPH)
    for side, sx in (("R", 1), ("L", -1)):
        mb.bone = f"leg.{side}"
        _stone(mb, (sx * 0.45 - 0.27, -0.36, 0.22), (sx * 0.45 + 0.27, 0.36, 0.74), radius=0.06, spacing=0.09)   # stubby leg
        _stone(mb, (sx * 0.45 - 0.36, -0.5, 0.0), (sx * 0.45 + 0.36, 0.58, 0.24), radius=0.05, spacing=0.09)     # foot
        for k in range(3):  # toes
            x = sx * 0.45 + (k - 1) * 0.22
            mb.box((x - 0.08, 0.56, 0.02), (x + 0.08, 0.66, 0.16), "prop:stone", STONE_DARK)
    mb.bone = None

    def walk(t):
        s = wave(t)
        return {"leg.R": [(SIDE, 0.32 * s)], "leg.L": [(SIDE, -0.32 * s)], "torso": [(FWD, 0.06 * s)]}

    def jump(t):
        # Squash on take-off and landing (the legs splay, the torso pitches down), stretched tall at the top.
        squash = math.cos(2 * math.pi * t)
        return {"leg.R": [(FWD, 0.35 * max(0.0, squash))], "leg.L": [(FWD, -0.35 * max(0.0, squash))],
                "torso": [(SIDE, 0.18 * max(0.0, squash) - 0.06 * max(0.0, -squash))]}

    idle = lambda t: {"torso": [(SIDE, 0.02 * wave(t))]}
    attack = lambda t: {"torso": [(SIDE, -0.12 * math.sin(math.pi * t))]}
    return clips((GOLEM_BONES, {"idle": (2.0, idle), "walk": (1.2, walk), "jump": (0.8, jump), "attack": (0.6, attack)}))


def golem_head(mb: MeshBuilder, p, rng):
    """The carved head, centred at the origin (about 1.3 m wide): a squared stone face with deep glowing eye slots
    under heavy brows, a grim mouth slit, glyph cheeks and a fan of sun rays on the crown. ``free``: the rocket ember
    glows under the neck."""
    glow = p.get("glow", SUN)
    _stone(mb, (-0.62, -0.46, -0.42), (0.62, 0.46, 0.42), radius=0.08, spacing=0.07)
    for sx in (-1, 1):
        mb.box((sx * 0.3 - 0.17, 0.4, 0.02), (sx * 0.3 + 0.17, 0.5, 0.18), "prop:stone", "#1e1006")              # eye slot
        mb.box((sx * 0.3 - 0.14, 0.44, 0.05), (sx * 0.3 + 0.14, 0.52, 0.15), "prop:plain:glow", glow)            # eye glow
        mb.box((sx * 0.3 - 0.22, 0.44, 0.2), (sx * 0.3 + 0.22, 0.56, 0.28), "prop:stone", STONE_DARK)            # brow
        mb.box((sx * 0.62 - 0.04, -0.2, -0.3), (sx * 0.62 + 0.05, 0.2, 0.25), "prop:stone", GLYPH)               # cheek glyph
        mb.box((sx * 0.52 - 0.06, 0.44, -0.36), (sx * 0.52 + 0.06, 0.52, -0.06), "prop:stone", STONE_DARK)        # jaw corner
    mb.box((-0.32, 0.43, -0.26), (0.32, 0.5, -0.16), "prop:stone", "#1e1006")                                      # mouth slit
    for k in range(4):  # teeth blocks in the slit
        x = -0.24 + k * 0.16
        mb.box((x - 0.05, 0.45, -0.25), (x + 0.05, 0.51, -0.2), "prop:stone", STONE)
    for k in range(9):  # crown of sun rays
        a = (k - 4) * 0.28
        base = Vector((math.sin(a) * 0.5, -0.05, 0.38 + math.cos(a) * 0.05))
        tip = Vector((math.sin(a) * 0.95, -0.05, 0.62 + math.cos(a) * 0.32))
        mb.cylinder(base, tip, 0.11, 0.03, "prop:stone", STONE if k % 2 else STONE_DARK, segments=4, smooth=False)
    mb.cylinder((0, -0.05, 0.42), (0, 0.05, 0.42), 0.2, None, "prop:plain:glow", glow, segments=16)                 # crown jewel
    if p.get("free"):
        mb.cylinder((0, 0, -0.42), (0, 0, -0.55), 0.3, 0.22, "prop:dark_iron", "#3a2a20", segments=14)               # nozzle
        mb.cylinder((0, 0, -0.5), (0, 0, -0.95), 0.24, 0.02, "prop:plain:glow", EMBER, segments=12, caps=False)       # exhaust
        mb.sphere((0, 0, -0.55), 0.2, "prop:plain:glow", "#ffd070", subdivisions=2, scale=(1, 1, 0.6))
        for k in range(6):  # sparks
            a = 2 * math.pi * k / 6 + rng.uniform(-0.2, 0.2)
            mb.sphere((math.cos(a) * 0.22, math.sin(a) * 0.22, -0.7 - rng.uniform(0, 0.2)), 0.035, "prop:plain:glow", EMBER, subdivisions=1)
    return None


def golem_fist(mb: MeshBuilder, p, rng):
    """A stone fist centred at the origin (about 0.9 m), knuckles forward (+Y): a carved block hand with four knuckle
    stones, a folded thumb, two glyph bands round the wrist and the chain ring at the back."""
    _stone(mb, (-0.36, -0.3, -0.36), (0.36, 0.34, 0.34), radius=0.07, spacing=0.07)                 # the hand
    for k in range(4):  # knuckles
        z = 0.24 - k * 0.165
        _stone(mb, (-0.34, 0.3, z - 0.075), (0.34, 0.44, z + 0.075), radius=0.035, spacing=0.1, glyphs=False)
        mb.box((-0.36, 0.33, z - 0.088), (0.36, 0.42, z - 0.078), "prop:stone", GLYPH)                 # finger seams
    _stone(mb, (0.3, -0.05, -0.34), (0.44, 0.38, -0.12), radius=0.04, spacing=0.08, glyphs=False)       # thumb
    for y in (-0.3, -0.42):  # wrist bands with glyph studs
        mb.cylinder((0, y, 0), (0, y - 0.08, 0), 0.4, None, "prop:stone", STONE_DARK, segments=16)
        for k in range(8):
            a = 2 * math.pi * k / 8
            mb.box((math.cos(a) * 0.4 - 0.03, y - 0.075, math.sin(a) * 0.4 - 0.03), (math.cos(a) * 0.4 + 0.03, y - 0.005, math.sin(a) * 0.4 + 0.03),
                   "prop:plain:glow" if k % 2 else "prop:stone", SUN if k % 2 else GLYPH)
    mb.cylinder((0, -0.5, 0), (0, -0.58, 0), 0.14, None, "prop:dark_iron", "#4a4a50", segments=10)          # chain ring
    return None


# ---------------------------------------------------------------------------------------------------- Plantera parts
VINE, VINE_DARK, THORN, CLAW, BUD, BUD_DARK = "#4a8a2a", "#22441a", "#d8d090", "#e8e0b8", "#e86a9a", "#7a2a4a"


def _vine_paint(q, n):
    stripe = 0.5 + 0.5 * math.sin(q.y * 22 + 3 * noise3(q, 4))
    return mix(VINE_DARK, VINE, 0.35 + 0.45 * stripe * (0.5 + 0.5 * n.z) + 0.15 * noise3(q, 12))


def plantera_hook(mb: MeshBuilder, p, rng):
    """The hook, centred at the origin and about 1.4 m long along +Y: a knotted length of vine thickening into a
    three-fingered claw whose curved talons grip forward, thorns all along the vine."""
    spine = [Vector((0, -0.7, 0)), Vector((0.04, -0.35, 0.05)), Vector((-0.03, 0.0, 0.0)), Vector((0, 0.3, 0.02))]
    radii = [0.07, 0.1, 0.13, 0.2]

    def build(b):
        for a0, a1, r0, r1 in zip(spine, spine[1:], radii, radii[1:]):
            b.capsule(a0, a1, (r0 + r1) / 2)
        for k in range(1, 3):  # knots
            b.ball(spine[k], radii[k] * 1.25)
        b.ellipsoid((0, 0.38, 0), (0.26, 0.2, 0.2))     # the palm of the claw

    organic(mb, build, "prop:bark", tris=1900, resolution=0.02, paint=_vine_paint,
            sculpt=lambda q, n: n * 0.006 * noise3(q, 30))
    for k in range(3):  # talons: thick at the palm, hooking forward and inward
        a = (k - 1) * 0.9
        base = Vector((math.sin(a) * 0.18, 0.45, math.cos(a) * 0.12 - 0.02))
        out = Vector((math.sin(a) * 0.32, 0.72, math.cos(a) * 0.2))
        tip = Vector((math.sin(a) * 0.16, 0.95, math.cos(a) * 0.04))
        mb.tube([base, out, tip], [0.08, 0.05, 0.005], "prop:plain", CLAW, segments=6)
    for k in range(16):  # thorns along the vine
        t = rng.uniform(0.05, 0.95)
        i = min(2, int(t * 3))
        c = spine[i].lerp(spine[i + 1], t * 3 - i)
        a = rng.uniform(0, 2 * math.pi)
        d = Vector((math.cos(a), rng.uniform(-0.3, 0.1), math.sin(a))).normalized()
        r = radii[i] * 0.95
        fang(mb, c + d * r * 0.8, c + d * (r + 0.1) + Vector((0, -0.04, 0)), 0.035, "prop:plain", THORN, segments=4)
    return None


TENTACLE_BONES = {"bud": ((0, -0.3, 0), (0, 0.1, 0), None),
                  "jaw.U": ((0, 0.05, 0.06), (0, 0.55, 0.12), "bud"), "jaw.D": ((0, 0.05, -0.06), (0, 0.55, -0.12), "bud")}


def plantera_tentacle(mb: MeshBuilder, p, rng):
    """A snapping bud, centred at the origin and facing +Y (about 1 m long): a fleshy bulb on the stub of its stalk
    that splits into two toothed jaws, spotted pink outside and dark inside; the jaws snap."""
    mb.bone = "bud"

    def bulb(b):
        b.ellipsoid((0, -0.1, 0), (0.28, 0.3, 0.27))
        b.capsule((0, -0.55, 0), (0, -0.2, 0), 0.09)    # the stalk's stub (the game draws the rest as a vine)

    def spots(q, n):
        s = noise3(q, 6.0, 2.0)
        return mix(VINE_DARK, VINE, 0.6) if q.y < -0.3 else mix(mix(BUD_DARK, BUD, 0.55 + 0.3 * n.z), "#f4c0d0", 1.0 if s > 0.5 else 0.0)

    organic(mb, bulb, "prop:flesh", tris=1000, resolution=0.018, paint=spots)
    for k in range(5):  # leaf sepals cupping the bud
        a = 2 * math.pi * k / 5
        base = Vector((math.cos(a) * 0.12, -0.3, math.sin(a) * 0.12))
        mb.tube([base, base + Vector((math.cos(a) * 0.18, 0.12, math.sin(a) * 0.18)), base + Vector((math.cos(a) * 0.22, 0.3, math.sin(a) * 0.22))],
                [0.05, 0.035, 0.005], "prop:leaves", VINE, segments=4)
    for side, sz in (("U", 1), ("D", -1)):
        mb.bone = f"jaw.{side}"

        def jaw(b, sz=sz):
            b.ellipsoid((0, 0.32, sz * 0.12), (0.24, 0.26, 0.11))
            b.ellipsoid((0, 0.32, sz * 0.05), (0.19, 0.22, 0.07), negative=True)   # hollow inside

        organic(mb, jaw, "prop:flesh", tris=700, resolution=0.016,
                paint=lambda q, n, sz=sz: mix(BUD_DARK, BUD, 0.6 + 0.3 * n.z * sz) if n.z * sz > -0.2 else "#3a0a1a")
        teeth_row(mb, (-0.18, 0.18, sz * 0.06), (0.0, 0.56, sz * 0.06), 5, 0.07, (0, 0, -sz), 0.025, rng, color=THORN, jag=0.2)
        teeth_row(mb, (0.18, 0.18, sz * 0.06), (0.0, 0.56, sz * 0.06), 5, 0.07, (0, 0, -sz), 0.025, rng, color=THORN, jag=0.2)
    mb.bone = None

    def snap(t, gape=0.5):
        o = gape * (0.5 + 0.5 * wave(t))
        return {"jaw.U": [(SIDE, -o)], "jaw.D": [(SIDE, o)]}

    return clips((TENTACLE_BONES, {"idle": (1.4, lambda t: snap(t, 0.25)), "fly": (0.6, snap),
                                   "attack": (0.35, lambda t: snap(t, 0.7))}))


GENERATORS = {
    "golem_body": golem_body, "golem_head": golem_head, "golem_fist": golem_fist,
    "plantera_hook": plantera_hook, "plantera_tentacle": plantera_tentacle,
}

"""Late bosses in the reference game's likeness: Golem (a carved lihzahrd stone idol with a sun-disc chest, glowing
eyes and great stone fists), Duke Fishron (a teal pig-snouted shark-dragon with tusks, finned wings and a sweeping
tail), the Empress of Light (a luminous fairy in a flowing gown with huge prismatic butterfly wings) and the Moon Lord
(gaunt, ribbed and grey-green, a bared glowing heart, an eye in his brow and in each palm, a beard of tentacles).
Origins at the feet unless noted; front faces Blender +Y. Fishron and the Empress are drawn scaled up in game.
"""
import colorsys
import math

from mathutils import Matrix, Vector

from .bosses_pre import _eyeball
from .bosses_pre2 import clips, wave
from .detail import fang, mix, noise3, organic, teeth_row
from .mesh import MeshBuilder

UP, FWD, SIDE = (0, 0, 1), (0, 1, 0), (1, 0, 0)

# ---------------------------------------------------------------------------------------------------- Golem
STONE, STONE_DARK, GLYPH, SUN = "#b8743a", "#6a3e1c", "#4a2a12", "#ffb830"


def _carved(q, n):
    """Lihzahrd stone: brick courses and a zigzag glyph band cut in darker lines."""
    course = abs(((q.z * 4.0) % 1.0) - 0.5) > 0.46
    zig = abs(((q.x * 3 + abs(((q.z * 6) % 1.0) - 0.5) * 2) % 1.0) - 0.5) > 0.44 and 1.0 < q.z < 1.35
    return mix(mix(STONE_DARK, STONE, 0.6 + 0.25 * noise3(q, 3) + 0.15 * max(0.0, n.z)), GLYPH, 0.8 if (course or zig) else 0.0)


def _block(mb, lo, hi, bevel=0.06):
    """A stone block with chamfered edges (a smaller core box and slabs on each face)."""
    lo, hi = Vector(lo), Vector(hi)
    mb.box(lo + Vector((bevel, bevel, bevel)), hi - Vector((bevel, bevel, bevel)), "prop:stone", STONE_DARK)
    for axis in range(3):
        a, b = lo.copy(), hi.copy()
        for k in range(3):
            if k != axis:
                a[k] += bevel
                b[k] -= bevel
        verts = mb.box(a, b, "prop:stone", STONE)
        _paint(mb, verts, _carved)


def _paint(mb, verts, fn):
    faces = {f for v in verts for f in v.link_faces}
    mb.bm.normal_update()
    for f in faces:
        for loop in f.loops:
            loop[mb.color_layer] = fn(loop.vert.co.copy(), loop.vert.normal.copy())


def golem(mb: MeshBuilder, p, rng):
    glow = p.get("glow", SUN)
    _block(mb, (-0.95, -0.6, 0.0), (0.95, 0.6, 0.35))                    # feet slab
    for sx in (-1, 1):
        _block(mb, (sx * 0.2 - 0.3, -0.4, 0.3), (sx * 0.2 + 0.3, 0.4, 0.75))   # stubby legs
    _block(mb, (-0.85, -0.58, 0.7), (0.85, 0.58, 1.95))                  # body
    # The sun disc on the chest: a glowing core, a stone ring, rays.
    mb.cylinder((0, 0.56, 1.35), (0, 0.66, 1.35), 0.3, None, "prop:stone", STONE_DARK, segments=24)
    mb.cylinder((0, 0.62, 1.35), (0, 0.7, 1.35), 0.2, None, "prop:plain:glow", glow, segments=24)
    for k in range(12):
        a = 2 * math.pi * k / 12
        mb.cylinder((math.cos(a) * 0.32, 0.6, 1.35 + math.sin(a) * 0.32), (math.cos(a) * 0.48, 0.6, 1.35 + math.sin(a) * 0.48), 0.05, 0.0, "prop:stone", STONE, segments=4, smooth=False)
    _block(mb, (-0.6, -0.48, 1.95), (0.6, 0.48, 2.75))                   # head
    for sx in (-1, 1):
        mb.box((sx * 0.28 - 0.14, 0.46, 2.3), (sx * 0.28 + 0.14, 0.52, 2.46), "prop:plain:glow", glow)   # eyes
        mb.box((sx * 0.28 - 0.18, 0.47, 2.48), (sx * 0.28 + 0.18, 0.55, 2.54), "prop:stone", STONE_DARK)  # brows
        _block(mb, (sx * 0.85, -0.35, 1.45), (sx * 1.25, 0.35, 1.9), 0.04)    # shoulders
        _fist(mb, Vector((sx * 1.45, 0.05, 1.0)), sx)
    mb.box((-0.3, 0.47, 2.05), (0.3, 0.53, 2.15), "prop:stone", GLYPH)   # mouth slit
    for k in range(7):  # a crown of sun rays
        a = (k - 3) * 0.3
        mb.cylinder((math.sin(a) * 0.45, 0, 2.75), (math.sin(a) * 0.8, 0, 3.15 + 0.08 * (3 - abs(k - 3))), 0.09, 0.02, "prop:stone", STONE, segments=4, smooth=False)
    return None


def _fist(mb, c, sx):
    _block(mb, c + Vector((-0.3, -0.42, -0.4)), c + Vector((0.3, 0.42, 0.35)), 0.05)
    for k in range(4):  # knuckles across the front
        z = c.z + 0.25 - k * 0.18
        mb.box((c.x - 0.28, c.y + 0.4, z - 0.07), (c.x + 0.28, c.y + 0.5, z + 0.07), "prop:stone", STONE)


# ---------------------------------------------------------------------------------------------------- Duke Fishron
TEAL, TEAL_DARK, BELLY, FIN = "#4ab8a8", "#1e5a5a", "#e8dcb0", "#6ad0c0"
DUKE_BONES = {"body": ((0, -0.2, 0.3), (0, 0.3, 0.3), None), "tail": ((0, -0.2, 0.3), (0, -0.8, 0.32), "body"),
              "wing.R": ((0.12, 0.05, 0.4), (0.6, -0.1, 0.5), "body"), "wing.L": ((-0.12, 0.05, 0.4), (-0.6, -0.1, 0.5), "body")}


def duke_fishron(mb: MeshBuilder, p, rng):
    """About 1.3 m long at scale 1 (the game draws him four times larger)."""
    mb.bone = "body"

    def body(b):
        b.ellipsoid((0, 0.05, 0.3), (0.26, 0.45, 0.27))
        b.ellipsoid((0, 0.38, 0.3), (0.2, 0.22, 0.19))                  # head
        b.capsule((0, 0.5, 0.28), (0, 0.62, 0.25), 0.07)                 # pig snout

    def skin(q, n):
        return mix(mix(TEAL_DARK, TEAL, 0.5 + 0.5 * n.z), BELLY, max(0.0, -n.z) ** 1.5 * 0.9)
    organic(mb, body, "prop:gel", tris=2400, resolution=0.012, paint=skin, sculpt=lambda q, n: n * 0.004 * noise3(q, 40))
    mb.cylinder((0, 0.62, 0.25), (0, 0.66, 0.25), 0.075, None, "prop:flesh", "#e8a0a0", segments=12)   # snout disc
    for sx in (-1, 1):
        mb.sphere((sx * 0.025, 0.665, 0.25), 0.015, "prop:plain", "#301010", subdivisions=1)            # nostrils
        mb.sphere((sx * 0.1, 0.42, 0.37), 0.035, "prop:plain:glow", "#ff3040", subdivisions=2)            # red eyes
        fang(mb, (sx * 0.07, 0.55, 0.2), (sx * 0.1, 0.62, 0.3), 0.02, "prop:plain", "#f4ecd8")          # tusks
        for k in range(2):  # whisker tentacles
            base = Vector((sx * 0.06, 0.52, 0.2 - k * 0.03))
            mb.tube([base, base + Vector((sx * 0.08, 0.02, -0.08)), base + Vector((sx * 0.12, -0.05, -0.18))], [0.012, 0.008, 0.002], "prop:flesh", TEAL_DARK, segments=4)
    teeth_row(mb, (-0.08, 0.5, 0.2), (0.08, 0.5, 0.2), 6, 0.035, (0, 0.2, -1), 0.012, rng)
    mb.extrude_polygon([(-0.2, 0.0), (0.15, 0.0), (0.0, 0.2), (-0.12, 0.16)], 0.02, "prop:gel", FIN, matrix=Matrix.Translation((0, 0.0, 0.46)) @ Matrix.Rotation(math.pi / 2, 4, "Z"))
    mb.bone = "tail"

    def tail(b):
        b.capsule((0, -0.3, 0.3), (0, -0.72, 0.33), 0.08)
    organic(mb, tail, "prop:gel", tris=700, resolution=0.012, paint=skin)
    for sz in (1, -1):  # the forked tail fin
        mb.extrude_polygon([(0, 0), (-0.25, sz * 0.28), (-0.18, sz * 0.05)], 0.015, "prop:gel", FIN,
                           matrix=Matrix.Translation((0, -0.7, 0.33)) @ Matrix.Rotation(math.pi / 2, 4, "Z"))
    for side, sx in (("R", 1), ("L", -1)):
        mb.bone = f"wing.{side}"
        root = Vector((sx * 0.12, 0.08, 0.4))
        pts = [(0, 0), (0.25, 0.12), (0.5, 0.1), (0.62, -0.05), (0.45, -0.12), (0.3, -0.06), (0.18, -0.14), (0.05, -0.08)]
        m = Matrix.Translation(root) @ Matrix.Rotation(-0.3 * sx, 4, "Z") @ Matrix.Diagonal((sx, 1, 1, 1)) @ Matrix.Rotation(math.pi / 2 - 0.2, 4, "X")
        mb.extrude_polygon(pts, 0.012, "prop:gel", FIN, matrix=m)
        for vx, vz in ((0.5, 0.1), (0.62, -0.05), (0.45, -0.12)):  # finger bones through the membrane
            mb.tube([m @ Vector((0, 0.01, 0)), m @ Vector((vx, 0.01, vz))], [0.015, 0.005], "prop:gel", TEAL_DARK, segments=4)
    mb.bone = None

    def fly(t):
        f = wave(t)
        return {"wing.R": [(FWD, -0.6 * f)], "wing.L": [(FWD, 0.6 * f)], "tail": [(UP, 0.35 * wave(t, 1, 0.25))], "body": [(UP, 0.05 * wave(t, 1, 0.75))]}

    def dash(t):
        return {"wing.R": [(FWD, -0.9)], "wing.L": [(FWD, 0.9)], "tail": [(UP, 0.5 * wave(t, 2))]}
    return clips((DUKE_BONES, {"fly": (0.8, fly), "idle": (1.2, fly), "attack": (0.4, dash)}))


# ---------------------------------------------------------------------------------------------------- Empress of Light
EMPRESS_BONES = {"body": ((0, 0, 0.35), (0, 0, 0.75), None),
                 "wing.R": ((0.05, -0.05, 0.65), (0.6, -0.1, 0.85), "body"), "wing.L": ((-0.05, -0.05, 0.65), (-0.6, -0.1, 0.85), "body")}


def _rainbow(t, sat=0.55, val=1.0):
    r, g, b = colorsys.hsv_to_rgb(t % 1.0, sat, val)
    return (r, g, b, 0.6)


def empress_of_light(mb: MeshBuilder, p, rng):
    """About 0.9 m tall at scale 1 (drawn three times larger): a glowing figure with flowing hair and gown, a halo of
    light, and two pairs of prismatic wings that shade through the rainbow toward their tips."""
    glow = p.get("glow", "#fff4ff")
    mb.bone = "body"

    def figure(b):
        b.ball((0, 0.02, 0.81), 0.065)                                     # head
        b.capsule((0, 0, 0.73), (0, 0, 0.6), 0.05)                          # chest
        b.capsule((0, 0, 0.6), (0, 0, 0.5), 0.038, stiffness=1.0)          # waist
        b.ellipsoid((0, -0.03, 0.34), (0.12, 0.1, 0.16))                   # the gown, flaring and trailing back
        b.ellipsoid((0, -0.1, 0.14), (0.2, 0.2, 0.08))
        b.capsule((0, -0.1, 0.14), (0, -0.32, 0.02), 0.07)
        for sx in (-1, 1):
            b.capsule((sx * 0.06, 0, 0.7), (sx * 0.16, 0.08, 0.58), 0.02)  # arms, open to the sides
            b.capsule((sx * 0.16, 0.08, 0.58), (sx * 0.24, 0.12, 0.66), 0.017)
    organic(mb, figure, "prop:plain:glow", tris=1800, resolution=0.008,
            paint=lambda q, n: mix(glow, "#ffd8f4", 0.3 * max(0.0, -n.z) + 0.15 * noise3(q, 20)))
    for k in range(9):  # long flowing hair, pink to blue
        a = (k - 4) * 0.28
        base = Vector((math.sin(a) * 0.05, -0.02, 0.84))
        pts = [base, base + Vector((math.sin(a) * 0.06, -0.12, -0.08)), base + Vector((math.sin(a) * 0.1, -0.24, -0.22)), base + Vector((math.sin(a) * 0.12, -0.3, -0.38))]
        mb.tube(pts, [0.025, 0.02, 0.014, 0.003], "prop:plain:glow", _rainbow(0.85 + k * 0.03, 0.4)[:3], segments=5)
    mb.lathe([(0.1, 0.0), (0.11, 0.008), (0.1, 0.016), (0.09, 0.008)], "prop:plain:glow", "#fff8d0", segments=32, matrix=Matrix.Translation((0, -0.05, 0.95)))  # halo
    for side, sx in (("R", 1), ("L", -1)):
        mb.bone = f"wing.{side}"
        for upper, pts in ((True, [(0, 0), (0.12, 0.2), (0.32, 0.36), (0.55, 0.38), (0.62, 0.22), (0.5, 0.06), (0.25, -0.02)]),
                           (False, [(0, -0.02), (0.2, -0.08), (0.36, -0.2), (0.34, -0.36), (0.2, -0.38), (0.08, -0.2)])):
            # Upright membranes (drawn in the XZ plane), swept a little back.
            m = Matrix.Translation((sx * 0.04, -0.06, 0.68)) @ Matrix.Rotation(0.3 * sx, 4, "Z") @ Matrix.Diagonal((sx, 1, 1, 1))
            verts = mb.extrude_polygon(pts, 0.004, "prop:gel:clear", (1, 1, 1, 0.6), matrix=m)
            _paint(mb, verts, lambda q, n: _rainbow(0.55 + abs(q.x) * 1.4 + (0.0 if upper else 0.2)))
            for i in range(1, len(pts) - 1, 2):  # glowing veins
                mb.tube([m @ Vector((0, 0.004, 0)), m @ Vector((pts[i][0], 0.004, pts[i][1]))], [0.006, 0.002], "prop:plain:glow", "#ffffff", segments=3)
    mb.bone = None

    def fly(t):
        f = wave(t)
        return {"wing.R": [(FWD, -0.35 * f)], "wing.L": [(FWD, 0.35 * f)], "body": [(SIDE, 0.04 * wave(t, 1, 0.3))]}
    return clips((EMPRESS_BONES, {"fly": (1.6, fly), "idle": (2.0, fly), "attack": (0.6, fly)}))


# ---------------------------------------------------------------------------------------------------- Moon Lord
SKIN, SKIN_DARK, EYE_GLOW = "#9ab4a8", "#3a4a46", "#4affd8"


def _ml_skin(q, n):
    return mix(mix(SKIN_DARK, SKIN, 0.55 + 0.3 * max(0.0, n.z) + 0.15 * noise3(q, 1.2)), "#c8d8cc", 0.2 * max(0.0, n.y))


def moon_lord(mb: MeshBuilder, p, rng):
    """His body (hitbox 4 m x 5 m, centre 2.5 m up): a gaunt ribbed torso around a bared glowing heart, arms reaching
    toward his floating hands, a neck rising to his head, long legs trailing below."""
    def torso(b):
        b.ellipsoid((0, -0.1, 3.3), (1.7, 1.1, 1.3))                      # chest
        b.ellipsoid((0, -0.15, 1.8), (1.1, 0.8, 0.9))                     # belly and pelvis
        b.ball((0, 1.0, 2.9), 0.85, negative=True)                         # the opened chest
        for sx in (-1, 1):
            b.ball((sx * 1.9, 0, 4.3), 0.75)                                # shoulders
            b.capsule((sx * 1.9, 0, 4.3), (sx * 5.0, 0.6, 5.6), 0.5)        # upper arms
            b.capsule((sx * 5.0, 0.6, 5.6), (sx * 7.1, 1.7, 3.9), 0.38)     # forearms
            b.capsule((sx * 0.8, 0, 1.2), (sx * 1.3, 0.6, -3.0), 0.55)      # thighs
            b.capsule((sx * 1.3, 0.6, -3.0), (sx * 1.2, 0.1, -6.3), 0.34)   # shins
        b.capsule((0, 0, 4.4), (0, 0.3, 7.0), 0.55)                        # neck
    organic(mb, torso, "prop:flesh", tris=9000, resolution=0.13, paint=_ml_skin, sculpt=lambda q, n: n * 0.06 * noise3(q, 1.5))
    mb.sphere((0, 0.7, 2.9), 0.7, "prop:plain:glow", EYE_GLOW, subdivisions=3)            # the heart
    for k in range(5):  # ribs caging it
        z = 2.2 + k * 0.38
        for sx in (-1, 1):
            mb.tube([Vector((sx * 0.3, 1.05, z)), Vector((sx * 1.0, 1.15, z + 0.1)), Vector((sx * 1.6, 0.4, z - 0.1))], [0.09, 0.12, 0.08], "prop:plain", "#d8dcd0", segments=6)
    for k in range(4):  # dark veins down the belly
        x = (k - 1.5) * 0.35
        mb.tube([Vector((x, 0.75, 1.9)), Vector((x * 1.2, 0.8, 1.2)), Vector((x, 0.6, 0.6))], [0.07, 0.06, 0.03], "prop:flesh", SKIN_DARK, segments=4)
    return None


def moon_lord_head(mb: MeshBuilder, p, rng):
    """His head: a tall cranium with ridges swept back, one great eye in the brow under heavy lids, a beard of
    tentacles hanging from the jaw."""
    def head(b):
        b.ellipsoid((0, 0, 3.0), (1.55, 1.45, 2.1))
        b.ball((0, 1.25, 3.2), 0.95, negative=True)                         # the eye socket
        for k in range(5):
            a = (k - 2) * 0.35
            b.capsule((math.sin(a) * 0.8, -0.4, 4.9), (math.sin(a) * 1.3, -1.6, 2.8), 0.22)   # ridges
    organic(mb, head, "prop:flesh", tris=5000, resolution=0.1, paint=_ml_skin, sculpt=lambda q, n: n * 0.05 * noise3(q, 1.8))
    _eyeball(mb, 0.9, EYE_GLOW, rng, vein_trees=6, sclera="#e2e8dc", tris=2400, vein_depth=2, center=(0, 0.55, 3.2))
    for k in range(9):  # the tentacle beard
        x = (k - 4) * 0.28
        base = Vector((x, 0.8 - abs(x) * 0.25, 1.3))
        curl = 0.35 * (1 if k % 2 else -1)
        mb.tube([base, base + Vector((curl * 0.3, 0.35, -1.0)), base + Vector((curl, 0.2, -2.1)), base + Vector((curl * 0.6, 0.55, -2.9))],
                [0.2, 0.15, 0.1, 0.03], "prop:flesh", SKIN if k % 3 else SKIN_DARK, segments=7)
    return None


def moon_lord_hand(mb: MeshBuilder, p, rng):
    """A hand: a gaunt palm facing +Y with an eye set in it, long clawed fingers curling forward, a wrist toward the arm."""
    def hand(b):
        b.ellipsoid((0, 0, 1.8), (1.05, 0.45, 1.1))
        b.ball((0, 0.6, 1.8), 0.55, negative=True)
        b.capsule((0, -0.2, 1.1), (0, -0.9, 0.7), 0.35)                     # wrist
        for k in range(4):
            x = (k - 1.5) * 0.5
            root = Vector((x, 0.05, 2.7))
            j1, j2, j3 = root + Vector((x * 0.15, 0.2, 0.7)), root + Vector((x * 0.2, 0.55, 1.25)), root + Vector((x * 0.2, 0.95, 1.45))
            for a0, a1, r in ((root, j1, 0.2), (j1, j2, 0.16), (j2, j3, 0.11)):
                b.capsule(a0, a1, r)
    organic(mb, hand, "prop:flesh", tris=4000, resolution=0.07, paint=_ml_skin)
    _eyeball(mb, 0.5, EYE_GLOW, rng, vein_trees=4, sclera="#e2e8dc", tris=1500, vein_depth=2, center=(0, 0.25, 1.8))
    for k in range(4):  # claws
        x = (k - 1.5) * 0.5
        tip = Vector((x * 1.2, 0.95, 4.15))
        fang(mb, tip, tip + Vector((0, 0.35, -0.1)), 0.08, "prop:plain", "#e8e0d0")
    return None


GENERATORS = {"golem_idol": golem, "duke_fishron_detailed": duke_fishron, "empress_detailed": empress_of_light,
              "moon_lord_body": moon_lord, "moon_lord_head_detailed": moon_lord_head, "moon_lord_hand_detailed": moon_lord_hand}

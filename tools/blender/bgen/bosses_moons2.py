"""Moon-event bosses in the reference game's likeness, part two (P4-35).

Pumpking: a giant ribbed jack-o'-lantern head with a carved grin burning from inside, a gnarled stem crowned with
curling vines, a tattered dark cloak trailing beneath, and two clawed hands each gripping a scythe. The hands are also
built alone ('moon_pumpking_blade', centred on the grip) so they can become separate parts later.
Santa-NK1: Santa's workshop turned war machine: tracked hull, a riveted turret wearing Santa's face, beard and hat on a
rotating turret bone, a gatling gun that spins up and a rocket pod.
Origins at the bottom (the blade: at the grip), facing +Y.
"""
import math

from mathutils import Matrix, Vector

from . import generator
from .bosses_pre2 import clips, wave
from .detail import fang, mix, noise3, organic
from .mesh import MeshBuilder

UP, FWD, SIDE = Vector((0, 0, 1)), Vector((0, 1, 0)), Vector((1, 0, 0))


def _repaint(mb, verts, fn):
    mb.bm.normal_update()
    for f in {f for v in verts for f in v.link_faces}:
        for loop in f.loops:
            loop[mb.color_layer] = fn(loop.vert.co.copy(), loop.vert.normal.copy())


def _facing(pos, normal):
    """A matrix placing an XZ-plane polygon at ``pos`` facing along ``normal`` (its local +Y points along it)."""
    rot = Vector((0, 1, 0)).rotation_difference(Vector(normal).normalized()).to_matrix().to_4x4()
    return Matrix.Translation(pos) @ rot


# ---------------------------------------------------------------------------------------------------- Pumpking
ORANGE, ORANGE_DARK, FLAME, CLOAK, CLAW = "#e8761a", "#8a3008", "#ffb030", "#221420", "#2a2622"
HEAD_C, HEAD_R = Vector((0, 0, 2.75)), Vector((1.2, 1.05, 0.95))


def _head_surface(x, z):
    """A point on the pumpkin's front for face coordinates (x, z relative to the head centre) and its normal."""
    nx, nz = x / HEAD_R.x, z / HEAD_R.z
    ny = math.sqrt(max(0.05, 1 - nx * nx - nz * nz))
    p = HEAD_C + Vector((x, ny * HEAD_R.y, z))
    n = Vector((nx / HEAD_R.x, ny / HEAD_R.y, nz / HEAD_R.z)).normalized()
    return p, n


def _carve(mb, pts, x, z, glow, scale=1.0):
    """A carved opening: a pale cut wall around a glowing hole, laid on the pumpkin's curve."""
    p, n = _head_surface(x, z)
    m = _facing(p, n)
    mb.extrude_polygon([(a * scale * 1.14, b * scale * 1.14) for a, b in pts], 0.16, "prop:flesh", "#f0b060", matrix=m @ Matrix.Translation((0, -0.04, 0)))
    mb.extrude_polygon([(a * scale, b * scale) for a, b in pts], 0.16, "prop:plain:glow", glow, matrix=m @ Matrix.Translation((0, -0.03, 0)))


def _pumpkin_head(mb, rng, glow):
    def build(b):
        b.ellipsoid(HEAD_C, tuple(HEAD_R))
        b.ellipsoid(HEAD_C - Vector((0, 0, 0.45)), (0.95, 0.85, 0.5))

    def sculpt(q, n):
        d = q - HEAD_C
        a = math.atan2(d.y, d.x)
        lobe = abs(math.cos(a * 5 + 0.3 * noise3(q, 2)))
        top = max(0.0, 1 - abs(d.z) / HEAD_R.z)
        return -n * (0.12 * (1 - lobe) ** 3 * top + 0.01 * noise3(q, 12))

    def paint(q, n):
        d = q - HEAD_C
        a = math.atan2(d.y, d.x)
        groove = (1 - abs(math.cos(a * 5 + 0.3 * noise3(q, 2)))) ** 2
        c = mix(ORANGE, ORANGE_DARK, 0.75 * groove + 0.25 * max(0.0, -n.z))
        return mix(c, "#5a6a20", max(0.0, (d.z - 0.75) / 0.25))           # greenish where the stem joins

    organic(mb, build, "prop:flesh", tris=3600, resolution=0.045, paint=paint, sculpt=sculpt, smooth=1)
    eye = [(-0.24, -0.16), (0.24, -0.16), (0.0, 0.22)]
    for sx in (-1, 1):
        _carve(mb, [(x * sx, z) for x, z in eye] if sx > 0 else eye[::-1], sx * 0.45, 0.28, glow)
    _carve(mb, [(-0.1, -0.08), (0.1, -0.08), (0.0, 0.1)], 0, 0.02, glow)
    grin = []
    for k in range(13):                                                  # upper lip with two square teeth
        t = k / 12
        x = -0.75 + 1.5 * t
        z = 0.1 - 0.2 * (1 - (2 * t - 1) ** 2) * -0.6
        if k in (4, 8):
            grin += [(x - 0.06, z), (x - 0.06, z - 0.17), (x + 0.06, z - 0.17), (x + 0.06, z)]
        else:
            grin.append((x, z))
    for k in range(12, -1, -1):                                          # lower lip, a deep smile with a fang
        t = k / 12
        x = -0.75 + 1.5 * t
        z = -0.05 - 0.38 * (1 - (2 * t - 1) ** 2)
        if k == 6:
            grin += [(x + 0.07, z), (x, z + 0.2), (x - 0.07, z)]
        else:
            grin.append((x, z))
    _carve(mb, [(x, z) for x, z in grin], 0, -0.33, glow)


def _stem_crown(mb, rng):
    top = HEAD_C + Vector((0, 0, HEAD_R.z - 0.08))
    stem = [top, top + Vector((0.05, 0.02, 0.3)), top + Vector((0.12, -0.05, 0.55)), top + Vector((0.3, -0.1, 0.7))]
    v = mb.tube(stem, [0.2, 0.17, 0.13, 0.09], "prop:bark", "#5a5020", segments=8)
    _repaint(mb, v, lambda q, n: mix("#6a6028", "#2a2a10", 0.5 + 0.5 * math.sin(math.atan2(q.y, q.x) * 6)))
    for k in range(6):  # curling vine tendrils, a crown of them
        a = 2 * math.pi * k / 6 + 0.3
        d = Vector((math.cos(a), math.sin(a), 0))
        pts, radii = [], []
        for i in range(9):
            t = i / 8
            ang = t * 4.5
            r = 0.25 + 0.45 * t
            pts.append(top + d * r + Vector((0, 0, 0.1 + 0.35 * math.sin(ang) * (1 - t) + 0.25 * t)) + d.cross(UP) * 0.15 * math.cos(ang))
            radii.append(0.06 * (1 - t) + 0.01)
        mb.tube(pts, radii, "prop:jungle_leaves", "#3a6a1a", segments=5)
        leaf = pts[4]
        mb.sphere(leaf + Vector((0, 0, 0.05)), 0.16, "prop:jungle_leaves", rng.choice(("#3a7a20", "#2a5a18")), subdivisions=1,
                  scale=(1.2, 0.8, 0.25), rotation=Matrix.Rotation(a, 4, "Z"))


def _cloak(mb, rng):
    """A hooded cloak hanging under the head, folded, its hem torn into ragged tongues."""
    prof = [(0.05, 0.12), (1.05, 0.12), (0.95, 0.6), (0.8, 1.2), (0.7, 1.7), (0.78, 2.0), (0.6, 2.2), (0.0, 2.25)]
    seg = 40
    verts = mb.lathe(prof, "prop:cloth", CLOAK, segments=seg)
    for v in verts:
        a = math.atan2(v.co.y, v.co.x)
        r = Vector((v.co.x, v.co.y, 0))
        fold = 0.07 * math.sin(a * 9) * min(1.0, (2.0 - v.co.z) / 1.0)
        if r.length > 1e-3:
            v.co += r.normalized() * fold
        if v.co.z < 0.2 and r.length > 0.5:                              # tattered hem
            k = round((a + math.pi) / (2 * math.pi) * seg)
            v.co.z += (0.0 if k % 2 else 0.35 + 0.2 * rng.random())
    _repaint(mb, verts, lambda q, n: mix(mix(CLOAK, "#4a2a40", 0.5 + 0.5 * math.sin(math.atan2(q.y, q.x) * 9)), "#000000",
                                          max(0.0, 1 - q.z / 0.8) * 0.5))
    for sx in (-1, 1):  # sleeves the hands float out of
        mb.cylinder(Vector((sx * 0.6, 0.05, 1.85)), Vector((sx * 1.35, 0.3, 1.55)), 0.3, 0.42, "prop:cloth", mix(CLOAK, "#4a2a40", 0.3), segments=10, caps=False)


def _hand(mb, grip, sx, rng):
    """A long-clawed black hand around a scythe's haft, the curved blade swept up and back."""
    g = Vector(grip)
    mb.sphere(g + Vector((sx * 0.05, -0.05, 0)), 0.32, "prop:leather", CLAW, subdivisions=2, scale=(0.8, 1.0, 1.25))
    mb.cylinder(g + Vector((-sx * 0.25, -0.1, -0.05)), g + Vector((-sx * 0.65, -0.2, -0.1)), 0.18, 0.24, "prop:leather", CLAW, segments=8)
    for k in range(4):  # fingers curled round the haft, ending in claws
        z = g.z + 0.15 - k * 0.1
        a = g + Vector((sx * 0.12, 0.1, z - g.z))
        b = a + Vector((-sx * 0.05, 0.2, 0))
        c = b + Vector((-sx * 0.2, 0.05, -0.02))
        mb.tube([a, b, c], [0.055, 0.045, 0.035], "prop:leather", CLAW, segments=5)
        fang(mb, c, c + Vector((-sx * 0.1, -0.1, -0.05)), 0.03, "prop:bone", "#d8d0c0", segments=4)
    mb.tube([g + Vector((sx * 0.1, 0.05, 0.2)), g + Vector((-sx * 0.05, 0.22, 0.32)), g + Vector((-sx * 0.15, 0.2, 0.25))], [0.06, 0.045, 0.03],
            "prop:leather", CLAW, segments=5)
    mb.cylinder(g + Vector((0, 0.08, -0.8)), g + Vector((0, 0.08, 0.9)), 0.05, 0.045, "prop:dark_planks", "#4a3020", segments=7)  # the haft
    head = g + Vector((0, 0.08, 0.9))
    mb.sphere(head, 0.09, "prop:iron:metal", "#3a3a40", subdivisions=1)
    blade = []
    for k in range(11):  # a crescent blade curving forward and down from the haft's head
        t = k / 10
        a = -0.2 + t * 2.0
        blade.append((math.sin(a) * 1.3, math.cos(a) * 0.7 - 0.45 * t))
    for k in range(10, -1, -1):
        t = k / 10
        a = -0.2 + t * 2.0
        w = 0.4 * math.sin(math.pi * min(1.0, t * 1.1 + 0.05))
        blade.append((math.sin(a) * (1.3 - w), math.cos(a) * (0.7 - w) - 0.45 * t))
    m = Matrix.Translation(head) @ Matrix.Rotation(math.pi / 2, 4, "Z")      # the blade swings in the YZ plane, edge forward
    mb.extrude_polygon(blade, 0.05, "prop:iron:metal", "#8a8c94", matrix=m)
    mb.extrude_polygon([(x * 1.0, z) for x, z in blade[:11]] + [(x * 0.99, z * 0.99) for x, z in blade[:11]][::-1], 0.056, "prop:plain:glow", FLAME,
                       matrix=m)


@generator("moon_pumpking")
def pumpking(mb: MeshBuilder, p, rng):
    """Head 2.4 m wide on a 2.2 m cloak, hands out to ±2.2 m. ``hands``: False leaves them off (when they are parts)."""
    glow = p.get("glow", FLAME)
    mb.bone = "body"
    _cloak(mb, rng)
    mb.bone = "head"
    _pumpkin_head(mb, rng, glow)
    _stem_crown(mb, rng)
    bones = {"body": ((0, 0, 0.3), (0, 0, 1.8), None), "head": ((0, 0, 1.9), (0, 0, 3.6), "body")}
    if p.get("hands", True):
        for s, sx in (("R", 1), ("L", -1)):
            mb.bone = f"hand.{s}"
            _hand(mb, Vector((sx * 2.05, 0.35, 1.5)), sx, rng)
            bones[f"hand.{s}"] = ((sx * 0.9, 0, 1.8), (sx * 2.05, 0.35, 1.5), "body")
    mb.bone = None

    def idle(t):
        return {"head": [(FWD, 0.05 * wave(t)), (UP, 0.06 * wave(t, 1, 0.3))], "body": [(FWD, 0.03 * wave(t, 1, 0.5))],
                "hand.R": [(SIDE, 0.12 * wave(t, 1, 0.1))], "hand.L": [(SIDE, 0.12 * wave(t, 1, 0.6))]}

    def fly(t):
        return {"head": [(SIDE, -0.06), (FWD, 0.04 * wave(t))], "body": [(SIDE, -0.12 + 0.04 * wave(t, 2))],
                "hand.R": [(SIDE, -0.3 + 0.15 * wave(t)), (UP, 0.1 * wave(t, 1, 0.25))], "hand.L": [(SIDE, -0.3 + 0.15 * wave(t, 1, 0.5)), (UP, -0.1 * wave(t, 1, 0.75))]}

    def attack(t):
        cock = math.sin(math.pi * min(1.0, t / 0.35)) if t < 0.35 else 0.0
        swipe = 0.0 if t < 0.3 else math.sin(math.pi * min(1.0, (t - 0.3) / 0.6))
        return {"head": [(SIDE, -0.1 * swipe), (UP, 0.15 * cock)],
                "hand.R": [(SIDE, 1.0 * cock - 1.3 * swipe), (UP, -0.8 * swipe)], "hand.L": [(SIDE, 1.0 * cock - 1.3 * swipe), (UP, 0.8 * swipe)]}

    actions = {"idle": (2.0, idle), "fly": (1.2, fly), "attack": (0.8, attack)}
    if not p.get("hands", True):
        actions = {k: (ln, lambda t, fn=fn: {b: v for b, v in fn(t).items() if not b.startswith("hand")}) for k, (ln, fn) in actions.items()}
    return clips((bones, actions))


@generator("moon_pumpking_blade")
def pumpking_blade(mb: MeshBuilder, p, rng):
    """One of Pumpking's hands with its scythe, centred on the grip (a free part). ``side``: 1 right, -1 left."""
    _hand(mb, Vector((0, 0, 0)), p.get("side", 1), rng)
    return None


# ---------------------------------------------------------------------------------------------------- Santa-NK1
RED, RED_DARK, WHITE, STEEL, BLACK = "#b8201c", "#5a0c0a", "#f0eee8", "#6a6e76", "#18181c"
SANTA_BONES = {"hull": ((0, -0.5, 0.6), (0, 0.5, 0.6), None), "turret": ((0, -0.15, 1.6), (0, -0.15, 3.0), "hull"),
               "gun": ((0.62, 0.35, 2.0), (0.62, 1.6, 2.0), "turret")}


def _track(mb, sx):
    """One track: a rubber stadium loop studded with cleats, road wheels and a red fender over it."""
    x = sx * 1.25
    loop = [(math.cos(a) * 0.45 + (1.25 if math.cos(a) > 0 else -1.25), math.sin(a) * 0.45 + 0.47) for a in
            [(i + 0.5) * 2 * math.pi / 28 for i in range(28)]]
    to_yz = Matrix.Translation((x, 0, 0)) @ Matrix.Rotation(math.pi / 2, 4, "Z")
    mb.extrude_polygon(loop, 0.62, "prop:leather", "#26262a", matrix=to_yz)
    for k in range(-2, 3):  # cleats along the top and bottom runs
        for z in (0.0, 0.94):
            y = k * 0.55
            mb.box((x - 0.33, y - 0.07, z - 0.04), (x + 0.33, y + 0.07, z + 0.04), "prop:dark_iron", "#3a3c40")
    for y in (-1.25, -0.42, 0.42, 1.25):
        r = 0.4 if abs(y) > 1 else 0.3
        mb.cylinder((x - sx * 0.33, y, 0.47), (x + sx * 0.36, y, 0.47), r, r, "prop:dark_iron", "#2e3034", segments=14)
        mb.cylinder((x + sx * 0.35, y, 0.47), (x + sx * 0.4, y, 0.47), r * 0.55, r * 0.45, "prop:iron:metal", RED, segments=10)
        mb.sphere((x + sx * 0.41, y, 0.47), r * 0.15, "prop:gold:metal", "#d8b040", subdivisions=1)
    mb.box((x - 0.38, -1.75, 0.98), (x + 0.38, 1.75, 1.08), "prop:iron:metal", RED_DARK)


def _hull(mb):
    side = [(-1.6, 0.55), (1.35, 0.55), (1.75, 0.95), (1.25, 1.6), (-1.5, 1.6), (-1.75, 1.2)]   # (y, z), sloped glacis at the front
    m = Matrix.Rotation(math.pi / 2, 4, "Z")
    mb.extrude_polygon(side, 2.0, "prop:iron:metal", RED, matrix=m)
    rim = [Vector((x, y, 1.6)) for x, y in ((1.02, 1.25), (1.02, -1.5), (-1.02, -1.5), (-1.02, 1.25), (1.02, 1.25))]
    mb.tube(rim, [0.09] * len(rim), "prop:fur", WHITE, segments=8, smooth=True)  # fur trim round the deck
    # A black belt with a gold buckle across the glacis.
    mid = Vector((0, 1.35, 0.55)).lerp(Vector((0, 1.75, 0.95)), 0.55)
    tilt = Matrix.Rotation(-math.pi / 4, 4, "X")
    n = Vector((0, 1, -1)).normalized()
    mb.box((-1.02, -0.07, -0.13), (1.02, 0.07, 0.13), "prop:leather", BLACK, matrix=Matrix.Translation(mid) @ tilt)
    mb.box((-0.24, -0.1, -0.2), (0.24, 0.1, 0.2), "prop:gold:metal", "#e0b840", matrix=Matrix.Translation(mid + n * 0.02) @ tilt)
    mb.box((-0.13, -0.12, -0.1), (0.13, 0.12, 0.1), "prop:leather", BLACK, matrix=Matrix.Translation(mid + n * 0.03) @ tilt)
    for sx in (-1, 1):
        for y in (-1.4, -0.7, 0.0, 0.7):  # rivets
            mb.sphere((sx * 1.01, y, 1.4), 0.04, "prop:iron:metal", STEEL, subdivisions=1)
        mb.cylinder((sx * 0.6, -1.55, 1.3), (sx * 0.75, -2.0, 1.75), 0.11, 0.13, "prop:dark_iron", "#2a2a2e", segments=8)   # exhausts
        mb.sphere((sx * 0.75, -2.0, 1.75), 0.09, "prop:plain:glow", "#ff7020", subdivisions=1)
    for k in range(4):  # headlamps
        mb.sphere(((k - 1.5) * 0.4, 1.62, 1.22), 0.09, "prop:plain:glow", "#fff4c0", subdivisions=1, scale=(1, 0.4, 1))


def _santa_head(mb, rng):
    """The turret: a riveted ring wearing Santa's face, beard and hat."""
    mb.cylinder((0, -0.15, 1.58), (0, -0.15, 2.0), 1.0, 0.92, "prop:iron:metal", RED, segments=24)
    mb.cylinder((0, -0.15, 1.95), (0, -0.15, 2.05), 0.95, 0.95, "prop:fur", WHITE, segments=24)
    c = Vector((0, 0.1, 2.55))
    organic(mb, lambda b: b.ellipsoid(c, (0.5, 0.48, 0.55)), "prop:plain", tris=900, resolution=0.03,
            paint=lambda q, n: mix("#e8b090", "#c07860", max(0.0, -n.y) * 0.6))
    for sx in (-1, 1):  # glowing robot eyes under bushy brows, rosy cheeks
        mb.sphere(c + Vector((sx * 0.18, 0.44, 0.12)), 0.08, "prop:plain:glow", "#ff2010", subdivisions=2, scale=(1.2, 0.5, 0.8))
        mb.sphere(c + Vector((sx * 0.2, 0.42, 0.24)), 0.1, "prop:fur", WHITE, subdivisions=1, scale=(1.4, 0.6, 0.5))
        mb.sphere(c + Vector((sx * 0.3, 0.36, -0.05)), 0.1, "prop:plain", "#e07070", subdivisions=1, scale=(1, 0.5, 0.8))
    mb.sphere(c + Vector((0, 0.5, -0.02)), 0.1, "prop:plain", "#d05050", subdivisions=2)

    def beard(b):
        b.ellipsoid(c + Vector((0, 0.25, -0.35)), (0.52, 0.32, 0.4))
        b.ellipsoid(c + Vector((0, 0.32, -0.62)), (0.36, 0.25, 0.3))
        for sx in (-1, 1):
            b.ellipsoid(c + Vector((sx * 0.2, 0.46, -0.12)), (0.22, 0.1, 0.08))   # moustache
            b.ellipsoid(c + Vector((sx * 0.45, 0.0, -0.05)), (0.12, 0.25, 0.3))
    organic(mb, beard, "prop:fur", tris=1400, resolution=0.025, paint=lambda q, n: mix(WHITE, "#b8b8c0", max(0.0, -n.z) * 0.5),
            sculpt=lambda q, n: n * 0.03 * noise3(q, 18) - Vector((0, 0, 0.02 * abs(noise3(q, 30)))))
    hat = [c + Vector((0, -0.05, 0.38)), c + Vector((0, -0.12, 0.75)), c + Vector((0, -0.4, 1.0)), c + Vector((0, -0.75, 0.95)),
           c + Vector((0, -0.95, 0.75))]
    mb.tube(hat, [0.52, 0.42, 0.28, 0.16, 0.07], "prop:red_cloth", "#c01818", segments=14)
    ring = [c + Vector((math.cos(a) * 0.53, -0.05 + math.sin(a) * 0.53, 0.38)) for a in [i * 2 * math.pi / 20 for i in range(21)]]
    mb.tube(ring, [0.1] * len(ring), "prop:fur", WHITE, segments=8)
    mb.sphere(hat[-1], 0.16, "prop:fur", WHITE, subdivisions=2)


def _gatling(mb):
    a, b = Vector((0.62, 0.35, 2.0)), Vector((0.62, 1.7, 2.0))
    mb.cylinder(a - Vector((0, 0.15, 0)), a + Vector((0, 0.45, 0)), 0.24, 0.24, "prop:dark_iron", "#2a2c30", segments=12)
    for k in range(6):
        ang = k * math.pi / 3
        off = Vector((math.cos(ang) * 0.12, 0, math.sin(ang) * 0.12))
        mb.cylinder(a + off, b + off, 0.045, 0.045, "prop:iron:metal", STEEL, segments=6)
    for y in (1.0, 1.6):
        mb.cylinder(Vector((0.62, y, 2.0)), Vector((0.62, y + 0.06, 2.0)), 0.2, 0.2, "prop:iron:metal", RED, segments=12)
    mb.box((0.35, -0.05, 1.8), (0.62, 0.3, 2.15), "prop:iron:metal", RED_DARK)


def _rocket_pod(mb):
    mb.box((-1.15, -0.55, 1.95), (-0.55, 0.45, 2.55), "prop:iron:metal", "#2a5a2a")
    mb.box((-1.17, -0.4, 2.15), (-0.53, 0.47, 2.25), "prop:gold:metal", "#d8b040")
    for i in range(2):
        for j in range(2):
            p0 = Vector((-1.0 + i * 0.3, 0.46, 2.1 + j * 0.3))
            mb.cylinder(p0, p0 + Vector((0, 0.12, 0)), 0.1, 0.1, "prop:dark_iron", BLACK, segments=8)
            mb.cylinder(p0 + Vector((0, 0.1, 0)), p0 + Vector((0, 0.3, 0)), 0.08, 0.0, "prop:iron:metal", "#e04020", segments=8)


@generator("moon_santa_nk1")
def santa_nk1(mb: MeshBuilder, p, rng):
    """3.6 m wide, 3.6 m long and 3.8 m tall to the hat."""
    mb.bone = "hull"
    for sx in (-1, 1):
        _track(mb, sx)
    _hull(mb)
    mb.bone = "turret"
    _santa_head(mb, rng)
    _rocket_pod(mb)
    mb.bone = "gun"
    _gatling(mb)
    mb.bone = None

    def idle(t):
        return {"hull": [(SIDE, 0.01 * wave(t, 2))], "turret": [(UP, 0.12 * wave(t))]}

    def walk(t):
        return {"hull": [(SIDE, 0.025 * wave(t, 2)), (FWD, 0.015 * wave(t, 1))], "turret": [(UP, 0.08 * wave(t, 1, 0.25)), (SIDE, -0.02 * wave(t, 2))]}

    def attack(t):
        kick = 0.03 * wave(t, 8)
        return {"hull": [(SIDE, -0.02 + kick * 0.3)], "turret": [(UP, 0.18 * wave(t)), (SIDE, -kick)], "gun": [(FWD, t * 4 * math.pi / 3)]}

    return clips((SANTA_BONES, {"idle": (2.0, idle), "walk": (1.0, walk), "attack": (1.0, attack)}))

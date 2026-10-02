"""Event bosses in the reference game's likeness, part two (P4-37).

Flying Dutchman: a ragged ghost galleon lying broadside to its target: a lofted plank hull with a raised stern castle,
gun ports with cannons run out, two masts with braced yards and torn, translucent sails, rigging, and lanterns burning
ghost-green. The long axis is X (bow at +X), the cannons face +Y. A lone cannon is 'event_dutchman_cannon'.
Celestial Pillars: a carved, segmented monolith per style (solar flares, vortex spirals, nebula tendrils and brain,
stardust crystals and constellations) with a floating rune ring that turns; ``shield: true`` adds the translucent
shield shell (for a shielded model variant: the renderer has no per-part shield toggle).
Origins at the bottom, facing +Y.
"""
import math

from mathutils import Matrix, Vector

from . import generator
from .bosses_pre2 import clips, wave
from .detail import mix, noise3, organic
from .mesh import MeshBuilder

UP, FWD, SIDE = Vector((0, 0, 1)), Vector((0, 1, 0)), Vector((1, 0, 0))
TO_Y = Matrix.Rotation(-math.pi / 2, 4, "X")   # lathe axis Z -> +Y


def _grid(mb, rows, material, paint, closed=False, flip=False):
    """Quads between consecutive rows of points (each row the same length); ``paint(p) -> colour``."""
    verts = [[mb.bm.verts.new(p) for p in row] for row in rows]
    faces = []
    n = len(rows[0])
    for i in range(len(rows) - 1):
        for j in range(n if closed else n - 1):
            k = (j + 1) % n
            quad = (verts[i][j], verts[i][k], verts[i + 1][k], verts[i + 1][j])
            faces.append(mb.bm.faces.new(quad[::-1] if flip else quad))
    flat = [v for row in verts for v in row]
    mb._finish_part(flat, material, "#ffffff", True)
    for f in faces:
        for loop in f.loops:
            loop[mb.color_layer] = paint(loop.vert.co)
    return verts, faces


# ---------------------------------------------------------------------------------------------------- Flying Dutchman
WOOD, WOOD_DARK, GHOST, SAIL = "#4a3a2c", "#1a1410", "#70ffb0", (0.78, 0.88, 0.84, 0.7)


def _hull_shape(x):
    bow = max(0.0, (x - 1.4) / 3.0)
    w = 1.35 * (1 - bow ** 1.7) * (1 - 0.25 * max(0.0, (-x - 3.0) / 1.3) ** 2) + 0.03
    bottom = 0.25 + 0.7 * bow ** 2 + 0.45 * max(0.0, (-x - 2.8) / 1.5) ** 2
    top = 2.3 + 0.7 * bow ** 2 + (0.8 if x < -2.5 else 0.0)
    return w, bottom, top


def _planks(p):
    seam = (p.z * 4.2 + 0.15 * math.sin(p.x * 0.7)) % 1.0
    tone = 0.5 + 0.5 * noise3(Vector((p.x * 0.4, 0, math.floor(p.z * 4.2))), 3.1)
    c = mix(WOOD, "#6a5640", 0.35 * tone)
    c = mix(c, WOOD_DARK, 0.85 if seam < 0.08 else 0.0)
    return mix(c, "#2a5a48", max(0.0, 1.2 - p.z) * 0.5)                   # ghostly algae below the waterline


def _hull(mb):
    xs = [-4.3 + 8.75 * i / 30 for i in range(31)]
    for shell, flip in ((1.0, False), (0.94, True)):                    # outer skin and the inside of the bulwarks
        rows = []
        for x in xs:
            w, bottom, top = _hull_shape(x)
            row = []
            for k in range(15):
                phi = -math.pi / 2 + math.pi * k / 14
                row.append(Vector((x, w * shell * math.sin(phi), bottom + (top - bottom) * (1 - abs(math.cos(phi)) ** 0.7))))
            rows.append(row)
        _grid(mb, rows, "prop:dark_planks", _planks, flip=flip)
    w, bottom, top = _hull_shape(xs[0])
    stern = [(w * math.sin(-math.pi / 2 + math.pi * k / 14), bottom + (top - bottom) * (1 - abs(math.cos(-math.pi / 2 + math.pi * k / 14)) ** 0.7))
             for k in range(15)]
    mb.extrude_polygon([(y, z) for y, z in stern], 0.12, "prop:dark_planks", WOOD, matrix=Matrix.Translation((xs[0], 0, 0)) @ Matrix.Rotation(math.pi / 2, 4, "Z"))
    for k in range(3):  # glowing stern windows
        mb.box((xs[0] - 0.08, (k - 1) * 0.55 - 0.18, 2.4), (xs[0] - 0.02, (k - 1) * 0.55 + 0.18, 2.8), "prop:plain:glow", GHOST)
    deck = []
    for x in xs[1:-1]:
        w, _, top = _hull_shape(x)
        z = min(top, 2.3 + (0.8 if x < -2.5 else 0.0)) - 0.25
        deck.append([Vector((x, -w * 0.92, z)), Vector((x, w * 0.92, z))])
    _grid(mb, deck, "prop:planks", lambda p: mix("#5a4a38", WOOD_DARK, 0.7 if (p.y * 3.5) % 1 < 0.08 else 0.0))
    for x in [-1.9 + 1.2 * k for k in range(4)]:                          # gun ports with cannons run out
        w, _, _ = _hull_shape(x)
        mb.box((x - 0.25, w - 0.04, 1.5), (x + 0.25, w + 0.06, 1.95), "prop:dark_iron", "#0a0a0a")
        cannon(mb, Vector((x, w - 0.45, 1.6)), 0.8)
    for x in [xs[0] + 0.3 + 0.55 * k for k in range(16)]:                # railing posts and rails
        w, _, top = _hull_shape(x)
        if x > 4.2:
            break
        for sy in (-1, 1):
            mb.cylinder((x, sy * w * 0.97, top), (x, sy * w * 0.97, top + 0.45), 0.035, 0.035, "prop:dark_planks", WOOD, segments=5)
    for sy in (-1, 1):
        rail = [Vector((x, sy * _hull_shape(x)[0] * 0.97, _hull_shape(x)[2] + 0.45)) for x in xs[1:-2]]
        mb.tube(rail, [0.05] * len(rail), "prop:dark_planks", WOOD, segments=5)
    mb.tube([Vector((4.1, 0, 3.0)), Vector((4.7, 0, 3.55))], [0.12, 0.06], "prop:dark_planks", WOOD, segments=6)   # bowsprit
    mb.sphere((4.25, 0, 2.75), 0.22, "prop:bone", "#d8d0b8", subdivisions=2, scale=(1, 0.85, 1.1))              # a skull figurehead
    for sx in (-1, 1):
        mb.sphere((4.42, sx * 0.08, 2.8), 0.05, "prop:plain:glow", GHOST, subdivisions=1)


def _sail(mb, center, width, height, rng, angle):
    """A torn square sail bellied by the wind: a grid with holes and a ragged foot, both sides."""
    rot = Matrix.Rotation(angle, 4, "Z")
    nu, nv = 9, 7
    rows = []
    for j in range(nv + 1):
        v = j / nv
        row = []
        for i in range(nu + 1):
            u = i / nu
            belly = 0.35 * math.sin(math.pi * u) * math.sin(math.pi * min(1.0, v * 1.1))
            rag = (rng.uniform(0.0, 0.45) if j == 0 else 0.0) * height / 3
            row.append(Vector(center) + rot @ Vector(((u - 0.5) * width, belly, (v - 0.5) * height + rag)))
        rows.append(row)
    keep = {(i, j) for i in range(nu) for j in range(nv) if rng.random() > (0.12 if j > 0 else 0.35)}
    for flip in (False, True):
        verts, faces = _grid(mb, rows, "prop:cloth:clear", lambda p: SAIL, flip=flip)
        dead = [faces[j * nu + i] for i in range(nu) for j in range(nv) if (i, j) not in keep]
        for f in dead:
            mb.bm.faces.remove(f)


@generator("event_flying_dutchman")
def flying_dutchman(mb: MeshBuilder, p, rng):
    """9.3 m stem to stern, 7.2 m keel to masthead, 3.4 m in the beam."""
    mb.bone = "hull"
    _hull(mb)
    masts = [(-0.9, 6.9), (1.6, 7.2)]
    for x, h in masts:
        mb.cylinder((x, 0, 1.8), (x, 0, h), 0.13, 0.07, "prop:dark_planks", WOOD, segments=8)
        mb.cylinder((x, 0, h - 0.2), (x, 0, h - 0.05), 0.22, 0.22, "prop:dark_planks", WOOD_DARK, segments=10)   # crow's nest
        for target in ((4.6, 0, 3.5), (-4.2, 0, 3.1), (x, 1.35, 2.6), (x, -1.35, 2.6)):                     # rigging
            mb.tube([Vector((x, 0, h - 0.3)), Vector(target)], [0.015, 0.015], "prop:leather", "#2a2620", segments=3)
    for (lx, lz) in ((-4.15, 3.6), (4.4, 3.4), (0.4, 2.7)):              # ghost lanterns
        mb.cylinder((lx, 0, lz + 0.3), (lx, 0, lz + 0.4), 0.02, 0.02, "prop:iron:metal", "#3a3a3a", segments=4)
        mb.cylinder((lx, 0, lz), (lx, 0, lz + 0.3), 0.12, 0.1, "prop:plain:glow", GHOST, segments=6)
        mb.cylinder((lx, 0, lz + 0.3), (lx, 0, lz + 0.38), 0.13, 0.0, "prop:iron:metal", "#3a3a3a", segments=6)
    mb.bone = "sails"
    for x, h in masts:
        for k, (zc, wd, ht) in enumerate(((h - 3.3, 3.1, 1.9), (h - 1.3, 2.4, 1.5))):
            ang = 0.55
            yard = Matrix.Rotation(ang, 4, "Z") @ Vector((1, 0, 0))
            top = Vector((x, 0, zc + ht / 2 + 0.05))
            mb.cylinder(top - yard * wd * 0.58, top + yard * wd * 0.58, 0.05, 0.05, "prop:dark_planks", WOOD, segments=5)
            _sail(mb, (x, 0, zc), wd, ht, rng, ang)
    mb.bone = None
    bones = {"hull": ((-1.0, 0, 1.0), (1.0, 0, 1.0), None), "sails": ((0.3, 0, 2.5), (0.3, 0, 7.0), "hull")}

    def fly(t):
        return {"hull": [(SIDE, 0.03 * wave(t)), (FWD, 0.02 * wave(t, 1, 0.3))], "sails": [(SIDE, 0.02 * wave(t, 2))]}

    def attack(t):
        kick = math.exp(-t * 6) * 0.08
        return {"hull": [(SIDE, -kick), (FWD, 0.02 * wave(t))], "sails": [(SIDE, 0.03 * wave(t, 3))]}

    return clips((bones, {"fly": (3.0, fly), "idle": (3.0, fly), "attack": (1.0, attack)}))


def cannon(mb, base, scale=1.0):
    """A ship's cannon on its wheeled carriage, the barrel pointing +Y from ``base`` (its trunnions)."""
    b, s = Vector(base), scale
    prof = [(0.0, -0.15), (0.1, -0.18), (0.13, -0.1), (0.15, 0.0), (0.13, 0.35), (0.1, 1.0), (0.115, 1.06), (0.1, 1.1), (0.06, 1.1), (0.06, 0.9)]
    mb.lathe([(r * s, z * s) for r, z in prof], "prop:dark_iron", "#2a2c2e", segments=14, matrix=Matrix.Translation(b) @ TO_Y)
    mb.sphere(b + Vector((0, -0.2 * s, 0)), 0.06 * s, "prop:dark_iron", "#2a2c2e", subdivisions=1)
    mb.box(b + Vector((-0.2, -0.35, -0.3)) * s, b + Vector((0.2, 0.2, -0.08)) * s, "prop:dark_planks", "#3a2a1c")
    for sx in (-1, 1):
        for y in (-0.25, 0.12):
            mb.cylinder(b + Vector((sx * 0.2, y, -0.28)) * s, b + Vector((sx * 0.26, y, -0.28)) * s, 0.1 * s, 0.1 * s, "prop:dark_planks", "#2a2018",
                        segments=10)


@generator("event_dutchman_cannon")
def dutchman_cannon(mb: MeshBuilder, p, rng):
    """One cannon alone (a future part), trunnions at the origin, barrel toward +Y."""
    cannon(mb, Vector((0, 0, 0)), p.get("scale", 1.0))
    return None


# ---------------------------------------------------------------------------------------------------- Celestial Pillars
PILLAR_STYLES = {  # stone, dark accent, glow
    "solar": ("#c8702a", "#4a1a08", "#ffb040"), "vortex": ("#2a8a7a", "#0a2a26", "#3affc0"),
    "nebula": ("#8a3aa8", "#2a0a3a", "#ff6ae0"), "stardust": ("#3a5ab8", "#101a40", "#8ac8ff")}
TOP = 9.0


def _column_r(z):
    return 1.6 + 0.18 * math.sin(z * 2.4) - 0.3 * max(0.0, (z - 7.5) / 1.5)


def _spiral(mb, z0, z1, r0, r1, turns, radius, material, color, phase=0.0, steps=40):
    pts = []
    for i in range(steps + 1):
        t = i / steps
        a = phase + t * turns * 2 * math.pi
        r = r0 + (r1 - r0) * t
        pts.append(Vector((math.cos(a) * r, math.sin(a) * r, z0 + (z1 - z0) * t)))
    mb.tube(pts, [radius * (1 - 0.6 * t / 1) for t in [i / steps for i in range(steps + 1)]], material, color, segments=6)


def _crown(mb, style, stone, dark, glow, rng):
    top = Vector((0, 0, TOP))
    if style == "solar":  # a crown of flame tongues around a blazing sun
        for k in range(12):
            a = k * 2 * math.pi / 12
            d = Vector((math.cos(a), math.sin(a), 0))
            h = 1.6 + 0.8 * (k % 2)
            pts = [top + d * 0.9, top + d * 1.3 + UP * h * 0.4, top + d * 1.15 + UP * h * 0.8, top + d * 0.7 + UP * h + d.cross(UP) * 0.3]
            mb.tube(pts, [0.32, 0.26, 0.15, 0.0], "prop:stone", stone, segments=7)
            mb.tube([q + d * 0.08 for q in pts[1:]], [0.12, 0.08, 0.0], "prop:plain:glow", glow, segments=5)
        mb.sphere(top + UP * 1.4, 0.95, "prop:plain:glow", glow, subdivisions=3)
        for k in range(3):
            _spiral(mb, 1.2, TOP - 0.2, 1.8, 1.55, 1.2, 0.09, "prop:plain:glow", mix(glow, "#ff4000", 0.4), phase=k * 2.1)
    elif style == "vortex":  # a twisting funnel of storm arms around a glowing eye
        for k in range(5):
            _spiral(mb, TOP - 0.2, TOP + 2.4, 0.6, 2.0, 0.6, 0.22, "prop:stone", stone, phase=k * 2 * math.pi / 5, steps=20)
            _spiral(mb, TOP, TOP + 2.2, 0.6, 1.8, 0.6, 0.08, "prop:plain:glow", glow, phase=k * 2 * math.pi / 5 + 0.25, steps=20)
        mb.sphere(top + UP * 1.3, 0.6, "prop:plain:glow", glow, subdivisions=2, scale=(1, 1, 1.3))
        for k in range(3):  # spiral ridges up the shaft
            _spiral(mb, 1.0, TOP, 1.75, 1.42, 2.5, 0.14, "prop:stone", dark, phase=k * 2 * math.pi / 3)
            _spiral(mb, 1.0, TOP, 1.73, 1.4, 2.5, 0.05, "prop:plain:glow", glow, phase=k * 2 * math.pi / 3 + 0.35)
    elif style == "nebula":  # curling horns cradling a floating brain, tendrils trailing down the shaft
        for sx in (-1, 1):
            mb.tube([top + Vector((sx * 0.7, 0, 0)), top + Vector((sx * 1.7, 0, 1.0)), top + Vector((sx * 1.6, 0, 2.2)), top + Vector((sx * 0.9, 0, 2.7))],
                    [0.35, 0.28, 0.16, 0.02], "prop:stone", stone, segments=8)
        c = top + UP * 1.75

        def folds(q):
            return math.exp(-(noise3(q, 5.0, 1.3) / 0.16) ** 2)
        organic(mb, lambda b: (b.ellipsoid(c + Vector((0.35, 0, 0)), (0.6, 0.85, 0.7)), b.ellipsoid(c - Vector((0.35, 0, 0)), (0.6, 0.85, 0.7))),
                "prop:flesh:glow", tris=2200, resolution=0.04, paint=lambda q, n: mix(glow, dark, folds(q) * 0.85),
                sculpt=lambda q, n: -n * 0.06 * folds(q))
        for k in range(7):
            a = k * 2 * math.pi / 7
            pts = [Vector((math.cos(a) * 1.55, math.sin(a) * 1.55, TOP - 0.3))]
            for i in range(5):
                a += 0.5
                pts.append(Vector((math.cos(a) * (1.8 + 0.2 * (i % 2)), math.sin(a) * (1.8 + 0.2 * (i % 2)), pts[-1].z - rng.uniform(0.9, 1.4))))
            mb.tube(pts, [0.16, 0.14, 0.11, 0.08, 0.05, 0.01], "prop:flesh", mix(stone, glow, 0.3), segments=6)
    else:  # stardust: a cluster of crystal shards under a four-pointed star, constellations around the shaft
        for k in range(8):  # shards breaking out of the plinth too
            a = k * 2 * math.pi / 8 + 0.3
            d = Vector((math.cos(a), math.sin(a), 0))
            base = d * 1.9 + UP * 0.9
            mb.cylinder(base, base + d * 0.5 + UP * rng.uniform(0.8, 1.5), rng.uniform(0.18, 0.28), 0.0, "prop:crystal:glow", glow, segments=5, smooth=False)
        for k in range(14):
            a = k * 2 * math.pi / 14 + rng.uniform(-0.2, 0.2)
            d = Vector((math.cos(a), math.sin(a), 0))
            base = top + d * rng.uniform(0.2, 0.8)
            mb.cylinder(base, base + (d * 0.6 + UP * rng.uniform(1.2, 2.2)), rng.uniform(0.2, 0.35), 0.0, "prop:crystal:glow",
                        mix(glow, "#ffffff", rng.uniform(0, 0.4)), segments=5, smooth=False)
        c = top + UP * 2.5
        star = [(math.cos(k * math.pi / 4) * (0.95 if k % 2 == 0 else 0.25), math.sin(k * math.pi / 4) * (0.95 if k % 2 == 0 else 0.25)) for k in range(8)]
        mb.extrude_polygon(star, 0.25, "prop:plain:glow", "#e8f4ff", matrix=Matrix.Translation(c))
        stars = []
        for k in range(26):
            a, z = rng.uniform(0, 2 * math.pi), rng.uniform(1.5, TOP - 0.5)
            stars.append(Vector((math.cos(a) * 2.1, math.sin(a) * 2.1, z)))
            mb.sphere(stars[-1], 0.09, "prop:plain:glow", "#ffffff", subdivisions=1)
        stars.sort(key=lambda q: q.z)
        for a, b in zip(stars, stars[1:]):
            if (a - b).length < 2.6:
                mb.cylinder(a, b, 0.02, 0.02, "prop:plain:glow", glow, segments=4, caps=False)


@generator("event_celestial_pillar")
def celestial_pillar(mb: MeshBuilder, p, rng):
    """A ~11.5 m monolith 4.6 m across the base (sized like the previous pillars); ``style`` picks the element."""
    style = p.get("style", "solar")
    stone, dark, glow = PILLAR_STYLES.get(style, PILLAR_STYLES["solar"])
    stone, dark, glow = p.get("color", stone), p.get("accent", dark), p.get("glow", glow)
    mb.bone = "core"
    mb.lathe([(2.6, 0.0), (2.65, 0.25), (2.4, 0.4), (2.25, 0.45), (2.25, 0.85), (2.0, 0.95), (0.0, 0.95)], "prop:stone_brick", dark, segments=12, smooth=False)
    prof = [(_column_r(0.9 + 8.1 * i / 40), 0.9 + 8.1 * i / 40) for i in range(41)] + [(0.0, TOP)]

    def carve(q, n):
        r = math.hypot(q.x, q.y)
        neck = max(0.0, math.cos(q.z * 2.4 + math.pi / 2))
        flute = 0.5 + 0.5 * math.cos(math.atan2(q.y, q.x) * 8)
        return mix(mix(stone, dark, 0.55 * neck ** 3 + 0.25 * flute ** 6 + 0.1 * noise3(q, 4)), "#ffffff", 0.08 * max(0.0, r - 1.6))
    _repaint_verts = mb.lathe(prof, "prop:stone", stone, segments=48 if style == "stardust" else 32)
    mb.bm.normal_update()
    for f in {f for v in _repaint_verts for f in v.link_faces}:
        for loop in f.loops:
            loop[mb.color_layer] = carve(loop.vert.co, None)
    for z in (2.0, 4.6, 7.2):  # glowing carved bands
        mb.lathe([(_column_r(z) + 0.02, z - 0.08), (_column_r(z) + 0.06, z), (_column_r(z) + 0.02, z + 0.08)], "prop:plain:glow", glow, segments=32)
    for sy in (1, -1):  # the emblem front and back
        c = Vector((0, sy * (_column_r(5.6) + 0.05), 5.6))
        mb.sphere(c, 0.55, "prop:stone", dark, subdivisions=2, scale=(1, 0.3, 1.3))
        mb.sphere(c + Vector((0, sy * 0.12, 0)), 0.32, "prop:plain:glow", glow, subdivisions=2, scale=(1, 0.3, 1.3))
    _crown(mb, style, stone, dark, glow, rng)
    if p.get("shield"):
        shell = [(math.sin(a) * 3.3, 6.0 - math.cos(a) * 6.6) for a in [i * math.pi / 16 for i in range(17)]]
        mb.lathe(shell, "prop:gel:clear", (*mix(glow, "#ffffff", 0.3)[:3], 0.22), segments=40)
    mb.bone = "ring"  # the floating rune ring
    ring = [Vector((math.cos(a) * 2.75, math.sin(a) * 2.75, 6.4)) for a in [i * 2 * math.pi / 48 for i in range(49)]]
    mb.tube(ring, [0.1] * 49, "prop:stone", dark, segments=6)
    for k in range(8):
        a = k * 2 * math.pi / 8
        mb.box((-0.18, -0.08, -0.3), (0.18, 0.08, 0.3), "prop:stone", stone, matrix=Matrix.Translation((math.cos(a) * 2.75, math.sin(a) * 2.75, 6.4)) @ Matrix.Rotation(a + math.pi / 2, 4, "Z"))
        mb.box((-0.1, -0.1, -0.18), (0.1, 0.1, 0.18), "prop:plain:glow", glow, matrix=Matrix.Translation((math.cos(a) * 2.83, math.sin(a) * 2.83, 6.4)) @ Matrix.Rotation(a + math.pi / 2, 4, "Z"))
    mb.bone = None
    bones = {"core": ((0, 0, 0), (0, 0, 4.0), None), "ring": ((0, 0, 6.4), (0, 0, 7.4), "core")}

    def idle(t):
        return {"ring": [(UP, t * 2 * math.pi / 8), (SIDE, 0.05 * wave(t))]}

    return clips((bones, {"idle": (4.0, idle)}))

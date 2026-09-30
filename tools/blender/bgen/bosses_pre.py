"""Pre-hardmode bosses, modelled after the reference game's look: King Slime and Queen Slime (translucent gel with a
crown or tiara, something trapped inside), the Eye of Cthulhu (a veined eyeball with a glossy cornea and trailing
optic nerves, and its torn-open toothed maw), the Brain of Cthulhu (folded lobes, dangling spinal cord and nerves;
its exposed phase stares with a bulging eye) and its Creepers.
Origins sit at the bottom of the creature (the game draws creatures at their feet, raising centred models).
Front faces Blender +Y.
"""
import math

from mathutils import Matrix, Vector

from .detail import fang, fibonacci, mix, noise3, organic, teeth_ring, veins
from .mesh import MeshBuilder


# ---------------------------------------------------------------------------------------------------- slimes
def _slime_body(mb, w, h, body, rng, alpha=0.78):
    """A gel dome: wide soft base, rounded top, gently lumpy; lighter on top, deeper colour below."""
    def build(b):
        b.ellipsoid((0, 0, h * 0.5), (w * 0.5, w * 0.47, h * 0.5))
        b.ellipsoid((0, 0, h * 0.2), (w * 0.53, w * 0.5, h * 0.22))

    def sculpt(p, n):
        return n * 0.03 * noise3(p, 2.2, 3.0)

    def paint(p, n):
        top = max(0.0, min(1.0, p.z / h))
        c = mix(mix(body, "#0a1030", 0.35), mix(body, "#ffffff", 0.3), top ** 1.2)
        return (c[0], c[1], c[2], alpha)

    organic(mb, build, "prop:gel:clear", tris=5000, resolution=w / 40, paint=paint, sculpt=sculpt)
    # Bubbles and specks suspended in the gel.
    for _ in range(9):
        d = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(0.1, 1))).normalized()
        c = Vector((d.x * w * 0.32, d.y * w * 0.3, h * (0.25 + 0.45 * d.z)))
        mb.sphere(c, rng.uniform(0.05, 0.14) * w / 3.5, "prop:gel:clear", (*mix(body, "#ffffff", 0.6)[:3], 0.45), subdivisions=2)


def _slime_face(mb, w, h, eye_z, spread=0.15, size=0.075):
    """Two glossy black oval eyes on the front surface, each with a bright catch-light."""
    for sx in (-1, 1):
        x = sx * w * spread
        y = math.sqrt(max(0.0, (w * 0.47) ** 2 * (1 - (x / (w * 0.5)) ** 2 - ((eye_z - h * 0.5) / (h * 0.5)) ** 2))) - 0.02
        mb.sphere((x, y, eye_z), w * size, "prop:plain", "#080810", subdivisions=2, scale=(0.75, 0.35, 1.25))
        mb.sphere((x + sx * 0.02 + 0.04, y + w * 0.02, eye_z + w * size * 0.5), w * size * 0.28, "prop:plain:glow", "#e8f0ff", subdivisions=1)


def _crown(mb, center, r, gold="#ffffff", gem="#ff2040", points=5, tiara=False):
    """A royal crown (or a tiara) with a ridged band, pointed tines tipped with balls and set jewels."""
    c = Vector(center)
    band_h = r * (0.35 if not tiara else 0.22)
    mb.lathe([(r * 0.98, 0), (r * 1.04, band_h * 0.12), (r, band_h * 0.25), (r * 1.03, band_h * 0.8), (r * 0.99, band_h),
              (r * 0.9, band_h), (r * 0.9, 0)], "prop:gold", gold, segments=36, smooth=True, matrix=Matrix.Translation(c))
    for k in range(points):
        a = 2 * math.pi * k / points + math.pi / 2
        radial = Vector((math.cos(a), math.sin(a), 0))
        front = max(0.0, radial.y)
        tall = r * ((0.55 if not tiara else 0.25) + (0.35 if tiara else 0.12) * front)
        base = c + radial * r * 0.96 + Vector((0, 0, band_h * 0.9))
        mb.cylinder(base, base + Vector((0, 0, tall)) + radial * r * 0.04, r * 0.16, 0.0, "prop:gold", gold, segments=6, smooth=False)
        mb.sphere(base + Vector((0, 0, tall + r * 0.04)), r * 0.06, "prop:gold", gold, subdivisions=1)
        jewel = c + radial * r * 1.03 + Vector((0, 0, band_h * 0.5))
        mb.sphere(jewel, r * 0.06, "prop:crystal:glow", "#40a0ff" if k % 2 else gem, subdivisions=1, scale=(1, 0.6, 1))
    mb.sphere(c + Vector((0, r * 1.02, band_h * (0.5 if not tiara else 1.4))), r * (0.15 if not tiara else 0.2),
              "prop:crystal:glow", gem, subdivisions=2, scale=(1, 0.55, 1.2))


def _ninja(mb, base, s):
    """The ninja King Slime swallowed: a small dark figure curled up inside."""
    b = Vector(base)
    navy, skin = "#1a1c2e", "#e0b090"
    mb.sphere(b + Vector((0, 0, s * 0.62)), s * 0.12, "prop:cloth", navy, subdivisions=2)
    mb.box(b + Vector((-s * 0.1, s * 0.08, s * 0.6)), b + Vector((s * 0.1, s * 0.13, s * 0.66)), "prop:plain", skin)  # eye slit
    mb.cylinder(b + Vector((0, 0, s * 0.68)), b + Vector((0, 0, s * 0.72)), s * 0.125, None, "prop:red_cloth", "#c02020", segments=10)
    mb.tube([b + Vector((0, -s * 0.12, s * 0.7)), b + Vector((-s * 0.1, -s * 0.3, s * 0.64)), b + Vector((-s * 0.15, -s * 0.42, s * 0.55))],
            [s * 0.03, s * 0.025, s * 0.01], "prop:red_cloth", "#c02020", segments=4)
    mb.cylinder(b + Vector((0, 0, s * 0.25)), b + Vector((0, 0, s * 0.52)), s * 0.12, s * 0.1, "prop:cloth", navy, segments=8)
    for sx in (-1, 1):
        mb.tube([b + Vector((sx * s * 0.1, 0, s * 0.48)), b + Vector((sx * s * 0.22, s * 0.08, s * 0.35)), b + Vector((sx * s * 0.12, s * 0.16, s * 0.3))],
                [s * 0.04, s * 0.035, s * 0.03], "prop:cloth", navy, segments=5)
        mb.tube([b + Vector((sx * s * 0.06, 0, s * 0.26)), b + Vector((sx * s * 0.12, s * 0.2, s * 0.18)), b + Vector((sx * s * 0.08, s * 0.05, 0))],
                [s * 0.05, s * 0.045, s * 0.035], "prop:cloth", navy, segments=5)


def king_slime(mb: MeshBuilder, p, rng):
    w, h = p.get("width", 3.5), p.get("height", 3.0)
    body = p.get("body", "#2f7cf6")
    _ninja(mb, (0.1, -0.1, h * 0.12), h * 0.55)
    _slime_body(mb, w, h, body, rng)
    _slime_face(mb, w, h, h * 0.52)
    _crown(mb, (0, 0, h * 0.9), w * 0.2, gem="#ff2848")


def queen_slime(mb: MeshBuilder, p, rng):
    w, h = p.get("width", 3.5), p.get("height", 3.0)
    body = p.get("body", "#f08ad8")
    # Crystal shards grown through the gel, poking out of the top.
    for k in range(9):
        a = rng.uniform(0, 2 * math.pi)
        base = Vector((math.cos(a) * w * rng.uniform(0.05, 0.28), math.sin(a) * w * rng.uniform(0.05, 0.25), h * rng.uniform(0.45, 0.75)))
        tip = base + Vector((math.cos(a) * 0.35, math.sin(a) * 0.3, rng.uniform(0.5, 1.0))) * w * 0.22
        mb.cylinder(base, tip, w * rng.uniform(0.035, 0.06), 0.0, "prop:crystal:glow", rng.choice(("#ff78e0", "#c090ff", "#70d8ff")), segments=5, smooth=False)
    _slime_body(mb, w, h, body, rng, alpha=0.7)
    _slime_face(mb, w, h, h * 0.5, spread=0.13, size=0.07)
    _crown(mb, (0, 0, h * 0.93), w * 0.16, gold="#e8eef8", gem="#ff60d0", points=7, tiara=True)
    if p.get("wings"):
        _crystal_wings(mb, w, h)


def _crystal_wings(mb, w, h):
    """Two pairs of translucent crystal wings, veined like an insect's, spread from the upper back."""
    for sx in (-1, 1):
        for upper, (span, tilt) in enumerate(((w * 0.95, 0.35), (w * 0.65, -0.25))):
            root = Vector((sx * w * 0.12, -w * 0.3, h * (0.6 + 0.12 * upper)))
            pts = [(0, 0), (span * 0.35, span * 0.28), (span * 0.8, span * 0.36), (span, span * 0.18), (span * 0.85, -span * 0.08), (span * 0.35, -span * 0.12)]
            m = Matrix.Translation(root) @ Matrix.Rotation(tilt * sx, 4, "Y") @ Matrix.Rotation(-0.35 * sx, 4, "Z") @ Matrix.Diagonal((sx, 1, 1, 1)) @ Matrix.Rotation(math.pi / 2, 4, "X")
            mb.extrude_polygon(pts, 0.02, "prop:crystal:clear", (0.95, 0.65, 1.0, 0.55), matrix=m)
            for vx, vz in ((span * 0.8, span * 0.33), (span * 0.95, span * 0.12), (span * 0.6, -span * 0.08)):
                mb.tube([m @ Vector((0, 0, 0)), m @ Vector((vx * 0.5, 0.012, vz * 0.6)), m @ Vector((vx, 0.012, vz))], [0.03, 0.02, 0.008], "prop:crystal:glow", "#ff9ae8", segments=4)


# ---------------------------------------------------------------------------------------------------- eyes
def _eyeball(mb, r, iris, rng, vein_trees=10, sclera="#f6f0ea", pupil=0.36, cornea=True, blood=0.0):
    """A detailed eyeball centred at the origin looking along +Y: sclera reddening toward the back, fibrous iris,
    dark pupil, clear glossy cornea and branching veins."""
    def paint(p, n):
        back = max(0.0, -n.y)
        return mix(sclera, "#d06060", back ** 1.5 * 0.55 + blood + 0.05 * noise3(p, 6))

    organic(mb, lambda b: b.ball((0, 0, 0), r), "prop:plain", tris=4000, resolution=r / 22, paint=paint, smooth=1)
    ir = r * 0.5
    y0 = math.sqrt(r * r - ir * ir) - r * 0.005
    iris_c = Vector((0, y0, 0))

    def iris_color(q, n):
        d = q - iris_c
        rad = Vector((d.x, 0, d.z)).length / ir
        ang = math.atan2(d.z, d.x)
        fib = 0.5 + 0.5 * math.sin(ang * 38 + 3 * noise3(q, 8))
        c = mix(iris, "#ffffff", 0.25 * fib * (1 - rad))
        c = mix(c, "#060606", max(0.0, (rad - 0.82) / 0.18) * 0.8)       # dark limbal ring
        return mix(c, "#020202", 1.0 if rad < pupil else 0.0)

    prof = [(0.0, 0.0), (ir * pupil * 0.9, 0.0), (ir * 0.55, -ir * 0.01), (ir, -ir * 0.03), (ir * 1.02, -ir * 0.12)]
    verts = mb.lathe(prof, "prop:plain", "#ffffff", segments=48, matrix=Matrix.Translation(iris_c) @ Matrix.Rotation(-math.pi / 2, 4, "X"))
    _repaint(mb, verts, iris_color)
    if cornea:
        mb.sphere(iris_c + Vector((0, -ir * 0.62, 0)), ir * 1.08, "prop:gel:clear", (1, 1, 1, 0.25), subdivisions=3, scale=(1, 0.6, 1))
    for d in fibonacci(vein_trees * 2, hemisphere=(0, -1, 0))[:vein_trees]:
        veins(mb, (0, 0, 0), r, d + Vector((0, 0.3, 0)), rng, "prop:flesh", "#b81c1c", length=r * 1.2, width=r * 0.022, depth=3)
    return iris_c


def _repaint(mb, verts, fn):
    faces = {f for v in verts for f in v.link_faces}
    for f in faces:
        for loop in f.loops:
            loop[mb.color_layer] = fn(loop.vert.co.copy(), loop.vert.normal.copy())


def _optic_nerves(mb, r, rng, color="#b83838", count=6, length=2.4):
    """A bundle of thick, tapering optic nerves trailing from the back of the eye, twisting as they go."""
    for k in range(count):
        a = 2 * math.pi * k / count + rng.uniform(-0.2, 0.2)
        off = Vector((math.cos(a), 0, math.sin(a) * 0.8)) * r * rng.uniform(0.22, 0.4)
        pts, radii = [], []
        for i in range(7):
            t = i / 6
            twist = a + t * 2.2
            pts.append(Vector((math.cos(twist) * r * 0.25 * (1 - t) + off.x * (1 - t * 0.4),
                               -r * 0.75 - length * t * rng.uniform(0.8, 1.1),
                               math.sin(twist) * r * 0.2 * (1 - t) + off.z * (1 - t * 0.4) - t * t * r * 0.25)))
            radii.append(r * (0.16 - 0.14 * t) * rng.uniform(0.85, 1.1))
        mb.tube(pts, radii, "prop:flesh", mix(color, "#601010", rng.uniform(0, 0.4)), segments=7)


def eye_of_cthulhu(mb: MeshBuilder, p, rng):
    r = p.get("radius", 1.25)
    _eyeball(mb, r, p.get("iris", "#4c7cb8"), rng, vein_trees=12)
    _optic_nerves(mb, r, rng)


def eye_of_cthulhu_maw(mb: MeshBuilder, p, rng):
    """The second form: the iris has torn open into a ragged, fanged mouth."""
    r = p.get("radius", 1.25)
    _eyeball(mb, r, "#300808", rng, vein_trees=14, cornea=False, blood=0.12)
    front = Vector((0, r * 0.62, 0))
    rot = Matrix.Translation(front) @ Matrix.Rotation(-math.pi / 2, 4, "X")
    mb.lathe([(r * 0.66, r * 0.12), (r * 0.7, 0.0), (r * 0.55, -r * 0.3), (r * 0.28, -r * 0.5), (0.0, -r * 0.55)], "prop:flesh", "#3a0606", segments=32, matrix=rot)
    # Torn gum flaps around the rim, then two rings of fangs.
    for k in range(14):
        a = 2 * math.pi * k / 14
        radial = Vector((math.cos(a), 0, math.sin(a)))
        base = front + radial * r * 0.68 + Vector((0, r * 0.1, 0))
        mb.tube([base - radial * r * 0.05, base + radial * r * 0.07 + Vector((0, r * 0.08, 0))], [r * 0.09, r * 0.02], "prop:flesh", "#a01818", segments=5)
    teeth_ring(mb, front + Vector((0, r * 0.08, 0)), r * 0.6, (0, 1, 0), 16, r * 0.32, r * 0.06, inward=0.75, rng=rng)
    teeth_ring(mb, front - Vector((0, r * 0.15, 0)), r * 0.42, (0, 1, 0), 12, r * 0.22, r * 0.045, inward=0.8, rng=rng)
    _optic_nerves(mb, r, rng, count=7)


def creeper(mb: MeshBuilder, p, rng):
    r = p.get("radius", 0.42)
    _eyeball(mb, r, p.get("iris", "#8a1414"), rng, vein_trees=8, sclera="#f0d8d0", blood=0.08)
    _optic_nerves(mb, r, rng, count=4, length=r * 1.8)


# ---------------------------------------------------------------------------------------------------- brain
def brain_of_cthulhu(mb: MeshBuilder, p, rng):
    """Two folded hemispheres over a brain stem and dangling spinal cord trailing nerves. Exposed: darker, raw, with a
    bulging eye pushing out of the front."""
    exposed = p.get("exposed", False)
    c = Vector((0, 0, p.get("centerZ", 1.55)))
    rx, ry, rz = 1.05, 1.2, 0.85
    base = "#b0303e" if exposed else "#e48a98"
    groove = "#4a0a14" if exposed else "#9a3a4c"

    def build(b):
        for sx in (-1, 1):
            b.ellipsoid(c + Vector((sx * rx * 0.5, 0, 0)), (rx * 0.56, ry, rz))
            b.ellipsoid(c + Vector((sx * rx * 0.45, -ry * 0.35, -rz * 0.35)), (rx * 0.45, ry * 0.55, rz * 0.5))  # occipital lobes
        b.capsule(c + Vector((0, -ry * 0.25, -rz * 0.5)), c + Vector((0, -ry * 0.3, -rz * 1.25)), 0.26)       # brain stem

    def fold(q):
        return math.exp(-(noise3(q, 3.2, 1.7) / 0.14) ** 2)

    def sculpt(q, n):
        mid = math.exp(-((q.x - c.x) / 0.07) ** 2) * (1 if q.z > c.z - rz * 0.5 else 0)
        return -n * (0.07 * fold(q) + 0.12 * mid)

    def paint(q, n):
        return mix(base, groove, fold(q) * 0.9 + 0.08 * noise3(q, 9))

    organic(mb, build, "prop:flesh", tris=12000, resolution=0.028, paint=paint, sculpt=sculpt, smooth=1)
    # Spinal cord and nerves hanging below.
    stem = c + Vector((0, -ry * 0.3, -rz * 1.2))
    cord = [stem, stem + Vector((0.05, -0.1, -0.35)), stem + Vector((-0.05, 0.0, -0.75)), Vector((0.04, -ry * 0.2, 0.02))]
    mb.tube(cord, [0.18, 0.15, 0.12, 0.06], "prop:flesh", "#c05a66" if not exposed else "#801828", segments=8)
    for k in range(10):
        a = 2 * math.pi * k / 10 + rng.uniform(-0.2, 0.2)
        start = c + Vector((math.cos(a) * rx * 0.55, math.sin(a) * ry * 0.6 - 0.1, -rz * 0.7))
        end = Vector((start.x * 1.3, start.y * 1.2, rng.uniform(0.0, 0.35)))
        mid = start.lerp(end, 0.5) + Vector((rng.uniform(-0.2, 0.2), rng.uniform(-0.2, 0.2), rng.uniform(-0.1, 0.1)))
        mb.tube([start, mid, end], [0.07, 0.045, 0.012], "prop:flesh", "#b8404c" if not exposed else "#701020", segments=5)
    if exposed:
        eye_c = c + Vector((0, ry * 0.72, -rz * 0.05))
        m = Matrix.Translation(eye_c)
        verts_before = set(mb.bm.verts)
        _eyeball(mb, 0.55, "#c01818", rng, vein_trees=6, blood=0.1)
        moved = [v for v in mb.bm.verts if v not in verts_before]
        import bmesh
        bmesh.ops.transform(mb.bm, matrix=m, verts=moved)


GENERATORS = {
    "king_slime_v2": king_slime,
    "queen_slime_v2": queen_slime,
    "eye_of_cthulhu_v2": eye_of_cthulhu,
    "eye_of_cthulhu_maw_v2": eye_of_cthulhu_maw,
    "creeper_v2": creeper,
    "brain_of_cthulhu_v2": brain_of_cthulhu,
}

"""Common enemies at boss quality (P3-45), on the boss helpers but with modest triangle budgets (they come in crowds):
slimes of translucent jelly with something visible inside, veined demon eyes with coloured irises and optic nerves,
furred bats with membrane wings on finger struts and cursed skulls (the Wyvern is bgen/sky_dragon.py). Front faces
Blender +Y."""
import math

from mathutils import Vector

from . import generator
from .bosses_pre import _eyeball, _optic_nerves, _slime_face
from .creatures import BAT_BONES, _action, _bat_actions
from .detail import mix, noise3, organic, teeth_row
from .mesh import MeshBuilder


@generator
def jelly_slime(mb: MeshBuilder, p, rng):
    """A slime: a lumpy dome of clear jelly (the enemy's tint colours it), darker below, with suspended bubbles, two
    glossy eyes and a swallowed object visible inside."""
    w, h = p.get("width", 1.0), p.get("height", 0.82)
    body = p.get("body", "#f2f4f8")

    def build(b):
        b.ellipsoid((0, 0, h * 0.5), (w * 0.5, w * 0.47, h * 0.5))
        b.ellipsoid((0, 0, h * 0.2), (w * 0.53, w * 0.5, h * 0.22))

    def paint(q, n):
        top = max(0.0, min(1.0, q.z / h))
        c = mix(mix(body, "#202838", 0.3), mix(body, "#ffffff", 0.35), top ** 1.2)
        return (c[0], c[1], c[2], p.get("alpha", 0.7))

    organic(mb, build, "prop:gel:clear", tris=1400, resolution=w / 30, paint=paint,
            sculpt=lambda q, n: n * 0.025 * noise3(q, 4.0, 3.0))
    mb.sphere((0.06, 0.02, h * 0.32), w * 0.11, "prop:plain", p.get("inside", "#e8c040"), subdivisions=1)  # the swallowed coin
    for _ in range(5):
        d = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(0.1, 1))).normalized()
        c = Vector((d.x * w * 0.3, d.y * w * 0.28, h * (0.25 + 0.45 * d.z)))
        mb.sphere(c, rng.uniform(0.03, 0.06) * w, "prop:gel:clear", (*mix(body, "#ffffff", 0.6)[:3], 0.4), subdivisions=1)
    _slime_face(mb, w, h, h * 0.55, spread=0.15, size=0.07)
    return None


@generator
def veined_eye(mb: MeshBuilder, p, rng):
    """A demon eye: a veined eyeball with a coloured iris (glowing unless ``glow`` is false) trailing optic nerves."""
    r = p.get("radius", 0.42)
    _eyeball(mb, r, p.get("iris", "#e0201a"), rng, vein_trees=5, tris=1100, vein_depth=2)
    _optic_nerves(mb, r, rng, count=3, length=1.6, segments=5)
    if p.get("glow", True):
        mb.sphere((0, r * 0.93, 0), r * 0.16, "prop:plain:glow", p.get("iris", "#e0201a"), subdivisions=1, scale=(1, 0.3, 1))
    return None


@generator
def fur_bat(mb: MeshBuilder, p, rng):
    """A bat: a furred body and head with big ears, a pug snout, glowing eyes, and membrane wings stretched between
    three finger struts on each arm."""
    fur, wing, eyes = p.get("fur", "#5a4a44"), p.get("wing", "#3a2a2a"), p.get("eyes", "#ff4020")
    mb.bone = "body"
    organic(mb, lambda b: (b.ellipsoid((0, 0, 0), (0.11, 0.15, 0.11)), b.ball((0, 0.15, 0.05), 0.085)), "prop:fur", fur,
            tris=700, resolution=0.012, paint=lambda q, n: mix(mix(fur, "#000000", 0.35), fur, 0.5 + 0.5 * n.z))
    for sx in (-1, 1):
        mb.cylinder((sx * 0.045, 0.14, 0.11), (sx * 0.08, 0.13, 0.24), 0.035, 0.0, "prop:fur", fur, segments=5)   # ears
        mb.sphere((sx * 0.035, 0.225, 0.075), 0.016, "prop:plain:glow", eyes, subdivisions=1)
        mb.cylinder((sx * 0.015, 0.23, 0.02), (sx * 0.015, 0.235, -0.01), 0.006, 0.0, "prop:plain", "#f0ece0", segments=4)  # fangs
    mb.sphere((0, 0.235, 0.04), 0.03, "prop:flesh", "#5a3a3a", subdivisions=1, scale=(1.2, 0.6, 0.8))  # snout
    for side, sx in (("R", 1), ("L", -1)):
        mb.bone = f"wing.{side}"
        wrist = Vector((sx * 0.22, 0.03, 0.06))
        mb.tube([Vector((sx * 0.06, 0.02, 0.02)), wrist], [0.012, 0.008], "prop:flesh", wing, segments=4)
        tips = [Vector((sx * 0.55, 0.08, 0.07)), Vector((sx * 0.5, -0.06, 0.03)), Vector((sx * 0.36, -0.12, 0.0))]
        for t in tips:
            mb.tube([wrist, t], [0.007, 0.003], "prop:flesh", wing, segments=3)
        rim = [Vector((sx * 0.06, -0.06, 0.0)), *reversed(tips), wrist]
        pts = [(q.x, q.y) for q in rim]
        _membrane(mb, pts, 0.03, wing)
    mb.bone = None
    return {"rig": BAT_BONES, "actions": _bat_actions}


def _membrane(mb, pts, z, color):
    """A thin wing membrane through the given (x, y) points at height z (two-sided by a hair of thickness)."""
    bm = mb.bm
    top = [bm.verts.new((x, y, z + 0.002)) for x, y in pts]
    bot = [bm.verts.new((x, y, z - 0.002)) for x, y in pts]
    bm.faces.new(top)
    bm.faces.new(list(reversed(bot)))
    mb._finish_part(top + bot, "prop:cloth", color, False)


@generator
def cursed_skull(mb: MeshBuilder, p, rng):
    """A floating skull centred at ``centerZ``: cranium with cheekbones and sockets, a toothy jaw, burning eyes."""
    r, z = p.get("radius", 0.42), p.get("centerZ", 0.42)
    bone, glow = p.get("bone", "#d8e0c8"), p.get("eyes", "#50ff80")
    c = Vector((0, 0, z))

    def build(b):
        b.ellipsoid(c + Vector((0, -r * 0.05, r * 0.18)), (r * 0.88, r, r * 0.9))
        b.ellipsoid(c + Vector((0, r * 0.42, -r * 0.35)), (r * 0.62, r * 0.45, r * 0.42))
        for sx in (-1, 1):
            b.ellipsoid(c + Vector((sx * r * 0.5, r * 0.5, -r * 0.2)), (r * 0.22, r * 0.25, r * 0.16))
            b.ball(c + Vector((sx * r * 0.33, r * 0.82, r * 0.02)), r * 0.27, negative=True)
        b.ellipsoid(c + Vector((0, r * 0.88, -r * 0.3)), (r * 0.09, r * 0.15, r * 0.14), negative=True)

    organic(mb, build, "prop:bone", bone, tris=1100, resolution=r / 18, smooth=1,
            paint=lambda q, n: mix(mix(bone, "#3a3020", 0.4), bone, 0.6 + 0.4 * n.z))
    for sx in (-1, 1):
        mb.sphere(tuple(c + Vector((sx * r * 0.33, r * 0.62, r * 0.02))), r * 0.2, "prop:plain", "#141008", subdivisions=1, scale=(1, 0.6, 1))
        mb.sphere(tuple(c + Vector((sx * r * 0.33, r * 0.72, r * 0.03))), r * 0.08, "prop:plain:glow", glow, subdivisions=1)
    teeth_row(mb, c + Vector((-r * 0.38, r * 0.7, -r * 0.62)), c + Vector((r * 0.38, r * 0.7, -r * 0.62)), 8, r * 0.14, (0, 0.15, -1), r * 0.045, rng)
    for _ in range(p.get("cracks", 1)):
        a = rng.uniform(0, 2 * math.pi)
        pts = [c + Vector((math.cos(a) * r * 0.45, math.sin(a) * r * 0.5 - r * 0.1, r * 0.95))]
        for _ in range(3):
            pts.append(pts[-1] + Vector((rng.uniform(-0.15, 0.15), rng.uniform(-0.15, 0.15), -0.18)) * r)
        mb.tube([c + (q - c).normalized() * r * 0.97 for q in pts], [r * 0.018, r * 0.014, r * 0.01, r * 0.005], "prop:plain", "#2a1e18", segments=3)
    return None

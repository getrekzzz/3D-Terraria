"""Boss models: Gel Sovereign, The Watcher (eye and maw phases), Bore Tyrant (head, segment, tail), Crypt Warden
(skull and hands; the skull also serves the guardian, the hexed skulls and, in steel, Warden Prime), The Mawwall (mouth, eyes, a flesh tile
the game repeats over the wall, hungry mouths).
Sizes are final (scale 1 in game). Front faces Blender +Y."""
import math

from mathutils import Matrix, Vector

from .mesh import MeshBuilder


def gel_sovereign(mb: MeshBuilder, p, rng):
    w, h = p.get("width", 3.5), p.get("height", 3.0)
    mb.sphere((0, 0, h * 0.45), w * 0.5, "prop:gel", p.get("body", "#5aa0ff"), subdivisions=3, scale=(1, 1, h / w * 0.95), jitter=0.015, rng=rng)
    for sx in (-1, 1):
        mb.sphere((sx * w * 0.14, w * 0.43, h * 0.55), w * 0.06, "prop:plain", "#101018", subdivisions=2)
        mb.sphere((sx * w * 0.14 + 0.03, w * 0.47, h * 0.58), w * 0.018, "prop:plain", "#ffffff", subdivisions=1)
    # A crown floating in the top of the body.
    top = h * 0.88
    mb.lathe([(w * 0.2, top), (w * 0.2, top + 0.22), (w * 0.17, top + 0.22), (w * 0.17, top)], "prop:gold", "#ffffff", segments=16, smooth=False)
    for k in range(5):
        a = k * 2 * math.pi / 5
        base = Vector((math.cos(a) * w * 0.185, math.sin(a) * w * 0.185, top + 0.2))
        mb.cylinder(base, base + Vector((0, 0, 0.28)), 0.07, 0.0, "prop:gold", segments=4, smooth=False)
    mb.sphere((0, w * 0.19, top + 0.12), 0.09, "prop:crystal:glow", "#ff3050", subdivisions=1)
    mb.sphere((0.2, 0.2, h * 0.3), 0.3, "prop:bark", "#8a6a4a", subdivisions=1, jitter=0.2, rng=rng)  # something swallowed


def _eye(mb, r, iris, veins, rng):
    mb.sphere((0, 0, 0), r, "prop:plain", "#f4ece4", subdivisions=3)
    for _ in range(veins):
        a, b = rng.random() * 2 * math.pi, rng.random() * math.pi * 0.6 + 0.2
        start = Vector((math.cos(a) * math.sin(b), -math.cos(b), math.sin(a) * math.sin(b))) * r * 1.0
        end = start.normalized() * r * 1.01 + Vector((0, r * 0.35, 0))
        mb.tube([start, start.lerp(end, 0.5) + Vector((0, 0, 0.05)), end], [0.025, 0.02, 0.008], "prop:plain", "#b02020", segments=4)
    return iris


def watcher(mb: MeshBuilder, p, rng):
    r = p.get("radius", 1.2)
    _eye(mb, r, p.get("iris", "#3a8a5a"), 10, rng)
    mb.sphere((0, r * 0.86, 0), r * 0.42, "prop:plain", p.get("iris", "#3a8a5a"), subdivisions=2, scale=(1, 0.42, 1))
    mb.sphere((0, r * 1.0, 0), r * 0.2, "prop:plain", "#050505", subdivisions=2, scale=(1, 0.35, 1))
    _tendrils(mb, r, rng, p.get("tendril", "#b04848"))


def watcher_maw(mb: MeshBuilder, p, rng):
    r = p.get("radius", 1.2)
    _eye(mb, r, "#000000", 12, rng)
    # The iris side splits into a toothed mouth.
    mb.lathe([(r * 0.62, 0.0), (r * 0.55, r * 0.25), (r * 0.2, r * 0.28), (0.0, r * 0.2)], "prop:flesh", "#401010",
             segments=16, matrix=Matrix.Translation((0, r * 0.72, 0)) @ Matrix.Rotation(-math.pi / 2, 4, "X"))
    for k in range(12):
        a = k * 2 * math.pi / 12
        base = Vector((math.cos(a) * r * 0.58, r * 0.8, math.sin(a) * r * 0.58))
        mb.cylinder(base, base + Vector((-math.cos(a) * 0.12, 0.12, -math.sin(a) * 0.12)), 0.06, 0.0, "prop:plain", "#f0e8d0", segments=4, smooth=False)
    _tendrils(mb, r, rng, p.get("tendril", "#b04848"))


def _tendrils(mb, r, rng, color):
    for k in range(7):
        a = k * 2 * math.pi / 7 + rng.random() * 0.3
        base = Vector((math.cos(a) * r * 0.5, -r * 0.8, math.sin(a) * r * 0.5))
        mid = base + Vector((math.cos(a) * 0.3, -0.8, math.sin(a) * 0.3))
        tip = mid + Vector((math.cos(a) * 0.2, -0.9, math.sin(a) * 0.2 - 0.2))
        mb.tube([base, mid, tip], [0.14, 0.09, 0.02], "prop:flesh", color, segments=6)


def worm_segment(mb: MeshBuilder, p, rng):
    """One armoured ring of a burrowing worm, modelled along +Y (the direction of travel)."""
    r, length = p.get("radius", 0.75), p.get("length", 1.1)
    shell, flesh = p.get("shell", "#b09ac8"), p.get("flesh", "#c07080")
    kind = p.get("part", "segment")
    if kind == "tail":
        mb.cylinder((0, -length * 0.5, 0), (0, length * 0.9, 0), r * 0.25, r * 0.85, "prop:stone", shell, segments=10)
        return
    mb.cylinder((0, -length * 0.5, 0), (0, length * 0.5, 0), r * 0.8, r * 0.8, "prop:flesh", flesh, segments=12)
    for y in (-length * 0.3, length * 0.15):
        mb.cylinder((0, y, 0), (0, y + length * 0.3, 0), r, r * 0.92, "prop:stone", shell, segments=12)
    for k in range(4):
        a = k * math.pi / 2 + math.pi / 4
        base = Vector((math.cos(a) * r * 0.95, 0, math.sin(a) * r * 0.95))
        mb.cylinder(base, base * 1.35 + Vector((0, -0.15, 0)), 0.08, 0.0, "prop:dark_iron", "#c0b0d0", segments=4, smooth=False)
    if kind == "head":
        mb.cylinder((0, length * 0.5, 0), (0, length * 0.9, 0), r * 0.9, r * 0.45, "prop:stone", shell, segments=12)
        for sx in (-1, 1):
            base = Vector((sx * r * 0.45, length * 0.85, 0))
            mb.tube([base, base + Vector((sx * 0.3, 0.45, 0.05)), base + Vector((-sx * 0.05, 0.8, 0.0))], [0.13, 0.09, 0.02], "prop:plain", "#f0e0c0", segments=5)
        for sx in (-1, 1):
            mb.sphere((sx * r * 0.45, length * 0.6, r * 0.55), 0.1, "prop:plain:glow", "#ffd040", subdivisions=1)


def crown(mb: MeshBuilder, p, rng):
    mb.lathe([(0.12, 0.0), (0.12, 0.08), (0.105, 0.08), (0.105, 0.0)], "prop:gold", "#ffffff", segments=16, smooth=False)
    for k in range(5):
        a = k * 2 * math.pi / 5
        base = Vector((math.cos(a) * 0.11, math.sin(a) * 0.11, 0.07))
        mb.cylinder(base, base + Vector((0, 0, 0.1)), 0.03, 0.0, "prop:gold", segments=4, smooth=False)
    mb.sphere((0, 0.12, 0.05), 0.035, "prop:gel:glow", p.get("gem", "#5ab8ff"), subdivisions=1)


def eye_lens(mb: MeshBuilder, p, rng):
    mb.sphere((0, 0, 0.12), 0.12, "prop:plain", "#f4ece4", subdivisions=2)
    mb.sphere((0, 0.1, 0.12), 0.06, "prop:plain:glow", p.get("iris", "#ff4040"), subdivisions=1, scale=(1, 0.4, 1))
    mb.sphere((0, 0.125, 0.12), 0.025, "prop:plain", "#050505", subdivisions=1, scale=(1, 0.4, 1))


def skull(mb: MeshBuilder, p, rng):
    """A floating skull centred at height centerZ (bosses are drawn at their centre, other fliers at their feet)."""
    r, z = p.get("radius", 1.3), p.get("centerZ", 0.0)
    bone, glow = p.get("bone", "#e8e0c8"), p.get("eyes", "#ff5030")
    mat = f"prop:{p.get('layer', 'plain')}"  # Warden Prime recasts it in steel
    mb.sphere((0, -r * 0.05, z + r * 0.12), r, mat, bone, subdivisions=3, scale=(0.88, 1.0, 0.92), jitter=0.012, rng=rng)
    # Cheekbones and upper jaw, then a hinged lower jaw hanging slightly open.
    mb.box((-r * 0.62, r * 0.2, z - r * 0.55), (r * 0.62, r * 0.8, z - r * 0.18), mat, bone)
    mb.box((-r * 0.5, r * 0.12, z - r * 0.98), (r * 0.5, r * 0.72, z - r * 0.7), mat, bone)
    for sx in (-1, 1):
        mb.box((sx * r * 0.44 - r * 0.07, r * 0.1, z - r * 0.8), (sx * r * 0.44 + r * 0.07, r * 0.3, z - r * 0.3), mat, bone)
    for k in range(-3, 4):  # teeth, upper and lower rows
        x = k * r * 0.13
        mb.box((x - r * 0.045, r * 0.74, z - r * 0.62), (x + r * 0.045, r * 0.82, z - r * 0.46), mat, "#f6f0e2")
        mb.box((x - r * 0.045, r * 0.64, z - r * 0.74), (x + r * 0.045, r * 0.72, z - r * 0.6), mat, "#f6f0e2")
    # Deep sockets with a burning point in each, and the nasal hole.
    for sx in (-1, 1):
        mb.sphere((sx * r * 0.34, r * 0.74, z + r * 0.08), r * 0.25, "prop:plain", "#120c0c", subdivisions=2, scale=(1, 0.55, 0.9))
        mb.sphere((sx * r * 0.34, r * 0.86, z + r * 0.08), r * 0.085, "prop:plain:glow", glow, subdivisions=1)
    mb.sphere((0, r * 0.84, z - r * 0.2), r * 0.1, "prop:plain", "#120c0c", subdivisions=1, scale=(0.8, 0.5, 1.2))
    for _ in range(p.get("cracks", 3)):  # a few dark cracks across the dome
        a = rng.random() * 2 * math.pi
        start = Vector((math.cos(a) * r * 0.5, -r * 0.1 + math.sin(a) * r * 0.3, z + r * 0.85))
        mb.tube([start, start + Vector((rng.random() * 0.3 - 0.15, rng.random() * 0.3, -r * 0.35))], [0.04, 0.02], "prop:plain", "#5a5040", segments=4)


def bone_hand(mb: MeshBuilder, p, rng):
    """A skeletal hand centred at the origin, palm facing +Y (towards what it grabs), fingers curling forward."""
    s, bone = p.get("size", 1.0), p.get("bone", "#e0d8c0")
    mb.box((-0.45 * s, -0.15 * s, -0.35 * s), (0.45 * s, 0.1 * s, 0.25 * s), "prop:plain", bone)  # palm
    mb.cylinder((0, -0.05 * s, -0.35 * s), (0, -0.12 * s, -1.0 * s), 0.13 * s, 0.1 * s, "prop:plain", bone, segments=8)  # wrist
    mb.sphere((0, -0.12 * s, -1.05 * s), 0.16 * s, "prop:plain", bone, subdivisions=1)
    for k in range(4):
        x = (-0.33 + k * 0.22) * s
        base = Vector((x, 0.0, 0.25 * s))
        mid = base + Vector((0, 0.2 * s, 0.35 * s))
        tip = mid + Vector((0, 0.35 * s, 0.1 * s))
        mb.tube([base, mid, tip], [0.075 * s, 0.065 * s, 0.03 * s], "prop:plain", bone, segments=6)
        for knuckle in (base, mid):
            mb.sphere(tuple(knuckle), 0.085 * s, "prop:plain", bone, subdivisions=1)
    thumb = Vector((0.5 * s, 0.0, -0.1 * s))
    mb.tube([thumb, thumb + Vector((0.2 * s, 0.25 * s, 0.15 * s)), thumb + Vector((0.1 * s, 0.5 * s, 0.25 * s))], [0.08 * s, 0.06 * s, 0.03 * s], "prop:plain", bone, segments=6)


FLESH, DARK_FLESH, TOOTH = "#b04a4a", "#5a1a20", "#f0e6cc"


def _teeth(mb, ring_radius, y, count, length, rng, inward=True):
    """A ring of teeth on the XZ plane at depth y, pointing into the ring's centre (a mouth facing +Y)."""
    for k in range(count):
        a = k * 2 * math.pi / count + rng.random() * 0.15
        base = Vector((math.cos(a) * ring_radius, y, math.sin(a) * ring_radius))
        tip = base - Vector((math.cos(a), 0, math.sin(a))) * length * (1 if inward else -1) + Vector((0, length * 0.3, 0))
        mb.cylinder(base, tip, length * 0.28, 0.0, "prop:plain", TOOTH, segments=5, smooth=False)


def maw_mouth(mb: MeshBuilder, p, rng):
    """The wall's mouth, centred at the origin and opening towards +Y out of the flesh (which is 3 m thick)."""
    r = p.get("radius", 2.1)
    mb.sphere((0, 0.2, 0), r * 1.05, "prop:flesh", FLESH, subdivisions=3, scale=(1, 0.55, 1), jitter=0.05, rng=rng)
    lips = Matrix.Translation((0, 1.45, 0)) @ Matrix.Rotation(-math.pi / 2, 4, "X")
    mb.lathe([(r * 0.95, -0.3), (r * 1.05, 0.15), (r * 0.8, 0.45), (r * 0.62, 0.3), (r * 0.6, 0.0)], "prop:flesh", "#c05a5a", segments=24, matrix=lips)
    mb.sphere((0, 1.62, 0), r * 0.64, "prop:plain", "#1a0406", subdivisions=2, scale=(1, 0.12, 1))  # the throat
    _teeth(mb, r * 0.66, 1.8, 14, 0.45, rng)
    _teeth(mb, r * 0.5, 1.7, 10, 0.3, rng)
    for k in range(6):  # veins spreading from the lips over the flesh
        a = k * 2 * math.pi / 6 + rng.random() * 0.4
        start = Vector((math.cos(a) * r * 1.0, 1.35, math.sin(a) * r * 1.0))
        mb.tube([start, start + Vector((math.cos(a) * 0.35, -0.35, math.sin(a) * 0.35)), start + Vector((math.cos(a) * 0.6, -0.8, math.sin(a) * 0.55))],
                [0.1, 0.08, 0.03], "prop:flesh", "#7a1a2a", segments=5)


def maw_eye(mb: MeshBuilder, p, rng):
    """A great eye set in a fleshy lid, centred at the origin, looking along +Y."""
    r = p.get("radius", 1.4)
    mb.sphere((0, 0, 0), r, "prop:plain", "#f2e8e0", subdivisions=3)
    mb.sphere((0, r * 0.84, 0), r * 0.45, "prop:plain", p.get("iris", "#e04a20"), subdivisions=2, scale=(1, 0.4, 1))
    mb.sphere((0, r * 0.98, 0), r * 0.2, "prop:plain:glow", "#200000", subdivisions=2, scale=(1, 0.3, 1.4))
    lid = Matrix.Translation((0, r * 0.45, 0)) @ Matrix.Rotation(-math.pi / 2, 4, "X")
    mb.lathe([(r * 1.2, -0.4), (r * 1.15, 0.1), (r * 0.98, 0.35), (r * 0.9, 0.2)], "prop:flesh", FLESH, segments=20, matrix=lid)
    for _ in range(8):
        a, b = rng.random() * 2 * math.pi, rng.random() * 0.5 + 0.3
        start = Vector((math.cos(a) * math.sin(b), math.cos(b), math.sin(a) * math.sin(b))) * r * 1.0
        end = (start + Vector((0, r * 0.3, 0))).normalized() * r * 1.01
        mb.tube([start, start.lerp(end, 0.5) * 1.01, end], [0.05, 0.04, 0.015], "prop:plain", "#b02020", segments=4)


def maw_flesh(mb: MeshBuilder, p, rng):
    """One tile of the wall: size x size (X across, Z up), thickness along Y with its lumpy face at +Y."""
    s, t = p.get("size", 4.0), p.get("thickness", 3.0)
    mb.box((-s / 2, -t / 2, -s / 2), (s / 2, t / 2 - 0.2, s / 2), "prop:flesh", DARK_FLESH)
    for _ in range(p.get("lumps", 9)):
        x, z = (rng.random() - 0.5) * s * 0.85, (rng.random() - 0.5) * s * 0.85
        rad = 0.45 + rng.random() * 0.5
        mb.sphere((x, t / 2 - 0.35, z), rad, "prop:flesh", FLESH, subdivisions=2, scale=(1, 0.55, 1), jitter=0.08, rng=rng)
    for _ in range(4):
        x0, z0 = (rng.random() - 0.5) * s, (rng.random() - 0.5) * s
        pts = [Vector((x0, t / 2 - 0.1, z0))]
        for _ in range(3):
            pts.append(pts[-1] + Vector(((rng.random() - 0.5) * 1.4, 0.02, (rng.random() - 0.5) * 1.4)))
        mb.tube([Vector((max(-s / 2, min(s / 2, v.x)), v.y, max(-s / 2, min(s / 2, v.z)))) for v in pts], [0.1, 0.08, 0.06, 0.03], "prop:flesh", "#6a1022", segments=5)


def hungry(mb: MeshBuilder, p, rng):
    """A tethered fleshy mouth, centred at the origin, biting towards +Y."""
    r = p.get("radius", 0.55)
    mb.sphere((0, -0.05, 0), r, "prop:flesh", FLESH, subdivisions=2, jitter=0.06, rng=rng)
    mb.sphere((0, r * 0.75, 0), r * 0.55, "prop:plain", "#1a0406", subdivisions=2, scale=(1, 0.35, 0.8))
    _teeth(mb, r * 0.58, r * 0.8, 9, 0.16, rng)
    mb.sphere((0, r * 0.35, r * 0.75), r * 0.2, "prop:plain", "#f2e8e0", subdivisions=1)
    mb.sphere((0, r * 0.5, r * 0.82), r * 0.09, "prop:plain", "#e04a20", subdivisions=1)


GENERATORS = {
    "maw_mouth": maw_mouth, "maw_eye": maw_eye, "maw_flesh": maw_flesh, "hungry": hungry,
    "gel_sovereign": gel_sovereign, "watcher": watcher, "watcher_maw": watcher_maw, "worm_segment": worm_segment,
    "crown": crown, "eye_lens": eye_lens, "skull": skull, "bone_hand": bone_hand,
}

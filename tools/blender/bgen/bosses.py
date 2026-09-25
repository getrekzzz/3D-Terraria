"""Boss models: Gel Sovereign, The Watcher (eye and maw phases), Bore Tyrant (head, segment, tail).
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


GENERATORS = {
    "gel_sovereign": gel_sovereign, "watcher": watcher, "watcher_maw": watcher_maw, "worm_segment": worm_segment,
    "crown": crown, "eye_lens": eye_lens,
}

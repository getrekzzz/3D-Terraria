"""Detail helpers for creature and boss models: smooth organic bodies from metaballs, branching veins that hug a
surface, rows and rings of teeth, spikes and plates, noise for skin. Everything adds parts to a MeshBuilder.
Blender Z up, creatures face +Y.
"""
import math

from mathutils import Matrix, Vector

from .human.blobs import Blob, displace
from .mesh import MeshBuilder, hex_color


def organic(mb: MeshBuilder, build, material, color="#ffffff", tris=4000, resolution=0.05, paint=None, sculpt=None,
            smooth=2, matrix=None):
    """A smooth body from metaball elements: ``build(blob)`` adds capsules/ellipsoids/balls (surface sizes in metres),
    then it is polygonised to about ``tris`` triangles, optionally sculpted (``sculpt(position, normal) -> offset``)
    and merged into the builder with ``paint(position, normal) -> colour`` per vertex."""
    blob = Blob(f"{mb.name}_blob", resolution)
    build(blob)
    obj = blob.to_mesh(f"{mb.name}_body", tris, smooth_iterations=smooth)
    if sculpt is not None:
        displace(obj, sculpt)
    return mb.merge(obj, material, color, paint=paint, matrix=matrix)


def noise3(p: Vector, scale=1.0, seed=0.0):
    """Cheap smooth value in -1..1 from layered sines (deterministic, no textures needed)."""
    x, y, z = p.x * scale + seed, p.y * scale - seed * 0.7, p.z * scale + seed * 1.3
    return (math.sin(x * 1.7 + math.sin(z * 1.3)) * math.cos(y * 1.9 + math.sin(x * 0.7))
            + 0.5 * math.sin(x * 3.1 + y * 2.3 + z * 1.7) * math.cos(z * 2.9 - x * 1.1)) / 1.5


def mix(a, b, t):
    """Blends two colours (hex or rgba) by t (0 = a)."""
    ca, cb = hex_color(a), hex_color(b)
    t = max(0.0, min(1.0, t))
    return tuple(ca[i] + (cb[i] - ca[i]) * t for i in range(4))


def on_sphere(center, radius, direction, lift=0.0):
    return Vector(center) + Vector(direction).normalized() * (radius + lift)


def veins(mb: MeshBuilder, center, radius, start_dir, rng, material="prop:flesh", color="#a01818", length=1.0,
          width=0.03, depth=3, branches=2, lift=0.004, spread=0.7, segments=5):
    """A branching vein tree crawling over a sphere (``center``, ``radius``) from ``start_dir``: each branch is a thin
    tapered tube following the surface, splitting ``depth`` times into ``branches`` thinner twigs."""
    c = Vector(center)

    def grow(direction, heading, size, w, level):
        pts, radii = [], []
        d = Vector(direction).normalized()
        h = Vector(heading)
        steps = 5
        for i in range(steps + 1):
            pts.append(on_sphere(c, radius, d, lift + w * 0.4))
            radii.append(w * (1 - 0.6 * i / steps))
            # Walk along the surface: turn the heading a little, keep it tangent.
            h = (h - d * h.dot(d)).normalized()
            h = (h + Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))) * 0.35).normalized()
            h = (h - d * h.dot(d)).normalized()
            d = (d + h * (size / steps) / radius).normalized()
        mb.tube(pts, radii, material, color, segments=segments)
        if level < depth:
            for _ in range(branches):
                side = d.cross(h) * rng.choice((-1, 1))
                nh = (h * math.cos(spread) + side * math.sin(spread)).normalized()
                grow(d, nh, size * rng.uniform(0.5, 0.75), w * 0.62, level + 1)

    d0 = Vector(start_dir).normalized()
    h0 = d0.orthogonal().normalized()
    h0 = (Matrix.Rotation(rng.uniform(0, 2 * math.pi), 3, d0) @ h0).normalized()
    grow(d0, h0, length, width, 1)


def fang(mb: MeshBuilder, base, tip, width, material="prop:plain", color="#f2ead8", segments=5):
    """A tapered, slightly curved tooth."""
    base, tip = Vector(base), Vector(tip)
    mid = base.lerp(tip, 0.5) + (tip - base).orthogonal().normalized() * (tip - base).length * 0.06
    mb.tube([base, mid, tip], [width, width * 0.62, 0.002], material, color, segments=segments)


def teeth_ring(mb: MeshBuilder, center, radius, axis, count, length, width, inward=0.6, rng=None, material="prop:plain",
               color="#f2ead8", jag=0.25):
    """Teeth around a round mouth (lamprey maws, torn eyes): bases on a ring about ``axis``, pointing inward/forward."""
    c, ax = Vector(center), Vector(axis).normalized()
    u = ax.orthogonal().normalized()
    v = ax.cross(u)
    for k in range(count):
        a = 2 * math.pi * k / count
        radial = u * math.cos(a) + v * math.sin(a)
        base = c + radial * radius
        size = length * (1 + (rng.uniform(-jag, jag) if rng else 0))
        tip = base + (ax * (1 - inward) - radial * inward).normalized() * size
        fang(mb, base, tip, width, material, color)


def teeth_row(mb: MeshBuilder, p0, p1, count, length, direction, width, rng=None, material="prop:plain", color="#f2ead8", jag=0.3):
    """A row of teeth along a jaw line from ``p0`` to ``p1``, pointing along ``direction``; tallest in the middle."""
    p0, p1, d = Vector(p0), Vector(p1), Vector(direction).normalized()
    for k in range(count):
        t = (k + 0.5) / count
        base = p0.lerp(p1, t)
        size = length * (0.55 + 0.45 * math.sin(math.pi * t)) * (1 + (rng.uniform(-jag, jag) if rng else 0))
        fang(mb, base, base + d * size, width * (0.7 + 0.3 * math.sin(math.pi * t)), material, color)


def spikes(mb: MeshBuilder, center, radius, directions, length, width, material="prop:plain", color="#e8e0d0", segments=5):
    """Cones standing out of a sphere's surface along the given directions."""
    for d in directions:
        d = Vector(d).normalized()
        base = on_sphere(center, radius, d, -width * 0.3)
        mb.cylinder(base, base + d * length, width, 0.0, material, color, segments=segments)


def fibonacci(n, hemisphere=None):
    """``n`` evenly spread unit directions (optionally only those with a positive dot against ``hemisphere``)."""
    out = []
    golden = math.pi * (3 - math.sqrt(5))
    for i in range(n):
        z = 1 - 2 * (i + 0.5) / n
        r = math.sqrt(1 - z * z)
        d = Vector((math.cos(golden * i) * r, math.sin(golden * i) * r, z))
        if hemisphere is None or d.dot(Vector(hemisphere)) > 0:
            out.append(d)
    return out

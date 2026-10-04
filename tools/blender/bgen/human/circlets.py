"""The rigid crowns that rest on the finished hair (crowns.py finds its surface and the ring round it):

- gold: a gold band lying on the hair all round, a little taller over the brow, its edges darker so it reads against
  fair hair, gold laurel sprigs along the sides, small jewels at the temples and a jewel (``flower``) over the brow in
  a fan of gold leaves;
- frost: a band of frost lying on the hair with ice crystals all round it, short behind and tallest over the brow.

Each band lies on the surface (its inner face on the hair, standing up along it), and every leaf is tipped out until
it clears the hair under it.
"""
import math

import bmesh
from mathutils import Vector

from .crowns import _branches, _finish, leaf
from .paint import hex3

PROFILE = [(math.copysign(abs(math.cos(a)) ** 0.35, math.cos(a)), math.copysign(abs(math.sin(a)) ** 0.35, math.sin(a)))
           for a in (2 * math.pi * k / 16 for k in range(16))]           # a rounded rectangle: (up, out) in -1..1


def band(bm, layer, ring, height, thick, color):
    """A closed strip along ``ring`` [(point, normal, azimuth)] lying on the surface: ``height(az)`` each way up it,
    ``2 * thick`` through with its inner face on the surface, ``color(edge, az)`` at each vertex (edge 1 at its top
    and bottom). Returns each ring point's outward and up-the-surface directions."""
    count = len(ring)
    rows, frames = [], []
    for i, (pt, n, az) in enumerate(ring):
        t = (ring[(i + 1) % count][0] - ring[i - 1][0]).normalized()
        u = n.cross(t).normalized()
        u = u if u.z >= 0 else -u
        out = t.cross(u).normalized()
        out = out if out.dot(n) >= 0 else -out
        h = height(az)
        row = []
        for x, y in PROFILE:
            v = bm.verts.new(pt + out * thick * (1 + y) + u * h * x)
            row.append((v, color(abs(x), az)))
        rows.append(row)
        frames.append((out, u))
    for i in range(count):
        a, b = rows[i], rows[(i + 1) % count]
        for k in range(len(PROFILE)):
            q = (a[k], a[(k + 1) % len(PROFILE)], b[(k + 1) % len(PROFILE)], b[k])
            f = bm.faces.new([v for v, _ in q])
            for loop, (_, c) in zip(f.loops, q):
                loop[layer] = (*c, 1.0)
    return frames


def clear(surf, base, d, n, length, gap=0.0015, curl=0.0):
    """``d`` tipped out along ``n`` until a leaf ``length`` long from ``base`` (its tip bent ``curl`` times its length
    along ``n``) stays ``gap`` off the hair."""
    d = d.normalized()
    for _ in range(8):
        sunk = False
        for f in (0.45, 0.9):
            q = base + d * length * f + n * (curl * length * f * f)
            loc, _ = surf.hit(q + n * 0.03, -n)
            sunk = sunk or (loc is not None and (loc - q).dot(n) > -gap)
        if not sunk:
            break
        d = (d + n * 0.2).normalized()
    return d


def _gem(bm, layer, centre, n, radius, color, squash=0.55):
    res = bmesh.ops.create_icosphere(bm, subdivisions=2, radius=radius)
    side = n.cross(Vector((0, 0, 1))).normalized()
    up = side.cross(n).normalized()
    for v in res["verts"]:
        v.co = centre + side * v.co.x + up * v.co.z * 1.15 + n * v.co.y * squash
    for f in {f for v in res["verts"] for f in v.link_faces}:
        for loop in f.loops:
            loop[layer] = (*color, 1.0)


def gold(head, surf, ring, p, rng):
    base = hex3(p.get("hatColor", "#ffd040"))
    accent = hex3(p.get("flower", "#e03050"))
    dark = tuple(c * 0.5 for c in base)
    bright = tuple(min(1.0, c * 1.06) for c in base)
    bm = bmesh.new()
    layer = bm.loops.layers.color.new("Color")
    thick = 0.0013
    frames = band(bm, layer, ring, lambda az: 0.0042 + 0.0024 * max(0.0, math.cos(az)) ** 3, thick,
                  lambda e, az: dark if e > 0.9 else bright)
    for pt, n, tan, az, half in _branches(ring, 0.0125):           # laurel sprigs along the sides
        front = math.cos(az)
        if front > 0.78 or front < -0.6:
            continue
        up = n.cross(tan).normalized()
        up = up if up.z >= 0 else -up
        for side in (1, -1):
            ang = math.radians(34 + rng.uniform(-6, 6))
            length = 0.019 * rng.uniform(0.9, 1.1)
            at = pt + n * (2 * thick + 0.0004) + up * side * 0.0016
            d = clear(surf, at, tan * math.cos(ang) + up * side * math.sin(ang) + n * 0.12, n, length)
            leaf(bm, layer, at, d, n, length, length * 0.36, base, fold=0.2, curl=-0.08, thick=0.0013)
    gems = bmesh.new()
    gl = gems.loops.layers.color.new("Color")
    k_front = max(range(len(ring)), key=lambda k: math.cos(ring[k][2]))
    out, rise = frames[k_front]
    c = ring[k_front][0] + out * (2 * thick + 0.002)
    for k in range(-3, 4):    # gold leaves fanning up round the jewel
        a = math.radians(24 * k)
        d = rise * math.cos(a) + out.cross(rise) * math.sin(a) + out * 0.12
        length = 0.022 - 0.0026 * abs(k)
        d = clear(surf, c + d * 0.004, d, out, length)
        leaf(bm, layer, c - out * 0.002 + d * 0.004, d, out, length, length * 0.34, base, fold=0.22, curl=-0.08,
             thick=0.0013)
    _gem(bm, layer, c, out, 0.0068, base, 0.5)                       # its setting
    _gem(gems, gl, c + out * 0.0026, out, 0.0056, accent)
    for sx in (1, -1):        # small jewels at the temples
        k = min(range(len(ring)), key=lambda k: abs(ring[k][2] - (math.pi / 2 - 0.45 if sx > 0 else 1.5 * math.pi + 0.45)))
        o, _ = frames[k]
        _gem(bm, layer, ring[k][0] + o * (2 * thick + 0.0008), o, 0.0034, base, 0.5)
        _gem(gems, gl, ring[k][0] + o * (2 * thick + 0.0022), o, 0.0026, accent)
    return [_finish(bm, head, "wreath", "char:metal"), _finish(gems, head, "circlet_jewel", "char:eye")]


def frost(head, surf, ring, p, rng):
    from .crowns import _shard
    base = hex3(p.get("hatColor", "#e8f8ff"))
    cyan = (0.55, 0.82, 1.0)
    bm = bmesh.new()
    layer = bm.loops.layers.color.new("Color")
    thick = 0.0017
    frames = band(bm, layer, ring, lambda az: 0.0044 + 0.0012 * max(0.0, math.cos(az)), thick,
                  lambda e, az: tuple(w + (c - w) * (0.2 + 0.25 * e) for w, c in zip(base, cyan)))
    count, smooth = len(ring), len(bm.faces)
    for k in range(0, count, 3):
        pt, n, az = ring[k]
        out, u = frames[k]
        tang = (ring[(k + 1) % count][0] - ring[k - 1][0]).normalized()
        front = 0.5 + 0.5 * math.cos(az)
        for j in range(2 if front > 0.45 else 1):
            height = (0.017 + 0.046 * front ** 2) * (1.0 if j == 0 else 0.58) * rng.uniform(0.85, 1.1)
            up = (Vector((0, 0, 1)) * 0.6 + u * 0.4 + out * (0.22 + 0.12 * j)).normalized()
            up = (up + tang * rng.uniform(-0.2, 0.2) * (1 if j else 0.4)).normalized()
            at = pt + out * (2 * thick + 0.0008) + u * 0.002 + tang * (0.003 * j * (1 if k % 6 else -1))
            _shard(bm, layer, at, up, out, height, height * 0.15 + 0.0022, base, cyan)
    obj = _finish(bm, head, "frost_crown", "char:eye")
    for poly in obj.data.polygons:      # (the band smooth, the crystals cut in flat facets)
        poly.use_smooth = poly.index < smooth
    return [obj]

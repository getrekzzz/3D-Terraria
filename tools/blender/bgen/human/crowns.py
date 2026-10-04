"""Headwear that rests on the finished hair (or the bare scalp): the outer surface is found by casting rays at the
groomed hair and the head, so each piece sits on it all round whatever the hairstyle. ``hatShape``:

- leaves: a wreath of leaves on a twisted vine, two branches meeting at the brow, with flowers (``flower``) along it;
- fronds: big leaves standing up round the head like a headdress;
- gold: a gold circlet with small gold laurel leaves along the sides and a jewel (``flower``) over the brow in a
  fan of gold leaves;
- frost: a crown of ice, a band of frost with crystal shards rising tallest over the brow;
- party (a cap): a striped paper cone perched on top with a pompom; nurse (a cap): a soft white crown behind a stiff
  cuff rolled back at the top, with a red cross.
"""
import math

import bmesh
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

from .features import blade, paint, round_section, sweep
from .garments import mesh_from, set_slot
from .paint import hex3
from .rig import rigid


class Surface:
    """The outer surface of the head and its hair (head-local units), probed with rays toward the skull."""

    def __init__(self, head, objs):
        self.head = head
        verts, polys = [], []
        for o in objs:
            if o.type != "MESH":
                continue
            base = len(verts)
            mw = o.matrix_world
            verts += [head.local(mw @ v.co) for v in o.data.vertices]
            polys += [[base + i for i in p.vertices] for p in o.data.polygons]
        self.tree = BVHTree.FromPolygons(verts, polys)

    def hit(self, origin, direction):
        """The first surface point (and its normal) on the ray from ``origin`` going ``direction``."""
        loc, nrm, _, _ = self.tree.ray_cast(Vector(origin), Vector(direction).normalized(), 1.0)
        return (loc, nrm) if loc is not None else (None, None)

    def ring(self, centre, tilt, n=96, sink=0.0015):
        """A closed path round the head on the tilted plane through ``centre`` (rising ``tilt`` per unit forward),
        on the hair's outer surface smoothed round the ring: [(point, outward normal, azimuth)]."""
        c = Vector(centre)
        rad, nrm, dirs = [], [], []
        for k in range(n):
            a = 2 * math.pi * k / n
            d = Vector((math.sin(a), math.cos(a), tilt * math.cos(a))).normalized()
            loc, normal = self.hit(c + d * 0.4, -d)
            r = (loc - c).length if loc is not None else 0.09
            rad.append(r)
            nrm.append(normal if normal is not None else d)
            dirs.append(d)
        rad = np.asarray(rad)
        w = np.exp(-0.5 * (np.arange(-8, 9) / 3.0) ** 2)
        sm = np.convolve(np.r_[rad[-8:], rad, rad[:8]], w / w.sum(), "valid")
        sm = np.maximum(sm, rad - 0.004)                     # stands proud of no lock by more than a few mm
        sm = np.convolve(np.r_[sm[-8:], sm, sm[:8]], w / w.sum(), "valid") - sink
        out = []
        for k in range(n):
            nb = sum((nrm[(k + j) % n] for j in range(-3, 4)), Vector()).normalized()
            out.append((c + dirs[k] * sm[k], nb, 2 * math.pi * k / n))
        return out


def _jitter(rng, color, amount):
    k = 1 + rng.uniform(-amount, amount)
    hue = rng.uniform(-amount, amount) * 0.5
    return tuple(min(1.0, max(0.0, c * k + hue * (i - 1))) for i, c in enumerate(color))


def leaf(bm, layer, base, d, n, length, width, color, fold=0.3, curl=-0.12, thick=0.0011, rows=6, round_tip=False):
    """A leaf: a blade with a paler midrib and underside."""
    def tone(u, v, under):
        k = (1.12 if abs(v) < 0.12 else 1.0) * (1.08 if under else 1.0) * (0.9 + 0.1 * (1 - abs(v)))
        return tuple(min(1.0, c * k) for c in color)
    blade(bm, layer, base, d, n, length, width, tone, fold, curl, thick, rows, round_tip)


def _flower(bm, layer, centre, n, size, color, rng):
    """Five round petals cupped round a domed centre."""
    t = n.orthogonal().normalized()
    for k in range(5):
        a = 2 * math.pi * k / 5 + rng.uniform(-0.2, 0.2)
        d = (t * math.cos(a) + n.cross(t) * math.sin(a)) + n * 0.35
        leaf(bm, layer, centre - n * 0.001, d, n, size, size * 0.62, _jitter(rng, color, 0.06), fold=-0.15, curl=0.1,
             thick=0.0008, rows=4, round_tip=True)
    res = bmesh.ops.create_icosphere(bm, subdivisions=1, radius=size * 0.32)
    for v in res["verts"]:
        v.co = centre + v.co + n * (v.co.dot(n) * -0.4 + size * 0.08)
    eye = (0.75, 0.45, 0.12)
    for f in {f for v in res["verts"] for f in v.link_faces}:
        for loop in f.loops:
            loop[layer] = (*eye, 1.0)


def _branches(ring, spacing):
    """Stations along the ring for leaves: two branches from the back meeting at the brow. Each station is
    (point, normal, tangent pointing toward the front, azimuth)."""
    pts = [r[0] for r in ring]
    n = len(pts)
    acc = [0.0]
    for k in range(n):
        acc.append(acc[-1] + (pts[(k + 1) % n] - pts[k]).length)
    total = acc[-1]
    out = []
    for half in (0, 1):
        s = total / 2 - spacing * 0.5
        while s > spacing * 0.4:
            pos = s if half == 0 else total - s
            k = int(np.searchsorted(acc, pos) - 1) % n
            f = (pos - acc[k]) / max(acc[k + 1] - acc[k], 1e-9)
            p = pts[k].lerp(pts[(k + 1) % n], f)
            tangent = (pts[(k + 1) % n] - pts[k]).normalized() * (-1 if half == 0 else 1)
            out.append((p, ring[k][1], tangent, ring[k][2], half))
            s -= spacing
    return out


def build(head, objs, p):
    """The headwear resting on the hair, as objects bound to the head."""
    surf = Surface(head, objs)
    shape = p.get("hatShape", "leaves" if p.get("hat") == "wreath" else "party")
    if shape == "party":
        return [_party(head, surf, p)]
    if shape == "nurse":
        return [_nurse(head, surf, p)]
    return _wreath(head, surf, p, shape)


def _finish(bm, head, name, slot):
    for v in bm.verts:
        v.co = head.at(v.co)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    obj = mesh_from(bm, name)
    set_slot(obj, slot)
    rigid(obj, "head")
    return obj


def _wreath(head, surf, p, shape):
    rng = np.random.default_rng(sum(map(ord, shape + p.get("hatColor", ""))))
    base = hex3(p.get("hatColor", "#7ab050"))
    accent = hex3(p.get("flower", "#f0d040"))
    tilt = {"fronds": 0.3, "gold": 0.28, "frost": 0.26}.get(shape, 0.32)
    ring = surf.ring((0, -0.012, 0.056 if shape != "fronds" else 0.05), tilt)
    if shape == "frost":
        return _frost(head, ring, base, rng)
    bm = bmesh.new()
    layer = bm.loops.layers.color.new("Color")
    gold = shape == "gold"
    path = [r[0] + r[1] * (0.0022 if gold else 0.0026) for r in ring]
    loop = path + [path[0]]
    if gold:      # a flat band standing on the hair
        verts = sweep(bm, loop, lambda t, a: (math.cos(a) * 0.0016, math.sin(a) * 0.0048), 10,
                      up=Vector((0, 0, 1)), caps=(False, False))
        paint(bm, verts, lambda t, a: tuple(c * (0.85 + 0.25 * max(0.0, math.cos(a))) for c in base))
    else:         # two thin stems twisting round each other
        for phase in (0.0, math.pi):
            twist = [pt + (r[1] * math.cos(phase + 26 * r[2]) + r[1].cross(Vector((0, 0, 1))).normalized() * math.sin(phase + 26 * r[2]) * 0.6) * 0.0016
                     for pt, r in zip(path, ring)]
            twist.append(twist[0])
            stem = tuple(c * 0.55 for c in base)
            verts = sweep(bm, twist, round_section(lambda t: 0.0016), 6, caps=(False, False))
            paint(bm, verts, lambda t, a: stem)
    spacing = {"fronds": 0.016, "gold": 0.011}.get(shape, 0.0125)
    flowers = []
    for i, (pt, n, tan, az, half) in enumerate(_branches(ring, spacing)):
        front = math.cos(az)
        if gold and front > 0.82:
            continue
        up = n.cross(tan).normalized() * (1 if half else -1)
        if up.z < 0:
            up = -up
        for side in ((1, -1) if shape != "fronds" else (1,)):
            if shape == "fronds":
                length = 0.04 + 0.016 * (0.5 + 0.5 * front) + rng.uniform(-0.004, 0.004)
                d = (tan * 0.35 + up * 1.0 + n * 0.4)
                leaf(bm, layer, pt + n * 0.001, d, n, length, length * 0.3, _jitter(rng, base, 0.12), fold=0.35, curl=0.1)
                continue
            ang = math.radians(38 + rng.uniform(-8, 8))
            d = tan * math.cos(ang) + up * side * math.sin(ang) + n * (0.2 if gold else 0.36)
            size = 0.03 if not gold else 0.018
            length = size * rng.uniform(0.85, 1.12) * (1.0 + 0.15 * front)
            color = base if gold else _jitter(rng, base, 0.12)
            leaf(bm, layer, pt + up * side * 0.0012, d, n, length, length * (0.36 if gold else 0.4), color,
                 fold=0.28, curl=-0.1 if gold else -0.16)
        if shape == "leaves" and i % 5 == 2:
            flowers.append((pt + n * 0.004, n))
    objs = []
    if gold:      # the jewel over the brow in a gold setting
        front = max(ring, key=lambda r: math.cos(r[2]))
        c, n = front[0] + front[1] * 0.004, front[1]
        rise = (Vector((0, 0, 1)) - n * n.z).normalized()
        for k in range(-3, 4):    # gold leaves fanning up round it
            a = math.radians(24 * k)
            d = rise * math.cos(a) + n.cross(rise) * math.sin(a) + n * 0.12
            length = 0.021 - 0.0026 * abs(k)
            leaf(bm, layer, c - n * 0.002 + d * 0.004, d, n, length, length * 0.34, base, fold=0.25, curl=-0.08)
        res = bmesh.ops.create_icosphere(bm, subdivisions=2, radius=0.0062)
        for v in res["verts"]:
            v.co = c + Vector((v.co.x, v.co.y * 0.6, v.co.z * 1.25))
        for f in {f for v in res["verts"] for f in v.link_faces}:
            for loop in f.loops:
                loop[layer] = (*base, 1.0)
        gem = bmesh.new()
        res = bmesh.ops.create_icosphere(gem, subdivisions=2, radius=0.0052)
        for v in res["verts"]:
            v.co = c + n * 0.0026 + Vector((v.co.x, v.co.y * 0.55, v.co.z * 1.2))
        gl = gem.loops.layers.color.new("Color")
        for f in gem.faces:
            for loop in f.loops:
                loop[gl] = (*accent, 1.0)
        objs.append(_finish(gem, head, "circlet_jewel", "char:eye"))
    else:
        fb = bmesh.new()
        fl = fb.loops.layers.color.new("Color")
        for c, n in flowers:
            _flower(fb, fl, c, n, 0.0085 + rng.uniform(-0.001, 0.0015), accent, rng)
        if flowers:
            objs.append(_finish(fb, head, "wreath_flowers", "char:cloth"))
    objs.insert(0, _finish(bm, head, "wreath", "char:metal" if gold else "char:leather"))
    return objs


def _frost(head, ring, base, rng):
    """A band of frost on the hair with crystal shards rising from it, tallest over the brow."""
    bm = bmesh.new()
    layer = bm.loops.layers.color.new("Color")
    path = [r[0] + r[1] * 0.002 for r in ring]
    verts = sweep(bm, path + [path[0]], lambda t, a: (math.cos(a) * 0.0024, math.sin(a) * 0.004), 8,
                  up=Vector((0, 0, 1)), caps=(False, False))
    paint(bm, verts, lambda t, a: tuple(c * (0.85 + 0.15 * math.cos(a)) for c in base))
    cyan = (0.55, 0.82, 1.0)
    for k in range(0, len(ring), 4):
        pt, n, az = ring[k]
        front = 0.5 + 0.5 * math.cos(az)
        for j in range(2 if front > 0.5 else 1):
            height = (0.012 + 0.05 * front ** 2.2) * (1.0 if j == 0 else 0.55) * rng.uniform(0.85, 1.1)
            up = (Vector((0, 0, 1)) * 1.0 + n * (0.25 + 0.15 * j)).normalized()
            tang = n.cross(Vector((0, 0, 1))).normalized()
            up = (up + tang * rng.uniform(-0.25, 0.25) * (1 if j else 0.3)).normalized()
            base_pt = pt + n * 0.002 + tang * (0.003 * j * (1 if k % 8 else -1))
            _shard(bm, layer, base_pt, up, n, height, height * 0.16 + 0.002, base, cyan)
    obj = _finish(bm, head, "frost_crown", "char:eye")
    for poly in obj.data.polygons:
        poly.use_smooth = False
    return [obj]


def _shard(bm, layer, base, up, out, height, width, white, cyan):
    """A crystal: a six-sided prism tapering to a point, flat faceted."""
    side = up.cross(out).normalized()
    fwd = side.cross(up).normalized()
    ring0, ring1 = [], []
    for k in range(6):
        a = 2 * math.pi * k / 6
        off = side * math.cos(a) * width + fwd * math.sin(a) * width * 0.55
        ring0.append(bm.verts.new(base - up * 0.003 + off))
        ring1.append(bm.verts.new(base + up * height * 0.72 + off * 0.8))
    tip = bm.verts.new(base + up * height)
    faces = []
    for k in range(6):
        faces.append(bm.faces.new((ring0[k], ring0[(k + 1) % 6], ring1[(k + 1) % 6], ring1[k])))
        faces.append(bm.faces.new((ring1[k], ring1[(k + 1) % 6], tip)))
    faces.append(bm.faces.new(ring0[::-1]))
    for f in faces:
        for loop in f.loops:
            t = max(0.0, (loop.vert.co - base).dot(up) / height)
            c = tuple(w + (cc - w) * (0.65 - 0.5 * t) for w, cc in zip(white, cyan))
            loop[layer] = (*c, 1.0)


def _party(head, surf, p):
    """A paper party cone perched on top of the hair, tipped to one side, striped, with a pompom."""
    base = hex3(p.get("hatColor", "#ff6ae0"))
    stripe = tuple(c + (1 - c) * 0.8 for c in base)
    axis = Vector((0.32, 0.12, 1.0)).normalized()
    c0 = Vector((0.0, -0.005, 0.02))
    loc, n = surf.hit(c0 + axis * 0.4, -axis)
    seat = loc if loc is not None else c0 + axis * 0.11
    bm = bmesh.new()
    layer = bm.loops.layers.color.new("Color")
    r0, height = 0.04, 0.12
    path = [seat - axis * 0.01 + axis * (height * i / 24) for i in range(25)]
    verts = sweep(bm, path, round_section(lambda t: max(r0 * (1 - t), 0.0012)), 28, caps=(True, False))
    paint(bm, verts, lambda t, a: stripe if math.sin(a * 2 + t * 14) > 0.35 else base)
    tip = path[-1]
    res = bmesh.ops.create_icosphere(bm, subdivisions=2, radius=0.014)
    for v in res["verts"]:
        d = v.co.normalized()
        lump = 1 + 0.12 * math.sin(d.x * 9 + d.y * 4) * math.sin(d.z * 8 + d.x * 3)
        v.co = tip + axis * 0.008 + v.co * lump
    for f in {f for v in res["verts"] for f in v.link_faces}:
        for loop in f.loops:
            loop[layer] = (*stripe, 1.0)
    return _finish(bm, head, "party_hat", "char:cloth")


def _nurse(head, surf, p):
    """A nurse's cap: a soft white crown over the top of the hair behind a stiff cuff standing up at the front, its
    top edge rolled back, with a red cross on the cuff."""
    base = hex3(p.get("hatColor", "#ffffff"))
    bm = bmesh.new()
    layer = bm.loops.layers.color.new("Color")
    cols, rows, depth = 25, 9, 7
    band, crown = [], []
    for j in range(cols):
        a = math.radians(-54 + 108 * j / (cols - 1))
        d = Vector((math.sin(a), 0.3, math.cos(a))).normalized()
        foot = _probe(surf, d)
        lean = Vector((0, -0.35, 0.1)) * (1 - 0.6 * abs(math.sin(a)))
        rise = 0.03 * (1 - 0.38 * (a / math.radians(54)) ** 2)
        band.append([foot - d * 0.002 + (d + lean) * rise * t
                     + (Vector((0, -1, 0.3)) * 0.016 - d * 0.004) * max(0.0, t - 0.72) ** 2 / 0.0784
                     for t in (i / (rows - 1) for i in range(rows))])
        col = []
        for k in range(depth):
            s = k / (depth - 1)
            ak, th = a * (1 - 0.3 * s), math.radians(48 * s)
            dk = Vector((math.sin(ak), 0.3, math.cos(ak))).normalized()
            dk = Vector((dk.x, dk.y * math.cos(th) - dk.z * math.sin(th), dk.y * math.sin(th) + dk.z * math.cos(th)))
            puff = 0.0025 + 0.0065 * math.sin(math.pi * min(s * 1.15, 1.0)) * (1 - 0.6 * (a / math.radians(54)) ** 2)
            col.append(_probe(surf, dk) + dk * puff)
        crown.append(col)
    band = [list(r) for r in zip(*band)]
    crown = [list(r) for r in zip(*crown)]
    _sheet(bm, band, 0.0017)
    _sheet(bm, crown, 0.0012)
    for f in bm.faces:
        for loop in f.loops:
            loop[layer] = (*base, 1.0)
    red = (0.82, 0.12, 0.14)
    lu = sum((a - b).length for a, b in zip(band[4][1:], band[4]))
    lv = sum((r1[12] - r0[12]).length for r0, r1 in zip(band, band[1:]))
    for w, h in ((0.0034, 0.0105), (0.0105, 0.0034)):       # the red cross, laid on the cuff's front
        g = [[_on_sheet(band, 0.5 + w * x / lu, 0.42 + h * y / lv, 0.0024) for x in (-1, 0, 1)] for y in (-1, 0, 1)]
        for f in _sheet(bm, g, 0.0005):
            for loop in f.loops:
                loop[layer] = (*red, 1.0)
    return _finish(bm, head, "nurse_cap", "char:cloth")


def _probe(surf, d):
    loc, _ = surf.hit(d * 0.4, -d)
    return loc if loc is not None else d * 0.11


def _normals(grid):
    rows, cols = len(grid), len(grid[0])
    return [[(grid[i][min(j + 1, cols - 1)] - grid[i][max(j - 1, 0)]).cross(
        grid[min(i + 1, rows - 1)][j] - grid[max(i - 1, 0)][j]).normalized() for j in range(cols)] for i in range(rows)]


def _on_sheet(grid, u, v, lift):
    """The point at (u across, v up) on a grid sheet, lifted off its front (the side facing forward) by ``lift``."""
    rows, cols = len(grid), len(grid[0])
    x, y = u * (cols - 1), v * (rows - 1)
    j, i = min(int(x), cols - 2), min(int(y), rows - 2)
    fx, fy = x - j, y - i
    pt = (grid[i][j] * (1 - fx) + grid[i][j + 1] * fx) * (1 - fy) + (grid[i + 1][j] * (1 - fx) + grid[i + 1][j + 1] * fx) * fy
    n = _normals(grid)[i][j]
    return pt + n * lift * (1 if n.y > 0 else -1)


def _sheet(bm, grid, thick):
    """A thick sheet from a grid of points: two skins offset along the normal and a rim. Returns its faces."""
    norms = _normals(grid)
    rows, cols = len(grid), len(grid[0])
    skins = [[[bm.verts.new(p + n * thick * side) for p, n in zip(r, nr)] for r, nr in zip(grid, norms)]
             for side in (1, -1)]
    faces = []
    for g in skins:
        for i in range(rows - 1):
            for j in range(cols - 1):
                faces.append(bm.faces.new((g[i][j], g[i][j + 1], g[i + 1][j + 1], g[i + 1][j])))
    rim = ([(i, 0) for i in range(rows)] + [(rows - 1, j) for j in range(1, cols)]
           + [(i, cols - 1) for i in range(rows - 2, -1, -1)] + [(0, j) for j in range(cols - 2, 0, -1)])
    for (i0, j0), (i1, j1) in zip(rim, rim[1:] + rim[:1]):
        a, b = skins
        faces.append(bm.faces.new((a[i0][j0], b[i0][j0], b[i1][j1], a[i1][j1])))
    return faces

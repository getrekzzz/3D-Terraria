"""Headwear that rests on the finished hair (or the bare scalp): the outer surface is found by casting rays at the
groomed hair and the head, so each piece sits on it all round whatever the hairstyle. ``hatShape``:

- leaves: a wreath of leaves on a twisted vine, two branches meeting at the brow, with flowers (``flower``) along it;
- fronds: big leaves swept back along the head from the brow like a headdress;
- gold, frost: a gold circlet, a crown of ice (circlets.py);
- party, nurse (caps): perched on the hair (perched.py).
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

    def ring(self, centre, tilt, n=96, sink=0.0015, rest=False):
        """A closed path round the head on the tilted plane through ``centre`` (rising ``tilt`` per unit forward),
        on the hair's outer surface smoothed round the ring: [(point, outward normal, azimuth)]. ``rest``: for a
        rigid band, lying on the outermost locks and bridging the hollows between them, sunk into none."""
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
        if rest:
            sm = np.max([np.roll(rad, k) for k in range(-1, 2)], 0)
            w = np.exp(-0.5 * (np.arange(-8, 9) / 1.3) ** 2)
            sm = np.maximum(np.convolve(np.r_[sm[-8:], sm, sm[:8]], w / w.sum(), "valid"), rad) - sink
        else:
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
    if shape in ("party", "nurse"):
        from . import perched
        return [getattr(perched, shape)(head, surf, p)]
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
    tilt = {"fronds": 0.3, "gold": 0.28, "frost": 0.26}.get(shape, 0.32)
    rigid = shape in ("gold", "frost")
    ring = surf.ring((0, -0.012, 0.056 if shape != "fronds" else 0.05), tilt, sink=0.0004 if rigid else 0.0015,
                     rest=rigid)
    if rigid:
        from . import circlets
        return getattr(circlets, shape)(head, surf, ring, p, rng)
    base = hex3(p.get("hatColor", "#7ab050"))
    accent = hex3(p.get("flower", "#f0d040"))
    bm = bmesh.new()
    layer = bm.loops.layers.color.new("Color")
    path = [r[0] + r[1] * 0.0026 for r in ring]
    for phase in (0.0, math.pi):          # two thin stems twisting round each other
        twist = [pt + (r[1] * math.cos(phase + 26 * r[2]) + r[1].cross(Vector((0, 0, 1))).normalized() * math.sin(phase + 26 * r[2]) * 0.6) * 0.0016
                 for pt, r in zip(path, ring)]
        twist.append(twist[0])
        stem = tuple(c * 0.55 for c in base)
        verts = sweep(bm, twist, round_section(lambda t: 0.0016), 6, caps=(False, False))
        paint(bm, verts, lambda t, a: stem)
    flowers = []
    for i, (pt, n, tan, az, half) in enumerate(_branches(ring, 0.014 if shape == "fronds" else 0.0125)):
        front = math.cos(az)
        up = n.cross(tan).normalized() * (1 if half else -1)
        if up.z < 0:
            up = -up
        if shape == "fronds":      # big leaves swept back along the head from the brow, overlapping
            from .circlets import clear
            if front < -0.8:       # (the stems' bare ends tied behind)
                continue
            length = 0.038 + 0.012 * (0.5 + 0.5 * front) + rng.uniform(-0.004, 0.004)
            d = clear(surf, pt + n * 0.0015, -tan * 0.9 + up * 0.32 + n * 0.22, n, length, 0.002, -0.2)
            leaf(bm, layer, pt + n * 0.0015, d, n, length, length * 0.3, _jitter(rng, base, 0.12), fold=0.12, curl=-0.2,
                 thick=0.0014)
            continue
        for side in (1, -1):
            ang = math.radians(38 + rng.uniform(-8, 8))
            d = tan * math.cos(ang) + up * side * math.sin(ang) + n * 0.36
            length = 0.03 * rng.uniform(0.85, 1.12) * (1.0 + 0.15 * front)
            leaf(bm, layer, pt + up * side * 0.0012, d, n, length, length * 0.4, _jitter(rng, base, 0.12),
                 fold=0.28, curl=-0.16)
        if shape == "leaves" and i % 5 == 2:
            flowers.append((pt + n * 0.004, n))
    objs = [_finish(bm, head, "wreath", "char:leather")]
    if flowers:
        fb = bmesh.new()
        fl = fb.loops.layers.color.new("Color")
        for c, n in flowers:
            _flower(fb, fl, c, n, 0.0085 + rng.uniform(-0.001, 0.0015), accent, rng)
        objs.append(_finish(fb, head, "wreath_flowers", "char:cloth"))
    return objs


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

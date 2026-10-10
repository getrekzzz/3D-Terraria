"""Bodies made of other stuff on the human rig (``anatomy``): stone (a golem of granite blocks, golem.py), wood (a
gnarled wooden creature, woodfolk.py), cookie (a gingerbread man, cookie.py) and armor (an empty suit of plate,
plate.py). Each kind builds its own parts round the frame's joints, weighted to the armature, painted and slotted;
they are joined here and given the people's actions, so the shared animation sets fit them unchanged.

Shared helpers: meshing a distance field (cached on disk by fieldmesh), painting per vertex with numpy, copying a
nearby surface's weights, rigid pieces weighted by which of them each vertex belongs to, rounded and chipped boxes,
and deterministic value noise (every random choice is seeded from fixed constants).
"""
import importlib
import zlib

import numpy as np

from . import fieldmesh
from .sdf import F, smax

KINDS = {"stone": "golem", "wood": "woodfolk", "cookie": "cookie", "armor": "plate"}


def build(name, p):
    """The rigged, animated character [armature, mesh] for a body of another material."""
    from . import frame as frame_mod, join, motion, occlusion, rest_uvs
    from .head import Head
    fr = frame_mod.Frame(p)
    head = Head(fr, p)
    arm = frame_mod.build_armature(fr, name)
    kind = importlib.import_module(f".{KINDS[p['anatomy']]}", __package__)
    objs = kind.build(fr, head, arm, p)
    for o in objs:
        rest_uvs(o)
    mesh = join(objs, name)
    occlusion.bake(mesh, 0.1 * fr.s)
    mesh.parent = arm
    mod = mesh.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    motion.animate(arm, fr, p)
    return [arm, mesh]


# ---------------------------------------------------------------------------------------------- meshing
def mesh(field, bounds, h, target, name, key, sources, min_piece=None):
    """The zero set of ``field`` (world metres) as a mesh object of about ``target`` triangles, cached under ``key``
    and the source of the modules ``sources`` (this one is always included)."""
    bounds = tuple(tuple(round(float(v), 4) for v in b) for b in bounds)
    return fieldmesh.build(field, bounds, h, target, name, (key, name), tuple(sources) + ("matter.py",), "matter",
                           min_piece=min_piece)


def frame_key(fr):
    return (fr.female, fr.old, round(fr.height, 4))


def box_of(points, pad):
    pts = np.asarray([tuple(v) for v in points], dtype=float)
    return tuple(pts.min(0) - pad), tuple(pts.max(0) + pad)


def coords(obj):
    co = np.empty(len(obj.data.vertices) * 3, dtype=np.float32)
    obj.data.vertices.foreach_get("co", co)
    return co.reshape(-1, 3).astype(float)


def normals(obj):
    n = np.empty(len(obj.data.vertices) * 3, dtype=np.float32)
    obj.data.vertices.foreach_get("normal", n)
    return n.reshape(-1, 3).astype(float)


def paint(obj, rgb):
    """Per-vertex colours (N, 3, sRGB 0..1) onto every face corner of ``obj``'s "Color" attribute."""
    me = obj.data
    if "Color" not in me.color_attributes:
        me.color_attributes.new("Color", "BYTE_COLOR", "CORNER")
    layer = me.color_attributes["Color"]
    me.color_attributes.active_color = layer
    idx = np.empty(len(me.loops), dtype=np.int64)
    me.loops.foreach_get("vertex_index", idx)
    rgb = np.clip(np.asarray(rgb, dtype=float).reshape(-1, 3), 0, 1)
    rgba = np.concatenate([rgb[idx], np.ones((len(idx), 1))], 1).astype(np.float32)
    layer.data.foreach_set("color_srgb", rgba.ravel())


def slot(obj, name):
    from .garments import set_slot
    set_slot(obj, name)


def hexrgb(value):
    from .paint import hex3
    return np.asarray(hex3(value) if isinstance(value, str) else value, dtype=float)


# ---------------------------------------------------------------------------------------------- weights
def copy_weights(obj, lookup, bones=None):
    """Each vertex takes the weights of the nearest vertex of a weighted surface (``lookup``: rig.body_lookup);
    with ``bones`` only those bones' share is kept (renormalised), falling back to the nearest of them."""
    from .rig import write_weights
    tree, body_w = lookup
    out = []
    for v in obj.data.vertices:
        w = dict(body_w[tree.find(v.co)[1]])
        if bones is not None:
            w = {b: x for b, x in w.items() if b in bones} or {bones[0]: 1.0}
        out.append(w)
    write_weights(obj, out)


def piece_weights(obj, pieces):
    """Rigid pieces meshed together: each vertex goes wholly to the bone of the piece whose field is nearest
    (``pieces``: [(bone, field)])."""
    from .rig import write_weights
    co = coords(obj).astype(F)
    d = np.stack([f(co) for _, f in pieces], 1)
    pick = np.argmin(d, 1)
    write_weights(obj, [{pieces[k][0]: 1.0} for k in pick])
    return pick


# ---------------------------------------------------------------------------------------------- shapes
def basis(x_dir, y_hint):
    """Local-to-world rotation (columns are the local axes): x along ``x_dir``, y toward ``y_hint``, z = x cross y."""
    x = np.asarray(x_dir, dtype=float)
    x = x / np.linalg.norm(x)
    y = np.asarray(y_hint, dtype=float)
    y = y - x * (y @ x)
    y = y / np.linalg.norm(y)
    return np.stack([x, y, np.cross(x, y)], 1)


def local(p, c, rot=None):
    q = p - np.asarray(c, dtype=F)
    return q if rot is None else q @ np.asarray(rot, dtype=F)


def round_box(q, half, r):
    """A box of half sizes ``half`` with edges rounded by ``r``, in its own coordinates ``q``."""
    d = np.abs(q) - (np.asarray(half, dtype=F) - F(r))
    return np.linalg.norm(np.maximum(d, F(0)), axis=1) + np.minimum(d.max(1), F(0)) - F(r)


def chips(d, q, half, seed, count=6, depth=(0.15, 0.4), soft=0.004):
    """Knocks ``count`` corners and edges off a box (``half``, own coordinates ``q``) with tilted planes."""
    rng = np.random.default_rng(seed)
    half = np.asarray(half, dtype=float)
    for _ in range(count):
        n = rng.choice((-1.0, 1.0), 3) * (0.25 + rng.random(3))
        n /= np.linalg.norm(n)
        reach = float(np.abs(n) @ half)
        cut = reach * (1 - rng.uniform(*depth) * 0.5)
        d = smax(d, q @ n.astype(F) - F(cut), soft)
    return d


def crack(d, q, normal, offset, width, depth, along=None, reach=None, wiggle=0.0, seed=0):
    """A V-shaped crack cut ``depth`` into the surface ``d`` along the plane q.normal = offset, ``width`` wide at the
    surface, wandering by ``wiggle``; with ``along``/``reach`` it runs only where q.along < reach."""
    n = np.asarray(normal, dtype=float)
    n = (n / np.linalg.norm(n)).astype(F)
    plane = q @ n - F(offset)
    if wiggle:
        plane = plane + F(wiggle) * (noise(q, 1.0 / max(width * 9, 1e-4), seed) - F(0.5))
    groove = np.abs(plane) - F(width) - np.minimum(d, F(0)) * F(width / depth)
    if along is not None:
        a = np.asarray(along, dtype=float)
        groove = np.maximum(groove, q @ (a / np.linalg.norm(a)).astype(F) - F(reach))
    return np.maximum(d, -groove)


# ---------------------------------------------------------------------------------------------- noise
def hash3(i, seed=0):
    """A pseudo-random value in [0, 1) for each integer lattice point (N, 3)."""
    i = i.astype(np.int64) & 0xFFFFFFFF
    h = (i[:, 0] * 374761393 + i[:, 1] * 668265263 + i[:, 2] * 1440662683 + seed * 2246822519 + 3266489917) & 0xFFFFFFFF
    h = ((h ^ (h >> 15)) * 2246822519) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 3266489917) & 0xFFFFFFFF
    h = h ^ (h >> 16)
    return ((h & 0xFFFFFF).astype(np.float64) / float(0x1000000)).astype(F)


def noise(p, freq, seed=0):
    """Smooth value noise in [0, 1) at ``freq`` cycles per unit."""
    g = np.asarray(p, dtype=np.float64) * freq
    i = np.floor(g)
    t = g - i
    t = t * t * (3 - 2 * t)
    i = i.astype(np.int64)
    out = np.zeros(len(g))
    for dx in (0, 1):
        wx = t[:, 0] if dx else 1 - t[:, 0]
        for dy in (0, 1):
            wy = t[:, 1] if dy else 1 - t[:, 1]
            for dz in (0, 1):
                wz = t[:, 2] if dz else 1 - t[:, 2]
                out += wx * wy * wz * hash3(i + np.asarray((dx, dy, dz)), seed)
    return out.astype(F)


def fbm(p, freq, octaves=3, seed=0):
    """Value noise summed over octaves (each twice the frequency and half the weight), in [0, 1)."""
    out, w, total = 0.0, 1.0, 0.0
    for k in range(octaves):
        out = out + w * noise(p, freq * 2 ** k, seed + 17 * k)
        total += w
        w *= 0.5
    return (out / total).astype(F)


def seed_of(text):
    return zlib.crc32(text.encode()) & 0xFFFF


def ss(a, b, x):
    """Smoothstep from a to b (arrays)."""
    t = np.clip((np.asarray(x, dtype=float) - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


# ---------------------------------------------------------------------------------------------- placing on surfaces
def onto(field, origin, direction, far, steps=24):
    """Where rays from ``origin`` (outside) along ``direction`` first cross into ``field`` within ``far``: bisection
    between the start and the far end, which must lie inside. Arrays (N, 3) in, (N, 3) out."""
    o = np.asarray(origin, dtype=F).reshape(-1, 3)
    d = np.asarray(direction, dtype=F).reshape(-1, 3)
    d = d / np.linalg.norm(d, axis=1, keepdims=True)
    lo = np.zeros(len(o), dtype=F)
    hi = np.full(len(o), F(far))
    for _ in range(steps):
        mid = (lo + hi) * F(0.5)
        inside = field(o + d * mid[:, None]) < 0
        hi = np.where(inside, mid, hi)
        lo = np.where(inside, lo, mid)
    return o + d * hi[:, None]


def gradient(field, p, e=1e-4):
    from .sdf import gradient as grad
    g = grad(field, np.asarray(p, dtype=F).reshape(-1, 3), e)
    return g / np.maximum(np.linalg.norm(g, axis=1, keepdims=True), 1e-9)


def tube(bm, path, radius, segs=8, around=None):
    """A round tube of ``radius`` (or radius(t)) along ``path`` (Vectors). With ``around`` (centre, axis) the path is
    a closed loop round that axis, wrapped without caps or twist. Returns [(vert, t, angle)] like features.sweep."""
    import math
    from mathutils import Vector
    from .features import round_section, sweep
    rad = radius if callable(radius) else (lambda t: radius)
    if around is None:
        return sweep(bm, path, round_section(rad), segs)
    c, axis = Vector(tuple(around[0])), Vector(tuple(around[1])).normalized()
    rings, out = [], []
    for i, pt in enumerate(path):
        t = (path[(i + 1) % len(path)] - path[i - 1]).normalized()
        n = pt - c
        n = (n - axis * n.dot(axis)).normalized()
        n = (n - t * n.dot(t)).normalized()
        b = t.cross(n)
        u = i / len(path)
        ring = []
        for k in range(segs):
            a = 2 * math.pi * k / segs
            v = bm.verts.new(pt + (n * math.cos(a) + b * math.sin(a)) * rad(u))
            ring.append(v)
            out.append((v, u, a))
        rings.append(ring)
    for i in range(len(rings)):
        r0, r1 = rings[i], rings[(i + 1) % len(rings)]
        for k in range(segs):
            bm.faces.new((r0[k], r0[(k + 1) % segs], r1[(k + 1) % segs], r1[k]))
    return out
